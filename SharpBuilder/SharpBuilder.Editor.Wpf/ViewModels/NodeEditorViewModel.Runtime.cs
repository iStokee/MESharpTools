using System;
using System.Collections.Generic;
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
		var errors = issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
		if (errors.Count > 0)
		{
			Status = errors.Any(e => e.Message.Contains("in-game API"))
				? "Design mode: game-backed nodes need the in-game script host"
				: $"Validation failed ({errors.Count} error(s))";
			MessageBox.Show(
				string.Join(Environment.NewLine, issues.Select(i => i.ToString())),
				"Cannot run script",
				MessageBoxButton.OK,
				MessageBoxImage.Warning);
			return;
		}

		if (issues.Count > 0)
		{
			Status = $"Running with {issues.Count} warning(s)";
		}

		var signals = BuildSignalMap();
		if (signals == null)
			return;

		IsRunning = true;
		IsLooping = loop;
		Status = loop ? "Running (loop)" : "Running once";

		ClearTrail();

		_runCts?.Dispose();
		_runCts = new CancellationTokenSource();
		var token = _runCts.Token;
		try
		{
			_runTask = Task.Run(() => _engine.RunAsync(Script, signals, loop, token), token);
			await _runTask;
		}
		finally
		{
			// Only here, when the engine has genuinely finished, does the run end.
			IsRunning = false;
			if (token.IsCancellationRequested)
			{
				Status = "Stopped";
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
			return Signals
				.Where(s => !string.IsNullOrWhiteSpace(s.Key))
				.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
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
			if (_currentRunNode != null && !ReferenceEquals(_currentRunNode, node))
			{
				_currentRunNode.IsCurrent = false;
			}

			_currentRunNode = node;
			node.IsCurrent = true;
			node.IsActive = true;
			Status = $"Entered {node.Title}";
			RefreshDashboard();
		});
	}

	private void OnNodeCompleted(object? sender, (NodeModel Node, NodeExecutionResult Result) e)
	{
		RunOnUi(() =>
		{
			e.Node.LastRunStatus = e.Result.Status == NodeExecutionStatus.Fail
				? NodeRunStatus.Fail
				: NodeRunStatus.Success;

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

			RefreshDashboard();
		});
	}

	private void OnTransitionTaken(object? sender, TransitionModel transition)
	{
		RunOnUi(() =>
		{
			transition.IsActive = true;
			RefreshDashboard();
		});
	}

	private void OnEngineCompleted(object? sender, EventArgs e)
	{
		RunOnUi(() =>
		{
			IsRunning = false;
			Status = "Cycle complete";
			RefreshDashboard();
		});
	}

	private void OnEngineFaulted(object? sender, Exception ex)
	{
		RunOnUi(() =>
		{
			IsRunning = false;
			Status = $"Error: {ex.Message}";
			RefreshDashboard();
			MessageBox.Show($"SharpBuilder engine failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
		});
	}

	private static void RunOnUi(Action action)
	{
		if (Application.Current?.Dispatcher != null)
		{
			if (Application.Current.Dispatcher.CheckAccess())
			{
				action();
			}
			else
			{
				Application.Current.Dispatcher.Invoke(action);
			}
		}
		else
		{
			action();
		}
	}
}
