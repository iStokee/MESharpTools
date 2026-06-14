using System.Collections.ObjectModel;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Tests;

/// <summary>
/// Shared builders so tests describe graphs in a compact, readable form.
/// </summary>
internal static class TestGraphs
{
	public static GraphModel Graph(params NodeModel[] nodes) => new()
	{
		Name = "test graph",
		StartNodeId = nodes.Length > 0 ? nodes[0].Id : null,
		SchemaVersion = 2,
		Nodes = new ObservableCollection<NodeModel>(nodes)
	};

	public static NodeModel Node(string definitionId, NodeType type = NodeType.Action, string? title = null) => new()
	{
		Title = title ?? definitionId,
		DefinitionId = definitionId,
		Type = type,
		DwellMilliseconds = 0
	};

	public static TransitionModel Edge(
		NodeModel from,
		NodeModel to,
		string label = "edge",
		string? conditionKey = null,
		bool expectedValue = true,
		bool isFallback = false,
		TransitionTrigger trigger = TransitionTrigger.Any)
	{
		var transition = new TransitionModel
		{
			FromNodeId = from.Id,
			ToNodeId = to.Id,
			Label = label,
			ConditionKey = conditionKey ?? string.Empty,
			ExpectedValue = expectedValue,
			IsFallback = isFallback,
			Trigger = trigger
		};
		from.Transitions.Add(transition);
		return transition;
	}

	public static NodeParameterValue Param(NodeModel node, string key, string rawValue, NodeParamType type = NodeParamType.String, bool allowMultiple = false)
	{
		var value = new NodeParameterValue { Key = key, Type = type, AllowMultiple = allowMultiple, RawValue = rawValue };
		node.Parameters.Add(value);
		return value;
	}

	public static NodeParameterValue BoolParam(NodeModel node, string key, bool value)
	{
		var parameter = new NodeParameterValue { Key = key, Type = NodeParamType.Bool, BoolValue = value };
		node.Parameters.Add(parameter);
		return parameter;
	}
}
