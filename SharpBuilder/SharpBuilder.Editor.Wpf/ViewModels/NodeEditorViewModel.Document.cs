using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using Application = System.Windows.Application;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public partial class NodeEditorViewModel
{
	private void AttachScript(GraphModel script)
	{
		// A pending property edit belongs to the previous script; the swap invalidates it.
		_propertyEditPending = false;
		_propertyEditTimer?.Stop();

		_suppressDirty = true;
		try
		{
			script.PropertyChanged += OnScriptPropertyChanged;
			script.Nodes.CollectionChanged += OnNodesChanged;
			foreach (var node in script.Nodes)
			{
				EnsureDefinition(node);
				node.PropertyChanged += OnNodePropertyChanged;
				node.Transitions.CollectionChanged += OnTransitionsChanged;
				node.Parameters.CollectionChanged += OnParametersChanged;
				foreach (var transition in node.Transitions)
				{
					transition.PropertyChanged += OnTransitionPropertyChanged;
				}
				foreach (var param in node.Parameters)
				{
					param.PropertyChanged += OnParameterPropertyChanged;
				}
			}

			SelectedNode = script.Nodes.FirstOrDefault();
			Status = $"Loaded \"{script.Name}\"";
		}
		finally
		{
			_suppressDirty = false;
		}

		_shadowScript = GraphCloneService.Clone(script);
		RebuildEdges();
		IsDirty = false;
		OnPropertyChanged(nameof(IsCanvasEmpty));
	}

	/// <summary>
	/// Recreates the connector layer from the current graph. Called on structural changes (nodes or
	/// transitions added/removed/reordered/retargeted); node moves are handled by each edge itself.
	/// </summary>
	private void RebuildEdges()
	{
		foreach (var edge in Edges)
			edge.Dispose();
		Edges.Clear();

		var nodesById = new Dictionary<Guid, NodeModel>(Script.Nodes.Count);
		foreach (var node in Script.Nodes)
			nodesById[node.Id] = node;

		foreach (var node in Script.Nodes)
		{
			foreach (var transition in node.Transitions)
			{
				if (nodesById.TryGetValue(transition.ToNodeId, out var target))
					Edges.Add(new EdgeViewModel(node, target, transition));
			}
		}
	}

	private void DetachScript()
	{
		_script.PropertyChanged -= OnScriptPropertyChanged;
		_script.Nodes.CollectionChanged -= OnNodesChanged;
		foreach (var node in _script.Nodes)
		{
			node.PropertyChanged -= OnNodePropertyChanged;
			node.Transitions.CollectionChanged -= OnTransitionsChanged;
			node.Parameters.CollectionChanged -= OnParametersChanged;
			foreach (var transition in node.Transitions)
			{
				transition.PropertyChanged -= OnTransitionPropertyChanged;
			}
			foreach (var param in node.Parameters)
			{
				param.PropertyChanged -= OnParameterPropertyChanged;
			}
		}
	}

	private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (NodeModel node in e.NewItems)
			{
				EnsureDefinition(node);
				node.PropertyChanged += OnNodePropertyChanged;
				node.Transitions.CollectionChanged += OnTransitionsChanged;
				node.Parameters.CollectionChanged += OnParametersChanged;
				foreach (var transition in node.Transitions)
				{
					transition.PropertyChanged += OnTransitionPropertyChanged;
				}
				foreach (var param in node.Parameters)
				{
					param.PropertyChanged += OnParameterPropertyChanged;
				}
			}
		}

		if (e.OldItems != null)
		{
			foreach (NodeModel node in e.OldItems)
			{
				node.PropertyChanged -= OnNodePropertyChanged;
				node.Transitions.CollectionChanged -= OnTransitionsChanged;
				node.Parameters.CollectionChanged -= OnParametersChanged;
				foreach (var transition in node.Transitions)
				{
					transition.PropertyChanged -= OnTransitionPropertyChanged;
				}
				foreach (var param in node.Parameters)
				{
					param.PropertyChanged -= OnParameterPropertyChanged;
				}

				_selectedNodes.Remove(node);
			}
		}

		RefreshSignals();
		RebuildEdges();
		OnPropertyChanged(nameof(IsCanvasEmpty));
		AddTransitionCommand.NotifyCanExecuteChanged();
		Dashboard.Refresh();
		MarkDirty();
	}

	private void OnTransitionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (TransitionModel transition in e.NewItems)
			{
				transition.PropertyChanged += OnTransitionPropertyChanged;
			}
		}

		if (e.OldItems != null)
		{
			foreach (TransitionModel transition in e.OldItems)
			{
				transition.PropertyChanged -= OnTransitionPropertyChanged;
			}
		}

		RefreshSignals();
		RebuildEdges();
		Dashboard.Refresh();
		MarkDirty();
	}

	private void OnTransitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(TransitionModel.IsActive))
			return;

		if (e.PropertyName == nameof(TransitionModel.ConditionKey))
			RefreshSignals();

		// Only endpoint changes alter connector geometry. Labels, trigger state, and condition values
		// stay on the existing EdgeViewModel, so typing in the inspector no longer recreates every path.
		if (e.PropertyName is nameof(TransitionModel.ToNodeId) or nameof(TransitionModel.FromNodeId))
			RebuildEdges();

		NotePropertyEdit();
	}

	private void OnParametersChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (NodeParameterValue param in e.NewItems)
			{
				param.PropertyChanged += OnParameterPropertyChanged;
			}
		}

		if (e.OldItems != null)
		{
			foreach (NodeParameterValue param in e.OldItems)
			{
				param.PropertyChanged -= OnParameterPropertyChanged;
			}
		}

		RefreshSignals();
		RefreshParameterBindings();
		Dashboard.Refresh();
		MarkDirty();
	}

	private void OnParameterPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (string.Equals(e.PropertyName, nameof(NodeParameterValue.RawValue), StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(e.PropertyName, nameof(NodeParameterValue.BoolValue), StringComparison.OrdinalIgnoreCase))
		{
			if (sender is NodeParameterValue parameter &&
			    string.Equals(parameter.Key, "signal", StringComparison.OrdinalIgnoreCase))
			{
				RefreshSignals();
			}

			if (sender is NodeParameterValue dashboardParameter &&
			    string.Equals(dashboardParameter.Key, "showOnlyActiveXp", StringComparison.OrdinalIgnoreCase))
			{
				Dashboard.Refresh();
			}

			NotePropertyEdit();
		}
	}

	private void OnScriptPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// Metadata edits count as document changes; UpdatedAt is touched by saving itself.
		if (e.PropertyName is nameof(GraphModel.Name) or nameof(GraphModel.Description)
		    or nameof(GraphModel.Author) or nameof(GraphModel.StartNodeId))
		{
			NotePropertyEdit();
		}
	}

	private void MarkDirty()
	{
		if (!_suppressDirty)
		{
			IsDirty = true;
		}
	}

	/// <summary>
	/// Marks an in-place property change (inspector fields, node titles, edge labels…) as an
	/// uncommitted edit. It is committed as a single "Edit properties" undo entry after a short
	/// idle pause, or by the next selection change, batch, or undo/redo.
	/// </summary>
	private void NotePropertyEdit()
	{
		if (_suppressDirty || _editHistory.IsApplying)
			return;

		MarkDirty();

		// During a gesture batch the commit at the end of the gesture captures everything.
		if (_activeGraphEditBatchLabel != null)
			return;

		_propertyEditPending = true;
		if (_propertyEditTimer == null)
		{
			_propertyEditTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
			_propertyEditTimer.Tick += (_, _) => CommitPendingPropertyEdit();
		}

		// Restart the idle countdown on every change.
		_propertyEditTimer.Stop();
		_propertyEditTimer.Start();
	}

	private void RefreshSignals()
	{
		var keys = GraphSignalService.DiscoverSignalKeys(Script);

		// Remove stale signals
		for (var i = Signals.Count - 1; i >= 0; i--)
		{
			if (!keys.Contains(Signals[i].Key, StringComparer.OrdinalIgnoreCase))
			{
				Signals.RemoveAt(i);
			}
		}

		// Add missing signals (default false)
		foreach (var key in keys)
		{
			if (!Signals.Any(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)))
			{
				Signals.Add(new RuntimeSignal { Key = key, Value = false });
			}
		}

		// Only rebuild the suggestion list when the key set actually changed — a reset while an
		// autocomplete dropdown is open would collapse it mid-typing.
		var suggestionsChanged = SignalSuggestions.Count != keys.Count;
		if (!suggestionsChanged)
		{
			for (var i = 0; i < keys.Count; i++)
			{
				if (!string.Equals(SignalSuggestions[i], keys[i], StringComparison.Ordinal))
				{
					suggestionsChanged = true;
					break;
				}
			}
		}

		if (suggestionsChanged)
		{
			SignalSuggestions.Clear();
			foreach (var key in keys)
			{
				SignalSuggestions.Add(key);
			}
		}

		OnPropertyChanged(nameof(Signals));
		OnPropertyChanged(nameof(SignalSuggestions));
	}

	private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// X/Y moves are tracked by each EdgeViewModel directly; nothing to invalidate here.

		// Run/selection feedback is visual-only state; everything else is a document edit.
		if (e.PropertyName is not (nameof(NodeModel.IsActive) or nameof(NodeModel.IsCurrent)
		    or nameof(NodeModel.IsSelected) or nameof(NodeModel.LastRunStatus)))
		{
			NotePropertyEdit();
		}
	}
}
