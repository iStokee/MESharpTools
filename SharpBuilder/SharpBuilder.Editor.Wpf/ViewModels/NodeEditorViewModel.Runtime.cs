using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MESharp.API;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public partial class NodeEditorViewModel
{
	private const int MaxRunLogEntries = 500;

	/// <summary>Bounded, newest-last log of run activity (node results, diagnostics, faults).</summary>
	public ObservableCollection<RunLogEntry> RunLog { get; } = new();

	/// <summary>Validation results from the last run attempt; items select their node when clicked.</summary>
	public ObservableCollection<ValidationIssue> ValidationIssues { get; } = new();

	/// <summary>
	/// Appends a run-log entry, trimming the oldest lines past the cap. Must be called on the UI
	/// thread (all callers already marshal through <see cref="RunOnUi"/> or run there).
	/// </summary>
	private void AppendRunLog(RunLogKind kind, string message)
	{
		RunLog.Add(new RunLogEntry(DateTime.Now, kind, message));
		while (RunLog.Count > MaxRunLogEntries)
			RunLog.RemoveAt(0);
	}

	private void OnEngineDiagnostic(string message)
	{
		RunOnUi(() => AppendRunLog(RunLogKind.Error, message));
	}

	private void SelectValidationIssue(ValidationIssue? issue)
	{
		if (issue?.NodeId is not { } nodeId)
			return;

		var node = Script.Nodes.FirstOrDefault(n => n.Id == nodeId);
		if (node != null)
			SelectNode(node, toggle: false);
	}

	private async Task StartRunAsync(bool loop)
	{
		if (IsRunning)
			return;

		// A previous run may still be unwinding after Stop; wait for the engine to
		// actually finish, otherwise its already-running guard throws.
		if (_runTask is { IsCompleted: false })
		{
			try
			{
				await _runTask;
			}
			catch
			{
				// Failures of the previous run were already surfaced via Faulted.
			}
		}

		var issues = _validator.Validate(Script, GameRuntime.IsGameApiAvailable);
		ValidationIssues.Clear();
		foreach (var issue in issues)
			ValidationIssues.Add(issue);

		var errors = issues.Count(i => i.Severity == ValidationSeverity.Error);
		if (errors > 0)
		{
			Status = issues.Any(i => i.Severity == ValidationSeverity.Error && i.Message.Contains("in-game API"))
				? "Design mode: game-backed nodes need the in-game script host"
				: $"Validation failed ({errors} error(s))";
			IsValidationOpen = true;
			AppendRunLog(RunLogKind.Error, $"Run blocked by {errors} validation error(s) — see the Validation panel.");
			return;
		}

		if (issues.Count > 0)
		{
			Status = $"Running with {issues.Count} warning(s)";
			AppendRunLog(RunLogKind.Info, $"Running with {issues.Count} validation warning(s).");
		}

		var signals = BuildSignalMap();
		if (signals == null)
			return;

		IsRunning = true;
		// Remember the active run's mode without clobbering the user's Loop toggle
		// (Step must not switch the toggle off for the next Start).
		_currentRunLooping = loop;
		Status = loop ? "Running (loop)" : "Running once";
		AppendRunLog(RunLogKind.Info, loop ? "Run started (loop)." : "Run started (single pass).");

		ClearTrail();
		var runScript = GraphCloneService.Clone(Script);

		_runCts?.Dispose();
		_runCts = new CancellationTokenSource();
		var token = _runCts.Token;
		try
		{
			_runTask = Task.Run(() => _engine.RunAsync(runScript, signals, loop, token), token);
			await _runTask;
		}
		finally
		{
			// Only here, when the engine has genuinely finished, does the run end.
			IsRunning = false;
			if (token.IsCancellationRequested)
			{
				Status = "Stopped";
				AppendRunLog(RunLogKind.Info, "Run stopped.");
			}
		}
	}

	private void StopRun()
	{
		if (!IsRunning)
			return;

		// Signal cancellation; IsRunning resets when the engine task actually completes.
		_runCts?.Cancel();
		Status = "Stopping…";
	}

	private IReadOnlyDictionary<string, bool>? BuildSignalMap()
	{
		try
		{
			var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			List<string>? duplicates = null;
			foreach (var signal in Signals)
			{
				if (string.IsNullOrWhiteSpace(signal.Key))
					continue;

				if (map.ContainsKey(signal.Key))
					(duplicates ??= new List<string>()).Add(signal.Key);

				// Last duplicate wins, but the user gets told instead of silently losing a value.
				map[signal.Key] = signal.Value;
			}

			if (duplicates != null)
			{
				AppendRunLog(
					RunLogKind.Fail,
					$"Duplicate signal key(s): {string.Join(", ", duplicates.Distinct(StringComparer.OrdinalIgnoreCase))} — the last value wins.");
			}

			return map;
		}
		catch (Exception ex)
		{
			Status = $"Signal error: {ex.Message}";
			return null;
		}
	}

	private void OnNodeEntered(object? sender, NodeModel node)
	{
		RunOnUi(() =>
		{
			node = ResolveLiveNode(node) ?? node;
			if (_currentRunNode != null && !ReferenceEquals(_currentRunNode, node))
			{
				_currentRunNode.IsCurrent = false;
				if (!_runTrailNodeCounts.ContainsKey(_currentRunNode.Id))
				{
					_currentRunNode.IsActive = false;
					_currentRunNode.LastRunStatus = NodeRunStatus.None;
				}
			}

			_currentRunNode = node;
			node.IsCurrent = true;
			RecordTrailNode(node);
			Status = $"Entered {node.Title}";
			// Keep the current-node readout live; the 1 Hz dashboard timer owns everything else.
			Dashboard.CurrentNode = node.Title;
		});
	}

	private void OnNodeCompleted(object? sender, (NodeModel Node, NodeExecutionResult Result) e)
	{
		RunOnUi(() =>
		{
			var node = ResolveLiveNode(e.Node) ?? e.Node;
			node.LastRunStatus = e.Result.Status == NodeExecutionStatus.Fail
				? NodeRunStatus.Fail
				: NodeRunStatus.Success;

			AppendRunLog(
				e.Result.Status == NodeExecutionStatus.Fail ? RunLogKind.Fail : RunLogKind.Success,
				$"{node.Title}: {e.Result.Status}");

			// Mirror executor outputs into the Signals panel so it reflects the live run.
			if (e.Result.Outputs != null)
			{
				foreach (var kv in e.Result.Outputs)
				{
					var existing = Signals.FirstOrDefault(s => string.Equals(s.Key, kv.Key, StringComparison.OrdinalIgnoreCase));
					if (existing != null)
					{
						existing.Value = kv.Value;
					}
					else
					{
						Signals.Add(new RuntimeSignal { Key = kv.Key, Value = kv.Value });
					}
				}
			}
		});
	}

	private void OnTransitionTaken(object? sender, TransitionModel transition)
	{
		RunOnUi(() =>
		{
			RecordTrailTransition(ResolveLiveTransition(transition) ?? transition);
		});
	}

	private void OnEngineCompleted(object? sender, EventArgs e)
	{
		RunOnUi(() =>
		{
			IsRunning = false;
			Status = "Cycle complete";
			AppendRunLog(RunLogKind.Info, "Cycle complete.");
			Dashboard.Refresh();
		});
	}

	private void OnEngineFaulted(object? sender, Exception ex)
	{
		// No modal here: a fault mid-run must not steal focus from the game. The run log and
		// status line carry the details.
		RunOnUi(() =>
		{
			IsRunning = false;
			Status = $"Error: {ex.Message}";
			AppendRunLog(RunLogKind.Error, $"Engine faulted: {ex.Message}");
			IsRunLogOpen = true;
			Dashboard.Refresh();
		});
	}

	/// <summary>
	/// Queues UI feedback from engine events without blocking the engine thread — a fast graph must
	/// never wait on the dispatcher between nodes. Falls back to inline execution when there is no
	/// WPF application (unit tests) or we're already on the UI thread.
	/// </summary>
	internal static void RunOnUi(Action action)
	{
		var dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			action();
		}
		else
		{
			dispatcher.BeginInvoke(action);
		}
	}

	private NodeModel? ResolveLiveNode(NodeModel node)
		=> Script.Nodes.FirstOrDefault(n => n.Id == node.Id);

	private TransitionModel? ResolveLiveTransition(TransitionModel transition)
		=> AllTransitions.FirstOrDefault(t => t.Id == transition.Id);

	private void RecordTrailNode(NodeModel node)
	{
		IncrementTrailCount(_runTrailNodeCounts, node.Id);
		node.IsActive = true;
		_runTrail.Enqueue(new RunTrailEntry(RunTrailEntryKind.Node, node.Id));
		TrimRunTrail();
	}

	private void RecordTrailTransition(TransitionModel transition)
	{
		IncrementTrailCount(_runTrailTransitionCounts, transition.Id);
		transition.IsActive = true;
		_runTrail.Enqueue(new RunTrailEntry(RunTrailEntryKind.Transition, transition.Id));
		TrimRunTrail();
	}

	private void TrimRunTrail()
	{
		while (_runTrail.Count > MaxRunTrailEntries)
		{
			var expired = _runTrail.Dequeue();
			if (expired.Kind == RunTrailEntryKind.Node)
			{
				if (DecrementTrailCount(_runTrailNodeCounts, expired.Id))
				{
					var node = Script.Nodes.FirstOrDefault(n => n.Id == expired.Id);
					if (node != null && !node.IsCurrent)
					{
						node.IsActive = false;
						node.LastRunStatus = NodeRunStatus.None;
					}
				}
			}
			else if (DecrementTrailCount(_runTrailTransitionCounts, expired.Id))
			{
				var transition = AllTransitions.FirstOrDefault(t => t.Id == expired.Id);
				if (transition != null)
					transition.IsActive = false;
			}
		}
	}

	private static void IncrementTrailCount(Dictionary<Guid, int> counts, Guid id)
	{
		counts[id] = counts.TryGetValue(id, out var current) ? current + 1 : 1;
	}

	private static bool DecrementTrailCount(Dictionary<Guid, int> counts, Guid id)
	{
		if (!counts.TryGetValue(id, out var current))
			return true;

		if (current > 1)
		{
			counts[id] = current - 1;
			return false;
		}

		counts.Remove(id);
		return true;
	}
}
