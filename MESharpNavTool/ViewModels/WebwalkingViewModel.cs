using MESharp.API;
using MESharp.Commands;
using MESharp.Models;
using MESharp.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace MESharp.ViewModels
{
    public sealed class RouteWorkflowStep
    {
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public bool IsComplete { get; init; }
        public bool IsActive { get; init; }
        public string StatusText => IsComplete ? "Done" : IsActive ? "Now" : "Next";
    }

    public sealed class WebwalkingViewModel : BaseViewModel, IDisposable, IActivatableViewModel
    {
        private readonly DispatcherTimer _refreshTimer;
        private readonly NotifyCollectionChangedEventHandler _currentRouteChangedHandler;
        private CancellationTokenSource? _runCts;
        private bool _isActive;
        private bool _disposed;
        private bool _isDirty;
        private bool _isRunning;
        private bool _suppressDirtyTracking;
        private bool _lastRunSucceeded;

        private string _lastStatus = "Ready.";
        private string _runStatus = "Idle";
        private string _currentTile = "--";
        private string _workflowSummary = "Start a draft, add waypoints, validate, save, then run.";
        private string _validationSummary = "Validation has not run yet.";
        private string _preflightSummary = "Current position preflight has not run yet.";

        private RouteDefinition? _selectedRoute;
        private RouteWaypoint? _selectedWaypoint;

        private string _searchText = string.Empty;
        private string _routeName = "new.route";
        private string _routeDescription = string.Empty;
        private string _routeCategory = "custom";
        private string _routeTags = string.Empty;
        private bool _routeEnabled = true;
        private string _renameText = string.Empty;

        private string _targetX = string.Empty;
        private string _targetY = string.Empty;
        private string _targetZ = "0";

        private string _wpLabel = string.Empty;
        private int _wpX;
        private int _wpY;
        private int _wpZ;
        private int _wpAreaRadius = 1;
        private int _wpArrivalDistance = 2;
        private int _wpTimeoutMs = 9000;
        private int _wpJitterTiles = 1;
        private bool _wpChainWhileMoving = true;
        private bool _wpIsTransition;
        private string _wpTransitionIds = string.Empty;

        public ObservableCollection<string> ActivityLog { get; } = new();
        public ObservableCollection<RouteWorkflowStep> WorkflowSteps { get; } = new();
        public ObservableCollection<RouteDefinition> SavedRoutes { get; } = new();
        public ObservableCollection<RouteDefinition> FilteredRoutes { get; } = new();
        /// <summary>Category suggestions for the editable Category combo: common defaults plus every category already in the catalog.</summary>
        public ObservableCollection<string> CategoryOptions { get; } = new();
        public ObservableCollection<RouteWaypoint> CurrentRoute { get; } = new();

        public string LastStatus { get => _lastStatus; set => SetProperty(ref _lastStatus, value); }
        public string RunStatus { get => _runStatus; set => SetProperty(ref _runStatus, value); }
        public string CurrentTile { get => _currentTile; set => SetProperty(ref _currentTile, value); }
        public bool IsRunning { get => _isRunning; set { SetProperty(ref _isRunning, value); RefreshCommandStates(); } }
        public bool IsDirty { get => _isDirty; set { if (SetProperty(ref _isDirty, value)) RefreshWorkflowState(); } }
        public string WorkflowSummary { get => _workflowSummary; set => SetProperty(ref _workflowSummary, value); }
        public string ValidationSummary { get => _validationSummary; set => SetProperty(ref _validationSummary, value); }
        public string PreflightSummary { get => _preflightSummary; set => SetProperty(ref _preflightSummary, value); }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyRouteFilter();
                }
            }
        }

        public RouteDefinition? SelectedRoute
        {
            get => _selectedRoute;
            set
            {
                if (SetProperty(ref _selectedRoute, value))
                {
                    RenameText = value?.Name ?? string.Empty;
                    RefreshCommandStates();
                }
            }
        }

        public RouteWaypoint? SelectedWaypoint
        {
            get => _selectedWaypoint;
            set
            {
                if (SetProperty(ref _selectedWaypoint, value))
                {
                    LoadWaypointEditor(value);
                    RefreshCommandStates();
                }
            }
        }

        public string RouteName { get => _routeName; set { if (SetProperty(ref _routeName, value)) { MarkDraftDirty(); RefreshCommandStates(); } } }
        public string RouteDescription { get => _routeDescription; set { if (SetProperty(ref _routeDescription, value)) MarkDraftDirty(); } }
        public string RouteCategory { get => _routeCategory; set { if (SetProperty(ref _routeCategory, value)) MarkDraftDirty(); } }
        public string RouteTags { get => _routeTags; set { if (SetProperty(ref _routeTags, value)) MarkDraftDirty(); } }
        public bool RouteEnabled { get => _routeEnabled; set { if (SetProperty(ref _routeEnabled, value)) MarkDraftDirty(); } }
        public string RenameText { get => _renameText; set => SetProperty(ref _renameText, value); }

        public string TargetX { get => _targetX; set => SetProperty(ref _targetX, value); }
        public string TargetY { get => _targetY; set => SetProperty(ref _targetY, value); }
        public string TargetZ { get => _targetZ; set => SetProperty(ref _targetZ, value); }

        public string WaypointLabel { get => _wpLabel; set => SetProperty(ref _wpLabel, value); }
        public int WaypointX { get => _wpX; set => SetProperty(ref _wpX, value); }
        public int WaypointY { get => _wpY; set => SetProperty(ref _wpY, value); }
        public int WaypointZ { get => _wpZ; set => SetProperty(ref _wpZ, value); }
        public int WaypointAreaRadius { get => _wpAreaRadius; set => SetProperty(ref _wpAreaRadius, value); }
        public int WaypointArrivalDistance { get => _wpArrivalDistance; set => SetProperty(ref _wpArrivalDistance, value); }
        public int WaypointTimeoutMs { get => _wpTimeoutMs; set => SetProperty(ref _wpTimeoutMs, value); }
        public int WaypointJitterTiles { get => _wpJitterTiles; set => SetProperty(ref _wpJitterTiles, value); }
        public bool WaypointChainWhileMoving { get => _wpChainWhileMoving; set => SetProperty(ref _wpChainWhileMoving, value); }
        public bool WaypointIsTransition { get => _wpIsTransition; set => SetProperty(ref _wpIsTransition, value); }
        public string WaypointTransitionIds { get => _wpTransitionIds; set => SetProperty(ref _wpTransitionIds, value); }

        public ICommand NewRouteCommand { get; }
        public ICommand SaveRouteCommand { get; }
        public ICommand LoadRouteCommand { get; }
        public ICommand DuplicateRouteCommand { get; }
        public ICommand RenameRouteCommand { get; }
        public ICommand DeleteRouteCommand { get; }
        public ICommand RefreshRouteListCommand { get; }

        public ICommand AddCurrentWaypointCommand { get; }
        public ICommand AddTargetWaypointCommand { get; }
        public ICommand InsertWaypointAboveCommand { get; }
        public ICommand InsertWaypointBelowCommand { get; }
        public ICommand RemoveWaypointCommand { get; }
        public ICommand MoveWaypointUpCommand { get; }
        public ICommand MoveWaypointDownCommand { get; }
        public ICommand ApplyWaypointEditsCommand { get; }
        public ICommand ClearCurrentRouteCommand { get; }
        public ICommand UseCurrentTileAsTargetCommand { get; }
        public ICommand UseCurrentTileForWaypointCommand { get; }
        public ICommand CopyTargetToWaypointCommand { get; }
        public ICommand ResetWaypointDefaultsCommand { get; }

        public ICommand RunCurrentRouteCommand { get; }
        public ICommand RunSelectedRouteCommand { get; }
        public ICommand StopRouteCommand { get; }
        public ICommand ShowHelpCommand { get; }
        public ICommand ShowRouteOnMapCommand { get; }

        public WebwalkingViewModel()
        {
            NewRouteCommand = new RelayCommand(_ => NewRoute());
            SaveRouteCommand = new RelayCommand(_ => SaveCurrentRoute(), _ => CanSaveCurrentRoute());
            LoadRouteCommand = new RelayCommand(_ => LoadSelectedRoute(), _ => SelectedRoute != null && !IsRunning);
            DuplicateRouteCommand = new RelayCommand(_ => DuplicateSelectedRoute(), _ => SelectedRoute != null && !IsRunning);
            RenameRouteCommand = new RelayCommand(_ => RenameSelectedRoute(), _ => SelectedRoute != null && !string.IsNullOrWhiteSpace(RenameText) && !IsRunning);
            DeleteRouteCommand = new RelayCommand(_ => DeleteSelectedRoute(), _ => SelectedRoute != null && !IsRunning);
            RefreshRouteListCommand = new RelayCommand(_ => RefreshRouteList());

            AddCurrentWaypointCommand = new RelayCommand(_ => AddWaypointFromCurrent(), _ => !IsRunning);
            AddTargetWaypointCommand = new RelayCommand(_ => AddWaypointFromTarget(), _ => !IsRunning);
            InsertWaypointAboveCommand = new RelayCommand(_ => InsertWaypointAbove(), _ => SelectedWaypoint != null && !IsRunning);
            InsertWaypointBelowCommand = new RelayCommand(_ => InsertWaypointBelow(), _ => SelectedWaypoint != null && !IsRunning);
            RemoveWaypointCommand = new RelayCommand(_ => RemoveSelectedWaypoint(), _ => SelectedWaypoint != null && !IsRunning);
            MoveWaypointUpCommand = new RelayCommand(_ => MoveSelectedWaypoint(-1), _ => CanMoveSelectedWaypoint(-1) && !IsRunning);
            MoveWaypointDownCommand = new RelayCommand(_ => MoveSelectedWaypoint(1), _ => CanMoveSelectedWaypoint(1) && !IsRunning);
            ApplyWaypointEditsCommand = new RelayCommand(_ => ApplyWaypointEdits(), _ => SelectedWaypoint != null && !IsRunning);
            ClearCurrentRouteCommand = new RelayCommand(_ => ClearCurrentRoute(), _ => CurrentRoute.Any() && !IsRunning);
            UseCurrentTileAsTargetCommand = new RelayCommand(_ => UseCurrentTileAsTarget(), _ => !IsRunning);
            UseCurrentTileForWaypointCommand = new RelayCommand(_ => UseCurrentTileForWaypoint(), _ => !IsRunning);
            CopyTargetToWaypointCommand = new RelayCommand(_ => CopyTargetToWaypoint(), _ => !IsRunning);
            ResetWaypointDefaultsCommand = new RelayCommand(_ => ResetWaypointDefaults(), _ => !IsRunning);

            RunCurrentRouteCommand = new RelayCommand(_ => RunCurrentRoute(), _ => CurrentRoute.Any() && !IsRunning);
            RunSelectedRouteCommand = new RelayCommand(_ => RunSelectedRoute(), _ => SelectedRoute != null && !IsRunning);
            StopRouteCommand = new RelayCommand(_ => StopRun(), _ => IsRunning);
            ShowHelpCommand = new RelayCommand(_ => ShowHelpWindow());
            ShowRouteOnMapCommand = new RelayCommand(_ =>
            {
                var wp = SelectedRoute?.Waypoints?.FirstOrDefault();
                if (wp != null)
                    Services.CoverageMapServer.RequestFocus(new MESharp.API.WorldPoint(wp.X, wp.Y, wp.Z));
            }, _ => SelectedRoute?.Waypoints?.Count > 0);

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };
            _refreshTimer.Tick += OnRefreshTick;

            _currentRouteChangedHandler = (_, __) =>
            {
                IsDirty = true;
                RefreshCommandStates();
                RefreshWorkflowState();
            };
            CurrentRoute.CollectionChanged += _currentRouteChangedHandler;

            RefreshRouteList();
            RefreshWorkflowState();
            AddLog("Webwalking tooling ready.");
        }

        private void RefreshRouteList()
        {
            var currentSelectionId = SelectedRoute?.Id;
            SavedRoutes.Clear();
            foreach (var route in RouteStore.Load())
            {
                SavedRoutes.Add(route);
            }

            ApplyRouteFilter();
            if (!string.IsNullOrWhiteSpace(currentSelectionId))
            {
                SelectedRoute = FilteredRoutes.FirstOrDefault(r => string.Equals(r.Id, currentSelectionId, StringComparison.OrdinalIgnoreCase));
            }
            else if (FilteredRoutes.Count > 0)
            {
                SelectedRoute = FilteredRoutes[0];
            }

            RefreshCategoryOptions();

            // A load error must stay visible — the catalog is degraded (core routes only)
            // and saves are refused by the engine until the store is fixed.
            if (!string.IsNullOrWhiteSpace(RouteStore.LastError))
            {
                LastStatus = RouteStore.LastError!;
                AddLog(LastStatus);
            }
            else
            {
                LastStatus = $"Loaded {SavedRoutes.Count} routes.";
            }

            RefreshCommandStates();
            RefreshWorkflowState();
        }

        private static readonly string[] DefaultCategories = { "core", "custom", "slayer", "quest", "skilling", "bank" };

        private void RefreshCategoryOptions()
        {
            var categories = DefaultCategories
                .Concat(SavedRoutes.Select(r => r.Category))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CategoryOptions.Clear();
            foreach (var category in categories)
                CategoryOptions.Add(category);
        }

        private void ApplyRouteFilter()
        {
            var search = (SearchText ?? string.Empty).Trim();

            FilteredRoutes.Clear();
            foreach (var route in SavedRoutes)
            {
                if (string.IsNullOrWhiteSpace(search))
                {
                    FilteredRoutes.Add(route);
                    continue;
                }

                var tagMatch = route.Tags?.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase)) == true;
                if (route.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    route.Category.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    tagMatch)
                {
                    FilteredRoutes.Add(route);
                }
            }
        }

        private void NewRoute()
        {
            _suppressDirtyTracking = true;
            try
            {
                ClearCurrentRoute();
                RouteName = "new.route";
                RouteDescription = string.Empty;
                RouteCategory = "custom";
                RouteTags = string.Empty;
                RouteEnabled = true;
                SelectedRoute = null;
                RenameText = string.Empty;
            }
            finally
            {
                _suppressDirtyTracking = false;
            }

            IsDirty = false;
            LastStatus = "Started a new route draft.";
            RefreshWorkflowState();
        }

        private bool CanSaveCurrentRoute()
        {
            return !IsRunning &&
                   !string.IsNullOrWhiteSpace(RouteName) &&
                   CurrentRoute.Count > 0;
        }

        private void SaveCurrentRoute()
        {
            if (!CanSaveCurrentRoute())
            {
                LastStatus = "Route save requires a name and at least one waypoint.";
                return;
            }

            var normalizedName = RouteName.Trim();
            // Identity is the (normalized) name: saving under a new name creates a new
            // route instead of silently renaming whatever happens to be selected.
            var existing = SavedRoutes.FirstOrDefault(r => string.Equals(r.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

            var route = new RouteDefinition
            {
                SchemaVersion = RouteDefinition.CurrentSchemaVersion,
                Id = existing?.Id ?? BuildRouteId(normalizedName),
                Name = normalizedName,
                Description = (RouteDescription ?? string.Empty).Trim(),
                Category = string.IsNullOrWhiteSpace(RouteCategory) ? "custom" : RouteCategory.Trim(),
                IsEnabled = RouteEnabled,
                Tags = ParseTags(RouteTags),
                CreatedAt = existing == null || existing.CreatedAt == default ? DateTime.UtcNow : existing.CreatedAt,
                SavedAt = DateTime.UtcNow,
                Waypoints = CurrentRoute.Select(CloneWaypoint).ToList()
            };
            route.Normalize();

            if (!RouteStore.TryUpsert(route, out var saveError))
            {
                LastStatus = saveError ?? "Route save failed.";
                AddLog(LastStatus);
                return;
            }
            RefreshRouteList();
            SelectedRoute = SavedRoutes.FirstOrDefault(r => string.Equals(r.Id, route.Id, StringComparison.OrdinalIgnoreCase));
            RenameText = route.Name;
            IsDirty = false;
            LastStatus = $"Saved route '{route.Name}' ({route.Waypoints.Count} waypoints).";
            AddLog(LastStatus);
            RefreshWorkflowState();
        }

        private static string BuildRouteId(string name)
        {
            var safe = (name ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');
            return string.IsNullOrWhiteSpace(safe) ? $"route_{Guid.NewGuid():N}" : safe;
        }

        private static List<string> ParseTags(string tagsText)
        {
            if (string.IsNullOrWhiteSpace(tagsText))
            {
                return new List<string>();
            }

            return tagsText
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void LoadSelectedRoute()
        {
            var route = SelectedRoute;
            if (route == null)
            {
                LastStatus = "Select a route to load.";
                return;
            }

            _suppressDirtyTracking = true;
            try
            {
                CurrentRoute.Clear();
                foreach (var waypoint in route.Waypoints)
                {
                    CurrentRoute.Add(CloneWaypoint(waypoint));
                }

                RouteName = route.Name;
                RouteDescription = route.Description;
                RouteCategory = route.Category;
                RouteEnabled = route.IsEnabled;
                RouteTags = string.Join(", ", route.Tags ?? new List<string>());
                RenameText = route.Name;
            }
            finally
            {
                _suppressDirtyTracking = false;
            }

            IsDirty = false;
            LastStatus = $"Loaded route '{route.Name}'.";
            AddLog(LastStatus);
            RefreshCommandStates();
            RefreshWorkflowState();
        }

        private void DuplicateSelectedRoute()
        {
            var source = SelectedRoute;
            if (source == null)
            {
                return;
            }

            var duplicate = CloneRoute(source);
            duplicate.Id = $"{source.Id}_copy_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            duplicate.Name = BuildUniqueDuplicateName(source.Name);
            duplicate.CreatedAt = DateTime.UtcNow;
            duplicate.SavedAt = DateTime.UtcNow;

            if (!RouteStore.TryUpsert(duplicate, out var error))
            {
                LastStatus = error ?? "Route duplicate failed.";
                AddLog(LastStatus);
                return;
            }
            RefreshRouteList();
            SelectedRoute = SavedRoutes.FirstOrDefault(r => string.Equals(r.Id, duplicate.Id, StringComparison.OrdinalIgnoreCase));
            LastStatus = $"Duplicated route as '{duplicate.Name}'.";
            AddLog(LastStatus);
        }

        private string BuildUniqueDuplicateName(string baseName)
        {
            var candidate = $"{baseName} (copy)";
            var suffix = 2;
            while (SavedRoutes.Any(r => string.Equals(r.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{baseName} (copy {suffix})";
                suffix++;
            }

            return candidate;
        }

        private void RenameSelectedRoute()
        {
            var route = SelectedRoute;
            var newName = (RenameText ?? string.Empty).Trim();
            if (route == null || string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            var duplicateName = SavedRoutes.Any(r => !ReferenceEquals(r, route) && string.Equals(r.Name, newName, StringComparison.OrdinalIgnoreCase));
            if (duplicateName)
            {
                LastStatus = $"A route named '{newName}' already exists.";
                return;
            }

            route.Name = newName;
            route.SavedAt = DateTime.UtcNow;
            route.Normalize();
            if (!RouteStore.TryUpsert(route, out var error))
            {
                LastStatus = error ?? "Route rename failed.";
                AddLog(LastStatus);
                return;
            }
            RefreshRouteList();
            SelectedRoute = SavedRoutes.FirstOrDefault(r => string.Equals(r.Id, route.Id, StringComparison.OrdinalIgnoreCase));
            LastStatus = $"Renamed route to '{newName}'.";
            AddLog(LastStatus);
        }

        private void DeleteSelectedRoute()
        {
            var route = SelectedRoute;
            if (route == null)
            {
                return;
            }

            var confirmed = System.Windows.MessageBox.Show(
                $"Delete route '{route.Name}' ({route.Waypoints?.Count ?? 0} waypoints)? This cannot be undone.",
                "Delete route",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
            if (!confirmed)
            {
                return;
            }

            if (!RouteStore.TryDelete(route, out var error))
            {
                LastStatus = error ?? "Route delete failed.";
                AddLog(LastStatus);
                return;
            }
            RefreshRouteList();
            LastStatus = $"Deleted route '{route.Name}'.";
            AddLog(LastStatus);
        }

        private void AddWaypointFromCurrent()
        {
            try
            {
                var tile = LocalPlayer.GetTilePosition();
                var waypoint = BuildWaypoint(tile.x, tile.y, tile.z);
                CurrentRoute.Add(waypoint);
                SelectedWaypoint = waypoint;
                LastStatus = $"Added waypoint {waypoint}.";
            }
            catch (Exception ex)
            {
                LastStatus = $"Failed to read current tile: {ex.Message}";
            }
        }

        private void AddWaypointFromTarget()
        {
            if (!TryParseTarget(out var x, out var y, out var z))
            {
                LastStatus = "Enter valid X/Y target coordinates.";
                return;
            }

            var waypoint = BuildWaypoint(x, y, z);
            CurrentRoute.Add(waypoint);
            SelectedWaypoint = waypoint;
            LastStatus = $"Added waypoint {waypoint}.";
        }

        private RouteWaypoint BuildWaypoint(int x, int y, int z)
        {
            var waypoint = new RouteWaypoint
            {
                Id = Guid.NewGuid().ToString("N"),
                Label = string.IsNullOrWhiteSpace(WaypointLabel) ? string.Empty : WaypointLabel.Trim(),
                X = x,
                Y = y,
                Z = z,
                AreaRadius = WaypointAreaRadius,
                ArrivalDistance = WaypointArrivalDistance,
                TimeoutMs = WaypointTimeoutMs,
                JitterTiles = WaypointJitterTiles,
                ChainWhileMoving = WaypointChainWhileMoving,
                IsTransition = WaypointIsTransition,
                TransitionObjectIds = ParseTransitionIds(WaypointTransitionIds)
            };
            waypoint.Normalize();
            return waypoint;
        }

        private void InsertWaypointAbove()
        {
            if (SelectedWaypoint == null)
            {
                return;
            }

            var index = CurrentRoute.IndexOf(SelectedWaypoint);
            if (index < 0)
            {
                return;
            }

            var copy = CloneWaypoint(SelectedWaypoint);
            copy.Id = Guid.NewGuid().ToString("N");
            CurrentRoute.Insert(index, copy);
            SelectedWaypoint = copy;
            LastStatus = $"Inserted waypoint above row {index + 1}.";
        }

        private void InsertWaypointBelow()
        {
            if (SelectedWaypoint == null)
            {
                return;
            }

            var index = CurrentRoute.IndexOf(SelectedWaypoint);
            if (index < 0)
            {
                return;
            }

            var copy = CloneWaypoint(SelectedWaypoint);
            copy.Id = Guid.NewGuid().ToString("N");
            var insertIndex = Math.Min(CurrentRoute.Count, index + 1);
            CurrentRoute.Insert(insertIndex, copy);
            SelectedWaypoint = copy;
            LastStatus = $"Inserted waypoint below row {index + 1}.";
        }

        private void RemoveSelectedWaypoint()
        {
            if (SelectedWaypoint == null)
            {
                return;
            }

            var removed = SelectedWaypoint;
            CurrentRoute.Remove(removed);
            SelectedWaypoint = CurrentRoute.FirstOrDefault();
            LastStatus = $"Removed waypoint {removed}.";
        }

        private bool CanMoveSelectedWaypoint(int direction)
        {
            if (SelectedWaypoint == null)
            {
                return false;
            }

            var index = CurrentRoute.IndexOf(SelectedWaypoint);
            if (index < 0)
            {
                return false;
            }

            var target = index + direction;
            return target >= 0 && target < CurrentRoute.Count;
        }

        private void MoveSelectedWaypoint(int direction)
        {
            if (!CanMoveSelectedWaypoint(direction) || SelectedWaypoint == null)
            {
                return;
            }

            var index = CurrentRoute.IndexOf(SelectedWaypoint);
            var target = index + direction;
            CurrentRoute.Move(index, target);
            SelectedWaypoint = CurrentRoute[target];
            LastStatus = $"Moved waypoint to row {target + 1}.";
            RefreshWorkflowState();
        }

        private void ApplyWaypointEdits()
        {
            if (SelectedWaypoint == null)
            {
                return;
            }

            SelectedWaypoint.Label = (WaypointLabel ?? string.Empty).Trim();
            SelectedWaypoint.X = WaypointX;
            SelectedWaypoint.Y = WaypointY;
            SelectedWaypoint.Z = WaypointZ;
            SelectedWaypoint.AreaRadius = WaypointAreaRadius;
            SelectedWaypoint.ArrivalDistance = WaypointArrivalDistance;
            SelectedWaypoint.TimeoutMs = WaypointTimeoutMs;
            SelectedWaypoint.JitterTiles = WaypointJitterTiles;
            SelectedWaypoint.ChainWhileMoving = WaypointChainWhileMoving;
            SelectedWaypoint.IsTransition = WaypointIsTransition;
            SelectedWaypoint.TransitionObjectIds = ParseTransitionIds(WaypointTransitionIds);
            SelectedWaypoint.Normalize();

            var index = CurrentRoute.IndexOf(SelectedWaypoint);
            if (index >= 0)
            {
                CurrentRoute[index] = CloneWaypoint(SelectedWaypoint);
                SelectedWaypoint = CurrentRoute[index];
            }

            IsDirty = true;
            LastStatus = "Applied waypoint edits.";
            RefreshWorkflowState();
        }

        private static List<int> ParseTransitionIds(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<int>();
            }

            return input
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => int.TryParse(part, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();
        }

        private void LoadWaypointEditor(RouteWaypoint? waypoint)
        {
            if (waypoint == null)
            {
                ClearWaypointEditor();
                return;
            }

            WaypointLabel = waypoint.Label;
            WaypointX = waypoint.X;
            WaypointY = waypoint.Y;
            WaypointZ = waypoint.Z;
            WaypointAreaRadius = waypoint.AreaRadius;
            WaypointArrivalDistance = waypoint.ArrivalDistance;
            WaypointTimeoutMs = waypoint.TimeoutMs;
            WaypointJitterTiles = waypoint.JitterTiles;
            WaypointChainWhileMoving = waypoint.ChainWhileMoving;
            WaypointIsTransition = waypoint.IsTransition;
            WaypointTransitionIds = string.Join(",", waypoint.TransitionObjectIds ?? new List<int>());
        }

        private void ClearWaypointEditor()
        {
            WaypointLabel = string.Empty;
            WaypointX = 0;
            WaypointY = 0;
            WaypointZ = 0;
            ResetWaypointDefaults();
        }

        private void ResetWaypointDefaults()
        {
            WaypointAreaRadius = 1;
            WaypointArrivalDistance = 2;
            WaypointTimeoutMs = 9000;
            WaypointJitterTiles = 1;
            WaypointChainWhileMoving = true;
            WaypointIsTransition = false;
            WaypointTransitionIds = string.Empty;
        }

        private void ClearCurrentRoute()
        {
            CurrentRoute.Clear();
            SelectedWaypoint = null;
            IsDirty = true;
            LastStatus = "Cleared current route draft.";
            RefreshCommandStates();
            RefreshWorkflowState();
        }

        private void RunCurrentRoute()
        {
            if (!CurrentRoute.Any())
            {
                LastStatus = "Current route draft is empty.";
                return;
            }

            var waypoints = CurrentRoute.Select(ToWebwalkingWaypoint).ToList();
            _ = RunDraftRouteAsync(RouteName, waypoints);
        }

        private void RunSelectedRoute()
        {
            var route = SelectedRoute;
            if (route == null)
            {
                LastStatus = "Select a route to run.";
                return;
            }

            _ = RunSavedRouteAsync(route.Id, route.Name);
        }

        private async Task RunSavedRouteAsync(string routeId, string routeName)
        {
            if (_disposed || IsRunning) return;

            _runCts?.Cancel();
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            var ct = _runCts.Token;

            IsRunning = true;
            _lastRunSucceeded = false;
            RunStatus = $"Running {routeName}";
            AddLog($"Route run started: {routeName}");

            try
            {
                var result = await Webwalking.RunRouteDetailedAsync(routeId, ct);
                _lastRunSucceeded = result.Succeeded;
                LastStatus = FormatRouteResult(result);
                AddLog(LastStatus);
                RefreshWorkflowState();
            }
            catch (Exception ex)
            {
                LastStatus = $"Route '{routeName}' error: {ex.Message}";
                AddLog(LastStatus);
            }
            finally
            {
                IsRunning = false;
                RunStatus = "Idle";
                _runCts?.Dispose();
                _runCts = null;
            }
        }

        private async Task RunDraftRouteAsync(string draftName, IReadOnlyList<WebwalkingWaypoint> waypoints)
        {
            if (_disposed || IsRunning) return;

            _runCts?.Cancel();
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            var ct = _runCts.Token;

            IsRunning = true;
            _lastRunSucceeded = false;
            RunStatus = $"Running {draftName}";
            AddLog($"Draft run started: {draftName} ({waypoints.Count} waypoints)");

            try
            {
                var result = await Webwalking.RunRouteDetailedAsync(waypoints, draftName, ct);
                _lastRunSucceeded = result.Succeeded;
                LastStatus = FormatRouteResult(result);
                AddLog(LastStatus);
                RefreshWorkflowState();
            }
            catch (Exception ex)
            {
                LastStatus = $"Draft '{draftName}' error: {ex.Message}";
                AddLog(LastStatus);
            }
            finally
            {
                IsRunning = false;
                RunStatus = "Idle";
                _runCts?.Dispose();
                _runCts = null;
            }
        }

        private static WebwalkingWaypoint ToWebwalkingWaypoint(RouteWaypoint wp) => new()
        {
            Label = wp.Label,
            Point = new WorldPoint(wp.X, wp.Y, wp.Z),
            AreaRadius = wp.AreaRadius,
            ArrivalDistance = wp.ArrivalDistance,
            TimeoutMs = wp.TimeoutMs,
            JitterTiles = wp.JitterTiles,
            ChainWhileMoving = wp.ChainWhileMoving,
            IsTransition = wp.IsTransition,
            TransitionObjectIds = wp.TransitionObjectIds?.ToArray() ?? Array.Empty<int>()
        };

        private void StopRun()
        {
            _runCts?.Cancel();
            RunStatus = "Stopping...";
        }

        private void UpdateCurrentTile()
        {
            var next = TryGetCurrentTile(out var x, out var y, out var z) ? $"{x}, {y}, {z}" : "--";
            if (string.Equals(next, CurrentTile, StringComparison.Ordinal))
            {
                // Nothing changed this tick — skip the workflow rebuild (it re-validates the
                // whole draft and replaces the checklist items, which is wasteful at 750ms).
                return;
            }

            CurrentTile = next;
            RefreshWorkflowState();
        }

        private bool TryGetCurrentTile(out int x, out int y, out int z)
        {
            x = y = z = 0;
            try
            {
                var tile = LocalPlayer.GetTilePosition();
                x = tile.x;
                y = tile.y;
                z = tile.z;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void UseCurrentTileAsTarget()
        {
            if (!TryGetCurrentTile(out var x, out var y, out var z))
            {
                LastStatus = "Failed to read current tile.";
                return;
            }

            TargetX = x.ToString();
            TargetY = y.ToString();
            TargetZ = z.ToString();
            LastStatus = "Copied current tile into target inputs.";
        }

        private void UseCurrentTileForWaypoint()
        {
            if (!TryGetCurrentTile(out var x, out var y, out var z))
            {
                LastStatus = "Failed to read current tile.";
                return;
            }

            WaypointX = x;
            WaypointY = y;
            WaypointZ = z;
            LastStatus = "Copied current tile into waypoint editor.";
        }

        private void CopyTargetToWaypoint()
        {
            if (!TryParseTarget(out var x, out var y, out var z))
            {
                LastStatus = "Enter valid target coordinates first.";
                return;
            }

            WaypointX = x;
            WaypointY = y;
            WaypointZ = z;
            LastStatus = "Copied target inputs into waypoint editor.";
        }

        private bool TryParseTarget(out int x, out int y, out int z)
        {
            x = y = z = 0;
            if (!int.TryParse(TargetX, out x))
            {
                return false;
            }

            if (!int.TryParse(TargetY, out y))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(TargetZ))
            {
                z = 0;
            }
            else if (!int.TryParse(TargetZ, out z))
            {
                // A typed-but-invalid plane must not silently become plane 0.
                return false;
            }

            return true;
        }

        private static RouteDefinition CloneRoute(RouteDefinition source)
        {
            return new RouteDefinition
            {
                SchemaVersion = source.SchemaVersion,
                Id = source.Id,
                Name = source.Name,
                Description = source.Description,
                Category = source.Category,
                IsEnabled = source.IsEnabled,
                Tags = source.Tags?.ToList() ?? new List<string>(),
                Waypoints = source.Waypoints?.Select(CloneWaypoint).ToList() ?? new List<RouteWaypoint>(),
                CreatedAt = source.CreatedAt,
                SavedAt = source.SavedAt
            };
        }

        private static RouteWaypoint CloneWaypoint(RouteWaypoint source)
        {
            var clone = new RouteWaypoint
            {
                Id = source.Id,
                Label = source.Label,
                X = source.X,
                Y = source.Y,
                Z = source.Z,
                AreaRadius = source.AreaRadius,
                ArrivalDistance = source.ArrivalDistance,
                TimeoutMs = source.TimeoutMs,
                JitterTiles = source.JitterTiles,
                ChainWhileMoving = source.ChainWhileMoving,
                IsTransition = source.IsTransition,
                TransitionObjectIds = source.TransitionObjectIds?.ToList() ?? new List<int>()
            };
            clone.Normalize();
            return clone;
        }

        private void AddLog(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            ActivityLog.Insert(0, entry);
            while (ActivityLog.Count > 40)
            {
                ActivityLog.RemoveAt(ActivityLog.Count - 1);
            }
        }

        private void MarkDraftDirty()
        {
            if (_suppressDirtyTracking)
            {
                return;
            }

            IsDirty = true;
            RefreshWorkflowState();
        }

        private void RefreshWorkflowState()
        {
            var hasName = !string.IsNullOrWhiteSpace(RouteName);
            var hasWaypoints = CurrentRoute.Count > 0;
            var hasSelectedWaypoint = SelectedWaypoint != null;
            var validation = BuildCurrentRouteValidation();
            var isValid = validation.IsValid;
            var isSaved = hasWaypoints && hasName && !IsDirty && SavedRoutes.Any(r =>
                string.Equals(r.Name, RouteName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Id, BuildRouteId(RouteName), StringComparison.OrdinalIgnoreCase));
            var preflightOk = TryBuildPreflightSummary(out var preflightSummary, out var preflightReady);

            ValidationSummary = FormatValidationSummary(validation);
            PreflightSummary = preflightSummary;
            WorkflowSummary = isValid && isSaved && preflightOk
                ? "Draft is saved, valid, and ready for a bounded test run."
                : "Work left: " + string.Join(", ", BuildRemainingWorkflowItems(hasName, hasWaypoints, isValid, isSaved, preflightOk));

            WorkflowSteps.Clear();
            WorkflowSteps.Add(new RouteWorkflowStep
            {
                Title = "Name the route",
                Detail = hasName ? $"Using '{RouteName.Trim()}'." : "Enter a stable route name before saving.",
                IsComplete = hasName,
                IsActive = !hasName
            });
            WorkflowSteps.Add(new RouteWorkflowStep
            {
                Title = "Capture waypoints",
                Detail = hasWaypoints ? $"{CurrentRoute.Count} waypoint(s) in the draft." : "Add current tile or target tile waypoints.",
                IsComplete = hasWaypoints,
                IsActive = hasName && !hasWaypoints
            });
            WorkflowSteps.Add(new RouteWorkflowStep
            {
                Title = "Review selected waypoint",
                Detail = hasSelectedWaypoint ? $"Selected: {SelectedWaypoint}" : "Select a waypoint to inspect or tune details.",
                IsComplete = hasSelectedWaypoint,
                IsActive = hasWaypoints && !hasSelectedWaypoint
            });
            WorkflowSteps.Add(new RouteWorkflowStep
            {
                Title = "Validate draft",
                Detail = ValidationSummary,
                IsComplete = isValid,
                IsActive = hasWaypoints && !isValid
            });
            WorkflowSteps.Add(new RouteWorkflowStep
            {
                Title = "Check starting position",
                Detail = preflightSummary,
                IsComplete = preflightOk,
                IsActive = isValid && preflightReady && !preflightOk
            });
            WorkflowSteps.Add(new RouteWorkflowStep
            {
                Title = "Save route",
                Detail = isSaved ? "Saved route matches the current draft name." : IsDirty ? "Draft has unsaved edits." : "Save once validation looks right.",
                IsComplete = isSaved,
                IsActive = isValid && preflightOk && !isSaved
            });
            WorkflowSteps.Add(new RouteWorkflowStep
            {
                Title = "Run bounded test",
                Detail = IsRunning ? RunStatus : "Use Run Draft first, then promote via graph edges.",
                IsComplete = !IsRunning && _lastRunSucceeded,
                IsActive = isValid && preflightOk && isSaved && !IsRunning
            });
        }

        private WebwalkingValidationResult BuildCurrentRouteValidation()
        {
            var route = new WebwalkingStoredRoute
            {
                Name = RouteName,
                Description = RouteDescription,
                Category = RouteCategory,
                IsEnabled = RouteEnabled,
                Tags = ParseTags(RouteTags),
                Waypoints = CurrentRoute.Select(wp => new WebwalkingStoredWaypoint
                {
                    Id = wp.Id,
                    Label = wp.Label,
                    X = wp.X,
                    Y = wp.Y,
                    Z = wp.Z,
                    AreaRadius = wp.AreaRadius,
                    ArrivalDistance = wp.ArrivalDistance,
                    TimeoutMs = wp.TimeoutMs,
                    JitterTiles = wp.JitterTiles,
                    ChainWhileMoving = wp.ChainWhileMoving,
                    IsTransition = wp.IsTransition,
                    TransitionObjectIds = wp.TransitionObjectIds?.ToList() ?? new List<int>()
                }).ToList()
            };

            route.Normalize();
            return Webwalking.ValidateRoute(route);
        }

        private static string FormatValidationSummary(WebwalkingValidationResult validation)
        {
            if (validation.IsValid && validation.Warnings.Count == 0)
            {
                return "No validation issues.";
            }

            if (validation.IsValid)
            {
                return $"{validation.Warnings.Count} warning(s): {validation.Warnings[0].Message}";
            }

            return $"{validation.Errors.Count} error(s): {validation.Errors[0].Message}";
        }

        private bool TryBuildPreflightSummary(out string summary, out bool ready)
        {
            ready = CurrentRoute.Count > 0;
            if (!ready)
            {
                summary = "Add at least one waypoint to check the starting position.";
                return false;
            }

            if (!TryGetCurrentTile(out var x, out var y, out var z))
            {
                summary = "Current tile unavailable; connect to the game before testing.";
                return false;
            }

            var first = CurrentRoute[0];
            if (z != first.Z)
            {
                summary = $"Current plane {z} differs from first waypoint plane {first.Z}.";
                return false;
            }

            var distance = Math.Abs(x - first.X) + Math.Abs(y - first.Y);
            var threshold = Math.Max(12, Math.Max(first.ArrivalDistance, first.AreaRadius) * 4);
            if (distance <= threshold)
            {
                summary = $"Current tile is {distance} tile(s) from the first waypoint.";
                return true;
            }

            summary = $"Current tile is {distance} tile(s) from the first waypoint; start nearer or connect this route through the graph.";
            return false;
        }

        private static IEnumerable<string> BuildRemainingWorkflowItems(bool hasName, bool hasWaypoints, bool isValid, bool isSaved, bool preflightOk)
        {
            if (!hasName) yield return "name route";
            if (!hasWaypoints) yield return "add waypoints";
            if (!isValid) yield return "fix validation";
            if (!preflightOk) yield return "check start";
            if (!isSaved) yield return "save";
        }

        private static string FormatRouteResult(WebwalkingRunResult result) => result.Status switch
        {
            WebwalkingRunStatus.Success => $"Route '{result.RouteName}' completed.",
            WebwalkingRunStatus.Cancelled => $"Route '{result.RouteName}' cancelled.",
            WebwalkingRunStatus.RouteNotFound => $"Route '{result.RouteId}' not found — save it first.",
            WebwalkingRunStatus.RouteDisabled => $"Route '{result.RouteName}' is disabled.",
            WebwalkingRunStatus.EmptyRoute => $"Route '{result.RouteName}' has no waypoints.",
            WebwalkingRunStatus.DispatchFailed => $"Route '{result.RouteName}' failed to dispatch waypoint {result.WaypointIndex + 1} ({result.WaypointLabel}).",
            WebwalkingRunStatus.WaypointTimeout => $"Route '{result.RouteName}' timed out at waypoint {result.WaypointIndex + 1} ({result.WaypointLabel}).",
            _ => $"Route '{result.RouteName}': {result.Message}"
        };

        private void ShowHelpWindow()
        {
            try
            {
                var window = new Views.SectionHelpWindow(Views.NavHelpContent.Routes());
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                LastStatus = $"Could not open webwalking help: {ex.Message}";
                AddLog(LastStatus);
            }
        }

        private static void RefreshCommandStates() => CommandManager.InvalidateRequerySuggested();

        public void OnActivated()
        {
            if (_disposed)
            {
                return;
            }

            if (_isActive)
            {
                UpdateCurrentTile();
                return;
            }

            _isActive = true;
            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }

            UpdateCurrentTile();
        }

        public void OnDeactivated()
        {
            if (_disposed || !_isActive)
            {
                return;
            }

            _isActive = false;
            try
            {
                _refreshTimer.Stop();
            }
            catch
            {
                // ignore
            }

            if (IsRunning)
            {
                AddLog("Route run stopped: the Routes section was deactivated while running.");
                StopRun();
            }
        }

        private void OnRefreshTick(object? sender, EventArgs e) => UpdateCurrentTile();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            OnDeactivated();
            _disposed = true;

            try
            {
                _refreshTimer.Stop();
            }
            catch
            {
                // ignore
            }

            _refreshTimer.Tick -= OnRefreshTick;
            try
            {
                CurrentRoute.CollectionChanged -= _currentRouteChangedHandler;
            }
            catch
            {
                // ignore
            }

            try
            {
                _runCts?.Cancel();
            }
            catch
            {
                // ignore
            }

            try
            {
                _runCts?.Dispose();
            }
            catch
            {
                // ignore
            }

            _runCts = null;
        }
    }
}
