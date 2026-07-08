using System;
using System.Collections.Generic;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;

namespace SharpBuilder.Editor.Wpf.Services;

/// <summary>
/// Undo/redo stack of whole-graph snapshots. Stored snapshots are treated as immutable: callers
/// hand in a pristine "before" clone (ownership transfers here), the live "after" graph is cloned
/// on record, and applying an entry hands back a fresh clone — so no snapshot is ever mutated.
/// </summary>
public sealed class GraphEditHistory
{
	private sealed record Edit(string Label, GraphModel Before, GraphModel After);

	// List-as-stack (push/pop at the end) so trimming the oldest entry is a cheap RemoveAt(0).
	private readonly List<Edit> _undo = new();
	private readonly List<Edit> _redo = new();

	public int MaxSize { get; set; } = 80;
	public bool IsApplying { get; private set; }
	public bool CanUndo => _undo.Count > 0;
	public bool CanRedo => _redo.Count > 0;

	public event EventHandler? Changed;

	public void Clear()
	{
		_undo.Clear();
		_redo.Clear();
		OnChanged();
	}

	/// <summary>
	/// Records an edit. <paramref name="before"/> must be a dedicated clone (it is stored as-is);
	/// <paramref name="after"/> may be the live graph (it is cloned). Returns false when the edit
	/// is a no-op and nothing was recorded.
	/// </summary>
	public bool Record(string label, GraphModel before, GraphModel after)
	{
		if (IsApplying || GraphCompareService.AreEquivalent(before, after))
			return false;

		PushUndo(new Edit(label, before, GraphCloneService.Clone(after)));
		_redo.Clear();
		OnChanged();
		return true;
	}

	public GraphModel? Undo(GraphModel current)
	{
		if (!CanUndo)
			return null;

		var edit = Pop(_undo);
		_redo.Add(new Edit(edit.Label, edit.Before, GraphCloneService.Clone(current)));
		OnChanged();
		return Apply(edit.Before);
	}

	public GraphModel? Redo()
	{
		if (!CanRedo)
			return null;

		var edit = Pop(_redo);
		PushUndo(edit);
		OnChanged();
		return Apply(edit.After);
	}

	private GraphModel Apply(GraphModel graph)
	{
		IsApplying = true;
		try
		{
			// Hand out a clone so the live document can keep mutating without corrupting the snapshot.
			return GraphCloneService.Clone(graph);
		}
		finally
		{
			IsApplying = false;
		}
	}

	private void PushUndo(Edit edit)
	{
		_undo.Add(edit);
		while (_undo.Count > MaxSize)
			_undo.RemoveAt(0);
	}

	private static Edit Pop(List<Edit> stack)
	{
		var edit = stack[^1];
		stack.RemoveAt(stack.Count - 1);
		return edit;
	}

	private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
