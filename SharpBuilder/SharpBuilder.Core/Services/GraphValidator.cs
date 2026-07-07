using System;
using System.Collections.Generic;
using System.Linq;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

public enum ValidationSeverity
{
	Warning,
	Error
}

public record ValidationIssue(ValidationSeverity Severity, string Message, Guid? NodeId = null)
{
	public override string ToString() => $"[{Severity}] {Message}";
}

/// <summary>
/// Static checks run before executing a script so users get actionable feedback
/// instead of silent no-ops or runtime faults.
/// </summary>
public class GraphValidator
{
	private readonly NodeCatalogService _catalogService;
	private readonly GraphConnectionRules _connectionRules = new();

	public GraphValidator(NodeCatalogService catalogService)
	{
		_catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
	}

	public IReadOnlyList<ValidationIssue> Validate(GraphModel script, bool gameApiAvailable = true)
	{
		if (script == null) throw new ArgumentNullException(nameof(script));

		var issues = new List<ValidationIssue>();

		if (script.Nodes.Count == 0)
		{
			issues.Add(new ValidationIssue(ValidationSeverity.Error, "Script has no nodes."));
			return issues;
		}

		var nodeIds = new HashSet<Guid>(script.Nodes.Select(n => n.Id));

		if (script.StartNodeId.HasValue && !nodeIds.Contains(script.StartNodeId.Value))
		{
			issues.Add(new ValidationIssue(ValidationSeverity.Error, "Start node id does not match any node."));
		}

		foreach (var node in script.Nodes)
		{
			var definition = _catalogService.GetDefinition(node.DefinitionId);
			if (definition == null)
			{
				issues.Add(new ValidationIssue(
					ValidationSeverity.Warning,
					$"Node '{node.Title}' uses unknown definition '{node.DefinitionId}'; it will run as a no-op placeholder.",
					node.Id));
			}
			else
			{
				if (string.Equals(definition.Id, NodeCatalogDefaults.GenericActionId, StringComparison.OrdinalIgnoreCase)
					&& script.Nodes.Count > 1)
				{
					issues.Add(new ValidationIssue(
						ValidationSeverity.Warning,
						$"Node '{node.Title}' is a placeholder note — it performs no game action (only its dwell delay applies). Replace it with a real node.",
						node.Id));
				}

				if (!definition.IsImplemented)
				{
					issues.Add(new ValidationIssue(
						ValidationSeverity.Warning,
						$"Node '{node.Title}' ({definition.Title}) is not implemented yet and will always fail.",
						node.Id));
				}

				if (definition.Maturity == NodeMaturity.Advanced)
				{
					issues.Add(new ValidationIssue(
						ValidationSeverity.Warning,
						$"Node '{node.Title}' ({definition.Title}) is an advanced native-capture node. Prefer stable typed nodes unless this graph needs captured opcode/offset tuning.",
						node.Id));
				}

				if (!gameApiAvailable && definition.RequiresGameApi)
				{
					issues.Add(new ValidationIssue(
						ValidationSeverity.Error,
						$"Node '{node.Title}' ({definition.Title}) needs the in-game API, which is not available in this process. Load the script host inside the game client to run it.",
						node.Id));
				}

				foreach (var parameter in definition.Parameters.Where(p => p.IsRequired))
				{
					var value = node.Parameters.FirstOrDefault(p =>
						string.Equals(p.Key, parameter.Key, StringComparison.OrdinalIgnoreCase));

					var isEmpty = parameter.Type switch
					{
						NodeParamType.Bool => false, // bools always carry a value
						_ => string.IsNullOrWhiteSpace(value?.RawValue)
					};

					if (value == null || isEmpty)
					{
						issues.Add(new ValidationIssue(
							ValidationSeverity.Error,
							$"Node '{node.Title}': required parameter '{parameter.Label}' is empty.",
							node.Id));
					}
				}

				foreach (var parameter in node.Parameters)
				{
					var drift = OffsetNameResolver.DetectDrift(parameter.RawValue);
					if (drift != null)
					{
						issues.Add(new ValidationIssue(
							ValidationSeverity.Warning,
							$"Node '{node.Title}': offset '{drift.Value.Name}' was saved as {drift.Value.SavedValue} but is now {drift.Value.CurrentValue} (game update); the run uses the current value. Re-save the script to clear this warning.",
							node.Id));
					}
				}
			}

			foreach (var transition in node.Transitions)
			{
				if (!nodeIds.Contains(transition.ToNodeId))
				{
					issues.Add(new ValidationIssue(
						ValidationSeverity.Error,
						$"Node '{node.Title}' has a transition pointing at a missing node.",
						node.Id));
				}
			}

			var isTerminal = string.Equals(node.DefinitionId, NodeCatalogDefaults.TerminalId, StringComparison.OrdinalIgnoreCase);
			var isCanvasOnlyUi = string.Equals(node.DefinitionId, NodeCatalogDefaults.ScriptDashboardId, StringComparison.OrdinalIgnoreCase);
			if (isTerminal && node.Transitions.Count > 0)
			{
				issues.Add(new ValidationIssue(
					ValidationSeverity.Warning,
					$"Terminal node '{node.Title}' has outgoing transitions; they will never be taken.",
					node.Id));
			}

			if (!isTerminal && !isCanvasOnlyUi && node.Transitions.Count == 0 && script.Nodes.Count > 1)
			{
				issues.Add(new ValidationIssue(
					ValidationSeverity.Warning,
					$"Node '{node.Title}' has no outgoing transitions; the pass ends there (with Loop on, the run restarts from Start).",
					node.Id));
			}

			if (node.Transitions.Count(t => t.IsFallback) > 1)
			{
				issues.Add(new ValidationIssue(
					ValidationSeverity.Warning,
					$"Node '{node.Title}' has multiple fallback transitions; only the first (top-most) is used.",
					node.Id));
			}

			foreach (var duplicate in node.Transitions
				.GroupBy(t => t.ToNodeId)
				.Where(g => g.Count() > 1))
			{
				var target = script.Nodes.FirstOrDefault(n => n.Id == duplicate.Key)?.Title ?? duplicate.Key.ToString();
				issues.Add(new ValidationIssue(
					ValidationSeverity.Warning,
					$"Node '{node.Title}' has duplicate transitions to '{target}'; only the first matching edge can be useful.",
					node.Id));
			}

			foreach (var transition in node.Transitions)
			{
				var target = script.Nodes.FirstOrDefault(n => n.Id == transition.ToNodeId);
				if (target == null)
					continue;

				var rule = _connectionRules.CanRetarget(script, transition, target);
				if (!rule.CanConnect && !ReferenceEquals(rule.ExistingTransition, transition))
				{
					issues.Add(new ValidationIssue(
						ValidationSeverity.Warning,
						$"Transition '{transition.Label}' on node '{node.Title}' violates connection rules: {rule.Message}.",
						node.Id));
				}
			}
		}

		foreach (var issue in FindUnpublishedSignalReads(script))
		{
			issues.Add(issue);
		}

		foreach (var node in FindUnreachableNodes(script))
		{
			issues.Add(new ValidationIssue(
				ValidationSeverity.Warning,
				$"Node '{node.Title}' is unreachable from the start node.",
				node.Id));
		}

		return issues;
	}

	/// <summary>
	/// Warns when a condition edge or boolean-condition node reads a signal that no node in the
	/// graph ever publishes — such signals silently stay false unless set externally.
	/// </summary>
	private IEnumerable<ValidationIssue> FindUnpublishedSignalReads(GraphModel script)
	{
		var published = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var node in script.Nodes)
		{
			var definition = _catalogService.GetDefinition(node.DefinitionId);
			if (definition == null)
				continue;

			if (!string.IsNullOrWhiteSpace(definition.PublishedSignalKey))
			{
				published.Add(definition.PublishedSignalKey);
			}

			if (!string.IsNullOrWhiteSpace(definition.PublishedSignalParameterKey))
			{
				var signalParam = node.Parameters.FirstOrDefault(p =>
					string.Equals(p.Key, definition.PublishedSignalParameterKey, StringComparison.OrdinalIgnoreCase))?.RawValue;
				var key = string.IsNullOrWhiteSpace(signalParam) ? definition.DefaultPublishedSignalKey : signalParam.Trim();
				if (!string.IsNullOrWhiteSpace(key))
				{
					published.Add(key);
				}
			}
		}

		var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var node in script.Nodes)
		{
			if (string.Equals(node.DefinitionId, NodeCatalogDefaults.BooleanConditionId, StringComparison.OrdinalIgnoreCase))
			{
				var read = node.Parameters.FirstOrDefault(p =>
					string.Equals(p.Key, "signal", StringComparison.OrdinalIgnoreCase))?.RawValue?.Trim();
				if (!string.IsNullOrWhiteSpace(read) && !published.Contains(read) && reported.Add(read))
				{
					yield return new ValidationIssue(
						ValidationSeverity.Warning,
						$"Node '{node.Title}' reads signal '{read}', but no node in this graph publishes it — it stays false unless set externally (runtime signals panel or runner config).",
						node.Id);
				}
			}

			foreach (var transition in node.Transitions)
			{
				var key = transition.ConditionKey?.Trim();
				if (!string.IsNullOrWhiteSpace(key) && !published.Contains(key) && reported.Add(key))
				{
					yield return new ValidationIssue(
						ValidationSeverity.Warning,
						$"Transition '{transition.Label}' on node '{node.Title}' is gated on signal '{key}', but no node in this graph publishes it — the edge can never match unless the signal is set externally.",
						node.Id);
				}
			}
		}
	}

	private static IEnumerable<NodeModel> FindUnreachableNodes(GraphModel script)
	{
		var start = script.StartNodeId.HasValue
			? script.Nodes.FirstOrDefault(n => n.Id == script.StartNodeId.Value) ?? script.Nodes.First()
			: script.Nodes.First();

		var byId = script.Nodes.ToDictionary(n => n.Id);
		var visited = new HashSet<Guid>();
		var queue = new Queue<NodeModel>();
		queue.Enqueue(start);
		visited.Add(start.Id);

		while (queue.Count > 0)
		{
			var node = queue.Dequeue();
			foreach (var transition in node.Transitions)
			{
				if (visited.Add(transition.ToNodeId) && byId.TryGetValue(transition.ToNodeId, out var next))
				{
					queue.Enqueue(next);
				}
			}
		}

		return script.Nodes.Where(n =>
			!visited.Contains(n.Id) &&
			!string.Equals(n.DefinitionId, NodeCatalogDefaults.ScriptDashboardId, StringComparison.OrdinalIgnoreCase));
	}
}
