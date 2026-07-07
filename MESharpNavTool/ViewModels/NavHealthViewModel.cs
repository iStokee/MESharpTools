using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MESharp.API;
using MESharp.CacheDerivation;
using MESharp.Commands;

namespace MESharp.ViewModels
{
    /// <summary>A single health row: a coloured dot (State) + a title and one-line detail.</summary>
    public sealed class HealthIndicator : BaseViewModel
    {
        private string _title = "";
        private string _detail = "";
        private string _state = "off"; // ok | warn | error | off

        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public string Detail { get => _detail; set => SetProperty(ref _detail, value); }
        public string State { get => _state; set => SetProperty(ref _state, value); }
    }

    /// <summary>
    /// Diagnostics / "Health" pane for the navigation hub: at-a-glance status of the data the
    /// webwalk system depends on (collision grids, obstacle catalog, ladder seed, the graph, and
    /// the rs3cache dump) plus buttons to regenerate/reload them. Regeneration runs the shared
    /// <see cref="CollisionDeriver"/> / <see cref="ObstacleDeriver"/> in-process on a background
    /// thread — no external exe or SDK needed — then refreshes the live consumers.
    /// </summary>
    public sealed class NavHealthViewModel : BaseViewModel, IActivatableViewModel
    {
        private string _dumpDirectory;
        private string _dumpCommand = "";
        private string _dumpArguments = "";
        private bool _treatWildernessDitchAsCrossable;
        private bool _isBusy;
        private string _busyOperation = "";
        private string _log = "";
        private CancellationTokenSource? _busyCts;

        public ObservableCollection<HealthIndicator> Indicators { get; } = new();

        public NavHealthViewModel()
        {
            var settings = Services.NavDiagnosticsStore.Load();
            _dumpDirectory = settings.DumpDirectory;
            _dumpCommand = settings.DumpCommand;
            _dumpArguments = settings.DumpArguments;
            _treatWildernessDitchAsCrossable = settings.TreatWildernessDitchAsCrossable;

            RefreshCommand = new RelayCommand(_ => Refresh(), _ => !IsBusy);
            SaveDumpPathCommand = new RelayCommand(_ => SaveSettings(), _ => !IsBusy);
            CancelBusyCommand = new RelayCommand(_ => { try { _busyCts?.Cancel(); } catch { } Append("Cancellation requested…"); }, _ => IsBusy);

            RegenerateCollisionCommand = new RelayCommand(_ => RegenerateCollision(), _ => !IsBusy && DumpLooksValid());
            RegenerateObstaclesCommand = new RelayCommand(_ => RegenerateObstacles(), _ => !IsBusy && DumpLooksValid());
            ImportLadderSeedsCommand = new RelayCommand(_ => ImportLadderSeeds(), _ => !IsBusy && File.Exists(LadderSeedPath));
            RegenerateAreasCommand = new RelayCommand(_ => RegenerateAreas(), _ => !IsBusy && DumpLooksValid());
            RegenerateNpcAreasCommand = new RelayCommand(_ => RegenerateNpcAreas(), _ => !IsBusy && DumpLooksValid());
            GenerateWikiMapDataCommand = new RelayCommand(_ => GenerateWikiMapData(), _ => !IsBusy);
            RegenerateDumpCommand = new RelayCommand(_ => RegenerateDump(), _ => !IsBusy && !string.IsNullOrWhiteSpace(DumpCommand));
            ImportAreaSeedsCommand = new RelayCommand(_ => ImportAreaSeeds(), _ => !IsBusy && (File.Exists(AreaSeedPath) || File.Exists(NpcAreaSeedPath)));

            ReloadCollisionCommand = new RelayCommand(_ => { CollisionPathfinder.SetGridDirectory(null); Refresh(); Append("Reloaded collision grids."); }, _ => !IsBusy);
            ReloadCatalogCommand = new RelayCommand(_ => { ObstacleCatalog.Invalidate(); Refresh(); Append($"Reloaded obstacle catalog ({ObstacleCatalog.Count} entries)."); }, _ => !IsBusy);
            ReloadGraphCommand = new RelayCommand(_ => { WebwalkGraph.ReloadGraph(); Refresh(); Append("Reloaded webwalk graph."); }, _ => !IsBusy);

            Refresh();
        }

        // ── bound state ─────────────────────────────────────────────────────────

        public string DumpDirectory
        {
            get => _dumpDirectory;
            set
            {
                if (SetProperty(ref _dumpDirectory, value))
                {
                    RaisePropertyChanged(nameof(DumpStatusText));
                    InvalidateCommands();
                }
            }
        }

        /// <summary>External cache-dump tool path (e.g. rs3cache.exe). Not shipped with MESharp.</summary>
        public string DumpCommand
        {
            get => _dumpCommand;
            set { if (SetProperty(ref _dumpCommand, value)) InvalidateCommands(); }
        }

        /// <summary>Args for the dump tool. "{output}" is replaced with the dump directory.</summary>
        public string DumpArguments
        {
            get => _dumpArguments;
            set => SetProperty(ref _dumpArguments, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaisePropertyChanged(nameof(IsNotBusy));
                    InvalidateCommands();
                }
            }
        }

        public bool IsNotBusy => !IsBusy;
        public string BusyOperation { get => _busyOperation; private set => SetProperty(ref _busyOperation, value); }
        public string Log { get => _log; private set => SetProperty(ref _log, value); }

        public string DumpStatusText => DumpLooksValid()
            ? "Dump looks valid (location_configs.json + tiles + locations found)."
            : "Dump not found here — set the rs3cache dump folder to enable regeneration.";

        public bool TreatWildernessDitchAsCrossable
        {
            get => _treatWildernessDitchAsCrossable;
            set
            {
                if (SetProperty(ref _treatWildernessDitchAsCrossable, value))
                    InvalidateCommands();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand SaveDumpPathCommand { get; }
        public ICommand CancelBusyCommand { get; }
        public ICommand RegenerateCollisionCommand { get; }
        public ICommand RegenerateObstaclesCommand { get; }
        public ICommand ImportLadderSeedsCommand { get; }
        public ICommand RegenerateAreasCommand { get; }
        public ICommand RegenerateNpcAreasCommand { get; }
        public ICommand GenerateWikiMapDataCommand { get; }
        public ICommand RegenerateDumpCommand { get; }
        public ICommand ImportAreaSeedsCommand { get; }
        public ICommand ReloadCollisionCommand { get; }
        public ICommand ReloadCatalogCommand { get; }
        public ICommand ReloadGraphCommand { get; }

        public void OnActivated() => Refresh();
        public void OnDeactivated() { }

        // ── health snapshot ───────────────────────────────────────────────────────

        private string LadderSeedPath => Path.Combine(ObstacleDeriver.DefaultOutputDirectory, "ladders.seed.json");
        private string CatalogPath => Path.Combine(ObstacleDeriver.DefaultOutputDirectory, "obstacles.json");
        private string AreaSeedPath => Path.Combine(AreaDeriver.DefaultOutputDirectory, "areas.seed.json");
        private string NpcAreaSeedPath => Path.Combine(NpcAreaDeriver.DefaultOutputDirectory, "npc_areas.seed.json");

        public void Refresh()
        {
            Indicators.Clear();

            // Collision grids
            var gridDir = CollisionPathfinder.GetGridDirectory();
            var gridCount = SafeCount(gridDir, "*_*.json");
            Indicators.Add(new HealthIndicator
            {
                Title = "Collision grids",
                State = CollisionPathfinder.IsAvailable() ? "ok" : "error",
                Detail = CollisionPathfinder.IsAvailable()
                    ? $"{gridCount} squares in {gridDir}"
                    : $"No grids found in {gridDir} — regenerate from the dump."
            });

            Indicators.Add(new HealthIndicator
            {
                Title = "Wilderness ditch crossing",
                State = TreatWildernessDitchAsCrossable ? "ok" : "warn",
                Detail = TreatWildernessDitchAsCrossable
                    ? "Next collision regeneration keeps Wilderness ditch crossings pathable; runtime pulse will click Cross."
                    : "Disabled. South/north Wilderness routes may remain disconnected by the static grid."
            });

            // Obstacle catalog
            var catalogExists = File.Exists(CatalogPath);
            Indicators.Add(new HealthIndicator
            {
                Title = "Obstacle catalog",
                State = catalogExists ? "ok" : "warn",
                Detail = catalogExists
                    ? $"{ObstacleCatalog.Count} objects ({CatalogPath}); resolver also has built-in verb heuristics."
                    : "obstacles.json missing — the resolver falls back to verb heuristics. Regenerate to enrich it."
            });

            // Ladder seed fragment
            var ladderExists = File.Exists(LadderSeedPath);
            var ladderEdges = ladderExists ? CountSeedEdges(LadderSeedPath) : 0;
            Indicators.Add(new HealthIndicator
            {
                Title = "Ladder plane-links",
                State = ladderExists ? "ok" : "off",
                Detail = ladderExists
                    ? $"{ladderEdges} edges in ladders.seed.json — use Import to merge into the graph."
                    : "ladders.seed.json not generated yet."
            });

            // Area seed fragments (cache-derived banks/skilling from objects + fishing/shops/slayer from NPCs)
            var objAreas = File.Exists(AreaSeedPath) ? CountSeedAreas(AreaSeedPath) : 0;
            var npcAreas = File.Exists(NpcAreaSeedPath) ? CountSeedAreas(NpcAreaSeedPath) : 0;
            var anyAreaSeed = File.Exists(AreaSeedPath) || File.Exists(NpcAreaSeedPath);
            Indicators.Add(new HealthIndicator
            {
                Title = "Cache-derived areas",
                State = anyAreaSeed ? "ok" : "off",
                Detail = anyAreaSeed
                    ? $"{objAreas} object-areas + {npcAreas} NPC-areas — use Import to merge into the graph."
                    : "Not generated yet (Derive areas / NPC areas from the dump)."
            });

            // Webwalk graph
            WebwalkGraphData? graphSnapshot = null;
            try
            {
                graphSnapshot = WebwalkGraph.GetGraph();
                Indicators.Add(new HealthIndicator
                {
                    Title = "Webwalk graph",
                    State = graphSnapshot.Nodes.Count > 0 ? "ok" : "warn",
                    Detail = $"{graphSnapshot.Nodes.Count} nodes · {graphSnapshot.Edges.Count} edges ({WebwalkGraph.GetGraphStorePath()})"
                });
            }
            catch (Exception ex)
            {
                Indicators.Add(new HealthIndicator { Title = "Webwalk graph", State = "error", Detail = ex.Message });
            }

            if (graphSnapshot != null)
            {
                var wikiNodes = graphSnapshot.Nodes.Count(n =>
                    string.Equals(n.Source, "wiki", StringComparison.OrdinalIgnoreCase) ||
                    n.Tags.Any(t => string.Equals(t, "wiki", StringComparison.OrdinalIgnoreCase)));
                var wikiEdges = graphSnapshot.Edges.Count(e =>
                    string.Equals(e.Source, "wiki", StringComparison.OrdinalIgnoreCase));
                var wikiAreas = graphSnapshot.Areas.Count(a =>
                    string.Equals(a.Source, "wiki", StringComparison.OrdinalIgnoreCase) ||
                    a.Tags.Any(t => string.Equals(t, "wiki", StringComparison.OrdinalIgnoreCase)));
                Indicators.Add(new HealthIndicator
                {
                    Title = "Wiki map web data",
                    State = wikiNodes + wikiEdges + wikiAreas > 0 ? "ok" : "off",
                    Detail = wikiNodes + wikiEdges + wikiAreas > 0
                        ? $"{wikiNodes} nodes, {wikiEdges} edges, {wikiAreas} areas sourced from RuneScape Wiki maps."
                        : "Not generated yet. Use Generate Wiki Map Web Data to import map-derived nodes, entrances, and areas."
                });
            }

            // Dump
            Indicators.Add(new HealthIndicator
            {
                Title = "rs3cache dump",
                State = DumpLooksValid() ? "ok" : "off",
                Detail = DumpLooksValid() ? $"Ready at {DumpDirectory}" : DumpStatusText
            });

            RaisePropertyChanged(nameof(DumpStatusText));
            InvalidateCommands();
        }

        // ── regeneration (background) ──────────────────────────────────────────────

        private void RegenerateCollision()
        {
            SaveSettings();
            RunBackground("Regenerating collision grids", (log, ct) =>
            {
                var options = new CollisionDeriveOptions
                {
                    TreatWildernessDitchAsCrossable = TreatWildernessDitchAsCrossable
                };
                log($"Options: Wilderness ditch crossable = {options.TreatWildernessDitchAsCrossable}");
                var result = CollisionDeriver.Derive(DumpDirectory, outDir: null, log: log, ct: ct, options: options);
                CollisionPathfinder.SetGridDirectory(null); // drop cache → lazy reload from default dir
                return $"Collision grids regenerated: {result.SquaresWritten} squares ({result.Skipped} skipped).";
            });
        }

        private void RegenerateObstacles()
        {
            RunBackground("Regenerating obstacle catalog + ladder seeds", (log, ct) =>
            {
                var result = ObstacleDeriver.Derive(DumpDirectory, outDir: null, options: null, log: log, ct: ct);
                ObstacleCatalog.Invalidate();
                return $"Catalog regenerated: {result.CatalogEntries} objects, {result.LadderPairs} ladder pairs " +
                       $"({result.Edges} seed edges in ladders.seed.json).";
            });
        }

        private void ImportLadderSeeds()
        {
            RunBackground("Importing ladder seeds into the graph", (log, ct) =>
            {
                var json = File.ReadAllText(LadderSeedPath);
                var fragment = JsonSerializer.Deserialize<WebwalkGraphData>(json, SeedReadOptions) ?? new WebwalkGraphData();
                log($"Read {fragment.Nodes.Count} nodes / {fragment.Edges.Count} edges from ladders.seed.json");
                if (!WebwalkGraph.TryImport(fragment.Nodes, fragment.Edges, replace: false, source: "cache",
                        out var error, out var nodesUpserted, out var edgesUpserted))
                    throw new InvalidOperationException(error ?? "Import rejected.");
                return $"Imported {nodesUpserted} nodes / {edgesUpserted} edges into the webwalk graph.";
            });
        }

        private void RegenerateAreas()
        {
            RunBackground("Deriving named areas from the dump (objects)", (log, ct) =>
            {
                var result = AreaDeriver.Derive(DumpDirectory, outDir: null, options: null, log: log, ct: ct);
                return $"Areas derived: {result.ConfigsMatched} bank/skilling configs, {result.Placements} placements " +
                       $"→ {result.Areas} areas in areas.seed.json.";
            });
        }

        private void RegenerateNpcAreas()
        {
            RunBackground("Deriving NPC areas from the dump (fishing/shops/slayer)", (log, ct) =>
            {
                var result = NpcAreaDeriver.Derive(DumpDirectory, outDir: null, options: null, log: log, ct: ct);
                return $"NPC areas derived: {result.ConfigsMatched} npc configs, {result.Spawns} spawns " +
                       $"→ {result.Areas} areas in npc_areas.seed.json.";
            });
        }

        private void GenerateWikiMapData()
        {
            RunBackground("Generating wiki map web data", (log, ct) =>
            {
                var result = RuneScapeWikiDungeonImporter.DiscoverAndImportNavigationMapsAsync(
                        limit: 120,
                        replaceExactIds: true,
                        replaceMatchingAreas: true,
                        log: log,
                        cancellationToken: ct)
                    .GetAwaiter()
                    .GetResult();
                return result.Message;
            });
        }

        private void RegenerateDump()
        {
            SaveSettings();
            RunBackground("Running the external cache dump", (log, ct) =>
            {
                var exe = DumpCommand?.Trim();
                if (string.IsNullOrWhiteSpace(exe))
                    throw new InvalidOperationException("Set the dump tool path first (DumpCommand).");

                var args = (DumpArguments ?? string.Empty).Replace("{output}", DumpDirectory);
                Directory.CreateDirectory(DumpDirectory);
                log($"$ {exe} {args}");

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Directory.Exists(DumpDirectory) ? DumpDirectory : Environment.CurrentDirectory
                };

                using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                while (!proc.WaitForExit(250))
                {
                    if (ct.IsCancellationRequested)
                    {
                        try { proc.Kill(entireProcessTree: true); } catch { }
                        throw new OperationCanceledException("Dump cancelled.");
                    }
                }
                proc.WaitForExit(); // flush async readers

                if (proc.ExitCode != 0)
                    throw new InvalidOperationException($"Dump tool exited with code {proc.ExitCode}.");
                return $"Cache dump complete (exit 0). Now Regenerate collision/obstacles/areas from {DumpDirectory}.";
            });
        }

        private void ImportAreaSeeds()
        {
            RunBackground("Importing cache-derived areas into the graph", (log, ct) =>
            {
                var total = 0;
                foreach (var path in new[] { AreaSeedPath, NpcAreaSeedPath })
                {
                    if (!File.Exists(path)) continue;
                    var fragment = JsonSerializer.Deserialize<WebwalkGraphData>(File.ReadAllText(path), SeedReadOptions) ?? new WebwalkGraphData();
                    log($"Read {fragment.Areas.Count} areas from {Path.GetFileName(path)}");
                    if (!WebwalkGraph.TryImport(fragment.Nodes, fragment.Edges, fragment.Areas, replace: false, source: "cache",
                            out var error, out _, out _, out var areasUpserted))
                        throw new InvalidOperationException(error ?? $"Import rejected ({Path.GetFileName(path)}).");
                    total += areasUpserted;
                }
                return $"Imported {total} cache-derived areas into the webwalk graph.";
            });
        }

        private static readonly JsonSerializerOptions SeedReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private void SaveSettings()
        {
            Services.NavDiagnosticsStore.Save(new Services.NavDiagnosticsStore.NavDiagnosticsSettings(
                DumpDirectory,
                TreatWildernessDitchAsCrossable,
                DumpCommand,
                DumpArguments));
            Append("Saved diagnostics settings.");
            RaisePropertyChanged(nameof(DumpStatusText));
            InvalidateCommands();
        }

        /// <summary>
        /// Runs <paramref name="work"/> off the UI thread, streaming its log lines into the panel
        /// and refreshing the indicators when it finishes. Buttons are disabled while busy.
        /// </summary>
        private void RunBackground(string operation, Func<Action<string>, CancellationToken, string> work)
        {
            if (IsBusy) return;
            IsBusy = true;
            BusyOperation = operation + "…";
            Append($"▶ {operation} (dump: {DumpDirectory})");

            _busyCts?.Dispose();
            _busyCts = new CancellationTokenSource();
            var ct = _busyCts.Token;

            var dispatcher = Application.Current?.Dispatcher;
            void Log(string line)
            {
                if (dispatcher != null) dispatcher.BeginInvoke(() => Append(line));
                else Append(line);
            }

            Task.Run(() =>
            {
                string summary;
                try
                {
                    summary = work(Log, ct);
                }
                catch (OperationCanceledException)
                {
                    summary = "✖ Cancelled.";
                }
                catch (Exception ex)
                {
                    summary = "✖ Failed: " + ex.Message;
                }

                void Done()
                {
                    Append(summary);
                    BusyOperation = "";
                    IsBusy = false;
                    Refresh();
                }
                if (dispatcher != null) dispatcher.BeginInvoke(Done);
                else Done();
            });
        }

        // ── helpers ────────────────────────────────────────────────────────────────

        private bool DumpLooksValid()
        {
            try
            {
                var dir = DumpDirectory;
                return !string.IsNullOrWhiteSpace(dir)
                    && File.Exists(Path.Combine(dir, "location_configs.json"))
                    && Directory.Exists(Path.Combine(dir, "tiles"))
                    && Directory.Exists(Path.Combine(dir, "locations"));
            }
            catch { return false; }
        }

        private static int SafeCount(string dir, string pattern)
        {
            try { return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, pattern).Count() : 0; }
            catch { return 0; }
        }

        private static int CountSeedEdges(string path) => CountSeedArray(path, "Edges");
        private static int CountSeedAreas(string path) => CountSeedArray(path, "Areas");

        private static int CountSeedArray(string path, string property)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                return doc.RootElement.TryGetProperty(property, out var e) && e.ValueKind == JsonValueKind.Array
                    ? e.GetArrayLength() : 0;
            }
            catch { return 0; }
        }

        private void Append(string line)
        {
            var sb = new StringBuilder(Log);
            if (sb.Length > 0) sb.Append(Environment.NewLine);
            sb.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ").Append(line);
            // Keep the log bounded so a full collision run (thousands of progress lines) stays light.
            const int maxChars = 8000;
            var text = sb.ToString();
            if (text.Length > maxChars)
                text = "…" + text[^maxChars..];
            Log = text;
        }

        private static void InvalidateCommands()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null) dispatcher.BeginInvoke(CommandManager.InvalidateRequerySuggested);
            else CommandManager.InvalidateRequerySuggested();
        }
    }
}
