using System;
using System.Collections.Generic;
using System.Linq;
using MESharp.API;
using SharpBuilder.Editor.Wpf.ViewModels;

namespace SharpBuilder.Editor.Wpf.Services;

public sealed record DashboardRefreshSnapshot(
	string XpStatus,
	int ActiveSkillCount,
	int TotalXpGained,
	string ItemsStatus,
	long ItemsGpPerHour,
	int ItemsActiveCount,
	SkillSession? SkillSession);

public sealed class DashboardRefreshService
{
	public DashboardRefreshSnapshot Refresh(
		bool gameApiAvailable,
		IReadOnlyCollection<DashboardSkillRow> skills,
		ItemTracker itemTracker,
		SkillSession? skillSession)
	{
		if (!gameApiAvailable)
		{
			return new DashboardRefreshSnapshot(
				"XP unavailable outside the injected game runtime",
				0,
				0,
				"Items unavailable outside the injected game runtime",
				0,
				0,
				skillSession);
		}

		var itemStatus = RefreshItems(itemTracker, out var itemsGpPerHour, out var itemsActiveCount);
		var xpStatus = RefreshSkills(skills, ref skillSession, out var activeSkillCount, out var totalXpGained);

		return new DashboardRefreshSnapshot(
			xpStatus,
			activeSkillCount,
			totalXpGained,
			itemStatus,
			itemsGpPerHour,
			itemsActiveCount,
			skillSession);
	}

	private static string RefreshItems(ItemTracker itemTracker, out long gpPerHour, out int activeCount)
	{
		if (itemTracker.TryUpdate(out var error))
		{
			gpPerHour = itemTracker.TotalGpPerHour;
			activeCount = itemTracker.ActiveCount;
			return itemTracker.ActiveCount == 0
				? "Item tracker ready; no item flow yet"
				: $"{itemTracker.ActiveCount} item{(itemTracker.ActiveCount == 1 ? string.Empty : "s")} tracked";
		}

		gpPerHour = 0;
		activeCount = 0;
		return $"Item tracker failed: {error}";
	}

	private static string RefreshSkills(
		IReadOnlyCollection<DashboardSkillRow> skills,
		ref SkillSession? skillSession,
		out int activeSkillCount,
		out int totalXpGained)
	{
		activeSkillCount = 0;
		totalXpGained = 0;

		try
		{
			skillSession ??= new SkillSession();
		}
		catch (Exception ex)
		{
			skillSession = null;
			return $"XP tracker unavailable: {ex.Message}";
		}

		var updated = 0;
		string? error = null;
		foreach (var row in skills)
		{
			if (row.TryUpdate(skillSession, out error))
				updated++;
			else
				break;
		}

		if (updated != skills.Count)
			return $"XP refresh failed: {error}";

		activeSkillCount = skills.Count(s => s.IsActive);
		totalXpGained = skills.Sum(s => s.XpGained);
		return activeSkillCount == 0
			? "XP tracker ready; no gains this session"
			: $"{activeSkillCount} active skill{(activeSkillCount == 1 ? string.Empty : "s")}";
	}
}
