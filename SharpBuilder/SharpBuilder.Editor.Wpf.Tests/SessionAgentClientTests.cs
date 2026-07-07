using System.IO;
using System.Text.Json;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using SharpBuilder.Editor.Wpf.Services;
using SharpBuilder.SessionAgent;
using Xunit;

namespace SharpBuilder.Editor.Wpf.Tests;

/// <summary>
/// Integration tests: the Studio-side client against the real agent pipe server,
/// covering the exact attach flow the workspace session rail performs.
/// </summary>
public class SessionAgentClientTests
{
	[Fact]
	public async Task Client_Handshakes_Requests_AndReceivesEvents()
	{
		using var core = new SessionAgentCore();
		using var server = new SessionAgentPipeServer(core);
		server.Start();

		using var client = await SessionAgentClient.ConnectAsync(SessionAgentPipeServer.PipeName);

		// Status round-trip with id correlation.
		var status = await client.RequestAsync("status");
		Assert.True(status.GetProperty("ok").GetBoolean());
		Assert.False(status.GetProperty("graphLoaded").GetBoolean());

		// Subscribe, then load — the graph-loaded event must arrive as a push.
		var events = new List<JsonElement>();
		var loadedEvent = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		client.EventReceived += evt =>
		{
			lock (events) events.Add(evt);
			if (evt.GetProperty("kind").GetString() == "graph-loaded")
				loadedEvent.TrySetResult(evt);
		};
		await client.SubscribeRunAsync();

		var catalog = new NodeCatalogService();
		var service = new GraphScriptService(catalog);
		var path = Path.Combine(Path.GetTempPath(), $"client-test-{Guid.NewGuid():N}.builder.json");
		await service.SaveAsync(service.CreateNew("Client test graph"), path);

		try
		{
			var load = await client.RequestAsync("load", new Dictionary<string, object?> { ["path"] = path });
			Assert.True(load.GetProperty("ok").GetBoolean());

			var evt = await loadedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Equal("Client test graph", evt.GetProperty("graphName").GetString());

			// Status now reports the loaded graph + its path (what Observe uses to mirror).
			var after = await client.RequestAsync("status");
			Assert.True(after.GetProperty("graphLoaded").GetBoolean());
			Assert.Equal(path, after.GetProperty("graphPath").GetString());
		}
		finally
		{
			try { File.Delete(path); } catch { }
		}
	}

	[Fact]
	public async Task Client_StartAndStop_DriveTheRemoteRun()
	{
		using var core = new SessionAgentCore();
		using var server = new SessionAgentPipeServer(core);
		server.Start();

		var catalog = new NodeCatalogService();
		var service = new GraphScriptService(catalog);
		var path = Path.Combine(Path.GetTempPath(), $"client-run-{Guid.NewGuid():N}.builder.json");
		await service.SaveAsync(service.CreateNew("Client run graph"), path);

		try
		{
			using var client = await SessionAgentClient.ConnectAsync(SessionAgentPipeServer.PipeName);
			await client.RequestAsync("load", new Dictionary<string, object?> { ["path"] = path });

			var start = await client.RequestAsync("start", new Dictionary<string, object?> { ["loop"] = true });
			Assert.True(start.GetProperty("ok").GetBoolean());
			await WaitUntilAsync(() => core.IsRunning, "remote start did not begin a run");

			var stop = await client.RequestAsync("stop");
			Assert.True(stop.GetProperty("ok").GetBoolean());
			await WaitUntilAsync(() => !core.IsRunning, "remote stop did not end the run");
		}
		finally
		{
			try { File.Delete(path); } catch { }
		}
	}

	[Fact]
	public void RemoteRunObserver_MapsEventsOntoTheLocalGraph()
	{
		var catalog = new NodeCatalogService();
		var service = new GraphScriptService(catalog);
		var graph = service.CreatePowerFishingTemplate();
		var first = graph.Nodes[0];
		var second = graph.Nodes[1];
		var edge = first.Transitions.First();

		var observer = new RemoteRunObserver(graph);

		observer.Handle(Parse("""{"type":"event","topic":"run","kind":"run-started","loop":true}"""));
		observer.Handle(Parse($$"""{"type":"event","topic":"run","kind":"node-entered","nodeId":"{{first.Id}}","title":"x"}"""));
		Assert.True(first.IsCurrent);
		Assert.True(first.IsActive);

		observer.Handle(Parse($$"""{"type":"event","topic":"run","kind":"node-completed","nodeId":"{{first.Id}}","title":"x","status":"Success"}"""));
		Assert.Equal(NodeRunStatus.Success, first.LastRunStatus);

		observer.Handle(Parse($$"""{"type":"event","topic":"run","kind":"transition","transitionId":"{{edge.Id}}","label":"e","toNodeId":"{{edge.ToNodeId}}"}"""));
		Assert.True(edge.IsActive);

		observer.Handle(Parse($$"""{"type":"event","topic":"run","kind":"node-entered","nodeId":"{{second.Id}}","title":"y"}"""));
		Assert.False(first.IsCurrent);
		Assert.True(second.IsCurrent);

		observer.Handle(Parse("""{"type":"event","topic":"run","kind":"run-completed"}"""));
		Assert.False(second.IsCurrent);
		Assert.Equal("Completed", observer.LastRunState);
	}

	private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

	private static async Task WaitUntilAsync(Func<bool> condition, string failure, int timeoutMs = 5000)
	{
		var deadline = Environment.TickCount64 + timeoutMs;
		while (Environment.TickCount64 < deadline)
		{
			if (condition())
				return;
			await Task.Delay(25);
		}

		Assert.Fail(failure);
	}
}
