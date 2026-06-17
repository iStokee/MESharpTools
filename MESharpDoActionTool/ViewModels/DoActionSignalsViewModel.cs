using MESharp.API;
using MESharp.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MESharp.ViewModels
{
    public sealed class DoActionSignalRow
    {
        public DateTime TimestampUtc { get; init; }
        public string Surface { get; init; } = string.Empty;
        public string Operation { get; init; } = string.Empty;
        public bool Result { get; init; }
        public string Snippet { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;
        /// <summary>Coarse signal type (Object, NPC, Walk, Interface, ...) — drives row color.</summary>
        public string Kind { get; init; } = string.Empty;
        /// <summary>True for signals captured from the native DoAction hook (real clicks), false for tool-dispatched ones.</summary>
        public bool IsPlayerClick { get; init; }
        public string TileText { get; init; } = string.Empty;
    }

    /// <summary>
    /// One card in the "Click History" panel — a parsed real in-game click showing the exact
    /// native action opcode + route offset (the values needed to calibrate an interaction node),
    /// plus a ready-to-paste API call (<see cref="ApiSnippet"/>) reproducing the interaction.
    /// </summary>
    public sealed record ClickHistoryRow
    {
        public DateTime TimestampUtc { get; init; }
        /// <summary>Short type tag shown as a chip: [O]bject, [N]PC, [G]round, [T]ile, [I]nterface.</summary>
        public string Tag { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        /// <summary>Coarse kind (Object/NPC/Walk/GroundItem/Interface) — drives the card color.</summary>
        public string Kind { get; init; } = string.Empty;
        /// <summary>Ready-to-paste C# API call that reproduces this interaction (the "Copy API" payload).</summary>
        public string ApiSnippet { get; init; } = string.Empty;

        // --- Parity fields mirroring the Signals grid so the list shows the same information ---
        /// <summary>Originating surface ("Native" for real player clicks, else the dispatching tool/API).</summary>
        public string Surface { get; init; } = string.Empty;
        public string Operation { get; init; } = string.Empty;
        /// <summary>True for signals captured from the native DoAction hook (real clicks).</summary>
        public bool IsPlayerClick { get; init; }
        public bool Result { get; init; }
        /// <summary>"OK" / "FAILED" — the grid's Result column as text.</summary>
        public string ResultText => Result ? "OK" : "FAILED";
        /// <summary>Player tile at click time, formatted "(x, y, z)".</summary>
        public string TileText { get; init; } = string.Empty;
        /// <summary>Raw captured snippet (the grid's Snippet column).</summary>
        public string Snippet { get; init; } = string.Empty;
        /// <summary>Local-time clock used as the card's at-a-glance timestamp.</summary>
        public string TimeText => TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
    }

    public sealed class DoActionSignalsViewModel : INotifyPropertyChanged, IActivatableViewModel, IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly EventHandler _timerTickHandler;
        private bool _isActive;
        private bool _disposed;
        private int _maxCount = 100;
        private bool _includeFailed = true;
        private bool _autoRefresh = true;
        private bool _captureEnabled = true;
        private bool _echoToConsole;
        private string _statusMessage = "Ready.";
        private string _nativeBridgeState = "off";
        private string _nativeBridgeText = "Native hook: unknown";
        private int _bridgeRecheckCountdown;
        private DoActionSignalRow? _selectedSignal;
        private ClickHistoryRow? _selectedClick;
        private bool _showClickHistoryView = true;

        public ObservableCollection<DoActionSignalRow> Signals { get; } = new();

        private const int ClickHistoryMax = 50;
        /// <summary>Last few parsed in-game clicks (newest first) for the "Click History" panel.</summary>
        public ObservableCollection<ClickHistoryRow> ClickHistory { get; } = new();

        public int MaxCount
        {
            get => _maxCount;
            set => SetProperty(ref _maxCount, Math.Clamp(value, 10, 500));
        }

        public bool IncludeFailed
        {
            get => _includeFailed;
            set => SetProperty(ref _includeFailed, value);
        }

        public bool AutoRefresh
        {
            get => _autoRefresh;
            set
            {
                if (SetProperty(ref _autoRefresh, value))
                {
                    UpdateTimer();
                }
            }
        }

        public bool CaptureEnabled
        {
            get => _captureEnabled;
            set
            {
                if (SetProperty(ref _captureEnabled, value))
                {
                    DoActionDebugSignals.Configure(enabled: value);
                    StatusMessage = value ? "DoAction capture enabled." : "DoAction capture disabled.";
                    RefreshNativeBridge();
                    if (value)
                    {
                        RefreshSignals();
                    }
                }
            }
        }

        /// <summary>"ok" (hook live), "off" (capture disabled), "error" (bridge unreachable).</summary>
        public string NativeBridgeState
        {
            get => _nativeBridgeState;
            private set => SetProperty(ref _nativeBridgeState, value);
        }

        public string NativeBridgeText
        {
            get => _nativeBridgeText;
            private set => SetProperty(ref _nativeBridgeText, value);
        }

        public bool EchoToConsole
        {
            get => _echoToConsole;
            set
            {
                if (SetProperty(ref _echoToConsole, value))
                {
                    DoActionDebugSignals.Configure(echoToConsole: value);
                    StatusMessage = value ? "DoAction signal console echo enabled." : "DoAction signal console echo disabled.";
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public DoActionSignalRow? SelectedSignal
        {
            get => _selectedSignal;
            set
            {
                if (SetProperty(ref _selectedSignal, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Selected card in the Click History list (drives "Copy Selected" while that view is active).</summary>
        public ClickHistoryRow? SelectedClick
        {
            get => _selectedClick;
            set
            {
                if (SetProperty(ref _selectedClick, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// View toggle: false = the full Signals data grid, true = the full Click History list.
        /// The two are the same feed (Click History is the parsed real-click subset); only one
        /// is shown at a time, taking the entire content area.
        /// </summary>
        public bool ShowClickHistoryView
        {
            get => _showClickHistoryView;
            set
            {
                if (SetProperty(ref _showClickHistoryView, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowSignalsView)));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Inverse of <see cref="ShowClickHistoryView"/>; bound to the "Signals" toggle.</summary>
        public bool ShowSignalsView
        {
            get => !_showClickHistoryView;
            set
            {
                if (value)
                {
                    ShowClickHistoryView = false;
                }
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand ClearBufferCommand { get; }
        public ICommand CopySelectedCommand { get; }
        public ICommand CopyAllCommand { get; }
        /// <summary>Copies the selected signal as a ready-to-paste C# API call (falls back to the raw snippet).</summary>
        public ICommand CopyAsApiCallCommand { get; }
        /// <summary>Copies a single click-history row's API call (command parameter is the <see cref="ClickHistoryRow"/>).</summary>
        public ICommand CopyClickCommand { get; }

        public DoActionSignalsViewModel()
        {
            RefreshCommand = new RelayCommand(_ => RefreshSignals());
            ClearBufferCommand = new RelayCommand(_ => ClearBuffer());
            CopySelectedCommand = new RelayCommand(_ => CopySelected(), _ => HasSelection);
            CopyAllCommand = new RelayCommand(_ => CopyAll(), _ => Signals.Count > 0);
            CopyAsApiCallCommand = new RelayCommand(_ => CopyAsApiCall(), _ => HasSelection);
            CopyClickCommand = new RelayCommand(CopyClick, p => p is ClickHistoryRow);

            var config = DoActionDebugSignals.GetConfig();
            _captureEnabled = config.Enabled;
            _echoToConsole = config.EchoToConsole;

            _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };
            _timerTickHandler = OnTimerTick;
            _timer.Tick += _timerTickHandler;

            RefreshSignals();
        }

        public void OnActivated()
        {
            _isActive = true;
            // Stream real player clicks live: the pump drains the native DoAction hook
            // into the managed buffer with real timestamps and the player's tile.
            try { DoActionDebugSignals.StartNativePump(); } catch { }
            RefreshNativeBridge();
            UpdateTimer();
            RefreshSignals();
        }

        private void RefreshNativeBridge()
        {
            try
            {
                var status = DoActionDebugSignals.VerifyNativeBridge();
                if (!status.Available)
                {
                    NativeBridgeState = "error";
                    NativeBridgeText = $"Native hook unreachable — {status.Error}";
                }
                else if (!CaptureEnabled)
                {
                    NativeBridgeState = "off";
                    NativeBridgeText = "Native hook reachable — capture is OFF, player clicks are not recorded.";
                }
                else if (!status.CaptureEnabled)
                {
                    // Set succeeded but readback disagrees — should not happen; surface it.
                    NativeBridgeState = "error";
                    NativeBridgeText = "Native hook reachable but the capture flag did not stick — check for a second csharp_interop/XInput bridge.";
                }
                else
                {
                    NativeBridgeState = "ok";
                    NativeBridgeText = "Native hook LIVE — real game-window clicks stream into this feed.";
                }
            }
            catch (Exception ex)
            {
                NativeBridgeState = "error";
                NativeBridgeText = $"Native hook check failed: {ex.Message}";
            }
        }

        public void OnDeactivated()
        {
            _isActive = false;
            try { DoActionDebugSignals.StopNativePump(); } catch { }
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            if (_disposed)
            {
                return;
            }

            if (_isActive && AutoRefresh)
            {
                if (!_timer.IsEnabled)
                {
                    _timer.Start();
                }
            }
            else if (_timer.IsEnabled)
            {
                _timer.Stop();
            }
        }

        private void RefreshSignals()
        {
            try
            {
                var snapshot = DoActionDebugSignals.Snapshot(MaxCount, IncludeFailed);
                Signals.Clear();
                foreach (var signal in snapshot)
                {
                    Signals.Add(new DoActionSignalRow
                    {
                        TimestampUtc = signal.TimestampUtc,
                        Surface = signal.Surface,
                        Operation = signal.Operation,
                        Result = signal.Result,
                        Snippet = signal.Snippet,
                        Notes = signal.Notes,
                        Kind = signal.Kind,
                        IsPlayerClick = string.Equals(signal.Surface, "Native", StringComparison.OrdinalIgnoreCase),
                        TileText = signal.Tile is { } t ? $"({t.X}, {t.Y}, {t.Z})" : string.Empty
                    });
                }

                RebuildClickHistory(snapshot);

                StatusMessage = $"Loaded {Signals.Count} signal(s).";
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Refresh failed: {ex.Message}";
            }
        }

        private void RebuildClickHistory(IReadOnlyList<DoActionDebugSignals.DoActionSignal> snapshot)
        {
            ClickHistory.Clear();
            foreach (var signal in snapshot) // snapshot is newest-first
            {
                if (!DoActionDebugSignals.TryParseCapture(signal.Snippet, out var cap))
                    continue;
                ClickHistory.Add(ToClickHistoryRow(signal, cap));
                if (ClickHistory.Count >= ClickHistoryMax)
                    break;
            }
        }

        private static ClickHistoryRow ToClickHistoryRow(DoActionDebugSignals.DoActionSignal signal, DoActionDebugSignals.CaptureInfo cap)
        {
            var ts = signal.TimestampUtc;
            var tile = cap.Tile is { } t ? $"{t.X},{t.Y}" : "?";
            var api = FormatApiCall(cap);

            // Grid-parity fields shared by every card, regardless of kind.
            var common = new ClickHistoryRow
            {
                TimestampUtc = ts,
                Surface = signal.Surface,
                Operation = signal.Operation,
                Result = signal.Result,
                Snippet = signal.Snippet,
                IsPlayerClick = string.Equals(signal.Surface, "Native", StringComparison.OrdinalIgnoreCase),
                TileText = signal.Tile is { } st ? $"({st.X}, {st.Y}, {st.Z})" : string.Empty,
                ApiSnippet = api
            };

            return cap.Kind switch
            {
                "Object" => common with
                {
                    Tag = "[O]", Kind = "Object",
                    Title = $"Object {cap.ActionOpcode} @({tile})",
                    Detail = $"ID={cap.Id}   action={cap.ActionOpcode}   offset={cap.Offset}"
                },
                "NPC" => common with
                {
                    Tag = "[N]", Kind = "NPC",
                    Title = $"NPC {cap.Id}",
                    Detail = $"ID={cap.Id}   action={cap.ActionOpcode}   offset={cap.Offset}"
                },
                "GroundItem" => common with
                {
                    Tag = "[G]", Kind = "GroundItem",
                    Title = $"Ground item {cap.Id} @({tile})",
                    Detail = $"ID={cap.Id}   action={cap.ActionOpcode}   offset={cap.Offset}"
                },
                "Interface" => common with
                {
                    Tag = "[I]", Kind = "Interface",
                    Title = $"UI [{cap.InterfacePath}]",
                    Detail = $"act={cap.ActionOpcode}   item={cap.Item}   offset={cap.Offset}"
                },
                _ => common with
                {
                    Tag = "[T]", Kind = "Walk",
                    Title = $"Walk ({tile})",
                    Detail = cap.Tile is { } tt ? $"({tt.X}, {tt.Y}, {tt.Z})" : string.Empty
                }
            };
        }

        /// <summary>
        /// Turns a captured in-game click into a ready-to-paste C# API call that reproduces it.
        /// The captured action opcode and route offset are the exact values these overloads take,
        /// so the emitted call is calibrated to the real interaction, not a guess.
        /// </summary>
        public static string FormatApiCall(DoActionDebugSignals.CaptureInfo cap)
        {
            switch (cap.Kind)
            {
                case "Object":
                    var objComment = string.IsNullOrWhiteSpace(cap.Name) ? "" : $" // {cap.Name}";
                    return $"Objects.DoActionByIds(new[] {{ {cap.Id} }}, {cap.ActionOpcode}, offset: {cap.Offset});{objComment}";
                case "NPC":
                    var npcComment = string.IsNullOrWhiteSpace(cap.Name) ? "" : $" // {cap.Name}";
                    return $"NPC.DoActionByIds(new[] {{ {cap.Id} }}, {cap.ActionOpcode}, offset: {cap.Offset});{npcComment}";
                case "GroundItem":
                    return $"GroundItems.DoActionByIdsRoute(new[] {{ {cap.Id} }}, {cap.ActionOpcode}, route: {cap.Offset});";
                case "Interface":
                    // Interfaces.DoAction takes the six command/interface words + offset; the capture
                    // carries the decoded action + interface path. Emit what we have and leave the
                    // remaining words for the scripter to fill from the path.
                    return $"// Interface click — path={cap.InterfacePath} action={cap.ActionOpcode} item={cap.Item} offset={cap.Offset}";
                case "Tile":
                case "Walk":
                    return cap.Tile is { } t
                        ? $"Movement.SpecialWalk(new WorldPoint({t.X}, {t.Y}, {t.Z}));"
                        : "// Walk — tile unavailable";
                default:
                    return $"// {cap.Kind} click — id={cap.Id} action={cap.ActionOpcode} offset={cap.Offset}";
            }
        }

        private void ClearBuffer()
        {
            try
            {
                var removed = DoActionDebugSignals.Clear();
                RefreshSignals();
                StatusMessage = $"Cleared {removed} buffered signal(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Clear failed: {ex.Message}";
            }
        }

        /// <summary>True when there is something to copy in the active view (a grid row or a click card).</summary>
        private bool HasSelection => ShowClickHistoryView ? SelectedClick != null : SelectedSignal != null;

        private void CopySelected()
        {
            if (ShowClickHistoryView)
            {
                if (SelectedClick is { } click)
                {
                    // Mirror the grid's "Copy Selected" (timestamp / surface.operation / result / snippet),
                    // then append the ready-to-paste API call — the genuinely useful bits, not the UI labels.
                    var text = $"[{click.TimestampUtc:O}] {click.Surface}.{click.Operation} => {click.ResultText}{Environment.NewLine}{click.Snippet}{Environment.NewLine}{click.ApiSnippet}";
                    TrySetClipboard(text, "Copied selected click.");
                }
                return;
            }

            if (SelectedSignal == null)
            {
                return;
            }

            var signalText = $"[{SelectedSignal.TimestampUtc:O}] {SelectedSignal.Surface}.{SelectedSignal.Operation} => {(SelectedSignal.Result ? "OK" : "FAILED")}{Environment.NewLine}{SelectedSignal.Snippet}";
            TrySetClipboard(signalText, "Copied selected signal.");
        }

        private void CopyAsApiCall()
        {
            if (ShowClickHistoryView)
            {
                if (SelectedClick is { } click && !string.IsNullOrEmpty(click.ApiSnippet))
                {
                    TrySetClipboard(click.ApiSnippet, "Copied API call for selected click.");
                }
                return;
            }

            if (SelectedSignal == null)
            {
                return;
            }

            if (DoActionDebugSignals.TryParseCapture(SelectedSignal.Snippet, out var cap))
            {
                TrySetClipboard(FormatApiCall(cap), "Copied API call for selected click.");
            }
            else
            {
                // Tool-dispatched / OSRS snippets aren't structured captures — give the raw snippet.
                TrySetClipboard(SelectedSignal.Snippet, "No structured capture — copied raw snippet.");
            }
        }

        private void CopyClick(object? parameter)
        {
            if (parameter is not ClickHistoryRow row || string.IsNullOrEmpty(row.ApiSnippet))
            {
                return;
            }

            TrySetClipboard(row.ApiSnippet, "Copied API call.");
        }

        private void CopyAll()
        {
            var lines = Signals.Select(s =>
                $"[{s.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)}] {s.Surface}.{s.Operation} => {(s.Result ? "OK" : "FAILED")} | {s.Snippet}");
            TrySetClipboard(string.Join(Environment.NewLine, lines), $"Copied {Signals.Count} signals.");
        }

        private void TrySetClipboard(string text, string okMessage)
        {
            try
            {
                Clipboard.SetText(text);
                StatusMessage = okMessage;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Copy failed: {ex.Message}";
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _timer.Stop();
            _timer.Tick -= _timerTickHandler;
            _disposed = true;
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_isActive && AutoRefresh)
            {
                RefreshSignals();

                // Re-probe the bridge occasionally (~every 7.5s) so injecting/closing
                // the game session updates the indicator without leaving the page.
                if (--_bridgeRecheckCountdown <= 0)
                {
                    _bridgeRecheckCountdown = 10;
                    RefreshNativeBridge();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
