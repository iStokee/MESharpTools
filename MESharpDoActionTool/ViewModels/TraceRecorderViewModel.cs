using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using MESharp.Commands;
using MESharp.Recording;
using MESharp.ViewModels;

namespace MESharp.ViewModels
{
    /// <summary>
    /// Drives a <see cref="TraceRecorder"/> from the DoAction tool. One toggle starts/stops a hand-played
    /// session capture; live counters + a status log show what's being recorded. The trace lands in a session
    /// folder under %LOCALAPPDATA%\MESharp\traces for an agent to read back.
    /// </summary>
    public sealed class TraceRecorderViewModel : BaseViewModel, IActivatableViewModel, IDisposable
    {
        private readonly TraceRecorder _recorder = new();
        private readonly DispatcherTimer _timer;
        private readonly Dispatcher _dispatcher;

        private bool _isRecording;
        private string _status = "Idle. Click Record, play a floor, then Stop.";
        private string _sessionDir = string.Empty;
        private int _samples;
        private int _heartbeats;
        private int _clicks;
        private int _screenshots;
        private TraceScreenshotMode _selectedScreenshotMode = TraceScreenshotMode.EventOnly;
        private bool _extendedReads;
        private bool _captureScreenshots = true;
        private TraceProfileRow? _selectedProfile;
        private string _profileDescription = string.Empty;
        private string _customProfileName = string.Empty;

        public TraceRecorderViewModel()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _recorder.Log += OnRecorderLog;

            ToggleCommand = new RelayCommand(_ => Toggle());
            OpenFolderCommand = new RelayCommand(_ => OpenFolder(), _ => !string.IsNullOrEmpty(_sessionDir));
            RefreshTracesCommand = new RelayCommand(_ => RefreshTraces());
            DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected(), _ => _selectedTrace != null && !_isRecording);
            DeleteAllCommand = new RelayCommand(_ => DeleteAll(), _ => Traces.Count > 0 && !_isRecording);
            OpenTraceCommand = new RelayCommand(_ => OpenSelectedTrace(), _ => _selectedTrace != null);
            SaveProfileCommand = new RelayCommand(_ => SaveProfile(), _ => !_isRecording);
            RefreshProfilesCommand = new RelayCommand(_ => RefreshProfiles(), _ => !_isRecording);
            OpenProfilesFolderCommand = new RelayCommand(_ => OpenProfilesFolder());

            _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += (_, _) => RefreshCounters();
            RefreshProfiles();
        }

        public ObservableCollection<string> LogLines { get; } = new();
        public ObservableCollection<TraceRow> Traces { get; } = new();
        public ObservableCollection<TraceProfileRow> Profiles { get; } = new();
        public IReadOnlyList<TraceScreenshotMode> ScreenshotModeOptions { get; } = new[]
        {
            TraceScreenshotMode.EventOnly,
            TraceScreenshotMode.EventPlusBurst,
            TraceScreenshotMode.EventPlusKeyframes,
            TraceScreenshotMode.Periodic1s
        };

        public ICommand ToggleCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand RefreshTracesCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand DeleteAllCommand { get; }
        public ICommand OpenTraceCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand RefreshProfilesCommand { get; }
        public ICommand OpenProfilesFolderCommand { get; }

        public TraceProfileRow? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (!SetProperty(ref _selectedProfile, value))
                {
                    return;
                }

                ApplySelectedProfile();
            }
        }

        public string ProfileDescription
        {
            get => _profileDescription;
            private set => SetProperty(ref _profileDescription, value);
        }

        public string CustomProfileName
        {
            get => _customProfileName;
            set => SetProperty(ref _customProfileName, value);
        }

        private TraceRow? _selectedTrace;
        public TraceRow? SelectedTrace
        {
            get => _selectedTrace;
            set => SetProperty(ref _selectedTrace, value);
        }

        private string _tracesSummary = string.Empty;
        public string TracesSummary
        {
            get => _tracesSummary;
            private set => SetProperty(ref _tracesSummary, value);
        }

        public bool IsRecording
        {
            get => _isRecording;
            private set { if (SetProperty(ref _isRecording, value)) RaisePropertyChanged(nameof(ToggleLabel)); }
        }

        public string ToggleLabel => _isRecording ? "Stop Recording" : "Record";

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public string SessionDir
        {
            get => _sessionDir;
            private set => SetProperty(ref _sessionDir, value);
        }

        public int Samples { get => _samples; private set => SetProperty(ref _samples, value); }
        public int Heartbeats { get => _heartbeats; private set => SetProperty(ref _heartbeats, value); }
        public int Clicks { get => _clicks; private set => SetProperty(ref _clicks, value); }
        public int Screenshots { get => _screenshots; private set => SetProperty(ref _screenshots, value); }

        public TraceScreenshotMode SelectedScreenshotMode
        {
            get => _selectedScreenshotMode;
            set => SetProperty(ref _selectedScreenshotMode, value);
        }

        /// <summary>Off by default. Adds the novel reads (equipment, interfaces, DG signals, prayer/adrenaline)
        /// that aren't yet proven crash-safe on the sampling thread. Leave off for the first runs.</summary>
        public bool ExtendedReads
        {
            get => _extendedReads;
            set => SetProperty(ref _extendedReads, value);
        }

        /// <summary>Capture PNGs on clicks / interface opens. On by default; turn off to bisect if a crash
        /// happens mid-play (screenshot capture is the next suspect after the state reads).</summary>
        public bool CaptureScreenshots
        {
            get => _captureScreenshots;
            set => SetProperty(ref _captureScreenshots, value);
        }

        public void OnActivated()
        {
            _timer.Start();
            RefreshTraces();
        }

        public void OnDeactivated() => _timer.Stop();

        private void RefreshTraces()
        {
            try
            {
                var sessions = TraceArchive.List();
                Traces.Clear();
                long total = 0;
                foreach (var s in sessions)
                {
                    Traces.Add(new TraceRow(s));
                    total += s.SizeBytes;
                }
                TracesSummary = sessions.Count == 0
                    ? "No saved traces."
                    : $"{sessions.Count} trace(s), {TraceRow.Bytes(total)} total.";
            }
            catch (Exception ex)
            {
                TracesSummary = $"Failed to list traces: {ex.Message}";
            }
        }

        private void RefreshProfiles()
        {
            var keepId = _selectedProfile?.Profile.Id;
            Profiles.Clear();
            foreach (var profile in TraceSignalProfileStore.LoadAll())
            {
                Profiles.Add(new TraceProfileRow(profile));
            }

            SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Profile.Id, keepId, StringComparison.OrdinalIgnoreCase))
                              ?? Profiles.FirstOrDefault(p => string.Equals(p.Profile.Id, "minimal", StringComparison.OrdinalIgnoreCase))
                              ?? Profiles.FirstOrDefault();
        }

        private void ApplySelectedProfile()
        {
            var profile = _selectedProfile?.Profile;
            if (profile == null)
            {
                ProfileDescription = string.Empty;
                return;
            }

            var options = profile.Options;
            SelectedScreenshotMode = NormalizeScreenshotMode(options);
            CaptureScreenshots = options.CaptureScreenshots;
            ExtendedReads = options.IncludePlayerExtras
                            && options.IncludeEquipment
                            && options.IncludeInterfaces
                            && options.IncludeDgSignals;
            CustomProfileName = profile.IsBuiltIn ? $"{profile.Name} Custom" : profile.Name;
            ProfileDescription = $"{profile.Description}  Signals: {DescribeSignals(options)}";
        }

        private void DeleteSelected()
        {
            var sel = _selectedTrace;
            if (sel == null) return;
            if (_recorder.IsRecording && string.Equals(sel.SessionId, _recorder.SessionId, StringComparison.Ordinal))
            {
                OnRecorderLog("Refusing to delete the trace that is currently recording.");
                return;
            }
            TraceArchive.Delete(sel.SessionId);
            OnRecorderLog($"Deleted trace {sel.SessionId}.");
            RefreshTraces();
        }

        private void DeleteAll()
        {
            var activeId = _recorder.IsRecording ? _recorder.SessionId : null;
            var removed = 0;
            foreach (var t in Traces.ToList())
            {
                if (activeId != null && string.Equals(t.SessionId, activeId, StringComparison.Ordinal)) continue;
                if (TraceArchive.Delete(t.SessionId)) removed++;
            }
            OnRecorderLog($"Deleted {removed} trace(s).");
            RefreshTraces();
        }

        private void OpenSelectedTrace()
        {
            var dir = _selectedTrace?.Dir;
            try
            {
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = dir, UseShellExecute = true });
                }
            }
            catch { /* best effort */ }
        }

        private void Toggle()
        {
            if (_recorder.IsRecording)
            {
                _recorder.Stop();
                IsRecording = false;
                RefreshTraces();
            }
            else
            {
                LogLines.Clear();
                var opts = _selectedProfile?.Profile.Options.Clone() ?? new TraceRecorderOptions();
                opts.ScreenshotMode = _selectedScreenshotMode;
                opts.ScreenshotOnKeyframe = _selectedScreenshotMode == TraceScreenshotMode.EventPlusKeyframes;
                opts.CaptureScreenshots = _captureScreenshots;
                if (_extendedReads) opts.EnableAllExtended();
                _recorder.Start(opts);
                IsRecording = true;
                SessionDir = _recorder.SessionDir ?? string.Empty;
            }
            RefreshCounters();
        }

        private void SaveProfile()
        {
            var source = _selectedProfile?.Profile ?? new TraceSignalProfile
            {
                Id = "custom",
                Name = "Custom Trace Profile",
                Description = "Custom recorder profile.",
                Options = new TraceRecorderOptions()
            };

            var profile = source.Clone();
            profile.Options.ScreenshotMode = _selectedScreenshotMode;
            profile.Options.ScreenshotOnKeyframe = _selectedScreenshotMode == TraceScreenshotMode.EventPlusKeyframes;
            profile.Options.CaptureScreenshots = _captureScreenshots;
            if (_extendedReads)
            {
                profile.Options.EnableAllExtended();
            }

            var saved = TraceSignalProfileStore.SaveCustom(profile, _customProfileName);
            OnRecorderLog($"Saved trace profile '{saved.Name}'.");
            RefreshProfiles();
            SelectedProfile = Profiles.FirstOrDefault(p => string.Equals(p.Profile.Id, saved.Id, StringComparison.OrdinalIgnoreCase))
                              ?? SelectedProfile;
        }

        private void RefreshCounters()
        {
            IsRecording = _recorder.IsRecording;
            Samples = _recorder.SampleCount;
            Heartbeats = _recorder.HeartbeatCount;
            Clicks = _recorder.ClickCount;
            Screenshots = _recorder.ScreenshotCount;
            if (_recorder.IsRecording)
            {
                Status = $"Recording… {Heartbeats} heartbeats / {Clicks} clicks / {Screenshots} shots";
            }
        }

        private void OnRecorderLog(string line)
        {
            void Append()
            {
                LogLines.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
                while (LogLines.Count > 200) LogLines.RemoveAt(LogLines.Count - 1);
                Status = line;
            }

            if (_dispatcher.CheckAccess()) Append();
            else _dispatcher.BeginInvoke((Action)Append);
        }

        private void OpenFolder()
        {
            try
            {
                if (!string.IsNullOrEmpty(_sessionDir) && System.IO.Directory.Exists(_sessionDir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _sessionDir,
                        UseShellExecute = true
                    });
                }
            }
            catch { /* best effort */ }
        }

        private void OpenProfilesFolder()
        {
            try
            {
                var dir = TraceSignalProfileStore.ProfileRoot();
                System.IO.Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch { /* best effort */ }
        }

        private static TraceScreenshotMode NormalizeScreenshotMode(TraceRecorderOptions options)
        {
            return options.ScreenshotMode == TraceScreenshotMode.EventOnly && options.ScreenshotOnKeyframe
                ? TraceScreenshotMode.EventPlusKeyframes
                : options.ScreenshotMode;
        }

        public void Dispose()
        {
            _timer.Stop();
            _recorder.Log -= OnRecorderLog;
            if (_recorder.IsRecording)
            {
                _recorder.Stop();
            }
        }

        /// <summary>Display row for a saved trace in the cleanup list.</summary>
        public sealed class TraceRow
        {
            public TraceRow(TraceSessionInfo info)
            {
                SessionId = info.SessionId;
                Dir = info.Dir;
                Display = $"{info.ModifiedUtc.ToLocalTime():MM-dd HH:mm}   {Bytes(info.SizeBytes),9}   " +
                          $"{info.Samples} samp / {info.Clicks} clk / {info.Screenshots} shots   {info.SessionId}";
            }

            public string SessionId { get; }
            public string Dir { get; }
            public string Display { get; }

            public static string Bytes(long n)
            {
                if (n >= 1L << 30) return $"{n / (double)(1L << 30):0.0} GB";
                if (n >= 1L << 20) return $"{n / (double)(1L << 20):0.0} MB";
                if (n >= 1L << 10) return $"{n / (double)(1L << 10):0.0} KB";
                return $"{n} B";
            }
        }

        public sealed class TraceProfileRow
        {
            public TraceProfileRow(TraceSignalProfile profile)
            {
                Profile = profile;
                Display = profile.DisplayName;
            }

            public TraceSignalProfile Profile { get; }
            public string Display { get; }
        }

        private static string DescribeSignals(TraceRecorderOptions o)
        {
            var parts = new[]
            {
                o.IncludeNativeClicks ? "native clicks" : null,
                o.IncludeManagedDoActions ? "managed actions" : null,
                o.IncludeChat ? "chat" : null,
                o.IncludeObjects ? (o.IncludeNonActionableObjects ? "all objects" : "actionable objects") : null,
                o.IncludeNpcs ? "NPCs" : null,
                o.IncludeInventory ? "inventory" : null,
                o.IncludeEquipment ? "equipment" : null,
                o.IncludeInterfaces ? "interface status" : null,
                o.IncludeInterfaceComponents ? "interface components" : null,
                o.IncludeDgSignals ? "DG signals" : null,
                o.IncludeClickContextFrames ? "click context" : null,
                o.CaptureScreenshots ? $"screenshots={NormalizeScreenshotMode(o)}" : "screenshots=off",
            }.Where(x => x != null);

            return string.Join(", ", parts);
        }
    }
}
