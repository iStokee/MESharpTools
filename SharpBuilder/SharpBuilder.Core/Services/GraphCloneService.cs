using System.Collections.ObjectModel;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

public static class GraphCloneService
{
	public static GraphModel Clone(GraphModel graph)
	{
		var clone = new GraphModel
		{
			Id = graph.Id,
			Name = graph.Name,
			Description = graph.Description,
			Author = graph.Author,
			StartNodeId = graph.StartNodeId,
			SchemaVersion = graph.SchemaVersion,
			UpdatedAt = graph.UpdatedAt
		};

		clone.Nodes = new ObservableCollection<NodeModel>(graph.Nodes.Select(Clone));
		return clone;
	}

	private static NodeModel Clone(NodeModel node)
	{
		var clone = new NodeModel
		{
			Id = node.Id,
			Title = node.Title,
			Description = node.Description,
			DefinitionId = node.DefinitionId,
			DefinitionTitle = node.DefinitionTitle,
			Type = node.Type,
			X = node.X,
			Y = node.Y,
			DwellMilliseconds = node.DwellMilliseconds,
			ActionText = node.ActionText,
			IsActive = node.IsActive,
			IsCurrent = node.IsCurrent,
			LastRunStatus = node.LastRunStatus,
			IsSelected = node.IsSelected
		};

		clone.Transitions = new ObservableCollection<TransitionModel>(node.Transitions.Select(Clone));
		clone.Parameters = new ObservableCollection<NodeParameterValue>(node.Parameters.Select(Clone));
		return clone;
	}

	private static TransitionModel Clone(TransitionModel transition) => new()
	{
		Id = transition.Id,
		FromNodeId = transition.FromNodeId,
		ToNodeId = transition.ToNodeId,
		Label = transition.Label,
		ConditionKey = transition.ConditionKey,
		ExpectedValue = transition.ExpectedValue,
		IsFallback = transition.IsFallback,
		IsActive = transition.IsActive,
		Trigger = transition.Trigger
	};

	private static NodeParameterValue Clone(NodeParameterValue parameter) => new()
	{
		Key = parameter.Key,
		Type = parameter.Type,
		AllowMultiple = parameter.AllowMultiple,
		RawValue = parameter.RawValue,
		BoolValue = parameter.BoolValue
	};
}
