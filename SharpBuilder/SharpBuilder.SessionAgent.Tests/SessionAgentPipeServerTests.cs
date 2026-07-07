using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SharpBuilder.Core.Services;
using SharpBuilder.SessionAgent;
using Xunit;

namespace SharpBuilder.SessionAgent.Tests;

/// <summary>
/// Wire-level tests: a real NamedPipeClientStream against the real server, exercising the
/// ACL'd pipe creation, newline-JSON framing, handshake, and event push end to end.
/// </summary>
public class SessionAgentPipeServerTests
{
	[Fact]
	public async Task HandshakeRequestAndEvents_FlowOverTheRealPipe()
	{
		using var core = new SessionAgentCore();
		using var server = new SessionAgentPipeServer(core);
		server.Start();

		using var client = new NamedPipeClientStream(".", SessionAgentPipeServer.PipeName,
			PipeDirection.InOut, PipeOptions.Asynchronous);
		await client.ConnectAsync(5000);

		using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, leaveOpen: true);
		using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

		// Handshake
		await writer.WriteLineAsync("""{"type":"hello","version":1,"client":"pipe-test"}""");
		var hello = JsonDocument.Parse((await ReadLineAsync(reader))!).RootElement;
		Assert.Equal("hello", hello.GetProperty("type").GetString());
		Assert.Equal("Builder", hello.GetProperty("surface").GetString());

		// Subscribe, then trigger an event via a load — it must arrive as a pushed line.
		await writer.WriteLineAsync("""{"type":"subscribe","topics":["run"]}""");
		var subscribed = JsonDocument.Parse((await ReadLineAsync(reader))!).RootElement;
		Assert.True(subscribed.GetProperty("ok").GetBoolean());

		var catalog = new NodeCatalogService();
		var service = new GraphScriptService(catalog);
		var path = Path.Combine(Path.GetTempPath(), $"agent-pipe-test-{Guid.NewGuid():N}.builder.json");
		await service.SaveAsync(service.CreateNew("Pipe test graph"), path);

		try
		{
			await writer.WriteLineAsync($$"""{"type":"request","id":"1","verb":"load","path":{{JsonSerializer.Serialize(path)}}}""");

			// Two lines arrive in either order: the response and the graph-loaded event.
			var lines = new[] { (await ReadLineAsync(reader))!, (await ReadLineAsync(reader))! }
				.Select(l => JsonDocument.Parse(l).RootElement)
				.ToList();

			var response = lines.Single(l => l.GetProperty("type").GetString() == "response");
			Assert.True(response.GetProperty("ok").GetBoolean());
			Assert.Equal("Pipe test graph", response.GetProperty("graphName").GetString());

			var evt = lines.Single(l => l.GetProperty("type").GetString() == "event");
			Assert.Equal("graph-loaded", evt.GetProperty("kind").GetString());
		}
		finally
		{
			try { File.Delete(path); } catch { }
		}
	}

	[Fact]
	public async Task SecondClient_CanConnectWhileFirstIsStillAttached()
	{
		using var core = new SessionAgentCore();
		using var server = new SessionAgentPipeServer(core);
		server.Start();

		using var first = new NamedPipeClientStream(".", SessionAgentPipeServer.PipeName,
			PipeDirection.InOut, PipeOptions.Asynchronous);
		await first.ConnectAsync(5000);

		using var second = new NamedPipeClientStream(".", SessionAgentPipeServer.PipeName,
			PipeDirection.InOut, PipeOptions.Asynchronous);
		await second.ConnectAsync(5000);

		// Both connections speak independently.
		foreach (var client in new[] { first, second })
		{
			using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, leaveOpen: true);
			using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
			await writer.WriteLineAsync("""{"type":"hello","version":1}""");
			var hello = JsonDocument.Parse((await ReadLineAsync(reader))!).RootElement;
			Assert.Equal("hello", hello.GetProperty("type").GetString());
		}
	}

	private static async Task<string?> ReadLineAsync(StreamReader reader)
		=> await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
}
