using System;
using System.Linq;
using System.Threading.Tasks;
using MESharp.API;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using SharpBuilder.Editor.Wpf.Services;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public partial class NodeEditorViewModel
{
	/// <summary>
	/// Updates only the cheap, in-process dashboard fields (clocks, current node, graph/signal
	/// summaries). It never touches the game client — all game-backed reads go through
	/// <see cref="BeginDashboardGameRefresh"/>, which runs under the game-API scheduler.
	/// </summary>
	private void RefreshDashboard()
	{
		DashboardRuntime = GraphRunElapsed.ToString(@"hh\:mm\:ss");
		DashboardUptime = (DateTime.UtcNow - _builderOpenedUtc).ToString(@"hh\:mm\:ss");
		DashboardCurrentNode = _currentRunNode?.Title ?? "None";
		DashboardRunMode = IsRunning ? (_currentRunLooping ? "Running loop" : "Running once") : Status;
		DashboardGraphSummary = $"{Script.Nodes.Count} nodes / {AllTransitions.Count()} transitions";
		DashboardSignalSummary = $"{Signals.Count} signal{(Signals.Count == 1 ? string.Empty : "s")}";
		DashboardLastUpdated = DateTime.Now.ToString("HH:mm:ss");

		DashboardSkillsView.Refresh();
	}

	private void BeginDashboardGameRefresh()
	{
		if (_dashboardGameRefreshInProgress)
		{
			_dashboardGameRefreshPending = true;
			return;
		}

		_dashboardGameRefreshPending = false;
		_dashboardGameRefreshInProgress = true;
		var version = _dashboardRefreshVersion;
		var skillNames = DashboardSkills.Select(s => s.SkillName).ToArray();
		var skillSession = _dashboardSkillSession;
		var gameApiAvailable = GameRuntime.IsGameApiAvailable;

		// Capture runs on the shared game-API lane so it can never overlap an executor node.
		_ = Task.Run(() => GameApi.Scheduler.RunAsync(() => _dashboardRefreshService.Capture(
				gameApiAvailable,
				skillNames,
				_itemTracker,
				skillSession)))
			.ContinueWith(task =>
			{
				RunOnUi(() =>
				{
					_dashboardGameRefreshInProgress = false;

					if (version != _dashboardRefreshVersion)
						return;

					if (task.IsFaulted)
					{
						var message = task.Exception?.GetBaseException().Message ?? "unknown error";
						DashboardXpStatus = $"XP refresh failed: {message}";
						DashboardItemsStatus = $"Item tracker failed: {message}";
						return;
					}

					var snapshot = _dashboardRefreshService.Apply(task.Result, DashboardSkills, _itemTracker);
					ApplyDashboardSnapshot(snapshot);

					if (_dashboardGameRefreshPending)
					{
						BeginDashboardGameRefresh();
					}
				});
			});
	}

	private void ApplyDashboardSnapshot(DashboardRefreshSnapshot snapshot)
	{
		_dashboardSkillSession = snapshot.SkillSession;
		DashboardXpStatus = snapshot.XpStatus;
		DashboardActiveSkillCount = snapshot.ActiveSkillCount;
		DashboardTotalXpGained = snapshot.TotalXpGained;
		DashboardItemsStatus = snapshot.ItemsStatus;
		DashboardItemsGpPerHour = snapshot.ItemsGpPerHour;
		DashboardItemsActiveCount = snapshot.ItemsActiveCount;
		DashboardSession = snapshot.SessionLabel;
		DashboardSkillsView.Refresh();
		DashboardItemsView.Refresh();
	}

	/// <summary>Zeroes the graph runtime clock (restarting it from now if a run is in progress).</summary>
	private void ResetGraphRuntime()
	{
		_graphRunAccumulated = TimeSpan.Zero;
		_graphRunStartedUtc = IsRunning ? DateTime.UtcNow : null;
		RefreshDashboard();
	}

	/// <summary>Re-baselines XP tracking so gained/XP-per-hour restart from the current totals.</summary>
	private void ResetXpTracker()
	{
		_dashboardRefreshVersion++;
		_dashboardSkillSession = null;
		RefreshDashboard();
		BeginDashboardGameRefresh();
	}

	/// <summary>Clears tracked items and re-baselines the item-flow session clock.</summary>
	private void ResetItemTracker()
	{
		_dashboardRefreshVersion++;
		_dashboardStartedUtc = DateTime.UtcNow;
		_itemTracker.Reset(_dashboardStartedUtc);
		RefreshDashboard();
		BeginDashboardGameRefresh();
	}

	private bool FilterDashboardSkill(object obj)
	{
		if (obj is not DashboardSkillRow row)
			return false;

		var activeOnly = _dashboardXpActiveOnly || DashboardShowOnlyActiveXp;
		return !activeOnly || row.IsActive;
	}

	private bool FilterDashboardItem(object obj)
	{
		if (obj is not DashboardItemRow row)
			return false;

		return !_dashboardItemsActiveOnly || row.IsActive;
	}

	private bool DashboardShowOnlyActiveXp =>
		_script?.Nodes
			.Where(n => string.Equals(n.DefinitionId, NodeCatalogDefaults.ScriptDashboardId, StringComparison.OrdinalIgnoreCase))
			.SelectMany(n => n.Parameters)
			.FirstOrDefault(p => string.Equals(p.Key, "showOnlyActiveXp", StringComparison.OrdinalIgnoreCase))
			?.BoolValue == true;
}
