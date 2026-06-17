using System;
using System.Linq;
using System.Windows;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public partial class NodeEditorViewModel
{
	private void AddNode()
	{
		var defaultDefinition = _catalogService.GetDefaultDefinitionForType(Script.Nodes.Count == 0 ? NodeType.Start : NodeType.Action);
		AddNodeFromDefinition(defaultDefinition);
	}

	private void AddNodeFromDefinition(NodeDefinition? definition) => CreateNodeFromDefinition(definition, null);

	/// <summary>Creates a node from a catalog definition at <paramref name="dropPosition"/> (canvas coordinates),
	/// used by drag-and-drop from the catalog. Falls back to cascade placement when null.</summary>
	public void DropNodeFromDefinition(NodeDefinition? definition, Point dropPosition) =>
		CreateNodeFromDefinition(definition, dropPosition);

	private void CreateNodeFromDefinition(NodeDefinition? definition, Point? dropPosition)
	{
		if (definition == null)
			return;

		var before = GraphCloneService.Clone(Script);

		double x, y;
		if (dropPosition is { } drop)
		{
			x = Math.Max(0, drop.X);
			y = Math.Max(0, drop.Y);
		}
		else
		{
			// Cascade placement, wrapping every 12 nodes so big graphs don't march off-canvas.
			var offset = (Script.Nodes.Count % 12) * 30;
			x = 80 + offset;
			y = 80 + offset;
		}

		var node = new NodeModel
		{
			Title = definition.Title,
			Description = definition.ShortDescription,
			DefinitionId = definition.Id,
			DefinitionTitle = definition.Title,
			Type = ResolveNodeType(definition),
			X = x,
			Y = y,
			DwellMilliseconds = 250
		};

		EnsureNodeParameters(node, definition);

		Script.Nodes.Add(node);
		// First node on an empty canvas becomes the start regardless of its definition;
		// the engine can start anywhere, so we honor the user's pick instead of swapping it.
		if (!Script.StartNodeId.HasValue || node.Type == NodeType.Start)
		{
			Script.StartNodeId = node.Id;
		}

		SelectedNode = node;
		Status = $"Added {node.Title}";
		RecordGraphEdit($"Add {node.Title}", before);
	}

	private void RemoveSelectedNode()
	{
		if (SelectedNode == null)
			return;

		var before = GraphCloneService.Clone(Script);
		var targetId = SelectedNode.Id;

		foreach (var node in Script.Nodes.ToList())
		{
			var toRemove = node.Transitions.Where(t => t.FromNodeId == targetId || t.ToNodeId == targetId).ToList();
			foreach (var edge in toRemove)
			{
				node.Transitions.Remove(edge);
			}
		}

		Script.Nodes.Remove(SelectedNode);
		_selectedNodes.Remove(SelectedNode);

		if (Script.StartNodeId == targetId)
		{
			Script.StartNodeId = Script.Nodes.FirstOrDefault()?.Id;
		}

		SelectedNode = Script.Nodes.FirstOrDefault();
		SelectedTransition = null;

		Status = "Removed node";
		RecordGraphEdit("Remove node", before);
	}

	private void AddTransition()
	{
		if (SelectedNode == null || Script.Nodes.Count < 2)
			return;

		var target = Script.Nodes.FirstOrDefault(n =>
			n.Id != SelectedNode.Id && _connectionRules.CanConnect(Script, SelectedNode, n).CanConnect);
		if (target == null)
		{
			Status = "No available transition target";
			return;
		}

		var before = GraphCloneService.Clone(Script);

		var transition = new TransitionModel
		{
			FromNodeId = SelectedNode.Id,
			ToNodeId = target.Id,
			Label = "Next",
			IsFallback = !SelectedNode.Transitions.Any()
		};

		SelectedNode.Transitions.Add(transition);
		SelectedTransition = transition;
		Status = "Added transition";
		RecordGraphEdit("Add transition", before);
	}

	/// <summary>
	/// Creates a transition between two nodes, used by the canvas port drag-to-connect gesture.
	/// Selects the existing edge instead of duplicating when one already targets the same node.
	/// </summary>
	public void ConnectNodes(NodeModel from, NodeModel to)
	{
		var rule = _connectionRules.CanConnect(Script, from, to);
		if (!rule.CanConnect)
		{
			if (from != null)
				SelectedNode = from;
			if (rule.ExistingTransition != null)
				SelectedTransition = rule.ExistingTransition;
			Status = rule.Message;
			return;
		}

		var before = GraphCloneService.Clone(Script);
		var transition = new TransitionModel
		{
			FromNodeId = from.Id,
			ToNodeId = to.Id,
			Label = "Next",
			IsFallback = !from.Transitions.Any()
		};

		from.Transitions.Add(transition);
		SelectedNode = from;
		SelectedTransition = transition;
		Status = $"Connected {from.Title} -> {to.Title}";
		RecordGraphEdit("Connect nodes", before);
	}

	/// <summary>
	/// Points an existing transition at a different target node, used by the canvas
	/// port drag gesture. No-ops when another edge on the same node already targets it.
	/// </summary>
	public void RetargetTransition(TransitionModel transition, NodeModel target)
	{
		var rule = _connectionRules.CanRetarget(Script, transition, target);
		var source = Script.Nodes.FirstOrDefault(n => n.Transitions.Contains(transition));
		if (!rule.CanConnect)
		{
			if (source != null)
				SelectedNode = source;
			if (rule.ExistingTransition != null)
				SelectedTransition = rule.ExistingTransition;
			Status = rule.Message;
			return;
		}

		if (source == null || target == null || transition == null)
			return;

		var before = GraphCloneService.Clone(Script);
		transition.ToNodeId = target.Id;
		SelectedNode = source;
		SelectedTransition = transition;
		Status = $"Retargeted transition to {target.Title}";
		RecordGraphEdit("Retarget transition", before);
	}

	/// <summary>
	/// Moves a transition within its node's list. Order matters: the engine evaluates
	/// transitions top to bottom, so this is the priority control.
	/// </summary>
	private void MoveTransition(TransitionModel? transition, int direction)
	{
		if (transition == null)
			return;

		var source = Script.Nodes.FirstOrDefault(n => n.Transitions.Contains(transition));
		if (source == null)
			return;

		var index = source.Transitions.IndexOf(transition);
		var newIndex = index + direction;
		if (index < 0 || newIndex < 0 || newIndex >= source.Transitions.Count)
			return;

		var before = GraphCloneService.Clone(Script);
		source.Transitions.Move(index, newIndex);
		SelectedTransition = transition;
		Status = $"Moved transition {(direction < 0 ? "up" : "down")}";
		RecordGraphEdit("Move transition", before);
	}

	private void RemoveTransition(TransitionModel? transition)
	{
		if (SelectedNode == null || transition == null)
			return;

		var before = GraphCloneService.Clone(Script);
		SelectedNode.Transitions.Remove(transition);
		if (ReferenceEquals(SelectedTransition, transition))
		{
			SelectedTransition = SelectedNode.Transitions.FirstOrDefault();
		}

		Status = "Removed transition";
		RecordGraphEdit("Remove transition", before);
	}

	private void SetSelectedAsStart()
	{
		if (SelectedNode == null)
			return;

		var before = GraphCloneService.Clone(Script);
		Script.StartNodeId = SelectedNode.Id;
		Status = $"{SelectedNode.Title} marked as start";
		RecordGraphEdit("Set start node", before);
	}

	private void ClearTrail()
	{
		_currentRunNode = null;

		foreach (var node in Script.Nodes)
		{
			node.IsActive = false;
			node.IsCurrent = false;
			node.LastRunStatus = NodeRunStatus.None;
		}

		foreach (var transition in AllTransitions)
		{
			transition.IsActive = false;
		}
	}

	private void DeleteSelection()
	{
		if (SelectedTransition != null && SelectedNode != null)
		{
			RemoveTransition(SelectedTransition);
			return;
		}

		if (_selectedNodes.Count == 0)
			return;

		foreach (var node in _selectedNodes.ToList())
		{
			SelectedNode = node;
			RemoveSelectedNode();
		}
	}

	/// <summary>
	/// Returns the transitions whose drawn edge crosses the given line, without modifying the graph.
	/// Used by the cut gesture to preview what a slash will remove before confirming.
	/// </summary>
	public IReadOnlyList<(NodeModel Node, TransitionModel Transition)> FindTransitionsIntersectingLine(Point start, Point end)
		=> Script.Nodes
			.SelectMany(node => node.Transitions.Select(transition => (Node: node, Transition: transition)))
			.Where(item =>
			{
				var target = Script.Nodes.FirstOrDefault(n => n.Id == item.Transition.ToNodeId);
				if (target == null)
					return false;

				var edgeStart = new Point(
					item.Node.X + Converters.NodeConnectorConverter.OutPortX,
					item.Node.Y + Converters.NodeConnectorConverter.GetOutPortY(item.Node, item.Transition));
				var edgeEnd = new Point(
					target.X + Converters.NodeConnectorConverter.InPortX,
					target.Y + Converters.NodeConnectorConverter.PortY);
				return SegmentsIntersect(start, end, edgeStart, edgeEnd);
			})
			.ToList();

	/// <summary>Removes a known set of transitions (e.g. the result of a confirmed cut gesture) as one undoable edit.</summary>
	public int DeleteTransitions(IReadOnlyList<(NodeModel Node, TransitionModel Transition)> items)
	{
		if (items == null || items.Count == 0)
			return 0;

		var before = GraphCloneService.Clone(Script);
		foreach (var (node, transition) in items)
		{
			node.Transitions.Remove(transition);
		}

		SelectedTransition = null;
		Status = $"Removed {items.Count} transition{(items.Count == 1 ? string.Empty : "s")}";
		RecordGraphEdit("Cut transitions", before);
		return items.Count;
	}

	public int DeleteTransitionsIntersectingLine(Point start, Point end)
	{
		var intersections = FindTransitionsIntersectingLine(start, end);
		if (intersections.Count == 0)
		{
			Status = "No transitions crossed";
			return 0;
		}

		return DeleteTransitions(intersections);
	}

	private static bool SegmentsIntersect(Point a, Point b, Point c, Point d)
	{
		var d1 = Direction(c, d, a);
		var d2 = Direction(c, d, b);
		var d3 = Direction(a, b, c);
		var d4 = Direction(a, b, d);

		if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
		    ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
		{
			return true;
		}

		return d1 == 0 && OnSegment(c, d, a)
		       || d2 == 0 && OnSegment(c, d, b)
		       || d3 == 0 && OnSegment(a, b, c)
		       || d4 == 0 && OnSegment(a, b, d);
	}

	private static double Direction(Point a, Point b, Point c)
		=> ((c.X - a.X) * (b.Y - a.Y)) - ((b.X - a.X) * (c.Y - a.Y));

	private static bool OnSegment(Point a, Point b, Point c)
		=> Math.Min(a.X, b.X) <= c.X && c.X <= Math.Max(a.X, b.X)
		   && Math.Min(a.Y, b.Y) <= c.Y && c.Y <= Math.Max(a.Y, b.Y);
}
