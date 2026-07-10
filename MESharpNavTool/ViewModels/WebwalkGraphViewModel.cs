using MESharp.API;
using MESharp.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace MESharp.ViewModels
{
    public sealed class GraphNodeDisplay
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int X { get; init; }
        public int Y { get; init; }
        public int Z { get; init; }
        public string Tags { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        public string Notes { get; init; } = string.Empty;
    }

    public sealed class GraphAreaDisplay
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Z { get; init; }
        public string Shape { get; init; } = string.Empty;   // "rect" or "polygon (N)"
        public string Bounds { get; init; } = string.Empty;  // human-readable extent
        public string Tags { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        // Centre, for "show on map".
        public int CenterX { get; init; }
        public int CenterY { get; init; }
    }

    public sealed class GraphRouteDisplay
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public int WaypointCount { get; init; }
        public override string ToString() => string.IsNullOrWhiteSpace(Id) ? Name : Id;
    }

    public sealed class GraphEdgeDisplay
    {
        public string Id { get; init; } = string.Empty;
        public string From { get; init; } = string.Empty;
        public string To { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string? RouteId { get; init; }
        public string? LodestoneDestination { get; init; }
        public string? ShortcutObject { get; init; }
        public int CostMs { get; init; }
        public int? ObservedCostMs { get; init; }
        public bool Reversible { get; init; }
        public int DangerLevel { get; init; }
        public bool Enabled { get; init; } = true;
        public string Notes { get; init; } = string.Empty;
        public string RequirementsJson { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public bool HasBrokenRef { get; init; }
        public string IssueSummary { get; init; } = string.Empty;
    }

    public sealed class WebwalkGraphViewModel : BaseViewModel, IDisposable, IActivatableViewModel
    {
        // Matches the casing webwalk_graph.json uses so the editor box shows familiar JSON.
        private static readonly System.Text.Json.JsonSerializerOptions RequirementsJsonOptions = new()
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _recorderTimer;
        private CancellationTokenSource? _pathRunCts;
        private bool _isActive;
        private bool _disposed;

        // ── Status ────────────────────────────────────────────────────────────────
        private string _lastStatus = "Ready.";
        private string _currentTile = "--";
        public string LastStatus { get => _lastStatus; set => SetProperty(ref _lastStatus, value); }
        public string CurrentTile { get => _currentTile; set => SetProperty(ref _currentTile, value); }

        // ── Recorder ─────────────────────────────────────────────────────────────
        private bool _isRecording;
        private int _recordIntervalSeconds = 3;
        private int _minWaypointDistance = 4;
        private string _recordRouteName = "recorded.route";
        private WorldPoint? _lastRecordedPoint;
        private string _recordingSummary = "0 waypoints.";

        public bool IsRecording { get => _isRecording; set { SetProperty(ref _isRecording, value); RefreshCommandStates(); } }
        public int RecordIntervalSeconds { get => _recordIntervalSeconds; set => SetProperty(ref _recordIntervalSeconds, value); }
        public int MinWaypointDistance { get => _minWaypointDistance; set => SetProperty(ref _minWaypointDistance, value); }
        public string RecordRouteName { get => _recordRouteName; set { SetProperty(ref _recordRouteName, value); RefreshCommandStates(); } }
        public string RecordingSummary { get => _recordingSummary; set => SetProperty(ref _recordingSummary, value); }

        public ObservableCollection<string> RecordedTiles { get; } = new();

        private readonly List<WebwalkRecordedSample> _recordedSamples = new();
        // Unfiltered tick-by-tick trail: segmentation needs time-dense samples for
        // dwell/teleport detection, which the distance-filtered list cannot provide.
        private readonly List<WebwalkRecordedSample> _rawSamples = new();
        private readonly List<WebwalkAuthoring.WebwalkObstacleCandidate> _recordedObstacles = new();
        private DateTime _recordStartUtc;
        private DateTime _lastWaypointCaptureUtc;

        // Fixed raw-sample cadence, matching the map recorder, so dwell/teleport
        // segmentation quality no longer depends on which recorder was used.
        private const int RawSampleIntervalMs = 600;

        // ── Graph view ────────────────────────────────────────────────────────────
        public ObservableCollection<GraphNodeDisplay> Nodes { get; } = new();
        public ObservableCollection<GraphEdgeDisplay> Edges { get; } = new();
        public ObservableCollection<GraphRouteDisplay> Routes { get; } = new();
        public ObservableCollection<GraphAreaDisplay> Areas { get; } = new();
        public ObservableCollection<string> ValidationIssues { get; } = new();
        public ObservableCollection<string> ActivityLog { get; } = new();

        private GraphNodeDisplay? _selectedNode;
        private GraphEdgeDisplay? _selectedEdge;
        private GraphRouteDisplay? _selectedRoute;
        private GraphAreaDisplay? _selectedArea;
        private bool _hasValidationErrors;
        private bool _hasValidationWarnings;
        private bool _isPathRunning;
        private string _pathFromNodeId = string.Empty;
        private string _pathToNodeId = string.Empty;
        private string _pathPreview = "Select endpoints and preview a path.";
        private string _pathRunStatus = "Idle.";

        public GraphNodeDisplay? SelectedNode { get => _selectedNode; set { if (SetProperty(ref _selectedNode, value)) RefreshCommandStates(); } }
        public GraphEdgeDisplay? SelectedEdge { get => _selectedEdge; set { if (SetProperty(ref _selectedEdge, value)) RefreshCommandStates(); } }
        public GraphRouteDisplay? SelectedRoute { get => _selectedRoute; set { if (SetProperty(ref _selectedRoute, value)) { if (value != null) NewEdgeRouteId = value.Id; RefreshCommandStates(); } } }
        public GraphAreaDisplay? SelectedArea { get => _selectedArea; set { if (SetProperty(ref _selectedArea, value)) RefreshCommandStates(); } }
        public bool HasValidationErrors { get => _hasValidationErrors; set => SetProperty(ref _hasValidationErrors, value); }
        public bool HasValidationWarnings { get => _hasValidationWarnings; set => SetProperty(ref _hasValidationWarnings, value); }
        public bool IsPathRunning { get => _isPathRunning; set { SetProperty(ref _isPathRunning, value); RefreshCommandStates(); } }
        public string PathFromNodeId { get => _pathFromNodeId; set { SetProperty(ref _pathFromNodeId, value); RefreshCommandStates(); } }
        public string PathToNodeId { get => _pathToNodeId; set { SetProperty(ref _pathToNodeId, value); RefreshCommandStates(); } }
        public string PathPreview { get => _pathPreview; set => SetProperty(ref _pathPreview, value); }
        public string PathRunStatus { get => _pathRunStatus; set => SetProperty(ref _pathRunStatus, value); }

        // ── Node editor ───────────────────────────────────────────────────────────
        private string _newNodeId = string.Empty;
        private string _newNodeName = string.Empty;
        private int _newNodeX;
        private int _newNodeY;
        private int _newNodeZ;
        private string _newNodeTags = string.Empty;
        private string _newNodeNotes = string.Empty;
        private bool _newNodeEnabled = true;
        public string NewNodeId { get => _newNodeId; set { SetProperty(ref _newNodeId, value); RefreshCommandStates(); } }
        public string NewNodeName { get => _newNodeName; set { SetProperty(ref _newNodeName, value); RefreshCommandStates(); } }
        public int NewNodeX { get => _newNodeX; set => SetProperty(ref _newNodeX, value); }
        public int NewNodeY { get => _newNodeY; set => SetProperty(ref _newNodeY, value); }
        public int NewNodeZ { get => _newNodeZ; set => SetProperty(ref _newNodeZ, value); }
        public string NewNodeTags { get => _newNodeTags; set => SetProperty(ref _newNodeTags, value); }
        public string NewNodeNotes { get => _newNodeNotes; set => SetProperty(ref _newNodeNotes, value); }
        public bool NewNodeEnabled { get => _newNodeEnabled; set => SetProperty(ref _newNodeEnabled, value); }

        // ── Edge editor ───────────────────────────────────────────────────────────
        private string _newEdgeId = string.Empty;
        private string _newEdgeFrom = string.Empty;
        private string _newEdgeTo = string.Empty;
        private string _newEdgeKind = "route";
        private string _newEdgeRouteId = string.Empty;
        private string _newEdgeLodestoneDestination = string.Empty;
        private string _newEdgeObjectName = string.Empty;
        private int _newEdgeActionIndex = 1;
        private int _newEdgeCostMs = 15000;
        private int _newEdgeDangerLevel;
        private bool _newEdgeReversible;
        private bool _newEdgeEnabled = true;
        private string _newEdgeNotes = string.Empty;
        private string _newEdgeRequirementsJson = string.Empty;
        public string NewEdgeId { get => _newEdgeId; set { SetProperty(ref _newEdgeId, value); RefreshCommandStates(); } }
        public string NewEdgeFrom { get => _newEdgeFrom; set { SetProperty(ref _newEdgeFrom, value); RefreshCommandStates(); } }
        public string NewEdgeTo { get => _newEdgeTo; set { SetProperty(ref _newEdgeTo, value); RefreshCommandStates(); } }
        public string NewEdgeKind { get => _newEdgeKind; set { SetProperty(ref _newEdgeKind, value); RefreshCommandStates(); } }
        public string NewEdgeRouteId { get => _newEdgeRouteId; set { SetProperty(ref _newEdgeRouteId, value); RefreshCommandStates(); } }
        public string NewEdgeLodestoneDestination { get => _newEdgeLodestoneDestination; set { SetProperty(ref _newEdgeLodestoneDestination, value); RefreshCommandStates(); } }
        public string NewEdgeObjectName { get => _newEdgeObjectName; set { SetProperty(ref _newEdgeObjectName, value); RefreshCommandStates(); } }
        public int NewEdgeActionIndex { get => _newEdgeActionIndex; set => SetProperty(ref _newEdgeActionIndex, value); }
        public int NewEdgeCostMs { get => _newEdgeCostMs; set => SetProperty(ref _newEdgeCostMs, value); }
        public int NewEdgeDangerLevel { get => _newEdgeDangerLevel; set => SetProperty(ref _newEdgeDangerLevel, value); }
        public bool NewEdgeReversible { get => _newEdgeReversible; set => SetProperty(ref _newEdgeReversible, value); }
        public bool NewEdgeEnabled { get => _newEdgeEnabled; set => SetProperty(ref _newEdgeEnabled, value); }
        public string NewEdgeNotes { get => _newEdgeNotes; set => SetProperty(ref _newEdgeNotes, value); }
        public string NewEdgeRequirementsJson { get => _newEdgeRequirementsJson; set => SetProperty(ref _newEdgeRequirementsJson, value); }

        /// <summary>Node id suggestions for the From/To and Path Preview combos ("*" first for wildcard edges).</summary>
        public ObservableCollection<string> NodeIdOptions { get; } = new();

        public static IReadOnlyList<string> LodestoneNames { get; } =
            Enum.GetNames(typeof(Lodestone)).OrderBy(n => n).ToList();

        // ── Commands ──────────────────────────────────────────────────────────────
        public ICommand StartRecordingCommand { get; }
        public ICommand StopRecordingCommand { get; }
        public ICommand ClearRecordingCommand { get; }
        public ICommand SaveRecordingAsRouteCommand { get; }
        public ICommand UseCurrentTileForNodeCommand { get; }
        public ICommand SaveNodeCommand { get; }
        public ICommand LoadSelectedNodeCommand { get; }
        public ICommand DeleteSelectedNodeCommand { get; }
        public ICommand UseSelectedNodeAsFromCommand { get; }
        public ICommand UseSelectedNodeAsToCommand { get; }
        public ICommand ShowNodeOnMapCommand { get; }
        public ICommand DeleteSelectedAreaCommand { get; }
        public ICommand ShowAreaOnMapCommand { get; }
        public ICommand UseSelectedRouteForEdgeCommand { get; }
        public ICommand ValidateSelectedRouteCommand { get; }
        public ICommand LoadSelectedEdgeCommand { get; }
        public ICommand DeleteSelectedEdgeCommand { get; }
        public ICommand SaveEdgeCommand { get; }
        public ICommand SaveRecordingAndEdgeCommand { get; }
        public ICommand SegmentRecordingCommand { get; }
        public ICommand PreviewPathCommand { get; }
        public ICommand RunPreviewPathCommand { get; }
        public ICommand StopPathCommand { get; }
        public ICommand RefreshGraphCommand { get; }
        public ICommand ValidateGraphCommand { get; }
        public ICommand OpenCoverageMapCommand { get; }
        public ICommand OpenGraphFileCommand { get; }
        public ICommand ShowHelpCommand { get; }

        public WebwalkGraphViewModel()
        {
            StartRecordingCommand = new RelayCommand(_ => StartRecording(), _ => !IsRecording);
            StopRecordingCommand = new RelayCommand(_ => StopRecording(), _ => IsRecording);
            ClearRecordingCommand = new RelayCommand(_ => ClearRecording(), _ => !IsRecording);
            SaveRecordingAsRouteCommand = new RelayCommand(_ => SaveRecordingAsRoute(), _ => !IsRecording && _recordedSamples.Count > 0 && !string.IsNullOrWhiteSpace(RecordRouteName));
            UseCurrentTileForNodeCommand = new RelayCommand(_ => UseCurrentTileForNode(), _ => !IsRecording);
            SaveNodeCommand = new RelayCommand(_ => SaveNode(), _ => !string.IsNullOrWhiteSpace(NewNodeId) && !string.IsNullOrWhiteSpace(NewNodeName));
            LoadSelectedNodeCommand = new RelayCommand(_ => LoadSelectedNode(), _ => SelectedNode != null);
            DeleteSelectedNodeCommand = new RelayCommand(_ => DeleteSelectedNode(), _ => SelectedNode != null);
            UseSelectedNodeAsFromCommand = new RelayCommand(_ => UseSelectedNodeAsFrom(), _ => SelectedNode != null);
            UseSelectedNodeAsToCommand = new RelayCommand(_ => UseSelectedNodeAsTo(), _ => SelectedNode != null);
            ShowNodeOnMapCommand = new RelayCommand(_ =>
            {
                if (SelectedNode is { } n)
                    Services.CoverageMapServer.RequestFocus(new WorldPoint(n.X, n.Y, n.Z));
            }, _ => SelectedNode != null);
            DeleteSelectedAreaCommand = new RelayCommand(_ => DeleteSelectedArea(), _ => SelectedArea != null);
            ShowAreaOnMapCommand = new RelayCommand(_ =>
            {
                if (SelectedArea is { } ar)
                    Services.CoverageMapServer.RequestFocus(new WorldPoint(ar.CenterX, ar.CenterY, ar.Z));
            }, _ => SelectedArea != null);
            UseSelectedRouteForEdgeCommand = new RelayCommand(_ => UseSelectedRouteForEdge(), _ => SelectedRoute != null);
            ValidateSelectedRouteCommand = new RelayCommand(_ => ValidateSelectedRoute(), _ => SelectedRoute != null);
            LoadSelectedEdgeCommand = new RelayCommand(_ => LoadSelectedEdge(), _ => SelectedEdge != null);
            DeleteSelectedEdgeCommand = new RelayCommand(_ => DeleteSelectedEdge(), _ => SelectedEdge != null);
            SaveEdgeCommand = new RelayCommand(_ => SaveEdge(), _ => CanSaveEdge(requireEdgeId: true));
            SaveRecordingAndEdgeCommand = new RelayCommand(_ => SaveRecordingAndEdge(), _ => CanSaveRecordingAndEdge());
            SegmentRecordingCommand = new RelayCommand(_ => SegmentRecording(), _ => !IsRecording && _rawSamples.Count > 1 && !string.IsNullOrWhiteSpace(RecordRouteName));
            PreviewPathCommand = new RelayCommand(_ => PreviewPath(), _ => CanPlanPath());
            RunPreviewPathCommand = new RelayCommand(_ => RunPreviewPath(), _ => CanPlanPath() && !IsPathRunning);
            StopPathCommand = new RelayCommand(_ => StopPath(), _ => IsPathRunning);
            RefreshGraphCommand = new RelayCommand(_ => RefreshGraph());
            ValidateGraphCommand = new RelayCommand(_ => ValidateGraph());
            OpenCoverageMapCommand = new RelayCommand(_ => OpenCoverageMap());
            OpenGraphFileCommand = new RelayCommand(_ => OpenGraphFile());
            ShowHelpCommand = new RelayCommand(_ =>
            {
                try { new Views.SectionHelpWindow(Views.NavHelpContent.GraphData()).ShowDialog(); }
                catch (Exception ex) { LastStatus = $"Could not open help: {ex.Message}"; }
            });

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(750) };
            _refreshTimer.Tick += (_, __) => UpdateCurrentTile();

            _recorderTimer = new DispatcherTimer(DispatcherPriority.Background);
            _recorderTimer.Tick += (_, __) => RecordTick();

            RefreshGraph();
            ValidateGraph();
            AddLog("Graph editor ready.");
        }

        public void OnActivated()
        {
            if (_disposed || _isActive) return;
            _isActive = true;
            _refreshTimer.Start();
            UpdateCurrentTile();
        }

        public void OnDeactivated()
        {
            if (_disposed || !_isActive) return;
            _isActive = false;
            _refreshTimer.Stop();
            if (IsRecording) StopRecording();
        }

        private void StartRecording()
        {
            // A new recording must be an independent trail. Retaining samples from the
            // previous session causes segmentation to invent a teleport between sessions.
            ResetRecordingSession();
            _lastRecordedPoint = null;
            _recordStartUtc = DateTime.UtcNow;
            // Click-awareness: the pump drains the native DoAction hook with real
            // timestamps + player tiles, so doors/shortcuts/stairs clicked while
            // recording become transition/obstacle candidates instead of invisible.
            DoActionDebugSignals.StartNativePump();
            IsRecording = true;
            _lastWaypointCaptureUtc = DateTime.MinValue;
            // Raw trail is always sampled fast; the interval/min-distance settings act as
            // waypoint-capture filters in RecordTick, so changing them mid-recording works.
            _recorderTimer.Interval = TimeSpan.FromMilliseconds(RawSampleIntervalMs);
            _recorderTimer.Start();
            AddLog($"Recording started (waypoint interval={Math.Max(1, RecordIntervalSeconds)}s, minDist={MinWaypointDistance} tiles; raw trail at {RawSampleIntervalMs}ms). Object clicks are captured as obstacle candidates.");
        }

        private void StopRecording()
        {
            _recorderTimer.Stop();
            IsRecording = false;

            try
            {
                var signals = DoActionDebugSignals.Snapshot(500);
                var obstacles = WebwalkAuthoring.CollectObstacleCandidates(signals, _recordStartUtc, DateTime.UtcNow);
                _recordedObstacles.Clear();
                _recordedObstacles.AddRange(obstacles);
                foreach (var o in obstacles)
                    AddLog($"Obstacle candidate: object {o.ObjectId} (action {o.ActionOp}) near ({o.Tile.X}, {o.Tile.Y}, {o.Tile.Z}).");
            }
            catch { /* signal capture is best-effort */ }
            finally
            {
                DoActionDebugSignals.StopNativePump();
            }

            AddLog($"Recording stopped. {_recordedSamples.Count} waypoints captured, {_recordedObstacles.Count} obstacle candidate(s).");
        }

        private void ClearRecording()
        {
            ResetRecordingSession();
            AddLog("Recording cleared.");
        }

        private void ResetRecordingSession()
        {
            _recordedSamples.Clear();
            _rawSamples.Clear();
            _recordedObstacles.Clear();
            RecordedTiles.Clear();
            _lastRecordedPoint = null;
            UpdateRecordingSummary();
        }

        private void RecordTick()
        {
            try
            {
                var tile = LocalPlayer.GetTilePosition();
                var pos = new WorldPoint(tile.x, tile.y, tile.z);

                _rawSamples.Add(new WebwalkRecordedSample { Position = pos });

                var now = DateTime.UtcNow;
                if ((now - _lastWaypointCaptureUtc).TotalSeconds < Math.Max(1, RecordIntervalSeconds))
                    return;
                if (!WebwalkAuthoring.ShouldRecordSample(pos, _lastRecordedPoint, MinWaypointDistance))
                    return;

                _lastWaypointCaptureUtc = now;
                _recordedSamples.Add(new WebwalkRecordedSample { Position = pos });
                _lastRecordedPoint = pos;
                RecordedTiles.Insert(0, $"[{_recordedSamples.Count}] ({tile.x}, {tile.y}, {tile.z})");
                if (RecordedTiles.Count > 100) RecordedTiles.RemoveAt(RecordedTiles.Count - 1);
                UpdateRecordingSummary();
            }
            catch { }
        }

        private void SaveRecordingAsRoute()
        {
            TrySaveRecordingRoute();
        }

        private bool TrySaveRecordingRoute()
        {
            if (_recordedSamples.Count == 0)
            {
                LastStatus = "No waypoints recorded.";
                return false;
            }

            var name = RecordRouteName.Trim();
            var route = WebwalkAuthoring.CreateRouteFromSamples(
                name,
                _recordedSamples,
                new WebwalkRecordingOptions { MinWaypointDistance = MinWaypointDistance });
            var summary = WebwalkAuthoring.SummarizeSamples(_recordedSamples);

            var transitions = WebwalkAuthoring.ApplyTransitions(route, _recordedObstacles);
            if (transitions > 0)
                AddLog($"Marked {transitions} waypoint(s) as transitions from recorded object clicks.");

            if (Webwalking.TrySaveRoute(route, out var error))
            {
                LastStatus = $"Saved route '{name}' ({summary.WaypointCount} waypoints, ~{summary.EstimatedRunTicks} ticks).";
                AddLog(LastStatus);
                NewEdgeRouteId = route.Id ?? name.ToLowerInvariant().Replace(' ', '_');
                RefreshRoutes();
                SelectedRoute = Routes.FirstOrDefault(r => string.Equals(r.Id, NewEdgeRouteId, StringComparison.OrdinalIgnoreCase));
                AutoFillEdgeId();
                return true;
            }
            else
            {
                LastStatus = $"Save failed: {error}";
                AddLog(LastStatus);
                return false;
            }
        }

        private void SaveRecordingAndEdge()
        {
            if (!TrySaveRecordingRoute())
                return;

            if (string.IsNullOrWhiteSpace(NewEdgeRouteId))
                return;

            if (string.IsNullOrWhiteSpace(NewEdgeId))
                NewEdgeId = GenerateEdgeId(NewEdgeFrom, NewEdgeTo, NewEdgeRouteId);

            SaveEdge();
        }

        /// <summary>
        /// Split the raw recorded trail into walk/teleport segments and save each walk
        /// segment as its own route ("name_seg1..N"). Teleport segments are logged as
        /// edge candidates for manual linking.
        /// </summary>
        private void SegmentRecording()
        {
            var baseName = RecordRouteName.Trim();
            var segments = WebwalkAuthoring.SegmentSamples(_rawSamples);
            if (segments.Count == 0)
            {
                LastStatus = "Segmentation produced no segments (trail too short?).";
                AddLog(LastStatus);
                return;
            }

            var savedRoutes = 0;
            var walkIndex = 0;
            foreach (var segment in segments)
            {
                if (segment.Kind == WebwalkSegmentKind.Teleport)
                {
                    // If an object click landed near the jump origin, name it — that's
                    // the staircase/ladder/portal a shortcut edge should dispatch.
                    var via = _recordedObstacles.FirstOrDefault(o =>
                        o.Tile.Z == segment.Start.Z &&
                        Math.Max(Math.Abs(o.Tile.X - segment.Start.X), Math.Abs(o.Tile.Y - segment.Start.Y)) <= 8);
                    AddLog($"Teleport candidate: {segment.Start} → {segment.End} ({segment.DistanceTiles} tiles, {segment.DurationMs / 1000.0:0.0}s)" +
                        (via != null
                            ? $" — likely via object {via.ObjectId} (action {via.ActionOp}); author a shortcut edge with it."
                            : " — link manually as a teleport edge."));
                    continue;
                }

                walkIndex++;
                var route = WebwalkAuthoring.CreateRouteFromSegment(
                    $"{baseName}_seg{walkIndex}",
                    segment,
                    new WebwalkRecordingOptions { MinWaypointDistance = MinWaypointDistance });

                var transitions = WebwalkAuthoring.ApplyTransitions(route, _recordedObstacles);
                if (transitions > 0)
                    AddLog($"Segment '{route.Name}': {transitions} waypoint(s) marked as transitions from recorded object clicks.");

                if (Webwalking.TrySaveRoute(route, out var error))
                {
                    savedRoutes++;
                    AddLog($"Saved segment route '{route.Name}': {segment.Start} → {segment.End}, {segment.DistanceTiles} tiles{(segment.EndedAtDwell ? ", ended at dwell (node candidate)" : "")}.");
                }
                else
                {
                    AddLog($"Segment '{baseName}_seg{walkIndex}' save failed: {error}");
                }
            }

            LastStatus = $"Segmentation: {segments.Count} segment(s), {savedRoutes} route(s) saved.";
            AddLog(LastStatus);
            RefreshRoutes();
        }

        private void UpdateRecordingSummary()
        {
            var summary = WebwalkAuthoring.SummarizeSamples(_recordedSamples);
            RecordingSummary = summary.WaypointCount == 0
                ? "0 waypoints."
                : $"{summary.WaypointCount} waypoints, ~{summary.ApproxDistanceTiles} tiles, ~{summary.EstimatedRunTicks} ticks / {summary.EstimatedRunMs / 1000.0:0.0}s running.";
            RefreshCommandStates();
        }

        private void UseCurrentTileForNode()
        {
            try
            {
                var tile = LocalPlayer.GetTilePosition();
                NewNodeX = tile.x;
                NewNodeY = tile.y;
                NewNodeZ = tile.z;
                LastStatus = $"Copied current tile ({tile.x}, {tile.y}, {tile.z}) to node editor.";
            }
            catch (Exception ex)
            {
                LastStatus = $"Failed to read tile: {ex.Message}";
            }
        }

        private void SaveNode()
        {
            var node = new WebwalkGraphNode
            {
                Id = NewNodeId.Trim(),
                Name = NewNodeName.Trim(),
                X = NewNodeX, Y = NewNodeY, Z = NewNodeZ,
                Tags = NewNodeTags.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Notes = string.IsNullOrWhiteSpace(NewNodeNotes) ? null : NewNodeNotes.Trim(),
                Enabled = NewNodeEnabled
            };

            if (WebwalkGraph.TrySaveNode(node, out var error))
            {
                LastStatus = $"Saved node '{node.Id}'.";
                AddLog(LastStatus);
                RefreshGraph();
                PathFromNodeId = string.IsNullOrWhiteSpace(PathFromNodeId) ? node.Id : PathFromNodeId;
            }
            else
            {
                LastStatus = $"Node save failed: {error}";
                AddLog(LastStatus);
            }
        }

        private void LoadSelectedNode()
        {
            if (SelectedNode == null) return;
            NewNodeId = SelectedNode.Id;
            NewNodeName = SelectedNode.Name;
            NewNodeX = SelectedNode.X;
            NewNodeY = SelectedNode.Y;
            NewNodeZ = SelectedNode.Z;
            NewNodeTags = SelectedNode.Tags;
            NewNodeNotes = SelectedNode.Notes;
            NewNodeEnabled = SelectedNode.Enabled;
            LastStatus = $"Loaded node '{SelectedNode.Id}' into the node editor.";
        }

        private static bool ConfirmDelete(string what)
        {
            return System.Windows.MessageBox.Show(
                $"Delete {what}? This cannot be undone.",
                "Confirm delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
        }

        private void DeleteSelectedNode()
        {
            if (SelectedNode == null) return;
            var nodeId = SelectedNode.Id;
            if (!ConfirmDelete($"node '{nodeId}' (any edges referencing it are deleted too)")) return;
            if (WebwalkGraph.TryDeleteNode(nodeId, out var error))
            {
                LastStatus = $"Deleted node '{nodeId}' and any referenced edges.";
                AddLog(LastStatus);
                if (string.Equals(PathFromNodeId, nodeId, StringComparison.OrdinalIgnoreCase)) PathFromNodeId = string.Empty;
                if (string.Equals(PathToNodeId, nodeId, StringComparison.OrdinalIgnoreCase)) PathToNodeId = string.Empty;
                RefreshGraph();
                ValidateGraph();
            }
            else
            {
                LastStatus = $"Node delete failed: {error}";
                AddLog(LastStatus);
            }
        }

        private void UseSelectedNodeAsFrom()
        {
            if (SelectedNode == null) return;
            NewEdgeFrom = SelectedNode.Id;
            PathFromNodeId = SelectedNode.Id;
            AutoFillEdgeId();
        }

        private void UseSelectedNodeAsTo()
        {
            if (SelectedNode == null) return;
            NewEdgeTo = SelectedNode.Id;
            PathToNodeId = SelectedNode.Id;
            AutoFillEdgeId();
        }

        private void UseSelectedRouteForEdge()
        {
            if (SelectedRoute == null) return;
            NewEdgeRouteId = SelectedRoute.Id;
            if (NewEdgeCostMs <= 1000)
                NewEdgeCostMs = EstimateRouteCostMs(SelectedRoute.WaypointCount);
            AutoFillEdgeId();
        }

        private void ValidateSelectedRoute()
        {
            if (SelectedRoute == null) return;
            if (!Webwalking.TryGetRoute(SelectedRoute.Id, out var route))
            {
                LastStatus = $"Route '{SelectedRoute.Id}' was not found.";
                AddLog(LastStatus);
                return;
            }

            var result = Webwalking.ValidateRoute(ToStoredRoute(route));
            LastStatus = result.IsValid
                ? $"Route '{route.Id}' is valid ({result.Warnings.Count} warning(s))."
                : $"Route '{route.Id}' has {result.Errors.Count} error(s), {result.Warnings.Count} warning(s).";
            AddLog(LastStatus);

            foreach (var issue in result.Issues.Take(8))
                AddLog($"Route {issue.Severity}: {issue.Code} - {issue.Message}");
        }

        private void LoadSelectedEdge()
        {
            if (SelectedEdge == null) return;
            NewEdgeId = SelectedEdge.Id;
            NewEdgeFrom = SelectedEdge.From;
            NewEdgeTo = SelectedEdge.To;
            NewEdgeKind = SelectedEdge.Kind;
            NewEdgeRouteId = SelectedEdge.RouteId ?? string.Empty;
            NewEdgeLodestoneDestination = SelectedEdge.LodestoneDestination ?? string.Empty;
            NewEdgeObjectName = SelectedEdge.ShortcutObject ?? string.Empty;
            NewEdgeCostMs = SelectedEdge.CostMs;
            NewEdgeDangerLevel = SelectedEdge.DangerLevel;
            NewEdgeReversible = SelectedEdge.Reversible;
            NewEdgeEnabled = SelectedEdge.Enabled;
            NewEdgeNotes = SelectedEdge.Notes;
            NewEdgeRequirementsJson = SelectedEdge.RequirementsJson;
            PathFromNodeId = SelectedEdge.From;
            PathToNodeId = SelectedEdge.To;
            LastStatus = $"Loaded edge '{SelectedEdge.Id}' into the edge editor.";
        }

        private void DeleteSelectedEdge()
        {
            if (SelectedEdge == null) return;
            var edgeId = SelectedEdge.Id;
            if (!ConfirmDelete($"edge '{edgeId}'")) return;
            if (WebwalkGraph.TryDeleteEdge(edgeId, out var error))
            {
                LastStatus = $"Deleted edge '{edgeId}'.";
                AddLog(LastStatus);
                RefreshGraph();
                ValidateGraph();
            }
            else
            {
                LastStatus = $"Edge delete failed: {error}";
                AddLog(LastStatus);
            }
        }

        private void SaveEdge()
        {
            var kind = string.IsNullOrWhiteSpace(NewEdgeKind) ? "route" : NewEdgeKind.Trim().ToLowerInvariant();

            WebwalkTeleportAction? teleportAction = null;
            if (string.Equals(kind, "lodestone", StringComparison.OrdinalIgnoreCase))
            {
                var dest = NewEdgeLodestoneDestination.Trim();
                if (string.IsNullOrWhiteSpace(dest))
                {
                    LastStatus = "Lodestone edges require a destination. Pick one from the list.";
                    AddLog(LastStatus);
                    return;
                }
                if (!Enum.TryParse<Lodestone>(dest, ignoreCase: true, out _))
                {
                    LastStatus = $"Unknown lodestone destination '{dest}'. Use the dropdown.";
                    AddLog(LastStatus);
                    return;
                }
                teleportAction = new WebwalkTeleportAction { Type = "lodestone", Destination = dest };
            }
            else if (string.Equals(kind, "shortcut", StringComparison.OrdinalIgnoreCase))
            {
                var objectName = NewEdgeObjectName.Trim();
                if (string.IsNullOrWhiteSpace(objectName))
                {
                    LastStatus = "Shortcut edges require the world object's name (e.g. 'Underwall tunnel').";
                    AddLog(LastStatus);
                    return;
                }
                teleportAction = new WebwalkTeleportAction
                {
                    Type = "object",
                    ObjectName = objectName,
                    ActionIndex = Math.Max(1, NewEdgeActionIndex)
                };
            }

            List<WebwalkRequirement> requirements = new();
            if (!string.IsNullOrWhiteSpace(NewEdgeRequirementsJson))
            {
                try
                {
                    requirements = System.Text.Json.JsonSerializer.Deserialize<List<WebwalkRequirement>>(
                        NewEdgeRequirementsJson, RequirementsJsonOptions) ?? new();
                }
                catch (Exception ex)
                {
                    LastStatus = $"Requirements JSON is invalid: {ex.Message}";
                    AddLog(LastStatus);
                    return;
                }
            }

            var edge = new WebwalkGraphEdge
            {
                Id = NewEdgeId.Trim(),
                FromNodeId = NewEdgeFrom.Trim(),
                ToNodeId = NewEdgeTo.Trim(),
                Kind = kind,
                RouteId = string.Equals(kind, "route", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(NewEdgeRouteId)
                    ? NewEdgeRouteId.Trim() : null,
                TeleportAction = teleportAction,
                CostMs = Math.Max(1000, NewEdgeCostMs),
                DangerLevel = NewEdgeDangerLevel,
                Reversible = string.Equals(kind, "route", StringComparison.OrdinalIgnoreCase) && NewEdgeReversible,
                Requirements = requirements,
                Notes = string.IsNullOrWhiteSpace(NewEdgeNotes) ? null : NewEdgeNotes.Trim(),
                Enabled = NewEdgeEnabled
            };

            if (WebwalkGraph.TrySaveEdge(edge, out var error))
            {
                LastStatus = $"Saved edge '{edge.Id}'.";
                AddLog(LastStatus);
                RefreshGraph();
                ValidateGraph();
            }
            else
            {
                LastStatus = $"Edge save failed: {error}";
                AddLog(LastStatus);
            }
        }

        private bool CanSaveEdge(bool requireEdgeId)
        {
            if (requireEdgeId && string.IsNullOrWhiteSpace(NewEdgeId)) return false;
            if (string.IsNullOrWhiteSpace(NewEdgeFrom)) return false;
            if (string.IsNullOrWhiteSpace(NewEdgeTo)) return false;
            var kind = (NewEdgeKind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind == "route") return !string.IsNullOrWhiteSpace(NewEdgeRouteId);
            if (kind == "lodestone") return !string.IsNullOrWhiteSpace(NewEdgeLodestoneDestination);
            if (kind == "shortcut") return !string.IsNullOrWhiteSpace(NewEdgeObjectName);
            return false;
        }

        private bool CanSaveRecordingAndEdge()
        {
            if (IsRecording) return false;
            if (_recordedSamples.Count == 0) return false;
            if (string.IsNullOrWhiteSpace(RecordRouteName)) return false;
            if (string.IsNullOrWhiteSpace(NewEdgeFrom)) return false;
            if (string.IsNullOrWhiteSpace(NewEdgeTo)) return false;
            return string.Equals(NewEdgeKind, "route", StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshGraph()
        {
            WebwalkGraph.ReloadGraph();
            RefreshRoutes();
            var graph = WebwalkGraph.GetGraph();

            var nodeIds = new HashSet<string>(graph.Nodes.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
            var routeIds = new HashSet<string>(Webwalking.GetRoutes().Select(r => r.Id), StringComparer.OrdinalIgnoreCase);

            var selectedNodeId = SelectedNode?.Id;
            var selectedEdgeId = SelectedEdge?.Id;
            Nodes.Clear();
            foreach (var n in graph.Nodes)
                Nodes.Add(new GraphNodeDisplay { Id = n.Id, Name = n.Name, X = n.X, Y = n.Y, Z = n.Z, Tags = string.Join(", ", n.Tags ?? new()), Enabled = n.Enabled, Notes = n.Notes ?? string.Empty });
            SelectedNode = string.IsNullOrWhiteSpace(selectedNodeId) ? null : Nodes.FirstOrDefault(n => string.Equals(n.Id, selectedNodeId, StringComparison.OrdinalIgnoreCase));

            NodeIdOptions.Clear();
            NodeIdOptions.Add("*");
            foreach (var id in graph.Nodes.Select(n => n.Id).OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
                NodeIdOptions.Add(id);

            Edges.Clear();
            foreach (var e in graph.Edges)
            {
                var brokenFrom = !e.IsWildcard && !nodeIds.Contains(e.FromNodeId);
                var brokenTo = !nodeIds.Contains(e.ToNodeId);
                var brokenRoute = string.Equals(e.Kind, "route", StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(e.RouteId) || !routeIds.Contains(e.RouteId));
                var unsupported = !WebwalkEdgeExecutors.ExecutableKinds.Contains(e.Kind, StringComparer.OrdinalIgnoreCase);
                var issues = new List<string>();
                if (brokenFrom) issues.Add("from node");
                if (brokenTo) issues.Add("to node");
                if (brokenRoute) issues.Add("route");
                if (unsupported) issues.Add("kind");
                Edges.Add(new GraphEdgeDisplay
                {
                    Id = e.Id, From = e.FromNodeId, To = e.ToNodeId, Kind = e.Kind,
                    RouteId = e.RouteId,
                    LodestoneDestination = e.TeleportAction?.Destination,
                    ShortcutObject = e.TeleportAction?.ObjectName,
                    CostMs = e.CostMs, ObservedCostMs = e.ObservedCostMs,
                    Reversible = e.Reversible, DangerLevel = e.DangerLevel,
                    Enabled = e.Enabled,
                    Notes = e.Notes ?? string.Empty,
                    Source = e.Source ?? string.Empty,
                    RequirementsJson = e.Requirements is { Count: > 0 }
                        ? System.Text.Json.JsonSerializer.Serialize(e.Requirements, RequirementsJsonOptions)
                        : string.Empty,
                    HasBrokenRef = issues.Count > 0,
                    IssueSummary = issues.Count == 0 ? string.Empty : "Broken: " + string.Join(", ", issues)
                });
            }
            SelectedEdge = string.IsNullOrWhiteSpace(selectedEdgeId) ? null : Edges.FirstOrDefault(e => string.Equals(e.Id, selectedEdgeId, StringComparison.OrdinalIgnoreCase));

            var selectedAreaId = SelectedArea?.Id;
            Areas.Clear();
            foreach (var a in graph.Areas.OrderBy(a => a.Name))
            {
                int minX, minY, maxX, maxY;
                if (a.IsPolygon)
                {
                    minX = a.Polygon!.Min(v => v.X); maxX = a.Polygon!.Max(v => v.X);
                    minY = a.Polygon!.Min(v => v.Y); maxY = a.Polygon!.Max(v => v.Y);
                }
                else { minX = a.MinX; maxX = a.MaxX; minY = a.MinY; maxY = a.MaxY; }

                Areas.Add(new GraphAreaDisplay
                {
                    Id = a.Id,
                    Name = a.Name,
                    Z = a.Z,
                    Shape = a.IsPolygon ? $"polygon ({a.Polygon!.Count})" : "rect",
                    Bounds = $"({minX},{minY})–({maxX},{maxY})",
                    Tags = string.Join(", ", a.Tags ?? new()),
                    Source = a.Source ?? string.Empty,
                    Enabled = a.Enabled,
                    CenterX = (minX + maxX) / 2,
                    CenterY = (minY + maxY) / 2
                });
            }
            SelectedArea = string.IsNullOrWhiteSpace(selectedAreaId) ? null : Areas.FirstOrDefault(a => string.Equals(a.Id, selectedAreaId, StringComparison.OrdinalIgnoreCase));

            LastStatus = $"Graph: {Nodes.Count} nodes, {Edges.Count} edges, {Areas.Count} areas. Store: {WebwalkGraph.GetGraphStorePath()}";
        }

        private void DeleteSelectedArea()
        {
            if (SelectedArea == null) return;
            var areaId = SelectedArea.Id;
            if (!ConfirmDelete($"area '{areaId}'")) return;
            if (WebwalkGraph.TryDeleteArea(areaId, out var error))
            {
                LastStatus = $"Deleted area '{areaId}'.";
                AddLog(LastStatus);
                RefreshGraph();
            }
            else
            {
                LastStatus = $"Area delete failed: {error}";
                AddLog(LastStatus);
            }
        }

        private void RefreshRoutes()
        {
            var selectedRouteId = SelectedRoute?.Id;
            Webwalking.ReloadRoutes();
            Routes.Clear();
            foreach (var route in Webwalking.GetRoutes().OrderBy(r => r.Category).ThenBy(r => r.Name))
            {
                Routes.Add(new GraphRouteDisplay
                {
                    Id = route.Id,
                    Name = route.Name,
                    Category = route.Category,
                    WaypointCount = route.Waypoints.Count
                });
            }
            SelectedRoute = string.IsNullOrWhiteSpace(selectedRouteId) ? null : Routes.FirstOrDefault(r => string.Equals(r.Id, selectedRouteId, StringComparison.OrdinalIgnoreCase));
        }

        private void PreviewPath()
        {
            if (!TryBuildPath(out var path, out var message))
            {
                PathPreview = message;
                LastStatus = PathPreview;
                AddLog(PathPreview);
                return;
            }

            var edgeList = path.Edges.Count == 0
                ? "(already at target)"
                : string.Join(" -> ", path.Edges.Select(e => e.Id));
            PathPreview = $"{path.Edges.Count} edge(s), ~{path.TotalCostMs / 1000.0:0.0}s: {edgeList}";
            LastStatus = PathPreview;
            AddLog("Path preview: " + PathPreview);
        }

        private async void RunPreviewPath()
        {
            if (!TryBuildPath(out var path, out var message))
            {
                PathRunStatus = message;
                LastStatus = message;
                AddLog(message);
                return;
            }

            _pathRunCts?.Dispose();
            _pathRunCts = new CancellationTokenSource();
            IsPathRunning = true;
            PathRunStatus = $"Running {path.Edges.Count} edge(s).";
            LastStatus = PathRunStatus;
            AddLog(PathRunStatus);

            try
            {
                var result = await Webwalking.RunPlanDetailedAsync(path, _pathRunCts.Token).ConfigureAwait(true);
                var edgeSummary = result.Edges.Count == 0
                    ? "no edges"
                    : string.Join(", ", result.Edges.Select((e, i) => $"{i + 1}:{e.EdgeId}={e.Succeeded}"));
                PathRunStatus = $"{result.Status}: {result.Message} ({edgeSummary})";
                LastStatus = PathRunStatus;
                AddLog("Path run: " + PathRunStatus);
            }
            catch (OperationCanceledException)
            {
                PathRunStatus = "Path run cancelled.";
                LastStatus = PathRunStatus;
                AddLog(PathRunStatus);
            }
            catch (Exception ex)
            {
                PathRunStatus = $"Path run error: {ex.Message}";
                LastStatus = PathRunStatus;
                AddLog(PathRunStatus);
            }
            finally
            {
                IsPathRunning = false;
                _pathRunCts?.Dispose();
                _pathRunCts = null;
            }
        }

        private void StopPath()
        {
            _pathRunCts?.Cancel();
            LastStatus = "Stopping path run...";
            AddLog(LastStatus);
        }

        private bool TryBuildPath(out WebwalkGraphPath path, out string message)
        {
            var from = PathFromNodeId.Trim();
            var to = PathToNodeId.Trim();
            var found = WebwalkGraph.FindPath(from, to);
            if (found == null)
            {
                path = new WebwalkGraphPath();
                message = $"No path from '{from}' to '{to}'.";
                return false;
            }

            path = found;
            message = string.Empty;
            return true;
        }

        private bool CanPlanPath()
            => !string.IsNullOrWhiteSpace(PathFromNodeId) && !string.IsNullOrWhiteSpace(PathToNodeId);

        /// <summary>
        /// Serve the Leaflet coverage map (with live graph + route data) on localhost and
        /// open it in the default browser.
        /// </summary>
        private void OpenCoverageMap()
        {
            try
            {
                var url = MESharp.Services.CoverageMapServer.Start();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                LastStatus = $"Coverage map: {url}";
                AddLog($"Coverage map served at {url} (live graph + routes; refresh the page after edits).");
            }
            catch (Exception ex)
            {
                LastStatus = $"Coverage map failed: {ex.Message}";
                AddLog(LastStatus);
            }
        }

        private void ValidateGraph()
        {
            var result = WebwalkGraph.ValidateGraph();
            ValidationIssues.Clear();
            foreach (var issue in result.Issues)
                ValidationIssues.Add($"[{issue.Severity.ToUpper()}] {issue.Code}: {issue.Message}");

            HasValidationErrors = result.Errors.Count > 0;
            HasValidationWarnings = result.Warnings.Count > 0;

            if (result.IsValid && result.Warnings.Count == 0)
                ValidationIssues.Add("Graph is valid — no issues found.");

            AddLog($"Validation: {result.Errors.Count} errors, {result.Warnings.Count} warnings.");
        }

        private void OpenGraphFile()
        {
            try
            {
                var path = WebwalkGraph.GetGraphStorePath();
                if (!System.IO.File.Exists(path))
                    System.IO.File.WriteAllText(path, "{}");
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                LastStatus = $"Could not open graph file: {ex.Message}";
            }
        }

        private void UpdateCurrentTile()
        {
            try
            {
                var tile = LocalPlayer.GetTilePosition();
                CurrentTile = $"{tile.x}, {tile.y}, {tile.z}";
            }
            catch { CurrentTile = "--"; }
        }

        private void AddLog(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            ActivityLog.Insert(0, entry);
            while (ActivityLog.Count > 40) ActivityLog.RemoveAt(ActivityLog.Count - 1);
        }

        private void AutoFillEdgeId()
        {
            if (!string.IsNullOrWhiteSpace(NewEdgeId)) return;
            if (string.IsNullOrWhiteSpace(NewEdgeFrom) || string.IsNullOrWhiteSpace(NewEdgeTo)) return;
            var qualifier = string.Equals(NewEdgeKind, "lodestone", StringComparison.OrdinalIgnoreCase)
                ? NewEdgeLodestoneDestination
                : NewEdgeRouteId;
            NewEdgeId = GenerateEdgeId(NewEdgeFrom, NewEdgeTo, qualifier);
        }

        private static string GenerateEdgeId(string fromNodeId, string toNodeId, string routeId)
        {
            var from = NormalizeIdPart(fromNodeId);
            var to = NormalizeIdPart(toNodeId);
            var route = NormalizeIdPart(routeId);
            return string.IsNullOrWhiteSpace(route)
                ? $"edge.{from}.to.{to}"
                : $"edge.{from}.to.{to}.{route}";
        }

        private static string NormalizeIdPart(string value)
        {
            var chars = (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '.')
                .ToArray();
            var text = new string(chars);
            while (text.Contains("..", StringComparison.Ordinal))
                text = text.Replace("..", ".");
            return text.Trim('.').Length == 0 ? "unnamed" : text.Trim('.');
        }

        private static int EstimateRouteCostMs(int waypointCount)
            => Math.Max(Webwalking.GameTickMs, Math.Max(1, waypointCount) * Webwalking.GameTickMs * 3);

        private static WebwalkingStoredRoute ToStoredRoute(WebwalkingRoute route)
            => new()
            {
                Id = route.Id,
                Name = route.Name,
                Description = route.Description,
                Category = route.Category,
                IsEnabled = route.IsEnabled,
                Tags = route.Tags.ToList(),
                Waypoints = route.Waypoints.Select(wp => new WebwalkingStoredWaypoint
                {
                    Label = wp.Label,
                    X = wp.Point.X,
                    Y = wp.Point.Y,
                    Z = wp.Point.Z,
                    AreaRadius = wp.AreaRadius,
                    ArrivalDistance = wp.ArrivalDistance,
                    TimeoutMs = wp.TimeoutMs,
                    JitterTiles = wp.JitterTiles,
                    ChainWhileMoving = wp.ChainWhileMoving,
                    IsTransition = wp.IsTransition,
                    TransitionObjectIds = wp.TransitionObjectIds.ToList()
                }).ToList()
            };

        private static void RefreshCommandStates() => CommandManager.InvalidateRequerySuggested();

        public void Dispose()
        {
            if (_disposed) return;
            OnDeactivated();
            _disposed = true;
            _refreshTimer.Stop();
            _recorderTimer.Stop();
            _pathRunCts?.Cancel();
            _pathRunCts?.Dispose();
        }
    }
}
