using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MESharp.API;
using MESharp.Commands;

namespace MESharp.ViewModels
{
    public enum NavigationHubSection
    {
        Map,
        Travel,
        Routes,
        Graph
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

        private NavigationHubSection _currentSection = NavigationHubSection.Map;
        private object? _currentDetailViewModel;
        private bool _isActive;
        private bool _disposed;

        private string _validationText = "Not validated";
        private string _validationDetail = "Graph has not been validated yet.";
        private string _validationState = "busy"; // ok | warn | error | busy
        private bool _isValidating;

        // ── Live status strip ────────────────────────────────────────────────
        private System.Windows.Threading.DispatcherTimer? _statusTimer;
        private string _playerTileText = "No session";
        private string _collisionText = "Collision: —";
        private string _collisionState = "off"; // ok | warn | off
        private string _nearestNodeText = "Nearest node: —";
        private string _graphStatsText = "Graph: —";
        private bool _isRecordingActive;
        private System.Collections.Generic.IReadOnlyList<WebwalkGraphNode>? _nodeCache;
        private int _nodeCacheCountdown;

        public MapViewModel Map => _map ??= new MapViewModel();
        private NavigationViewModel Travel => _travel ??= new NavigationViewModel();
        private WebwalkingViewModel Routes => _routes ??= new WebwalkingViewModel();
        private WebwalkGraphViewModel Graph => _graph ??= new WebwalkGraphViewModel();

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
                    RaisePropertyChanged(nameof(MapVisibility));
                    RaisePropertyChanged(nameof(DetailVisibility));
                }
            }
        }

        public bool IsMapSelected => CurrentSection == NavigationHubSection.Map;
        public bool IsTravelSelected => CurrentSection == NavigationHubSection.Travel;
        public bool IsRoutesSelected => CurrentSection == NavigationHubSection.Routes;
        public bool IsGraphSelected => CurrentSection == NavigationHubSection.Graph;

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
        public string NearestNodeText { get => _nearestNodeText; private set => SetProperty(ref _nearestNodeText, value); }
        public string GraphStatsText { get => _graphStatsText; private set => SetProperty(ref _graphStatsText, value); }

        /// <summary>True while either recorder (Graph Data pane or record-from-map) is running.</summary>
        public bool IsRecordingActive { get => _isRecordingActive; private set => SetProperty(ref _isRecordingActive, value); }

        public ICommand ShowMapCommand { get; }
        public ICommand ShowTravelCommand { get; }
        public ICommand ShowRoutesCommand { get; }
        public ICommand ShowGraphCommand { get; }
        public ICommand RefreshValidationCommand { get; }

        public NavigationHubViewModel()
        {
            ShowMapCommand = new RelayCommand(_ => ShowSection(NavigationHubSection.Map));
            ShowTravelCommand = new RelayCommand(_ => ShowSection(NavigationHubSection.Travel));
            ShowRoutesCommand = new RelayCommand(_ => ShowSection(NavigationHubSection.Routes));
            ShowGraphCommand = new RelayCommand(_ => ShowSection(NavigationHubSection.Graph));
            RefreshValidationCommand = new RelayCommand(_ => RunValidation());

            // "Show on map" from the Routes/Graph panes: flip to the map; the page
            // itself picks the queued focus tile up on its next /player.json poll.
            Services.CoverageMapServer.FocusRequested += OnMapFocusRequested;
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

        public void ShowSection(NavigationHubSection section)
        {
            if (_disposed) return;
            if (section == CurrentSection && (IsMapSelected ? _map != null : CurrentDetailViewModel != null))
                return;

            var previous = ActiveChild();
            CurrentSection = section;
            CurrentDetailViewModel = section switch
            {
                NavigationHubSection.Travel => Travel,
                NavigationHubSection.Routes => Routes,
                NavigationHubSection.Graph => Graph,
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
                CollisionText = "Collision: —";
                CollisionState = "off";
                NearestNodeText = "Nearest node: —";
                return;
            }

            PlayerTileText = $"{pos.X}, {pos.Y} · plane {pos.Z}";

            try
            {
                if (!CollisionPathfinder.IsAvailable())
                {
                    CollisionText = "Collision: no grids";
                    CollisionState = "off";
                }
                else if (CollisionPathfinder.FindNearestWalkable(pos, 2) != null)
                {
                    CollisionText = "Collision: covered";
                    CollisionState = "ok";
                }
                else
                {
                    CollisionText = "Collision: no data here";
                    CollisionState = "warn";
                }
            }
            catch
            {
                CollisionText = "Collision: —";
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
            if (_isValidating || _disposed) return;
            _isValidating = true;
            ValidationState = "busy";
            ValidationText = "Validating…";

            var dispatcher = Application.Current?.Dispatcher;
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
                    ValidationState = state;
                    ValidationText = text;
                    ValidationDetail = detail;
                    if (stats != null) GraphStatsText = stats;
                    _nodeCache = null; // graph may have changed; refresh nearest-node cache
                    _isValidating = false;
                }

                if (dispatcher != null) dispatcher.Invoke(Apply);
                else Apply();
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Services.CoverageMapServer.FocusRequested -= OnMapFocusRequested;
            _statusTimer?.Stop();

            foreach (var child in new object?[] { _map, _travel, _routes, _graph })
            {
                SafeDeactivate(child);
                if (child is IDisposable d)
                    try { d.Dispose(); } catch { }
            }
        }
    }
}
