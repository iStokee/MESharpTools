using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MESharp.Services;
using Binding = System.Windows.Data.Binding;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListView = System.Windows.Controls.ListView;
using ListViewItem = System.Windows.Controls.ListViewItem;
using Orientation = System.Windows.Controls.Orientation;
using TabControl = System.Windows.Controls.TabControl;
using TextBox = System.Windows.Controls.TextBox;

namespace csharp_interop.McpTools
{
    /// <summary>
    /// Comprehensive dashboard for the in-process MCP bridge: live status, request activity,
    /// tool catalog browser, and runtime log viewer.
    /// </summary>
    public sealed class McpDashboardWindow : Window
    {
        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color BgWindow = Color.FromRgb(18, 20, 24);
        private static readonly Color BgCard = Color.FromRgb(28, 32, 40);
        private static readonly Color BgField = Color.FromRgb(38, 43, 54);
        private static readonly Color BorderCol = Color.FromRgb(52, 58, 72);
        private static readonly Color AccentBlue = Color.FromRgb(66, 165, 245);
        private static readonly Color AccentGreen = Color.FromRgb(102, 187, 106);
        private static readonly Color AccentRed = Color.FromRgb(239, 83, 80);
        private static readonly Color AccentOrange = Color.FromRgb(255, 167, 38);
        private static readonly Color AccentYellow = Color.FromRgb(255, 213, 79);
        private static readonly Color TextDim = Color.FromRgb(148, 155, 170);

        private static readonly FontFamily Mono = new("Consolas");

        // ── Header / status ───────────────────────────────────────────────────
        private readonly Ellipse _statusDot = new() { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _statusPillText = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        private readonly Border _clientChip = new();
        private readonly TextBlock _clientChipText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
        private readonly Button _startBtn;
        private readonly Button _stopBtn;
        private readonly CheckBox _autoRefreshCheck = new() { Content = "Auto-refresh", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

        // ── Stat cards ────────────────────────────────────────────────────────
        private readonly TextBlock _reqValue = StatValue();
        private readonly TextBlock _failValue = StatValue();
        private readonly TextBlock _avgValue = StatValue();
        private readonly TextBlock _connValue = StatValue();
        private readonly TextBlock _uptimeValue = StatValue();
        private readonly TextBlock _lastCmdValue = StatValue(13);

        // ── Overview ──────────────────────────────────────────────────────────
        private readonly TextBlock _pipeValue = InfoValue();
        private readonly TextBlock _pidValue = InfoValue();
        private readonly TextBlock _ownerValue = InfoValue();
        private readonly TextBlock _listeningSinceValue = InfoValue();
        private readonly TextBlock _clientValue = InfoValue();
        private readonly TextBlock _autostartValue = InfoValue();
        private readonly TextBlock _settingsPathValue = InfoValue();
        private readonly TextBlock _serverPathValue = InfoValue();
        private readonly TextBlock _gameApiValue = InfoValue();
        private readonly TextBlock _catalogSummaryValue = InfoValue();
        private readonly TextBox _connectSnippetBox = new();

        // ── Activity ──────────────────────────────────────────────────────────
        private readonly ListView _activityList = new();
        private readonly ListView _commandStatsList = new();
        private readonly TextBox _activityFilter = new() { Width = 220 };
        private readonly CheckBox _errorsOnlyCheck = new() { Content = "Errors only", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        private long _lastSeenSequence = -1;
        private bool _activityFilterDirty = true;

        // ── Tools ─────────────────────────────────────────────────────────────
        private readonly ListView _toolsList = new();
        private readonly TextBox _toolsSearch = new() { Width = 220 };
        private readonly ComboBox _toolsCategory = new() { Width = 160, Margin = new Thickness(10, 0, 0, 0) };
        private readonly ComboBox _toolsSafety = new() { Width = 140, Margin = new Thickness(10, 0, 0, 0) };
        private readonly TextBlock _toolsCount = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), FontSize = 11 };
        private IReadOnlyList<McpToolInfo> _toolCatalog = Array.Empty<McpToolInfo>();

        // ── Logs ──────────────────────────────────────────────────────────────
        private readonly TextBox _logsBox = new();
        private readonly TextBox _logsFilter = new() { Width = 220 };
        private readonly CheckBox _tailCheck = new() { Content = "Follow tail", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        private readonly List<RuntimeLogLine> _logCache = new();
        private int _logFromIndex;
        private bool _logsDirty = true;

        // ── Knowledge ─────────────────────────────────────────────────────────
        private readonly McpKnowledgePanel _knowledgePanel;
        private TabControl? _tabs;
        private TabItem? _knowledgeTab;

        // ── Shared state ──────────────────────────────────────────────────────
        private readonly TextBlock _statusBar = new() { FontSize = 11 };
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        // Overview config (settings file, server path, connect snippet, game version) is near-static
        // but resolving it touches the filesystem (File.Exists + ReadAllText + JSON parse). Refresh it
        // only every Nth tick instead of every second so the UI thread isn't doing per-second disk I/O.
        private const int ConfigRefreshEveryTicks = 10;
        private int _configTickCounter;

        private static readonly object BridgeSync = new();
        private static McpRuntimeService? _dashboardOwnedBridge;

        public McpDashboardWindow()
        {
            Title = "MESharp MCP Dashboard";
            Width = 1240;
            Height = 820;
            MinWidth = 980;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(BgWindow);
            Foreground = Brushes.WhiteSmoke;

            _startBtn = MakeButton("Start Bridge", (_, _) => StartBridge(), AccentGreen);
            _stopBtn = MakeButton("Stop Bridge", (_, _) => StopBridge(), AccentRed);
            _knowledgePanel = new McpKnowledgePanel(SetStatus);

            Resources.MergedDictionaries.Add(BuildSharedStyles());
            Content = BuildLayout();

            _timer.Tick += (_, _) =>
            {
                if (_autoRefreshCheck.IsChecked == true)
                {
                    // Cheap dynamic refresh every tick; throttle the filesystem-backed config refresh.
                    var refreshConfig = _configTickCounter++ % ConfigRefreshEveryTicks == 0;
                    RefreshAll(refreshConfig);
                }
            };

            Loaded += (_, _) =>
            {
                LoadToolCatalog();
                RefreshAll();
                _timer.Start();
            };
            Closed += (_, _) => _timer.Stop();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Layout
        // ═══════════════════════════════════════════════════════════════════════

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // stat cards
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // tabs
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // status bar

            var header = BuildHeader();
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var cards = BuildStatCards();
            Grid.SetRow(cards, 1);
            root.Children.Add(cards);

            var tabs = BuildTabs();
            Grid.SetRow(tabs, 2);
            root.Children.Add(tabs);

            _statusBar.Margin = new Thickness(2, 8, 2, 0);
            _statusBar.Foreground = new SolidColorBrush(TextDim);
            Grid.SetRow(_statusBar, 3);
            root.Children.Add(_statusBar);

            return root;
        }

        private UIElement BuildHeader()
        {
            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 12) };

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(_startBtn);
            right.Children.Add(_stopBtn);
            right.Children.Add(MakeButton("Refresh", (_, _) => RefreshAll()));
            _autoRefreshCheck.Foreground = Brushes.WhiteSmoke;
            right.Children.Add(_autoRefreshCheck);
            DockPanel.SetDock(right, Dock.Right);
            header.Children.Add(right);

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(new TextBlock
            {
                Text = "MCP Dashboard",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });

            // Status pill
            var pillContent = new StackPanel { Orientation = Orientation.Horizontal };
            _statusDot.Margin = new Thickness(0, 0, 7, 0);
            pillContent.Children.Add(_statusDot);
            pillContent.Children.Add(_statusPillText);
            var pill = new Border
            {
                Background = new SolidColorBrush(BgCard),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(11, 5, 11, 5),
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = pillContent
            };
            left.Children.Add(pill);

            // Client chip
            _clientChip.Background = new SolidColorBrush(BgCard);
            _clientChip.BorderBrush = new SolidColorBrush(BorderCol);
            _clientChip.BorderThickness = new Thickness(1);
            _clientChip.CornerRadius = new CornerRadius(12);
            _clientChip.Padding = new Thickness(10, 4, 10, 4);
            _clientChip.Margin = new Thickness(8, 0, 0, 0);
            _clientChip.VerticalAlignment = VerticalAlignment.Center;
            _clientChip.Child = _clientChipText;
            left.Children.Add(_clientChip);

            header.Children.Add(left);
            return header;
        }

        private UIElement BuildStatCards()
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            for (var i = 0; i < 6; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            AddCard(grid, 0, "REQUESTS", _reqValue);
            AddCard(grid, 1, "FAILED", _failValue);
            AddCard(grid, 2, "AVG DURATION", _avgValue);
            AddCard(grid, 3, "CONNECTIONS", _connValue);
            AddCard(grid, 4, "UPTIME", _uptimeValue);
            AddCard(grid, 5, "LAST COMMAND", _lastCmdValue);
            return grid;
        }

        private static void AddCard(Grid grid, int column, string caption, TextBlock value)
        {
            var stack = new StackPanel { Margin = new Thickness(12, 9, 12, 9) };
            stack.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextDim),
                Margin = new Thickness(0, 0, 0, 3)
            });
            stack.Children.Add(value);

            var card = new Border
            {
                Background = new SolidColorBrush(BgCard),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0),
                Child = stack
            };
            Grid.SetColumn(card, column);
            grid.Children.Add(card);
        }

        private static TextBlock StatValue(int fontSize = 19) => new()
        {
            Text = "–",
            FontSize = fontSize,
            FontWeight = FontWeights.Bold,
            FontFamily = Mono,
            Foreground = Brushes.WhiteSmoke,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        private UIElement BuildTabs()
        {
            var tabs = new TabControl
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 10, 0, 0)
            };
            tabs.Items.Add(MakeTab("Overview", BuildOverviewTab()));
            tabs.Items.Add(MakeTab("Activity", BuildActivityTab()));
            tabs.Items.Add(MakeTab("Tools", BuildToolsTab()));
            _knowledgeTab = MakeTab("Knowledge", _knowledgePanel.Root);
            tabs.Items.Add(_knowledgeTab);
            tabs.Items.Add(MakeTab("Logs", BuildLogsTab()));
            tabs.Items.Add(MakeTab("Help", McpHelpPanel.Build()));
            tabs.SelectionChanged += (_, e) =>
            {
                if (ReferenceEquals(e.OriginalSource, tabs) && ReferenceEquals(tabs.SelectedItem, _knowledgeTab))
                {
                    _knowledgePanel.EnsureLoaded();
                }
            };
            _tabs = tabs;
            return tabs;
        }

        /// <summary>
        /// Brings the Knowledge tab to the front (used by the ShowKnowledgeDashboard entry point).
        /// </summary>
        public void FocusKnowledgeTab()
        {
            if (_tabs != null && _knowledgeTab != null)
            {
                _tabs.SelectedItem = _knowledgeTab;
                _knowledgePanel.EnsureLoaded();
            }
        }

        private static TabItem MakeTab(string headerText, UIElement content) => new()
        {
            Header = headerText,
            Content = content
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Overview tab
        // ═══════════════════════════════════════════════════════════════════════

        private UIElement BuildOverviewTab()
        {
            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftStack = new StackPanel();
            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            var rightStack = new StackPanel();
            Grid.SetColumn(rightStack, 2);
            grid.Children.Add(rightStack);

            // Bridge section
            var bridgePanel = SectionPanel("Bridge");
            bridgePanel.Children.Add(InfoRow("Pipe", _pipeValue));
            bridgePanel.Children.Add(InfoRow("Process ID", _pidValue));
            bridgePanel.Children.Add(InfoRow("Owner", _ownerValue));
            bridgePanel.Children.Add(InfoRow("Listening since", _listeningSinceValue));
            bridgePanel.Children.Add(InfoRow("Client", _clientValue));
            leftStack.Children.Add(WrapSection(bridgePanel));

            // Configuration section
            var configPanel = SectionPanel("Configuration");
            configPanel.Children.Add(InfoRow("Auto-start", _autostartValue));
            configPanel.Children.Add(InfoRow("Settings file", _settingsPathValue));
            configPanel.Children.Add(InfoRow("MCP server exe", _serverPathValue));
            configPanel.Children.Add(InfoRow("Game API version", _gameApiValue));
            configPanel.Children.Add(InfoRow("Tool catalog", _catalogSummaryValue));
            leftStack.Children.Add(WrapSection(configPanel));

            // Connect section
            var connectPanel = SectionPanel("Connect an MCP client");
            connectPanel.Children.Add(new TextBlock
            {
                Text = "Register MESharp.McpServer.exe as a stdio MCP server. It discovers running game sessions "
                     + "automatically, or pin one with the MESHARP_SESSION_PID environment variable.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(TextDim),
                Margin = new Thickness(0, 0, 0, 8)
            });
            _connectSnippetBox.IsReadOnly = true;
            _connectSnippetBox.AcceptsReturn = true;
            _connectSnippetBox.TextWrapping = TextWrapping.NoWrap;
            _connectSnippetBox.FontFamily = Mono;
            _connectSnippetBox.FontSize = 12;
            _connectSnippetBox.MinHeight = 120;
            _connectSnippetBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            connectPanel.Children.Add(_connectSnippetBox);
            var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            copyRow.Children.Add(MakeButton("Copy config", (_, _) => CopyToClipboard(_connectSnippetBox.Text, "Copied MCP client config.")));
            copyRow.Children.Add(MakeButton("Copy exe path", (_, _) => CopyToClipboard(ServiceRegistry.GetMcpServerPath(), "Copied MCP server path.")));
            connectPanel.Children.Add(copyRow);
            rightStack.Children.Add(WrapSection(connectPanel));

            // Maintenance section
            var maintenancePanel = SectionPanel("Maintenance");
            maintenancePanel.Children.Add(new TextBlock
            {
                Text = "Counters and the recent-call list live in-process and reset when the runtime restarts.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(TextDim),
                Margin = new Thickness(0, 0, 0, 8)
            });
            var maintenanceRow = new StackPanel { Orientation = Orientation.Horizontal };
            maintenanceRow.Children.Add(MakeButton("Reset counters", (_, _) =>
            {
                McpDiagnostics.ResetCounters();
                _lastSeenSequence = -1;
                _activityFilterDirty = true;
                RefreshAll();
                SetStatus("Counters reset.");
            }));
            maintenancePanel.Children.Add(maintenanceRow);
            rightStack.Children.Add(WrapSection(maintenancePanel));

            scroller.Content = grid;
            return scroller;
        }

        private static StackPanel SectionPanel(string title)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentBlue),
                Margin = new Thickness(0, 0, 0, 8)
            });
            return panel;
        }

        private static Border WrapSection(UIElement content) => new()
        {
            Background = new SolidColorBrush(BgCard),
            BorderBrush = new SolidColorBrush(BorderCol),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 12),
            Child = content
        };

        private static UIElement InfoRow(string caption, TextBlock value)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var captionText = new TextBlock
            {
                Text = caption,
                Foreground = new SolidColorBrush(TextDim),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Top
            };
            row.Children.Add(captionText);

            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            return row;
        }

        private static TextBlock InfoValue() => new()
        {
            Text = "–",
            FontFamily = Mono,
            FontSize = 12,
            Foreground = Brushes.WhiteSmoke,
            TextWrapping = TextWrapping.Wrap
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Activity tab
        // ═══════════════════════════════════════════════════════════════════════

        public sealed class ActivityRow
        {
            public string Time { get; init; } = string.Empty;
            public string Command { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public bool Ok { get; init; }
            public string Duration { get; init; } = string.Empty;
            public string Error { get; init; } = string.Empty;
        }

        public sealed class CommandStatsRow
        {
            public string Command { get; init; } = string.Empty;
            public string Count { get; init; } = string.Empty;
            public string Failed { get; init; } = string.Empty;
            public string AvgMs { get; init; } = string.Empty;
            public string Last { get; init; } = string.Empty;
        }

        private UIElement BuildActivityTab()
        {
            var layout = new DockPanel();

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            toolbar.Children.Add(ToolbarLabel("Filter"));
            _activityFilter.TextChanged += (_, _) => { _activityFilterDirty = true; RefreshActivity(); };
            toolbar.Children.Add(_activityFilter);
            _errorsOnlyCheck.Foreground = Brushes.WhiteSmoke;
            _errorsOnlyCheck.Checked += (_, _) => { _activityFilterDirty = true; RefreshActivity(); };
            _errorsOnlyCheck.Unchecked += (_, _) => { _activityFilterDirty = true; RefreshActivity(); };
            toolbar.Children.Add(_errorsOnlyCheck);
            DockPanel.SetDock(toolbar, Dock.Top);
            layout.Children.Add(toolbar);

            var split = new Grid();
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star), MinWidth = 400 });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
            layout.Children.Add(split);

            // Recent calls
            var callsView = new GridView();
            callsView.Columns.Add(MakeColumn("Time", nameof(ActivityRow.Time), 80));
            callsView.Columns.Add(MakeColumn("Command", nameof(ActivityRow.Command), 220));
            callsView.Columns.Add(MakeColumn("Status", nameof(ActivityRow.Status), 60));
            callsView.Columns.Add(MakeColumn("ms", nameof(ActivityRow.Duration), 60));
            callsView.Columns.Add(MakeColumn("Error", nameof(ActivityRow.Error), 320));
            _activityList.View = callsView;
            _activityList.ItemContainerStyle = BuildActivityRowStyle();
            var callsPanel = TitledListPanel("Recent calls (newest first)", _activityList);
            Grid.SetColumn(callsPanel, 0);
            split.Children.Add(callsPanel);

            // Per-command stats
            var statsView = new GridView();
            statsView.Columns.Add(MakeColumn("Command", nameof(CommandStatsRow.Command), 190));
            statsView.Columns.Add(MakeColumn("Calls", nameof(CommandStatsRow.Count), 50));
            statsView.Columns.Add(MakeColumn("Fail", nameof(CommandStatsRow.Failed), 45));
            statsView.Columns.Add(MakeColumn("Avg ms", nameof(CommandStatsRow.AvgMs), 60));
            statsView.Columns.Add(MakeColumn("Last", nameof(CommandStatsRow.Last), 75));
            _commandStatsList.View = statsView;
            var statsPanel = TitledListPanel("Per-command totals", _commandStatsList);
            Grid.SetColumn(statsPanel, 2);
            split.Children.Add(statsPanel);

            return layout;
        }

        private Style BuildActivityRowStyle()
        {
            var style = BuildListItemBaseStyle();
            var failTrigger = new System.Windows.DataTrigger { Binding = new Binding(nameof(ActivityRow.Ok)), Value = false };
            failTrigger.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(AccentRed)));
            style.Triggers.Add(failTrigger);
            return style;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Tools tab
        // ═══════════════════════════════════════════════════════════════════════

        public sealed class ToolRow
        {
            public string Name { get; init; } = string.Empty;
            public string Kind { get; init; } = string.Empty;
            public string Safety { get; init; } = string.Empty;
            public Brush SafetyBrush { get; init; } = Brushes.WhiteSmoke;
            public string Login { get; init; } = string.Empty;
            public string Mutates { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
        }

        private UIElement BuildToolsTab()
        {
            var layout = new DockPanel();

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            toolbar.Children.Add(ToolbarLabel("Search"));
            _toolsSearch.TextChanged += (_, _) => RefreshTools();
            toolbar.Children.Add(_toolsSearch);
            _toolsCategory.SelectionChanged += (_, _) => RefreshTools();
            toolbar.Children.Add(_toolsCategory);
            _toolsSafety.SelectionChanged += (_, _) => RefreshTools();
            toolbar.Children.Add(_toolsSafety);
            _toolsCount.Foreground = new SolidColorBrush(TextDim);
            toolbar.Children.Add(_toolsCount);
            DockPanel.SetDock(toolbar, Dock.Top);
            layout.Children.Add(toolbar);

            var view = new GridView();
            view.Columns.Add(MakeColumn("Tool", nameof(ToolRow.Name), 220));
            view.Columns.Add(MakeColumn("Category", nameof(ToolRow.Kind), 130));
            view.Columns.Add(MakeSafetyColumn());
            view.Columns.Add(MakeColumn("Login", nameof(ToolRow.Login), 50));
            view.Columns.Add(MakeColumn("Mutates", nameof(ToolRow.Mutates), 62));
            view.Columns.Add(MakeColumn("Description", nameof(ToolRow.Description), 520));
            _toolsList.View = view;
            _toolsList.ItemContainerStyle = BuildListItemBaseStyle();
            _toolsList.MouseDoubleClick += (_, _) =>
            {
                if (_toolsList.SelectedItem is ToolRow row)
                {
                    CopyToClipboard(row.Name, $"Copied tool name '{row.Name}'.");
                }
            };
            layout.Children.Add(TitledListPanel("Bridge tool catalog (double-click to copy a tool name)", _toolsList));

            return layout;
        }

        private GridViewColumn MakeSafetyColumn()
        {
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ToolRow.Safety)));
            factory.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ToolRow.SafetyBrush)));
            factory.SetValue(TextBlock.FontFamilyProperty, Mono);
            factory.SetValue(TextBlock.FontSizeProperty, 11.0);
            return new GridViewColumn
            {
                Header = "Safety",
                Width = 120,
                CellTemplate = new DataTemplate { VisualTree = factory }
            };
        }

        private static Brush SafetyToBrush(string safety) => safety switch
        {
            "safe" => new SolidColorBrush(AccentGreen),
            "blocking-safe" => new SolidColorBrush(AccentGreen),
            "mutating" => new SolidColorBrush(AccentRed),
            "runtime-mutating" => new SolidColorBrush(AccentOrange),
            "filesystem" => new SolidColorBrush(AccentYellow),
            "filesystem-build" => new SolidColorBrush(AccentYellow),
            _ => new SolidColorBrush(AccentBlue)
        };

        // ═══════════════════════════════════════════════════════════════════════
        //  Logs tab
        // ═══════════════════════════════════════════════════════════════════════

        private UIElement BuildLogsTab()
        {
            var layout = new DockPanel();

            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            toolbar.Children.Add(ToolbarLabel("Filter"));
            _logsFilter.Text = "MCP";
            _logsFilter.TextChanged += (_, _) => { _logsDirty = true; RefreshLogs(); };
            toolbar.Children.Add(_logsFilter);
            _tailCheck.Foreground = Brushes.WhiteSmoke;
            toolbar.Children.Add(_tailCheck);
            toolbar.Children.Add(MakeButton("Clear buffer", (_, _) =>
            {
                RuntimeLogBuffer.Clear();
                _logCache.Clear();
                _logsDirty = true;
                RefreshLogs();
                SetStatus("Runtime log buffer cleared.");
            }));
            toolbar.Children.Add(MakeButton("Copy", (_, _) => CopyToClipboard(_logsBox.Text, "Copied visible log lines.")));
            DockPanel.SetDock(toolbar, Dock.Top);
            layout.Children.Add(toolbar);

            _logsBox.IsReadOnly = true;
            _logsBox.AcceptsReturn = true;
            _logsBox.TextWrapping = TextWrapping.NoWrap;
            _logsBox.FontFamily = Mono;
            _logsBox.FontSize = 12;
            _logsBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _logsBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            layout.Children.Add(_logsBox);

            return layout;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Refresh logic
        // ═══════════════════════════════════════════════════════════════════════

        private void RefreshAll(bool refreshConfig = true)
        {
            var snapshot = McpDiagnostics.GetSnapshot();
            RefreshHeader(snapshot);
            RefreshCards(snapshot);
            RefreshOverviewDynamic(snapshot);
            if (refreshConfig)
            {
                RefreshOverviewConfig();
            }
            RefreshActivity();
            RefreshLogs();
        }

        private void RefreshHeader(McpBridgeSnapshot snapshot)
        {
            var listenerKnown = snapshot.ListenerActive || McpRuntimeService.HasActiveListener;
            if (listenerKnown)
            {
                _statusDot.Fill = new SolidColorBrush(AccentGreen);
                _statusPillText.Text = $"Listening · {snapshot.PipeName ?? $"MESharpMcpBridge.{Environment.ProcessId}"}";
                _statusPillText.Foreground = new SolidColorBrush(AccentGreen);
            }
            else
            {
                _statusDot.Fill = new SolidColorBrush(AccentRed);
                _statusPillText.Text = "Bridge stopped";
                _statusPillText.Foreground = new SolidColorBrush(AccentRed);
            }

            if (snapshot.ClientConnected)
            {
                _clientChipText.Text = "client connected";
                _clientChipText.Foreground = new SolidColorBrush(AccentBlue);
            }
            else
            {
                _clientChipText.Text = "no client";
                _clientChipText.Foreground = new SolidColorBrush(TextDim);
            }

            bool ownsBridge;
            lock (BridgeSync)
            {
                ownsBridge = _dashboardOwnedBridge is { OwnsListener: true };
            }

            _startBtn.IsEnabled = !listenerKnown;
            _stopBtn.IsEnabled = ownsBridge;
        }

        private void RefreshCards(McpBridgeSnapshot snapshot)
        {
            _reqValue.Text = snapshot.TotalRequests.ToString();
            _failValue.Text = snapshot.FailedRequests.ToString();
            _failValue.Foreground = snapshot.FailedRequests > 0 ? new SolidColorBrush(AccentRed) : Brushes.WhiteSmoke;
            _avgValue.Text = snapshot.TotalRequests > 0 ? $"{snapshot.AverageDurationMs} ms" : "–";
            _connValue.Text = snapshot.TotalConnections.ToString();
            _uptimeValue.Text = snapshot.ListenerActive && snapshot.ListenerStartedUtc.HasValue
                ? FormatDuration(DateTime.UtcNow - snapshot.ListenerStartedUtc.Value)
                : "–";
            _lastCmdValue.Text = snapshot.LastCommand ?? "–";
            _lastCmdValue.ToolTip = snapshot.LastRequestUtc.HasValue
                ? $"{snapshot.LastCommand} at {snapshot.LastRequestUtc:HH:mm:ss} UTC"
                : null;
        }

        // Per-tick overview rows: anything that tracks live bridge/client state.
        private void RefreshOverviewDynamic(McpBridgeSnapshot snapshot)
        {
            _pipeValue.Text = snapshot.PipeName ?? $"MESharpMcpBridge.{Environment.ProcessId} (expected)";
            _pidValue.Text = Environment.ProcessId.ToString();

            string owner;
            lock (BridgeSync)
            {
                if (_dashboardOwnedBridge is { OwnsListener: true })
                {
                    owner = "this dashboard";
                }
                else if (snapshot.ListenerActive || McpRuntimeService.HasActiveListener)
                {
                    owner = "runtime service / bridge script";
                }
                else
                {
                    owner = "–";
                }
            }
            _ownerValue.Text = owner;

            _listeningSinceValue.Text = snapshot.ListenerActive && snapshot.ListenerStartedUtc.HasValue
                ? $"{snapshot.ListenerStartedUtc:yyyy-MM-dd HH:mm:ss} UTC"
                : "–";

            if (snapshot.ClientConnected)
            {
                _clientValue.Text = $"connected since {snapshot.LastClientConnectedUtc:HH:mm:ss} UTC";
                _clientValue.Foreground = new SolidColorBrush(AccentGreen);
            }
            else
            {
                _clientValue.Text = snapshot.LastClientDisconnectedUtc.HasValue
                    ? $"disconnected at {snapshot.LastClientDisconnectedUtc:HH:mm:ss} UTC"
                    : "never connected";
                _clientValue.Foreground = new SolidColorBrush(TextDim);
            }
        }

        // Near-static overview rows: settings/server paths, connect snippet, game/catalog info.
        // These touch the filesystem, so this is throttled rather than run every tick.
        private void RefreshOverviewConfig()
        {
            try
            {
                var autostart = ServiceRegistry.GetMcpAutoStartEnabled();
                _autostartValue.Text = autostart ? "enabled (MCP_AUTOSTART)" : "disabled (MCP_AUTOSTART=false)";
                _autostartValue.Foreground = autostart ? new SolidColorBrush(AccentGreen) : new SolidColorBrush(AccentOrange);
                _settingsPathValue.Text = ServiceRegistry.GetMeSettingsPath();

                var serverPath = ServiceRegistry.GetMcpServerPath();
                if (string.IsNullOrWhiteSpace(serverPath))
                {
                    _serverPathValue.Text = "MESharp.McpServer.exe not found next to csharp_interop.dll";
                    _serverPathValue.Foreground = new SolidColorBrush(AccentRed);
                }
                else
                {
                    _serverPathValue.Text = serverPath;
                    _serverPathValue.Foreground = new SolidColorBrush(AccentGreen);
                }

                // Only reassign when the value actually changed; setting Text resets any selection,
                // which would fight the user trying to drag-select the snippet to copy it.
                var snippet = BuildConnectSnippet(serverPath);
                if (!string.Equals(_connectSnippetBox.Text, snippet, StringComparison.Ordinal))
                {
                    _connectSnippetBox.Text = snippet;
                }
            }
            catch (Exception ex)
            {
                _settingsPathValue.Text = $"unavailable: {ex.Message}";
            }

            try
            {
                _gameApiValue.Text = MESharp.API.Game.Version;
            }
            catch
            {
                _gameApiValue.Text = "unavailable (not injected?)";
            }

            if (_toolCatalog.Count > 0)
            {
                var categories = _toolCatalog.Select(tool => tool.Kind).Distinct().Count();
                _catalogSummaryValue.Text = $"{_toolCatalog.Count} tools across {categories} categories";
            }
        }

        private static string BuildConnectSnippet(string serverPath)
        {
            var path = string.IsNullOrWhiteSpace(serverPath) ? "<path-to>\\MESharp.McpServer.exe" : serverPath;
            var escaped = path.Replace("\\", "\\\\");
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"mcpServers\": {");
            builder.AppendLine("    \"mesharp\": {");
            builder.AppendLine($"      \"command\": \"{escaped}\",");
            builder.AppendLine("      \"env\": {");
            builder.AppendLine($"        \"MESHARP_SESSION_PID\": \"{Environment.ProcessId}\"");
            builder.AppendLine("      }");
            builder.AppendLine("    }");
            builder.AppendLine("  }");
            builder.Append('}');
            return builder.ToString();
        }

        private void RefreshActivity()
        {
            var calls = McpDiagnostics.GetRecentCalls();
            var newestSequence = calls.Count > 0 ? calls[0].Sequence : 0;
            if (!_activityFilterDirty && newestSequence == _lastSeenSequence)
            {
                return;
            }

            _lastSeenSequence = newestSequence;
            _activityFilterDirty = false;

            var filter = _activityFilter.Text?.Trim() ?? string.Empty;
            var errorsOnly = _errorsOnlyCheck.IsChecked == true;

            IEnumerable<McpCallRecord> filtered = calls;
            if (errorsOnly)
            {
                filtered = filtered.Where(call => !call.Ok);
            }

            if (filter.Length > 0)
            {
                filtered = filtered.Where(call =>
                    call.Command.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    (call.ErrorMessage?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            _activityList.ItemsSource = filtered
                .Select(call => new ActivityRow
                {
                    Time = call.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"),
                    Command = call.Command,
                    Status = call.Ok ? "✓ ok" : "✗ fail",
                    Ok = call.Ok,
                    Duration = call.DurationMs.ToString(),
                    Error = call.Ok ? string.Empty : $"{call.ErrorCode}: {call.ErrorMessage}"
                })
                .ToList();

            _commandStatsList.ItemsSource = McpDiagnostics.GetCommandStats()
                .Select(stat => new CommandStatsRow
                {
                    Command = stat.Command,
                    Count = stat.Count.ToString(),
                    Failed = stat.FailureCount > 0 ? stat.FailureCount.ToString() : string.Empty,
                    AvgMs = stat.AverageDurationMs.ToString(),
                    Last = stat.LastCalledUtc.ToLocalTime().ToString("HH:mm:ss")
                })
                .ToList();
        }

        private void LoadToolCatalog()
        {
            try
            {
                _toolCatalog = McpRuntimeService.GetToolCatalog();
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load tool catalog: {ex.Message}");
                return;
            }

            _toolsCategory.Items.Clear();
            _toolsCategory.Items.Add("all categories");
            foreach (var kind in _toolCatalog.Select(tool => tool.Kind).Distinct().OrderBy(kind => kind, StringComparer.Ordinal))
            {
                _toolsCategory.Items.Add(kind);
            }
            _toolsCategory.SelectedIndex = 0;

            _toolsSafety.Items.Clear();
            _toolsSafety.Items.Add("all safety");
            foreach (var safety in _toolCatalog.Select(tool => tool.Safety).Distinct().OrderBy(safety => safety, StringComparer.Ordinal))
            {
                _toolsSafety.Items.Add(safety);
            }
            _toolsSafety.SelectedIndex = 0;

            RefreshTools();
        }

        private void RefreshTools()
        {
            if (_toolCatalog.Count == 0)
            {
                return;
            }

            var search = _toolsSearch.Text?.Trim() ?? string.Empty;
            var category = _toolsCategory.SelectedIndex > 0 ? _toolsCategory.SelectedItem as string : null;
            var safety = _toolsSafety.SelectedIndex > 0 ? _toolsSafety.SelectedItem as string : null;

            var filtered = _toolCatalog
                .Where(tool => category == null || tool.Kind == category)
                .Where(tool => safety == null || tool.Safety == safety)
                .Where(tool => search.Length == 0 ||
                               tool.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                               tool.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(tool => new ToolRow
                {
                    Name = tool.Name,
                    Kind = tool.Kind,
                    Safety = tool.Safety,
                    SafetyBrush = SafetyToBrush(tool.Safety),
                    Login = tool.RequiresLogin ? "yes" : string.Empty,
                    Mutates = tool.MutatesGame ? "yes" : string.Empty,
                    Description = tool.Description
                })
                .ToList();

            _toolsList.ItemsSource = filtered;
            _toolsCount.Text = $"{filtered.Count} / {_toolCatalog.Count} tools";
        }

        private void RefreshLogs()
        {
            // Pull any new lines into the local cache, then rebuild the view only when something changed.
            while (true)
            {
                var batch = RuntimeLogBuffer.GetSince(_logFromIndex, 500);
                if (batch.Count == 0)
                {
                    break;
                }

                _logCache.AddRange(batch);
                _logFromIndex = batch[^1].Index + 1;
                _logsDirty = true;

                if (batch.Count < 500)
                {
                    break;
                }
            }

            const int maxCachedLines = 1500;
            if (_logCache.Count > maxCachedLines)
            {
                _logCache.RemoveRange(0, _logCache.Count - maxCachedLines);
            }

            if (!_logsDirty)
            {
                return;
            }

            _logsDirty = false;
            var filter = _logsFilter.Text?.Trim() ?? string.Empty;
            var builder = new StringBuilder();
            foreach (var line in _logCache)
            {
                if (filter.Length > 0 && line.Text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                builder.Append(line.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"));
                builder.Append("  ");
                builder.AppendLine(line.Text);
            }

            _logsBox.Text = builder.ToString();
            if (_tailCheck.IsChecked == true)
            {
                _logsBox.ScrollToEnd();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Bridge control
        // ═══════════════════════════════════════════════════════════════════════

        private void StartBridge()
        {
            lock (BridgeSync)
            {
                if (McpRuntimeService.HasActiveListener)
                {
                    SetStatus("Bridge is already listening.");
                    return;
                }

                try
                {
                    var service = new McpRuntimeService(new DefaultScriptRuntimeHost(), new SnippetRuntimeHost());
                    service.Start();
                    if (service.OwnsListener)
                    {
                        _dashboardOwnedBridge = service;
                        SetStatus("Bridge started from dashboard.");
                    }
                    else
                    {
                        service.Dispose();
                        SetStatus("Another bridge instance took the listener.");
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"Failed to start bridge: {ex.Message}");
                }
            }

            RefreshAll();
        }

        private void StopBridge()
        {
            lock (BridgeSync)
            {
                if (_dashboardOwnedBridge == null)
                {
                    SetStatus("The active bridge is owned by the runtime/bridge script; stop it there.");
                    return;
                }

                try
                {
                    _dashboardOwnedBridge.Dispose();
                    SetStatus("Bridge stopped.");
                }
                catch (Exception ex)
                {
                    SetStatus($"Failed to stop bridge: {ex.Message}");
                }
                finally
                {
                    _dashboardOwnedBridge = null;
                }
            }

            RefreshAll();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Shared helpers / styling
        // ═══════════════════════════════════════════════════════════════════════

        private void SetStatus(string message)
        {
            _statusBar.Text = $"{DateTime.Now:HH:mm:ss}  {message}";
        }

        private void CopyToClipboard(string text, string confirmation)
        {
            try
            {
                Clipboard.SetText(text ?? string.Empty);
                SetStatus(confirmation);
            }
            catch (Exception ex)
            {
                SetStatus($"Clipboard failed: {ex.Message}");
            }
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1)
            {
                return $"{(int)span.TotalHours}h {span.Minutes:00}m";
            }

            return span.TotalMinutes >= 1 ? $"{span.Minutes}m {span.Seconds:00}s" : $"{span.Seconds}s";
        }

        private static TextBlock ToolbarLabel(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(TextDim),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };

        private static GridViewColumn MakeColumn(string header, string bindingPath, double width) => new()
        {
            Header = header,
            Width = width,
            DisplayMemberBinding = new Binding(bindingPath)
        };

        private static DockPanel TitledListPanel(string title, ListView list)
        {
            var panel = new DockPanel();
            var caption = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(TextDim),
                FontSize = 11,
                Margin = new Thickness(2, 0, 0, 4)
            };
            DockPanel.SetDock(caption, Dock.Top);
            panel.Children.Add(caption);
            panel.Children.Add(list);
            return panel;
        }

        private static Style BuildListItemBaseStyle()
        {
            var style = new Style(typeof(ListViewItem));
            style.Setters.Add(new Setter(ForegroundProperty, Brushes.WhiteSmoke));
            style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, Mono));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            return style;
        }

        private static Button MakeButton(string text, RoutedEventHandler handler, Color? accent = null)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 86,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(accent ?? BgField),
                Foreground = accent.HasValue ? Brushes.Black : Brushes.WhiteSmoke,
                FontWeight = accent.HasValue ? FontWeights.SemiBold : FontWeights.Normal
            };
            button.Click += handler;
            return button;
        }

        private static ResourceDictionary BuildSharedStyles()
        {
            var dictionary = new ResourceDictionary();

            // Buttons: flat, rounded, dim when disabled.
            var buttonTemplate = new ControlTemplate(typeof(Button));
            var buttonBorder = new FrameworkElementFactory(typeof(Border));
            buttonBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            buttonBorder.SetBinding(Border.BackgroundProperty, TemplatedParentBinding(nameof(Background)));
            buttonBorder.SetBinding(Border.PaddingProperty, TemplatedParentBinding(nameof(Padding)));
            var buttonContent = new FrameworkElementFactory(typeof(ContentPresenter));
            buttonContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            buttonContent.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            buttonBorder.AppendChild(buttonContent);
            buttonTemplate.VisualTree = buttonBorder;

            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(TemplateProperty, buttonTemplate));
            buttonStyle.Setters.Add(new Setter(CursorProperty, System.Windows.Input.Cursors.Hand));
            var buttonHover = new Trigger { Property = IsMouseOverProperty, Value = true };
            buttonHover.Setters.Add(new Setter(OpacityProperty, 0.82));
            buttonStyle.Triggers.Add(buttonHover);
            var buttonPressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
            buttonPressed.Setters.Add(new Setter(OpacityProperty, 0.65));
            buttonStyle.Triggers.Add(buttonPressed);
            var buttonDisabled = new Trigger { Property = IsEnabledProperty, Value = false };
            buttonDisabled.Setters.Add(new Setter(OpacityProperty, 0.35));
            buttonStyle.Triggers.Add(buttonDisabled);
            dictionary.Add(typeof(Button), buttonStyle);

            // Text boxes: dark fields.
            var textBoxStyle = new Style(typeof(TextBox));
            textBoxStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(BgField)));
            textBoxStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.WhiteSmoke));
            textBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(BorderCol)));
            textBoxStyle.Setters.Add(new Setter(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, Brushes.WhiteSmoke));
            textBoxStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 3, 5, 3)));
            dictionary.Add(typeof(TextBox), textBoxStyle);

            // List views: dark surface.
            var listStyle = new Style(typeof(ListView));
            listStyle.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(BgCard)));
            listStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.WhiteSmoke));
            listStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(BorderCol)));
            listStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            dictionary.Add(typeof(ListView), listStyle);

            // Grid view column headers: flat dark strip.
            var headerTemplate = new ControlTemplate(typeof(System.Windows.Controls.GridViewColumnHeader));
            var headerBorder = new FrameworkElementFactory(typeof(Border));
            headerBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(BgField));
            headerBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(BorderCol));
            headerBorder.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 1));
            headerBorder.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
            var headerContent = new FrameworkElementFactory(typeof(ContentPresenter));
            headerContent.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            headerContent.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            headerBorder.AppendChild(headerContent);
            headerTemplate.VisualTree = headerBorder;

            var headerStyle = new Style(typeof(System.Windows.Controls.GridViewColumnHeader));
            headerStyle.Setters.Add(new Setter(TemplateProperty, headerTemplate));
            headerStyle.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(TextDim)));
            headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
            headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            dictionary.Add(typeof(System.Windows.Controls.GridViewColumnHeader), headerStyle);

            // Tabs: underline-style headers on a transparent strip.
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
            tabContent.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            tabBorder.AppendChild(tabContent);
            tabTemplate.VisualTree = tabBorder;

            var tabSelected = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
            tabSelected.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(AccentBlue), "TabBorder"));
            tabSelected.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
            tabTemplate.Triggers.Add(tabSelected);
            var tabHover = new Trigger { Property = IsMouseOverProperty, Value = true };
            tabHover.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
            tabTemplate.Triggers.Add(tabHover);

            var tabStyle = new Style(typeof(TabItem));
            tabStyle.Setters.Add(new Setter(TemplateProperty, tabTemplate));
            tabStyle.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(TextDim)));
            tabStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            tabStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            dictionary.Add(typeof(TabItem), tabStyle);

            return dictionary;
        }

        private static Binding TemplatedParentBinding(string path) => new(path)
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        };
    }
}
