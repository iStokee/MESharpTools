using System;
using System.Linq;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

public enum GraphConnectionRuleSeverity
{
	Info,
	Warning,
	Error
}

public sealed record GraphConnectionRuleResult(
	bool CanConnect,
	GraphConnectionRuleSeverity Severity,
	string Message,
	TransitionModel? ExistingTransition = null)
{
	public static GraphConnectionRuleResult Allowed(string message = "Connect nodes")
		=> new(true, GraphConnectionRuleSeverity.Info, message);

	public static GraphConnectionRuleResult Warning(string message)
		=> new(true, GraphConnectionRuleSeverity.Warning, message);

	public static GraphConnectionRuleResult Blocked(string message, TransitionModel? existingTransition = null)
		=> new(false, GraphConnectionRuleSeverity.Error, message, existingTransition);
}

/// <summary>
/// Central graph-editing rules shared by the editor gestures, commands, and validation.
/// </summary>
public sealed class GraphConnectionRules
{
	public GraphConnectionRuleResult CanConnect(GraphModel graph, NodeModel? from, NodeModel? to)
	{
		if (graph == null) throw new ArgumentNullException(nameof(graph));
		if (from == null || to == null)
			return GraphConnectionRuleResult.Blocked("Drop on a node to connect");

		if (from.Id == to.Id)
			return GraphConnectionRuleResult.Blocked("Cannot connect a node to itself");

		if (!graph.Nodes.Any(n => n.Id == from.Id))
			return GraphConnectionRuleResult.Blocked("Source node is not in this graph");

		if (!graph.Nodes.Any(n => n.Id == to.Id))
			return GraphConnectionRuleResult.Blocked("Target node is not in this graph");

		var existing = from.Transitions.FirstOrDefault(t => t.ToNodeId == to.Id);
		if (existing != null)
			return GraphConnectionRuleResult.Blocked($"{from.Title} already links to {to.Title}", existing);

		if (CreatesCycle(graph, from.Id, to.Id))
			return GraphConnectionRuleResult.Warning($"Connect {from.Title} to {to.Title} (creates a cycle)");

		return GraphConnectionRuleResult.Allowed($"Connect {from.Title} to {to.Title}");
	}

	public GraphConnectionRuleResult CanRetarget(GraphModel graph, TransitionModel? transition, NodeModel? target)
	{
		if (graph == null) throw new ArgumentNullException(nameof(graph));
		if (transition == null)
			return GraphConnectionRuleResult.Blocked("No transition selected");

		var source = graph.Nodes.FirstOrDefault(n => n.Transitions.Contains(transition));
		if (source == null)
			return GraphConnectionRuleResult.Blocked("Transition source is not in this graph");

		if (target == null)
			return GraphConnectionRuleResult.Blocked("Drop on a node to retarget");

		if (source.Id == target.Id)
			return GraphConnectionRuleResult.Blocked("Cannot retarget a transition to its source node");

		if (!graph.Nodes.Any(n => n.Id == target.Id))
			return GraphConnectionRuleResult.Blocked("Target node is not in this graph");

		if (transition.ToNodeId == target.Id)
			return GraphConnectionRuleResult.Blocked($"{source.Title} already links to {target.Title}", transition);

		var duplicate = source.Transitions.FirstOrDefault(t => !ReferenceEquals(t, transition) && t.ToNodeId == target.Id);
		if (duplicate != null)
			return GraphConnectionRuleResult.Blocked($"{source.Title} already links to {target.Title}", duplicate);

		if (CreatesCycleIgnoringTransition(graph, source.Id, target.Id, transition))
			return GraphConnectionRuleResult.Warning($"Retarget to {target.Title} (creates a cycle)");

		return GraphConnectionRuleResult.Allowed($"Retarget transition to {target.Title}");
	}

	public bool CreatesCycle(GraphModel graph, Guid fromNodeId, Guid toNodeId)
		=> HasPath(graph, toNodeId, fromNodeId, ignoredTransition: null);

	private static bool CreatesCycleIgnoringTransition(GraphModel graph, Guid fromNodeId, Guid toNodeId, TransitionModel ignoredTransition)
		=> HasPath(graph, toNodeId, fromNodeId, ignoredTransition);

	private static bool HasPath(GraphModel graph, Guid startNodeId, Guid targetNodeId, TransitionModel? ignoredTransition)
	{
		var byId = graph.Nodes.ToDictionary(n => n.Id);
		if (!byId.ContainsKey(startNodeId) || !byId.ContainsKey(targetNodeId))
			return false;

		var visited = new HashSet<Guid>();
		var queue = new Queue<Guid>();
		queue.Enqueue(startNodeId);
		visited.Add(startNodeId);

		while (queue.Count > 0)
		{
			var currentId = queue.Dequeue();
			if (currentId == targetNodeId)
				return true;

			foreach (var transition in byId[currentId].Transitions)
			{
				if (ReferenceEquals(transition, ignoredTransition))
					continue;

				if (visited.Add(transition.ToNodeId) && byId.ContainsKey(transition.ToNodeId))
				{
					queue.Enqueue(transition.ToNodeId);
				}
			}
		}

		return false;
	}
}
