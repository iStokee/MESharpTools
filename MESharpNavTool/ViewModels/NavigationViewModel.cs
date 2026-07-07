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
    public class NavigationViewModel : BaseViewModel, IDisposable, IActivatableViewModel
    {
        private readonly DispatcherTimer _refreshTimer;
        private bool _isActive;
        private bool _disposed;

        public ObservableCollection<string> ActivityLog { get; } = new();
        public ObservableCollection<LodestoneOption> Lodestones { get; }

        // Live status
        private string _tilePosition = "--";
        private string _exactPosition = "--";
        private bool _isMoving;
        private string _lastStatus = "Ready.";

        // Walk inputs
        private string _targetX = string.Empty;
        private string _targetY = string.Empty;
        private string _targetZ = "0";
        private int _stopShortTiles = 2;
        private int _timeoutMs = 8000;
        private int _jitterTiles = 1;
        private int _nudgeStep = 1;

        // Path inputs
        private string _pathInput = "3200,3200,0\n3205,3210,0";

        // Lodestone inputs
        private LodestoneOption? _selectedLodestone;
        private string _lodestoneSearch = "Varrock";
        private int _teleportTimeoutMs = 12000;

        // ─── API Utilities (moved from ApiUtilities) ───────────────────────
        private int _apiMinimapIconId;
        private int _apiMinimapX = 640;
        private int _apiMinimapY = 120;

        private int _apiSpellbookIndex;
        private string _apiTeleportName = "Varrock";
        private string _apiJewelryItemName = "Ring of duelling";
        private string _apiJewelryLocation1 = "Castle Wars";
        private string _apiJewelryLocation2 = string.Empty;
        private int _apiJewelryMenuLevel = 1;
        private int _apiJewelryOffset = Objects.Offsets.GeneralInterfaceChooseOption;

        public string TilePosition { get => _tilePosition; set => SetProperty(ref _tilePosition, value); }
        public string ExactPosition { get => _exactPosition; set => SetProperty(ref _exactPosition, value); }
        public bool IsMoving { get => _isMoving; set => SetProperty(ref _isMoving, value); }
        public string LastStatus { get => _lastStatus; set => SetProperty(ref _lastStatus, value); }

        public string TargetX { get => _targetX; set => SetProperty(ref _targetX, value); }
        public string TargetY { get => _targetY; set => SetProperty(ref _targetY, value); }
        public string TargetZ { get => _targetZ; set => SetProperty(ref _targetZ, value); }
        public int StopShortTiles { get => _stopShortTiles; set => SetProperty(ref _stopShortTiles, value); }
        public int TimeoutMs { get => _timeoutMs; set => SetProperty(ref _timeoutMs, value); }
        public int JitterTiles { get => _jitterTiles; set => SetProperty(ref _jitterTiles, value); }
        public int NudgeStep { get => _nudgeStep; set => SetProperty(ref _nudgeStep, value); }

        public string PathInput { get => _pathInput; set => SetProperty(ref _pathInput, value); }

        public LodestoneOption? SelectedLodestone { get => _selectedLodestone; set => SetProperty(ref _selectedLodestone, value); }
        public string LodestoneSearch { get => _lodestoneSearch; set => SetProperty(ref _lodestoneSearch, value); }
        public int TeleportTimeoutMs { get => _teleportTimeoutMs; set => SetProperty(ref _teleportTimeoutMs, value); }

        public int ApiMinimapIconId { get => _apiMinimapIconId; set => SetProperty(ref _apiMinimapIconId, value); }
        public int ApiMinimapX { get => _apiMinimapX; set => SetProperty(ref _apiMinimapX, value); }
        public int ApiMinimapY { get => _apiMinimapY; set => SetProperty(ref _apiMinimapY, value); }

        public int ApiSpellbookIndex { get => _apiSpellbookIndex; set => SetProperty(ref _apiSpellbookIndex, value); }
        public string ApiTeleportName { get => _apiTeleportName; set => SetProperty(ref _apiTeleportName, value); }
        public string ApiJewelryItemName { get => _apiJewelryItemName; set => SetProperty(ref _apiJewelryItemName, value); }
        public string ApiJewelryLocation1 { get => _apiJewelryLocation1; set => SetProperty(ref _apiJewelryLocation1, value); }
        public string ApiJewelryLocation2 { get => _apiJewelryLocation2; set => SetProperty(ref _apiJewelryLocation2, value); }
        public int ApiJewelryMenuLevel { get => _apiJewelryMenuLevel; set => SetProperty(ref _apiJewelryMenuLevel, value); }
        public int ApiJewelryOffset { get => _apiJewelryOffset; set => SetProperty(ref _apiJewelryOffset, value); }

        public ICommand RefreshCommand { get; }
        public ICommand WalkToCommand { get; }
        public ICommand WalkPathCommand { get; }
        public ICommand ClickToCommand { get; }
        public ICommand ClickPathCommand { get; }
        public ICommand WaitUntilWithinCommand { get; }
        public ICommand WaitWhileMovingCommand { get; }
        public ICommand TeleportSelectedCommand { get; }
        public ICommand TeleportByNameCommand { get; }
        public ICommand ApiMinimapClickIconCommand { get; }
        public ICommand ApiMinimapClickIconAtCommand { get; }
        public ICommand ApiSpellbookByIndexCommand { get; }
        public ICommand ApiSpellbookByNameCommand { get; }
        public ICommand ApiJewelryTeleportCommand { get; }
        public ICommand ApiSpiritTreeCommand { get; }
        public ICommand ApiGliderCommand { get; }
        public ICommand ApiFairyCommand { get; }
        public ICommand ApiQuiver4Command { get; }
        public ICommand UseCurrentTileCommand { get; }
        public ICommand NudgeXPositiveCommand { get; }
        public ICommand NudgeXNegativeCommand { get; }
        public ICommand NudgeYPositiveCommand { get; }
        public ICommand NudgeYNegativeCommand { get; }
        public ICommand NudgeZPositiveCommand { get; }
        public ICommand NudgeZNegativeCommand { get; }

        // ── Webwalk travel harness ───────────────────────────────────────────────
        private CancellationTokenSource? _travelCts;
        private string _travelDestination = string.Empty;
        private bool _isTraveling;

        public string TravelDestination { get => _travelDestination; set => SetProperty(ref _travelDestination, value); }
        public bool IsTraveling { get => _isTraveling; set => SetProperty(ref _isTraveling, value); }
        public ICommand TravelCommand { get; }
        public ICommand StopTravelCommand { get; }
        public ICommand ShowHelpCommand { get; }

        /// <summary>Everything ResolveAndTravel can resolve — node ids, node tags, route ids — for the editable destination combo.</summary>
        public ObservableCollection<string> DestinationOptions { get; } = new();

        private void RefreshDestinationOptions()
        {
            try
            {
                var options = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in MESharp.API.WebwalkGraph.GetNodes())
                {
                    if (!node.Enabled) continue;
                    options.Add(node.Id);
                    foreach (var tag in node.Tags ?? new())
                        if (!string.IsNullOrWhiteSpace(tag)) options.Add(tag.Trim());
                }
                foreach (var route in MESharp.API.Webwalking.GetRoutes())
                    if (!string.IsNullOrWhiteSpace(route.Id)) options.Add(route.Id);

                DestinationOptions.Clear();
                foreach (var option in options)
                    DestinationOptions.Add(option);
            }
            catch
            {
                // Suggestions are best-effort; the box still accepts free text.
            }
        }

        public NavigationViewModel()
        {
            Lodestones = new ObservableCollection<LodestoneOption>(BuildLodestones());
            SelectedLodestone = Lodestones.FirstOrDefault();

            RefreshCommand = new RelayCommand(_ => RefreshPosition());
            WalkToCommand = new RelayCommand(_ => WalkTo());
            WalkPathCommand = new RelayCommand(_ => WalkPath());
            ClickToCommand = new RelayCommand(_ => ClickTo());
            ClickPathCommand = new RelayCommand(_ => ClickPath());
            WaitUntilWithinCommand = new RelayCommand(_ => WaitUntilWithin());
            WaitWhileMovingCommand = new RelayCommand(_ => WaitWhileMoving());
            TeleportSelectedCommand = new RelayCommand(_ => TeleportSelected(), _ => SelectedLodestone != null);
            TravelCommand = new RelayCommand(async _ => await TravelAsync(), _ => !IsTraveling && !string.IsNullOrWhiteSpace(TravelDestination));
            StopTravelCommand = new RelayCommand(_ => _travelCts?.Cancel(), _ => IsTraveling);
            ShowHelpCommand = new RelayCommand(_ =>
            {
                try { new Views.SectionHelpWindow(Views.NavHelpContent.Travel()).ShowDialog(); }
                catch (Exception ex) { LastStatus = $"Could not open help: {ex.Message}"; }
            });
            TeleportByNameCommand = new RelayCommand(_ => TeleportByName(), _ => !string.IsNullOrWhiteSpace(LodestoneSearch));
            ApiMinimapClickIconCommand = new RelayCommand(_ => ApiMinimapClickIcon());
            ApiMinimapClickIconAtCommand = new RelayCommand(_ => ApiMinimapClickIconAt());
            ApiSpellbookByIndexCommand = new RelayCommand(_ => ApiSpellbookByIndex());
            ApiSpellbookByNameCommand = new RelayCommand(_ => ApiSpellbookByName(), _ => !string.IsNullOrWhiteSpace(ApiTeleportName));
            ApiJewelryTeleportCommand = new RelayCommand(_ => ApiJewelryTeleport(), _ => !string.IsNullOrWhiteSpace(ApiJewelryItemName));
            ApiSpiritTreeCommand = new RelayCommand(_ => ApiSpiritTree(), _ => !string.IsNullOrWhiteSpace(ApiTeleportName));
            ApiGliderCommand = new RelayCommand(_ => ApiGlider(), _ => !string.IsNullOrWhiteSpace(ApiTeleportName));
            ApiFairyCommand = new RelayCommand(_ => ApiFairy(), _ => !string.IsNullOrWhiteSpace(ApiTeleportName));
            ApiQuiver4Command = new RelayCommand(_ => ApiQuiver4(), _ => !string.IsNullOrWhiteSpace(ApiTeleportName));
            UseCurrentTileCommand = new RelayCommand(_ => UseCurrentTile());
            NudgeXPositiveCommand = new RelayCommand(_ => TargetX = Adjust(TargetX, _nudgeStep));
            NudgeXNegativeCommand = new RelayCommand(_ => TargetX = Adjust(TargetX, -_nudgeStep));
            NudgeYPositiveCommand = new RelayCommand(_ => TargetY = Adjust(TargetY, _nudgeStep));
            NudgeYNegativeCommand = new RelayCommand(_ => TargetY = Adjust(TargetY, -_nudgeStep));
            NudgeZPositiveCommand = new RelayCommand(_ => TargetZ = Adjust(TargetZ, _nudgeStep));
            NudgeZNegativeCommand = new RelayCommand(_ => TargetZ = Adjust(TargetZ, -_nudgeStep));

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };
            _refreshTimer.Tick += OnRefreshTick;

            AddLog("Navigation tester ready.");
        }

        private void RefreshPosition()
        {
            try
            {
                var tile = LocalPlayer.GetTilePosition();
                var exact = LocalPlayer.GetExactPosition();
                TilePosition = $"{tile.x}, {tile.y}, {tile.z}";
                ExactPosition = $"{exact.x:0.00}, {exact.y:0.00}, {exact.z:0.00}";
                IsMoving = LocalPlayer.IsMoving();
            }
            catch (Exception ex)
            {
                LastStatus = $"Failed to read player position: {ex.Message}";
            }
        }

        private void WalkTo()
        {
            if (!TryParseTarget(out var x, out var y, out var z))
            {
                LastStatus = "Enter X and Y (ints).";
                return;
            }

            try
            {
                var ok = Traversal.WalkTo(x, y, z, StopShortTiles, TimeoutMs, JitterTiles);
                LastStatus = ok ? $"Walking to {x},{y},{z} (stop {StopShortTiles})." : "WalkTo failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"WalkTo error: {ex.Message}";
                AddLog(LastStatus);
            }
        }

        private void ApiMinimapClickIcon()
        {
            try
            {
                var ok = Minimap.ClickIcon(ApiMinimapIconId);
                LastStatus = ok ? $"Minimap.ClickIcon({ApiMinimapIconId}): OK" : "Minimap.ClickIcon: Failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Minimap.ClickIcon error: {ex.Message}";
            }
        }

        private void ApiMinimapClickIconAt()
        {
            try
            {
                var ok = Minimap.ClickIconAt(ApiMinimapIconId, ApiMinimapX, ApiMinimapY);
                LastStatus = ok ? $"Minimap.ClickIconAt({ApiMinimapIconId},{ApiMinimapX},{ApiMinimapY}): OK" : "Minimap.ClickIconAt: Failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Minimap.ClickIconAt error: {ex.Message}";
            }
        }

        private void ApiSpellbookByIndex()
        {
            try
            {
                var ok = Teleports.Spellbook(ApiSpellbookIndex);
                LastStatus = ok ? $"Teleports.Spellbook({ApiSpellbookIndex}): OK" : "Teleports.Spellbook(index): Failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Teleports.Spellbook(index) error: {ex.Message}";
            }
        }

        private void ApiSpellbookByName()
        {
            try
            {
                var ok = Teleports.Spellbook(ApiTeleportName);
                LastStatus = ok ? $"Teleports.Spellbook('{ApiTeleportName}'): OK" : "Teleports.Spellbook(name): Failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Teleports.Spellbook(name) error: {ex.Message}";
            }
        }

        private void ApiJewelryTeleport()
        {
            try
            {
                var ok = Teleports.Jewelry(ApiJewelryItemName, ApiJewelryLocation1, ApiJewelryLocation2, ApiJewelryMenuLevel, ApiJewelryOffset);
                LastStatus = ok ? "Teleports.Jewelry: OK" : "Teleports.Jewelry: Failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Teleports.Jewelry error: {ex.Message}";
            }
        }

        private void ApiSpiritTree()
        {
            try
            {
                var ok = Teleports.SpiritTree(ApiTeleportName);
                LastStatus = ok ? "Teleports.SpiritTree: OK" : "Teleports.SpiritTree: Failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Teleports.SpiritTree error: {ex.Message}";
            }
        }

        private void ApiGlider()
        {
            try
            {
                var ok = Teleports.Glider(ApiTeleportName);
                LastStatus = ok ? "Teleports.Glider: OK" : "Teleports.Glider: Failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Teleports.Glider error: {ex.Message}";
            }
        }

        private void ApiFairy()
        {
            try
            {
                var ok = Teleports.Fairy(ApiTeleportName);
                LastStatus = ok ? "Teleports.Fairy: OK" : "Teleports.Fairy: Failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Teleports.Fairy error: {ex.Message}";
            }
        }

        private void ApiQuiver4()
        {
            try
            {
                var ok = Teleports.Quiver4(ApiTeleportName);
                LastStatus = ok ? "Teleports.Quiver4: OK" : "Teleports.Quiver4: Failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Teleports.Quiver4 error: {ex.Message}";
            }
        }

        private void WalkPath()
        {
            var points = ParsePath(PathInput).ToArray();
            if (points.Length == 0)
            {
                LastStatus = "Path input is empty or invalid (use x,y,z per line).";
                return;
            }

            try
            {
                var ok = Traversal.WalkPath(points, StopShortTiles, TimeoutMs, JitterTiles);
                LastStatus = ok ? $"Walking path ({points.Length} waypoints)." : "WalkPath failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"WalkPath error: {ex.Message}";
                AddLog(LastStatus);
            }
        }

        /// <summary>
        /// End-to-end webwalk test: resolve the destination (node id → tag → route name),
        /// plan via Dijkstra over the seeded graph, and execute with replanning. Logs the
        /// resolved target and per-edge results.
        /// </summary>
        private async Task TravelAsync()
        {
            var destination = TravelDestination.Trim();
            if (string.IsNullOrWhiteSpace(destination)) return;

            _travelCts?.Dispose();
            _travelCts = new CancellationTokenSource();
            IsTraveling = true;
            LastStatus = $"Traveling to '{destination}'…";
            AddLog($"Webwalk travel → '{destination}'");

            try
            {
                var result = await Navigation.ResolveAndTravelDetailedAsync(destination, profile: null, _travelCts.Token);

                if (result.PlanResult != null)
                {
                    foreach (var edge in result.PlanResult.Edges)
                        AddLog($"  [{(edge.Succeeded ? "ok" : "FAIL")}] {edge.Kind} {edge.FromNodeId} → {edge.ToNodeId} ({edge.ElapsedMs}ms){(edge.Succeeded ? "" : $" — {edge.Message}")}");
                }

                LastStatus = result.Succeeded
                    ? $"Arrived: {result.ResolvedDestination}"
                    : $"Travel failed ({result.FailReason}): {result.FailMessage}";
                AddLog(LastStatus);
            }
            catch (OperationCanceledException)
            {
                LastStatus = "Travel cancelled.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Travel error: {ex.Message}";
                AddLog(LastStatus);
            }
            finally
            {
                IsTraveling = false;
            }
        }

        private void TeleportSelected()
        {
            var selection = SelectedLodestone;
            if (selection == null) return;

            try
            {
                var ok = Traversal.Lodestone(selection.Destination, TeleportTimeoutMs);
                LastStatus = ok ? $"Lodestone to {selection.Name} issued." : $"Lodestone {selection.Name} failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"Lodestone error: {ex.Message}";
                AddLog(LastStatus);
            }
        }

        private void ClickTo()
        {
            if (!TryParseTarget(out var x, out var y, out var z))
            {
                LastStatus = "Enter X and Y (ints).";
                return;
            }

            try
            {
                var ok = Traversal.ClickTo(x, y, z, JitterTiles);
                LastStatus = ok ? $"Click issued to {x},{y},{z} (jitter {JitterTiles})." : "ClickTo failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"ClickTo error: {ex.Message}";
                AddLog(LastStatus);
            }
        }

        private void ClickPath()
        {
            var points = ParsePath(PathInput).ToArray();
            if (points.Length == 0)
            {
                LastStatus = "Path input is empty or invalid.";
                return;
            }

            try
            {
                var ok = Traversal.ClickPath(points, JitterTiles);
                LastStatus = ok ? $"ClickPath issued ({points.Length} waypoints)." : "ClickPath failed.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"ClickPath error: {ex.Message}";
                AddLog(LastStatus);
            }
        }

        private void WaitUntilWithin()
        {
            if (!TryParseTarget(out var x, out var y, out var z))
            {
                LastStatus = "Enter X and Y (ints).";
                return;
            }

            try
            {
                var ok = Traversal.WaitUntilWithin(x, y, z, StopShortTiles, TimeoutMs);
                LastStatus = ok ? $"Arrived within {StopShortTiles} tiles." : $"Timeout waiting to reach {x},{y},{z}.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"WaitUntilWithin error: {ex.Message}";
                AddLog(LastStatus);
            }
        }

        private void WaitWhileMoving()
        {
            try
            {
                var ok = Traversal.WaitWhileMoving(TimeoutMs);
                LastStatus = ok ? "Stopped moving within timeout." : "Still moving after timeout.";
                AddLog(LastStatus);
            }
            catch (Exception ex)
            {
                LastStatus = $"WaitWhileMoving error: {ex.Message}";
                AddLog(LastStatus);
            }
        }

        private void TeleportByName()
        {
            var dest = LodestoneSearch?.Trim();
            if (string.IsNullOrWhiteSpace(dest))
            {
                LastStatus = "Enter a lodestone name.";
                return;
            }

            try
            {
                var ok = Traversal.Lodestone(dest, TeleportTimeoutMs);
                LastStatus = ok ? $"Lodestone '{dest}' issued." : $"Lodestone '{dest}' failed.";
                AddLog(LastStatus);

                var matched = Lodestones.FirstOrDefault(l => l.Name.IndexOf(dest, StringComparison.OrdinalIgnoreCase) >= 0);
                if (matched != null)
                {
                    SelectedLodestone = matched;
                }
            }
            catch (Exception ex)
            {
                LastStatus = $"Lodestone error: {ex.Message}";
                AddLog(LastStatus);
            }
        }

        private void UseCurrentTile()
        {
            try
            {
                var tile = LocalPlayer.GetTilePosition();
                TargetX = tile.x.ToString();
                TargetY = tile.y.ToString();
                TargetZ = tile.z.ToString();
                LastStatus = $"Target set to current tile {tile.x},{tile.y},{tile.z}.";
            }
            catch (Exception ex)
            {
                LastStatus = $"Failed to read current tile: {ex.Message}";
            }
        }

        private static string Adjust(string current, int delta)
        {
            if (!int.TryParse(current, out var val)) val = 0;
            val += delta;
            return val.ToString();
        }

        private bool TryParseTarget(out int x, out int y, out int z)
        {
            x = y = z = 0;
            if (!int.TryParse(TargetX, out x)) return false;
            if (!int.TryParse(TargetY, out y)) return false;
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

        private IEnumerable<(int x, int y, int z)> ParsePath(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) yield break;

            var segments = input
                .Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0);

            foreach (var seg in segments)
            {
                var parts = seg.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (!int.TryParse(parts[0], out var x)) continue;
                if (!int.TryParse(parts[1], out var y)) continue;
                int z = 0;
                if (parts.Length >= 3 && int.TryParse(parts[2], out var zVal)) z = zVal;
                yield return (x, y, z);
            }
        }

        private void AddLog(string message)
        {
            var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            ActivityLog.Insert(0, entry);
            while (ActivityLog.Count > 25)
            {
                ActivityLog.RemoveAt(ActivityLog.Count - 1);
            }
        }

        private void RefreshCommandStates() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();

        private static IReadOnlyList<LodestoneOption> BuildLodestones()
        {
            return Enum.GetValues<Lodestone>()
                .Select(lodestone => new LodestoneOption(
                    LodestoneData.GetName(lodestone),
                    lodestone))
                .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            OnDeactivated();
            _disposed = true;
            try { _refreshTimer.Stop(); } catch { /* ignored */ }
            _refreshTimer.Tick -= OnRefreshTick;
            try { _travelCts?.Cancel(); } catch { /* ignored */ }
            try { _travelCts?.Dispose(); } catch { /* ignored */ }
            _travelCts = null;
        }

        public void OnActivated()
        {
            if (_disposed)
            {
                return;
            }

            RefreshDestinationOptions();

            if (_isActive)
            {
                RefreshPosition();
                return;
            }

            _isActive = true;
            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }

            RefreshPosition();
        }

        public void OnDeactivated()
        {
            if (_disposed || !_isActive)
            {
                return;
            }

            _isActive = false;
            try { _refreshTimer.Stop(); } catch { /* ignored */ }
            if (IsTraveling)
            {
                AddLog("Travel stopped: the Travel section was deactivated while running.");
                _travelCts?.Cancel();
            }
        }

        private void OnRefreshTick(object? sender, EventArgs e) => RefreshPosition();
    }

    public record LodestoneOption(string Name, Lodestone Destination)
    {
        public string Display => Name;
    }
}
