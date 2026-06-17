using System;
using System.Collections.Generic;
using System.Linq;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

public sealed record GraphExplanation(
	string ScriptName,
	Guid? StartNodeId,
	IReadOnlyList<NodeExplanation> Nodes,
	IReadOnlyList<SignalExplanation> Signals,
	IReadOnlyList<ValidationIssue> Issues)
{
	public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);
	public bool HasAdvancedNodes => Nodes.Any(n => n.Maturity == NodeMaturity.Advanced);
	public bool RequiresGameApi => Nodes.Any(n => n.RequiresGameApi);
}

public sealed record NodeExplanation(
	Guid NodeId,
	string Title,
	string DefinitionId,
	string DefinitionTitle,
	NodeMaturity Maturity,
	bool IsImplemented,
	bool RequiresGameApi,
	bool IsStart,
	IReadOnlyList<TransitionExplanation> Transitions);

public sealed record TransitionExplanation(
	Guid TransitionId,
	string Label,
	Guid ToNodeId,
	string? ToNodeTitle,
	TransitionTrigger Trigger,
	string? ConditionKey,
	bool ExpectedValue,
	bool IsFallback);

public sealed record SignalExplanation(
	string Key,
	IReadOnlyList<string> Publishers,
	IReadOnlyList<string> Readers);

/// <summary>
/// Produces a non-executing summary of what a graph will depend on and how it can branch.
/// </summary>
public sealed class GraphExplainService
{
	private readonly NodeCatalogService _catalogService;
	private readonly GraphValidator _validator;

	public GraphExplainService(NodeCatalogService catalogService)
	{
		_catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
		_validator = new GraphValidator(catalogService);
	}

	public GraphExplanation Explain(GraphModel script, bool gameApiAvailable = true)
	{
		if (script == null) throw new ArgumentNullException(nameof(script));

		var startNode = ResolveStartNode(script);
		var nodesById = script.Nodes.ToDictionary(n => n.Id);
		var nodes = script.Nodes
			.Select(node => ExplainNode(node, nodesById, startNode?.Id))
			.ToList();

		return new GraphExplanation(
			script.Name,
			startNode?.Id,
			nodes,
			ExplainSignals(script),
			_validator.Validate(script, gameApiAvailable));
	}

	private NodeExplanation ExplainNode(NodeModel node, IReadOnlyDictionary<Guid, NodeModel> nodesById, Guid? startNodeId)
	{
		var definition = _catalogService.GetDefinition(node.DefinitionId) ??
			_catalogService.GetDefaultDefinitionForType(node.Type);

		var transitions = node.Transitions
			.Select(transition => new TransitionExplanation(
				transition.Id,
				transition.Label,
				transition.ToNodeId,
				nodesById.TryGetValue(transition.ToNodeId, out var target) ? target.Title : null,
				transition.Trigger,
				string.IsNullOrWhiteSpace(transition.ConditionKey) ? null : transition.ConditionKey.Trim(),
				transition.ExpectedValue,
				transition.IsFallback))
			.ToList();

		return new NodeExplanation(
			node.Id,
			node.Title,
			definition.Id,
			definition.Title,
			definition.Maturity,
			definition.IsImplemented,
			definition.RequiresGameApi,
			startNodeId == node.Id,
			transitions);
	}

	private static NodeModel? ResolveStartNode(GraphModel script)
	{
		if (script.Nodes.Count == 0)
			return null;

		if (script.StartNodeId.HasValue)
		{
			var configured = script.Nodes.FirstOrDefault(n => n.Id == script.StartNodeId.Value);
			if (configured != null)
				return configured;
		}

		return script.Nodes.First();
	}

	private IReadOnlyList<SignalExplanation> ExplainSignals(GraphModel script)
	{
		var publishers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		var readers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

		foreach (var node in script.Nodes)
		{
			foreach (var signal in PublishedSignals(node))
				AddSignal(publishers, signal, node.Title);

			if (string.Equals(node.DefinitionId, NodeCatalogDefaults.BooleanConditionId, StringComparison.OrdinalIgnoreCase))
			{
				var read = node.Parameters.FirstOrDefault(p =>
					string.Equals(p.Key, "signal", StringComparison.OrdinalIgnoreCase))?.RawValue?.Trim();
				if (!string.IsNullOrWhiteSpace(read))
					AddSignal(readers, read, node.Title);
			}

			foreach (var transition in node.Transitions)
			{
				var key = transition.ConditionKey?.Trim();
				if (!string.IsNullOrWhiteSpace(key))
					AddSignal(readers, key, $"{node.Title} -> {transition.Label}");
			}
		}

		return publishers.Keys
			.Concat(readers.Keys)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
			.Select(key => new SignalExplanation(
				key,
				publishers.TryGetValue(key, out var writerList) ? writerList : Array.Empty<string>(),
				readers.TryGetValue(key, out var readerList) ? readerList : Array.Empty<string>()))
			.ToList();
	}

	private IEnumerable<string> PublishedSignals(NodeModel node)
	{
		var definition = _catalogService.GetDefinition(node.DefinitionId);
		if (definition == null)
			yield break;

		if (!string.IsNullOrWhiteSpace(definition.PublishedSignalKey))
			yield return definition.PublishedSignalKey;

		if (!string.IsNullOrWhiteSpace(definition.PublishedSignalParameterKey))
		{
			var configured = node.Parameters.FirstOrDefault(p =>
				string.Equals(p.Key, definition.PublishedSignalParameterKey, StringComparison.OrdinalIgnoreCase))?.RawValue?.Trim();
			if (!string.IsNullOrWhiteSpace(configured))
				yield return configured;
			else if (!string.IsNullOrWhiteSpace(definition.DefaultPublishedSignalKey))
				yield return definition.DefaultPublishedSignalKey;
		}
	}

	private static void AddSignal(IDictionary<string, List<string>> map, string key, string owner)
	{
		if (!map.TryGetValue(key, out var owners))
		{
			owners = new List<string>();
			map[key] = owners;
		}

		owners.Add(owner);
	}
}
