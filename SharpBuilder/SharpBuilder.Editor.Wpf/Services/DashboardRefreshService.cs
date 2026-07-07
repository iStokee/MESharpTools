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
	SkillSession? SkillSession,
	string SessionLabel);

public sealed record DashboardRefreshCapture(
	string XpStatus,
	string ItemsStatus,
	SkillSession? SkillSession,
	IReadOnlyList<DashboardSkillRefresh> Skills,
	ItemTrackerSnapshot? Items,
	string SessionLabel);

public sealed record DashboardSkillRefresh(
	SkillName SkillName,
	int Level,
	int Xp,
	int XpGained,
	double XpPerHour,
	int XpToNext,
	string Eta);

public sealed class DashboardRefreshService
{
	/// <summary>
	/// Reads all game-backed dashboard state (XP, items, and the active session/account). The caller
	/// must invoke this through the game-API scheduler so it never overlaps an executor node.
	/// </summary>
	public DashboardRefreshCapture Capture(
		bool gameApiAvailable,
		IReadOnlyCollection<SkillName> skillNames,
		ItemTracker itemTracker,
		SkillSession? skillSession)
	{
		if (!gameApiAvailable)
		{
			return new DashboardRefreshCapture(
				"XP unavailable outside the injected game runtime",
				"Items unavailable outside the injected game runtime",
				skillSession,
				Array.Empty<DashboardSkillRefresh>(),
				null,
				CaptureSessionLabel(gameApiAvailable: false));
		}

		var sessionLabel = CaptureSessionLabel(gameApiAvailable: true);
		var itemStatus = CaptureItems(itemTracker, out var itemSnapshot);
		var xpStatus = CaptureSkills(skillNames, ref skillSession, out var skillSnapshots);

		return new DashboardRefreshCapture(
			xpStatus,
			itemStatus,
			skillSession,
			skillSnapshots,
			itemSnapshot,
			sessionLabel);
	}

	/// <summary>
	/// Describes the account/session the dashboard is reading from. Falls back to a design-mode label
	/// when there is no injected game runtime, and a logged-out label when the client isn't logged in.
	/// </summary>
	private static string CaptureSessionLabel(bool gameApiAvailable)
	{
		if (!gameApiAvailable)
			return "Design mode — no game session";

		try
		{
			if (!LocalPlayer.IsLoggedIn())
				return "Not logged in";

			var name = LocalPlayer.Name;
			return string.IsNullOrWhiteSpace(name) ? "Logged in" : name;
		}
		catch (Exception ex)
		{
			return $"Session unavailable: {ex.Message}";
		}
	}

	public DashboardRefreshSnapshot Apply(
		DashboardRefreshCapture capture,
		IReadOnlyCollection<DashboardSkillRow> skills,
		ItemTracker itemTracker)
	{
		if (capture.Items != null)
			itemTracker.Apply(capture.Items);

		var capturedBySkill = capture.Skills.ToDictionary(s => s.SkillName);
		foreach (var row in skills)
		{
			if (capturedBySkill.TryGetValue(row.SkillName, out var captured))
			{
				row.Apply(
					captured.Level,
					captured.Xp,
					captured.XpGained,
					captured.XpPerHour,
					captured.XpToNext,
					captured.Eta);
			}
		}

		var activeSkillCount = skills.Count(s => s.IsActive);
		var totalXpGained = skills.Sum(s => s.XpGained);
		var xpStatus = capture.Skills.Count == skills.Count
			? activeSkillCount == 0
				? "XP tracker ready; no gains this session"
				: $"{activeSkillCount} active skill{(activeSkillCount == 1 ? string.Empty : "s")}"
			: capture.XpStatus;

		var itemsStatus = capture.Items == null
			? capture.ItemsStatus
			: itemTracker.ActiveCount == 0
				? "Item tracker ready; no item flow yet"
				: $"{itemTracker.ActiveCount} item{(itemTracker.ActiveCount == 1 ? string.Empty : "s")} tracked";

		return new DashboardRefreshSnapshot(
			xpStatus,
			activeSkillCount,
			totalXpGained,
			itemsStatus,
			itemTracker.TotalGpPerHour,
			itemTracker.ActiveCount,
			capture.SkillSession,
			capture.SessionLabel);
	}

	private static string CaptureItems(ItemTracker itemTracker, out ItemTrackerSnapshot? snapshot)
	{
		return itemTracker.TryCapture(out snapshot, out var error)
			? "Item tracker ready"
			: $"Item tracker failed: {error}";
	}

	private static string CaptureSkills(
		IReadOnlyCollection<SkillName> skillNames,
		ref SkillSession? skillSession,
		out IReadOnlyList<DashboardSkillRefresh> snapshots)
	{
		snapshots = Array.Empty<DashboardSkillRefresh>();

		try
		{
			skillSession ??= new SkillSession();
		}
		catch (Exception ex)
		{
			skillSession = null;
			return $"XP tracker unavailable: {ex.Message}";
		}

		var captured = new List<DashboardSkillRefresh>(skillNames.Count);
		try
		{
			foreach (var skillName in skillNames)
			{
				var snapshot = Skills.Get(skillName);
				var eta = skillSession.GetTimeToNextLevel(skillName, snapshot);
				captured.Add(new DashboardSkillRefresh(
					skillName,
					snapshot.CurrentLevel,
					snapshot.Xp,
					skillSession.GetXpGained(skillName, snapshot.Xp),
					skillSession.GetXpPerHour(skillName, snapshot.Xp),
					Skills.GetXpToNextLevel(snapshot),
					eta == TimeSpan.MaxValue ? "--" : eta.ToString(@"hh\:mm\:ss")));
			}
		}
		catch (Exception ex)
		{
			snapshots = captured;
			return $"XP refresh failed: {ex.Message}";
		}

		snapshots = captured;
		var activeSkillCount = captured.Count(s => s.XpGained > 0);
		return activeSkillCount == 0
			? "XP tracker ready; no gains this session"
			: $"{activeSkillCount} active skill{(activeSkillCount == 1 ? string.Empty : "s")}";
	}
}
