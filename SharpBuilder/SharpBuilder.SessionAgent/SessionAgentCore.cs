using System.Collections.Concurrent;
using System.Text.Json;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;

namespace SharpBuilder.SessionAgent;

/// <summary>
/// Per-connection protocol state. The transport layer owns one per accepted client.
/// </summary>
public sealed class AgentConnection
{
	public bool HelloReceived { get; internal set; }
	public bool SubscribedToRun { get; internal set; }
}

/// <summary>
/// Transport-free protocol + run engine for the SharpBuilder SessionAgent
/// (see docs/IPC_CONVENTIONS.md and SESSION_AGENT_PROTOCOL.md). Consumes one JSON line at a
/// time, returns the response line (or null for messages with no reply), and raises
/// <see cref="EventPublished"/> with serialized event lines for subscribed connections.
/// Keeping this class free of pipes makes the whole protocol unit-testable.
/// </summary>
public sealed class SessionAgentCore : IDisposable
{
	public const int ProtocolVersion = 1;
	public const string Surface = "Builder";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly NodeCatalogService _catalog;
	private readonly GraphScriptService _scriptService;
	private readonly GraphValidator _validator;
	private readonly GraphExecutionEngine _engine;
	private readonly object _runSync = new();
	// ConcurrentDictionary because the engine snapshots this map at each loop-cycle start on
	// its own thread while set-signal writes from connection handlers.
	private readonly ConcurrentDictionary<string, bool> _signals = new(StringComparer.OrdinalIgnoreCase);

	private GraphModel? _graph;
	private string? _graphPath;
	private CancellationTokenSource? _runCts;
	private Task? _runTask;
	private bool _looping;
	private volatile string _currentNode = "";

	/// <summary>Raised with a serialized event line to broadcast to run-subscribed connections.</summary>
	public event Action<string>? EventPublished;

	public SessionAgentCore()
		: this(new NodeCatalogService())
	{
	}

	private SessionAgentCore(NodeCatalogService catalog)
	{
		_catalog = catalog;
		_scriptService = new GraphScriptService(catalog);
		_validator = new GraphValidator(catalog);
		_engine = new GraphExecutionEngine(catalog, new NodeExecutorRegistry());

		_engine.NodeEntered += (_, node) =>
		{
			_currentNode = node.Title;
			PublishEvent(new { type = "event", topic = "run", kind = "node-entered", nodeId = node.Id, title = node.Title });
		};
		_engine.NodeCompleted += (_, e) =>
			PublishEvent(new { type = "event", topic = "run", kind = "node-completed", nodeId = e.Node.Id, title = e.Node.Title, status = e.Result.Status.ToString() });
		_engine.TransitionTaken += (_, t) =>
			PublishEvent(new { type = "event", topic = "run", kind = "transition", transitionId = t.Id, label = t.Label, toNodeId = t.ToNodeId });
		_engine.Completed += (_, _) =>
			PublishEvent(new { type = "event", topic = "run", kind = "run-completed" });
		_engine.Faulted += (_, ex) =>
			PublishEvent(new { type = "event", topic = "run", kind = "run-faulted", message = ex.Message });
	}

	public bool IsRunning => _engine.IsRunning;

	/// <summary>
	/// Handles one inbound line. Returns the response line to write back, or null when the
	/// message produces no direct reply. Never throws: malformed input yields an error line.
	/// </summary>
	public string? HandleLine(AgentConnection connection, string line)
	{
		if (string.IsNullOrWhiteSpace(line))
			return null;

		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(line);
		}
		catch (JsonException)
		{
			return Error(null, "bad-json", "Line is not valid JSON.");
		}

		using (doc)
		{
			var root = doc.RootElement;
			var type = GetString(root, "type");
			var id = GetString(root, "id");

			if (!connection.HelloReceived)
			{
				if (!string.Equals(type, "hello", StringComparison.OrdinalIgnoreCase))
					return Error(id, "hello-required", "First message must be a hello.");

				var clientVersion = root.TryGetProperty("version", out var v) && v.TryGetInt32(out var cv) ? cv : 0;
				if (clientVersion < 1)
					return Error(id, "version-mismatch", $"Server speaks version {ProtocolVersion}; client offered {clientVersion}.");

				connection.HelloReceived = true;
				return Serialize(new
				{
					type = "hello",
					surface = Surface,
					version = Math.Min(ProtocolVersion, clientVersion),
					pid = Environment.ProcessId,
					server = "SharpBuilder.SessionAgent/1.0"
				});
			}

			switch (type?.ToLowerInvariant())
			{
				case "hello":
					return Error(id, "already-greeted", "Hello was already exchanged on this connection.");
				case "subscribe":
					if (root.TryGetProperty("topics", out var topics) && topics.ValueKind == JsonValueKind.Array)
					{
						foreach (var topic in topics.EnumerateArray())
						{
							if (string.Equals(topic.GetString(), "run", StringComparison.OrdinalIgnoreCase))
								connection.SubscribedToRun = true;
						}
					}

					return Serialize(new { type = "response", id, ok = true, subscribed = connection.SubscribedToRun ? new[] { "run" } : Array.Empty<string>() });
				case "request":
					return HandleRequest(id, GetString(root, "verb"), root);
				default:
					// Unknown types are ignored per the conventions (forward compatibility).
					return null;
			}
		}
	}

	private string HandleRequest(string? id, string? verb, JsonElement root)
	{
		try
		{
			switch (verb?.ToLowerInvariant())
			{
				case "status":
					return BuildStatus(id);
				case "load":
					return HandleLoad(id, root);
				case "start":
					return HandleStart(id, root);
				case "stop":
					return HandleStop(id);
				case "set-signal":
					return HandleSetSignal(id, root);
				default:
					return Error(id, "unknown-verb", $"Unknown verb '{verb}'.");
			}
		}
		catch (Exception ex)
		{
			return Error(id, "internal", ex.Message);
		}
	}

	private string BuildStatus(string? id)
	{
		return Serialize(new
		{
			type = "response",
			id,
			ok = true,
			pid = Environment.ProcessId,
			graphLoaded = _graph != null,
			graphName = _graph?.Name,
			graphPath = _graphPath,
			running = IsRunning,
			looping = _looping,
			currentNode = IsRunning ? _currentNode : null,
			signals = new Dictionary<string, bool>(_signals, StringComparer.OrdinalIgnoreCase)
		});
	}

	private string HandleLoad(string? id, JsonElement root)
	{
		var path = GetString(root, "path");
		if (string.IsNullOrWhiteSpace(path))
			return Error(id, "bad-request", "load requires a 'path' to a saved .builder.json graph.");

		var (ok, error, graph) = LoadGraphFromPath(path);
		if (!ok || graph == null)
			return Error(id, error == "busy" ? "busy" : "load-failed", error ?? "Unknown load failure.");

		return Serialize(new { type = "response", id, ok = true, graphName = graph.Name, nodes = graph.Nodes.Count });
	}

	private string HandleStart(string? id, JsonElement root)
	{
		var loop = !root.TryGetProperty("loop", out var loopEl) || loopEl.ValueKind != JsonValueKind.False;
		var result = StartRun(loop);
		if (!result.Ok)
		{
			return result.Code == "validation-failed"
				? Serialize(new { type = "response", id, ok = false, code = result.Code, errors = result.Errors })
				: Error(id, result.Code ?? "internal", result.Errors.FirstOrDefault() ?? "Start failed.");
		}

		return Serialize(new { type = "response", id, ok = true, loop, warnings = result.Warnings });
	}

	private string HandleStop(string? id)
	{
		var wasRunning = StopRun();
		return wasRunning
			? Serialize(new { type = "response", id, ok = true, running = true, stopping = true })
			: Serialize(new { type = "response", id, ok = true, running = false });
	}

	private string HandleSetSignal(string? id, JsonElement root)
	{
		var key = GetString(root, "key");
		if (string.IsNullOrWhiteSpace(key))
			return Error(id, "bad-request", "set-signal requires 'key'.");

		var value = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.True;
		SetSignal(key.Trim(), value);
		return Serialize(new { type = "response", id, ok = true, key = key.Trim(), value });
	}

	// ─── Programmatic operations (shared by the pipe protocol and config autostart) ─────────

	public sealed record StartResult(bool Ok, string? Code, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

	/// <summary>Loads a saved .builder.json and resets the signal map to the graph's keys.</summary>
	public (bool Ok, string? Error, GraphModel? Graph) LoadGraphFromPath(string path)
	{
		if (IsRunning)
			return (false, "busy", null);

		// TryLoadAsync is file IO + JSON only; safe to wait synchronously on a pipe handler.
		var (graph, error) = _scriptService.TryLoadAsync(path).GetAwaiter().GetResult();
		if (graph == null)
			return (false, error ?? "Unknown load failure.", null);

		lock (_runSync)
		{
			_graph = graph;
			_graphPath = path;
		}

		_signals.Clear();
		foreach (var key in GraphSignalService.DiscoverSignalKeys(graph))
			_signals.TryAdd(key, false);

		PublishEvent(new { type = "event", topic = "run", kind = "graph-loaded", graphName = graph.Name, path });
		return (true, null, graph);
	}

	/// <summary>Validates and starts the loaded graph on a background task.</summary>
	public StartResult StartRun(bool loop)
	{
		lock (_runSync)
		{
			if (_graph == null)
				return new StartResult(false, "no-graph", new[] { "Load a graph before starting." }, Array.Empty<string>());
			if (IsRunning)
				return new StartResult(false, "busy", new[] { "A run is already in progress." }, Array.Empty<string>());

			var issues = _validator.Validate(_graph, GameRuntime.IsGameApiAvailable);
			var errors = issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => i.ToString()).ToList();
			if (errors.Count > 0)
				return new StartResult(false, "validation-failed", errors, Array.Empty<string>());

			// The engine mutates a clone, so remote runs can't corrupt the loaded graph;
			// events still carry the original node ids because Clone preserves them.
			var runGraph = GraphCloneService.Clone(_graph);
			_looping = loop;
			_currentNode = "";
			_runCts?.Dispose();
			_runCts = new CancellationTokenSource();
			var token = _runCts.Token;
			_runTask = Task.Run(() => _engine.RunAsync(runGraph, _signals, loop, token), token);

			PublishEvent(new { type = "event", topic = "run", kind = "run-started", loop });
			return new StartResult(true, null, Array.Empty<string>(), issues.Select(i => i.ToString()).ToList());
		}
	}

	/// <summary>Requests cancellation of the active run. Returns whether a run was in progress.</summary>
	public bool StopRun()
	{
		lock (_runSync)
		{
			if (!IsRunning)
				return false;

			_runCts?.Cancel();
		}

		PublishEvent(new { type = "event", topic = "run", kind = "run-stopping" });
		return true;
	}

	/// <summary>
	/// Sets an initial signal value. Loop-mode runs snapshot the signal map at each cycle
	/// start, so this takes effect on the next cycle.
	/// </summary>
	public void SetSignal(string key, bool value) => _signals[key] = value;

	/// <summary>Cancels any active run and waits briefly for the engine to unwind.</summary>
	public void Shutdown()
	{
		lock (_runSync)
		{
			_runCts?.Cancel();
		}

		try
		{
			_runTask?.Wait(TimeSpan.FromSeconds(3));
		}
		catch
		{
			// Cancellation unwind; nothing to surface.
		}
	}

	public void Dispose()
	{
		Shutdown();
		_runCts?.Dispose();
		_runCts = null;
	}

	private void PublishEvent(object payload)
	{
		var handlers = EventPublished;
		if (handlers == null)
			return;

		var line = Serialize(payload);
		foreach (Action<string> handler in handlers.GetInvocationList())
		{
			try
			{
				handler(line);
			}
			catch
			{
				// A broken subscriber must not take down the run or other subscribers.
			}
		}
	}

	private static string Serialize(object payload) => JsonSerializer.Serialize(payload, JsonOptions);

	private static string? GetString(JsonElement root, string property)
		=> root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

	private static string Error(string? id, string code, string message)
		=> Serialize(new { type = id == null ? "error" : "response", id, ok = false, code, message });
}
