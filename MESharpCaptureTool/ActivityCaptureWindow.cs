using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MESharp.Capture;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace MESharp;

/// <summary>Capture creation, durable session management, timeline review, and report inspection.</summary>
internal sealed class ActivityCaptureWindow : Window
{
    private static readonly Color BgWindow = Color.FromRgb(18, 20, 24);
    private static readonly Color BgCard = Color.FromRgb(28, 32, 40);
    private static readonly Color BgField = Color.FromRgb(38, 43, 54);
    private static readonly Color BorderCol = Color.FromRgb(52, 58, 72);
    private static readonly Color AccentBlue = Color.FromRgb(66, 165, 245);
    private static readonly Color AccentGreen = Color.FromRgb(102, 187, 106);
    private static readonly Color AccentRed = Color.FromRgb(239, 83, 80);
    private static readonly Color AccentOrange = Color.FromRgb(255, 167, 38);
    private static readonly Color TextDim = Color.FromRgb(148, 155, 170);
    private static readonly FontFamily Mono = new("Consolas");
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    private readonly TextBox _activity = Field("dungeoneering.floor_rush", 210, "Stable activity identifier");
    private readonly TextBox _profile = Field("dg-rush", 115, "Capture/analyzer profile");
    private readonly ComboBox _source = Combo(Enum.GetValues<CaptureRunSource>(), CaptureRunSource.Human, 100);
    private readonly ComboBox _privacy = Combo(Enum.GetValues<CapturePrivacyPreset>(), CapturePrivacyPreset.Balanced, 105);
    private readonly TextBox _scriptId = Field(string.Empty, 150, "Optional loaded script id");
    private readonly CheckBox _screenshots = new() { Content = "Screenshots", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _abort;
    private readonly Ellipse _statusDot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statusPillText = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _statusDetail = new() { Foreground = new SolidColorBrush(TextDim), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock _packsValue = StatValue();
    private readonly TextBlock _eventsValue = StatValue();
    private readonly TextBlock _framesValue = StatValue();
    private readonly TextBlock _storageValue = StatValue();

    private readonly TextBox _sessionSearch = Field(string.Empty, 230, "Filter by id, activity, profile, or outcome");
    private readonly ListView _sessions = new();
    private readonly TextBlock _sessionTitle = new() { Text = "Select a capture", FontSize = 18, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _sessionSubtitle = new() { Foreground = new SolidColorBrush(TextDim), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sessionMetadata = new() { FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sessionReason = new() { Foreground = new SolidColorBrush(TextDim), TextWrapping = TextWrapping.Wrap };
    private readonly Button _openSession;
    private readonly Button _copySessionId;
    private readonly Button _copySessionPath;
    private readonly Button _deleteSession;

    private readonly TextBox _eventSearch = Field(string.Empty, 210, "Filter event kind or source");
    private readonly ListView _events = new();
    private readonly Slider _timeline = new() { Minimum = 0, IsSnapToTickEnabled = false, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _timelinePosition = new() { Foreground = new SolidColorBrush(TextDim), FontFamily = Mono, FontSize = 11 };
    private readonly TextBox _eventDetail = new() { IsReadOnly = true, FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true };
    private readonly Image _frame = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _frameHint = new() { Text = "No frame for this event", Foreground = new SolidColorBrush(TextDim), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    private readonly ListView _notes = new();
    private readonly TextBox _noteAt = Field("0", 82, "Timeline position in milliseconds");
    private readonly TextBox _noteLabel = Field(string.Empty, 110, "Optional short label");
    private readonly TextBox _noteText = Field(string.Empty, double.NaN, "Review note");
    private readonly ListView _exclusions = new();
    private readonly TextBox _excludeStart = Field("0", 82, "Start in milliseconds");
    private readonly TextBox _excludeEnd = Field("0", 82, "End in milliseconds");
    private readonly TextBox _excludeReason = Field(string.Empty, double.NaN, "Why this range should be ignored");
    private readonly ComboBox _correctedOutcome = new() { MinWidth = 175 };
    private readonly TextBlock _reviewSummary = new() { Foreground = new SolidColorBrush(TextDim), TextWrapping = TextWrapping.Wrap };

    private readonly TextBox _report = new() { IsReadOnly = true, FontFamily = Mono, FontSize = 12, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap };
    private readonly TextBlock _statusBar = new() { Foreground = new SolidColorBrush(TextDim), FontSize = 11 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TabControl _tabs = new();
    private TabItem? _timelineTab;
    private TabItem? _reportTab;

    private IReadOnlyList<CaptureSessionInfo> _allSessions = Array.Empty<CaptureSessionInfo>();
    private IReadOnlyList<CaptureEvent> _loadedEvents = Array.Empty<CaptureEvent>();
    private CaptureSessionInfo? _selectedSession;
    private CaptureReview? _review;

    public ActivityCaptureWindow()
    {
        Title = "MESharp Activity Capture";
        Width = 1380;
        Height = 900;
        MinWidth = 1080;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(BgWindow);
        Foreground = Brushes.WhiteSmoke;

        _start = MakeButton("Start Capture", (_, _) => StartCapture(), AccentGreen);
        _stop = MakeButton("Stop / Success", (_, _) => StopCapture(CaptureOutcome.Succeeded), AccentBlue);
        _abort = MakeButton("Abort", (_, _) => StopCapture(CaptureOutcome.Aborted), AccentRed);
        _openSession = MakeButton("Open Folder", (_, _) => OpenSelectedSession());
        _copySessionId = MakeButton("Copy ID", (_, _) => CopySelectedSessionId());
        _copySessionPath = MakeButton("Copy Path", (_, _) => CopySelectedSessionPath());
        _deleteSession = MakeButton("Delete Capture", (_, _) => DeleteSelectedSession(), AccentRed);

        _correctedOutcome.Items.Add("Use captured outcome");
        foreach (var outcome in Enum.GetValues<CaptureOutcome>().Where(value => value != CaptureOutcome.Recording))
            _correctedOutcome.Items.Add(outcome);
        _correctedOutcome.SelectedIndex = 0;

        Resources.MergedDictionaries.Add(BuildSharedStyles());
        ConfigureLists();
        Content = BuildLayout();
        WireEvents();

        Loaded += (_, _) => RefreshAll(selectNewestWhenEmpty: true);
        Closed += (_, _) => _timer.Stop();
    }

    private UIElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Add(root, BuildHeader(), 0);
        Add(root, BuildCaptureCard(), 1);
        Add(root, BuildStatCards(), 2);
        Add(root, BuildTabs(), 3);
        _statusBar.Margin = new Thickness(2, 8, 2, 0);
        Add(root, _statusBar, 4);
        return root;
    }

    private UIElement BuildHeader()
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(MakeButton("Privacy Preview", (_, _) => PreviewPrivacy()));
        right.Children.Add(MakeButton("Open Capture Root", (_, _) => OpenFolder(ActivityCapture.CapturesRoot)));
        DockPanel.SetDock(right, Dock.Right);
        header.Children.Add(right);

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock { Text = "Activity Capture", FontSize = 22, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var pillContent = new StackPanel { Orientation = Orientation.Horizontal };
        _statusDot.Margin = new Thickness(0, 0, 7, 0);
        pillContent.Children.Add(_statusDot);
        pillContent.Children.Add(_statusPillText);
        left.Children.Add(new Border
        {
            Background = new SolidColorBrush(BgCard), BorderBrush = new SolidColorBrush(BorderCol), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(11, 5, 11, 5), Margin = new Thickness(16, 0, 10, 0), Child = pillContent
        });
        left.Children.Add(_statusDetail);
        header.Children.Add(left);
        return header;
    }

    private UIElement BuildCaptureCard()
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fields = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        fields.Children.Add(LabeledField("ACTIVITY", _activity));
        fields.Children.Add(LabeledField("PROFILE", _profile));
        fields.Children.Add(LabeledField("RUN SOURCE", _source));
        fields.Children.Add(LabeledField("PRIVACY", _privacy));
        fields.Children.Add(LabeledField("SCRIPT ID", _scriptId));
        _screenshots.Margin = new Thickness(4, 18, 10, 0);
        fields.Children.Add(_screenshots);
        content.Children.Add(fields);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        actions.Children.Add(_start);
        actions.Children.Add(_stop);
        actions.Children.Add(_abort);
        Grid.SetColumn(actions, 1);
        content.Children.Add(actions);

        var helper = new TextBlock
        {
            Text = "Start before playing manually or running a script. Balanced privacy masks chat and redacts the local player identity.",
            Foreground = new SolidColorBrush(TextDim), FontSize = 11, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(helper, 1);
        Grid.SetColumnSpan(helper, 2);
        content.Children.Add(helper);
        return Card(content, new Thickness(0, 0, 0, 10), new Thickness(12, 10, 12, 10));
    }

    private UIElement BuildStatCards()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddStatCard(grid, 0, "CAPTURE PACKS", _packsValue);
        AddStatCard(grid, 1, "TOTAL EVENTS", _eventsValue);
        AddStatCard(grid, 2, "SCREENSHOTS", _framesValue);
        AddStatCard(grid, 3, "DISK USAGE", _storageValue);
        return grid;
    }

    private UIElement BuildTabs()
    {
        _tabs.Background = Brushes.Transparent;
        _tabs.BorderThickness = new Thickness(0);
        _tabs.Padding = new Thickness(0, 6, 0, 0);
        _tabs.Items.Add(new TabItem { Header = "Capture Library", Content = BuildLibraryTab() });
        _timelineTab = new TabItem { Header = "Timeline & Review", Content = BuildTimelineTab() };
        _reportTab = new TabItem { Header = "Analysis Report", Content = BuildReportTab() };
        _tabs.Items.Add(_timelineTab);
        _tabs.Items.Add(_reportTab);
        return _tabs;
    }

    private UIElement BuildLibraryTab()
    {
        var layout = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        toolbar.Children.Add(ToolbarLabel("Filter"));
        toolbar.Children.Add(_sessionSearch);
        toolbar.Children.Add(MakeButton("Refresh", (_, _) => RefreshAll()));
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.65, GridUnitType.Star), MinWidth = 570 });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
        split.Children.Add(TitledPanel("Capture packs (newest first)", _sessions));

        var details = new StackPanel();
        details.Children.Add(_sessionTitle);
        _sessionSubtitle.Margin = new Thickness(0, 3, 0, 12);
        details.Children.Add(_sessionSubtitle);
        details.Children.Add(SectionCaption("SESSION METADATA"));
        details.Children.Add(_sessionMetadata);
        details.Children.Add(SectionCaption("STOP REASON"));
        details.Children.Add(_sessionReason);
        var primaryActions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        primaryActions.Children.Add(_openSession);
        primaryActions.Children.Add(_copySessionId);
        primaryActions.Children.Add(_copySessionPath);
        details.Children.Add(primaryActions);
        var destructive = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        destructive.Children.Add(SectionCaption("DESTRUCTIVE ACTION"));
        destructive.Children.Add(_deleteSession);
        details.Children.Add(destructive);
        var detailsCard = Card(details, new Thickness(0), new Thickness(16));
        Grid.SetColumn(detailsCard, 2);
        split.Children.Add(detailsCard);
        layout.Children.Add(split);
        return layout;
    }

    private UIElement BuildTimelineTab()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 310 });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 235 });

        var upper = new Grid();
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star), MinWidth = 390 });
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        upper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star), MinWidth = 500 });
        var eventPanel = new DockPanel();
        var eventToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
        eventToolbar.Children.Add(ToolbarLabel("Filter"));
        eventToolbar.Children.Add(_eventSearch);
        DockPanel.SetDock(eventToolbar, Dock.Top);
        eventPanel.Children.Add(eventToolbar);
        var timelinePanel = new Grid();
        timelinePanel.RowDefinitions.Add(new RowDefinition());
        timelinePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        timelinePanel.Children.Add(_timeline);
        Grid.SetRow(_timelinePosition, 1);
        timelinePanel.Children.Add(_timelinePosition);
        DockPanel.SetDock(timelinePanel, Dock.Bottom);
        eventPanel.Children.Add(timelinePanel);
        eventPanel.Children.Add(_events);
        upper.Children.Add(TitledPanel("Observed events", eventPanel));

        var inspection = new Grid();
        inspection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.2, GridUnitType.Star) });
        inspection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
        inspection.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var frameGrid = new Grid { Background = new SolidColorBrush(Color.FromRgb(12, 14, 18)) };
        frameGrid.Children.Add(_frameHint);
        frameGrid.Children.Add(_frame);
        inspection.Children.Add(TitledPanel("Nearest captured frame", frameGrid));
        var detailPanel = TitledPanel("Selected event payload", _eventDetail);
        Grid.SetRow(detailPanel, 2);
        inspection.Children.Add(detailPanel);
        Grid.SetColumn(inspection, 2);
        upper.Children.Add(inspection);
        grid.Children.Add(upper);

        var review = new Grid();
        review.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
        review.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        review.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
        review.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        review.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.58, GridUnitType.Star), MinWidth = 235 });
        review.Children.Add(BuildNotesPanel());
        var exclusions = BuildExclusionsPanel();
        Grid.SetColumn(exclusions, 2);
        review.Children.Add(exclusions);
        var outcome = BuildReviewActionsPanel();
        Grid.SetColumn(outcome, 4);
        review.Children.Add(outcome);
        Grid.SetRow(review, 2);
        grid.Children.Add(review);
        return grid;
    }

    private UIElement BuildNotesPanel()
    {
        var panel = new DockPanel();
        var editor = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.Children.Add(LabeledField("AT MS", _noteAt));
        var label = LabeledField("LABEL", _noteLabel);
        Grid.SetColumn(label, 2);
        fields.Children.Add(label);
        editor.Children.Add(fields);
        _noteText.Margin = new Thickness(0, 6, 0, 0);
        editor.Children.Add(_noteText);
        var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        actions.Children.Add(MakeButton("New", (_, _) => ClearNoteEditor()));
        actions.Children.Add(MakeButton("Add / Update", (_, _) => SaveNote(), AccentBlue));
        actions.Children.Add(MakeButton("Delete", (_, _) => DeleteNote(), AccentRed));
        editor.Children.Add(actions);
        DockPanel.SetDock(editor, Dock.Bottom);
        panel.Children.Add(editor);
        panel.Children.Add(_notes);
        return TitledPanel("Review notes", panel);
    }

    private UIElement BuildExclusionsPanel()
    {
        var panel = new DockPanel();
        var editor = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var fields = new WrapPanel();
        fields.Children.Add(LabeledField("START MS", _excludeStart));
        fields.Children.Add(LabeledField("END MS", _excludeEnd));
        editor.Children.Add(fields);
        _excludeReason.Margin = new Thickness(0, 6, 0, 0);
        editor.Children.Add(_excludeReason);
        var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        actions.Children.Add(MakeButton("New", (_, _) => ClearExclusionEditor()));
        actions.Children.Add(MakeButton("Add / Update", (_, _) => SaveExclusion(), AccentBlue));
        actions.Children.Add(MakeButton("Delete", (_, _) => DeleteExclusion(), AccentRed));
        editor.Children.Add(actions);
        DockPanel.SetDock(editor, Dock.Bottom);
        panel.Children.Add(editor);
        panel.Children.Add(_exclusions);
        return TitledPanel("Excluded ranges", panel);
    }

    private UIElement BuildReviewActionsPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(SectionCaption("CORRECTED OUTCOME"));
        panel.Children.Add(_correctedOutcome);
        panel.Children.Add(MakeButton("Save Review", (_, _) => SaveReviewOutcome(), AccentBlue, new Thickness(0, 10, 0, 0)));
        panel.Children.Add(MakeButton("Re-run Analysis", (_, _) => Analyze(showReport: true), AccentGreen, new Thickness(0, 8, 0, 0)));
        _reviewSummary.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(_reviewSummary);
        return Card(panel, new Thickness(0), new Thickness(14));
    }

    private UIElement BuildReportTab()
    {
        var layout = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        toolbar.Children.Add(MakeButton("Re-run Analysis", (_, _) => Analyze(showReport: false), AccentGreen));
        toolbar.Children.Add(MakeButton("Copy Report", (_, _) => CopyText(_report.Text, "Report copied.")));
        toolbar.Children.Add(MakeButton("Open Analysis Folder", (_, _) => OpenAnalysisFolder()));
        toolbar.Children.Add(new TextBlock
        {
            Text = "Analysis is deterministic and applies the non-destructive review edits from the timeline.",
            Foreground = new SolidColorBrush(TextDim), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
        });
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        layout.Children.Add(TitledPanel("Dungeoneering rush report (JSON)", _report));
        return layout;
    }

    private void ConfigureLists()
    {
        _sessions.View = GridView(
            Column("Started", nameof(SessionRow.Started), 128), Column("Activity", nameof(SessionRow.Activity), 175),
            Column("Outcome", nameof(SessionRow.Outcome), 90), Column("Events", nameof(SessionRow.Events), 58),
            Column("Frames", nameof(SessionRow.Frames), 58), Column("Size", nameof(SessionRow.Size), 72));
        _sessions.ItemContainerStyle = BuildListItemStyle();

        _events.View = GridView(
            Column("At", nameof(EventRow.At), 82), Column("Source", nameof(EventRow.Source), 92), Column("Kind", nameof(EventRow.Kind), 210),
            Column("Frame", nameof(EventRow.Frame), 48));
        _events.ItemContainerStyle = BuildListItemStyle();

        _notes.View = GridView(Column("At", nameof(CaptureReviewNote.AtMs), 72), Column("Label", nameof(CaptureReviewNote.Label), 90), Column("Note", nameof(CaptureReviewNote.Text), 250));
        _notes.ItemContainerStyle = BuildListItemStyle();
        _exclusions.View = GridView(Column("Start", nameof(CaptureExcludedRange.StartMs), 72), Column("End", nameof(CaptureExcludedRange.EndMs), 72), Column("Reason", nameof(CaptureExcludedRange.Reason), 250));
        _exclusions.ItemContainerStyle = BuildListItemStyle();
    }

    private void WireEvents()
    {
        _sessionSearch.TextChanged += (_, _) => RefreshSessionFilter();
        _eventSearch.TextChanged += (_, _) => RefreshEventFilter();
        _sessions.SelectionChanged += (_, _) => SelectSession((_sessions.SelectedItem as SessionRow)?.Session);
        _sessions.MouseDoubleClick += (_, _) => { if (_selectedSession != null && _timelineTab != null) _tabs.SelectedItem = _timelineTab; };
        _events.SelectionChanged += (_, _) => SelectEvent((_events.SelectedItem as EventRow)?.Event);
        _timeline.ValueChanged += (_, _) => SelectNearestEvent((long)_timeline.Value);
        _notes.SelectionChanged += (_, _) => LoadSelectedNote();
        _exclusions.SelectionChanged += (_, _) => LoadSelectedExclusion();
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
    }

    private void StartCapture() => TryAction(() =>
    {
        var status = ActivityCapture.Start(new CaptureStartOptions
        {
            Activity = _activity.Text.Trim(), ProfileId = _profile.Text.Trim(),
            RunSource = (CaptureRunSource)(_source.SelectedItem ?? CaptureRunSource.Human),
            Privacy = (CapturePrivacyPreset)(_privacy.SelectedItem ?? CapturePrivacyPreset.Balanced),
            ScriptId = string.IsNullOrWhiteSpace(_scriptId.Text) ? null : _scriptId.Text.Trim(),
            CaptureScreenshots = _screenshots.IsChecked == true
        });
        SetStatus($"Capture started: {status.SessionId}");
        RefreshAll();
        if (!string.IsNullOrWhiteSpace(status.SessionId)) SelectSessionById(status.SessionId);
    });

    private void StopCapture(CaptureOutcome outcome) => TryAction(() =>
    {
        var session = ActivityCapture.Stop(outcome, outcome == CaptureOutcome.Aborted ? "Stopped manually from Activity Capture." : null);
        SetStatus($"Capture stopped with outcome {session.Outcome}.");
        RefreshAll();
        SelectSessionById(session.SessionId);
    });

    private void PreviewPrivacy() => TryAction(() =>
    {
        var path = ActivityCapture.CapturePrivacyPreview((CapturePrivacyPreset)(_privacy.SelectedItem ?? CapturePrivacyPreset.Balanced));
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("The game client did not return a screenshot.");
        ShowFrame(path);
        _eventDetail.Text = $"Privacy preview\n{path}";
        if (_timelineTab != null) _tabs.SelectedItem = _timelineTab;
        SetStatus("Privacy preview captured. Inspect the frame before recording sensitive activity.");
    });

    private void RefreshAll(bool selectNewestWhenEmpty = false)
    {
        RefreshStatus();
        var selectedId = _selectedSession?.SessionId;
        _allSessions = ActivityCapture.List(500);
        _packsValue.Text = _allSessions.Count.ToString("N0");
        _eventsValue.Text = _allSessions.Sum(session => (long)session.EventCount).ToString("N0");
        _framesValue.Text = _allSessions.Sum(session => (long)session.ScreenshotCount).ToString("N0");
        _storageValue.Text = FormatBytes(_allSessions.Sum(session => session.SizeBytes));
        RefreshSessionFilter();
        if (selectedId != null) SelectSessionById(selectedId);
        else if (selectNewestWhenEmpty && _sessions.Items.Count > 0) _sessions.SelectedIndex = 0;
    }

    private void RefreshStatus()
    {
        var status = ActivityCapture.GetStatus();
        _statusDot.Fill = new SolidColorBrush(status.IsRecording ? AccentGreen : TextDim);
        _statusPillText.Text = status.IsRecording ? "RECORDING" : "IDLE";
        _statusPillText.Foreground = new SolidColorBrush(status.IsRecording ? AccentGreen : TextDim);
        _statusDetail.Text = status.IsRecording
            ? $"{status.EventCount:N0} events  •  {status.ScreenshotCount:N0} frames  •  {FormatDuration(DateTime.UtcNow - status.StartedUtc!.Value)}"
            : "Ready to capture";
        if (!string.IsNullOrWhiteSpace(status.LastWarning)) _statusDetail.Text += $"  •  {status.LastWarning}";
        _start.IsEnabled = !status.IsRecording;
        _stop.IsEnabled = status.IsRecording;
        _abort.IsEnabled = status.IsRecording;
        _deleteSession.IsEnabled = _selectedSession != null && !string.Equals(_selectedSession.SessionId, status.SessionId, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshSessionFilter()
    {
        var selectedId = _selectedSession?.SessionId;
        var query = _sessionSearch.Text.Trim();
        var rows = _allSessions
            .Where(session => query.Length == 0 || SessionSearchText(session).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(session => new SessionRow(session)).ToArray();
        _sessions.ItemsSource = rows;
        if (selectedId != null)
            _sessions.SelectedItem = rows.FirstOrDefault(row => row.Session.SessionId.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectSessionById(string sessionId)
    {
        var row = _sessions.Items.Cast<SessionRow>().FirstOrDefault(item => item.Session.SessionId.Equals(sessionId, StringComparison.OrdinalIgnoreCase));
        if (row != null) _sessions.SelectedItem = row;
    }

    private void SelectSession(CaptureSessionInfo? session)
    {
        _selectedSession = session;
        if (session == null)
        {
            ClearSessionView();
            return;
        }

        TryAction(() =>
        {
            _sessionTitle.Text = session.Activity;
            _sessionSubtitle.Text = $"{session.ProfileId}  •  {session.Outcome}  •  {session.RunSource} run";
            var duration = (session.EndedUtc ?? DateTime.UtcNow) - session.StartedUtc;
            _sessionMetadata.Text =
                $"ID       {session.SessionId}\n" +
                $"Started  {session.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
                $"Duration {FormatDuration(duration)}\n" +
                $"Privacy  {session.Privacy}\n" +
                $"Events   {session.EventCount:N0}\n" +
                $"Frames   {session.ScreenshotCount:N0}\n" +
                $"Size     {FormatBytes(session.SizeBytes)}";
            _sessionReason.Text = string.IsNullOrWhiteSpace(session.Reason) ? "No stop reason recorded." : session.Reason;
            _loadedEvents = ActivityCapture.ReadEvents(session.SessionId, 0, 5000);
            _timeline.Maximum = Math.Max(1, _loadedEvents.LastOrDefault()?.ElapsedMs ?? 1);
            _timeline.Value = 0;
            RefreshEventFilter();
            LoadReview();
            RefreshReport();
            RefreshStatus();
        });
    }

    private void ClearSessionView()
    {
        _sessionTitle.Text = "Select a capture";
        _sessionSubtitle.Text = string.Empty;
        _sessionMetadata.Text = string.Empty;
        _sessionReason.Text = string.Empty;
        _loadedEvents = Array.Empty<CaptureEvent>();
        _events.ItemsSource = null;
        _notes.ItemsSource = null;
        _exclusions.ItemsSource = null;
        _review = null;
        _eventDetail.Clear();
        _report.Text = "Select a capture to inspect its report.";
        _frame.Source = null;
        _frameHint.Visibility = Visibility.Visible;
        RefreshStatus();
    }

    private void RefreshEventFilter()
    {
        var selectedSequence = (_events.SelectedItem as EventRow)?.Event.Sequence;
        var query = _eventSearch.Text.Trim();
        var rows = _loadedEvents
            .Where(item => query.Length == 0 || item.Kind.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Source.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(item => new EventRow(item)).ToArray();
        _events.ItemsSource = rows;
        if (selectedSequence.HasValue) _events.SelectedItem = rows.FirstOrDefault(row => row.Event.Sequence == selectedSequence.Value);
        _timelinePosition.Text = $"0 ms  /  {(long)_timeline.Maximum:N0} ms  •  {rows.Length:N0} visible of {_loadedEvents.Count:N0}";
    }

    private void SelectNearestEvent(long atMs)
    {
        _timelinePosition.Text = $"{atMs:N0} ms  /  {(long)_timeline.Maximum:N0} ms  •  {_events.Items.Count:N0} visible of {_loadedEvents.Count:N0}";
        if (_loadedEvents.Count == 0) return;
        var nearest = _loadedEvents.MinBy(item => Math.Abs(item.ElapsedMs - atMs));
        if (nearest == null) return;
        var row = _events.Items.Cast<EventRow>().FirstOrDefault(item => item.Event.Sequence == nearest.Sequence);
        if (row != null && !ReferenceEquals(_events.SelectedItem, row)) _events.SelectedItem = row;
    }

    private void SelectEvent(CaptureEvent? item)
    {
        if (item == null) return;
        if (Math.Abs(_timeline.Value - item.ElapsedMs) > 0.5) _timeline.Value = item.ElapsedMs;
        _noteAt.Text = item.ElapsedMs.ToString();
        _excludeStart.Text = item.ElapsedMs.ToString();
        _excludeEnd.Text = item.ElapsedMs.ToString();
        _eventDetail.Text = $"#{item.Sequence}  {item.ElapsedMs:N0} ms  {item.Source} / {item.Kind}\n\n{JsonSerializer.Serialize(item.Payload, PrettyJson)}";
        _frame.Source = null;
        _frameHint.Visibility = Visibility.Visible;
        if (_selectedSession == null || string.IsNullOrWhiteSpace(item.Screenshot)) return;
        var path = Path.Combine(_selectedSession.Directory, item.Screenshot.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path)) ShowFrame(path);
    }

    private void ShowFrame(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        _frame.Source = bitmap;
        _frameHint.Visibility = Visibility.Collapsed;
    }

    private void LoadReview()
    {
        if (_selectedSession == null) return;
        _review = ActivityCapture.GetReview(_selectedSession.SessionId);
        RefreshReviewLists();
        _correctedOutcome.SelectedItem = _review.CorrectedOutcome is { } outcome ? outcome : "Use captured outcome";
    }

    private void RefreshReviewLists()
    {
        _notes.ItemsSource = null;
        _notes.ItemsSource = _review?.Notes.OrderBy(note => note.AtMs).ToArray();
        _exclusions.ItemsSource = null;
        _exclusions.ItemsSource = _review?.ExcludedRanges.OrderBy(range => range.StartMs).ToArray();
        _reviewSummary.Text = _review == null
            ? "Select a capture to edit review data."
            : $"{_review.Notes.Count} note(s)\n{_review.ExcludedRanges.Count} excluded range(s)\nOriginal events remain unchanged.";
    }

    private void ClearNoteEditor()
    {
        _notes.SelectedItem = null;
        _noteAt.Text = ((long)_timeline.Value).ToString();
        _noteLabel.Clear();
        _noteText.Clear();
        _noteText.Focus();
    }

    private void LoadSelectedNote()
    {
        if (_notes.SelectedItem is not CaptureReviewNote note) return;
        _noteAt.Text = note.AtMs.ToString();
        _noteLabel.Text = note.Label ?? string.Empty;
        _noteText.Text = note.Text;
        _timeline.Value = note.AtMs;
    }

    private void SaveNote() => TryAction(() =>
    {
        EnsureReview();
        if (!long.TryParse(_noteAt.Text, out var atMs) || atMs < 0) throw new ArgumentException("Note time must be a non-negative millisecond value.");
        if (string.IsNullOrWhiteSpace(_noteText.Text)) throw new ArgumentException("Note text is required.");
        var replacement = new CaptureReviewNote { AtMs = atMs, Label = NullIfWhiteSpace(_noteLabel.Text), Text = _noteText.Text.Trim() };
        if (_notes.SelectedItem is CaptureReviewNote selected) _review!.Notes[_review.Notes.IndexOf(selected)] = replacement;
        else _review!.Notes.Add(replacement);
        PersistReview("Review note saved.");
        ClearNoteEditor();
    });

    private void DeleteNote() => TryAction(() =>
    {
        EnsureReview();
        if (_notes.SelectedItem is not CaptureReviewNote selected) throw new InvalidOperationException("Select a note to delete.");
        _review!.Notes.Remove(selected);
        PersistReview("Review note deleted.");
        ClearNoteEditor();
    });

    private void ClearExclusionEditor()
    {
        _exclusions.SelectedItem = null;
        var at = ((long)_timeline.Value).ToString();
        _excludeStart.Text = at;
        _excludeEnd.Text = at;
        _excludeReason.Clear();
        _excludeReason.Focus();
    }

    private void LoadSelectedExclusion()
    {
        if (_exclusions.SelectedItem is not CaptureExcludedRange range) return;
        _excludeStart.Text = range.StartMs.ToString();
        _excludeEnd.Text = range.EndMs.ToString();
        _excludeReason.Text = range.Reason;
        _timeline.Value = range.StartMs;
    }

    private void SaveExclusion() => TryAction(() =>
    {
        EnsureReview();
        if (!long.TryParse(_excludeStart.Text, out var start) || !long.TryParse(_excludeEnd.Text, out var end) || start < 0 || end < 0)
            throw new ArgumentException("Exclusion start and end must be non-negative millisecond values.");
        var replacement = new CaptureExcludedRange
        {
            StartMs = Math.Min(start, end), EndMs = Math.Max(start, end),
            Reason = string.IsNullOrWhiteSpace(_excludeReason.Text) ? "Excluded during timeline review" : _excludeReason.Text.Trim()
        };
        if (_exclusions.SelectedItem is CaptureExcludedRange selected) _review!.ExcludedRanges[_review.ExcludedRanges.IndexOf(selected)] = replacement;
        else _review!.ExcludedRanges.Add(replacement);
        PersistReview("Excluded range saved.");
        ClearExclusionEditor();
    });

    private void DeleteExclusion() => TryAction(() =>
    {
        EnsureReview();
        if (_exclusions.SelectedItem is not CaptureExcludedRange selected) throw new InvalidOperationException("Select an excluded range to delete.");
        _review!.ExcludedRanges.Remove(selected);
        PersistReview("Excluded range deleted.");
        ClearExclusionEditor();
    });

    private void SaveReviewOutcome() => TryAction(() =>
    {
        EnsureReview();
        _review!.CorrectedOutcome = _correctedOutcome.SelectedItem is CaptureOutcome outcome ? outcome : null;
        PersistReview("Corrected outcome saved.");
    });

    private void PersistReview(string message)
    {
        EnsureReview();
        _review = ActivityCapture.SaveReview(_selectedSession!.SessionId, _review!);
        RefreshReviewLists();
        Analyze(showReport: false);
        SetStatus(message);
    }

    private void Analyze(bool showReport) => TryAction(() =>
    {
        if (_selectedSession == null) throw new InvalidOperationException("Select a capture to analyze.");
        var result = DgRushAnalyzer.Analyze(_selectedSession.SessionId);
        _report.Text = JsonSerializer.Serialize(result, PrettyJson);
        if (showReport && _reportTab != null) _tabs.SelectedItem = _reportTab;
        SetStatus("Analysis report regenerated.");
    });

    private void RefreshReport()
    {
        if (_selectedSession == null) return;
        var result = DgRushAnalyzer.GetReport(_selectedSession.SessionId);
        _report.Text = result == null ? "No report generated yet. Use Re-run Analysis." : JsonSerializer.Serialize(result, PrettyJson);
    }

    private void OpenSelectedSession()
    {
        if (_selectedSession != null) OpenFolder(_selectedSession.Directory);
    }

    private void OpenAnalysisFolder()
    {
        if (_selectedSession == null) { SetStatus("Select a capture first."); return; }
        OpenFolder(Path.Combine(_selectedSession.Directory, "analysis"));
    }

    private void CopySelectedSessionId()
    {
        if (_selectedSession != null) CopyText(_selectedSession.SessionId, "Session id copied.");
    }

    private void CopySelectedSessionPath()
    {
        if (_selectedSession != null) CopyText(_selectedSession.Directory, "Session path copied.");
    }

    private void DeleteSelectedSession()
    {
        if (_selectedSession == null) return;
        var target = _selectedSession;
        var answer = MessageBox.Show(this,
            $"Permanently delete capture '{target.SessionId}'?\n\nThis removes its events, frames, review, and analysis files.",
            "Delete Activity Capture", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        TryAction(() =>
        {
            DeleteCapturePack(target);
            _selectedSession = null;
            ClearSessionView();
            RefreshAll(selectNewestWhenEmpty: true);
            SetStatus($"Deleted capture {target.SessionId}.");
        });
    }

    private static void DeleteCapturePack(CaptureSessionInfo target)
    {
        var status = ActivityCapture.GetStatus();
        if (status.IsRecording && string.Equals(status.SessionId, target.SessionId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stop the active capture before deleting it.");

        if (string.IsNullOrWhiteSpace(target.SessionId) ||
            target.SessionId.Contains("..", StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(target.SessionId), target.SessionId, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected capture has an invalid session id.");

        var root = Path.GetFullPath(ActivityCapture.CapturesRoot) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(Path.Combine(ActivityCapture.CapturesRoot, target.SessionId));
        if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(directory, "manifest.json")))
            throw new InvalidOperationException("The selected capture is outside the capture root or no longer exists.");

        Directory.Delete(directory, recursive: true);
    }

    private void EnsureReview()
    {
        if (_selectedSession == null || _review == null) throw new InvalidOperationException("Select a capture first.");
    }

    private void OpenFolder(string path) => TryAction(() =>
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Folder not found: {path}");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        SetStatus($"Opened {path}");
    });

    private void CopyText(string text, string confirmation) => TryAction(() =>
    {
        Clipboard.SetText(text ?? string.Empty);
        SetStatus(confirmation);
    });

    private void TryAction(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            MessageBox.Show(this, ex.Message, "Activity Capture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetStatus(string message) => _statusBar.Text = $"{DateTime.Now:HH:mm:ss}  {message}";

    private static string SessionSearchText(CaptureSessionInfo session) =>
        $"{session.SessionId} {session.Activity} {session.ProfileId} {session.Outcome} {session.RunSource} {session.Privacy}";

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}h {value.Minutes:00}m {value.Seconds:00}s"
        : value.TotalMinutes >= 1 ? $"{value.Minutes}m {value.Seconds:00}s" : $"{Math.Max(0, value.TotalSeconds):0}s";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }

    private sealed class SessionRow
    {
        public SessionRow(CaptureSessionInfo session)
        {
            Session = session;
            Started = session.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            Activity = session.Activity;
            Outcome = session.Outcome.ToString();
            Events = session.EventCount.ToString("N0");
            Frames = session.ScreenshotCount.ToString("N0");
            Size = FormatBytes(session.SizeBytes);
        }
        public CaptureSessionInfo Session { get; }
        public string Started { get; }
        public string Activity { get; }
        public string Outcome { get; }
        public string Events { get; }
        public string Frames { get; }
        public string Size { get; }
    }

    private sealed class EventRow
    {
        public EventRow(CaptureEvent item)
        {
            Event = item;
            At = $"{item.ElapsedMs:N0} ms";
            Source = item.Source;
            Kind = item.Kind;
            Frame = string.IsNullOrWhiteSpace(item.Screenshot) ? string.Empty : "yes";
        }
        public CaptureEvent Event { get; }
        public string At { get; }
        public string Source { get; }
        public string Kind { get; }
        public string Frame { get; }
    }

    private static TextBox Field(string text, double width, string tooltip) => new()
    {
        Text = text, Width = width, ToolTip = tooltip
    };

    private static ComboBox Combo(Array values, object selected, double width) => new()
    {
        Width = width, ItemsSource = values, SelectedItem = selected
    };

    private static TextBlock StatValue() => new()
    {
        Text = "–", FontSize = 19, FontWeight = FontWeights.Bold, FontFamily = Mono, Foreground = Brushes.WhiteSmoke
    };

    private static void Add(Grid grid, UIElement child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private static void AddStatCard(Grid grid, int column, string caption, TextBlock value)
    {
        var content = new StackPanel { Margin = new Thickness(12, 9, 12, 9) };
        content.Children.Add(SectionCaption(caption));
        content.Children.Add(value);
        var card = Card(content, new Thickness(column == 0 ? 0 : 8, 0, 0, 0), new Thickness(0));
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private static Border Card(UIElement content, Thickness margin, Thickness padding) => new()
    {
        Background = new SolidColorBrush(BgCard), BorderBrush = new SolidColorBrush(BorderCol), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Margin = margin, Padding = padding, Child = content
    };

    private static FrameworkElement LabeledField(string caption, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        panel.Children.Add(SectionCaption(caption));
        panel.Children.Add(control);
        return panel;
    }

    private static TextBlock SectionCaption(string text) => new()
    {
        Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(TextDim), Margin = new Thickness(0, 0, 0, 4)
    };

    private static TextBlock ToolbarLabel(string text) => new()
    {
        Text = text, Foreground = new SolidColorBrush(TextDim), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
    };

    private static DockPanel TitledPanel(string title, UIElement content)
    {
        var panel = new DockPanel();
        var caption = new TextBlock { Text = title, Foreground = new SolidColorBrush(TextDim), FontSize = 11, Margin = new Thickness(2, 0, 0, 4) };
        DockPanel.SetDock(caption, Dock.Top);
        panel.Children.Add(caption);
        panel.Children.Add(content);
        return panel;
    }

    private static GridView GridView(params GridViewColumn[] columns)
    {
        var view = new GridView();
        foreach (var column in columns) view.Columns.Add(column);
        return view;
    }

    private static GridViewColumn Column(string header, string property, double width) => new()
    {
        Header = header, Width = width, DisplayMemberBinding = new Binding(property)
    };

    private static Button MakeButton(string text, RoutedEventHandler handler, Color? accent = null, Thickness? margin = null)
    {
        var button = new Button
        {
            Content = text, MinWidth = 82, Padding = new Thickness(12, 5, 12, 5), Margin = margin ?? new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(accent ?? BgField), Foreground = accent.HasValue ? Brushes.Black : Brushes.WhiteSmoke,
            FontWeight = accent.HasValue ? FontWeights.SemiBold : FontWeights.Normal
        };
        button.Click += handler;
        return button;
    }

    private static Style BuildListItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(ForegroundProperty, Brushes.WhiteSmoke));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, Mono));
        style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 3, 4, 3)));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        var selected = new Trigger { Property = ListViewItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromArgb(70, AccentBlue.R, AccentBlue.G, AccentBlue.B))));
        style.Triggers.Add(selected);
        return style;
    }

    private static ResourceDictionary BuildSharedStyles()
    {
        var dictionary = new ResourceDictionary();
        var buttonTemplate = new ControlTemplate(typeof(Button));
        var buttonBorder = new FrameworkElementFactory(typeof(Border));
        buttonBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        buttonBorder.SetBinding(Border.BackgroundProperty, ParentBinding(nameof(Background)));
        buttonBorder.SetBinding(Border.PaddingProperty, ParentBinding(nameof(Padding)));
        var buttonContent = new FrameworkElementFactory(typeof(ContentPresenter));
        buttonContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        buttonContent.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        buttonBorder.AppendChild(buttonContent);
        buttonTemplate.VisualTree = buttonBorder;
        var buttonStyle = new Style(typeof(Button));
        buttonStyle.Setters.Add(new Setter(TemplateProperty, buttonTemplate));
        buttonStyle.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(OpacityProperty, 0.82));
        buttonStyle.Triggers.Add(hover);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, 0.35));
        buttonStyle.Triggers.Add(disabled);
        dictionary.Add(typeof(Button), buttonStyle);

        var textBoxStyle = new Style(typeof(TextBox));
        textBoxStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(BgField)));
        textBoxStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.WhiteSmoke));
        textBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(BorderCol)));
        textBoxStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, Brushes.WhiteSmoke));
        textBoxStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        dictionary.Add(typeof(TextBox), textBoxStyle);

        var comboStyle = new Style(typeof(ComboBox));
        comboStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(BgField)));
        comboStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.WhiteSmoke));
        comboStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(BorderCol)));
        comboStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 3, 5, 3)));
        dictionary.Add(typeof(ComboBox), comboStyle);

        var comboItemStyle = new Style(typeof(ComboBoxItem));
        comboItemStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(BgField)));
        comboItemStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.WhiteSmoke));
        comboItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        var comboItemHover = new Trigger { Property = IsMouseOverProperty, Value = true };
        comboItemHover.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(AccentBlue)));
        comboItemHover.Setters.Add(new Setter(ForegroundProperty, Brushes.Black));
        comboItemStyle.Triggers.Add(comboItemHover);
        dictionary.Add(typeof(ComboBoxItem), comboItemStyle);

        var listStyle = new Style(typeof(ListView));
        listStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(BgCard)));
        listStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.WhiteSmoke));
        listStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(BorderCol)));
        listStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        dictionary.Add(typeof(ListView), listStyle);

        var headerTemplate = new ControlTemplate(typeof(GridViewColumnHeader));
        var headerBorder = new FrameworkElementFactory(typeof(Border));
        headerBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(BgField));
        headerBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(BorderCol));
        headerBorder.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 1));
        headerBorder.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
        var headerContent = new FrameworkElementFactory(typeof(ContentPresenter));
        headerContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        headerBorder.AppendChild(headerContent);
        headerTemplate.VisualTree = headerBorder;
        var headerStyle = new Style(typeof(GridViewColumnHeader));
        headerStyle.Setters.Add(new Setter(TemplateProperty, headerTemplate));
        headerStyle.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(TextDim)));
        headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        dictionary.Add(typeof(GridViewColumnHeader), headerStyle);

        var tabTemplate = new ControlTemplate(typeof(TabItem));
        var tabBorder = new FrameworkElementFactory(typeof(Border));
        tabBorder.Name = "TabBorder";
        tabBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        tabBorder.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2));
        tabBorder.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        tabBorder.SetValue(Border.PaddingProperty, new Thickness(14, 6, 14, 7));
        tabBorder.SetValue(Border.MarginProperty, new Thickness(0, 0, 4, 0));
        var tabContent = new FrameworkElementFactory(typeof(ContentPresenter));
        tabContent.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        tabBorder.AppendChild(tabContent);
        tabTemplate.VisualTree = tabBorder;
        var tabSelected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        tabSelected.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(AccentBlue), "TabBorder"));
        tabSelected.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        tabTemplate.Triggers.Add(tabSelected);
        var tabStyle = new Style(typeof(TabItem));
        tabStyle.Setters.Add(new Setter(TemplateProperty, tabTemplate));
        tabStyle.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(TextDim)));
        tabStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        tabStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        dictionary.Add(typeof(TabItem), tabStyle);
        return dictionary;
    }

    private static Binding ParentBinding(string property) => new(property)
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
    };
}
