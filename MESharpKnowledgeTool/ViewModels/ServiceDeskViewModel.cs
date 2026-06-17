using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MESharp.API;
using MESharp.Commands;
using MESharp.ViewModels;

namespace MESharp.ViewModels
{
    /// <summary>One verification case as shown in the service-desk queue.</summary>
    public sealed class ServiceDeskTaskRow : BaseViewModel
    {
        public string Id { get; init; } = "";
        public int CaseNumber { get; init; }
        public string CaseLabel => CaseNumber > 0 ? $"KOS-{CaseNumber:D4}" : "KOS-—";
        public string Title { get; init; } = "";
        public string Body { get; init; } = "";
        public string Category { get; init; } = "";
        public string State { get; init; } = "";
        public string SignalType { get; init; } = "";
        public string Tier { get; init; } = "";
        public string Target { get; init; } = "";
        public int Confidence { get; init; }
        public int EvidenceCount { get; init; }
        /// <summary>Human-readable summary of each captured evidence entry (so the desk shows what was recorded).</summary>
        public string EvidenceText { get; init; } = "";
        public string ProposedDiff { get; init; } = "";
        public WorldAnchor? Anchor { get; init; }

        private string _closingNotes = "";
        public string ClosingNotes { get => _closingNotes; set => SetProperty(ref _closingNotes, value); }

        public string StateDot => State switch
        {
            TaskStates.Resolved => "ok",
            TaskStates.Staged => "warn",
            TaskStates.Claimed => "busy",
            TaskStates.Closed => "off",
            _ => "open"
        };

        public string AnchorText => Anchor == null
            ? "Anywhere (no fixed tile)"
            : $"{Anchor.X}, {Anchor.Y} · plane {Anchor.Z}{(string.IsNullOrWhiteSpace(Anchor.Context) ? "" : $" — {Anchor.Context}")}";

        public bool IsStaged => string.Equals(State, TaskStates.Staged, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// KOS 2.0 "service desk": a cross-surface ticketing UI over the verification ledger. Cases carry
    /// KOS-#### numbers, lifecycle states, captured evidence, and durable closing notes. Standalone
    /// tool (its own window) — the nav hub's Verify tab is a lighter nav-filtered view onto the same
    /// ledger. Reads/writes the in-process <see cref="VerificationLedger"/> repository.
    /// </summary>
    public sealed class ServiceDeskViewModel : BaseViewModel, IActivatableViewModel
    {
        // Saved "queues" (left rail), Jira/ITSM style. The active view drives which cases the list shows.
        public const string ViewOpen = "open";
        public const string ViewNeedsAction = "needs";   // open + claimed
        public const string ViewStaged = "staged";       // awaiting diff approval
        public const string ViewResolved = "resolved";
        public const string ViewClosed = "closed";

        private static readonly string[] SignalFilters =
            { "all", SignalTypes.Tile, SignalTypes.OptionString, SignalTypes.InterfaceId, SignalTypes.NameId, SignalTypes.Varbit, SignalTypes.CapturePack };
        private static readonly string[] CategoryFilters = { "all", "nav", "quest", "slayer", "test" };

        private string _activeView = ViewOpen;
        private string _signalFilter = "all";
        private string _categoryFilter = "all";
        private string _searchText = "";
        private string _status = "Ready.";
        private ServiceDeskTaskRow? _selected;
        private int _openCount, _claimedCount, _stagedCount, _resolvedCount, _closedCount, _avgConfidence;

        // Live system-state strip so the levers' effects are visible (logged in? clicks recording? scan?).
        private DispatcherTimer? _liveTimer;
        private string _sessionState = "No session";
        private string _sessionDot = "off";          // ok | off
        private string _clickState = "Clicks: off";
        private string _clickDot = "off";            // ok | off
        private string _scanState = "Scan: idle";
        private string _scanDot = "off";             // busy | off
        private string _passiveState = "Passive: off";
        private string _passiveDot = "off";          // ok | off

        public ObservableCollection<ServiceDeskTaskRow> Tasks { get; } = new();
        public IReadOnlyList<string> SignalFilterOptions => SignalFilters;
        public IReadOnlyList<string> CategoryFilterOptions => CategoryFilters;

        public ServiceDeskViewModel()
        {
            NavigateCommand = new RelayCommand(p => { if (p is string page) ActivePage = page; });
            SelectViewCommand = new RelayCommand(p => { if (p is string v) ActiveView = v; });
            RefreshCommand = new RelayCommand(_ => Refresh());
            SyncCommand = new RelayCommand(_ => Run("Syncing nav verification tasks", () =>
                $"Created {NavTaskProducer.SyncFromGraph()} new case(s)."));

            ScanStartCommand = new RelayCommand(_ => { CapturePackRecorder.Start(); Status = "Scan recording… walk the area (e.g. the wall E↔W), then Stop."; RaiseScanState(); });
            ScanStopCommand = new RelayCommand(_ => { CapturePackRecorder.Stop(); Status = $"Scan stopped: {CapturePackRecorder.SampleCount} samples → {CapturePackRecorder.SessionPath()}"; RaiseScanState(); });
            ScanSampleCommand = new RelayCommand(_ => { var n = CapturePackRecorder.Sample(); Status = $"Captured sample {n}."; RaiseScanState(); });
            ScanSummaryCommand = new RelayCommand(_ =>
            {
                var s = CapturePackRecorder.Summarize();
                var ext = s.PlayerBounds is { Length: 4 } b ? $" · area X{b[0]}–{b[2]} Y{b[1]}–{b[3]}" : "";
                Status = $"Scan summary: {s.SampleCount} samples, {s.DistinctObjects} distinct objects, {s.DistinctNpcs} NPCs{ext}.";
            });

            ClaimCommand = new RelayCommand(t => WithRow(t, r => Run($"Claiming {r.CaseLabel}", () =>
                VerificationLedger.Default.Claim(r.Id, "me") ? "Claimed." : "Claim failed.")));
            ConfirmCommand = new RelayCommand(t => WithRow(t, r => Run($"Confirming {r.CaseLabel}", () =>
            {
                var res = VerificationCapture.CaptureForTask(r.Id);
                return res.Success ? $"Confirmed: {res.Message}" : res.Message;
            })));
            CapturePackCommand = new RelayCommand(t => WithRow(t, r => Run($"Capturing pack for {r.CaseLabel}", () =>
                VerificationCapture.AttachPack(r.Id).Message)));
            ResolveCommand = new RelayCommand(t => WithRow(t, r => Run($"Resolving {r.CaseLabel}", () =>
            {
                var res = VerificationLedger.Default.Resolve(r.Id);
                if (res.RequiresApproval) return "Needs a staged diff first (tier-2).";
                return res.Success ? $"Resolved: {res.Message}" : $"Resolve failed: {res.Message}";
            })));
            ApproveCommand = new RelayCommand(t => WithRow(t, r => Run($"Approving {r.CaseLabel}", () =>
            {
                var res = VerificationLedger.Default.Resolve(r.Id);
                return res.Success ? "Approved + applied." : res.Message;
            })), t => t is ServiceDeskTaskRow r && r.IsStaged);
            FlagCommand = new RelayCommand(t => WithRow(t, r => Run($"Flagging {r.CaseLabel}", () =>
                VerificationLedger.Default.Resolve(r.Id, applyResolver: false).Success ? "Flagged for code (no auto-write)." : "Flag failed.")));
            CloseCommand = new RelayCommand(t => WithRow(t, r => Run($"Closing {r.CaseLabel}", () =>
                VerificationLedger.Default.Close(r.Id) ? "Closed; evidence purged." : "Close failed.")));
            SaveNotesCommand = new RelayCommand(t => WithRow(t, r =>
            {
                var id = r.Id; var notes = r.ClosingNotes;
                Run("Saving closing notes", () => VerificationLedger.Default.SetClosingNotes(id, notes) ? "Closing notes saved." : "Save failed.");
            }));
        }

        // ── Top-level pages (left nav) ───────────────────────────────────────────
        public const string PageDashboard = "dashboard";
        public const string PageQueue = "queue";
        public const string PageCapture = "capture";
        private string _activePage = PageQueue;

        /// <summary>Which page the left nav is showing (see Page* consts).</summary>
        public string ActivePage
        {
            get => _activePage;
            set
            {
                if (!SetProperty(ref _activePage, value)) return;
                foreach (var n in new[] { nameof(IsPageDashboard), nameof(IsPageQueue), nameof(IsPageCapture) })
                    RaisePropertyChanged(n);
                if (value == PageDashboard || value == PageQueue) Refresh();
            }
        }
        public bool IsPageDashboard => _activePage == PageDashboard;
        public bool IsPageQueue => _activePage == PageQueue;
        public bool IsPageCapture => _activePage == PageCapture;

        /// <summary>Active left-rail queue (see View* consts). Drives which cases the list shows.</summary>
        public string ActiveView
        {
            get => _activeView;
            set
            {
                if (!SetProperty(ref _activeView, value)) return;
                foreach (var n in new[] { nameof(IsViewOpen), nameof(IsViewNeedsAction), nameof(IsViewStaged), nameof(IsViewResolved), nameof(IsViewClosed), nameof(ViewTitle) })
                    RaisePropertyChanged(n);
                Refresh();
            }
        }
        public bool IsViewOpen => _activeView == ViewOpen;
        public bool IsViewNeedsAction => _activeView == ViewNeedsAction;
        public bool IsViewStaged => _activeView == ViewStaged;
        public bool IsViewResolved => _activeView == ViewResolved;
        public bool IsViewClosed => _activeView == ViewClosed;
        public string ViewTitle => _activeView switch
        {
            ViewNeedsAction => "Needs action",
            ViewStaged => "Staged for review",
            ViewResolved => "Resolved",
            ViewClosed => "Closed",
            _ => "Open"
        };

        public string SignalFilter { get => _signalFilter; set { if (SetProperty(ref _signalFilter, value)) Refresh(); } }
        public string CategoryFilter { get => _categoryFilter; set { if (SetProperty(ref _categoryFilter, value)) Refresh(); } }
        public string SearchText { get => _searchText; set { if (SetProperty(ref _searchText, value)) Refresh(); } }

        public bool CaptureEnabled
        {
            get => VerificationCapture.CaptureEnabled;
            set { VerificationCapture.CaptureEnabled = value; RaisePropertyChanged(nameof(CaptureEnabled)); UpdateLiveStatus(); }
        }

        /// <summary>Attach a screenshot to capture packs (visual attestation). Image saved to disk; path in the pack.</summary>
        public bool ScreenshotsEnabled
        {
            get => ScreenshotService.Enabled;
            set { ScreenshotService.Enabled = value; RaisePropertyChanged(nameof(ScreenshotsEnabled)); }
        }

        /// <summary>Black out the chatbox before writing a screenshot (privacy). Falls back to a default region.</summary>
        public bool BlackoutChat
        {
            get => ScreenshotService.BlackoutChat;
            set { ScreenshotService.BlackoutChat = value; RaisePropertyChanged(nameof(BlackoutChat)); }
        }

        private bool _recordClicks;
        /// <summary>Enables ME's DoAction recorder + pump so capture packs include your click sequence
        /// (approach → cross → run). Without this, packs record the world snapshot but Clicks is empty.</summary>
        public bool RecordClicks
        {
            get => _recordClicks;
            set
            {
                if (!SetProperty(ref _recordClicks, value)) return;
                try
                {
                    if (value)
                    {
                        DoActionDebugSignals.Configure(enabled: true);
                        DoActionDebugSignals.StartNativePump();
                        var bridge = DoActionDebugSignals.VerifyNativeBridge();
                        Status = bridge.Available
                            ? "Click recording ON — clicks will now be captured in packs."
                            : $"Click recording: native bridge unavailable ({bridge.Error}). Reinject/rebuild ME?";
                    }
                    else
                    {
                        DoActionDebugSignals.StopNativePump();
                        Status = "Click recording OFF.";
                    }
                    UpdateLiveStatus();
                }
                catch (Exception ex) { Status = "Click recording toggle failed: " + ex.Message; }
            }
        }

        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public int OpenCount { get => _openCount; private set => SetProperty(ref _openCount, value); }
        public int ClaimedCount { get => _claimedCount; private set => SetProperty(ref _claimedCount, value); }
        public int StagedCount { get => _stagedCount; private set => SetProperty(ref _stagedCount, value); }
        public int ResolvedCount { get => _resolvedCount; private set => SetProperty(ref _resolvedCount, value); }
        public int ClosedCount { get => _closedCount; private set => SetProperty(ref _closedCount, value); }
        public int NeedsActionCount => _openCount + _claimedCount;
        public int AvgConfidence { get => _avgConfidence; private set => SetProperty(ref _avgConfidence, value); }

        public ServiceDeskTaskRow? Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    RaisePropertyChanged(nameof(HasSelection));
                    RaisePropertyChanged(nameof(HasNoSelection));
                }
            }
        }
        public bool HasSelection => _selected != null;
        public bool HasNoSelection => _selected == null;

        public bool IsScanning => CapturePackRecorder.IsRecording;
        public int ScanSampleCount => CapturePackRecorder.SampleCount;
        private void RaiseScanState() { RaisePropertyChanged(nameof(IsScanning)); RaisePropertyChanged(nameof(ScanSampleCount)); UpdateLiveStatus(); }

        public string SessionState { get => _sessionState; private set => SetProperty(ref _sessionState, value); }
        public string SessionDot { get => _sessionDot; private set => SetProperty(ref _sessionDot, value); }
        public string ClickState { get => _clickState; private set => SetProperty(ref _clickState, value); }
        public string ClickDot { get => _clickDot; private set => SetProperty(ref _clickDot, value); }
        public string ScanState { get => _scanState; private set => SetProperty(ref _scanState, value); }
        public string ScanDot { get => _scanDot; private set => SetProperty(ref _scanDot, value); }
        public string PassiveState { get => _passiveState; private set => SetProperty(ref _passiveState, value); }
        public string PassiveDot { get => _passiveDot; private set => SetProperty(ref _passiveDot, value); }

        public ICommand NavigateCommand { get; }
        public ICommand SelectViewCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SyncCommand { get; }
        public ICommand ScanStartCommand { get; }
        public ICommand ScanStopCommand { get; }
        public ICommand ScanSampleCommand { get; }
        public ICommand ScanSummaryCommand { get; }
        public ICommand ClaimCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CapturePackCommand { get; }
        public ICommand ResolveCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand FlagCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand SaveNotesCommand { get; }

        public void OnActivated()
        {
            // Auto-generate cases on open so the queue is just there — no manual "Sync" needed.
            // Idempotent: existing cases (open OR closed) are never recreated, so closed work stays closed.
            Run("Syncing cases from the nav graph", () =>
            {
                var created = NavTaskProducer.SyncFromGraph();
                return created > 0 ? $"Added {created} new case(s)." : "Up to date.";
            });
            if (_liveTimer == null)
            {
                _liveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
                _liveTimer.Tick += (_, _) => UpdateLiveStatus();
            }
            UpdateLiveStatus();
            _liveTimer.Start();
        }

        public void OnDeactivated()
        {
            _liveTimer?.Stop();
            // Release the DoAction pump refcount + stop any running scan so this tool's ALC can unload.
            try { if (_recordClicks) DoActionDebugSignals.StopNativePump(); } catch { }
            try { if (CapturePackRecorder.IsRecording) CapturePackRecorder.Stop(); } catch { }
        }

        /// <summary>Refresh the live status strip so the user can SEE whether each lever is actually on.</summary>
        private void UpdateLiveStatus()
        {
            try
            {
                var p = LocalPlayer.GetTileWorldPoint();
                if (p.X > 0 || p.Y > 0) { SessionState = $"Logged in · {p.X},{p.Y} pl{p.Z}"; SessionDot = "ok"; }
                else { SessionState = "No session"; SessionDot = "off"; }
            }
            catch { SessionState = "No session"; SessionDot = "off"; }

            var clicks = false;
            try { clicks = DoActionDebugSignals.NativePumpActive; } catch { }
            ClickState = clicks ? "Clicks: recording" : "Clicks: off";
            ClickDot = clicks ? "ok" : "off";

            ScanState = CapturePackRecorder.IsRecording ? $"Scan: REC ({CapturePackRecorder.SampleCount})" : "Scan: idle";
            ScanDot = CapturePackRecorder.IsRecording ? "busy" : "off";

            PassiveState = VerificationCapture.CaptureEnabled ? "Passive: on" : "Passive: off";
            PassiveDot = VerificationCapture.CaptureEnabled ? "ok" : "off";
        }

        private void Refresh()
        {
            try
            {
                // The Closed queue is the only one that needs archived/closed rows; everything else
                // works the open set. State is filtered per-view below (some views span >1 state).
                var query = new LedgerQuery
                {
                    SignalType = _signalFilter == "all" ? null : _signalFilter,
                    Category = _categoryFilter == "all" ? null : _categoryFilter,
                    IncludeClosed = _activeView == ViewClosed,
                    MaxCount = 1000
                };

                bool InView(string state) => _activeView switch
                {
                    ViewNeedsAction => state == TaskStates.Open || state == TaskStates.Claimed,
                    ViewStaged => state == TaskStates.Staged,
                    ViewResolved => state == TaskStates.Resolved,
                    ViewClosed => state == TaskStates.Closed,
                    _ => state == TaskStates.Open,
                };

                var search = (_searchText ?? "").Trim();
                var tasks = VerificationLedger.Default.ListTasks(query)
                    .Where(t => InView(t.TaskState))
                    .Where(t => search.Length == 0
                                || (t.Title?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                                || (t.Target?.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                                || $"KOS-{t.CaseNumber:D4}".IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(t => t.CaseNumber);

                var selectedId = _selected?.Id;
                Tasks.Clear();
                foreach (var t in tasks)
                {
                    Tasks.Add(new ServiceDeskTaskRow
                    {
                        Id = t.Id,
                        CaseNumber = t.CaseNumber,
                        Title = t.Title,
                        Body = t.Body,
                        Category = t.Category,
                        State = t.TaskState,
                        SignalType = t.SignalType,
                        Tier = VerificationResolverRegistry.TierOf(t.Resolver).ToString(),
                        Target = t.Target,
                        Confidence = t.Confidence,
                        EvidenceCount = t.Evidence?.Count ?? 0,
                        EvidenceText = SummarizeEvidence(t),
                        ProposedDiff = t.ProposedDiffJson,
                        Anchor = t.Anchor,
                        ClosingNotes = t.ClosingNotes
                    });
                }
                Selected = Tasks.FirstOrDefault(r => r.Id == selectedId) ?? Tasks.FirstOrDefault();

                var all = VerificationLedger.Default.ListTasks(new LedgerQuery { IncludeClosed = true, MaxCount = 1000 });
                OpenCount = all.Count(t => t.TaskState == TaskStates.Open);
                ClaimedCount = all.Count(t => t.TaskState == TaskStates.Claimed);
                StagedCount = all.Count(t => t.TaskState == TaskStates.Staged);
                ResolvedCount = all.Count(t => t.TaskState == TaskStates.Resolved);
                ClosedCount = all.Count(t => t.TaskState == TaskStates.Closed);
                RaisePropertyChanged(nameof(NeedsActionCount));
                AvgConfidence = all.Count == 0 ? 0 : (int)Math.Round(all.Average(t => t.Confidence));
            }
            catch (Exception ex)
            {
                Status = "Refresh failed: " + ex.Message;
            }
        }

        private static void WithRow(object? param, Action<ServiceDeskTaskRow> action)
        {
            if (param is ServiceDeskTaskRow row) action(row);
        }

        /// <summary>Build a readable, per-entry summary of a task's evidence so the desk shows what was
        /// actually captured (objects, wall objects, click count, region) rather than just a number.</summary>
        private static string SummarizeEvidence(KnowledgeItem t)
        {
            var ev = t.Evidence ?? new List<EvidenceEntry>();
            if (ev.Count == 0) return "No evidence captured yet.";

            var sb = new StringBuilder();
            for (var i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                sb.Append($"#{i + 1} [{e.SignalType}] {e.CapturedUtc.ToLocalTime():HH:mm:ss}  ");
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(e.PayloadJson) ? "{}" : e.PayloadJson);
                    var r = doc.RootElement;
                    if (r.TryGetProperty("PlayerX", out var pxEl))
                    {
                        int px = pxEl.GetInt32();
                        int py = r.TryGetProperty("PlayerY", out var v1) ? v1.GetInt32() : 0;
                        int pz = r.TryGetProperty("PlayerZ", out var v2) ? v2.GetInt32() : 0;
                        int objCount = r.TryGetProperty("Objects", out var oa) && oa.ValueKind == JsonValueKind.Array ? oa.GetArrayLength() : 0;
                        int clickCount = r.TryGetProperty("Clicks", out var ca) && ca.ValueKind == JsonValueKind.Array ? ca.GetArrayLength() : 0;
                        int wall = 0;
                        var wallIds = new HashSet<int>();
                        if (oa.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var o in oa.EnumerateArray())
                            {
                                var nm = o.TryGetProperty("Name", out var nmp) ? nmp.GetString() : null;
                                if (nm != null && nm.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    wall++;
                                    if (o.TryGetProperty("Id", out var idp) && idp.ValueKind == JsonValueKind.Number) wallIds.Add(idp.GetInt32());
                                }
                            }
                        }
                        string comp = "";
                        if (r.TryGetProperty("Collision", out var col))
                        {
                            string cs = col.TryGetProperty("CrossingSouthComponent", out var csp) && csp.ValueKind == JsonValueKind.Number ? csp.GetInt32().ToString() : "?";
                            string cn = col.TryGetProperty("CrossingNorthComponent", out var cnp) && cnp.ValueKind == JsonValueKind.Number ? cnp.GetInt32().ToString() : "?";
                            if (cs != "?" || cn != "?") comp = $", comp S{cs}/N{cn}";
                        }
                        var idsText = wallIds.Count > 0 ? $" ({string.Join(",", wallIds.OrderBy(x => x))})" : "";
                        sb.Append($"@{px},{py},{pz}: {objCount} objs, {wall} wall{idsText}, {clickCount} clicks{comp}");
                        var shot = r.TryGetProperty("ScreenshotPath", out var sp) ? sp.GetString() : null;
                        if (!string.IsNullOrEmpty(shot)) sb.Append($"  📷 {System.IO.Path.GetFileName(shot)}");
                        if (clickCount == 0)
                            sb.Append("  ⚠ no clicks — enable \"Record clicks\" then cross");
                    }
                    else
                    {
                        var raw = e.PayloadJson ?? "";
                        sb.Append(raw.Length > 140 ? raw[..140] : raw);
                    }
                }
                catch { sb.Append(e.PayloadJson); }
                sb.AppendLine();
            }
            return sb.ToString().TrimEnd();
        }

        private void Run(string operation, Func<string> work)
        {
            Status = operation + "…";
            var dispatcher = Application.Current?.Dispatcher;
            Task.Run(() =>
            {
                string summary;
                try { summary = work(); }
                catch (Exception ex) { summary = "Failed: " + ex.Message; }
                void Done()
                {
                    Status = summary;
                    Refresh();
                    CommandManager.InvalidateRequerySuggested();
                }
                if (dispatcher != null) dispatcher.BeginInvoke(Done);
                else Done();
            });
        }
    }
}
