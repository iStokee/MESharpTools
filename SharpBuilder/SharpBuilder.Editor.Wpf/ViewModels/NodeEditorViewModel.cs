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
using CommunityToolkit.Mvvm.ComponentModel;
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
public partial class NodeEditorViewModel : ObservableObject, IDisposable
{
	private const int MaxRunTrailEntries = 32;

	private readonly GraphScriptService _scriptService;
	private readonly GraphExecutionEngine _engine;
	private readonly NodeCatalogService _catalogService;
	private readonly GraphValidator _validator;
	private readonly GraphConnectionRules _connectionRules = new();
	private readonly GraphEditHistory _editHistory = new();
	private readonly GraphExplainService _explainService;
	private readonly CaptureCalibrationService _captureCalibrationService = new();
	private readonly Queue<RunTrailEntry> _runTrail = new();
	private readonly Dictionary<Guid, int> _runTrailNodeCounts = new();
	private readonly Dictionary<Guid, int> _runTrailTransitionCounts = new();
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
	private bool _currentRunLooping;
	private string _status = "Idle";
	/// <summary>
	/// Width (in DIPs) of a side panel when collapsed to its vertical rail.
	/// Must match the column MinWidth used in NodeEditorControl.xaml.
	/// </summary>
	public const double CollapsedRailWidth = 56;

	/// <summary>
	/// Default expanded width of the catalog column: one node tile wide. The tile is 168 DIP plus its
	/// 8 DIP right margin (176), inside the catalog grid's 16+16 insets and a ~17 DIP scrollbar.
	/// </summary>
	private const double DefaultCatalogWidth = 240;

	private string? _currentFilePath;
	private NodeCategory? _selectedCategory;
	private string _catalogSearchText = string.Empty;
	private CatalogSortMode _catalogSortMode = CatalogSortMode.Category;
	private bool _showAdvancedNodes;
	private bool _isLeftCollapsed;
	private bool _isRightCollapsed;
	private double _expandedLeftWidth = DefaultCatalogWidth;
	private double _expandedRightWidth = 360;
	private GridLength _leftColumnWidth = new(DefaultCatalogWidth);
	private GridLength _rightColumnWidth = new(360);
	private bool _isNodeInfoOpen;
	private string _nodeInfoTitle = "Node Info";
	private string _nodeInfoDescription = string.Empty;
	private bool _isDirty;
	private bool _suppressDirty;
	private string? _activeGraphEditBatchLabel;
	// Snapshot of the graph at the last recorded history entry; every Record diffs against it.
	private GraphModel _shadowScript = null!;
	private bool _propertyEditPending;
	private DispatcherTimer? _propertyEditTimer;
	private readonly Services.WindowSizeOption _customWindowSize = new("Custom", 1400, 900, isCustom: true);
	private Services.WindowSizeOption? _selectedWindowSize;
	private bool _suppressWindowSizeRequest;
	private bool _isSettingsOpen;
	private bool _isGraphExplanationOpen;
	private bool _isValidationOpen;
	private bool _isRunLogOpen;
	private string _graphExplanationSummary = "Graph not explained yet";
	private bool _isReadOnly;
	private string? _readOnlyReason;

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

		AddNodeCommand = new RelayCommand(AddNode, CanModifyGraph);
		CreateNodeFromDefinitionCommand = new RelayCommand<NodeDefinition?>(AddNodeFromDefinition, _ => CanModifyGraph());
		RemoveNodeCommand = new RelayCommand(RemoveSelectedNode, () => CanModifyGraph() && SelectedNode != null);
		AddTransitionCommand = new RelayCommand(AddTransition, () => CanModifyGraph() && SelectedNode != null && Script.Nodes.Count > 1);
		RemoveTransitionCommand = new RelayCommand<TransitionModel?>(RemoveTransition, _ => CanModifyGraph() && SelectedNode != null);
		MoveTransitionUpCommand = new RelayCommand<TransitionModel?>(t => MoveTransition(t, -1), _ => CanModifyGraph() && SelectedNode != null);
		MoveTransitionDownCommand = new RelayCommand<TransitionModel?>(t => MoveTransition(t, 1), _ => CanModifyGraph() && SelectedNode != null);
		SetAsStartCommand = new RelayCommand(SetSelectedAsStart, () => CanModifyGraph() && SelectedNode != null);
		ClearTrailCommand = new RelayCommand(ClearTrail);
		DeleteSelectedCommand = new RelayCommand(DeleteSelection, CanModifyGraph);
		UndoCommand = new RelayCommand(UndoGraphEdit, () => CanModifyGraph() && _editHistory.CanUndo);
		RedoCommand = new RelayCommand(RedoGraphEdit, () => CanModifyGraph() && _editHistory.CanRedo);
		ToggleLeftPanelCommand = new RelayCommand(() => IsLeftCollapsed = !IsLeftCollapsed);
		ToggleRightPanelCommand = new RelayCommand(() => IsRightCollapsed = !IsRightCollapsed);

		NewScriptCommand = new RelayCommand(CreateBlankScript, CanModifyGraph);
		LoadScriptCommand = new AsyncRelayCommand(LoadScriptAsync, CanModifyGraph);
		SaveScriptCommand = new AsyncRelayCommand(SaveScriptAsync, CanModifyGraph);
		ExportScriptCommand = new AsyncRelayCommand(ExportScriptAsync, CanModifyGraph);
		LoadTemplateCommand = new RelayCommand(LoadTemplate, CanModifyGraph);

		StartCommand = new AsyncRelayCommand(async () => await StartRunAsync(_isLooping), CanModifyGraph);
		StepCommand = new AsyncRelayCommand(async () => await StartRunAsync(false), CanModifyGraph);
		StopCommand = new RelayCommand(StopRun, () => !IsReadOnly && IsRunning);
		ExplainGraphCommand = new RelayCommand(ExplainGraph);
		ClearRunLogCommand = new RelayCommand(() => RunLog.Clear());
		SelectValidationIssueCommand = new RelayCommand<ValidationIssue?>(SelectValidationIssue);
		AddListItemCommand = new RelayCommand<NodeParamBinding?>(AddListEntry);
		RemoveListItemCommand = new RelayCommand<(NodeParamBinding binding, string value)?>(RemoveListEntry);
		CloseNodeInfoCommand = new RelayCommand(() => IsNodeInfoOpen = false);
		ShowDefinitionInfoCommand = new RelayCommand<NodeDefinition?>(ShowDefinitionInfo);
		CaptureFromGameCommand = new RelayCommand(CaptureFromGame, () => CanModifyGraph() && CanCaptureSelectedNode);
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
		_engine.DiagnosticSink = OnEngineDiagnostic;


		Dashboard = new DashboardViewModel(this);

		_script = _scriptService.CreatePowerFishingTemplate();
		AttachScript(_script);
		_editHistory.Clear();
		RefreshSignals();
		Dashboard.Refresh();

		// Seed initial panel collapse state from the persisted startup preferences.
		if (Services.EditorPreferences.StartLeftCollapsed)
			IsLeftCollapsed = true;
		if (Services.EditorPreferences.StartRightCollapsed)
			IsRightCollapsed = true;
	}

	/// <summary>Session dashboard (clocks, XP/item trackers) for this canvas.</summary>
	public DashboardViewModel Dashboard { get; }

	/// <summary>True when this canvas mirrors a remote session and must not change its local graph copy.</summary>
	public bool IsReadOnly
	{
		get => _isReadOnly;
		private set
		{
			if (_isReadOnly == value)
				return;

			_isReadOnly = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsEditorEnabled));
			NotifyEditCommandStateChanged();
		}
	}

	/// <summary>Inverse read-only flag for the editor surface's enabled state.</summary>
	public bool IsEditorEnabled => !IsReadOnly;

	public string? ReadOnlyReason
	{
		get => _readOnlyReason;
		private set => SetProperty(ref _readOnlyReason, value);
	}

	/// <summary>Sets observer mode and prevents local editing/runs from diverging from the remote agent.</summary>
	public void SetReadOnly(bool readOnly, string? reason = null)
	{
		if (readOnly)
		{
			StopRun();
			ReadOnlyReason = string.IsNullOrWhiteSpace(reason) ? "Remote observer" : reason;
			SetDashboardRefreshActive(false);
			Status = ReadOnlyReason;
		}
		else
		{
			ReadOnlyReason = null;
		}

		IsReadOnly = readOnly;
	}

	/// <summary>Called by the workspace when this local canvas becomes active or inactive.</summary>
	public void SetDashboardRefreshActive(bool active)
		=> Dashboard.SetRefreshActive(active && !IsReadOnly);

	/// <summary>Title of the node the engine is currently executing, for dashboard display.</summary>
	internal string? CurrentRunNodeTitle => _currentRunNode?.Title;

	/// <summary>Whether the active run was started in loop mode, for dashboard display.</summary>
	internal bool CurrentRunLooping => _currentRunLooping;

	public ObservableCollection<RuntimeSignal> Signals { get; } = new();
	public ObservableCollection<string> SignalSuggestions { get; } = new();
	public ObservableCollection<string> GraphExplanationLines { get; } = new();
	public ObservableCollection<NodeCategory> Categories { get; }
	public ObservableCollection<NodeDefinition> Definitions { get; }
	public ObservableCollection<NodeParamBinding> ParameterBindings { get; } = new();
	public ObservableCollection<NodeParamBinding> AdvancedParameterBindings { get; } = new();
	public bool HasAdvancedParameters => AdvancedParameterBindings.Count > 0;
	public ObservableCollection<string> NodeInfoUsageTips { get; } = new();
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

	/// <summary>
	/// Connector layer for the canvas: one entry per drawn edge, each keeping its own geometry in
	/// sync with its endpoint nodes. Rebuilt on structural changes via <see cref="RebuildEdges"/>.
	/// </summary>
	public ObservableCollection<EdgeViewModel> Edges { get; } = new();

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

			// Leaving a node commits its in-progress property tweaks as one undo entry.
			CommitPendingPropertyEdit();
			UpdatePrimarySelection(value);
			RefreshSelectedNodeDefinition();
			NotifySelectionChanged();
			CaptureStatus = "Click the target in-game, then Capture to fill this node.";
			CaptureDriftState = "none";
		}
	}

	public NodeDefinition? SelectedNodeDefinition
	{
		get => _selectedNodeDefinition;
		set
		{
			_selectedNodeDefinition = value;
			if (!IsReadOnly && SelectedNode != null && value != null &&
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
			Dashboard?.OnRunStateChanged(value);
			OnPropertyChanged();
			StopCommand.NotifyCanExecuteChanged();
		}
	}

	public bool IsLooping
	{
		get => _isLooping;
		set => SetProperty(ref _isLooping, value);
	}

	public string Status
	{
		get => _status;
		set => SetProperty(ref _status, value);
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
		set => SetProperty(ref _isSettingsOpen, value);
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
		set => SetProperty(ref _isGraphExplanationOpen, value);
	}

	/// <summary>True when the validation panel is expanded (auto-opens when a run is blocked).</summary>
	public bool IsValidationOpen
	{
		get => _isValidationOpen;
		set => SetProperty(ref _isValidationOpen, value);
	}

	/// <summary>True when the run log panel is expanded (auto-opens on engine faults).</summary>
	public bool IsRunLogOpen
	{
		get => _isRunLogOpen;
		set => SetProperty(ref _isRunLogOpen, value);
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
		set => SetProperty(ref _isNodeInfoOpen, value);
	}

	public string NodeInfoTitle
	{
		get => _nodeInfoTitle;
		private set => SetProperty(ref _nodeInfoTitle, value);
	}

	public string NodeInfoDescription
	{
		get => _nodeInfoDescription;
		private set => SetProperty(ref _nodeInfoDescription, value);
	}

	/// <summary>True when the graph has edits that are not on disk yet.</summary>
	public bool IsDirty
	{
		get => _isDirty;
		private set => SetProperty(ref _isDirty, value);
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
	public IRelayCommand ClearRunLogCommand { get; }
	public IRelayCommand<ValidationIssue?> SelectValidationIssueCommand { get; }
	public IRelayCommand<NodeParamBinding?> AddListItemCommand { get; }
	public IRelayCommand<(NodeParamBinding binding, string value)?> RemoveListItemCommand { get; }
	public IRelayCommand CloseNodeInfoCommand { get; }
	public IRelayCommand<NodeDefinition?> ShowDefinitionInfoCommand { get; }
	public IRelayCommand CaptureFromGameCommand { get; }

	private bool CanModifyGraph() => !IsReadOnly;

	private void NotifyEditCommandStateChanged()
	{
		AddNodeCommand.NotifyCanExecuteChanged();
		CreateNodeFromDefinitionCommand.NotifyCanExecuteChanged();
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		MoveTransitionUpCommand.NotifyCanExecuteChanged();
		MoveTransitionDownCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		DeleteSelectedCommand.NotifyCanExecuteChanged();
		UndoCommand.NotifyCanExecuteChanged();
		RedoCommand.NotifyCanExecuteChanged();
		NewScriptCommand.NotifyCanExecuteChanged();
		LoadScriptCommand.NotifyCanExecuteChanged();
		SaveScriptCommand.NotifyCanExecuteChanged();
		ExportScriptCommand.NotifyCanExecuteChanged();
		LoadTemplateCommand.NotifyCanExecuteChanged();
		StartCommand.NotifyCanExecuteChanged();
		StepCommand.NotifyCanExecuteChanged();
		StopCommand.NotifyCanExecuteChanged();
		CaptureFromGameCommand.NotifyCanExecuteChanged();
	}

	public void Dispose()
	{
		DisposeRuntimeUiQueue();
		try { DoActionDebugSignals.StopNativePump(); } catch { /* no session */ }
		Dashboard.Dispose();
		_propertyEditTimer?.Stop();
		_runCts?.Cancel();
		_runCts?.Dispose();
		_runCts = null;
		DetachScript();
		foreach (var edge in Edges)
			edge.Dispose();
		Edges.Clear();
		_engine.NodeEntered -= OnNodeEntered;
		_engine.NodeCompleted -= OnNodeCompleted;
		_engine.TransitionTaken -= OnTransitionTaken;
		_engine.Completed -= OnEngineCompleted;
		_engine.Faulted -= OnEngineFaulted;
		_engine.DiagnosticSink = null;
	}

	public GraphConnectionRuleResult PreviewConnect(NodeModel? source, NodeModel? target)
		=> _connectionRules.CanConnect(Script, source, target);

	public GraphConnectionRuleResult PreviewRetarget(TransitionModel? transition, NodeModel? target)
		=> _connectionRules.CanRetarget(Script, transition, target);

	/// <summary>
	/// Opens a gesture batch (e.g. a node drag): edits made until <see cref="CommitGraphEditBatch"/>
	/// fold into a single undo entry. Any pending property edit is committed first so it stays a
	/// separate entry.
	/// </summary>
	public void BeginGraphEditBatch(string label)
	{
		if (_activeGraphEditBatchLabel != null)
			return;

		CommitPendingPropertyEdit();
		_activeGraphEditBatchLabel = label;
	}

	public void CommitGraphEditBatch()
	{
		if (_activeGraphEditBatchLabel == null)
			return;

		var label = _activeGraphEditBatchLabel;
		_activeGraphEditBatchLabel = null;
		RecordGraphEdit(label);
	}

	/// <summary>
	/// Records the difference between the shadow snapshot (state at the last history entry) and the
	/// live graph. No-op while an edit is being applied or a gesture batch is open.
	/// </summary>
	private void RecordGraphEdit(string label)
	{
		if (_editHistory.IsApplying || _activeGraphEditBatchLabel != null)
			return;

		// Whatever property deltas were pending are captured by this record.
		_propertyEditPending = false;
		_propertyEditTimer?.Stop();

		var recorded = _editHistory.Record(label, _shadowScript, Script);
		_shadowScript = GraphCloneService.Clone(Script);
		if (recorded)
		{
			IsDirty = true;
		}
	}

	/// <summary>Turns debounced inspector/property edits into their own history entry.</summary>
	private void CommitPendingPropertyEdit()
	{
		if (!_propertyEditPending)
			return;

		_propertyEditPending = false;
		_propertyEditTimer?.Stop();
		RecordGraphEdit("Edit properties");
	}

	private void UndoGraphEdit()
	{
		CommitPendingPropertyEdit();
		var selection = CaptureSelection();
		var graph = _editHistory.Undo(Script);
		if (graph == null)
			return;

		Script = graph;
		RestoreSelection(selection);
		IsDirty = true;
		Status = "Undid graph edit";
	}

	private void RedoGraphEdit()
	{
		// A pending property edit is a new edit: committing it clears the redo stack, which is the
		// consistent outcome for "type, then hit redo".
		CommitPendingPropertyEdit();
		var selection = CaptureSelection();
		var graph = _editHistory.Redo();
		if (graph == null)
			return;

		Script = graph;
		RestoreSelection(selection);
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

	private enum RunTrailEntryKind
	{
		Node,
		Transition
	}

	private readonly record struct RunTrailEntry(RunTrailEntryKind Kind, Guid Id);
}

/// <summary>Ordering applied to the node catalog list.</summary>
public enum CatalogSortMode
{
	Category,
	NameAscending,
	NameDescending
}
