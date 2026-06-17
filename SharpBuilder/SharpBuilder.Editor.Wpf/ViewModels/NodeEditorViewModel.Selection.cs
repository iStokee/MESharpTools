using System.Linq;
using System.Windows;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public partial class NodeEditorViewModel
{
	public void SelectNode(NodeModel node, bool toggle)
	{
		if (node == null) return;

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
			foreach (var existing in _selectedNodes.ToList())
			{
				existing.IsSelected = false;
				_selectedNodes.Remove(existing);
			}

			node.IsSelected = true;
			_selectedNodes.Add(node);
			_selectedNode = node;
		}

		SelectedNodeDefinition = _selectedNode == null
			? null
			: _catalogService.GetDefinition(_selectedNode.DefinitionId);

		OnPropertyChanged(nameof(SelectedNode));
		OnPropertyChanged(nameof(SelectedNodes));
		OnPropertyChanged(nameof(CanEditNode));
		OnPropertyChanged(nameof(CanEditTransitions));
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		RefreshParameterBindings();
		IsNodeInfoOpen = false;
	}

	/// <summary>
	/// Clears all selected nodes.
	/// </summary>
	public void ClearSelection()
	{
		foreach (var node in _selectedNodes.ToList())
		{
			node.IsSelected = false;
			_selectedNodes.Remove(node);
		}

		_selectedNode = null;
		SelectedNodeDefinition = null;
		SelectedTransition = null;

		OnPropertyChanged(nameof(SelectedNode));
		OnPropertyChanged(nameof(SelectedNodes));
		OnPropertyChanged(nameof(CanEditNode));
		OnPropertyChanged(nameof(CanEditTransitions));
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		RefreshParameterBindings();
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
		SelectedNodeDefinition = _selectedNode == null
			? null
			: _catalogService.GetDefinition(_selectedNode.DefinitionId);

		OnPropertyChanged(nameof(SelectedNode));
		OnPropertyChanged(nameof(SelectedNodes));
		OnPropertyChanged(nameof(CanEditNode));
		OnPropertyChanged(nameof(CanEditTransitions));
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		RefreshParameterBindings();
	}

	private void UpdatePrimarySelection(NodeModel? node)
	{
		foreach (var existing in _selectedNodes.ToList())
		{
			existing.IsSelected = false;
			_selectedNodes.Remove(existing);
		}

		_selectedNode = node;

		if (_selectedNode != null)
		{
			_selectedNode.IsSelected = true;
			_selectedNodes.Add(_selectedNode);
		}
	}
}
