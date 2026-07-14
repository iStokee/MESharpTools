using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MESharp.API;
using MESharp.CacheDerivation;
using MESharp.Commands;
using MESharp.ViewModels;

namespace MESharp.ViewModels
{
    /// <summary>
    /// Single hub view model behind the Cache tool's four tabs:
    ///   Status   — game-cache load state (CacheManager) + chisel dump versions/download.
    ///   Browse   — searchable view over a downloaded chisel dump (enums, items, ...).
    ///   Compare  — which categories the C# API exposes vs. the chisel reference (gap report).
    ///   Diff     — chisel's published diff for the selected category.
    /// All chisel I/O is async and resumes on the UI thread (no ConfigureAwait(false) here).
    /// </summary>
    public sealed class CacheHubViewModel : BaseViewModel, IActivatableViewModel, IDisposable
    {
        private const int MaxBrowseRows = 5000;

        private readonly ChiselCacheService _chisel = new();
        private JsonDocument? _browseDocument;
        private bool _activated;

        public CacheHubViewModel()
        {
            ChiselCategories = new ObservableCollection<ChiselCategoryRow>(
                _chisel.Categories.Select(c => new ChiselCategoryRow(c)));
            BrowseEntries = new ObservableCollection<BrowseEntryRow>();
            GameTypes = new ObservableCollection<CacheTypeStatus>();
            GapRows = new ObservableCollection<GapRow>(BuildGapReport());

            BrowseCategoryOptions = _chisel.Categories.ToList();
            _selectedBrowseCategory = BrowseCategoryOptions.FirstOrDefault(c => c.Category == ChiselCategory.Enums)
                                       ?? BrowseCategoryOptions.FirstOrDefault();

            RefreshGameStatusCommand = new RelayCommand(_ => RefreshGameStatus());
            ReloadGameCacheCommand = new RelayCommand(async _ => await ReloadGameCacheAsync(), _ => !IsBusy);
            RefreshChiselStatusCommand = new RelayCommand(async _ => await RefreshChiselStatusAsync(), _ => !IsBusy);
            DownloadAllCommand = new RelayCommand(async _ => await DownloadAllAsync(), _ => !IsBusy);
            DownloadCategoryCommand = new RelayCommand(async row => await DownloadCategoryAsync(row as ChiselCategoryRow), _ => !IsBusy);
            LoadBrowseCommand = new RelayCommand(async _ => await LoadBrowseAsync(), _ => !IsBusy);
            LoadDiffCommand = new RelayCommand(async _ => await LoadDiffAsync(), _ => !IsBusy);
        }

        // ---- Game cache (Status tab) -------------------------------------------------

        public ObservableCollection<CacheTypeStatus> GameTypes { get; }

        private bool _gameCacheLoaded;
        public bool GameCacheLoaded { get => _gameCacheLoaded; private set => SetProperty(ref _gameCacheLoaded, value); }

        private string _gameStatusText = "Not queried.";
        public string GameStatusText { get => _gameStatusText; private set => SetProperty(ref _gameStatusText, value); }

        private string _mapDatasetStatusText = "Map/collision datasets not queried.";
        public string MapDatasetStatusText { get => _mapDatasetStatusText; private set => SetProperty(ref _mapDatasetStatusText, value); }

        // ---- Chisel dumps (Status tab) -----------------------------------------------

        public ObservableCollection<ChiselCategoryRow> ChiselCategories { get; }

        public string DumpDirectory => _chisel.DumpDirectory;

        // ---- Browse tab --------------------------------------------------------------

        public IReadOnlyList<ChiselCategoryInfo> BrowseCategoryOptions { get; }

        private ChiselCategoryInfo? _selectedBrowseCategory;
        public ChiselCategoryInfo? SelectedBrowseCategory
        {
            get => _selectedBrowseCategory;
            set => SetProperty(ref _selectedBrowseCategory, value);
        }

        private string _browseSearch = string.Empty;
        public string BrowseSearch
        {
            get => _browseSearch;
            set { if (SetProperty(ref _browseSearch, value)) ApplyBrowseFilter(); }
        }

        public ObservableCollection<BrowseEntryRow> BrowseEntries { get; }

        private BrowseEntryRow? _selectedBrowseEntry;
        public BrowseEntryRow? SelectedBrowseEntry
        {
            get => _selectedBrowseEntry;
            set
            {
                if (SetProperty(ref _selectedBrowseEntry, value))
                    SelectedEntryJson = value?.RenderDetail() ?? string.Empty;
            }
        }

        private string _selectedEntryJson = string.Empty;
        public string SelectedEntryJson { get => _selectedEntryJson; private set => SetProperty(ref _selectedEntryJson, value); }

        private string _browseStatus = "Pick a category and click Load.";
        public string BrowseStatus { get => _browseStatus; private set => SetProperty(ref _browseStatus, value); }

        private List<BrowseEntryRow> _allBrowseEntries = new();

        // ---- Compare / Gap report tab ------------------------------------------------

        public ObservableCollection<GapRow> GapRows { get; }

        // ---- Diff tab ----------------------------------------------------------------

        private ChiselCategoryInfo? _selectedDiffCategory;
        public ChiselCategoryInfo? SelectedDiffCategory { get => _selectedDiffCategory; set => SetProperty(ref _selectedDiffCategory, value); }

        public IReadOnlyList<ChiselCategoryInfo> DiffCategoryOptions => BrowseCategoryOptions;

        private string _diffText = string.Empty;
        public string DiffText { get => _diffText; private set => SetProperty(ref _diffText, value); }

        // ---- Shared --------------------------------------------------------------------

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set { if (SetProperty(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested(); }
        }

        private string _busyText = string.Empty;
        public string BusyText { get => _busyText; private set => SetProperty(ref _busyText, value); }

        public ICommand RefreshGameStatusCommand { get; }
        public ICommand ReloadGameCacheCommand { get; }
        public ICommand RefreshChiselStatusCommand { get; }
        public ICommand DownloadAllCommand { get; }
        public ICommand DownloadCategoryCommand { get; }
        public ICommand LoadBrowseCommand { get; }
        public ICommand LoadDiffCommand { get; }

        public void OnActivated()
        {
            if (_activated) return;
            _activated = true;
            _selectedDiffCategory ??= BrowseCategoryOptions.FirstOrDefault();
            RefreshGameStatus();
            _ = RefreshChiselStatusAsync();
        }

        public void OnDeactivated() { }

        // ---- Game cache ----

        private void RefreshGameStatus()
        {
            RefreshMapDatasetStatus();
            try
            {
                var status = CacheManager.GetStatus();
                GameCacheLoaded = status.IsLoaded;
                GameTypes.Clear();
                foreach (var t in status.Types) GameTypes.Add(t);
                GameStatusText = status.IsLoaded
                    ? $"Cache loaded (store={status.StoreLoaded}, types={status.TypesLoaded})."
                    : "Cache not loaded — is ME injected and cache enabled in settings.json?";
            }
            catch (Exception ex)
            {
                // Thrown when the native XInput1_4.dll export isn't present (e.g. running outside ME).
                GameCacheLoaded = false;
                GameTypes.Clear();
                GameStatusText = "Game cache unavailable (not running inside ME): " + ex.Message;
            }
        }

        private void RefreshMapDatasetStatus()
        {
            try
            {
                var gridDir = CollisionDeriver.DefaultOutputDirectory;
                var squareCount = Directory.Exists(gridDir)
                    ? Directory.EnumerateFiles(gridDir, "*_*.json", SearchOption.TopDirectoryOnly).Count()
                    : 0;
                var componentDir = Path.Combine(gridDir, "components");
                var componentCount = Directory.Exists(componentDir)
                    ? Directory.EnumerateFiles(componentDir, "*_*.json", SearchOption.TopDirectoryOnly).Count()
                    : 0;
                var auditPath = Path.Combine(gridDir, "collision-audit.json");
                var audit = "no master audit";
                if (File.Exists(auditPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(auditPath));
                    var root = doc.RootElement;
                    var generated = root.TryGetProperty("GeneratedAtUtc", out var at) ? at.GetDateTime().ToLocalTime().ToString("g") : "unknown time";
                    var directions = root.TryGetProperty("DirectionMismatches", out var dm) ? dm.GetInt64() : 0;
                    var missing = root.TryGetProperty("MissingGridTiles", out var mg) ? mg.GetInt64() : 0;
                    audit = $"audit {generated}: {directions:N0} edge mismatches, {missing:N0} missing tiles";
                }
                MapDatasetStatusText = $"Map data: {squareCount:N0} collision squares, {componentCount:N0} component shards; {audit}. Path: {gridDir}";
                var live = CacheManager.GetLiveJs5Status();
                if (live.IsSupported)
                    MapDatasetStatusText += live.IsAvailable
                        ? $" Live JS5: {live.NodeCount:N0} cached archives (provider 0x{live.ProviderAddress:X})."
                        : $" Live JS5: unavailable ({live.Error})";
            }
            catch (Exception ex)
            {
                MapDatasetStatusText = "Map dataset status unavailable: " + ex.Message;
            }
        }

        private async Task ReloadGameCacheAsync()
        {
            await RunBusy("Reloading game cache from disk...", async () =>
            {
                var ok = await Task.Run(CacheManager.Reload);
                RefreshGameStatus();
                GameStatusText = ok
                    ? "Cache reloaded from disk."
                    : "Cache reload failed - previous cache snapshot was kept if one was loaded.";
            });
        }

        // ---- Chisel ----

        private async Task RefreshChiselStatusAsync()
        {
            await RunBusy("Checking chisel dump versions…", async () =>
            {
                foreach (var row in ChiselCategories)
                {
                    row.ApplyLocal(_chisel.GetLocal(row.Info.Category), _chisel.HasLocal(row.Info.Category));
                    var remote = await ProbeSafe(row.Info.Category);
                    row.ApplyRemote(remote, _chisel.IsStale(row.Info.Category, remote));
                }
            });
        }

        private async Task<ChiselRemoteInfo?> ProbeSafe(ChiselCategory category)
        {
            try { return await _chisel.ProbeAsync(category); }
            catch { return null; }
        }

        private async Task DownloadAllAsync()
        {
            await RunBusy("Downloading stale dumps…", async () =>
            {
                foreach (var row in ChiselCategories)
                {
                    BusyText = $"Downloading {row.Info.DisplayName}…";
                    var result = await _chisel.DownloadIfStaleAsync(row.Info.Category);
                    row.ApplyDownload(result, _chisel.GetLocal(row.Info.Category), _chisel.HasLocal(row.Info.Category));
                }
            });
        }

        private async Task DownloadCategoryAsync(ChiselCategoryRow? row)
        {
            if (row is null) return;
            await RunBusy($"Downloading {row.Info.DisplayName}…", async () =>
            {
                var result = await _chisel.DownloadIfStaleAsync(row.Info.Category, force: true);
                row.ApplyDownload(result, _chisel.GetLocal(row.Info.Category), _chisel.HasLocal(row.Info.Category));
            });
        }

        // ---- Browse ----

        private async Task LoadBrowseAsync()
        {
            var info = SelectedBrowseCategory;
            if (info is null) return;

            await RunBusy($"Loading {info.DisplayName} dump…", async () =>
            {
                if (!_chisel.HasLocal(info.Category))
                {
                    BusyText = $"Downloading {info.DisplayName}…";
                    var dl = await _chisel.DownloadIfStaleAsync(info.Category);
                    if (!dl.Ok)
                    {
                        BrowseStatus = $"Download failed: {dl.Error}";
                        return;
                    }
                }

                var json = await _chisel.ReadLocalJsonAsync(info.Category);
                if (json is null)
                {
                    BrowseStatus = "No local dump to read.";
                    return;
                }

                // Parsing happens off the UI thread; the document is owned by the VM until replaced.
                var parsed = await Task.Run(() => BuildEntries(json));
                _browseDocument?.Dispose();
                _browseDocument = parsed.Document;
                _allBrowseEntries = parsed.Entries;
                ApplyBrowseFilter();
                BrowseStatus = $"{info.DisplayName}: {_allBrowseEntries.Count:N0} entries loaded.";
            });
        }

        private sealed record ParsedDump(JsonDocument Document, List<BrowseEntryRow> Entries);

        private static ParsedDump BuildEntries(string json)
        {
            var doc = JsonDocument.Parse(json);
            var entries = new List<BrowseEntryRow>();
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                    entries.Add(new BrowseEntryRow(prop.Name, Summarize(prop.Value), prop.Value));
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (var item in root.EnumerateArray())
                    entries.Add(new BrowseEntryRow(IndexKey(item, i++), Summarize(item), item));
            }
            else
            {
                entries.Add(new BrowseEntryRow("(root)", Summarize(root), root));
            }
            return new ParsedDump(doc, entries);
        }

        private static string IndexKey(JsonElement item, int index)
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "id", "Id", "ID" })
                    if (item.TryGetProperty(key, out var idv) && idv.ValueKind == JsonValueKind.Number)
                        return idv.GetRawText();
            }
            return index.ToString();
        }

        private static string Summarize(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "name", "Name", "title", "label" })
                    if (value.TryGetProperty(key, out var nv) && nv.ValueKind == JsonValueKind.String)
                        return nv.GetString() ?? string.Empty;
                return "{ … }";
            }
            if (value.ValueKind == JsonValueKind.Array)
                return $"[ {value.GetArrayLength()} ]";
            return value.ToString();
        }

        private void ApplyBrowseFilter()
        {
            var term = _browseSearch?.Trim() ?? string.Empty;
            IEnumerable<BrowseEntryRow> source = _allBrowseEntries;
            if (term.Length > 0)
            {
                source = source.Where(e =>
                    e.Key.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    e.Display.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            var shown = source.Take(MaxBrowseRows).ToList();
            BrowseEntries.Clear();
            foreach (var e in shown) BrowseEntries.Add(e);

            if (_allBrowseEntries.Count > shown.Count)
                BrowseStatus = $"Showing {shown.Count:N0} of {_allBrowseEntries.Count:N0} (refine search to narrow).";
        }

        // ---- Diff ----

        private async Task LoadDiffAsync()
        {
            var info = SelectedDiffCategory;
            if (info is null) return;
            await RunBusy($"Fetching {info.DisplayName} diff…", async () =>
            {
                var diff = await _chisel.GetDiffAsync(info.Category);
                DiffText = string.IsNullOrWhiteSpace(diff)
                    ? "(no diff available for this category)"
                    : diff!;
            });
        }

        // ---- Gap report ----

        private static IEnumerable<GapRow> BuildGapReport() => new[]
        {
            new GapRow("Items", "Items.cs", true, "Name→id, buy limit via Cache::GetItem."),
            new GapRow("NPCs", "NPC.cs", false, "Live scene only; cache defs not exposed (demand-driven)."),
            new GapRow("Locations", "Objects.cs", false, "Live scene only; object defs not exposed (demand-driven)."),
            new GapRow("Structs", "—", false, "In C++ Cache but no C# export yet (demand-driven)."),
            new GapRow("DB Rows", "DBRows.cs", true, "Typed column reads + perk metadata."),
            new GapRow("Enums", "—", false, "Not parsed in C++ at all; browse via chisel dump."),
            new GapRow("Achievements", "—", false, "In C++ Cache but no C# export yet (demand-driven)."),
            new GapRow("Quests", "Quest.cs", true, "Names, requirements, progress varbits."),
            new GapRow("Varbits", "Varbits.cs", true, "Metadata (base/start/end) + live value reads."),
            new GapRow("Cache status", "CacheManager.cs", true, "Load state + per-type counts (new in this tool)."),
        };

        private async Task RunBusy(string text, Func<Task> work)
        {
            if (IsBusy) return;
            IsBusy = true;
            BusyText = text;
            try { await work(); }
            catch (Exception ex) { BusyText = "Error: " + ex.Message; return; }
            finally { IsBusy = false; }
            BusyText = string.Empty;
        }

        public void Dispose()
        {
            _browseDocument?.Dispose();
            _chisel.Dispose();
        }
    }

    /// <summary>A chisel category row on the Status tab: static info + live local/remote freshness.</summary>
    public sealed class ChiselCategoryRow : BaseViewModel
    {
        public ChiselCategoryRow(ChiselCategoryInfo info) => Info = info;

        public ChiselCategoryInfo Info { get; }
        public string DisplayName => Info.DisplayName;

        private bool _hasLocal;
        public bool HasLocal { get => _hasLocal; private set => SetProperty(ref _hasLocal, value); }

        private string _localText = "not downloaded";
        public string LocalText { get => _localText; private set => SetProperty(ref _localText, value); }

        private bool _isStale = true;
        public bool IsStale { get => _isStale; private set => SetProperty(ref _isStale, value); }

        private string _statusText = "—";
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public void ApplyLocal(ChiselManifestEntry? local, bool hasLocal)
        {
            HasLocal = hasLocal;
            if (local is not null && hasLocal)
            {
                var size = local.ContentLength > 0 ? $"{local.ContentLength / 1024.0 / 1024.0:0.0} MiB" : "?";
                LocalText = $"{size}, {local.DownloadedAtUtc.LocalDateTime:yyyy-MM-dd HH:mm}";
            }
            else
            {
                LocalText = "not downloaded";
            }
        }

        public void ApplyRemote(ChiselRemoteInfo? remote, bool isStale)
        {
            IsStale = isStale;
            if (remote is null)
                StatusText = HasLocal ? "local copy (remote unknown)" : "not downloaded";
            else
                StatusText = isStale ? (HasLocal ? "update available" : "available") : "up to date";
        }

        public void ApplyDownload(ChiselDownloadResult result, ChiselManifestEntry? local, bool hasLocal)
        {
            ApplyLocal(local, hasLocal);
            if (!result.Ok) { StatusText = "error: " + result.Error; return; }
            IsStale = false;
            StatusText = result.Downloaded ? "downloaded" : "already current";
        }
    }

    /// <summary>One browsable entry from a chisel dump; detail rendered lazily from its JSON element.</summary>
    public sealed class BrowseEntryRow
    {
        private readonly JsonElement _element;

        public BrowseEntryRow(string key, string display, JsonElement element)
        {
            Key = key;
            Display = display;
            _element = element;
        }

        public string Key { get; }
        public string Display { get; }

        public string RenderDetail()
            => JsonSerializer.Serialize(_element, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>One row of the Compare/Gap report: chisel category vs. what the C# API exposes.</summary>
    public sealed record GapRow(string Category, string ApiClass, bool Exposed, string Notes)
    {
        public string ExposedText => Exposed ? "exposed" : "gap";
    }
}
