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
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace SharpBuilder.Editor.Wpf.ViewModels;

/// <summary>
/// Backing view model for the SharpBuilder node editor. Handles script persistence, runtime execution,
/// and light-weight visual state (selection, active trail).
/// </summary>
public class NodeEditorViewModel : INotifyPropertyChanged, IDisposable
{
	private readonly GraphScriptService _scriptService;
	private readonly GraphExecutionEngine _engine;
	private readonly NodeCatalogService _catalogService;
	private readonly GraphValidator _validator;
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
	private readonly Services.WindowSizeOption _customWindowSize = new("Custom", 1400, 900, isCustom: true);
	private Services.WindowSizeOption? _selectedWindowSize;
	private bool _suppressWindowSizeRequest;
	private bool _isSettingsOpen;
	private readonly DateTime _dashboardStartedUtc = DateTime.UtcNow;
	private readonly DispatcherTimer _dashboardTimer;
	private SkillSession? _dashboardSkillSession;
	private readonly ItemTracker _itemTracker;
	private bool _dashboardXpActiveOnly;
	private bool _dashboardItemsActiveOnly = true;
	private string _dashboardItemsStatus = "Item tracker idle";
	private long _dashboardItemsGpPerHour;
	private int _dashboardItemsActiveCount;
	private string _dashboardRuntime = "00:00:00";
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
		AddListItemCommand = new RelayCommand<NodeParamBinding?>(AddListEntry);
		RemoveListItemCommand = new RelayCommand<(NodeParamBinding binding, string value)?>(RemoveListEntry);
		CloseNodeInfoCommand = new RelayCommand(() => IsNodeInfoOpen = false);
		ShowDefinitionInfoCommand = new RelayCommand<NodeDefinition?>(ShowDefinitionInfo);
		CaptureFromGameCommand = new RelayCommand(CaptureFromGame, () => CanCaptureSelectedNode);

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
		RefreshSignals();
		RefreshDashboard();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public ObservableCollection<RuntimeSignal> Signals { get; } = new();
	public ObservableCollection<string> SignalSuggestions { get; } = new();
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

	public void SelectNode(NodeModel node, bool toggle)
	{
		if (node == null) return;

		// Drop any transition selection that doesn't belong to the new selection,
		// otherwise Delete removes a stale edge instead of the selected node.
		if (_selectedTransition != null && !node.Transitions.Contains(_selectedTransition))
		{
			SelectedTransition = null;
		}

		if (toggle)
		{
			if (_selectedNodes.Contains(node))
			{
				node.IsSelected = false;
				_selectedNodes.Remove(node);
			}
			else
			{
				node.IsSelected = true;
				_selectedNodes.Add(node);
			}

			_selectedNode = _selectedNodes.LastOrDefault();
		}
		else
		{
			foreach (var existing in _selectedNodes.ToList())
			{
				existing.IsSelected = false;
				_selectedNodes.Remove(existing);
			}

			node.IsSelected = true;
			_selectedNodes.Add(node);
			_selectedNode = node;
		}

		SelectedNodeDefinition = _selectedNode == null
			? null
			: _catalogService.GetDefinition(_selectedNode.DefinitionId);

		OnPropertyChanged(nameof(SelectedNode));
		OnPropertyChanged(nameof(SelectedNodes));
		OnPropertyChanged(nameof(CanEditNode));
		OnPropertyChanged(nameof(CanEditTransitions));
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		RefreshParameterBindings();
		IsNodeInfoOpen = false;
	}

	/// <summary>
	/// Clears all selected nodes.
	/// </summary>
	public void ClearSelection()
	{
		foreach (var node in _selectedNodes.ToList())
		{
			node.IsSelected = false;
			_selectedNodes.Remove(node);
		}

		_selectedNode = null;
		SelectedNodeDefinition = null;
		SelectedTransition = null;

		OnPropertyChanged(nameof(SelectedNode));
		OnPropertyChanged(nameof(SelectedNodes));
		OnPropertyChanged(nameof(CanEditNode));
		OnPropertyChanged(nameof(CanEditTransitions));
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		RefreshParameterBindings();
		IsNodeInfoOpen = false;
	}

	/// <summary>
	/// Selects all nodes within the specified bounds (for box/marquee selection).
	/// </summary>
	/// <param name="bounds">The selection rectangle in canvas coordinates.</param>
	public void SelectNodesInBounds(Rect bounds)
	{
		foreach (var node in Script.Nodes)
		{
			var nodeRect = Converters.NodeConnectorConverter.GetNodeBounds(node);
			if (bounds.IntersectsWith(nodeRect))
			{
				if (!_selectedNodes.Contains(node))
				{
					node.IsSelected = true;
					_selectedNodes.Add(node);
				}
			}
		}

		_selectedNode = _selectedNodes.LastOrDefault();
		SelectedNodeDefinition = _selectedNode == null
			? null
			: _catalogService.GetDefinition(_selectedNode.DefinitionId);

		OnPropertyChanged(nameof(SelectedNode));
		OnPropertyChanged(nameof(SelectedNodes));
		OnPropertyChanged(nameof(CanEditNode));
		OnPropertyChanged(nameof(CanEditTransitions));
		RemoveNodeCommand.NotifyCanExecuteChanged();
		AddTransitionCommand.NotifyCanExecuteChanged();
		SetAsStartCommand.NotifyCanExecuteChanged();
		RemoveTransitionCommand.NotifyCanExecuteChanged();
		RefreshParameterBindings();
	}

	private void UpdatePrimarySelection(NodeModel? node)
	{
		foreach (var existing in _selectedNodes.ToList())
		{
			existing.IsSelected = false;
			_selectedNodes.Remove(existing);
		}

		_selectedNode = node;

		if (_selectedNode != null)
		{
			_selectedNode.IsSelected = true;
			_selectedNodes.Add(_selectedNode);
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
			OnPropertyChanged();
			StopCommand.NotifyCanExecuteChanged();
		}
	}

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

	public IEnumerable<NodeDefinition> FilteredDefinitions
	{
		get
		{
			var source = (SelectedCategory == null || string.Equals(SelectedCategory.Id, "all", StringComparison.OrdinalIgnoreCase)
					? Definitions
					: Definitions.Where(d => string.Equals(d.CategoryId, SelectedCategory.Id, StringComparison.OrdinalIgnoreCase)))
				.Where(d => d.IsImplemented);

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
		get
		{
			var def = SelectedNodeDefinition;
			if (def == null) return false;
			return string.Equals(def.CategoryId, "npcs", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(def.CategoryId, "objects", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(def.Id, "loot.pickup", StringComparison.OrdinalIgnoreCase);
		}
	}

	private static string DesiredCaptureKind(NodeDefinition def)
	{
		if (string.Equals(def.CategoryId, "npcs", StringComparison.OrdinalIgnoreCase)) return "NPC";
		if (string.Equals(def.Id, "loot.pickup", StringComparison.OrdinalIgnoreCase)) return "GroundItem";
		return "Object";
	}

	private void CaptureFromGame()
	{
		var node = SelectedNode;
		var def = SelectedNodeDefinition;
		if (node == null || def == null || !CanCaptureSelectedNode)
		{
			CaptureStatus = "Select an Object/NPC interaction node first.";
			CaptureDriftState = "none";
			return;
		}

		var kind = DesiredCaptureKind(def);
		var cap = DoActionDebugSignals.LatestCapture(kind) ?? DoActionDebugSignals.LatestCapture();
		if (cap == null)
		{
			CaptureStatus = "No in-game click captured yet — click the target in the game, then Capture.";
			CaptureDriftState = "none";
			return;
		}

		// Remember the existing opcode/offset so we can report match vs drift after filling.
		var oldAction = FindParamValue(node, "actionIndex");
		var oldOffset = FindParamValue(node, "offset");

		var filledAny = false;
		filledAny |= SetParamRaw(node, def, "id", cap.Id > 0 ? cap.Id.ToString() : null);
		// No dedicated id field (e.g. NPC interact / highlighted object) — steer the captured
		// id into the node's primary target/list field instead.
		if (cap.Id > 0 && GetParamType(def, "id") == null)
		{
			foreach (var listKey in new[] { "target", "name", "objectIds" })
				if (SetParamRaw(node, def, listKey, cap.Id.ToString())) { filledAny = true; break; }
		}
		// Only fill actionIndex when it is a free numeric opcode; skip click-mode enums
		// (objects.interactHighlighted uses a 0/1/3 mouse mode, not an opcode).
		if (GetParamType(def, "actionIndex") == NodeParamType.Number)
			filledAny |= SetParamRaw(node, def, "actionIndex", cap.ActionOpcode.ToString());
		filledAny |= SetParamRaw(node, def, "offset", cap.Offset.ToString());
		if (cap.Distance > 0)
			filledAny |= SetParamRaw(node, def, "maxDistance", cap.Distance.ToString());

		if (!filledAny)
		{
			CaptureStatus = $"Captured {kind} (id {cap.Id}, action {cap.ActionOpcode}, offset {cap.Offset}) but this node has no matching fields.";
			CaptureDriftState = "none";
			return;
		}

		var hadValues = !string.IsNullOrWhiteSpace(oldAction) || !string.IsNullOrWhiteSpace(oldOffset);
		var matched = NumbersEqual(oldAction, cap.ActionOpcode.ToString()) && NumbersEqual(oldOffset, cap.Offset.ToString());
		CaptureDriftState = !hadValues ? "filled" : matched ? "match" : "drift";
		CaptureStatus = CaptureDriftState switch
		{
			"match" => $"Match — {kind} action {cap.ActionOpcode}, offset {cap.Offset} (id {cap.Id}).",
			"drift" => $"Drift — updated to action {cap.ActionOpcode}, offset {cap.Offset} (id {cap.Id}).",
			_ => $"Filled from {kind} click — action {cap.ActionOpcode}, offset {cap.Offset} (id {cap.Id})."
		};
	}

	private static NodeParamType? GetParamType(NodeDefinition def, string key)
		=> def.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.Type;

	private static string? FindParamValue(NodeModel node, string key)
		=> node.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.RawValue;

	private bool SetParamRaw(NodeModel node, NodeDefinition def, string key, string? value)
	{
		if (value == null) return false;
		var pdef = def.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
		if (pdef == null) return false;
		var pv = node.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
		if (pv == null) return false;

		// For enum params, prefer the catalog entry whose trailing value matches so the dropdown
		// stays populated; otherwise store the raw numeric (ToInt parses both at execution time).
		if (pdef.Type == NodeParamType.Enum && pdef.EnumValues != null && int.TryParse(value, out var numeric))
		{
			var match = pdef.EnumValues.FirstOrDefault(e => ParseTrailingInt(e) == numeric);
			pv.RawValue = match ?? value;
		}
		else
		{
			pv.RawValue = value;
		}
		return true;
	}

	private static bool NumbersEqual(string? a, string? b)
	{
		var pa = ParseTrailingInt(a);
		var pb = ParseTrailingInt(b);
		return pa.HasValue && pb.HasValue && pa.Value == pb.Value;
	}

	private static int? ParseTrailingInt(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return null;
		if (int.TryParse(text.Trim(), out var n)) return n;
		var eq = text.LastIndexOf('=');
		return eq >= 0 && int.TryParse(text[(eq + 1)..].Trim(), out n) ? n : null;
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
	public IRelayCommand<NodeParamBinding?> AddListItemCommand { get; }
	public IRelayCommand<(NodeParamBinding binding, string value)?> RemoveListItemCommand { get; }
	public IRelayCommand CloseNodeInfoCommand { get; }
	public IRelayCommand<NodeDefinition?> ShowDefinitionInfoCommand { get; }
	public IRelayCommand CaptureFromGameCommand { get; }

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

	private void AttachScript(GraphModel script)
	{
		_suppressDirty = true;
		try
		{
			script.PropertyChanged += OnScriptPropertyChanged;
			script.Nodes.CollectionChanged += OnNodesChanged;
			foreach (var node in script.Nodes)
			{
				EnsureDefinition(node);
				node.PropertyChanged += OnNodePropertyChanged;
				node.Transitions.CollectionChanged += OnTransitionsChanged;
				node.Parameters.CollectionChanged += OnParametersChanged;
				foreach (var param in node.Parameters)
				{
					param.PropertyChanged += OnParameterPropertyChanged;
				}
			}

			SelectedNode = script.Nodes.FirstOrDefault();
			Status = $"Loaded \"{script.Name}\"";
		}
		finally
		{
			_suppressDirty = false;
		}

		IsDirty = false;
		OnPropertyChanged(nameof(IsCanvasEmpty));
	}

	private void DetachScript()
	{
		_script.PropertyChanged -= OnScriptPropertyChanged;
		_script.Nodes.CollectionChanged -= OnNodesChanged;
		foreach (var node in _script.Nodes)
		{
			node.PropertyChanged -= OnNodePropertyChanged;
			node.Transitions.CollectionChanged -= OnTransitionsChanged;
			node.Parameters.CollectionChanged -= OnParametersChanged;
			foreach (var param in node.Parameters)
			{
				param.PropertyChanged -= OnParameterPropertyChanged;
			}
		}
	}

	private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (NodeModel node in e.NewItems)
			{
				EnsureDefinition(node);
				node.PropertyChanged += OnNodePropertyChanged;
				node.Transitions.CollectionChanged += OnTransitionsChanged;
				node.Parameters.CollectionChanged += OnParametersChanged;
				foreach (var param in node.Parameters)
				{
					param.PropertyChanged += OnParameterPropertyChanged;
				}
			}
		}

		if (e.OldItems != null)
		{
			foreach (NodeModel node in e.OldItems)
			{
				node.PropertyChanged -= OnNodePropertyChanged;
				node.Transitions.CollectionChanged -= OnTransitionsChanged;
				node.Parameters.CollectionChanged -= OnParametersChanged;
				foreach (var param in node.Parameters)
				{
					param.PropertyChanged -= OnParameterPropertyChanged;
				}
				_selectedNodes.Remove(node);
			}
		}

		RefreshSignals();
		OnPropertyChanged(nameof(AllTransitions));
		OnPropertyChanged(nameof(IsCanvasEmpty));
		AddTransitionCommand.NotifyCanExecuteChanged();
		RefreshDashboard();
		MarkDirty();
	}

	private void OnTransitionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		RefreshSignals();
		OnPropertyChanged(nameof(AllTransitions));
		RefreshDashboard();
		MarkDirty();
	}

	private void OnParametersChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.NewItems != null)
		{
			foreach (NodeParameterValue param in e.NewItems)
			{
				param.PropertyChanged += OnParameterPropertyChanged;
			}
		}

		if (e.OldItems != null)
		{
			foreach (NodeParameterValue param in e.OldItems)
			{
				param.PropertyChanged -= OnParameterPropertyChanged;
			}
		}

		RefreshSignals();
		RefreshParameterBindings();
		RefreshDashboard();
		MarkDirty();
	}

	private void OnParameterPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (string.Equals(e.PropertyName, nameof(NodeParameterValue.RawValue), StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(e.PropertyName, nameof(NodeParameterValue.BoolValue), StringComparison.OrdinalIgnoreCase))
		{
			RefreshSignals();
			RefreshDashboard();
			MarkDirty();
		}
	}

	private void OnScriptPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// Metadata edits count as document changes; UpdatedAt is touched by saving itself.
		if (e.PropertyName is nameof(GraphModel.Name) or nameof(GraphModel.Description)
		    or nameof(GraphModel.Author) or nameof(GraphModel.StartNodeId))
		{
			MarkDirty();
		}
	}

	private void MarkDirty()
	{
		if (!_suppressDirty)
		{
			IsDirty = true;
		}
	}

	private void AddNode()
	{
		var defaultDefinition = _catalogService.GetDefaultDefinitionForType(Script.Nodes.Count == 0 ? NodeType.Start : NodeType.Action);
		AddNodeFromDefinition(defaultDefinition);
	}

	private void AddNodeFromDefinition(NodeDefinition? definition)
	{
		if (definition == null)
			return;

		// Cascade placement, wrapping every 12 nodes so big graphs don't march off-canvas.
		var offset = (Script.Nodes.Count % 12) * 30;
		var node = new NodeModel
		{
			Title = definition.Title,
			Description = definition.ShortDescription,
			DefinitionId = definition.Id,
			DefinitionTitle = definition.Title,
			Type = ResolveNodeType(definition),
			X = 80 + offset,
			Y = 80 + offset,
			DwellMilliseconds = 250
		};

		EnsureNodeParameters(node, definition);

		Script.Nodes.Add(node);
		// First node on an empty canvas becomes the start regardless of its definition;
		// the engine can start anywhere, so we honor the user's pick instead of swapping it.
		if (!Script.StartNodeId.HasValue || node.Type == NodeType.Start)
		{
			Script.StartNodeId = node.Id;
		}

		SelectedNode = node;
		Status = $"Added {node.Title}";
	}

	private void RemoveSelectedNode()
	{
		if (SelectedNode == null)
			return;

		var targetId = SelectedNode.Id;

		foreach (var node in Script.Nodes.ToList())
		{
			var toRemove = node.Transitions.Where(t => t.FromNodeId == targetId || t.ToNodeId == targetId).ToList();
			foreach (var edge in toRemove)
			{
				node.Transitions.Remove(edge);
			}
		}

		Script.Nodes.Remove(SelectedNode);
		_selectedNodes.Remove(SelectedNode);

		if (Script.StartNodeId == targetId)
		{
			Script.StartNodeId = Script.Nodes.FirstOrDefault()?.Id;
		}

		SelectedNode = Script.Nodes.FirstOrDefault();
		SelectedTransition = null;

		Status = "Removed node";
	}

	private void AddTransition()
	{
		if (SelectedNode == null || Script.Nodes.Count < 2)
			return;

		var target = Script.Nodes.First(n => n.Id != SelectedNode.Id);

		var transition = new TransitionModel
		{
			FromNodeId = SelectedNode.Id,
			ToNodeId = target.Id,
			Label = "Next",
			IsFallback = !SelectedNode.Transitions.Any()
		};

		SelectedNode.Transitions.Add(transition);
		SelectedTransition = transition;
		Status = "Added transition";
	}

	/// <summary>
	/// Creates a transition between two nodes, used by the canvas port drag-to-connect gesture.
	/// Selects the existing edge instead of duplicating when one already targets the same node.
	/// </summary>
	public void ConnectNodes(NodeModel from, NodeModel to)
	{
		if (from == null || to == null || from.Id == to.Id)
			return;

		var existing = from.Transitions.FirstOrDefault(t => t.ToNodeId == to.Id);
		if (existing != null)
		{
			SelectedNode = from;
			SelectedTransition = existing;
			Status = $"{from.Title} already links to {to.Title}";
			return;
		}

		var transition = new TransitionModel
		{
			FromNodeId = from.Id,
			ToNodeId = to.Id,
			Label = "Next",
			IsFallback = !from.Transitions.Any()
		};

		from.Transitions.Add(transition);
		SelectedNode = from;
		SelectedTransition = transition;
		Status = $"Connected {from.Title} → {to.Title}";
	}

	/// <summary>
	/// Points an existing transition at a different target node, used by the canvas
	/// port drag gesture. No-ops when another edge on the same node already targets it.
	/// </summary>
	public void RetargetTransition(TransitionModel transition, NodeModel target)
	{
		if (transition == null || target == null || transition.ToNodeId == target.Id)
			return;

		var source = Script.Nodes.FirstOrDefault(n => n.Transitions.Contains(transition));
		if (source == null || source.Id == target.Id)
			return;

		if (source.Transitions.Any(t => !ReferenceEquals(t, transition) && t.ToNodeId == target.Id))
		{
			Status = $"{source.Title} already links to {target.Title}";
			return;
		}

		transition.ToNodeId = target.Id;
		SelectedNode = source;
		SelectedTransition = transition;
		Status = $"Retargeted transition to {target.Title}";
	}

	/// <summary>
	/// Moves a transition within its node's list. Order matters: the engine evaluates
	/// transitions top to bottom, so this is the priority control.
	/// </summary>
	private void MoveTransition(TransitionModel? transition, int direction)
	{
		if (transition == null)
			return;

		var source = Script.Nodes.FirstOrDefault(n => n.Transitions.Contains(transition));
		if (source == null)
			return;

		var index = source.Transitions.IndexOf(transition);
		var newIndex = index + direction;
		if (index < 0 || newIndex < 0 || newIndex >= source.Transitions.Count)
			return;

		source.Transitions.Move(index, newIndex);
		SelectedTransition = transition;
		Status = $"Moved transition {(direction < 0 ? "up" : "down")}";
	}

	private void RemoveTransition(TransitionModel? transition)
	{
		if (SelectedNode == null || transition == null)
			return;

		SelectedNode.Transitions.Remove(transition);
		if (ReferenceEquals(SelectedTransition, transition))
		{
			SelectedTransition = SelectedNode.Transitions.FirstOrDefault();
		}

		Status = "Removed transition";
	}

	private void SetSelectedAsStart()
	{
		if (SelectedNode == null)
			return;

		Script.StartNodeId = SelectedNode.Id;
		Status = $"{SelectedNode.Title} marked as start";
	}

	private void ClearTrail()
	{
		_currentRunNode = null;

		foreach (var node in Script.Nodes)
		{
			node.IsActive = false;
			node.IsCurrent = false;
			node.LastRunStatus = NodeRunStatus.None;
		}

		foreach (var transition in AllTransitions)
		{
			transition.IsActive = false;
		}
	}

	private void DeleteSelection()
	{
		if (SelectedTransition != null && SelectedNode != null)
		{
			RemoveTransition(SelectedTransition);
			return;
		}

		if (_selectedNodes.Count == 0)
			return;

		foreach (var node in _selectedNodes.ToList())
		{
			SelectedNode = node;
			RemoveSelectedNode();
		}
	}

	private bool ConfirmDiscardUnsavedChanges()
	{
		if (!IsDirty)
			return true;

		var result = MessageBox.Show(
			$"\"{Script.Name}\" has unsaved changes. Discard them?",
			"Unsaved changes",
			MessageBoxButton.YesNo,
			MessageBoxImage.Warning);
		return result == MessageBoxResult.Yes;
	}

	private void CreateBlankScript()
	{
		if (!ConfirmDiscardUnsavedChanges())
			return;

		StopRun();
		Script = _scriptService.CreateNew("New graph");
		CurrentFilePath = null;
		RefreshSignals();
	}

	private void LoadTemplate()
	{
		if (!ConfirmDiscardUnsavedChanges())
			return;

		StopRun();
		Script = _scriptService.CreatePowerFishingTemplate();
		CurrentFilePath = null;
		RefreshSignals();
	}

	/// <summary>
	/// Replaces the current graph with a pre-built one (e.g. a workspace demo canvas). Unlike the
	/// file/template commands this does not prompt to discard changes — the caller owns that choice,
	/// since a freshly created canvas has nothing to lose.
	/// </summary>
	public void LoadGraph(GraphModel graph, string? filePath = null)
	{
		if (graph == null) throw new ArgumentNullException(nameof(graph));

		StopRun();
		Script = graph;
		CurrentFilePath = filePath;
		RefreshSignals();
	}

	private async Task LoadScriptAsync()
	{
		if (!ConfirmDiscardUnsavedChanges())
			return;

		var dialog = new OpenFileDialog
		{
			Title = "Open SharpBuilder graph",
			Filter = "SharpBuilder graph (*.orbitfsm.json)|*.orbitfsm.json|JSON (*.json)|*.json|All files|*.*",
			InitialDirectory = _scriptService.ScriptsDirectory
		};

		if (dialog.ShowDialog() != true)
			return;

		var (loaded, error) = await _scriptService.TryLoadAsync(dialog.FileName);
		if (loaded == null)
		{
			MessageBox.Show($"Unable to load the selected graph.\n\n{error}", "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
			return;
		}

		StopRun();
		Script = loaded;
		CurrentFilePath = dialog.FileName;
		RefreshSignals();
	}

	private async Task SaveScriptAsync()
	{
		if (string.IsNullOrWhiteSpace(CurrentFilePath))
		{
			var dialog = new SaveFileDialog
			{
				Title = "Save SharpBuilder graph",
				Filter = "SharpBuilder graph (*.orbitfsm.json)|*.orbitfsm.json|JSON (*.json)|*.json|All files|*.*",
				FileName = $"{Script.Name}.orbitfsm.json",
				InitialDirectory = _scriptService.ScriptsDirectory
			};

			if (dialog.ShowDialog() != true)
				return;

			CurrentFilePath = dialog.FileName;
		}

		await _scriptService.SaveAsync(Script, CurrentFilePath);
		IsDirty = false;
		Status = $"Saved to {CurrentFilePath}";
	}

	private async Task ExportScriptAsync()
	{
		var dialog = new SaveFileDialog
		{
			Title = "Export / share SharpBuilder graph",
			Filter = "SharpBuilder graph (*.orbitfsm.json)|*.orbitfsm.json|JSON (*.json)|*.json|All files|*.*",
			FileName = $"{Script.Name}.orbitfsm.json",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
		};

		if (dialog.ShowDialog() != true)
			return;

		await _scriptService.SaveAsync(Script, dialog.FileName);
		Status = $"Exported to {dialog.FileName}";
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

	private void RefreshSignals()
	{
		var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var transition in Script.Nodes.SelectMany(n => n.Transitions).Where(t => t.HasCondition))
		{
			var key = transition.ConditionKey.Trim();
			if (!string.IsNullOrWhiteSpace(key))
				keys.Add(key);
		}

		foreach (var node in Script.Nodes)
		{
			foreach (var param in node.Parameters.Where(p => string.Equals(p.Key, "signal", StringComparison.OrdinalIgnoreCase)))
			{
				foreach (var value in param.SplitValues())
				{
					keys.Add(value);
				}

				if (!param.AllowMultiple && !string.IsNullOrWhiteSpace(param.RawValue))
				{
					keys.Add(param.RawValue.Trim());
				}
			}
		}

		// Remove stale signals
		for (var i = Signals.Count - 1; i >= 0; i--)
		{
			if (!keys.Contains(Signals[i].Key, StringComparer.OrdinalIgnoreCase))
			{
				Signals.RemoveAt(i);
			}
		}

		// Add missing signals (default false)
		foreach (var key in keys)
		{
			if (!Signals.Any(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)))
			{
				Signals.Add(new RuntimeSignal { Key = key, Value = false });
			}
		}

		SignalSuggestions.Clear();
		foreach (var key in keys.OrderBy(k => k))
		{
			SignalSuggestions.Add(key);
		}

		OnPropertyChanged(nameof(Signals));
		OnPropertyChanged(nameof(SignalSuggestions));
	}

	private void RefreshDashboard()
	{
		var elapsed = DateTime.UtcNow - _dashboardStartedUtc;
		DashboardRuntime = elapsed.ToString(@"hh\:mm\:ss");
		DashboardCurrentNode = _currentRunNode?.Title ?? "None";
		DashboardRunMode = IsRunning ? (IsLooping ? "Running loop" : "Running once") : Status;
		DashboardGraphSummary = $"{Script.Nodes.Count} nodes / {AllTransitions.Count()} transitions";
		DashboardSignalSummary = $"{Signals.Count} signal{(Signals.Count == 1 ? string.Empty : "s")}";
		DashboardLastUpdated = DateTime.Now.ToString("HH:mm:ss");

		DashboardSkillsView.Refresh();

		if (!GameRuntime.IsGameApiAvailable)
		{
			DashboardXpStatus = "XP unavailable outside the injected game runtime";
			DashboardItemsStatus = "Items unavailable outside the injected game runtime";
			return;
		}

		RefreshItemTracker();

		try
		{
			_dashboardSkillSession ??= new SkillSession();
		}
		catch (Exception ex)
		{
			DashboardXpStatus = $"XP tracker unavailable: {ex.Message}";
			_dashboardSkillSession = null;
			return;
		}

		var updated = 0;
		string? error = null;
		foreach (var row in DashboardSkills)
		{
			if (row.TryUpdate(_dashboardSkillSession, out error))
			{
				updated++;
			}
			else
			{
				break;
			}
		}

		if (updated == DashboardSkills.Count)
		{
			DashboardActiveSkillCount = DashboardSkills.Count(s => s.IsActive);
			DashboardTotalXpGained = DashboardSkills.Sum(s => s.XpGained);
			DashboardXpStatus = DashboardActiveSkillCount == 0
				? "XP tracker ready; no gains this session"
				: $"{DashboardActiveSkillCount} active skill{(DashboardActiveSkillCount == 1 ? string.Empty : "s")}";
			DashboardSkillsView.Refresh();
		}
		else
		{
			DashboardXpStatus = $"XP refresh failed: {error}";
		}
	}

	private void RefreshItemTracker()
	{
		if (_itemTracker.TryUpdate(out var error))
		{
			DashboardItemsGpPerHour = _itemTracker.TotalGpPerHour;
			DashboardItemsActiveCount = _itemTracker.ActiveCount;
			DashboardItemsStatus = _itemTracker.ActiveCount == 0
				? "Item tracker ready; no item flow yet"
				: $"{_itemTracker.ActiveCount} item{(_itemTracker.ActiveCount == 1 ? string.Empty : "s")} tracked";
			DashboardItemsView.Refresh();
		}
		else
		{
			DashboardItemsStatus = $"Item tracker failed: {error}";
		}
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

	/// <summary>
	/// A node drag fires X and Y changes many times per second, and each
	/// <see cref="AllTransitions"/> invalidation rebuilds every connector path. Coalesce them so the
	/// edges still follow the node live, but the rebuild runs at most once per render frame
	/// (and once total for a whole multi-node drag tick) instead of twice per node per delta.
	/// </summary>
	private void QueueTransitionsRefresh()
	{
		var dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null)
		{
			// No UI thread (e.g. unit tests): keep the original synchronous behavior.
			OnPropertyChanged(nameof(AllTransitions));
			return;
		}

		if (_transitionsRefreshQueued)
			return;

		_transitionsRefreshQueued = true;
		dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
		{
			_transitionsRefreshQueued = false;
			OnPropertyChanged(nameof(AllTransitions));
		});
	}

	private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(NodeModel.X) || e.PropertyName == nameof(NodeModel.Y))
		{
			QueueTransitionsRefresh();
		}

		// Run/selection feedback is visual-only state; everything else is a document edit.
		if (e.PropertyName is not (nameof(NodeModel.IsActive) or nameof(NodeModel.IsCurrent)
		    or nameof(NodeModel.IsSelected) or nameof(NodeModel.LastRunStatus)))
		{
			MarkDirty();
		}
	}

	private void EnsureDefinition(NodeModel node)
	{
		var definition = _catalogService.GetDefinition(node.DefinitionId) ?? _catalogService.GetDefaultDefinitionForType(node.Type);
		node.DefinitionId = definition.Id;
		node.DefinitionTitle = definition.Title;
		EnsureNodeParameters(node, definition);
	}

	private static NodeType ResolveNodeType(NodeDefinition definition)
	{
		if (string.Equals(definition.Id, NodeCatalogDefaults.StartId, StringComparison.OrdinalIgnoreCase))
			return NodeType.Start;
		if (string.Equals(definition.Id, NodeCatalogDefaults.TerminalId, StringComparison.OrdinalIgnoreCase))
			return NodeType.Terminal;
		if (string.Equals(definition.CategoryId, "conditions", StringComparison.OrdinalIgnoreCase))
			return NodeType.Condition;

		return NodeType.Action;
	}

	private void EnsureNodeParameters(NodeModel node, NodeDefinition definition)
	{
		if (definition.Parameters == null)
			return;

		foreach (var parameter in definition.Parameters)
		{
			var existing = node.Parameters.FirstOrDefault(p => string.Equals(p.Key, parameter.Key, StringComparison.OrdinalIgnoreCase));
			if (existing == null)
			{
					node.Parameters.Add(new NodeParameterValue
					{
						Key = parameter.Key,
						Type = parameter.Type,
						AllowMultiple = parameter.AllowMultiple,
						RawValue = parameter.DefaultValue ?? string.Empty
					});
			}
			else
			{
				existing.Type = parameter.Type;
				existing.AllowMultiple = parameter.AllowMultiple;
			}
		}

		for (var i = node.Parameters.Count - 1; i >= 0; i--)
		{
			if (definition.Parameters.All(p => !string.Equals(p.Key, node.Parameters[i].Key, StringComparison.OrdinalIgnoreCase)))
			{
				node.Parameters.RemoveAt(i);
			}
		}
	}

	private void ApplyDefinitionToNode(NodeModel node, NodeDefinition definition)
	{
		node.DefinitionId = definition.Id;
		node.DefinitionTitle = definition.Title;
		node.Type = ResolveNodeType(definition);
		EnsureNodeParameters(node, definition);
		RefreshSignals();
	}

	private void RefreshParameterBindings()
	{
		ParameterBindings.Clear();
		AdvancedParameterBindings.Clear();

		if (SelectedNode == null)
		{
			OnPropertyChanged(nameof(HasAdvancedParameters));
			return;
		}

		var definition = SelectedNodeDefinition ?? _catalogService.GetDefinition(SelectedNode.DefinitionId);
		if (definition?.Parameters == null)
		{
			OnPropertyChanged(nameof(HasAdvancedParameters));
			return;
		}

		EnsureNodeParameters(SelectedNode, definition);

		// One binding per parameter key (so owners can resolve their inline companions).
		var bindings = new Dictionary<string, NodeParamBinding>(StringComparer.OrdinalIgnoreCase);
		foreach (var parameter in definition.Parameters)
		{
			var value = SelectedNode.Parameters.FirstOrDefault(p => string.Equals(p.Key, parameter.Key, StringComparison.OrdinalIgnoreCase));
			if (value != null)
			{
				bindings[parameter.Key] = new NodeParamBinding(parameter, value);
			}
		}

		// Keys that are rendered inline beside another control are dropped from the main list.
		var inlineCompanionKeys = new HashSet<string>(
			definition.Parameters
				.Where(p => !string.IsNullOrEmpty(p.InlineCompanionKey))
				.Select(p => p.InlineCompanionKey!),
			StringComparer.OrdinalIgnoreCase);

		foreach (var parameter in definition.Parameters)
		{
			if (!bindings.TryGetValue(parameter.Key, out var binding))
				continue;

			if (inlineCompanionKeys.Contains(parameter.Key))
				continue; // rendered inline by its owning parameter

			if (!string.IsNullOrEmpty(parameter.InlineCompanionKey) &&
			    bindings.TryGetValue(parameter.InlineCompanionKey!, out var companion))
			{
				binding.InlineCompanion = companion;
			}

			if (parameter.IsAdvanced)
				AdvancedParameterBindings.Add(binding);
			else
				ParameterBindings.Add(binding);
		}

		OnPropertyChanged(nameof(ParameterBindings));
		OnPropertyChanged(nameof(AdvancedParameterBindings));
		OnPropertyChanged(nameof(HasAdvancedParameters));
	}

	private void AddListEntry(NodeParamBinding? binding)
	{
		if (binding == null)
			return;

		if (!string.IsNullOrWhiteSpace(binding.Value.RawValue))
		{
			binding.Value.RawValue += Environment.NewLine;
		}
	}

	public void ShowNodeInfo(NodeModel? node)
	{
		if (node == null)
		{
			IsNodeInfoOpen = false;
			return;
		}

		var definition = _catalogService.GetDefinition(node.DefinitionId);
		NodeInfoTitle = definition?.Title ?? node.Title;
		NodeInfoDescription = definition?.ShortDescription ?? node.Description;

		NodeInfoUsageTips.Clear();
		if (definition?.Parameters != null)
		{
			foreach (var parameter in definition.Parameters)
			{
				var requirement = parameter.IsRequired ? "required" : "optional";
				var example = string.IsNullOrWhiteSpace(parameter.Placeholder)
					? "Provide a value."
					: parameter.Placeholder;

				NodeInfoUsageTips.Add($"{parameter.Label} [{parameter.Type}, {requirement}] - {example}");
			}
		}

		if (NodeInfoUsageTips.Count == 0)
		{
			NodeInfoUsageTips.Add("No parameters needed. Connect transitions and run.");
		}

		IsNodeInfoOpen = true;
	}

	private void ShowDefinitionInfo(NodeDefinition? definition)
	{
		if (definition == null)
		{
			IsNodeInfoOpen = false;
			return;
		}

		NodeInfoTitle = definition.Title;
		NodeInfoDescription = definition.ShortDescription;
		NodeInfoUsageTips.Clear();

		if (definition.Parameters != null)
		{
			foreach (var parameter in definition.Parameters)
			{
				var requirement = parameter.IsRequired ? "required" : "optional";
				var example = string.IsNullOrWhiteSpace(parameter.Placeholder)
					? "Provide a value."
					: parameter.Placeholder;

				NodeInfoUsageTips.Add($"{parameter.Label} [{parameter.Type}, {requirement}] - {example}");
			}
		}

		if (NodeInfoUsageTips.Count == 0)
		{
			NodeInfoUsageTips.Add("No parameters needed. Add it to the canvas and connect transitions.");
		}

		IsNodeInfoOpen = true;
	}

	private static double ExtractWidth(GridLength width, double fallback)
	{
		if (width.IsAbsolute && width.Value > 0)
			return width.Value;

		return fallback;
	}

	private void RemoveListEntry((NodeParamBinding binding, string value)? args)
	{
		if (args == null)
			return;

		var (binding, value) = args.Value;
		var filtered = binding.Value.SplitValues()
			.Where(v => !string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
			.ToList();
		binding.Value.RawValue = string.Join(Environment.NewLine, filtered);
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
