using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using Xunit;
using static SharpBuilder.Core.Tests.TestGraphs;

namespace SharpBuilder.Core.Tests;

public class GraphExecutionEngineTests
{
	private static readonly IReadOnlyDictionary<string, bool> NoSignals = new Dictionary<string, bool>();

	/// <summary>
	/// Test double registered over the generic-action id so every Action node in a test
	/// graph runs scripted logic instead of the dwell-only placeholder.
	/// </summary>
	private sealed class ScriptedExecutor : INodeExecutor
	{
		private readonly Func<NodeExecutionContext, Task<NodeExecutionResult>> _body;

		public ScriptedExecutor(Func<NodeExecutionContext, NodeExecutionResult> body)
			: this(ctx => Task.FromResult(body(ctx)))
		{
		}

		public ScriptedExecutor(Func<NodeExecutionContext, Task<NodeExecutionResult>> body) => _body = body;

		public List<string> Executions { get; } = new();

		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			Executions.Add(context.Node.Title);
			return _body(context);
		}
	}

	private static (GraphExecutionEngine Engine, NodeExecutorRegistry Registry) CreateEngine()
	{
		var registry = new NodeExecutorRegistry();
		var engine = new GraphExecutionEngine(new NodeCatalogService(), registry);
		return (engine, registry);
	}

	private static List<string> TrackVisits(GraphExecutionEngine engine)
	{
		var visits = new List<string>();
		engine.NodeEntered += (_, node) => visits.Add(node.Title);
		return visits;
	}

	[Fact]
	public async Task SuccessResult_TakesOnSuccessEdge()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Success()));

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");
		var onFail = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "FailEnd");
		var onSuccess = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "SuccessEnd");
		Edge(a, onFail, trigger: TransitionTrigger.OnFail);
		Edge(a, onSuccess, trigger: TransitionTrigger.OnSuccess);

		var visits = TrackVisits(engine);
		await engine.RunAsync(Graph(a, onFail, onSuccess), NoSignals, loop: false);

		Assert.Equal(new[] { "A", "SuccessEnd" }, visits);
	}

	[Fact]
	public async Task FailResult_TakesOnFailEdge()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Fail()));

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");
		var onSuccess = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "SuccessEnd");
		var onFail = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "FailEnd");
		Edge(a, onSuccess, trigger: TransitionTrigger.OnSuccess);
		Edge(a, onFail, trigger: TransitionTrigger.OnFail);

		var visits = TrackVisits(engine);
		await engine.RunAsync(Graph(a, onSuccess, onFail), NoSignals, loop: false);

		Assert.Equal(new[] { "A", "FailEnd" }, visits);
	}

	[Fact]
	public async Task StatusGatedEdgeWithConditionKey_RequiresSignalToMatchToo()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Success()));

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");
		var gated = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Gated");
		var fallback = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Fallback");
		// OnSuccess edge also gated on an unset signal: must be skipped.
		Edge(a, gated, trigger: TransitionTrigger.OnSuccess, conditionKey: "neverSet", expectedValue: true);
		Edge(a, fallback, isFallback: true);

		var visits = TrackVisits(engine);
		await engine.RunAsync(Graph(a, gated, fallback), NoSignals, loop: false);

		Assert.Equal(new[] { "A", "Fallback" }, visits);
	}

	[Fact]
	public async Task PublishedOutputs_DriveConditionKeyedEdgesOnLaterNodes()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Success()));

		// Real SetSignalExecutor publishes "flag" = true into the runtime signals.
		var setter = Node("actions.setSignal", title: "Setter");
		Param(setter, "signal", "flag");
		BoolParam(setter, "value", true);

		var reader = Node(NodeCatalogDefaults.GenericActionId, title: "Reader");
		var matched = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Matched");
		var fallback = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Fallback");

		Edge(setter, reader);
		Edge(reader, matched, conditionKey: "flag", expectedValue: true);
		Edge(reader, fallback, isFallback: true);

		var visits = TrackVisits(engine);
		await engine.RunAsync(Graph(setter, reader, matched, fallback), NoSignals, loop: false);

		Assert.Equal(new[] { "Setter", "Reader", "Matched" }, visits);
	}

	[Fact]
	public async Task CooldownCondition_PublishesConfiguredSignal()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Success()));

		var cooldown = Node("conditions.cooldown", NodeType.Condition, "Potion cooldown");
		Param(cooldown, "intervalMs", "60000", NodeParamType.Number);
		Param(cooldown, "signal", "potionReady");
		BoolParam(cooldown, "startReady", true);
		BoolParam(cooldown, "consumeOnReady", true);
		BoolParam(cooldown, "expected", true);

		var reader = Node(NodeCatalogDefaults.GenericActionId, title: "Reader");
		var matched = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Matched");
		var fallback = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Fallback");

		Edge(cooldown, reader);
		Edge(reader, matched, conditionKey: "potionReady", expectedValue: true);
		Edge(reader, fallback, isFallback: true);

		var visits = TrackVisits(engine);
		await engine.RunAsync(Graph(cooldown, reader, matched, fallback), NoSignals, loop: false);

		Assert.Equal(new[] { "Potion cooldown", "Reader", "Matched" }, visits);
	}

	[Fact]
	public async Task ExternallySeededSignals_AreVisibleToConditionEdges()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Success()));

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");
		var matched = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Matched");
		var fallback = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Fallback");
		Edge(a, matched, conditionKey: "external", expectedValue: true);
		Edge(a, fallback, isFallback: true);

		var visits = TrackVisits(engine);
		await engine.RunAsync(
			Graph(a, matched, fallback),
			new Dictionary<string, bool> { ["external"] = true },
			loop: false);

		Assert.Equal(new[] { "A", "Matched" }, visits);
	}

	[Fact]
	public async Task FallbackEdge_BeatsEarlierPlainUngatedEdge()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Success()));

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");
		var plain = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Plain");
		var fallback = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Fallback");
		Edge(a, plain);
		Edge(a, fallback, isFallback: true);

		var visits = TrackVisits(engine);
		await engine.RunAsync(Graph(a, plain, fallback), NoSignals, loop: false);

		Assert.Equal(new[] { "A", "Fallback" }, visits);
	}

	[Fact]
	public async Task NoFallback_FirstUngatedEdgeIsUsed()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Success()));

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");
		var first = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "First");
		var second = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Second");
		Edge(a, first);
		Edge(a, second);

		var visits = TrackVisits(engine);
		await engine.RunAsync(Graph(a, first, second), NoSignals, loop: false);

		Assert.Equal(new[] { "A", "First" }, visits);
	}

	[Fact]
	public async Task TerminalNode_EndsRunEvenInLoopMode()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Success()));

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");
		var end = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "End");
		Edge(a, end);

		var completed = 0;
		engine.Completed += (_, _) => completed++;
		var visits = TrackVisits(engine);

		await engine.RunAsync(Graph(a, end), NoSignals, loop: true);

		Assert.Equal(new[] { "A", "End" }, visits);
		Assert.Equal(1, completed);
		Assert.False(engine.IsRunning);
	}

	[Fact]
	public async Task DeadEnd_EndsPassWithoutLoop()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Success()));

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");

		var completed = 0;
		engine.Completed += (_, _) => completed++;
		var visits = TrackVisits(engine);

		await engine.RunAsync(Graph(a), NoSignals, loop: false);

		Assert.Equal(new[] { "A" }, visits);
		Assert.Equal(1, completed);
	}

	[Fact]
	public async Task Cancellation_StopsLoopAndSuppressesCompletedEvent()
	{
		var (engine, registry) = CreateEngine();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(_ => NodeExecutionResult.Success()));

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");
		Edge(a, a, label: "self");

		using var cts = new CancellationTokenSource();
		var completed = 0;
		var visits = 0;
		engine.Completed += (_, _) => completed++;
		engine.NodeEntered += (_, _) =>
		{
			if (++visits >= 5)
				cts.Cancel();
		};

		await engine.RunAsync(Graph(a), NoSignals, loop: true, cts.Token);

		Assert.True(visits >= 5);
		Assert.Equal(0, completed);
		Assert.False(engine.IsRunning);
	}

	[Fact]
	public async Task Retry_DegradesToFailAfterMaxAttempts_AndRoutesViaOnFail()
	{
		var (engine, registry) = CreateEngine();
		var executor = new ScriptedExecutor(_ => NodeExecutionResult.Retry());
		registry.Register(NodeCatalogDefaults.GenericActionId, executor);
		engine.MaxRetryAttempts = 3;
		engine.MinRetryDelayMilliseconds = 1;

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");
		var failEnd = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "FailEnd");
		Edge(a, failEnd, trigger: TransitionTrigger.OnFail);

		var visits = TrackVisits(engine);
		await engine.RunAsync(Graph(a, failEnd), NoSignals, loop: false);

		Assert.Equal(engine.MaxRetryAttempts, executor.Executions.Count);
		Assert.Equal(new[] { "A", "A", "A", "FailEnd" }, visits);
	}

	[Fact]
	public async Task SecondConcurrentRun_Throws()
	{
		var (engine, registry) = CreateEngine();
		var gate = new TaskCompletionSource();
		registry.Register(NodeCatalogDefaults.GenericActionId, new ScriptedExecutor(async _ =>
		{
			await gate.Task;
			return NodeExecutionResult.Success();
		}));

		var a = Node(NodeCatalogDefaults.GenericActionId, title: "A");
		var script = Graph(a);

		var firstRun = engine.RunAsync(script, NoSignals, loop: false);

		await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunAsync(script, NoSignals, loop: false));
		Assert.True(engine.IsRunning);

		gate.SetResult();
		await firstRun;
		Assert.False(engine.IsRunning);
	}
}
