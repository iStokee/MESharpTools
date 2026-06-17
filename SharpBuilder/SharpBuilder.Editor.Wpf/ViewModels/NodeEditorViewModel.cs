using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using MESharp.API;
using Microsoft.Win32;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using SharpBuilder.Editor.Wpf.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace SharpBuilder.Editor.Wpf.ViewModels;

/// <summary>
/// Backing view model for the SharpBuilder node editor. Handles script persistence, runtime execution,
/// and light-weight visual state (selection, active trail).
/// </summary>
public partial class NodeEditorViewModel : INotifyPropertyChanged, IDisposable
{
	private readonly GraphScriptService _scriptService;
	private readonly GraphExecutionEngine _engine;
	private readonly NodeCatalogService _catalogService;
	private readonly GraphValidator _validator;
	private readonly GraphConnectionRules _connectionRules = new();
	private readonly GraphEditHistory _editHistory = new();
	private readonly GraphExplainService _explainService;
	private readonly CaptureCalibrationService _captureCalibrationService = new();
	private readonly DashboardRefreshService _dashboardRefreshService = new();
	private NodeModel? _currentRunNode;
	private CancellationTokenSource? _runCts;
	private Task? _runTask;
	private GraphModel _script;
	private NodeModel? _selectedNode;
	private NodeDefinition? _selectedNodeDefinition;
	private TransitionModel? _selectedTransition;
	private readonly ObservableCollection<NodeModel> _selectedNodes = new();
	private bool _isRunning;
	private bool _isLooping = true;
	private string _status = "Idle";
	/// <summary>
	/// Width (in DIPs) of a side panel when collapsed to its vertical rail.
	/// Must match the column MinWidth used in NodeEditorControl.xaml.
	/// </summary>
	public const double CollapsedRailWidth = 56;

	private string? _currentFilePath;
	private NodeCategory? _selectedCategory;
	private string _catalogSearchText = string.Empty;
	private CatalogSortMode _catalogSortMode = CatalogSortMode.Category;
	private bool _showAdvancedNodes;
	private bool _isLeftCollapsed;
	private bool _isRightCollapsed;
	private double _expandedLeftWidth = 320;
	private double _expandedRightWidth = 360;
	private GridLength _leftColumnWidth = new(320);
	private GridLength _rightColumnWidth = new(360);
	private bool _isNodeInfoOpen;
	private string _nodeInfoTitle = "Node Info";
	private string _nodeInfoDescription = string.Empty;
	private bool _isDirty;
	private bool _suppressDirty;
	private bool _transitionsRefreshQueued;
	private IDisposable? _activeGraphEditBatch;
	private readonly Services.WindowSizeOption _customWindowSize = new("Custom", 1400, 900, isCustom: true);
	private Services.WindowSizeOption? _selectedWindowSize;
	private bool _suppressWindowSizeRequest;
	private bool _isSettingsOpen;
	private bool _isGraphExplanationOpen;
	private string _graphExplanationSummary = "Graph not explained yet";
	private readonly DateTime _builderOpenedUtc = DateTime.UtcNow;
	private DateTime _dashboardStartedUtc = DateTime.UtcNow;
	// Graph runtime is accumulated across runs; while running we add the live span on top.
	private TimeSpan _graphRunAccumulated = TimeSpan.Zero;
	private DateTime? _graphRunStartedUtc;
	private readonly DispatcherTimer _dashboardTimer;
	private SkillSession? _dashboardSkillSession;
	private readonly ItemTracker _itemTracker;
	private bool _dashboardXpActiveOnly;
	private bool _dashboardItemsActiveOnly = true;
	private string _dashboardItemsStatus = "Item tracker idle";
	private long _dashboardItemsGpPerHour;
	private int _dashboardItemsActiveCount;
	private string _dashboardRuntime = "00:00:00";
	private string _dashboardUptime = "00:00:00";
	private string _dashboardCurrentNode = "None";
	private string _dashboardRunMode = "Idle";
	private string _dashboardGraphSummary = "0 nodes / 0 transitions";
	private string _dashboardSignalSummary = "0 signals";
	private string _dashboardXpStatus = "XP tracker idle";
	private string _dashboardLastUpdated = "--";
	private int _dashboardActiveSkillCount;
	private int _dashboardTotalXpGained;

	public NodeEditorViewModel()
		: this(new NodeCatalogService())
	{
	}

	private NodeEditorViewModel(NodeCatalogService catalogService)
		: this(new GraphScriptService(catalogService), new GraphExecutionEngine(catalogService, new NodeExecutorRegistry()), catalogService)
	{
	}

	public NodeEditorViewModel(
		GraphScriptService scriptService,
		GraphExecutionEngine engine,
		NodeCatalogService catalogService)
	{
		_scriptService = scriptService ?? throw new ArgumentNullException(nameof(scriptService));
		_engine = engine ?? throw new ArgumentNullException(nameof(engine));
		_catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
		_validator = new GraphValidator(catalogService);
		_explainService = new GraphExplainService(catalogService);

		Categories = new ObservableCollection<NodeCategory>(_catalogService.Categories);
		Definitions = new ObservableCollection<NodeDefinition>(_catalogService.Definitions);
		_selectedCategory = Categories.FirstOrDefault();

		WindowSizeOptions = new List<Services.WindowSizeOption>
		{
			new("Compact", 1280, 800),
			new("Standard", 1440, 900),
			new("Large", 1680, 1050),
			new("Full HD", 1920, 1080),
			_customWindowSize
		};
		_selectedWindowSize = _customWindowSize;
		SelectedNodes = new ReadOnlyObservableCollection<NodeModel>(_selectedNodes);

		AddNodeCommand = new RelayCommand(AddNode);
		CreateNodeFromDefinitionCommand = new RelayCommand<NodeDefinition?>(AddNodeFromDefinition);
		RemoveNodeCommand = new RelayCommand(RemoveSelectedNode, () => SelectedNode != null);
		AddTransitionCommand = new RelayCommand(AddTransition, () => SelectedNode != null && Script.Nodes.Count > 1);
		RemoveTransitionCommand = new RelayCommand<TransitionModel?>(RemoveTransition, _ => SelectedNode != null);
		MoveTransitionUpCommand = new RelayCommand<TransitionModel?>(t => MoveTransition(t, -1), _ => SelectedNode != null);
		MoveTransitionDownCommand = new RelayCommand<TransitionModel?>(t => MoveTransition(t, 1), _ => SelectedNode != null);
		SetAsStartCommand = new RelayCommand(SetSelectedAsStart, () => SelectedNode != null);
		ClearTrailCommand = new RelayCommand(ClearTrail);
		DeleteSelectedCommand = new RelayCommand(DeleteSelection);
		UndoCommand = new RelayCommand(UndoGraphEdit, () => _editHistory.CanUndo);
		RedoCommand = new RelayCommand(RedoGraphEdit, () => _editHistory.CanRedo);
		ToggleLeftPanelCommand = new RelayCommand(() => IsLeftCollapsed = !IsLeftCollapsed);
		ToggleRightPanelCommand = new RelayCommand(() => IsRightCollapsed = !IsRightCollapsed);

		NewScriptCommand = new RelayCommand(CreateBlankScript);
		LoadScriptCommand = new AsyncRelayCommand(LoadScriptAsync);
		SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync);
		ExportScriptCommand = new AsyncRelayCommand(ExportScriptAsync);
		LoadTemplateCommand = new RelayCommand(LoadTemplate);

		StartCommand = new AsyncRelayCommand(async () => await StartRunAsync(_isLooping));
		StepCommand = new AsyncRelayCommand(async () => await StartRunAsync(false));
		StopCommand = new RelayCommand(StopRun, () => IsRunning);
		ExplainGraphCommand = new RelayCommand(ExplainGraph);
		AddListItemCommand = new RelayCommand<NodeParamBinding?>(AddListEntry);
		RemoveListItemCommand = new RelayCommand<(NodeParamBinding binding, string value)?>(RemoveListEntry);
		CloseNodeInfoCommand = new RelayCommand(() => IsNodeInfoOpen = false);
		ShowDefinitionInfoCommand = new RelayCommand<NodeDefinition?>(ShowDefinitionInfo);
		CaptureFromGameCommand = new RelayCommand(CaptureFromGame, () => CanCaptureSelectedNode);
		ResetTimerCommand = new RelayCommand(ResetGraphRuntime);
		ResetXpCommand = new RelayCommand(ResetXpTracker);
		ResetItemsCommand = new RelayCommand(ResetItemTracker);
		_editHistory.Changed += (_, _) =>
		{
			OnPropertyChanged(nameof(CanUndo));
			OnPropertyChanged(nameof(CanRedo));
			UndoCommand.NotifyCanExecuteChanged();
			RedoCommand.NotifyCanExecuteChanged();
		};

		// Turn on ME's native DoAction capture + drain pump so "Capture from game" can read
		// real in-game clicks. Refcounted and no-throw when the game bridge is absent.
		try { DoActionDebugSignals.Configure(enabled: true); DoActionDebugSignals.StartNativePump(); } catch { /* no session */ }

		_engine.NodeEntered += OnNodeEntered;
		_engine.NodeCompleted += OnNodeCompleted;
		_engine.TransitionTaken += OnTransitionTaken;
		_engine.Completed += OnEngineCompleted;
		_engine.Faulted += OnEngineFaulted;

		DashboardSkills = new ObservableCollection<DashboardSkillRow>(
			Enum.GetValues<SkillName>().Select(skill => new DashboardSkillRow(skill)));
		DashboardSkillsView = CollectionViewSource.GetDefaultView(DashboardSkills);
		DashboardSkillsView.Filter = FilterDashboardSkill;

		_itemTracker = new ItemTracker(_dashboardStartedUtc);
		DashboardItemsView = new CollectionViewSource { Source = _itemTracker.Rows }.View;
		DashboardItemsView.Filter = FilterDashboardItem;

		_dashboardTimer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromSeconds(2)
		};
		_dashboardTimer.Tick += (_, _) => RefreshDashboard();
		_dashboardTimer.Start();

		_script = _scriptService.CreatePowerFishingTemplate();
		AttachScript(_script);
		_editHistory.Clear();
		RefreshSignals();
		RefreshDashboard();

		// Seed initial panel collapse state from the persisted startup preferences.
		if (Services.EditorPreferences.StartLeftCollapsed)
			IsLeftCollapsed = true;
		if (Services.EditorPreferences.StartRightCollapsed)
			IsRightCollapsed = true;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public ObservableCollection<RuntimeSignal> Signals { get; } = new();
	public ObservableCollection<string> SignalSuggestions { get; } = new();
	public ObservableCollection<string> GraphExplanationLines { get; } = new();
	public ObservableCollection<NodeCategory> Categories { get; }
	public ObservableCollection<NodeDefinition> Definitions { get; }
	public ObservableCollection<NodeParamBinding> ParameterBindings { get; } = new();
	public ObservableCollection<NodeParamBinding> AdvancedParameterBindings { get; } = new();
	public bool HasAdvancedParameters => AdvancedParameterBindings.Count > 0;
	public ObservableCollection<string> NodeInfoUsageTips { get; } = new();
	public ObservableCollection<DashboardSkillRow> DashboardSkills { get; }
	public ICollectionView DashboardSkillsView { get; }
	public ReadOnlyObservableCollection<NodeModel> SelectedNodes { get; }
	// Keep collapse width large enough to show the toggle affordance plus padding.
	public GridLength LeftColumnWidth
	{
		get => _leftColumnWidth;
		set
		{
			if (_leftColumnWidth.Equals(value))
				return;

			_leftColumnWidth = value;
			if (!_isLeftCollapsed)
			{
				_expandedLeftWidth = ExtractWidth(value, _expandedLeftWidth);
			}

			OnPropertyChanged();
		}
	}

	public GridLength RightColumnWidth
	{
		get => _rightColumnWidth;
		set
		{
			if (_rightColumnWidth.Equals(value))
				return;

			_rightColumnWidth = value;
			if (!_isRightCollapsed)
			{
				_expandedRightWidth = ExtractWidth(value, _expandedRightWidth);
			}

			OnPropertyChanged();
		}
	}

	public IEnumerable<TransitionModel> AllTransitions =>
		Script.Nodes.SelectMany(n => n.Transitions);

	public GraphModel Script
	{
		get => _script;
		private set
		{
			if (ReferenceEquals(_script, value))
				return;

			DetachScript();
			_script = value;
			AttachScript(_script);
			OnPropertyChanged();
			OnPropertyChanged(nameof(AllTransitions));
		}
	}

	public NodeModel? SelectedNode
	{
		get => _selectedNode;
		set
		{
			if (ReferenceEquals(_selectedNode, value))
				return;

			UpdatePrimarySelection(value);

			SelectedNodeDefinition = _selectedNode == null
				? null
				: _catalogService.GetDefinition(_selectedNode.DefinitionId);

			OnPropertyChanged();
			OnPropertyChanged(nameof(CanEditNode));
			OnPropertyChanged(nameof(CanEditTransitions));
			OnPropertyChanged(nameof(CanCaptureSelectedNode));
			RemoveNodeCommand.NotifyCanExecuteChanged();
			AddTransitionCommand.NotifyCanExecuteChanged();
			SetAsStartCommand.NotifyCanExecuteChanged();
			RemoveTransitionCommand.NotifyCanExecuteChanged();
			CaptureFromGameCommand.NotifyCanExecuteChanged();
			CaptureStatus = "Click the target in-game, then Capture to fill this node.";
			CaptureDriftState = "none";
			RefreshParameterBindings();
		}
	}

	public NodeDefinition? SelectedNodeDefinition
	{
		get => _selectedNodeDefinition;
		set
		{
			_selectedNodeDefinition = value;
			if (SelectedNode != null && value != null &&
			    !string.Equals(SelectedNode.DefinitionId, value.Id, StringComparison.OrdinalIgnoreCase))
			{
				ApplyDefinitionToNode(SelectedNode, value);
			}

			OnPropertyChanged();
			RefreshParameterBindings();
		}
	}

	public TransitionModel? SelectedTransition
	{
		get => _selectedTransition;
		set
		{
			if (ReferenceEquals(_selectedTransition, value))
				return;

			_selectedTransition = value;
			OnPropertyChanged();
			RemoveTransitionCommand.NotifyCanExecuteChanged();
		}
	}

	public bool IsRunning
	{
		get => _isRunning;
		private set
		{
			if (_isRunning == value) return;
			_isRunning = value;
			if (value)
			{
				// Start (or resume) the graph runtime clock.
				_graphRunStartedUtc = DateTime.UtcNow;
			}
			else if (_graphRunStartedUtc is { } startedUtc)
			{
				// Fold the just-finished span into the running total.
				_graphRunAccumulated += DateTime.UtcNow - startedUtc;
				_graphRunStartedUtc = null;
			}
			OnPropertyChanged();
			StopCommand.NotifyCanExecuteChanged();
		}
	}

	/// <summary>Total wall-clock time the graph has spent running this session (live while running).</summary>
	private TimeSpan GraphRunElapsed =>
		_graphRunAccumulated + (_graphRunStartedUtc is { } s ? DateTime.UtcNow - s : TimeSpan.Zero);

	public bool IsLooping
	{
		get => _isLooping;
		set
		{
			if (_isLooping == value) return;
			_isLooping = value;
			OnPropertyChanged();
		}
	}

	public string Status
	{
		get => _status;
		set
		{
			if (_status == value) return;
			_status = value;
			OnPropertyChanged();
		}
	}

	public string DashboardRuntime
	{
		get => _dashboardRuntime;
		private set
		{
			if (_dashboardRuntime == value) return;
			_dashboardRuntime = value;
			OnPropertyChanged();
		}
	}

	public string DashboardUptime
	{
		get => _dashboardUptime;
		private set
		{
			if (_dashboardUptime == value) return;
			_dashboardUptime = value;
			OnPropertyChanged();
		}
	}

	public string DashboardCurrentNode
	{
		get => _dashboardCurrentNode;
		private set
		{
			if (_dashboardCurrentNode == value) return;
			_dashboardCurrentNode = value;
			OnPropertyChanged();
		}
	}

	public string DashboardRunMode
	{
		get => _dashboardRunMode;
		private set
		{
			if (_dashboardRunMode == value) return;
			_dashboardRunMode = value;
			OnPropertyChanged();
		}
	}

	public string DashboardGraphSummary
	{
		get => _dashboardGraphSummary;
		private set
		{
			if (_dashboardGraphSummary == value) return;
			_dashboardGraphSummary = value;
			OnPropertyChanged();
		}
	}

	public string DashboardSignalSummary
	{
		get => _dashboardSignalSummary;
		private set
		{
			if (_dashboardSignalSummary == value) return;
			_dashboardSignalSummary = value;
			OnPropertyChanged();
		}
	}

	public string DashboardXpStatus
	{
		get => _dashboardXpStatus;
		private set
		{
			if (_dashboardXpStatus == value) return;
			_dashboardXpStatus = value;
			OnPropertyChanged();
		}
	}

	public string DashboardLastUpdated
	{
		get => _dashboardLastUpdated;
		private set
		{
			if (_dashboardLastUpdated == value) return;
			_dashboardLastUpdated = value;
			OnPropertyChanged();
		}
	}

	public int DashboardActiveSkillCount
	{
		get => _dashboardActiveSkillCount;
		private set
		{
			if (_dashboardActiveSkillCount == value) return;
			_dashboardActiveSkillCount = value;
			OnPropertyChanged();
		}
	}

	public int DashboardTotalXpGained
	{
		get => _dashboardTotalXpGained;
		private set
		{
			if (_dashboardTotalXpGained == value) return;
			_dashboardTotalXpGained = value;
			OnPropertyChanged();
		}
	}

	/// <summary>When true the XP table shows only skills that have gained XP this session.</summary>
	public bool DashboardXpActiveOnly
	{
		get => _dashboardXpActiveOnly;
		set
		{
			if (_dashboardXpActiveOnly == value) return;
			_dashboardXpActiveOnly = value;
			OnPropertyChanged();
			DashboardSkillsView.Refresh();
		}
	}

	// --- Items dashboard ---

	public ICollectionView DashboardItemsView { get; }

	/// <summary>When true the Items table shows only items that have moved this session.</summary>
	public bool DashboardItemsActiveOnly
	{
		get => _dashboardItemsActiveOnly;
		set
		{
			if (_dashboardItemsActiveOnly == value) return;
			_dashboardItemsActiveOnly = value;
			OnPropertyChanged();
			DashboardItemsView.Refresh();
		}
	}

	public string DashboardItemsStatus
	{
		get => _dashboardItemsStatus;
		private set
		{
			if (_dashboardItemsStatus == value) return;
			_dashboardItemsStatus = value;
			OnPropertyChanged();
		}
	}

	public long DashboardItemsGpPerHour
	{
		get => _dashboardItemsGpPerHour;
		private set
		{
			if (_dashboardItemsGpPerHour == value) return;
			_dashboardItemsGpPerHour = value;
			OnPropertyChanged();
		}
	}

	public int DashboardItemsActiveCount
	{
		get => _dashboardItemsActiveCount;
		private set
		{
			if (_dashboardItemsActiveCount == value) return;
			_dashboardItemsActiveCount = value;
			OnPropertyChanged();
		}
	}

	public string? CurrentFilePath
	{
		get => _currentFilePath;
		private set
		{
			_currentFilePath = value;
			OnPropertyChanged();
		}
	}

	// --- Studio window settings (gear menu) ---

	/// <summary>Raised when the user picks a window-size preset so the host window can apply it.</summary>
	public event Action<double, double>? WindowSizeRequested;

	public IReadOnlyList<Services.WindowSizeOption> WindowSizeOptions { get; }

	public Services.WindowSizeOption? SelectedWindowSize
	{
		get => _selectedWindowSize;
		set
		{
			if (ReferenceEquals(_selectedWindowSize, value))
				return;

			_selectedWindowSize = value;
			OnPropertyChanged();

			if (value != null && !_suppressWindowSizeRequest)
			{
				WindowSizeRequested?.Invoke(value.Width, value.Height);
			}
		}
	}

	public bool IsSettingsOpen
	{
		get => _isSettingsOpen;
		set
		{
			if (_isSettingsOpen == value) return;
			_isSettingsOpen = value;
			OnPropertyChanged();
		}
	}

	// --- Startup / mini-map preferences (gear menu). Backed by the process-wide store so they
	//     persist across sessions and seed every new canvas. ---

	public bool StartLeftCollapsed
	{
		get => Services.EditorPreferences.StartLeftCollapsed;
		set
		{
			if (Services.EditorPreferences.StartLeftCollapsed == value) return;
			Services.EditorPreferences.StartLeftCollapsed = value;
			OnPropertyChanged();
		}
	}

	public bool StartRightCollapsed
	{
		get => Services.EditorPreferences.StartRightCollapsed;
		set
		{
			if (Services.EditorPreferences.StartRightCollapsed == value) return;
			Services.EditorPreferences.StartRightCollapsed = value;
			OnPropertyChanged();
		}
	}

	/// <summary>When true the mini-map is always shown; when false it auto-hides after panning.</summary>
	public bool MiniMapAlwaysVisible
	{
		get => Services.EditorPreferences.MiniMapAlwaysVisible;
		set
		{
			if (Services.EditorPreferences.MiniMapAlwaysVisible == value) return;
			Services.EditorPreferences.MiniMapAlwaysVisible = value;
			OnPropertyChanged();
		}
	}

	/// <summary>
	/// Reports the window's live size so the "Custom" option mirrors manual resizes. Snaps the
	/// selection to a matching preset (or the custom entry) without re-requesting a resize, which
	/// would otherwise feed back into the window.
	/// </summary>
	public void SetCurrentWindowSize(double width, double height)
	{
		if (width <= 0 || height <= 0)
			return;

		_customWindowSize.Width = Math.Round(width);
		_customWindowSize.Height = Math.Round(height);

		var match = WindowSizeOptions.FirstOrDefault(o =>
			!o.IsCustom && Math.Abs(o.Width - _customWindowSize.Width) < 1 && Math.Abs(o.Height - _customWindowSize.Height) < 1)
			?? _customWindowSize;

		_suppressWindowSizeRequest = true;
		SelectedWindowSize = match;
		_suppressWindowSizeRequest = false;
	}

	public NodeCategory? SelectedCategory
	{
		get => _selectedCategory;
		set
		{
			_selectedCategory = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(FilteredDefinitions));
		}
	}

	/// <summary>Free-text catalog filter; matches a node's title, description, id, and category.</summary>
	public string CatalogSearchText
	{
		get => _catalogSearchText;
		set
		{
			if (string.Equals(_catalogSearchText, value, StringComparison.Ordinal))
				return;

			_catalogSearchText = value ?? string.Empty;
			OnPropertyChanged();
			OnPropertyChanged(nameof(FilteredDefinitions));
		}
	}

	/// <summary>Sort options offered in the catalog header dropdown.</summary>
	public IReadOnlyList<CatalogSortMode> CatalogSortModes { get; } =
		new[] { CatalogSortMode.Category, CatalogSortMode.NameAscending, CatalogSortMode.NameDescending };

	public CatalogSortMode CatalogSortMode
	{
		get => _catalogSortMode;
		set
		{
			if (_catalogSortMode == value)
				return;

			_catalogSortMode = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(FilteredDefinitions));
		}
	}

	public bool ShowAdvancedNodes
	{
		get => _showAdvancedNodes;
		set
		{
			if (_showAdvancedNodes == value)
				return;

			_showAdvancedNodes = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(FilteredDefinitions));
		}
	}

	public IEnumerable<NodeDefinition> FilteredDefinitions
	{
		get
		{
			var source = (SelectedCategory == null || string.Equals(SelectedCategory.Id, "all", StringComparison.OrdinalIgnoreCase)
					? Definitions
					: Definitions.Where(d => string.Equals(d.CategoryId, SelectedCategory.Id, StringComparison.OrdinalIgnoreCase)))
				.Where(d => d.IsImplemented)
				.Where(d => ShowAdvancedNodes || d.Maturity == NodeMaturity.Stable);

			var search = _catalogSearchText?.Trim();
			if (!string.IsNullOrEmpty(search))
			{
				source = source.Where(d =>
					d.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
					d.ShortDescription.Contains(search, StringComparison.OrdinalIgnoreCase) ||
					d.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
					d.CategoryId.Contains(search, StringComparison.OrdinalIgnoreCase));
			}

			return _catalogSortMode switch
			{
				CatalogSortMode.NameAscending => source.OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase),
				CatalogSortMode.NameDescending => source.OrderByDescending(d => d.Title, StringComparer.OrdinalIgnoreCase),
				// Category keeps the seeded order (category group, then in-category Order).
				_ => source.OrderBy(d => d.CategoryId, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Order)
			};
		}
	}

	/// <summary>
	/// Options for the transition trigger dropdown in the inspector.
	/// </summary>
	public IReadOnlyList<TransitionTrigger> TransitionTriggers { get; } =
		new[] { TransitionTrigger.Any, TransitionTrigger.OnSuccess, TransitionTrigger.OnFail };

	public bool IsLeftCollapsed
	{
		get => _isLeftCollapsed;
		set
		{
			if (_isLeftCollapsed == value) return;
			_isLeftCollapsed = value;

			if (_isLeftCollapsed)
			{
				_expandedLeftWidth = ExtractWidth(LeftColumnWidth, _expandedLeftWidth);
				LeftColumnWidth = new GridLength(CollapsedRailWidth);
			}
			else
			{
				LeftColumnWidth = new GridLength(Math.Max(220, _expandedLeftWidth));
			}

			OnPropertyChanged();
		}
	}

	public bool IsGraphExplanationOpen
	{
		get => _isGraphExplanationOpen;
		set
		{
			if (_isGraphExplanationOpen == value) return;
			_isGraphExplanationOpen = value;
			OnPropertyChanged();
		}
	}

	public string GraphExplanationSummary
	{
		get => _graphExplanationSummary;
		set
		{
			if (string.Equals(_graphExplanationSummary, value, StringComparison.Ordinal)) return;
			_graphExplanationSummary = value ?? string.Empty;
			OnPropertyChanged();
		}
	}

	public bool IsRightCollapsed
	{
		get => _isRightCollapsed;
		set
		{
			if (_isRightCollapsed == value) return;
			_isRightCollapsed = value;

			if (_isRightCollapsed)
			{
				_expandedRightWidth = ExtractWidth(RightColumnWidth, _expandedRightWidth);
				RightColumnWidth = new GridLength(CollapsedRailWidth);
			}
			else
			{
				RightColumnWidth = new GridLength(Math.Max(240, _expandedRightWidth));
			}

			OnPropertyChanged();
		}
	}

	public bool IsNodeInfoOpen
	{
		get => _isNodeInfoOpen;
		set
		{
			if (_isNodeInfoOpen == value) return;
			_isNodeInfoOpen = value;
			OnPropertyChanged();
		}
	}

	public string NodeInfoTitle
	{
		get => _nodeInfoTitle;
		private set
		{
			if (string.Equals(_nodeInfoTitle, value, StringComparison.Ordinal))
				return;

			_nodeInfoTitle = value;
			OnPropertyChanged();
		}
	}

	public string NodeInfoDescription
	{
		get => _nodeInfoDescription;
		private set
		{
			if (string.Equals(_nodeInfoDescription, value, StringComparison.Ordinal))
				return;

			_nodeInfoDescription = value;
			OnPropertyChanged();
		}
	}

	/// <summary>True when the graph has edits that are not on disk yet.</summary>
	public bool IsDirty
	{
		get => _isDirty;
		private set
		{
			if (_isDirty == value) return;
			_isDirty = value;
			OnPropertyChanged();
		}
	}

	/// <summary>True when the canvas has no nodes, used for the empty-state hint.</summary>
	public bool IsCanvasEmpty => Script.Nodes.Count == 0;

	public bool CanEditNode => SelectedNode != null;

	public bool CanEditTransitions => SelectedNode != null && Script.Nodes.Count > 1;

	public bool CanUndo => _editHistory.CanUndo;

	public bool CanRedo => _editHistory.CanRedo;

	// ─── In-game capture (calibrate an interaction node from a real DoAction click) ──────

	private string _captureStatus = "Click the target in-game, then Capture to fill this node.";
	/// <summary>Human-readable result of the last "Capture from game" press.</summary>
	public string CaptureStatus
	{
		get => _captureStatus;
		private set { if (!string.Equals(_captureStatus, value, StringComparison.Ordinal)) { _captureStatus = value; OnPropertyChanged(); } }
	}

	private string _captureDriftState = "none";
	/// <summary>"none" | "filled" | "match" | "drift" — drives the inspector badge color.</summary>
	public string CaptureDriftState
	{
		get => _captureDriftState;
		private set { if (!string.Equals(_captureDriftState, value, StringComparison.Ordinal)) { _captureDriftState = value; OnPropertyChanged(); } }
	}

	/// <summary>True for interaction node types (objects/NPCs/loot) that can be calibrated from a click.</summary>
	public bool CanCaptureSelectedNode
	{
		get => _captureCalibrationService.CanCapture(SelectedNodeDefinition);
	}

	private void CaptureFromGame()
	{
		var result = _captureCalibrationService.Capture(SelectedNode, SelectedNodeDefinition);
		CaptureStatus = result.Status;
		CaptureDriftState = result.DriftState;
	}

	public IRelayCommand AddNodeCommand { get; }
	public IRelayCommand<NodeDefinition?> CreateNodeFromDefinitionCommand { get; }
	public IRelayCommand RemoveNodeCommand { get; }
	public IRelayCommand AddTransitionCommand { get; }
	public IRelayCommand<TransitionModel?> RemoveTransitionCommand { get; }
	public IRelayCommand<TransitionModel?> MoveTransitionUpCommand { get; }
	public IRelayCommand<TransitionModel?> MoveTransitionDownCommand { get; }
	public IRelayCommand SetAsStartCommand { get; }
	public IRelayCommand ClearTrailCommand { get; }
	public IRelayCommand DeleteSelectedCommand { get; }
	public IRelayCommand UndoCommand { get; }
	public IRelayCommand RedoCommand { get; }
	public IRelayCommand ToggleLeftPanelCommand { get; }
	public IRelayCommand ToggleRightPanelCommand { get; }

	public IRelayCommand NewScriptCommand { get; }
	public IAsyncRelayCommand LoadScriptCommand { get; }
	public IAsyncRelayCommand SaveScriptCommand { get; }
	public IAsyncRelayCommand ExportScriptCommand { get; }
	public IRelayCommand LoadTemplateCommand { get; }

	public IAsyncRelayCommand StartCommand { get; }
	public IAsyncRelayCommand StepCommand { get; }
	public IRelayCommand StopCommand { get; }
	public IRelayCommand ExplainGraphCommand { get; }
	public IRelayCommand<NodeParamBinding?> AddListItemCommand { get; }
	public IRelayCommand<(NodeParamBinding binding, string value)?> RemoveListItemCommand { get; }
	public IRelayCommand CloseNodeInfoCommand { get; }
	public IRelayCommand<NodeDefinition?> ShowDefinitionInfoCommand { get; }
	public IRelayCommand CaptureFromGameCommand { get; }
	public IRelayCommand ResetTimerCommand { get; }
	public IRelayCommand ResetXpCommand { get; }
	public IRelayCommand ResetItemsCommand { get; }

	public void Dispose()
	{
		try { DoActionDebugSignals.StopNativePump(); } catch { /* no session */ }
		_dashboardTimer.Stop();
		_runCts?.Cancel();
		_runCts?.Dispose();
		_runCts = null;
		DetachScript();
		_engine.NodeEntered -= OnNodeEntered;
		_engine.NodeCompleted -= OnNodeCompleted;
		_engine.TransitionTaken -= OnTransitionTaken;
		_engine.Completed -= OnEngineCompleted;
		_engine.Faulted -= OnEngineFaulted;
	}

	public GraphConnectionRuleResult PreviewConnect(NodeModel? source, NodeModel? target)
		=> _connectionRules.CanConnect(Script, source, target);

	public GraphConnectionRuleResult PreviewRetarget(TransitionModel? transition, NodeModel? target)
		=> _connectionRules.CanRetarget(Script, transition, target);

	public void BeginGraphEditBatch(string label)
	{
		_activeGraphEditBatch ??= _editHistory.Batch(label, Script);
	}

	public void CommitGraphEditBatch()
	{
		if (_activeGraphEditBatch == null)
			return;

		_activeGraphEditBatch.Dispose();
		_activeGraphEditBatch = null;
		var beforeCanUndo = _editHistory.CanUndo;
		_editHistory.CommitBatch(Script);
		if (_editHistory.CanUndo != beforeCanUndo || IsDirty)
		{
			IsDirty = true;
		}
	}

	private void RecordGraphEdit(string label, GraphModel before)
	{
		if (_editHistory.IsApplying)
			return;

		var hadUndo = _editHistory.CanUndo;
		_editHistory.Record(label, before, Script);
		if (_editHistory.CanUndo || hadUndo)
		{
			IsDirty = true;
		}
	}

	private void UndoGraphEdit()
	{
		var graph = _editHistory.Undo(Script);
		if (graph == null)
			return;

		Script = graph;
		IsDirty = true;
		Status = "Undid graph edit";
	}

	private void RedoGraphEdit()
	{
		var graph = _editHistory.Redo();
		if (graph == null)
			return;

		Script = graph;
		IsDirty = true;
		Status = "Redid graph edit";
	}

	private void ExplainGraph()
	{
		var explanation = _explainService.Explain(Script, GameRuntime.IsGameApiAvailable);
		var presentation = GraphExplanationPresenter.Present(explanation);
		GraphExplanationLines.Clear();
		foreach (var line in presentation.Lines)
			GraphExplanationLines.Add(line);

		GraphExplanationSummary = presentation.Summary;
		IsGraphExplanationOpen = true;
		Status = $"Explained graph: {GraphExplanationSummary}";
	}

	private static double ExtractWidth(GridLength width, double fallback)
	{
		if (width.IsAbsolute && width.Value > 0)
			return width.Value;

		return fallback;
	}

	protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}

/// <summary>Ordering applied to the node catalog list.</summary>
public enum CatalogSortMode
{
	Category,
	NameAscending,
	NameDescending
}
