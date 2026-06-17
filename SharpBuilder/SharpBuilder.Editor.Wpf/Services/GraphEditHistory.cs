using System;
using System.Collections.Generic;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;

namespace SharpBuilder.Editor.Wpf.Services;

public sealed class GraphEditHistory
{
	private sealed record Edit(string Label, GraphModel Before, GraphModel After);

	private readonly Stack<Edit> _undo = new();
	private readonly Stack<Edit> _redo = new();
	private GraphModel? _batchBefore;
	private string? _batchLabel;
	private int _batchDepth;

	public int MaxSize { get; set; } = 80;
	public bool IsApplying { get; private set; }
	public bool CanUndo => _undo.Count > 0;
	public bool CanRedo => _redo.Count > 0;

	public event EventHandler? Changed;

	public void Clear()
	{
		_undo.Clear();
		_redo.Clear();
		_batchBefore = null;
		_batchLabel = null;
		_batchDepth = 0;
		OnChanged();
	}

	public void Record(string label, GraphModel before, GraphModel after)
	{
		if (IsApplying || GraphEquals(before, after))
			return;

		PushUndo(new Edit(label, GraphCloneService.Clone(before), GraphCloneService.Clone(after)));
		_redo.Clear();
		OnChanged();
	}

	public IDisposable Batch(string label, GraphModel current)
	{
		if (_batchDepth == 0)
		{
			_batchBefore = GraphCloneService.Clone(current);
			_batchLabel = label;
		}

		_batchDepth++;
		return new BatchScope(this);
	}

	public void CommitBatch(GraphModel current)
	{
		if (_batchDepth != 0 || _batchBefore == null)
			return;

		Record(_batchLabel ?? "Edit graph", _batchBefore, current);
		_batchBefore = null;
		_batchLabel = null;
	}

	public GraphModel? Undo(GraphModel current)
	{
		if (!CanUndo)
			return null;

		var edit = _undo.Pop();
		_redo.Push(new Edit(edit.Label, GraphCloneService.Clone(edit.Before), GraphCloneService.Clone(current)));
		OnChanged();
		return Apply(edit.Before);
	}

	public GraphModel? Redo()
	{
		if (!CanRedo)
			return null;

		var edit = _redo.Pop();
		PushUndo(edit);
		OnChanged();
		return Apply(edit.After);
	}

	private GraphModel Apply(GraphModel graph)
	{
		IsApplying = true;
		try
		{
			return GraphCloneService.Clone(graph);
		}
		finally
		{
			IsApplying = false;
		}
	}

	private void PushUndo(Edit edit)
	{
		_undo.Push(edit);
		while (_undo.Count > MaxSize)
		{
			var keep = _undo.Take(MaxSize).Reverse().ToArray();
			_undo.Clear();
			foreach (var item in keep)
				_undo.Push(item);
		}
	}

	private static bool GraphEquals(GraphModel a, GraphModel b)
		=> Newtonsoft.Json.JsonConvert.SerializeObject(a) == Newtonsoft.Json.JsonConvert.SerializeObject(b);

	private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

	private sealed class BatchScope : IDisposable
	{
		private readonly GraphEditHistory _history;
		private bool _disposed;

		public BatchScope(GraphEditHistory history) => _history = history;

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			_history._batchDepth = Math.Max(0, _history._batchDepth - 1);
		}
	}
}
