using System.Text.Json;
using SharpBuilder.Core.Services;
using SharpBuilder.SessionAgent;
using Xunit;

namespace SharpBuilder.SessionAgent.Tests;

public class SessionAgentCoreTests : IDisposable
{
	private readonly SessionAgentCore _core = new();
	private readonly AgentConnection _connection = new();
	private readonly List<string> _events = new();
	private readonly List<string> _tempFiles = new();

	public SessionAgentCoreTests()
	{
		_core.EventPublished += line => { lock (_events) _events.Add(line); };
	}

	public void Dispose()
	{
		_core.Dispose();
		foreach (var file in _tempFiles)
		{
			try { File.Delete(file); } catch { }
		}
	}

	private static JsonElement Parse(string? line)
	{
		Assert.NotNull(line);
		return JsonDocument.Parse(line!).RootElement.Clone();
	}

	private JsonElement Send(string line) => Parse(_core.HandleLine(_connection, line));

	private void CompleteHello()
	{
		var reply = Send("""{"type":"hello","version":1,"client":"tests"}""");
		Assert.Equal("hello", reply.GetProperty("type").GetString());
	}

	private string SaveTempGraph()
	{
		var catalog = new NodeCatalogService();
		var service = new GraphScriptService(catalog);
		var graph = service.CreateNew("Agent test graph");
		var path = Path.Combine(Path.GetTempPath(), $"agent-test-{Guid.NewGuid():N}.builder.json");
		service.SaveAsync(graph, path).GetAwaiter().GetResult();
		_tempFiles.Add(path);
		return path;
	}

	[Fact]
	public void FirstMessage_MustBeHello()
	{
		var reply = Send("""{"type":"request","id":"1","verb":"status"}""");
		Assert.False(reply.GetProperty("ok").GetBoolean());
		Assert.Equal("hello-required", reply.GetProperty("code").GetString());
	}

	[Fact]
	public void Hello_ReturnsSurfaceVersionAndPid()
	{
		var reply = Send("""{"type":"hello","version":1,"client":"tests"}""");
		Assert.Equal("hello", reply.GetProperty("type").GetString());
		Assert.Equal("Builder", reply.GetProperty("surface").GetString());
		Assert.Equal(1, reply.GetProperty("version").GetInt32());
		Assert.Equal(Environment.ProcessId, reply.GetProperty("pid").GetInt32());
	}

	[Fact]
	public void Hello_WithUnsupportedVersion_Errors()
	{
		var reply = Send("""{"type":"hello","version":0}""");
		Assert.Equal("version-mismatch", reply.GetProperty("code").GetString());
	}

	[Fact]
	public void MalformedJson_YieldsErrorLine()
	{
		var reply = Send("this is not json");
		Assert.Equal("bad-json", reply.GetProperty("code").GetString());
	}

	[Fact]
	public void Status_BeforeLoad_ReportsNoGraph()
	{
		CompleteHello();
		var reply = Send("""{"type":"request","id":"s1","verb":"status"}""");
		Assert.True(reply.GetProperty("ok").GetBoolean());
		Assert.Equal("s1", reply.GetProperty("id").GetString());
		Assert.False(reply.GetProperty("graphLoaded").GetBoolean());
		Assert.False(reply.GetProperty("running").GetBoolean());
	}

	[Fact]
	public void Load_WithoutPath_Errors()
	{
		CompleteHello();
		var reply = Send("""{"type":"request","id":"l1","verb":"load"}""");
		Assert.False(reply.GetProperty("ok").GetBoolean());
		Assert.Equal("bad-request", reply.GetProperty("code").GetString());
	}

	[Fact]
	public void Load_SavedGraph_ReportsNameAndNodeCount()
	{
		CompleteHello();
		var path = SaveTempGraph();
		var reply = Send($$"""{"type":"request","id":"l2","verb":"load","path":{{JsonSerializer.Serialize(path)}}}""");
		Assert.True(reply.GetProperty("ok").GetBoolean());
		Assert.Equal("Agent test graph", reply.GetProperty("graphName").GetString());
		Assert.Equal(1, reply.GetProperty("nodes").GetInt32());

		var status = Send("""{"type":"request","id":"s2","verb":"status"}""");
		Assert.True(status.GetProperty("graphLoaded").GetBoolean());
		Assert.Equal("Agent test graph", status.GetProperty("graphName").GetString());
	}

	[Fact]
	public void Start_WithoutGraph_Errors()
	{
		CompleteHello();
		var reply = Send("""{"type":"request","id":"r1","verb":"start"}""");
		Assert.False(reply.GetProperty("ok").GetBoolean());
		Assert.Equal("no-graph", reply.GetProperty("code").GetString());
	}

	[Fact]
	public void SetSignal_IsReflectedInStatus()
	{
		CompleteHello();
		var reply = Send("""{"type":"request","id":"sig1","verb":"set-signal","key":"invFull","value":true}""");
		Assert.True(reply.GetProperty("ok").GetBoolean());

		var status = Send("""{"type":"request","id":"s3","verb":"status"}""");
		Assert.True(status.GetProperty("signals").GetProperty("invFull").GetBoolean());
	}

	[Fact]
	public void Subscribe_MarksConnectionForRunEvents()
	{
		CompleteHello();
		var reply = Send("""{"type":"subscribe","topics":["run"]}""");
		Assert.True(reply.GetProperty("ok").GetBoolean());
		Assert.True(_connection.SubscribedToRun);
	}

	[Fact]
	public void UnknownMessageType_IsIgnored()
	{
		CompleteHello();
		Assert.Null(_core.HandleLine(_connection, """{"type":"future-thing","x":1}"""));
	}

	[Fact]
	public async Task StartRunStop_EndToEnd_PublishesLifecycleEvents()
	{
		CompleteHello();
		var path = SaveTempGraph();
		Send($$"""{"type":"request","id":"l3","verb":"load","path":{{JsonSerializer.Serialize(path)}}}""");

		// Loop a single-node graph so the run stays alive until we stop it.
		var start = Send("""{"type":"request","id":"r2","verb":"start","loop":true}""");
		Assert.True(start.GetProperty("ok").GetBoolean());

		await WaitUntilAsync(() => _core.IsRunning, "engine did not start");
		await WaitUntilAsync(() => EventKinds().Contains("node-entered"), "no node-entered event");

		var status = Send("""{"type":"request","id":"s4","verb":"status"}""");
		Assert.True(status.GetProperty("running").GetBoolean());
		Assert.True(status.GetProperty("looping").GetBoolean());

		var stop = Send("""{"type":"request","id":"x1","verb":"stop"}""");
		Assert.True(stop.GetProperty("ok").GetBoolean());

		await WaitUntilAsync(() => !_core.IsRunning, "engine did not stop");

		var kinds = EventKinds();
		Assert.Contains("graph-loaded", kinds);
		Assert.Contains("run-started", kinds);
		Assert.Contains("run-stopping", kinds);
	}

	private List<string> EventKinds()
	{
		lock (_events)
		{
			return _events
				.Select(e => JsonDocument.Parse(e).RootElement)
				.Where(e => e.TryGetProperty("kind", out _))
				.Select(e => e.GetProperty("kind").GetString()!)
				.ToList();
		}
	}

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
