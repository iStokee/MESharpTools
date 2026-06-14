using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MESharp.API;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Control = System.Windows.Controls.Control;
using DataBinding = System.Windows.Data.Binding;
using FontFamily = System.Windows.Media.FontFamily;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace csharp_interop.McpTools
{
    /// <summary>
    /// Knowledge OS curation panel hosted inside the MCP dashboard. Ports the former standalone
    /// KnowledgeDashboardWindow (presets, filters, status-dot list, full editor) with the
    /// dashboard's dark card styling.
    /// </summary>
    internal sealed class McpKnowledgePanel
    {
        private static readonly Color BgCard = Color.FromRgb(28, 32, 40);
        private static readonly Color BorderCol = Color.FromRgb(52, 58, 72);
        private static readonly Color AccentBlue = Color.FromRgb(66, 165, 245);
        private static readonly Color TextDim = Color.FromRgb(148, 155, 170);
        private static readonly FontFamily Mono = new("Consolas");

        private static readonly string[] ViewPresets = { "All", "Inquiry Ledger", "Question Debt", "Waiting on User", "Validated Knowledge", "Stale Knowledge", "Recent Outcomes" };

        private readonly Action<string> _setStatus;

        // ── Filters ──────────────────────────────────────────────────────────
        private readonly TextBox _searchBox = new();
        private readonly ComboBox _presetCombo = new();
        private readonly ComboBox _categoryFilter = new();
        private readonly ComboBox _statusFilter = new();
        private readonly ComboBox _kindFilter = new();
        private readonly ComboBox _targetFilter = new();
        private readonly ComboBox _timeHorizonFilter = new();
        private readonly CheckBox _blockingCheck = new() { Content = "Blocking only", Margin = new Thickness(0, 0, 12, 0) };
        private readonly CheckBox _debtCheck = new() { Content = "Question Debt only" };

        // ── List ──────────────────────────────────────────────────────────────
        private readonly ListBox _itemsList = new();

        // ── Core editor ───────────────────────────────────────────────────────
        private readonly TextBox _titleBox = new();
        private readonly TextBox _bodyBox = new();
        private readonly ComboBox _kindBox = EditableCombo();
        private readonly ComboBox _categoryBox = EditableCombo();
        private readonly ComboBox _statusBox = EditableCombo();
        private readonly ComboBox _sourceBox = EditableCombo();
        private readonly TextBox _tagsBox = new();
        private readonly Slider _confidenceSlider = new() { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true };
        private readonly TextBox _relatedTypeBox = new();
        private readonly TextBox _relatedIdBox = new();

        // ── Extended editor ───────────────────────────────────────────────────
        private readonly TextBox _targetBox = new();
        private readonly TextBox _timeHorizonBox = new();
        private readonly TextBox _modeBox = new();
        private readonly TextBox _impactBox = new();
        private readonly TextBox _urgencyBox = new();
        private readonly CheckBox _blockingEdit = new() { Content = "Blocking" };
        private readonly TextBox _deferReasonBox = new();
        private readonly TextBox _evidenceBox = new();
        private readonly TextBox _relatedItemIdsBox = new();
        private readonly TextBox _originatingAgentBox = new();
        private readonly TextBox _originatingTaskBox = new();
        private readonly TextBlock _validationsText = ReadOnlyValue();
        private readonly TextBlock _failuresText = ReadOnlyValue();
        private readonly TextBlock _usageText = ReadOnlyValue();
        private readonly TextBlock _lastReviewedText = ReadOnlyValue();
        private readonly TextBlock _createdText = ReadOnlyValue();

        // ── State ─────────────────────────────────────────────────────────────
        private List<KnowledgeItem> _items = new();
        private KnowledgeItem? _selected;
        private bool _loadedOnce;
        private bool _suppressSelectionLoad;
        private bool _dirty;
        private bool _suppressDirty;
        private readonly TextBlock _dirtyIndicator = new()
        {
            Text = "● Unsaved changes",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 167, 38)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Visibility = Visibility.Collapsed
        };
        private readonly System.Windows.Threading.DispatcherTimer _searchDebounce = new()
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };

        public McpKnowledgePanel(Action<string> setStatus)
        {
            _setStatus = setStatus;
            SeedEditorDropdowns();
            ApplyTooltips();
            Root = BuildLayout();
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce.Stop();
                RefreshItems();
            };
            HookDirtyTracking();
        }

        private static ComboBox EditableCombo() => new() { IsEditable = true };

        private void SeedEditorDropdowns()
        {
            SeedEditableCombo(_kindBox, "observation", "inquiry", "investigation", "evidence", "hypothesis", "answer", "knowledge", "outcome", "note", "fact");
            SeedEditableCombo(_categoryBox, "navigation", "api", "runtime", "script", "banking", "combat", "scripting", "timing", "inventory", "dungeoneering", "tooling", "meta", "general");
            SeedEditableCombo(_statusBox, "draft", "open", "active", "investigating", "partial", "answered", "validated", "promoted", "stale", "waiting-user", "deprecated", "archived", "failed");
            SeedEditableCombo(_sourceBox, "human", "agent", "script", "seed");
        }

        private static void SeedEditableCombo(ComboBox combo, params string[] values)
        {
            foreach (var value in values)
                combo.Items.Add(value);
        }

        private void ApplyTooltips()
        {
            // Filters
            _presetCombo.ToolTip = "Quick views over the store. Each preset configures the filters below for a common curation task.";
            _categoryFilter.ToolTip = "Filter by domain bucket (navigation, combat, banking, ...).";
            _statusFilter.ToolTip = "Filter by lifecycle status.";
            _kindFilter.ToolTip = "Filter by item kind (inquiry, answer, knowledge, outcome, ...).";
            _targetFilter.ToolTip = "Filter by intended audience: self, future-agent, user, tool, or project.";
            _timeHorizonFilter.ToolTip = "Filter by relevance window: short, medium, or long.";
            _blockingCheck.ToolTip = "Show only items flagged as blocking progress.";
            _debtCheck.ToolTip = "Question Debt: open/investigating inquiries and hypotheses, answers that were never validated, and stale knowledge.";

            // Core editor
            _titleBox.ToolTip = "Short, searchable summary of the item.";
            _kindBox.ToolTip = "What this item is: observation (raw note), inquiry (open question), investigation, evidence, hypothesis, "
                             + "answer, knowledge (promoted reusable lesson), outcome (result of applying knowledge), note, or fact. "
                             + "Type a custom kind if none fits.";
            _categoryBox.ToolTip = "Domain bucket used for filtering and agent searches (navigation, combat, banking, ...).";
            _statusBox.ToolTip = "Lifecycle: draft → open → investigating → partial → answered → validated → promoted. "
                               + "Use stale/deprecated/archived to retire items; waiting-user when blocked on a human.";
            _sourceBox.ToolTip = "Who created the item: human (you), agent (MCP agent), script, or seed (baseline seeder).";
            _tagsBox.ToolTip = "Comma-separated tags for search (e.g. lodestone, wilderness, tick-timing).";
            _relatedTypeBox.ToolTip = "Optional type of the related domain object (e.g. route, npc, interface).";
            _relatedIdBox.ToolTip = "Optional id of the related domain object.";
            _confidenceSlider.ToolTip = "0–100 trust score. kb.outcome recalculates this from validations vs failures once an item has 2+ recorded results.";
            _bodyBox.ToolTip = "Full content. For answers/knowledge, state the observed fact first and keep inference clearly separated.";

            // Extended editor
            _targetBox.ToolTip = "Who the item is for: self, future-agent, user, tool, or project.";
            _timeHorizonBox.ToolTip = "How long the item stays relevant: short, medium, or long.";
            _modeBox.ToolTip = "Working mode that produced the item (e.g. explore, implement, debug).";
            _impactBox.ToolTip = "1–5: how much acting on this item matters.";
            _urgencyBox.ToolTip = "1–5: how soon this item needs attention.";
            _blockingEdit.ToolTip = "Marks the item as blocking progress — shown with a red dot and a BLOCKING badge in the list.";
            _deferReasonBox.ToolTip = "Why this item is parked, if it is deliberately not being worked on.";
            _evidenceBox.ToolTip = "Summary of the evidence backing this item (probe output, snapshots, traces).";
            _relatedItemIdsBox.ToolTip = "Comma-separated ids of linked knowledge items (kb.link writes these).";
            _originatingAgentBox.ToolTip = "Agent that created the item, when written through MCP.";
            _originatingTaskBox.ToolTip = "Task or mission the item came from.";
            _validationsText.ToolTip = "Times kb.outcome recorded this item working as expected.";
            _failuresText.ToolTip = "Times kb.outcome recorded this item failing.";
            _usageText.ToolTip = "Times the item was applied, regardless of outcome.";
            _lastReviewedText.ToolTip = "Last time a human/agent pressed Review on this item.";
            _createdText.ToolTip = "When the item was created.";
        }

        public UIElement Root { get; }

        /// <summary>
        /// Loads items the first time the panel becomes visible; cheap to call repeatedly.
        /// </summary>
        public void EnsureLoaded()
        {
            if (_loadedOnce)
            {
                return;
            }

            _loadedOnce = true;
            RefreshItems();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Layout
        // ═══════════════════════════════════════════════════════════════════════

        private UIElement BuildLayout()
        {
            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star), MinWidth = 300 });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star), MinWidth = 420 });

            var splitter = new GridSplitter
            {
                Width = 12,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Columns,
                ToolTip = "Drag to resize the list/editor split"
            };
            Grid.SetColumn(splitter, 1);
            main.Children.Add(splitter);

            // ── Left: filters + list ──
            var left = new DockPanel();
            var filtersPanel = BuildFiltersPanel();
            DockPanel.SetDock(filtersPanel, Dock.Top);
            left.Children.Add(filtersPanel);

            _itemsList.Background = Brushes.Transparent;
            _itemsList.BorderThickness = new Thickness(0);
            _itemsList.Foreground = Brushes.WhiteSmoke;
            _itemsList.SelectionChanged += (_, _) =>
            {
                if (_suppressSelectionLoad)
                {
                    return;
                }

                var newItem = _itemsList.SelectedItem as KnowledgeItem;
                if (_dirty && !ReferenceEquals(newItem, _selected))
                {
                    var discard = MessageBox.Show(
                        "Discard unsaved changes to the current item?",
                        "Unsaved changes",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);
                    if (discard != MessageBoxResult.Yes)
                    {
                        // Revert the selection without reloading the editor.
                        _suppressSelectionLoad = true;
                        try
                        {
                            _itemsList.SelectedItem = _items.FirstOrDefault(i =>
                                string.Equals(i.Id, _selected?.Id, StringComparison.OrdinalIgnoreCase));
                        }
                        finally
                        {
                            _suppressSelectionLoad = false;
                        }

                        return;
                    }
                }

                _selected = newItem;
                LoadEditor(_selected);
            };
            _itemsList.ItemTemplate = BuildListItemTemplate();
            _itemsList.ItemContainerStyle = BuildListItemContainerStyle();
            // Let the ListBox use its own internal ScrollViewer (keeps UI virtualization);
            // an outer ScrollViewer would measure it with infinite height and realize every row.
            ScrollViewer.SetHorizontalScrollBarVisibility(_itemsList, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_itemsList, ScrollBarVisibility.Auto);
            left.Children.Add(_itemsList);

            var leftCard = Card(left);
            Grid.SetColumn(leftCard, 0);
            main.Children.Add(leftCard);

            // ── Right: editor ──
            var editorStack = new StackPanel();
            editorStack.Children.Add(BuildEditorHeader());
            editorStack.Children.Add(BuildCoreEditor());
            editorStack.Children.Add(BuildBodySection());
            editorStack.Children.Add(BuildExtendedExpander());
            editorStack.Children.Add(BuildActionButtons());

            var editorScroller = new ScrollViewer
            {
                // Horizontal must be Disabled (not Hidden) so the content is measured at the
                // viewport width — that constraint is what makes the body TextBox wrap instead
                // of growing sideways and producing a horizontal scrollbar.
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = editorStack
            };
            var rightCard = Card(editorScroller);
            Grid.SetColumn(rightCard, 2);
            main.Children.Add(rightCard);

            return main;
        }

        private static Border Card(UIElement content) => new()
        {
            Background = new SolidColorBrush(BgCard),
            BorderBrush = new SolidColorBrush(BorderCol),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Child = content
        };

        private UIElement BuildEditorHeader()
        {
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(MakeButton("New", (_, _) => NewItem(), "Start a new draft item in the editor (not saved until you press Save)."));
            buttons.Children.Add(MakeButton("Refresh", (_, _) => RefreshItems(), "Reload the list from the knowledge store."));
            DockPanel.SetDock(buttons, Dock.Right);
            header.Children.Add(buttons);
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = "Knowledge Editor",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentBlue),
                VerticalAlignment = VerticalAlignment.Center
            });
            titlePanel.Children.Add(_dirtyIndicator);
            header.Children.Add(titlePanel);
            return header;
        }

        private UIElement BuildFiltersPanel()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            var presetRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            presetRow.ColumnDefinitions.Add(new ColumnDefinition());
            presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var presetStack = new StackPanel();
            presetStack.Children.Add(Label("View Preset"));
            foreach (var preset in ViewPresets)
                _presetCombo.Items.Add(preset);
            _presetCombo.SelectedIndex = 0;
            _presetCombo.SelectionChanged += (_, _) =>
            {
                if (_presetCombo.SelectedItem is string preset)
                    ApplyPreset(preset);
            };
            presetStack.Children.Add(_presetCombo);
            Grid.SetColumn(presetStack, 0);
            presetRow.Children.Add(presetStack);

            var checksPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            _blockingCheck.Foreground = Brushes.WhiteSmoke;
            _blockingCheck.Checked += (_, _) => RefreshItems();
            _blockingCheck.Unchecked += (_, _) => RefreshItems();
            _debtCheck.Foreground = Brushes.WhiteSmoke;
            _debtCheck.Checked += (_, _) => RefreshItems();
            _debtCheck.Unchecked += (_, _) => RefreshItems();
            checksPanel.Children.Add(_blockingCheck);
            checksPanel.Children.Add(_debtCheck);
            Grid.SetColumn(checksPanel, 2);
            presetRow.Children.Add(checksPanel);

            panel.Children.Add(presetRow);

            panel.Children.Add(Label("Search"));
            _searchBox.Margin = new Thickness(0, 0, 0, 6);
            _searchBox.ToolTip = "Search title, body, tags, status, source";
            _searchBox.TextChanged += (_, _) =>
            {
                // Debounce so each keystroke doesn't query the store and rebind the list.
                _searchDebounce.Stop();
                _searchDebounce.Start();
            };
            panel.Children.Add(_searchBox);

            var filterGrid = new Grid();
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition());
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition());
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition());

            filterGrid.Children.Add(FilterStack("Category", _categoryFilter));
            var statusStack = FilterStack("Status", _statusFilter);
            Grid.SetColumn(statusStack, 2);
            filterGrid.Children.Add(statusStack);
            var kindStack = FilterStack("Kind", _kindFilter);
            Grid.SetColumn(kindStack, 4);
            filterGrid.Children.Add(kindStack);
            panel.Children.Add(filterGrid);

            SeedDropdown(_categoryFilter, "", "navigation", "api", "runtime", "script", "banking", "combat", "scripting", "timing", "inventory", "dungeoneering", "tooling", "meta", "general");
            SeedDropdown(_statusFilter, "", "draft", "open", "active", "investigating", "partial", "answered", "validated", "promoted", "stale", "waiting-user", "deprecated", "archived", "failed");
            SeedDropdown(_kindFilter, "", "observation", "inquiry", "investigation", "evidence", "hypothesis", "answer", "knowledge", "outcome", "note", "fact");

            var filterGrid2 = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            filterGrid2.ColumnDefinitions.Add(new ColumnDefinition());
            filterGrid2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
            filterGrid2.ColumnDefinitions.Add(new ColumnDefinition());

            filterGrid2.Children.Add(FilterStack("Target", _targetFilter));
            var thStack = FilterStack("Time Horizon", _timeHorizonFilter);
            Grid.SetColumn(thStack, 2);
            filterGrid2.Children.Add(thStack);
            panel.Children.Add(filterGrid2);

            SeedDropdown(_targetFilter, "", "self", "future-agent", "user", "tool", "project");
            SeedDropdown(_timeHorizonFilter, "", "short", "medium", "long");

            return panel;
        }

        private UIElement BuildCoreEditor()
        {
            var meta = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            for (var i = 0; i < 4; i++)
                meta.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < 5; i++)
                meta.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddField(meta, "Title", _titleBox, 0, 0, 4);
            AddField(meta, "Kind", _kindBox, 1, 0);
            AddField(meta, "Category", _categoryBox, 1, 1);
            AddField(meta, "Status", _statusBox, 1, 2);
            AddField(meta, "Source", _sourceBox, 1, 3);
            AddField(meta, "Tags (comma-separated)", _tagsBox, 2, 0, 4);
            AddField(meta, "Related type", _relatedTypeBox, 3, 0, 2);
            AddField(meta, "Related id", _relatedIdBox, 3, 2, 2);

            var confidencePanel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            var confHeader = new DockPanel();
            confHeader.Children.Add(Label("Confidence"));
            var confValue = new TextBlock
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                FontFamily = Mono,
                FontSize = 11,
                Foreground = Brushes.LightGray
            };
            DockPanel.SetDock(confValue, Dock.Right);
            confHeader.Children.Add(confValue);
            confidencePanel.Children.Add(confHeader);
            confidencePanel.Children.Add(_confidenceSlider);
            confValue.Text = ((int)_confidenceSlider.Value).ToString();
            _confidenceSlider.ValueChanged += (_, e) => confValue.Text = ((int)e.NewValue).ToString();
            Grid.SetRow(confidencePanel, 4);
            Grid.SetColumnSpan(confidencePanel, 4);
            meta.Children.Add(confidencePanel);

            return meta;
        }

        private UIElement BuildBodySection()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(Label("Body"));
            _bodyBox.AcceptsReturn = true;
            _bodyBox.TextWrapping = TextWrapping.Wrap;
            _bodyBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _bodyBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _bodyBox.SpellCheck.IsEnabled = false;
            _bodyBox.MinHeight = 120;
            _bodyBox.MaxHeight = 360;
            panel.Children.Add(_bodyBox);
            return panel;
        }

        private UIElement BuildExtendedExpander()
        {
            var expander = new Expander
            {
                Header = "Extended Fields",
                Margin = new Thickness(0, 6, 0, 8),
                Foreground = Brushes.WhiteSmoke,
                IsExpanded = false
            };

            var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            for (var i = 0; i < 4; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < 8; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            AddField(grid, "Target", _targetBox, 0, 0, 2);
            AddField(grid, "Time Horizon", _timeHorizonBox, 0, 2);
            AddField(grid, "Mode", _modeBox, 0, 3);

            AddField(grid, "Impact (1-5)", _impactBox, 1, 0);
            AddField(grid, "Urgency (1-5)", _urgencyBox, 1, 1);
            var blockingPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6), VerticalAlignment = VerticalAlignment.Bottom };
            _blockingEdit.Foreground = Brushes.WhiteSmoke;
            blockingPanel.Children.Add(_blockingEdit);
            Grid.SetRow(blockingPanel, 1);
            Grid.SetColumn(blockingPanel, 2);
            Grid.SetColumnSpan(blockingPanel, 2);
            grid.Children.Add(blockingPanel);

            AddField(grid, "Defer Reason", _deferReasonBox, 2, 0, 4);

            _evidenceBox.AcceptsReturn = true;
            _evidenceBox.TextWrapping = TextWrapping.Wrap;
            _evidenceBox.MinHeight = 50;
            _evidenceBox.MaxHeight = 80;
            _evidenceBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            AddField(grid, "Evidence Summary", _evidenceBox, 3, 0, 4);

            AddField(grid, "Related Item IDs (comma-separated)", _relatedItemIdsBox, 4, 0, 4);

            AddField(grid, "Originating Agent", _originatingAgentBox, 5, 0, 2);
            AddField(grid, "Originating Task", _originatingTaskBox, 5, 2, 2);

            AddReadOnlyField(grid, "Validations", _validationsText, 6, 0);
            AddReadOnlyField(grid, "Failures", _failuresText, 6, 1);
            AddReadOnlyField(grid, "Usage Count", _usageText, 6, 2);

            AddReadOnlyField(grid, "Last Reviewed", _lastReviewedText, 7, 0, 2);
            AddReadOnlyField(grid, "Created", _createdText, 7, 2, 2);

            expander.Content = grid;
            return expander;
        }

        private UIElement BuildActionButtons()
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            panel.Children.Add(MakeButton("Save", (_, _) => SaveCurrent(), "Create or update the item with the editor contents."));
            panel.Children.Add(MakeButton("✓ Worked", (_, _) => RecordOutcome(succeeded: true),
                "Record that this item's guidance was applied and helped: creates a linked outcome item, increments validations/usage, and recalculates confidence (same as kb.outcome)."));
            panel.Children.Add(MakeButton("✗ Failed", (_, _) => RecordOutcome(succeeded: false),
                "Record that this item's guidance was applied and did NOT help: creates a linked outcome item, increments failures/usage, and recalculates confidence (same as kb.outcome)."));
            panel.Children.Add(MakeButton("Review", (_, _) => ReviewCurrent(), "Stamp the item as reviewed now (updates Last Reviewed)."));
            panel.Children.Add(MakeButton("Promote", (_, _) => PromoteCurrent(), "Promote an answered item to reusable knowledge: Kind=knowledge, Status=promoted, confidence at least 75."));
            panel.Children.Add(MakeButton("Deprecate", (_, _) => { if (_selected != null) { _statusBox.Text = "deprecated"; SaveCurrent(); } }, "Mark the item as no longer valid (kept for history)."));
            panel.Children.Add(MakeButton("Archive", (_, _) => { if (_selected != null) { _statusBox.Text = "archived"; SaveCurrent(); } }, "Hide the item from day-to-day views without deleting it."));
            panel.Children.Add(MakeButton("Delete", (_, _) => DeleteCurrent(), "Permanently remove the item from the store."));
            return panel;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Data / actions (ported from KnowledgeDashboardWindow)
        // ═══════════════════════════════════════════════════════════════════════

        private void RefreshItems()
        {
            var query = new KnowledgeQuery
            {
                Search = _searchBox.Text,
                Category = (_categoryFilter.SelectedItem as string) is { Length: > 0 } cat ? cat : null,
                Status = (_statusFilter.SelectedItem as string) is { Length: > 0 } st ? st : null,
                Kind = (_kindFilter.SelectedItem as string) is { Length: > 0 } knd ? knd : null,
                Target = (_targetFilter.SelectedItem as string) is { Length: > 0 } tgt ? tgt : null,
                TimeHorizon = (_timeHorizonFilter.SelectedItem as string) is { Length: > 0 } th ? th : null,
                Blocking = _blockingCheck.IsChecked == true ? true : (bool?)null,
                MaxCount = 500
            };

            List<KnowledgeItem> items;
            try
            {
                items = Knowledge.List(query).ToList();
            }
            catch (Exception ex)
            {
                _setStatus($"Knowledge load failed: {ex.Message}");
                return;
            }

            if (_debtCheck.IsChecked == true)
                items = items.Where(IsQuestionDebt).ToList();

            _items = items;
            MergeObservedCategories(items);

            // Rebind while preserving selection and any in-progress editor contents:
            // suppression keeps the rebind (and re-selection of the same item) from
            // clobbering unsaved edits via LoadEditor.
            var previousId = (_itemsList.SelectedItem as KnowledgeItem)?.Id ?? _selected?.Id;
            _suppressSelectionLoad = true;
            try
            {
                _itemsList.ItemsSource = null;
                _itemsList.ItemsSource = _items;
                if (!string.IsNullOrWhiteSpace(previousId))
                {
                    var match = _items.FirstOrDefault(i => string.Equals(i.Id, previousId, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        _itemsList.SelectedItem = match;
                        _selected = match;
                    }
                }
            }
            finally
            {
                _suppressSelectionLoad = false;
            }

            _setStatus($"{_items.Count} knowledge item(s) · {Knowledge.GetStorePath()}");
        }

        /// <summary>
        /// Categories are open-ended (agents invent new ones, e.g. "dungeoneering"); make sure
        /// every category present in the store is offered by the filter and editor dropdowns.
        /// </summary>
        private void MergeObservedCategories(IEnumerable<KnowledgeItem> items)
        {
            foreach (var category in items
                         .Select(i => i.Category)
                         .Where(c => !string.IsNullOrWhiteSpace(c))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!_categoryFilter.Items.Cast<object>().Any(o => string.Equals(o as string, category, StringComparison.OrdinalIgnoreCase)))
                    _categoryFilter.Items.Add(category);
                if (!_categoryBox.Items.Cast<object>().Any(o => string.Equals(o as string, category, StringComparison.OrdinalIgnoreCase)))
                    _categoryBox.Items.Add(category);
            }
        }

        private static bool IsQuestionDebt(KnowledgeItem item)
        {
            var debtStatuses = new[] { "open", "investigating", "partial", "waiting-user" };
            if ((string.Equals(item.Kind, "inquiry", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(item.Kind, "hypothesis", StringComparison.OrdinalIgnoreCase)) &&
                debtStatuses.Any(s => string.Equals(item.Status, s, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (string.Equals(item.Kind, "answer", StringComparison.OrdinalIgnoreCase) && item.ValidationCount == 0)
                return true;
            if (string.Equals(item.Kind, "knowledge", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Status, "stale", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private void ApplyPreset(string preset)
        {
            _blockingCheck.IsChecked = false;
            _debtCheck.IsChecked = false;
            _targetFilter.SelectedIndex = 0;
            _timeHorizonFilter.SelectedIndex = 0;

            switch (preset)
            {
                case "Inquiry Ledger":
                    SelectComboValue(_kindFilter, "inquiry");
                    _statusFilter.SelectedIndex = 0;
                    _categoryFilter.SelectedIndex = 0;
                    break;
                case "Question Debt":
                    _debtCheck.IsChecked = true;
                    _kindFilter.SelectedIndex = 0;
                    _statusFilter.SelectedIndex = 0;
                    _categoryFilter.SelectedIndex = 0;
                    break;
                case "Waiting on User":
                    SelectComboValue(_statusFilter, "waiting-user");
                    _kindFilter.SelectedIndex = 0;
                    _categoryFilter.SelectedIndex = 0;
                    break;
                case "Validated Knowledge":
                    SelectComboValue(_kindFilter, "knowledge");
                    SelectComboValue(_statusFilter, "validated");
                    _categoryFilter.SelectedIndex = 0;
                    break;
                case "Stale Knowledge":
                    SelectComboValue(_statusFilter, "stale");
                    _kindFilter.SelectedIndex = 0;
                    _categoryFilter.SelectedIndex = 0;
                    break;
                case "Recent Outcomes":
                    SelectComboValue(_kindFilter, "outcome");
                    _statusFilter.SelectedIndex = 0;
                    _categoryFilter.SelectedIndex = 0;
                    break;
                default: // All
                    _kindFilter.SelectedIndex = 0;
                    _statusFilter.SelectedIndex = 0;
                    _categoryFilter.SelectedIndex = 0;
                    break;
            }

            RefreshItems();
        }

        private static void SelectComboValue(ComboBox combo, string value)
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i] as string, value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void NewItem()
        {
            _selected = new KnowledgeItem
            {
                Title = "New knowledge item",
                Kind = "fact",
                Category = "general",
                Source = "human",
                Status = "draft",
                Confidence = 50
            };
            _itemsList.SelectedItem = null;
            LoadEditor(_selected);
        }

        /// <summary>
        /// Marks the editor dirty whenever any field changes (suppressed while LoadEditor populates).
        /// </summary>
        private void HookDirtyTracking()
        {
            void Dirty() => SetDirty(true);

            foreach (var box in new[]
                     {
                         _titleBox, _bodyBox, _tagsBox, _relatedTypeBox, _relatedIdBox,
                         _targetBox, _timeHorizonBox, _modeBox, _impactBox, _urgencyBox,
                         _deferReasonBox, _evidenceBox, _relatedItemIdsBox,
                         _originatingAgentBox, _originatingTaskBox
                     })
            {
                box.TextChanged += (_, _) => Dirty();
            }

            foreach (var combo in new[] { _kindBox, _categoryBox, _statusBox, _sourceBox })
            {
                // Editable ComboBoxes bubble the inner TextBox's TextChanged.
                combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                    new TextChangedEventHandler((_, _) => Dirty()));
                combo.SelectionChanged += (_, _) => Dirty();
            }

            _confidenceSlider.ValueChanged += (_, _) => Dirty();
            _blockingEdit.Checked += (_, _) => Dirty();
            _blockingEdit.Unchecked += (_, _) => Dirty();
        }

        private void SetDirty(bool dirty)
        {
            if (dirty && _suppressDirty)
            {
                return;
            }

            _dirty = dirty;
            _dirtyIndicator.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadEditor(KnowledgeItem? item)
        {
            _suppressDirty = true;
            try
            {
                LoadEditorCore(item);
            }
            finally
            {
                _suppressDirty = false;
            }

            SetDirty(false);
        }

        private void LoadEditorCore(KnowledgeItem? item)
        {
            item ??= new KnowledgeItem();
            _titleBox.Text = item.Title;
            _bodyBox.Text = item.Body;
            _kindBox.Text = item.Kind;
            _categoryBox.Text = item.Category;
            _statusBox.Text = item.Status;
            _sourceBox.Text = item.Source;
            _tagsBox.Text = string.Join(", ", item.Tags);
            _relatedTypeBox.Text = item.RelatedType ?? string.Empty;
            _relatedIdBox.Text = item.RelatedId ?? string.Empty;
            _confidenceSlider.Value = item.Confidence;

            _targetBox.Text = item.Target;
            _timeHorizonBox.Text = item.TimeHorizon;
            _modeBox.Text = item.Mode;
            _impactBox.Text = item.Impact.ToString();
            _urgencyBox.Text = item.Urgency.ToString();
            _blockingEdit.IsChecked = item.Blocking;
            _deferReasonBox.Text = item.DeferReason;
            _evidenceBox.Text = item.EvidenceSummary;
            _relatedItemIdsBox.Text = string.Join(", ", item.RelatedItemIds);
            _originatingAgentBox.Text = item.OriginatingAgent;
            _originatingTaskBox.Text = item.OriginatingTask;

            _validationsText.Text = item.ValidationCount.ToString();
            _failuresText.Text = item.FailureCount.ToString();
            _usageText.Text = item.UsageCount.ToString();
            _lastReviewedText.Text = item.LastReviewedAt.HasValue ? item.LastReviewedAt.Value.ToString("yyyy-MM-dd HH:mm UTC") : "–";
            _createdText.Text = item.CreatedUtc.ToString("yyyy-MM-dd HH:mm UTC");
        }

        private void SaveCurrent()
        {
            _selected ??= new KnowledgeItem();
            _selected.Title = _titleBox.Text;
            _selected.Body = _bodyBox.Text;
            _selected.Kind = _kindBox.Text;
            _selected.Category = _categoryBox.Text;
            _selected.Status = _statusBox.Text;
            _selected.Source = _sourceBox.Text;
            _selected.Tags = SplitTags(_tagsBox.Text);
            _selected.RelatedType = _relatedTypeBox.Text;
            _selected.RelatedId = _relatedIdBox.Text;
            _selected.Confidence = (int)_confidenceSlider.Value;
            _selected.Target = _targetBox.Text;
            _selected.TimeHorizon = _timeHorizonBox.Text;
            _selected.Mode = _modeBox.Text;
            _selected.Impact = int.TryParse(_impactBox.Text, out var imp) ? Math.Clamp(imp, 1, 5) : 3;
            _selected.Urgency = int.TryParse(_urgencyBox.Text, out var urg) ? Math.Clamp(urg, 1, 5) : 3;
            _selected.Blocking = _blockingEdit.IsChecked == true;
            _selected.DeferReason = _deferReasonBox.Text;
            _selected.EvidenceSummary = _evidenceBox.Text;
            _selected.RelatedItemIds = SplitTags(_relatedItemIdsBox.Text);
            _selected.OriginatingAgent = _originatingAgentBox.Text;
            _selected.OriginatingTask = _originatingTaskBox.Text;

            try
            {
                _selected = Knowledge.Save(_selected);
                SetDirty(false);
                RefreshItems();
                _itemsList.SelectedItem = _items.FirstOrDefault(i => i.Id == _selected.Id);
                _setStatus($"Saved '{_selected.Title}'.");
            }
            catch (Exception ex)
            {
                _setStatus($"Save failed: {ex.Message}");
            }
        }

        private void ReviewCurrent()
        {
            if (_selected == null || string.IsNullOrWhiteSpace(_selected.Id))
                return;

            _selected.LastReviewedAt = DateTime.UtcNow;
            _selected.UpdatedUtc = DateTime.UtcNow;

            try
            {
                _selected = Knowledge.Save(_selected);
                _lastReviewedText.Text = _selected.LastReviewedAt?.ToString("yyyy-MM-dd HH:mm UTC") ?? string.Empty;
                RefreshItems();
                _setStatus($"Marked '{_selected.Title}' as reviewed.");
            }
            catch (Exception ex)
            {
                _setStatus($"Review failed: {ex.Message}");
            }
        }

        private void PromoteCurrent()
        {
            if (_selected == null || string.IsNullOrWhiteSpace(_selected.Id))
                return;

            try
            {
                var promoted = Knowledge.Promote(_selected.Id, _titleBox.Text, _bodyBox.Text, (int)Math.Max(_confidenceSlider.Value, 75));
                RefreshItems();
                _itemsList.SelectedItem = _items.FirstOrDefault(i => i.Id == promoted.Id);
                _setStatus($"Promoted '{_selected.Title}' to reusable knowledge.");
            }
            catch (Exception ex)
            {
                _setStatus($"Promote failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Manual equivalent of the kb.outcome MCP tool: records an application result against
        /// the stored item (not the editor's unsaved contents), creates a linked outcome item,
        /// and recalculates confidence from validations vs failures.
        /// </summary>
        private void RecordOutcome(bool succeeded)
        {
            if (_selected == null || string.IsNullOrWhiteSpace(_selected.Id) ||
                !Knowledge.TryGet(_selected.Id, out var stored) || stored == null)
            {
                _setStatus("Save the item before recording an outcome.");
                return;
            }

            try
            {
                var now = DateTime.UtcNow;
                var outcome = Knowledge.Save(new KnowledgeItem
                {
                    Kind = "outcome",
                    Title = $"Outcome: {stored.Title}",
                    Body = $"Recorded manually from the MCP dashboard: applying this item {(succeeded ? "worked" : "failed")}.",
                    Status = succeeded ? "validated" : "failed",
                    Category = stored.Category,
                    Tags = stored.Tags.ToList(),
                    Source = "human",
                    RelatedItemIds = new List<string> { stored.Id },
                    OriginatingTask = "McpKnowledgePanel"
                });

                if (succeeded)
                {
                    stored.ValidationCount++;
                    stored.LastValidatedAt = now;
                }
                else
                {
                    stored.FailureCount++;
                }

                stored.UsageCount++;

                var total = stored.ValidationCount + stored.FailureCount;
                if (total >= 2)
                {
                    stored.Confidence = Math.Clamp((int)Math.Round(100.0 * stored.ValidationCount / total), 5, 99);
                }

                if (!stored.RelatedItemIds.Contains(outcome.Id, StringComparer.OrdinalIgnoreCase))
                    stored.RelatedItemIds.Add(outcome.Id);

                stored = Knowledge.Save(stored);

                // Refresh the read-only counters (and confidence) without clobbering unsaved edits.
                _selected = stored;
                _validationsText.Text = stored.ValidationCount.ToString();
                _failuresText.Text = stored.FailureCount.ToString();
                _usageText.Text = stored.UsageCount.ToString();
                if (!_dirty)
                {
                    LoadEditor(stored);
                }

                RefreshItems();
                _setStatus($"Recorded {(succeeded ? "success" : "failure")} for '{stored.Title}' (conf {stored.Confidence}).");
            }
            catch (Exception ex)
            {
                _setStatus($"Outcome failed: {ex.Message}");
            }
        }

        private void DeleteCurrent()
        {
            if (_selected == null || string.IsNullOrWhiteSpace(_selected.Id))
                return;

            var title = _selected.Title;
            var confirm = MessageBox.Show(
                $"Permanently delete '{title}'?\n\nDeprecate or Archive keeps history; Delete cannot be undone.",
                "Delete knowledge item",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes)
                return;

            if (!Knowledge.Delete(_selected.Id))
            {
                _setStatus("Delete failed: item was not found.");
                return;
            }

            _selected = null;
            LoadEditor(null);
            RefreshItems();
            _setStatus($"Deleted '{title}'.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  List item template (status dots + status-driven text styling)
        // ═══════════════════════════════════════════════════════════════════════

        private DataTemplate BuildListItemTemplate()
        {
            var template = new DataTemplate();
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
            borderFactory.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 2));

            var gridFactory = new FrameworkElementFactory(typeof(Grid));
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var col2 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col2.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            gridFactory.AppendChild(col1);
            gridFactory.AppendChild(col2);

            var dotFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            dotFactory.SetValue(System.Windows.Shapes.Ellipse.WidthProperty, 8.0);
            dotFactory.SetValue(System.Windows.Shapes.Ellipse.HeightProperty, 8.0);
            dotFactory.SetValue(System.Windows.Shapes.Ellipse.MarginProperty, new Thickness(0, 0, 8, 0));
            dotFactory.SetValue(System.Windows.Shapes.Ellipse.VerticalAlignmentProperty, VerticalAlignment.Center);
            dotFactory.SetValue(System.Windows.Shapes.Ellipse.StyleProperty, BuildStatusDotStyle());
            dotFactory.SetValue(Grid.ColumnProperty, 0);

            var contentFactory = new FrameworkElementFactory(typeof(StackPanel));
            contentFactory.SetValue(Grid.ColumnProperty, 1);

            var titleFactory = new FrameworkElementFactory(typeof(TextBlock));
            titleFactory.SetBinding(TextBlock.TextProperty, new DataBinding("Title"));
            titleFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

            var titleStyle = new Style(typeof(TextBlock));
            titleStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));

            var promotedTrigger = new DataTrigger { Binding = new DataBinding("Status"), Value = "promoted" };
            promotedTrigger.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
            promotedTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(102, 187, 106))));
            titleStyle.Triggers.Add(promotedTrigger);

            var staleTrigger = new DataTrigger { Binding = new DataBinding("Status"), Value = "stale" };
            staleTrigger.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
            staleTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(158, 158, 158))));
            titleStyle.Triggers.Add(staleTrigger);

            var archivedTrigger = new DataTrigger { Binding = new DataBinding("Status"), Value = "archived" };
            archivedTrigger.Setters.Add(new Setter(TextBlock.OpacityProperty, 0.4));
            titleStyle.Triggers.Add(archivedTrigger);

            titleFactory.SetValue(TextBlock.StyleProperty, titleStyle);

            var subFactory = new FrameworkElementFactory(typeof(StackPanel));
            subFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            subFactory.SetValue(StackPanel.MarginProperty, new Thickness(0, 2, 0, 0));

            var catLabel = CreateSubLabel("Category");
            var kindLabel = CreateSubLabel("Kind");
            var statusLabel = CreateSubLabel("Status");
            var confLabel = new FrameworkElementFactory(typeof(TextBlock));
            confLabel.SetBinding(TextBlock.TextProperty, new DataBinding("Confidence") { StringFormat = "conf {0}" });
            confLabel.SetValue(TextBlock.FontFamilyProperty, Mono);
            confLabel.SetValue(TextBlock.FontSizeProperty, 10.0);
            confLabel.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(TextDim));
            kindLabel.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
            statusLabel.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
            confLabel.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
            subFactory.AppendChild(catLabel);
            subFactory.AppendChild(kindLabel);
            subFactory.AppendChild(statusLabel);
            subFactory.AppendChild(confLabel);

            var blockingLabel = new FrameworkElementFactory(typeof(TextBlock));
            blockingLabel.SetValue(TextBlock.TextProperty, "● BLOCKING");
            blockingLabel.SetValue(TextBlock.FontFamilyProperty, Mono);
            blockingLabel.SetValue(TextBlock.FontSizeProperty, 10.0);
            blockingLabel.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(244, 67, 54)));
            blockingLabel.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 0, 0));
            var blockingStyle = new Style(typeof(TextBlock));
            blockingStyle.Setters.Add(new Setter(TextBlock.VisibilityProperty, Visibility.Collapsed));
            var blockingTrigger = new DataTrigger { Binding = new DataBinding("Blocking"), Value = true };
            blockingTrigger.Setters.Add(new Setter(TextBlock.VisibilityProperty, Visibility.Visible));
            blockingStyle.Triggers.Add(blockingTrigger);
            blockingLabel.SetValue(TextBlock.StyleProperty, blockingStyle);
            subFactory.AppendChild(blockingLabel);

            contentFactory.AppendChild(titleFactory);
            contentFactory.AppendChild(subFactory);

            gridFactory.AppendChild(dotFactory);
            gridFactory.AppendChild(contentFactory);
            borderFactory.AppendChild(gridFactory);
            template.VisualTree = borderFactory;
            return template;
        }

        private static FrameworkElementFactory CreateSubLabel(string bindingPath)
        {
            var f = new FrameworkElementFactory(typeof(TextBlock));
            f.SetBinding(TextBlock.TextProperty, new DataBinding(bindingPath));
            f.SetValue(TextBlock.FontFamilyProperty, Mono);
            f.SetValue(TextBlock.FontSizeProperty, 10.0);
            f.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(TextDim));
            return f;
        }

        private static Style BuildListItemContainerStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.WhiteSmoke));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Stretch));
            return style;
        }

        private static Style BuildStatusDotStyle()
        {
            var style = new Style(typeof(System.Windows.Shapes.Ellipse));
            style.Setters.Add(new Setter(System.Windows.Shapes.Ellipse.FillProperty, new SolidColorBrush(Color.FromRgb(120, 144, 156))));
            AddStatusTrigger(style, "open", Color.FromRgb(33, 150, 243));
            AddStatusTrigger(style, "investigating", Color.FromRgb(255, 235, 59));
            AddStatusTrigger(style, "partial", Color.FromRgb(255, 193, 7));
            AddStatusTrigger(style, "waiting-user", Color.FromRgb(255, 152, 0));
            AddStatusTrigger(style, "answered", Color.FromRgb(158, 158, 158));
            AddStatusTrigger(style, "validated", Color.FromRgb(76, 175, 80));
            AddStatusTrigger(style, "promoted", Color.FromRgb(102, 187, 106));
            AddStatusTrigger(style, "stale", Color.FromRgb(158, 158, 158));
            AddStatusTrigger(style, "archived", Color.FromRgb(84, 110, 122));
            AddStatusTrigger(style, "draft", Color.FromRgb(120, 144, 156));
            AddStatusTrigger(style, "active", Color.FromRgb(66, 165, 245));
            AddStatusTrigger(style, "failed", Color.FromRgb(211, 47, 47));
            AddStatusTrigger(style, "deprecated", Color.FromRgb(239, 83, 80));

            var blockingDotTrigger = new DataTrigger
            {
                Binding = new DataBinding("Blocking"),
                Value = true
            };
            blockingDotTrigger.Setters.Add(new Setter(System.Windows.Shapes.Ellipse.FillProperty, new SolidColorBrush(Color.FromRgb(244, 67, 54))));
            style.Triggers.Add(blockingDotTrigger);

            return style;
        }

        private static void AddStatusTrigger(Style style, string status, Color color)
        {
            var trigger = new DataTrigger
            {
                Binding = new DataBinding(nameof(KnowledgeItem.Status)),
                Value = status
            };
            trigger.Setters.Add(new Setter(System.Windows.Shapes.Ellipse.FillProperty, new SolidColorBrush(color)));
            style.Triggers.Add(trigger);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Small helpers
        // ═══════════════════════════════════════════════════════════════════════

        private void SeedDropdown(ComboBox combo, params string[] values)
        {
            combo.Items.Clear();
            foreach (var v in values)
                combo.Items.Add(v);
            combo.SelectedIndex = 0;
            combo.SelectionChanged += (_, _) => RefreshItems();
        }

        private static StackPanel FilterStack(string label, ComboBox combo)
            => new() { Children = { Label(label), combo } };

        private static void AddField(Grid grid, string label, Control box, int row, int column, int columnSpan = 1)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 6, 6) };
            panel.Children.Add(Label(label));
            panel.Children.Add(box);
            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
            Grid.SetColumnSpan(panel, columnSpan);
            grid.Children.Add(panel);
        }

        private static void AddReadOnlyField(Grid grid, string label, TextBlock value, int row, int column, int columnSpan = 1)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 6, 6) };
            panel.Children.Add(Label(label));
            panel.Children.Add(value);
            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
            Grid.SetColumnSpan(panel, columnSpan);
            grid.Children.Add(panel);
        }

        private static TextBlock ReadOnlyValue() => new()
        {
            FontFamily = Mono,
            FontSize = 11,
            Foreground = Brushes.LightGray
        };

        private static TextBlock Label(string text) => new()
        {
            Text = text,
            Foreground = new SolidColorBrush(TextDim),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        };

        private static Button MakeButton(string text, RoutedEventHandler handler, string? toolTip = null)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 80,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 4),
                ToolTip = toolTip
            };
            button.Click += handler;
            return button;
        }

        private static List<string> SplitTags(string text)
            => (text ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
    }
}
