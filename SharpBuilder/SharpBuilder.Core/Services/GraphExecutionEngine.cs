using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

/// <summary>
/// Lightweight graph runner that walks nodes and edges and emits events for UI feedback.
/// Consumers are responsible for reacting to node and transition events (e.g., highlighting).
/// </summary>
public class GraphExecutionEngine
{
	public event EventHandler<NodeModel>? NodeEntered;
	public event EventHandler<(NodeModel Node, NodeExecutionResult Result)>? NodeCompleted;
	public event EventHandler<TransitionModel>? TransitionTaken;
	public event EventHandler? Completed;
	public event EventHandler<Exception>? Faulted;

	/// <summary>
	/// Maximum consecutive Retry results from a single node before it is treated as a failure.
	/// </summary>
	public int MaxRetryAttempts { get; set; } = 5;

	/// <summary>
	/// Minimum pause between retry attempts so a failing executor cannot hot-loop.
	/// </summary>
	public int MinRetryDelayMilliseconds { get; set; } = 250;

	/// <summary>
	/// Receives engine diagnostics (node exceptions, subscriber failures). Console output is the
	/// fallback so headless runs still log; UIs set this to surface diagnostics in a run log.
	/// </summary>
	public Action<string>? DiagnosticSink { get; set; }

	/// <summary>
	/// Consecutive zero-dwell node executions allowed before the engine inserts a 1ms breather.
	/// </summary>
	private const int ZeroDwellStepLimit = 250;

	private readonly NodeCatalogService _catalogService;
	private readonly NodeExecutorRegistry _executorRegistry;

	public GraphExecutionEngine()
		: this(new NodeCatalogService(), new NodeExecutorRegistry())
	{
	}

	public GraphExecutionEngine(NodeCatalogService catalogService, NodeExecutorRegistry executorRegistry)
	{
		_catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
		_executorRegistry = executorRegistry ?? throw new ArgumentNullException(nameof(executorRegistry));
	}

	private int _runState; // 0 = idle, 1 = running (Interlocked so concurrent starts can't both pass the guard)

	public bool IsRunning => Volatile.Read(ref _runState) == 1;

	public async Task RunAsync(
		GraphModel script,
		IReadOnlyDictionary<string, bool> signals,
		bool loop,
		CancellationToken cancellationToken = default)
	{
		if (script == null) throw new ArgumentNullException(nameof(script));
		if (signals == null) throw new ArgumentNullException(nameof(signals));
		if (script.Nodes.Count == 0) return;
		if (Interlocked.CompareExchange(ref _runState, 1, 0) != 0)
			throw new InvalidOperationException("This engine instance is already running a script.");

		try
		{
			var terminalReached = false;

			// The graph is fixed for the whole run (callers hand the engine a clone), so index the
			// nodes once instead of scanning the list on every transition hop.
			var nodesById = new Dictionary<Guid, NodeModel>(script.Nodes.Count);
			foreach (var node in script.Nodes)
				nodesById[node.Id] = node;

			do
			{
				var runtimeSignals = new Dictionary<string, bool>(signals, StringComparer.OrdinalIgnoreCase);
				var startNode = ResolveStartNode(script);
				var current = startNode;

				var retryAttempts = 0;
				NodeModel? lastRetryNode = null;
				var stepsWithoutDelay = 0;

				while (current != null && !cancellationToken.IsCancellationRequested)
				{
					PublishNodeEntered(current);

					var definition = ResolveDefinition(current);
					var parameterMap = BuildParameterMap(current, definition);
					var executor = _executorRegistry.Resolve(definition.Id);

					var result = await ExecuteNodeGatedAsync(
						executor,
						new NodeExecutionContext(current, definition, runtimeSignals, parameterMap),
						cancellationToken);

					if (result.Outputs != null)
					{
						foreach (var kv in result.Outputs)
						{
							runtimeSignals[kv.Key] = kv.Value;
						}
					}

					if (result.Status == NodeExecutionStatus.Retry)
					{
						retryAttempts = ReferenceEquals(lastRetryNode, current) ? retryAttempts + 1 : 1;
						lastRetryNode = current;

						if (retryAttempts < MaxRetryAttempts)
						{
							var delay = Math.Max(current.DwellMilliseconds, MinRetryDelayMilliseconds);
							await Task.Delay(delay, cancellationToken);
							continue;
						}

						// Too many retries: degrade to a failure so fail/fallback edges can route recovery.
						result = new NodeExecutionResult(NodeExecutionStatus.Fail, result.Outputs);
					}

					retryAttempts = 0;
					lastRetryNode = null;

					PublishNodeCompleted(current, result);

					// Dwell is applied uniformly here so individual executors don't have to.
					if (current.DwellMilliseconds > 0)
					{
						await Task.Delay(current.DwellMilliseconds, cancellationToken);
						stepsWithoutDelay = 0;
					}
					else if (++stepsWithoutDelay >= ZeroDwellStepLimit)
					{
						// A cycle of zero-dwell logic nodes would otherwise spin a core flat out.
						await Task.Delay(1, cancellationToken);
						stepsWithoutDelay = 0;
					}

					if (definition.Id == NodeCatalogDefaults.TerminalId)
					{
						// Terminal stops the whole run, including loop mode.
						terminalReached = true;
						break;
					}

					var nextTransition = ResolveTransition(current, runtimeSignals, result.Status);
					if (nextTransition == null)
					{
						break;
					}

					PublishTransitionTaken(nextTransition);

					current = nodesById.TryGetValue(nextTransition.ToNodeId, out var nextNode)
						? nextNode
						: throw new InvalidOperationException(
							$"Transition '{nextTransition.Label}' from '{current.Title}' points to missing node {nextTransition.ToNodeId}.");
				}
			} while (loop && !terminalReached && !cancellationToken.IsCancellationRequested);

			if (!cancellationToken.IsCancellationRequested)
			{
				PublishCompleted();
			}
		}
		catch (OperationCanceledException)
		{
			// Swallow cancellations silently
		}
		catch (Exception ex)
		{
			PublishFaulted(ex);
		}
		finally
		{
			Volatile.Write(ref _runState, 0);
		}
	}

	private static NodeModel ResolveStartNode(GraphModel script)
	{
		if (script.StartNodeId.HasValue)
		{
			var start = script.Nodes.FirstOrDefault(n => n.Id == script.StartNodeId.Value);
			if (start != null)
				return start;
		}

		return script.Nodes.First();
	}

	private static TransitionModel? ResolveTransition(
		NodeModel current,
		IReadOnlyDictionary<string, bool> signals,
		NodeExecutionStatus status)
	{
		if (current.Transitions.Count == 0)
			return null;

		// Edges gated on the execution result win first (in order). A status-gated edge
		// may additionally carry a condition key, which must also match.
		var requiredTrigger = status == NodeExecutionStatus.Fail ? TransitionTrigger.OnFail : TransitionTrigger.OnSuccess;
		foreach (var transition in current.Transitions)
		{
			if (transition.Trigger != requiredTrigger)
				continue;

			if (transition.HasCondition &&
			    (!signals.TryGetValue(transition.ConditionKey, out var gated) || gated != transition.ExpectedValue))
				continue;

			return transition;
		}

		// Then plain conditional edges (in order), skipping edges gated on the other status.
		foreach (var transition in current.Transitions)
		{
			if (transition.Trigger != TransitionTrigger.Any || !transition.HasCondition)
				continue;

			if (signals.TryGetValue(transition.ConditionKey, out var value) && value == transition.ExpectedValue)
				return transition;
		}

		// Then the declared fallback, then the first ungated edge.
		TransitionModel? firstUngated = null;
		foreach (var transition in current.Transitions)
		{
			if (transition.Trigger != TransitionTrigger.Any)
				continue;

			if (transition.IsFallback)
				return transition;

			firstUngated ??= transition;
		}

		return firstUngated;
	}

	private NodeDefinition ResolveDefinition(NodeModel node)
	{
		return _catalogService.GetDefinition(node.DefinitionId) ?? _catalogService.GetDefaultDefinitionForType(node.Type);
	}

	private async Task<NodeExecutionResult> ExecuteNodeGatedAsync(
		INodeExecutor executor,
		NodeExecutionContext context,
		CancellationToken cancellationToken)
	{
		try
		{
			if (executor is IGameApiSelfManaged)
			{
				// The executor manages the lane itself (gating discrete ops, releasing during long
				// waits) so the dashboard can interleave mid-node. Don't hold the lane around it.
				return await executor.ExecuteAsync(context, cancellationToken);
			}

			// Hold the game-API lane for the whole node so its native calls never overlap a
			// concurrent dashboard read. The lane is released between nodes, letting the dashboard
			// interleave; a long node simply makes the next dashboard read wait its turn.
			using (await GameApi.Scheduler.AcquireAsync(cancellationToken).ConfigureAwait(false))
			{
				return await executor.ExecuteAsync(context, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			LogDiagnostic($"Node '{context.Node.Title}' ({context.Definition.Id}) threw {ex.GetType().Name}: {ex.Message}; routing as failure.");
			return NodeExecutionResult.Fail();
		}
	}

	private void PublishNodeEntered(NodeModel node)
	{
		var handlers = NodeEntered;
		if (handlers == null) return;
		foreach (EventHandler<NodeModel> handler in handlers.GetInvocationList())
			PublishEvent(() => handler(this, node), nameof(NodeEntered));
	}

	private void PublishNodeCompleted(NodeModel node, NodeExecutionResult result)
	{
		var handlers = NodeCompleted;
		if (handlers == null) return;
		foreach (EventHandler<(NodeModel Node, NodeExecutionResult Result)> handler in handlers.GetInvocationList())
			PublishEvent(() => handler(this, (node, result)), nameof(NodeCompleted));
	}

	private void PublishTransitionTaken(TransitionModel transition)
	{
		var handlers = TransitionTaken;
		if (handlers == null) return;
		foreach (EventHandler<TransitionModel> handler in handlers.GetInvocationList())
			PublishEvent(() => handler(this, transition), nameof(TransitionTaken));
	}

	private void PublishCompleted()
	{
		var handlers = Completed;
		if (handlers == null) return;
		foreach (EventHandler handler in handlers.GetInvocationList())
			PublishEvent(() => handler(this, EventArgs.Empty), nameof(Completed));
	}

	private void PublishFaulted(Exception exception)
	{
		var handlers = Faulted;
		if (handlers == null) return;
		foreach (EventHandler<Exception> handler in handlers.GetInvocationList())
			PublishEvent(() => handler(this, exception), nameof(Faulted));
	}

	private void PublishEvent(Action raise, string eventName)
	{
		try
		{
			raise();
		}
		catch (Exception ex)
		{
			LogDiagnostic($"{eventName} subscriber threw {ex.GetType().Name}: {ex.Message}");
		}
	}

	private void LogDiagnostic(string message)
	{
		Console.WriteLine($"[SharpBuilder.Engine] {message}");
		try
		{
			DiagnosticSink?.Invoke(message);
		}
		catch
		{
			// A faulty sink must never take the engine down.
		}
	}

	private static IReadOnlyDictionary<string, object?> BuildParameterMap(NodeModel node, NodeDefinition definition)
	{
		var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		if (definition.Parameters == null || definition.Parameters.Count == 0)
			return map;

		foreach (var parameter in definition.Parameters)
		{
			foreach (var value in node.Parameters)
			{
				if (!string.Equals(value.Key, parameter.Key, StringComparison.OrdinalIgnoreCase))
					continue;

				map[parameter.Key] = value.GetTypedValue();
				break;
			}
		}

		return map;
	}
}
