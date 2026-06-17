using System;
using System.Linq;
using MESharp.API;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public partial class NodeEditorViewModel
{
	private void RefreshDashboard()
	{
		DashboardRuntime = GraphRunElapsed.ToString(@"hh\:mm\:ss");
		DashboardUptime = (DateTime.UtcNow - _builderOpenedUtc).ToString(@"hh\:mm\:ss");
		DashboardCurrentNode = _currentRunNode?.Title ?? "None";
		DashboardRunMode = IsRunning ? (IsLooping ? "Running loop" : "Running once") : Status;
		DashboardGraphSummary = $"{Script.Nodes.Count} nodes / {AllTransitions.Count()} transitions";
		DashboardSignalSummary = $"{Signals.Count} signal{(Signals.Count == 1 ? string.Empty : "s")}";
		DashboardLastUpdated = DateTime.Now.ToString("HH:mm:ss");

		DashboardSkillsView.Refresh();

		var snapshot = _dashboardRefreshService.Refresh(
			GameRuntime.IsGameApiAvailable,
			DashboardSkills,
			_itemTracker,
			_dashboardSkillSession);
		_dashboardSkillSession = snapshot.SkillSession;
		DashboardXpStatus = snapshot.XpStatus;
		DashboardActiveSkillCount = snapshot.ActiveSkillCount;
		DashboardTotalXpGained = snapshot.TotalXpGained;
		DashboardItemsStatus = snapshot.ItemsStatus;
		DashboardItemsGpPerHour = snapshot.ItemsGpPerHour;
		DashboardItemsActiveCount = snapshot.ItemsActiveCount;
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
		_dashboardSkillSession = null;
		RefreshDashboard();
	}

	/// <summary>Clears tracked items and re-baselines the item-flow session clock.</summary>
	private void ResetItemTracker()
	{
		_dashboardStartedUtc = DateTime.UtcNow;
		_itemTracker.Reset(_dashboardStartedUtc);
		RefreshDashboard();
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
