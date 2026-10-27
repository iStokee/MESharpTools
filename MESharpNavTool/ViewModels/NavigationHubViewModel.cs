using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MESharp.API;
using MESharp.Commands;
using MESharp.Services.NavigationData;

namespace MESharp.ViewModels
{
    public enum NavigationHubSection
    {
        Map,
        Travel,
        Routes,
        Graph,
        Dungeons,
        Status
    }

    /// <summary>
    /// Unified navigation area: the live world map is the landing screen, with the
    /// Travel (movement/teleport probes), Routes (route editor + recorder) and
    /// Graph Data (node/edge tables) tools as left-nav detail screens. Replaces the
    /// former Navigation / Webwalking / Graph top-level tabs. Graph health is validated
    /// automatically (activation, section changes, manual refresh) and surfaced as a
    /// status chip instead of a separate validate screen.
    /// </summary>
    public sealed class NavigationHubViewModel : BaseViewModel, IActivatableViewModel, IDisposable
    {
        private MapViewModel? _map;
        private NavigationViewModel? _travel;
        private WebwalkingViewModel? _routes;
        private WebwalkGraphViewModel? _graph;
        private DungeonImportViewModel? _dungeons;
        private NavHealthViewModel? _status;

        private NavigationHubSection _currentSection = NavigationHubSection.Map;
        private object? _currentDetailViewModel;
        private bool _showDeveloperTools;
        private bool _isActive;
        private bool _disposed;

        private string _validationText = "Not validated";
        private string _validationDetail = "Graph has not been validated yet.";
        private string _validationState = "busy"; // ok | warn | error | busy
        private bool _isValidating;
        private bool _validationPending;
        private CancellationTokenSource? _validationCts;

        // ── Live status strip ────────────────────────────────────────────────
        private System.Windows.Threading.DispatcherTimer? _statusTimer;
        private string _playerTileText = "No session";
        private string _collisionText = "Navigation data: —";
        private string _collisionState = "off"; // ok | warn | off
        private string _cacheText = "Definitions: —";
        private string _cacheState = "off";
        private string _nearestNodeText = "Nearest node: —";
        private string _graphStatsText = "Graph: —";
        private bool _isRecordingActive;
        private System.Collections.Generic.IReadOnlyList<WebwalkGraphNode>? _nodeCache;
        private int _nodeCacheCountdown;

        public MapViewModel Map => _map ??= new MapViewModel();
        private NavigationViewModel Travel
        {
            get
            {
                if (_travel == null)
                {
                    _travel = new NavigationViewModel { AllowDiagnostics = ShowDeveloperTools };
                    _travel.PropertyChanged += OnJourneyPropertyChanged;
                }
                return _travel;
            }
        }
        private WebwalkingViewModel Routes
        {
            get
            {
                if (_routes == null)
                {
                    _routes = new WebwalkingViewModel();
                    _routes.PropertyChanged += OnJourneyPropertyChanged;
                }
                return _routes;
            }
        }
        private WebwalkGraphViewModel Graph
        {
            get
            {
                if (_graph == null)
                {
                    _graph = new WebwalkGraphViewModel();
                    _graph.PropertyChanged += OnJourneyPropertyChanged;
                }
                return _graph;
            }
        }
        private DungeonImportViewModel Dungeons => _dungeons ??= new DungeonImportViewModel();
        private NavHealthViewModel Status => _status ??= new NavHealthViewModel { ShowDeveloperTools = ShowDeveloperTools };

        public bool ShowDeveloperTools
        {
            get => _showDeveloperTools;
            set
            {
                if (!SetProperty(ref _showDeveloperTools, value))
                    return;

                Services.CoverageMapServer.SetDeveloperToolsEnabled(value);
                if (_travel != null) _travel.AllowDiagnostics = value;
                if (_status != null) _status.ShowDeveloperTools = value;

                if (!value && IsDeveloperSection(CurrentSection))
                    ShowSection(NavigationHubSection.Map);
            }
        }

        public NavigationHubSection CurrentSection
        {
            get => _currentSection;
            private set
            {
                if (SetProperty(ref _currentSection, value))
                {
                    RaisePropertyChanged(nameof(IsMapSelected));
                    RaisePropertyChanged(nameof(IsTravelSelected));
                    RaisePropertyChanged(nameof(IsRoutesSelected));
                    RaisePropertyChanged(nameof(IsGraphSelected));
                    RaisePropertyChanged(nameof(IsDungeonsSelected));
                    RaisePropertyChanged(nameof(IsStatusSelected));
                    RaisePropertyChanged(nameof(MapVisibility));
                    RaisePropertyChanged(nameof(DetailVisibility));
                }
            }
        }

        public bool IsMapSelected => CurrentSection == NavigationHubSection.Map;
        public bool IsTravelSelected => CurrentSection == NavigationHubSection.Travel;
        public bool IsRoutesSelected => CurrentSection == NavigationHubSection.Routes;
        public bool IsGraphSelected => CurrentSection == NavigationHubSection.Graph;
        public bool IsDungeonsSelected => CurrentSection == NavigationHubSection.Dungeons;
        public bool IsStatusSelected => CurrentSection == NavigationHubSection.Status;

        /// <summary>The map view stays alive (hidden, not destroyed) across section switches so WebView2 keeps its pan/zoom.</summary>
        public Visibility MapVisibility => IsMapSelected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility DetailVisibility => IsMapSelected ? Visibility.Collapsed : Visibility.Visible;

        public object? CurrentDetailViewModel
        {
            get => _currentDetailViewModel;
            private set => SetProperty(ref _currentDetailViewModel, value);
        }

        public string ValidationText { get => _validationText; private set => SetProperty(ref _validationText, value); }
        public string ValidationDetail { get => _validationDetail; private set => SetProperty(ref _validationDetail, value); }
        public string ValidationState { get => _validationState; private set => SetProperty(ref _validationState, value); }

        public string PlayerTileText { get => _playerTileText; private set => SetProperty(ref _playerTileText, value); }
        public string CollisionText { get => _collisionText; private set => SetProperty(ref _collisionText, value); }
        public string CollisionState { get => _collisionState; private set => SetProperty(ref _collisionState, value); }
        public string CacheText { get => _cacheText; private set => SetProperty(ref _cacheText, value); }
        public string CacheState { get => _cacheState; private set => SetProperty(ref _cacheState, value); }
        public string NearestNodeText { get => _nearestNodeText; private set => SetProperty(ref _nearestNodeText, value); }
        public string GraphStatsText { get => _graphStatsText; private set => SetProperty(ref _graphStatsText, value); }

        /// <summary>True while either recorder (Graph Data pane or record-from-map) is running.</summary>
        public bool IsRecordingActive { get => _isRecordingActive; private set => SetProperty(ref _isRecordingActive, value); }

        public ICommand ShowMapCommand { get; }
        public ICommand ShowTravelCommand { get; }
        public ICommand ShowRoutesCommand { get; }
        public ICommand ShowGraphCommand { get; }
        public ICommand ShowDungeonsCommand { get; }
        public ICommand ShowStatusCommand { get; }
        public ICommand RefreshValidationCommand { get; }
        public ICommand ReviewValidationCommand { get; }
        public ICommand StopJourneyCommand { get; }
        private string _journeyText = "Choose a destination on the map or in Go To.";
        private bool _isJourneyActive;
        private string _journeySource = string.Empty;
        public string JourneyText { get => _journeyText; private set => SetProperty(ref _journeyText, value); }
        public bool IsJourneyActive
        {
            get => _isJourneyActive;
            private set
            {
                if (SetProperty(ref _isJourneyActive, value)) RaisePropertyChanged(nameof(JourneyHeading));
            }
        }
        public string JourneyHeading => IsJourneyActive ? "Journey in progress" :
            string.IsNullOrEmpty(_journeySource) ? "Ready" : "Last journey";
        private bool HasActiveJourney => !_disposed && (_travel?.IsTraveling == true || _routes?.IsRunning == true ||
            _graph?.IsPathRunning == true || Services.CoverageMapServer.TravelState.Running);

        public NavigationHubViewModel()
        {
            Services.CoverageMapServer.SetDeveloperToolsEnabled(false);
            ShowMapCommand = new RelayCommand(_ => ShowSection(NavigationHubSection.Map));
            ShowTravelCommand = new RelayCommand(_ => ShowSection(NavigationHubSection.Travel));
            ShowRoutesCommand = new RelayCommand(_ => ShowSection(NavigationHubSection.Routes));
            ShowGraphCommand = new RelayCommand(_ => ShowSection(NavigationHubSection.Graph));
            ShowDungeonsCommand = new RelayCommand(_ => ShowSection(NavigationHubSection.Dungeons));
            ShowStatusCommand = new RelayCommand(_ => ShowSection(NavigationHubSection.Status));
            RefreshValidationCommand = new RelayCommand(_ => RunValidation());
            ReviewValidationCommand = new RelayCommand(_ =>
            {
                ShowSection(NavigationHubSection.Graph);
                Graph.SelectedWorkspaceTabIndex = 1;
            }, _ => ShowDeveloperTools);
            StopJourneyCommand = new RelayCommand(_ =>
            {
                if (!HasActiveJourney) return;
                if (_travel?.StopTravelCommand.CanExecute(null) == true) _travel.StopTravelCommand.Execute(null);
                if (_routes?.StopRouteCommand.CanExecute(null) == true) _routes.StopRouteCommand.Execute(null);
                if (_graph?.StopPathCommand.CanExecute(null) == true) _graph.StopPathCommand.Execute(null);
                Services.CoverageMapServer.CancelTravel();
            }, _ => HasActiveJourney);

            // "Show on map" from the Routes/Graph panes: flip to the map; the page
            // itself picks the queued focus tile up on its next /player.json poll.
            Services.CoverageMapServer.FocusRequested += OnMapFocusRequested;
            Services.CoverageMapServer.TravelStateChanged += OnJourneyStateChanged;
        }

        private void OnJourneyPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(NavigationViewModel.IsTraveling) or nameof(WebwalkingViewModel.IsRunning) or
                nameof(WebwalkGraphViewModel.IsPathRunning) or nameof(NavigationViewModel.LastStatus) or nameof(WebwalkingViewModel.RunStatus))
                RefreshJourney(sender == _travel ? "travel" : sender == _routes ? "route" : "connection");
        }

        private void OnJourneyStateChanged() => RefreshJourney("map");

        private void RefreshJourney(string source)
        {
            if (_disposed) return;
            void Update()
            {
                if (_disposed) return;
                _journeySource = source;
                UpdateJourneyStatus();
            }
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) Update();
            else if (!dispatcher.HasShutdownStarted)
            {
                try { dispatcher.BeginInvoke((Action)Update); }
                catch (InvalidOperationException) { /* dispatcher closed during notification */ }
            }
        }

        private void UpdateJourneyStatus()
        {
            if (_disposed) return;
            var mapJourney = Services.CoverageMapServer.TravelState;
            var active = HasActiveJourney;
            if (IsJourneyActive != active)
            {
                IsJourneyActive = active;
                CommandManager.InvalidateRequerySuggested();
            }
            if (_travel?.IsTraveling == true) _journeySource = "travel";
            else if (_routes?.IsRunning == true) _journeySource = "route";
            else if (_graph?.IsPathRunning == true) _journeySource = "connection";
            else if (mapJourney.Running) _journeySource = "map";
            JourneyText = (_journeySource switch
            {
                "travel" => _travel?.LastStatus,
                "route" => _routes?.IsRunning == true ? _routes.RunStatus : _routes?.LastStatus,
                "connection" => _graph?.IsPathRunning == true ? "Running the connection test journey." : _graph?.LastStatus,
                "map" => mapJourney.Message,
                _ => null
            }) ?? "Choose a destination on the map or in Go To.";
            RaisePropertyChanged(nameof(JourneyHeading));
        }

        private void OnMapFocusRequested()
        {
            var dispatcher = Application.Current?.Dispatcher;
            dispatcher?.BeginInvoke(() =>
            {
                if (!_disposed)
                    ShowSection(NavigationHubSection.Map);
            });
        }

        public bool ConfirmClose() => _routes?.ConfirmDiscardDraft("close Navigation") ?? true;

        public void ShowSection(NavigationHubSection section)
        {
            if (_disposed) return;
            if (!_showDeveloperTools && IsDeveloperSection(section))
                section = NavigationHubSection.Map;
            if (section == CurrentSection && (IsMapSelected ? _map != null : CurrentDetailViewModel != null))
                return;

            var previous = ActiveChild();
            CurrentSection = section;
            CurrentDetailViewModel = section switch
            {
                NavigationHubSection.Travel => Travel,
                NavigationHubSection.Routes => Routes,
                NavigationHubSection.Graph => Graph,
                NavigationHubSection.Dungeons => Dungeons,
                NavigationHubSection.Status => Status,
                _ => null
            };
            var next = ActiveChild();

            if (_isActive && !ReferenceEquals(previous, next))
            {
                SafeDeactivate(previous);
                SafeActivate(next);
            }

            // Edits happen on the Routes/Graph screens (and on the map via its API), so
            // every section switch is a natural moment to re-check graph health.
            RunValidation();
        }

        private static bool IsDeveloperSection(NavigationHubSection section) =>
            section is NavigationHubSection.Routes or NavigationHubSection.Graph or NavigationHubSection.Dungeons;

        private object? ActiveChild() => IsMapSelected ? _map : CurrentDetailViewModel;

        public void OnActivated()
        {
            if (_disposed || _isActive) return;
            _isActive = true;
            if (IsMapSelected && _map == null)
                _ = Map; // create the landing screen lazily on first activation
            SafeActivate(ActiveChild());
            StartStatusTimer();
            RunValidation();
        }

        public void OnDeactivated()
        {
            if (!_isActive) return;
            _isActive = false;
            _statusTimer?.Stop();
            SafeDeactivate(ActiveChild());
        }

        private void StartStatusTimer()
        {
            if (_statusTimer == null)
            {
                _statusTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _statusTimer.Tick += (_, _) => UpdateStatusStrip();
            }

            UpdateStatusStrip();
            _statusTimer.Start();
        }

        /// <summary>
        /// One pass of the always-visible status strip: player tile, collision
        /// coverage under the player, nearest graph node, and recording state.
        /// All reads are cheap; failures degrade to "No session" rather than throwing.
        /// </summary>
        private void UpdateStatusStrip()
        {
            if (_disposed || !_isActive) return;

            IsRecordingActive = _graph?.IsRecording == true || Services.CoverageMapServer.IsRecording;
            UpdateJourneyStatus();

            NavigationDataStatus? navigationData = null;
            try { navigationData = NavigationDataManager.GetStatus(); } catch { }

            WorldPoint pos;
            try
            {
                pos = Traversal.GetCurrentPosition();
            }
            catch
            {
                pos = default;
            }

            if (pos.X <= 0 && pos.Y <= 0)
            {
                PlayerTileText = "No session";
                CollisionText = navigationData?.IsChecking == true && navigationData.IsInstalled == false
                    ? "Preparing navigation data…"
                    : navigationData?.IsInstalled == true
                        ? "Navigation data: installed"
                        : "Navigation data: limited";
                CollisionState = navigationData?.IsChecking == true ? "warn" : navigationData?.IsInstalled == true ? "ok" : "off";
                CacheText = "Definitions: —";
                CacheState = "off";
                NearestNodeText = "Nearest node: —";
                return;
            }

            PlayerTileText = $"{pos.X}, {pos.Y} · plane {pos.Z}";

            try
            {
                var definitions = CacheManager.GetStatus();
                CacheText = definitions.IsLoaded ? "Definitions: ready" : "Definitions: unavailable";
                CacheState = definitions.IsLoaded ? "ok" : "warn";
            }
            catch
            {
                CacheText = "Definitions: —";
                CacheState = "off";
            }

            try
            {
                if (NavigationReadiness.IsPreparing)
                {
                    CollisionText = "Preparing navigation data…";
                    CollisionState = "warn";
                }
                else if (!CollisionPathfinder.IsAvailable())
                {
                    CollisionText = navigationData?.IsChecking == true
                        ? "Preparing navigation data…"
                        : "Navigation data: limited";
                    CollisionState = navigationData?.IsChecking == true ? "warn" : "off";
                }
                else if (CollisionPathfinder.FindNearestWalkable(pos, 2) != null)
                {
                    CollisionText = "Navigation data: ready";
                    CollisionState = "ok";
                }
                else
                {
                    CollisionText = "Navigation data: partial";
                    CollisionState = "warn";
                }
            }
            catch
            {
                CollisionText = "Navigation data: —";
                CollisionState = "off";
            }

            try
            {
                // Graph snapshot is cloned per call, so refresh the node list at a
                // slower cadence and scan the cache every tick.
                if (_nodeCache == null || --_nodeCacheCountdown <= 0)
                {
                    _nodeCache = WebwalkGraph.GetNodes();
                    _nodeCacheCountdown = 10;
                }

                WebwalkGraphNode? nearest = null;
                var best = int.MaxValue;
                foreach (var node in _nodeCache)
                {
                    if (node.Z != pos.Z) continue;
                    var d = Math.Max(Math.Abs(node.X - pos.X), Math.Abs(node.Y - pos.Y));
                    if (d < best)
                    {
                        best = d;
                        nearest = node;
                    }
                }

                NearestNodeText = nearest == null
                    ? "Nearest node: none on this plane"
                    : $"Nearest node: {(string.IsNullOrWhiteSpace(nearest.Name) ? nearest.Id : nearest.Name)} ({best} tiles)";
            }
            catch
            {
                NearestNodeText = "Nearest node: —";
            }
        }

        private static void SafeActivate(object? vm)
        {
            if (vm is IActivatableViewModel a)
                try { a.OnActivated(); } catch { }
        }

        private static void SafeDeactivate(object? vm)
        {
            if (vm is IActivatableViewModel a)
                try { a.OnDeactivated(); } catch { }
        }

        private void RunValidation()
        {
            if (_disposed) return;
            if (_isValidating)
            {
                _validationPending = true;
                return;
            }
            _isValidating = true;
            ValidationState = "busy";
            ValidationText = "Validating…";

            var dispatcher = Application.Current?.Dispatcher;
            var validationCts = new CancellationTokenSource();
            _validationCts = validationCts;
            Task.Run(() =>
            {
                string text, detail, state;
                string? stats = null;
                try
                {
                    var result = WebwalkGraph.ValidateGraph();
                    var graph = WebwalkGraph.GetGraph();
                    stats = $"{graph.Nodes.Count} nodes · {graph.Edges.Count} edges";
                    var errors = result.Errors.Count;
                    var warnings = result.Warnings.Count;

                    if (errors > 0)
                    {
                        state = "error";
                        text = $"{errors} error{(errors == 1 ? "" : "s")}" + (warnings > 0 ? $", {warnings} warn" : "");
                    }
                    else if (warnings > 0)
                    {
                        state = "warn";
                        text = $"{warnings} warning{(warnings == 1 ? "" : "s")}";
                    }
                    else
                    {
                        state = "ok";
                        text = "Graph OK";
                    }

                    var issueLines = result.Issues.Take(12)
                        .Select(i => $"[{i.Severity}] {i.Code}: {i.Message}");
                    detail = $"{graph.Nodes.Count} nodes, {graph.Edges.Count} edges.";
                    if (errors + warnings > 0)
                        detail += Environment.NewLine + string.Join(Environment.NewLine, issueLines);
                    else
                        detail += " No issues found.";
                    detail += Environment.NewLine + "Click to re-validate.";
                }
                catch (Exception ex)
                {
                    state = "error";
                    text = "Validation failed";
                    detail = ex.Message;
                }

                void Apply()
                {
                    try
                    {
                        if (_disposed || validationCts.IsCancellationRequested)
                        {
                            if (ReferenceEquals(_validationCts, validationCts))
                                _isValidating = false;
                            return;
                        }

                        ValidationState = state;
                        ValidationText = text;
                        ValidationDetail = detail;
                        if (stats != null) GraphStatsText = stats;
                        _nodeCache = null; // graph may have changed; refresh nearest-node cache
                        _isValidating = false;
                        if (_validationPending)
                        {
                            _validationPending = false;
                            RunValidation();
                        }
                    }
                    finally
                    {
                        if (ReferenceEquals(_validationCts, validationCts))
                            _validationCts = null;
                        validationCts.Dispose();
                    }
                }

                try
                {
                    if (dispatcher != null)
                        dispatcher.BeginInvoke((Action)Apply);
                    else
                        Apply();
                }
                catch
                {
                    if (ReferenceEquals(_validationCts, validationCts))
                        _isValidating = false;
                    validationCts.Dispose();
                }
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _validationCts?.Cancel(); } catch { }
            Services.CoverageMapServer.FocusRequested -= OnMapFocusRequested;
            Services.CoverageMapServer.TravelStateChanged -= OnJourneyStateChanged;
            if (_travel != null) _travel.PropertyChanged -= OnJourneyPropertyChanged;
            if (_routes != null) _routes.PropertyChanged -= OnJourneyPropertyChanged;
            if (_graph != null) _graph.PropertyChanged -= OnJourneyPropertyChanged;
            _statusTimer?.Stop();

            foreach (var child in new object?[] { _map, _travel, _routes, _graph, _dungeons, _status })
            {
                SafeDeactivate(child);
                if (child is IDisposable d)
                    try { d.Dispose(); } catch { }
            }
        }
    }
}
