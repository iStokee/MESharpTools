using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public partial class NodeEditorViewModel
{
	public void SelectNode(NodeModel node, bool toggle)
	{
		if (node == null) return;

		CommitPendingPropertyEdit();

		// Drop any transition selection that doesn't belong to the new selection,
		// otherwise Delete removes a stale edge instead of the selected node.
		if (_selectedTransition != null && !node.Transitions.Contains(_selectedTransition))
		{
			SelectedTransition = null;
		}

		if (toggle)
		{
			if (_selectedNodes.Contains(node))
			{
				node.IsSelected = false;
				_selectedNodes.Remove(node);
			}
			else
			{
				node.IsSelected = true;
				_selectedNodes.Add(node);
			}

			_selectedNode = _selectedNodes.LastOrDefault();
		}
		else
		{
			ClearSelectedNodes();
			node.IsSelected = true;
			_selectedNodes.Add(node);
			_selectedNode = node;
		}

		RefreshSelectedNodeDefinition();
		NotifySelectionChanged();
		IsNodeInfoOpen = false;
	}

	/// <summary>
	/// Clears all selected nodes.
	/// </summary>
	public void ClearSelection()
	{
		CommitPendingPropertyEdit();

		ClearSelectedNodes();
		_selectedNode = null;
		SelectedNodeDefinition = null;
		SelectedTransition = null;

		NotifySelectionChanged();
		IsNodeInfoOpen = false;
	}

	/// <summary>
	/// Selects all nodes within the specified bounds (for box/marquee selection).
	/// </summary>
	/// <param name="bounds">The selection rectangle in canvas coordinates.</param>
	public void SelectNodesInBounds(Rect bounds)
	{
		foreach (var node in Script.Nodes)
		{
			var nodeRect = Converters.NodeConnectorConverter.GetNodeBounds(node);
			if (bounds.IntersectsWith(nodeRect))
			{
				if (!_selectedNodes.Contains(node))
				{
					node.IsSelected = true;
					_selectedNodes.Add(node);
				}
			}
		}

		_selectedNode = _selectedNodes.LastOrDefault();
		RefreshSelectedNodeDefinition();
		NotifySelectionChanged();
	}

	private void UpdatePrimarySelection(NodeModel? node)
	{
		ClearSelectedNodes();
		_selectedNode = node;

		if (_selectedNode != null)
		{
			_selectedNode.IsSelected = true;
			_selectedNodes.Add(_selectedNode);
		}
	}

	/// <summary>Selection state captured by id so it survives a whole-graph swap (undo/redo).</summary>
	private readonly record struct SelectionSnapshot(
		IReadOnlyList<Guid> NodeIds,
		Guid? PrimaryNodeId,
		Guid? TransitionId);

	private SelectionSnapshot CaptureSelection() => new(
		_selectedNodes.Select(n => n.Id).ToList(),
		_selectedNode?.Id,
		_selectedTransition?.Id);

	/// <summary>Re-selects the captured nodes/transition by id on the current (swapped-in) graph.</summary>
	private void RestoreSelection(SelectionSnapshot snapshot)
	{
		ClearSelectedNodes();

		foreach (var id in snapshot.NodeIds)
		{
			var node = Script.Nodes.FirstOrDefault(n => n.Id == id);
			if (node == null)
				continue;

			node.IsSelected = true;
			_selectedNodes.Add(node);
		}

		_selectedNode = snapshot.PrimaryNodeId is { } primaryId
			? _selectedNodes.FirstOrDefault(n => n.Id == primaryId) ?? _selectedNodes.LastOrDefault()
			: _selectedNodes.LastOrDefault();

		_selectedTransition = snapshot.TransitionId is { } transitionId
			? AllTransitions.FirstOrDefault(t => t.Id == transitionId)
			: null;

		RefreshSelectedNodeDefinition();
		OnPropertyChanged(nameof(SelectedTransition));
		NotifySelectionChanged();
	}

	private void ClearSelectedNodes()
	{
		foreach (var existing in _selectedNodes.ToList())
		{
			existing.IsSelected = false;
			_selectedNodes.Remove(existing);
		}
	}

	private void RefreshSelectedNodeDefinition()
	{
		SelectedNodeDefinition = _selectedNode == null
			? null
			: _catalogService.GetDefinition(_selectedNode.DefinitionId);
	}

	/// <summary>Raises every selection-dependent property/command notification in one place.</summary>
	private void NotifySelectionChanged()
	{
		OnPropertyChanged(nameof(SelectedNode));
		OnPropertyChanged(nameof(SelectedNodes));
		OnPropertyChanged(nameof(CanEditNode));
		OnPropertyChanged(nameof(CanEditTransitions));
		OnPropertyChanged(nameof(CanCaptureSelectedNode));
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		CaptureFromGameCommand.NotifyCanExecuteChanged();
		RefreshParameterBindings();
	}
}
