using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

/// <summary>
/// Structural equality over the document-relevant parts of a graph. Used by the undo history to
/// skip no-op edits without serializing whole graphs. Run-time visual state (IsCurrent, IsActive,
/// LastRunStatus, IsSelected) and save-time metadata (UpdatedAt) are deliberately ignored — a
/// selection change or a run must never look like a document edit.
/// </summary>
public static class GraphCompareService
{
	public static bool AreEquivalent(GraphModel? a, GraphModel? b)
	{
		if (ReferenceEquals(a, b))
			return true;
		if (a == null || b == null)
			return false;

		if (a.Id != b.Id ||
		    a.Name != b.Name ||
		    a.Description != b.Description ||
		    a.Author != b.Author ||
		    a.StartNodeId != b.StartNodeId ||
		    a.SchemaVersion != b.SchemaVersion ||
		    a.Nodes.Count != b.Nodes.Count)
		{
			return false;
		}

		for (var i = 0; i < a.Nodes.Count; i++)
		{
			if (!NodesEqual(a.Nodes[i], b.Nodes[i]))
				return false;
		}

		return true;
	}

	private static bool NodesEqual(NodeModel a, NodeModel b)
	{
		if (a.Id != b.Id ||
		    a.Title != b.Title ||
		    a.Description != b.Description ||
		    a.DefinitionId != b.DefinitionId ||
		    a.DefinitionTitle != b.DefinitionTitle ||
		    a.Type != b.Type ||
		    a.X != b.X ||
		    a.Y != b.Y ||
		    a.DashboardWidth != b.DashboardWidth ||
		    a.DashboardHeight != b.DashboardHeight ||
		    a.DwellMilliseconds != b.DwellMilliseconds ||
		    a.ActionText != b.ActionText ||
		    a.Transitions.Count != b.Transitions.Count ||
		    a.Parameters.Count != b.Parameters.Count)
		{
			return false;
		}

		for (var i = 0; i < a.Transitions.Count; i++)
		{
			if (!TransitionsEqual(a.Transitions[i], b.Transitions[i]))
				return false;
		}

		for (var i = 0; i < a.Parameters.Count; i++)
		{
			if (!ParametersEqual(a.Parameters[i], b.Parameters[i]))
				return false;
		}

		return true;
	}

	private static bool TransitionsEqual(TransitionModel a, TransitionModel b) =>
		a.Id == b.Id &&
		a.FromNodeId == b.FromNodeId &&
		a.ToNodeId == b.ToNodeId &&
		a.Label == b.Label &&
		a.ConditionKey == b.ConditionKey &&
		a.ExpectedValue == b.ExpectedValue &&
		a.IsFallback == b.IsFallback &&
		a.Trigger == b.Trigger;

	private static bool ParametersEqual(NodeParameterValue a, NodeParameterValue b) =>
		a.Key == b.Key &&
		a.Type == b.Type &&
		a.AllowMultiple == b.AllowMultiple &&
		a.RawValue == b.RawValue &&
		a.BoolValue == b.BoolValue;
}
