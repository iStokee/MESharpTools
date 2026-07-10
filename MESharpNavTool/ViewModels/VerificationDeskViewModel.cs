using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MESharp.API;
using MESharp.Commands;

namespace MESharp.ViewModels
{
    /// <summary>One verification task as shown in the service-desk list.</summary>
    public sealed class VerificationTaskRow : BaseViewModel
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Body { get; init; } = "";
        public string State { get; init; } = "";
        public string SignalType { get; init; } = "";
        public string Tier { get; init; } = "";
        public string Target { get; init; } = "";
        public int Confidence { get; init; }
        public int EvidenceCount { get; init; }
        public string ProposedDiff { get; init; } = "";
        public WorldAnchor? Anchor { get; init; }

        private string _closingNotes = "";
        /// <summary>Editable human notes on how the captured data is used / links to other tasks.</summary>
        public string ClosingNotes { get => _closingNotes; set => SetProperty(ref _closingNotes, value); }

        public bool IsCapturePack => string.Equals(SignalType, SignalTypes.CapturePack, StringComparison.OrdinalIgnoreCase);

        /// <summary>Dot colour bucket: open=grey, claimed=blue, staged=amber, resolved=green, closed=muted.</summary>
        public string StateDot => State switch
        {
            TaskStates.Resolved => "ok",
            TaskStates.Staged => "warn",
            TaskStates.Claimed => "busy",
            TaskStates.Closed => "off",
            _ => "open"
        };

        private string _distanceText = "";
        public string DistanceText { get => _distanceText; set => SetProperty(ref _distanceText, value); }

        public string AnchorText => Anchor == null
            ? "Anywhere (no fixed tile)"
            : $"{Anchor.X}, {Anchor.Y} · plane {Anchor.Z}{(string.IsNullOrWhiteSpace(Anchor.Context) ? "" : $" — {Anchor.Context}")}";

        public bool IsTile => string.Equals(SignalType, SignalTypes.Tile, StringComparison.OrdinalIgnoreCase);
        public bool IsStaged => string.Equals(State, TaskStates.Staged, StringComparison.OrdinalIgnoreCase);
        public bool IsAutoWrite => string.Equals(Tier, nameof(ResolverTier.AutoWrite), StringComparison.OrdinalIgnoreCase);
        public bool IsFlag => string.Equals(Tier, nameof(ResolverTier.Flag), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "Verify" service-desk pane for the navigation hub (KOS 2.0). Surfaces verification tasks for
    /// unconfirmed curated/synthetic nav data and walks the user/agent through confirming them — a
    /// finite, ordered queue with one-click confirm (tier-1 auto-write), diff approval (tier-2) and
    /// flagging (tier-3). Reads/writes go through the in-process <see cref="VerificationLedger"/>
    /// repository; passive auto-resolve is driven elsewhere (Traversal) when capture is enabled.
    /// </summary>
    public sealed class VerificationDeskViewModel : BaseViewModel, IActivatableViewModel, IDisposable
    {
        private static readonly string[] StateFilters = { "all", "open", "claimed", "staged", "resolved" };
        private static readonly string[] SignalFilters =
            { "all", SignalTypes.Tile, SignalTypes.OptionString, SignalTypes.InterfaceId, SignalTypes.NameId, SignalTypes.Varbit };

        private string _stateFilter = "open";
        private string _signalFilter = "all";
        private bool _nearMeOnly;
        private string _status = "";
        private VerificationTaskRow? _selected;
        private int _openCount, _stagedCount, _resolvedCount, _avgConfidence;
        private readonly CancellationTokenSource _shutdownCts = new();
        private bool _disposed;

        public ObservableCollection<VerificationTaskRow> Tasks { get; } = new();
        public IReadOnlyList<string> StateFilterOptions => StateFilters;
        public IReadOnlyList<string> SignalFilterOptions => SignalFilters;

        public VerificationDeskViewModel()
        {
            RefreshCommand = new RelayCommand(_ => Refresh());
            SyncCommand = new RelayCommand(_ => Sync());
            ConfirmCommand = new RelayCommand(t => Confirm(t as VerificationTaskRow), t => t is VerificationTaskRow);
            CapturePackCommand = new RelayCommand(t => CapturePackFor(t as VerificationTaskRow), t => t is VerificationTaskRow);
            SaveNotesCommand = new RelayCommand(t => SaveNotes(t as VerificationTaskRow), t => t is VerificationTaskRow);
            ResolveCommand = new RelayCommand(t => Resolve(t as VerificationTaskRow), t => t is VerificationTaskRow);
            ApproveCommand = new RelayCommand(t => Approve(t as VerificationTaskRow), t => t is VerificationTaskRow r && r.IsStaged);
            CloseCommand = new RelayCommand(t => CloseTask(t as VerificationTaskRow), t => t is VerificationTaskRow);
            FlagCommand = new RelayCommand(t => Flag(t as VerificationTaskRow), t => t is VerificationTaskRow);
            ShowOnMapCommand = new RelayCommand(t => ShowOnMap(t as VerificationTaskRow), t => t is VerificationTaskRow r && r.Anchor != null);
            HelpCommand = new RelayCommand(t => ShowHelp((t as VerificationTaskRow)?.SignalType));
        }

        public string StateFilter { get => _stateFilter; set { if (SetProperty(ref _stateFilter, value)) Refresh(); } }
        public string SignalFilter { get => _signalFilter; set { if (SetProperty(ref _signalFilter, value)) Refresh(); } }
        public bool NearMeOnly { get => _nearMeOnly; set { if (SetProperty(ref _nearMeOnly, value)) Refresh(); } }

        /// <summary>Two-way bound to the passive-capture switch (auto-resolves T1 tasks during normal play).</summary>
        public bool CaptureEnabled
        {
            get => VerificationCapture.CaptureEnabled;
            set { VerificationCapture.CaptureEnabled = value; RaisePropertyChanged(nameof(CaptureEnabled)); }
        }

        public string Status { get => _status; private set => SetProperty(ref _status, value); }
        public int OpenCount { get => _openCount; private set => SetProperty(ref _openCount, value); }
        public int StagedCount { get => _stagedCount; private set => SetProperty(ref _stagedCount, value); }
        public int ResolvedCount { get => _resolvedCount; private set => SetProperty(ref _resolvedCount, value); }
        public int AvgConfidence { get => _avgConfidence; private set => SetProperty(ref _avgConfidence, value); }

        public VerificationTaskRow? Selected
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

        public ICommand RefreshCommand { get; }
        public ICommand SyncCommand { get; }
        public ICommand ConfirmCommand { get; }
        public ICommand CapturePackCommand { get; }
        public ICommand SaveNotesCommand { get; }
        public ICommand ResolveCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand FlagCommand { get; }
        public ICommand ShowOnMapCommand { get; }
        public ICommand HelpCommand { get; }

        public void OnActivated()
        {
            if (!_disposed) Refresh();
        }
        public void OnDeactivated() { }

        private void Refresh()
        {
            if (_disposed) return;
            try
            {
                var query = new LedgerQuery
                {
                    TaskState = _stateFilter == "all" ? null : _stateFilter,
                    SignalType = _signalFilter == "all" ? null : _signalFilter,
                    MaxCount = 500
                };

                WorldPoint player = default;
                if (_nearMeOnly)
                {
                    try { player = Traversal.GetCurrentPosition(); } catch { player = default; }
                    if (player.X > 0 || player.Y > 0)
                    {
                        query.Near = new WorldAnchor { X = player.X, Y = player.Y, Z = player.Z };
                        query.NearRadius = int.MaxValue;
                    }
                }

                var tasks = VerificationLedger.Default.ListTasks(query);
                Tasks.Clear();
                foreach (var t in tasks)
                {
                    var row = new VerificationTaskRow
                    {
                        Id = t.Id,
                        Title = t.Title,
                        Body = t.Body,
                        State = t.TaskState,
                        SignalType = t.SignalType,
                        Tier = VerificationResolverRegistry.TierOf(t.Resolver).ToString(),
                        Target = t.Target,
                        Confidence = t.Confidence,
                        EvidenceCount = t.Evidence?.Count ?? 0,
                        ProposedDiff = t.ProposedDiffJson,
                        Anchor = t.Anchor,
                        ClosingNotes = t.ClosingNotes
                    };
                    if ((player.X > 0 || player.Y > 0) && t.Anchor != null && t.Anchor.Z == player.Z)
                    {
                        var d = Math.Max(Math.Abs(t.Anchor.X - player.X), Math.Abs(t.Anchor.Y - player.Y));
                        row.DistanceText = $"{d} tiles";
                    }
                    Tasks.Add(row);
                }

                // Counts span the whole ledger (not just the filtered view) so the header reflects reality.
                var all = VerificationLedger.Default.ListTasks(new LedgerQuery { MaxCount = 1000, IncludeClosed = false });
                OpenCount = all.Count(t => t.TaskState == TaskStates.Open);
                StagedCount = all.Count(t => t.TaskState == TaskStates.Staged);
                ResolvedCount = all.Count(t => t.TaskState == TaskStates.Resolved);
                AvgConfidence = all.Count == 0 ? 0 : (int)Math.Round(all.Average(t => t.Confidence));
            }
            catch (Exception ex)
            {
                Status = "Refresh failed: " + ex.Message;
            }
        }

        private void Sync()
        {
            RunBackground("Syncing nav verification tasks", () =>
            {
                var created = NavTaskProducer.SyncFromGraph();
                return $"Created {created} new task{(created == 1 ? "" : "s")} from the nav graph.";
            });
        }

        private void Confirm(VerificationTaskRow? row)
        {
            if (row == null) return;
            RunBackground($"Confirming {row.Title}", () =>
            {
                var result = VerificationCapture.CaptureForTask(row.Id);
                return result.Success
                    ? $"Confirmed: {result.Message}"
                    : $"Captured evidence — {result.Message}";
            });
        }

        private void CapturePackFor(VerificationTaskRow? row)
        {
            if (row == null) return;
            RunBackground($"Capturing pack for {row.Title}", () =>
            {
                var result = VerificationCapture.AttachPack(row.Id);
                return result.Message;
            });
        }

        private void SaveNotes(VerificationTaskRow? row)
        {
            if (row == null) return;
            // Capture the text before the background refresh rebuilds the row.
            var id = row.Id;
            var notes = row.ClosingNotes;
            RunBackground("Saving closing notes", () =>
            {
                var ok = VerificationLedger.Default.SetClosingNotes(id, notes);
                return ok ? "Closing notes saved." : "Save failed.";
            });
        }

        private void Resolve(VerificationTaskRow? row)
        {
            if (row == null) return;
            RunBackground($"Resolving {row.Title}", () =>
            {
                var result = VerificationLedger.Default.Resolve(row.Id);
                if (result.RequiresApproval) return "Needs a staged diff first (tier-2).";
                return result.Success ? $"Resolved: {result.Message}" : $"Resolve failed: {result.Message}";
            });
        }

        private void Approve(VerificationTaskRow? row)
        {
            if (row == null) return;
            // Approving a staged tier-2 task = run the resolver to apply the staged diff.
            Resolve(row);
        }

        private void Flag(VerificationTaskRow? row)
        {
            if (row == null) return;
            RunBackground($"Flagging {row.Title}", () =>
            {
                // Tier-3: mark resolved without dispatching a resolver — the code fix is made out of band.
                var result = VerificationLedger.Default.Resolve(row.Id, applyResolver: false);
                return result.Success ? "Flagged for code (no auto-write)." : result.Message;
            });
        }

        private void CloseTask(VerificationTaskRow? row)
        {
            if (row == null) return;
            RunBackground($"Closing {row.Title}", () =>
            {
                var ok = VerificationLedger.Default.Close(row.Id);
                return ok ? "Closed; evidence purged." : "Close failed.";
            });
        }

        private void ShowOnMap(VerificationTaskRow? row)
        {
            if (row?.Anchor == null) return;
            try { Services.CoverageMapServer.RequestFocus(new WorldPoint(row.Anchor.X, row.Anchor.Y, row.Anchor.Z)); }
            catch { /* map may not be running */ }
        }

        private void ShowHelp(string? signalType)
        {
            var (title, body) = HelpFor(signalType);
            try
            {
                var content = new Views.HelpContent
                {
                    Title = title,
                    Subtitle = "How to confirm this verification task",
                    Sections =
                    {
                        new Views.HelpSection
                        {
                            Title = "Steps",
                            Topics = { new Views.HelpTopic { Title = title, Body = body } }
                        }
                    }
                };
                new Views.SectionHelpWindow(content) { Owner = Application.Current?.MainWindow }.ShowDialog();
            }
            catch { Status = body; }
        }

        private static (string title, string body) HelpFor(string? signalType) => signalType switch
        {
            SignalTypes.Tile => ("Confirm a tile",
                "Walk your character to the exact tile this node should sit on, then click Confirm. " +
                "The ledger captures your live tile and writes it back to the graph node. " +
                "Tip: enable Capture and just walk past seeded nodes — tile tasks auto-resolve on arrival."),
            SignalTypes.OptionString => ("Confirm an interaction option",
                "Stand by the object/shortcut, right-click it and use the real menu option once. " +
                "With the DoAction recorder on, click Confirm to capture the option text + action index " +
                "and write them onto the graph edge."),
            SignalTypes.NameId => ("Confirm an object/NPC name↔id",
                "Interact with the exact object/NPC once (DoAction recorder on), then Confirm to capture " +
                "its id + name and bind them to the edge."),
            SignalTypes.InterfaceId => ("Confirm an interface component id",
                "Open the interface and run ui.scan_interface (or the scan tool) to find the real component id, " +
                "then Stage the {key, component} change. A reviewer approves it before it writes to " +
                "ME.InterfaceOverrides.json (tier-2, because a wrong id silently breaks clicks)."),
            SignalTypes.Varbit => ("Confirm a varbit/varp mapping",
                "Trigger the in-game change and watch the varbit/varp delta (quest.watch / a varbit probe). " +
                "Record the confirmed id + value, then resolve."),
            _ => ("Verifying nav data",
                "Each ticket is a curated/synthetic value that needs an in-game check. Confirm tier-1 tickets " +
                "with one click (auto-writes back); Stage+approve tier-2; Flag tier-3 for a code change.")
        };

        private void RunBackground(string operation, Func<string> work)
        {
            if (_disposed) return;
            Status = operation + "…";
            var dispatcher = Application.Current?.Dispatcher;
            var ct = _shutdownCts.Token;
            Task.Run(() =>
            {
                string summary;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    summary = work();
                    ct.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { summary = "Failed: " + ex.Message; }

                void Done()
                {
                    if (_disposed) return;
                    Status = summary;
                    Refresh();
                    CommandManager.InvalidateRequerySuggested();
                }
                if (dispatcher != null) dispatcher.BeginInvoke(Done);
                else Done();
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _shutdownCts.Cancel(); } catch { }
            // Background ledger operations may still be observing this token. Let the
            // view-model become collectible with its source once they have unwound.
        }
    }
}
