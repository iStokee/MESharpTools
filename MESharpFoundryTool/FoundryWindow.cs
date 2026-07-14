using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace MESharp;

internal sealed class FoundryWindow : Window
{
    private static readonly Brush PresentBrush = Frozen(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly Brush MissingBrush = Frozen(Color.FromRgb(0xE5, 0x73, 0x73));
    private static readonly Brush UnknownBrush = Frozen(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly Brush ReadyBrush = Frozen(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly Brush AttentionBrush = Frozen(Color.FromRgb(0xEF, 0x6C, 0x00));
    private static readonly Brush ReadyBackgroundBrush = Frozen(Color.FromArgb(0x20, 0x4C, 0xAF, 0x50));
    private static readonly Brush AttentionBackgroundBrush = Frozen(Color.FromArgb(0x18, 0xEF, 0x6C, 0x00));

    private readonly FoundryRecorder _recorder = new();
    private readonly FoundrySettings _settings = FoundrySettings.Load();
    private readonly DispatcherTimer _telemetryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _preflightTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _preflightReady;

    private readonly TextBox _activity = new();
    private readonly TextBox _notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 44, MaxHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel _targetRows = new();
    private readonly Button _addTarget = new() { Content = "Add target", Margin = new(0, 8, 8, 0), Padding = new(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _probeTargets = new() { Content = "Refresh preflight", Margin = new(0, 8, 0, 0), Padding = new(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Refresh the automatic live target checks now" };
    private readonly Button _start = new() { Content = "Start demonstration", Margin = new(0, 0, 8, 0), Padding = new(14, 6, 14, 6), FontWeight = FontWeights.SemiBold, IsEnabled = false };
    private readonly Button _cycle = new() { Content = "Mark cycle", Margin = new(0, 0, 8, 0), Padding = new(14, 6, 14, 6), IsEnabled = false };
    private readonly Button _stop = new() { Content = "Stop and finalize", Padding = new(14, 6, 14, 6), IsEnabled = false };
    private readonly TextBlock _telemetry = new() { FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center, Margin = new(16, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _cycleChips = new() { FontFamily = new FontFamily("Consolas"), Margin = new(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _cycleProgress = new() { Minimum = 0, Maximum = 5, Height = 5, Margin = new(0, 8, 0, 0) };
    private readonly Border _readiness = new() { CornerRadius = new(6), Padding = new(10, 7, 10, 7), Margin = new(0, 12, 0, 0) };
    private readonly TextBlock _readinessText = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly TextBox _log = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ListBox _sessions = new() { BorderThickness = new(0), Background = Brushes.Transparent };
    private readonly AtomPipelinePanel _pipeline;

    public FoundryWindow()
    {
        _pipeline = new AtomPipelinePanel(_settings);
        Title = "MESharp Foundry";
        Width = _settings.WindowWidth ?? 980;
        Height = _settings.WindowHeight ?? 700;
        MinWidth = 700; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "App.BackgroundBrush");
        SetResourceReference(ForegroundProperty, "App.ForegroundBrush");
        _start.SetResourceReference(Control.BackgroundProperty, "PrimaryBrush");
        _start.SetResourceReference(Control.ForegroundProperty, "PrimaryForegroundBrush");
        _cycleProgress.SetResourceReference(Control.ForegroundProperty, "PrimaryBrush");
        _cycleProgress.SetResourceReference(Control.BackgroundProperty, "App.FieldBrush");

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // readiness
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // session metadata
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // targets
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // controls + telemetry
        root.RowDefinitions.Add(new RowDefinition { Height = new(1, GridUnitType.Star) }); // log + sessions

        root.Children.Add(AtRow(BuildHeader(), 0));
        root.Children.Add(AtRow(BuildReadiness(), 1));
        root.Children.Add(AtRow(BuildSessionCard(), 2));
        root.Children.Add(AtRow(BuildTargetsCard(), 3));
        root.Children.Add(AtRow(BuildControls(), 4));
        root.Children.Add(AtRow(BuildBottom(), 5));
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Capture", Content = root });
        tabs.Items.Add(new TabItem { Header = "Pipeline", Content = _pipeline });
        Content = tabs;

        _activity.Text = _settings.Activity;
        _notes.Text = _settings.Notes;
        foreach (var target in _settings.TargetClasses) _targetRows.Children.Add(CreateTargetRow(target));
        if (_settings.TargetClasses.Count == 0) _targetRows.Children.Add(CreateTargetRow(new FoundryTargetClass()));

        _addTarget.Click += (_, _) => { _targetRows.Children.Add(CreateTargetRow(new FoundryTargetClass())); InvalidatePreflight(); };
        _probeTargets.Click += (_, _) => RunPreflight(logResults: true);
        _start.Click += (_, _) => Start();
        _cycle.Click += (_, _) => _recorder.MarkCycle();
        _stop.Click += (_, _) => Stop();
        _recorder.Status += message => Dispatcher.Invoke(() => AppendLog(message));
        _telemetryTimer.Tick += (_, _) => UpdateTelemetry();
        _preflightTimer.Tick += (_, _) => RunPreflight(logResults: false);
        _activity.TextChanged += (_, _) => InvalidatePreflight();
        _sessions.MouseDoubleClick += (_, _) => OpenSelectedSession();
        Loaded += (_, _) => { RunPreflight(logResults: false); _preflightTimer.Start(); };
        Closed += (_, _) => { _telemetryTimer.Stop(); _preflightTimer.Stop(); PersistSettings(); _pipeline.Dispose(); _recorder.Dispose(); };

        RefreshSessions();
        UpdateReadiness("Automatic preflight is checking the activity and live target bindings…", false);
        AppendLog("Preflight updates automatically. When every target is green, record five clean cycles and include deliberate negative scenes.");
    }

    private UIElement BuildReadiness()
    {
        _readiness.Child = _readinessText;
        return _readiness;
    }

    private UIElement BuildHeader()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Foundry demonstration recorder", FontSize = 18, FontWeight = FontWeights.SemiBold });
        var caption = new TextBlock
        {
            Text = "Captures frames, native input, privileged truth, and projected screen positions for the bound target classes — hovered or not, present or absent.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 2, 0, 0)
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "App.SubtleForegroundBrush");
        panel.Children.Add(caption);
        return panel;
    }

    private UIElement BuildSessionCard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(2, GridUnitType.Star) });
        var activity = Labeled("Activity", _activity);
        activity.Margin = new(0, 0, 12, 0);
        Grid.SetColumn(activity, 0);
        var notes = Labeled("Notes", _notes);
        Grid.SetColumn(notes, 1);
        grid.Children.Add(activity);
        grid.Children.Add(notes);
        return Card(grid);
    }

    private UIElement BuildTargetsCard()
    {
        var header = new TextBlock { Text = "TARGET CLASSES", FontSize = 11, Margin = new(0, 0, 0, 6) };
        header.SetResourceReference(TextBlock.ForegroundProperty, "App.SubtleForegroundBrush");
        var guidance = new TextBlock
        {
            Text = "Use stable dataset labels (for example bank-target), then enter the live scene ID. Preflight refreshes automatically and blocks collapsed or off-screen points.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new(0, 0, 0, 8)
        };
        guidance.SetResourceReference(TextBlock.ForegroundProperty, "App.SubtleForegroundBrush");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_addTarget);
        buttons.Children.Add(_probeTargets);
        var scroller = new ScrollViewer
        {
            Content = _targetRows, MaxHeight = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        return Card(header, guidance, scroller, buttons);
    }

    private UIElement BuildControls()
    {
        var grid = new Grid { Margin = new(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(_start); buttons.Children.Add(_cycle); buttons.Children.Add(_stop);
        Grid.SetColumn(buttons, 0);
        Grid.SetColumn(_telemetry, 1);
        grid.Children.Add(buttons);
        grid.Children.Add(_telemetry);
        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(_cycleProgress);
        panel.Children.Add(_cycleChips);
        return panel;
    }

    private UIElement BuildBottom()
    {
        var grid = new Grid { Margin = new(0, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new(2, GridUnitType.Star) });

        var log = Card(SectionHeader("ACTIVITY LOG"), Stretched(_log));
        log.Margin = new(0, 0, 12, 0);
        Grid.SetColumn(log, 0);

        var sessions = Card(SectionHeader("RECENT SESSIONS  (double-click to open)"), Stretched(_sessions));
        Grid.SetColumn(sessions, 1);

        grid.Children.Add(log);
        grid.Children.Add(sessions);
        return grid;
    }

    private void Start()
    {
        try
        {
            RunPreflight(logResults: true);
            if (!_preflightReady)
                throw new InvalidOperationException("Preflight is not ready. Resolve the highlighted setup issue before recording.");
            PersistSettings();
            _recorder.Start(_settings);
            _preflightTimer.Stop();
            _start.IsEnabled = false; _cycle.IsEnabled = true; _stop.IsEnabled = true;
            SetTargetRowsRecording(true);
            _cycleChips.Text = "";
            _cycleProgress.Value = 0;
            UpdateReadiness("RECORDING · Perform one clean cycle, then mark it. Keep the cursor away from targets between actions.", true);
            _telemetryTimer.Start();
        }
        catch (Exception ex) { AppendLog(ex.Message); }
    }

    private void Stop()
    {
        _recorder.Stop();
        _telemetryTimer.Stop();
        _telemetry.Text = "";
        _start.IsEnabled = true; _cycle.IsEnabled = false; _stop.IsEnabled = false;
        SetTargetRowsRecording(false);
        _preflightReady = false;
        _preflightTimer.Start();
        RunPreflight(logResults: false);
        RefreshSessions();
    }

    private void RunPreflight(bool logResults)
    {
        if (_recorder.IsRecording) return;
        try
        {
            PersistSettings(save: false);
            var setupErrors = ValidateSetup(_settings).ToList();
            if (setupErrors.Count > 0)
            {
                _preflightReady = false;
                _start.IsEnabled = false;
                ApplyTargetStatus([]);
                UpdateReadiness($"SETUP · {setupErrors[0]}", false);
                if (logResults) foreach (var error in setupErrors) AppendLog(error);
                return;
            }

            var statuses = FoundryRecorder.ProbeTargets(_settings.TargetClasses);
            ApplyTargetStatus(statuses);
            if (logResults)
                foreach (var status in statuses)
                    AppendLog($"{status.ClassName}: {(status.Present ? Describe(status) : "entity not in scene")}");

            var unresolved = statuses.Where(status => !status.Present || status.Screen is not { Length: 2 }).ToArray();
            var collisions = FindProjectionCollisions(statuses).ToArray();
            _preflightReady = unresolved.Length == 0 && collisions.Length == 0;
            _start.IsEnabled = _preflightReady;
            if (_preflightReady)
            {
                UpdateReadiness($"READY · {statuses.Count} live target{(statuses.Count == 1 ? "" : "s")} resolved to distinct in-frame points. Start when the scene is prepared.", true);
            }
            else if (collisions.Length > 0)
            {
                UpdateReadiness($"NOT READY · {collisions[0]}. Move the camera or correct the bindings.", false);
            }
            else
            {
                var names = string.Join(", ", unresolved.Select(status => status.ClassName));
                UpdateReadiness($"NOT READY · Missing or off-screen: {names}.", false);
            }
        }
        catch (Exception ex)
        {
            _preflightReady = false;
            _start.IsEnabled = false;
            UpdateReadiness("PREFLIGHT ERROR · Check that the game is loaded and the target bindings are valid.", false);
            if (logResults) AppendLog($"Preflight failed: {ex.Message}");
        }
    }

    private static IEnumerable<string> ValidateSetup(FoundrySettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Activity))
            yield return "Name the activity.";
        if (settings.TargetClasses.Count == 0)
            yield return "Add at least one target binding.";
        foreach (var target in settings.TargetClasses)
        {
            if (!IsStableClassName(target.ClassName))
                yield return $"Use a stable lowercase class label for '{target.ClassName}' (for example, bank-target).";
            if (target.Id <= 0)
                yield return $"Enter a scene entity ID for '{target.ClassName}'.";
        }
        var duplicate = settings.TargetClasses.GroupBy(target => target.ClassName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            yield return $"Target class '{duplicate.Key}' is configured more than once.";
    }

    private static bool IsStableClassName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] == '-' || value[^1] == '-') return false;
        return value.All(character => char.IsAsciiLetterLower(character) || char.IsDigit(character) || character == '-');
    }

    private static IEnumerable<string> FindProjectionCollisions(IReadOnlyList<FoundryTargetStatus> statuses)
    {
        for (var left = 0; left < statuses.Count; left++)
        for (var right = left + 1; right < statuses.Count; right++)
        {
            if (statuses[left].Screen is not { Length: 2 } a || statuses[right].Screen is not { Length: 2 } b) continue;
            if (Math.Abs(a[0] - b[0]) <= 4 && Math.Abs(a[1] - b[1]) <= 4)
                yield return $"{statuses[left].ClassName} and {statuses[right].ClassName} resolve to the same screen point";
        }
    }

    private void UpdateTelemetry()
    {
        var last = _recorder.LastInputDescription;
        _telemetry.Text = $"{_recorder.Elapsed:hh\\:mm\\:ss}  {_recorder.EventCount} events  cycle {_recorder.CycleCount + 1}: {_recorder.InputEventsThisCycle} inputs" +
                          (string.IsNullOrEmpty(last) ? "" : $"  |  {last}");
        var counts = _recorder.CompletedCycleInputCounts;
        _cycleProgress.Value = Math.Min(5, _recorder.CycleCount);
        _cycleChips.Text = counts.Count == 0 ? "" :
            string.Join("   ", counts.Select((count, index) => $"{(count == 0 ? "⚠" : "✓")} C{index + 1}: {count} inputs"));
        if (_recorder.CycleCount >= 5)
            UpdateReadiness("FIVE CYCLES CAPTURED · Add deliberate variation if useful, or stop and finalize.", true);
        ApplyTargetStatus(_recorder.LastTargetStatus);
    }

    private void ApplyTargetStatus(IReadOnlyList<FoundryTargetStatus> statuses)
    {
        foreach (var row in _targetRows.Children.OfType<TargetRow>())
        {
            var match = statuses.FirstOrDefault(s => string.Equals(s.ClassName, row.ClassName, StringComparison.OrdinalIgnoreCase));
            if (match is null) row.ShowStatus(null, "");
            else row.ShowStatus(match.Present && match.Screen is { Length: 2 }, match.Present ? Describe(match) : "entity not in scene");
        }
    }

    private static string Describe(FoundryTargetStatus status)
    {
        var screen = status.Screen is { Length: 2 } point ? $"screen ({point[0]:F0}, {point[1]:F0})" : "off screen / no projection";
        return status.Distance is { } distance ? $"{screen}, {distance:F0} tiles" : screen;
    }

    private void PersistSettings(bool save = true)
    {
        _settings.Activity = _activity.Text;
        _settings.Notes = _notes.Text;
        _settings.TargetClasses = _targetRows.Children.OfType<TargetRow>()
            .Select(row => row.ToTarget())
            .Where(target => !string.IsNullOrWhiteSpace(target.ClassName))
            .ToList();
        _settings.WindowWidth = ActualWidth > 0 ? ActualWidth : _settings.WindowWidth;
        _settings.WindowHeight = ActualHeight > 0 ? ActualHeight : _settings.WindowHeight;
        if (save) _settings.Save();
    }

    private void SetTargetRowsRecording(bool recording)
    {
        foreach (var row in _targetRows.Children.OfType<TargetRow>())
        {
            row.SetRecording(recording);
            if (!recording) row.ResetAbsent();
        }
        _addTarget.IsEnabled = !recording;
        _probeTargets.IsEnabled = !recording;
    }

    private TargetRow CreateTargetRow(FoundryTargetClass target)
    {
        var row = new TargetRow(target);
        row.RemoveRequested += () => { if (!_recorder.IsRecording) { _targetRows.Children.Remove(row); InvalidatePreflight(); } };
        row.AbsentChanged += (className, absent) => _recorder.SetTargetAbsent(className, absent);
        row.BindingChanged += InvalidatePreflight;
        return row;
    }

    private void InvalidatePreflight()
    {
        if (_recorder.IsRecording) return;
        _preflightReady = false;
        _start.IsEnabled = false;
        UpdateReadiness("CHECKING · Automatic preflight will refresh the live bindings.", false);
    }

    private void UpdateReadiness(string message, bool ready)
    {
        _readinessText.Text = message;
        _readinessText.Foreground = ready ? ReadyBrush : AttentionBrush;
        _readiness.Background = ready ? ReadyBackgroundBrush : AttentionBackgroundBrush;
        _readiness.BorderBrush = ready ? ReadyBrush : AttentionBrush;
        _readiness.BorderThickness = new Thickness(1);
    }

    private void AppendLog(string message)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _log.ScrollToEnd();
    }

    private void RefreshSessions()
    {
        _sessions.Items.Clear();
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atom", "Foundry");
            if (!Directory.Exists(root)) return;
            foreach (var directory in Directory.GetDirectories(root, "foundry_*").OrderByDescending(d => d).Take(15))
            {
                var label = Path.GetFileName(directory);
                try
                {
                    var manifestPath = Path.Combine(directory, "session.json");
                    if (File.Exists(manifestPath))
                    {
                        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                        var cycles = manifest.RootElement.TryGetProperty("cycleCount", out var c) ? c.GetInt32() : 0;
                        var events = manifest.RootElement.TryGetProperty("eventCount", out var e) ? e.GetInt32() : 0;
                        var activity = manifest.RootElement.TryGetProperty("activity", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                        var projection = ProjectionBadge(manifest.RootElement);
                        label = $"{label}\n{cycles} cycles · {events} events{(activity is null ? "" : $" · {activity}")}{projection}";
                    }
                }
                catch { }
                _sessions.Items.Add(new ListBoxItem { Content = label, Tag = directory });
            }
        }
        catch { }
    }

    private static string ProjectionBadge(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("targetCaptureSummary", out var summaries) || summaries.ValueKind != JsonValueKind.Array)
            return "";
        var values = summaries.EnumerateArray().ToArray();
        if (values.Length == 0) return "";
        var projected = values.All(value => value.TryGetProperty("projectedFrames", out var count) && count.GetInt32() > 0);
        var negatives = values.All(value => value.TryGetProperty("negativeFrames", out var count) && count.GetInt32() > 0);
        return projected && negatives ? " · dataset coverage ✓" : projected ? " · negatives ⚠" : " · projections ⚠";
    }

    private void OpenSelectedSession()
    {
        if (_sessions.SelectedItem is ListBoxItem { Tag: string directory } && Directory.Exists(directory))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", directory); }
            catch (Exception ex) { AppendLog(ex.Message); }
        }
    }

    private static UIElement AtRow(UIElement element, int row)
    {
        Grid.SetRow(element, row);
        return element;
    }

    private static FrameworkElement Stretched(FrameworkElement element)
    {
        element.VerticalAlignment = VerticalAlignment.Stretch;
        return element;
    }

    private static TextBlock SectionHeader(string text)
    {
        var header = new TextBlock { Text = text, FontSize = 11, Margin = new(0, 0, 0, 6) };
        header.SetResourceReference(TextBlock.ForegroundProperty, "App.SubtleForegroundBrush");
        return header;
    }

    private static FrameworkElement Labeled(string label, UIElement element)
    {
        var panel = new DockPanel();
        var text = new TextBlock { Text = label.ToUpperInvariant(), FontSize = 11, Margin = new(0, 0, 0, 2) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "App.SubtleForegroundBrush");
        DockPanel.SetDock(text, Dock.Top);
        panel.Children.Add(text);
        panel.Children.Add(element);
        return panel;
    }

    private static Border Card(params UIElement[] children)
    {
        FrameworkElement content;
        if (children.Length == 1 && children[0] is FrameworkElement single)
        {
            content = single;
        }
        else
        {
            // Last child stretches so star-sized cards (log, sessions) fill their space.
            var grid = new Grid();
            for (var index = 0; index < children.Length; index++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = index == children.Length - 1 ? new(1, GridUnitType.Star) : GridLength.Auto });
                Grid.SetRow(children[index], index);
                grid.Children.Add(children[index]);
            }
            content = grid;
        }
        var card = new Border { Padding = new(12), CornerRadius = new(6), Margin = new(0, 12, 0, 0), BorderThickness = new(1), Child = content };
        card.SetResourceReference(Border.BackgroundProperty, "App.CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "App.BorderBrush");
        return card;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>One editable target-class binding with a live presence/projection readout.</summary>
    private sealed class TargetRow : Grid
    {
        private readonly TextBox _className = new() { MinWidth = 140, Margin = new(0, 0, 8, 0) };
        private readonly ComboBox _kind = new() { Width = 84, Margin = new(0, 0, 8, 0) };
        private readonly TextBox _id = new() { Width = 70, Margin = new(0, 0, 8, 0) };
        private readonly Ellipse _dot = new() { Width = 9, Height = 9, Fill = UnknownBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) };
        private readonly TextBlock _liveStatus = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly CheckBox _absent = new() { Content = "Label absent", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0), IsEnabled = false, ToolTip = "While recording, check only when this target is definitely not visible. Keep it checked for several seconds to capture negative frames." };
        private readonly Button _remove = new() { Content = "✕", Width = 26, ToolTip = "Remove target class" };

        public event Action? RemoveRequested;
        public event Action<string, bool>? AbsentChanged;
        public event Action? BindingChanged;

        public string ClassName => _className.Text.Trim();

        public TargetRow(FoundryTargetClass target)
        {
            Margin = new(0, 2, 0, 2);
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _kind.Items.Add("npc"); _kind.Items.Add("object");
            _className.Text = target.ClassName;
            _kind.SelectedItem = string.Equals(target.Kind, "object", StringComparison.OrdinalIgnoreCase) ? "object" : "npc";
            _id.Text = target.Id > 0 ? target.Id.ToString() : "";
            _className.ToolTip = "Dataset class name, e.g. bank-target";
            _id.ToolTip = "Entity id to project (NPC id or object id)";
            _liveStatus.SetResourceReference(TextBlock.ForegroundProperty, "App.SubtleForegroundBrush");

            SetColumn(_className, 0); SetColumn(_kind, 1); SetColumn(_id, 2);
            SetColumn(_dot, 3); SetColumn(_liveStatus, 4); SetColumn(_absent, 5); SetColumn(_remove, 6);
            Children.Add(_className); Children.Add(_kind); Children.Add(_id);
            Children.Add(_dot); Children.Add(_liveStatus); Children.Add(_absent); Children.Add(_remove);

            _remove.Click += (_, _) => RemoveRequested?.Invoke();
            _absent.Checked += (_, _) => RaiseAbsent(true);
            _absent.Unchecked += (_, _) => RaiseAbsent(false);
            _className.TextChanged += (_, _) => BindingChanged?.Invoke();
            _kind.SelectionChanged += (_, _) => BindingChanged?.Invoke();
            _id.TextChanged += (_, _) => BindingChanged?.Invoke();
        }

        public void ShowStatus(bool? present, string text)
        {
            _dot.Fill = present switch { true => PresentBrush, false => MissingBrush, null => UnknownBrush };
            _liveStatus.Text = text;
        }

        private void RaiseAbsent(bool absent)
        {
            if (!string.IsNullOrEmpty(ClassName)) AbsentChanged?.Invoke(ClassName, absent);
        }

        public FoundryTargetClass ToTarget() => new()
        {
            ClassName = ClassName,
            Kind = _kind.SelectedItem as string ?? "npc",
            Id = int.TryParse(_id.Text.Trim(), out var id) ? id : 0,
        };

        public void SetRecording(bool recording)
        {
            _className.IsEnabled = !recording;
            _kind.IsEnabled = !recording;
            _id.IsEnabled = !recording;
            _remove.IsEnabled = !recording;
            _absent.IsEnabled = recording;
        }

        public void ResetAbsent() => _absent.IsChecked = false;
    }
}
