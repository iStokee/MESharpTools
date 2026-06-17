using System;
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

		IsDirty = false;
		OnPropertyChanged(nameof(IsCanvasEmpty));
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
		OnPropertyChanged(nameof(AllTransitions));
		OnPropertyChanged(nameof(IsCanvasEmpty));
		AddTransitionCommand.NotifyCanExecuteChanged();
		RefreshDashboard();
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
		OnPropertyChanged(nameof(AllTransitions));
		RefreshDashboard();
		MarkDirty();
	}

	private void OnTransitionPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(TransitionModel.IsActive))
			return;

		RefreshSignals();
		OnPropertyChanged(nameof(AllTransitions));
		RefreshDashboard();
		MarkDirty();
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
		RefreshDashboard();
		MarkDirty();
	}

	private void OnParameterPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (string.Equals(e.PropertyName, nameof(NodeParameterValue.RawValue), StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(e.PropertyName, nameof(NodeParameterValue.BoolValue), StringComparison.OrdinalIgnoreCase))
		{
			RefreshSignals();
			RefreshDashboard();
			MarkDirty();
		}
	}

	private void OnScriptPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// Metadata edits count as document changes; UpdatedAt is touched by saving itself.
		if (e.PropertyName is nameof(GraphModel.Name) or nameof(GraphModel.Description)
		    or nameof(GraphModel.Author) or nameof(GraphModel.StartNodeId))
		{
			MarkDirty();
		}
	}

	private void MarkDirty()
	{
		if (!_suppressDirty)
		{
			IsDirty = true;
		}
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

		SignalSuggestions.Clear();
		foreach (var key in keys)
		{
			SignalSuggestions.Add(key);
		}

		OnPropertyChanged(nameof(Signals));
		OnPropertyChanged(nameof(SignalSuggestions));
	}

	/// <summary>
	/// A node drag fires X and Y changes many times per second, and each
	/// <see cref="AllTransitions"/> invalidation rebuilds every connector path. Coalesce them so the
	/// edges still follow the node live, but the rebuild runs at most once per render frame
	/// (and once total for a whole multi-node drag tick) instead of twice per node per delta.
	/// </summary>
	private void QueueTransitionsRefresh()
	{
		var dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null)
		{
			// No UI thread (e.g. unit tests): keep the original synchronous behavior.
			OnPropertyChanged(nameof(AllTransitions));
			return;
		}

		if (_transitionsRefreshQueued)
			return;

		_transitionsRefreshQueued = true;
		dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
		{
			_transitionsRefreshQueued = false;
			OnPropertyChanged(nameof(AllTransitions));
		});
	}

	private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(NodeModel.X) || e.PropertyName == nameof(NodeModel.Y))
		{
			QueueTransitionsRefresh();
		}

		// Run/selection feedback is visual-only state; everything else is a document edit.
		if (e.PropertyName is not (nameof(NodeModel.IsActive) or nameof(NodeModel.IsCurrent)
		    or nameof(NodeModel.IsSelected) or nameof(NodeModel.LastRunStatus)))
		{
			MarkDirty();
		}
	}
}
