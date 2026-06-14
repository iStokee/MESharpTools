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

	// Definitions whose executors always publish a fixed signal key.
	private static readonly Dictionary<string, string> FixedSignalPublishers = new(StringComparer.OrdinalIgnoreCase)
	{
		["conditions.inventoryFull"] = "inventoryFull",
		["conditions.inCombat"] = "inCombat",
		["inventory.contains"] = "inventory.contains",
		["inventory.count"] = "inventory.count.met",
		["equipment.contains"] = "equipment.contains",
		["objects.exists"] = "objects.exists"
	};

	// Definitions whose executors publish the node's "signal" parameter (with a default when empty).
	private static readonly Dictionary<string, string?> SignalParamPublishers = new(StringComparer.OrdinalIgnoreCase)
	{
		["actions.setSignal"] = null,
		["npcs.find"] = "npcs.found",
		["objects.find"] = "objects.found",
		["conditions.locationRadius"] = "insideAnchor",
		["conditions.healthPercent"] = "healthLow",
		["conditions.prayerPercent"] = "prayerLow",
		["conditions.cooldown"] = "cooldownReady",
		["familiar.check"] = "hasFamiliar"
	};

	/// <summary>
	/// Warns when a condition edge or boolean-condition node reads a signal that no node in the
	/// graph ever publishes — such signals silently stay false unless set externally.
	/// </summary>
	private static IEnumerable<ValidationIssue> FindUnpublishedSignalReads(GraphModel script)
	{
		var published = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var node in script.Nodes)
		{
			if (FixedSignalPublishers.TryGetValue(node.DefinitionId, out var fixedKey))
			{
				published.Add(fixedKey);
			}

			if (SignalParamPublishers.TryGetValue(node.DefinitionId, out var defaultKey))
			{
				var signalParam = node.Parameters.FirstOrDefault(p =>
					string.Equals(p.Key, "signal", StringComparison.OrdinalIgnoreCase))?.RawValue;
				var key = string.IsNullOrWhiteSpace(signalParam) ? defaultKey : signalParam.Trim();
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
