using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MESharp.API;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using SharpBuilder.Editor.Wpf.Services;

namespace SharpBuilder.Editor.Wpf.ViewModels;

/// <summary>
/// Session dashboard for one editor canvas: run/uptime clocks, XP and item trackers, and the
/// game-session readout. Owns the 1 Hz refresh timer; all game-backed reads go through the shared
/// game-API scheduler so they never overlap an executing node. Extracted from
/// <see cref="NodeEditorViewModel"/> so the editor VM stays focused on graph editing and runs.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
	private readonly NodeEditorViewModel _editor;
	private readonly DashboardRefreshService _refreshService = new();
	private readonly ItemTracker _itemTracker;
	private readonly DispatcherTimer _timer;
	private readonly DateTime _builderOpenedUtc = DateTime.UtcNow;
	private DateTime _startedUtc = DateTime.UtcNow;
	private SkillSession? _skillSession;
	private bool _gameRefreshInProgress;
	private bool _gameRefreshPending;
	private int _refreshVersion;
	// Graph runtime is accumulated across runs; while running we add the live span on top.
	private TimeSpan _graphRunAccumulated = TimeSpan.Zero;
	private DateTime? _graphRunStartedUtc;

	[ObservableProperty] private string _runtime = "00:00:00";
	[ObservableProperty] private string _uptime = "00:00:00";
	[ObservableProperty] private string _currentNode = "None";
	[ObservableProperty] private string _runMode = "Idle";
	[ObservableProperty] private string _graphSummary = "0 nodes / 0 transitions";
	[ObservableProperty] private string _signalSummary = "0 signals";
	[ObservableProperty] private string _xpStatus = "XP tracker idle";

	/// <summary>Account/session the dashboard is reading from (player name, logged-out, or design mode).</summary>
	[ObservableProperty] private string _session = "No session yet";

	[ObservableProperty] private string _lastUpdated = "--";
	[ObservableProperty] private int _activeSkillCount;
	[ObservableProperty] private int _totalXpGained;
	[ObservableProperty] private string _itemsStatus = "Item tracker idle";
	[ObservableProperty] private long _itemsGpPerHour;
	[ObservableProperty] private int _itemsActiveCount;

	internal DashboardViewModel(NodeEditorViewModel editor)
	{
		_editor = editor ?? throw new ArgumentNullException(nameof(editor));

		Skills = new ObservableCollection<DashboardSkillRow>(
			Enum.GetValues<SkillName>().Select(skill => new DashboardSkillRow(skill)));
		SkillsView = CollectionViewSource.GetDefaultView(Skills);
		SkillsView.Filter = FilterSkill;

		_itemTracker = new ItemTracker(_startedUtc);
		ItemsView = new CollectionViewSource { Source = _itemTracker.Rows }.View;
		ItemsView.Filter = FilterItem;

		ResetTimerCommand = new RelayCommand(ResetGraphRuntime);
		ResetXpCommand = new RelayCommand(ResetXpTracker);
		ResetItemsCommand = new RelayCommand(ResetItemTracker);

		_timer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromSeconds(1)
		};
		_timer.Tick += (_, _) =>
		{
			Refresh();
			BeginGameRefresh();
		};
		_timer.Start();
	}

	public ObservableCollection<DashboardSkillRow> Skills { get; }
	public ICollectionView SkillsView { get; }
	public ICollectionView ItemsView { get; }

	public IRelayCommand ResetTimerCommand { get; }
	public IRelayCommand ResetXpCommand { get; }
	public IRelayCommand ResetItemsCommand { get; }

	private bool _xpActiveOnly;
	/// <summary>When true the XP table shows only skills that have gained XP this session.</summary>
	public bool XpActiveOnly
	{
		get => _xpActiveOnly;
		set
		{
			if (!SetProperty(ref _xpActiveOnly, value))
				return;
			SkillsView.Refresh();
		}
	}

	private bool _itemsActiveOnly = true;
	/// <summary>When true the Items table shows only items that have moved this session.</summary>
	public bool ItemsActiveOnly
	{
		get => _itemsActiveOnly;
		set
		{
			if (!SetProperty(ref _itemsActiveOnly, value))
				return;
			ItemsView.Refresh();
		}
	}

	/// <summary>Total wall-clock time the graph has spent running this session (live while running).</summary>
	private TimeSpan GraphRunElapsed =>
		_graphRunAccumulated + (_graphRunStartedUtc is { } s ? DateTime.UtcNow - s : TimeSpan.Zero);

	/// <summary>Starts or folds the graph-runtime clock; called by the editor when a run starts/stops.</summary>
	internal void OnRunStateChanged(bool isRunning)
	{
		if (isRunning)
		{
			_graphRunStartedUtc = DateTime.UtcNow;
		}
		else if (_graphRunStartedUtc is { } startedUtc)
		{
			_graphRunAccumulated += DateTime.UtcNow - startedUtc;
			_graphRunStartedUtc = null;
		}
	}

	/// <summary>
	/// Updates only the cheap, in-process dashboard fields (clocks, current node, graph/signal
	/// summaries). It never touches the game client — all game-backed reads go through
	/// <see cref="BeginGameRefresh"/>, which runs under the game-API scheduler.
	/// </summary>
	internal void Refresh()
	{
		var script = _editor.Script;
		if (script == null)
			return;

		Runtime = GraphRunElapsed.ToString(@"hh\:mm\:ss");
		Uptime = (DateTime.UtcNow - _builderOpenedUtc).ToString(@"hh\:mm\:ss");
		CurrentNode = _editor.CurrentRunNodeTitle ?? "None";
		RunMode = _editor.IsRunning ? (_editor.CurrentRunLooping ? "Running loop" : "Running once") : _editor.Status;
		GraphSummary = $"{script.Nodes.Count} nodes / {_editor.AllTransitions.Count()} transitions";
		SignalSummary = $"{_editor.Signals.Count} signal{(_editor.Signals.Count == 1 ? string.Empty : "s")}";
		LastUpdated = DateTime.Now.ToString("HH:mm:ss");

		SkillsView.Refresh();
	}

	internal void BeginGameRefresh()
	{
		if (_gameRefreshInProgress)
		{
			_gameRefreshPending = true;
			return;
		}

		_gameRefreshPending = false;
		_gameRefreshInProgress = true;
		var version = _refreshVersion;
		var skillNames = Skills.Select(s => s.SkillName).ToArray();
		var skillSession = _skillSession;
		var gameApiAvailable = GameRuntime.IsGameApiAvailable;

		// Capture runs on the shared game-API lane so it can never overlap an executor node.
		_ = Task.Run(() => GameApi.Scheduler.RunAsync(() => _refreshService.Capture(
				gameApiAvailable,
				skillNames,
				_itemTracker,
				skillSession)))
			.ContinueWith(task =>
			{
				NodeEditorViewModel.RunOnUi(() =>
				{
					_gameRefreshInProgress = false;

					if (version != _refreshVersion)
						return;

					if (task.IsFaulted)
					{
						var message = task.Exception?.GetBaseException().Message ?? "unknown error";
						XpStatus = $"XP refresh failed: {message}";
						ItemsStatus = $"Item tracker failed: {message}";
						return;
					}

					var snapshot = _refreshService.Apply(task.Result, Skills, _itemTracker);
					ApplySnapshot(snapshot);

					if (_gameRefreshPending)
					{
						BeginGameRefresh();
					}
				});
			});
	}

	private void ApplySnapshot(DashboardRefreshSnapshot snapshot)
	{
		_skillSession = snapshot.SkillSession;
		XpStatus = snapshot.XpStatus;
		ActiveSkillCount = snapshot.ActiveSkillCount;
		TotalXpGained = snapshot.TotalXpGained;
		ItemsStatus = snapshot.ItemsStatus;
		ItemsGpPerHour = snapshot.ItemsGpPerHour;
		ItemsActiveCount = snapshot.ItemsActiveCount;
		Session = snapshot.SessionLabel;
		SkillsView.Refresh();
		ItemsView.Refresh();
	}

	/// <summary>Zeroes the graph runtime clock (restarting it from now if a run is in progress).</summary>
	private void ResetGraphRuntime()
	{
		_graphRunAccumulated = TimeSpan.Zero;
		_graphRunStartedUtc = _editor.IsRunning ? DateTime.UtcNow : null;
		Refresh();
	}

	/// <summary>Re-baselines XP tracking so gained/XP-per-hour restart from the current totals.</summary>
	private void ResetXpTracker()
	{
		_refreshVersion++;
		_skillSession = null;
		Refresh();
		BeginGameRefresh();
	}

	/// <summary>Clears tracked items and re-baselines the item-flow session clock.</summary>
	private void ResetItemTracker()
	{
		_refreshVersion++;
		_startedUtc = DateTime.UtcNow;
		_itemTracker.Reset(_startedUtc);
		Refresh();
		BeginGameRefresh();
	}

	private bool FilterSkill(object obj)
	{
		if (obj is not DashboardSkillRow row)
			return false;

		var activeOnly = _xpActiveOnly || ShowOnlyActiveXpFromDashboardNode;
		return !activeOnly || row.IsActive;
	}

	private bool FilterItem(object obj)
	{
		if (obj is not DashboardItemRow row)
			return false;

		return !_itemsActiveOnly || row.IsActive;
	}

	private bool ShowOnlyActiveXpFromDashboardNode =>
		_editor.Script?.Nodes
			.Where(n => string.Equals(n.DefinitionId, NodeCatalogDefaults.ScriptDashboardId, StringComparison.OrdinalIgnoreCase))
			.SelectMany(n => n.Parameters)
			.FirstOrDefault(p => string.Equals(p.Key, "showOnlyActiveXp", StringComparison.OrdinalIgnoreCase))
			?.BoolValue == true;

	public void Dispose() => _timer.Stop();
}
