using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MESharp.Services;

namespace MESharp.Views.Behaviors
{
    public static class PanelLayoutBehavior
    {
        private const string DragFormatPrefix = "MESharp.PanelLayout.PanelKey";
        private const double DefaultMinPanelHeight = 220d;
        private const double ResizeHandleHeight = 10d;
        private static readonly Thickness DefaultPanelSpacing = new(0, 0, 0, 10);

        private static readonly Dictionary<string, List<Panel>> PanelsByPageKey = new(StringComparer.Ordinal);
        private static readonly HashSet<string> PendingApplyPages = new(StringComparer.Ordinal);
        private static readonly HashSet<string> ApplyingPages = new(StringComparer.Ordinal);
        private static readonly object ApplyingPagesSync = new();

        public static readonly DependencyProperty PageKeyProperty =
            DependencyProperty.RegisterAttached(
                "PageKey",
                typeof(string),
                typeof(PanelLayoutBehavior),
                new PropertyMetadata(null, OnPageKeyChanged));

        public static readonly DependencyProperty PanelKeyProperty =
            DependencyProperty.RegisterAttached(
                "PanelKey",
                typeof(string),
                typeof(PanelLayoutBehavior),
                new PropertyMetadata(null));

        public static readonly DependencyProperty ColumnKeyProperty =
            DependencyProperty.RegisterAttached(
                "ColumnKey",
                typeof(string),
                typeof(PanelLayoutBehavior),
                new PropertyMetadata("default"));

        public static readonly DependencyProperty MinPanelHeightProperty =
            DependencyProperty.RegisterAttached(
                "MinPanelHeight",
                typeof(double),
                typeof(PanelLayoutBehavior),
                new PropertyMetadata(DefaultMinPanelHeight));

        public static readonly DependencyProperty IsHeightResizableProperty =
            DependencyProperty.RegisterAttached(
                "IsHeightResizable",
                typeof(bool),
                typeof(PanelLayoutBehavior),
                new PropertyMetadata(true));

        private static readonly DependencyProperty TrackerProperty =
            DependencyProperty.RegisterAttached(
                "Tracker",
                typeof(LayoutTracker),
                typeof(PanelLayoutBehavior),
                new PropertyMetadata(null));

        private static readonly DependencyProperty ExpanderTrackerAttachedProperty =
            DependencyProperty.RegisterAttached(
                "ExpanderTrackerAttached",
                typeof(bool),
                typeof(PanelLayoutBehavior),
                new PropertyMetadata(false));

        private static readonly DependencyProperty AutoPanelMarginAppliedProperty =
            DependencyProperty.RegisterAttached(
                "AutoPanelMarginApplied",
                typeof(bool),
                typeof(PanelLayoutBehavior),
                new PropertyMetadata(false));

        public static string GetPageKey(DependencyObject obj) => (string)obj.GetValue(PageKeyProperty);
        public static void SetPageKey(DependencyObject obj, string value) => obj.SetValue(PageKeyProperty, value);

        public static string GetPanelKey(DependencyObject obj) => (string)obj.GetValue(PanelKeyProperty);
        public static void SetPanelKey(DependencyObject obj, string value) => obj.SetValue(PanelKeyProperty, value);

        public static string GetColumnKey(DependencyObject obj) => (string)obj.GetValue(ColumnKeyProperty);
        public static void SetColumnKey(DependencyObject obj, string value) => obj.SetValue(ColumnKeyProperty, value);

        public static double GetMinPanelHeight(DependencyObject obj) => (double)obj.GetValue(MinPanelHeightProperty);
        public static void SetMinPanelHeight(DependencyObject obj, double value) => obj.SetValue(MinPanelHeightProperty, value);

        public static bool GetIsHeightResizable(DependencyObject obj) => (bool)obj.GetValue(IsHeightResizableProperty);
        public static void SetIsHeightResizable(DependencyObject obj, bool value) => obj.SetValue(IsHeightResizableProperty, value);

        private static void OnPageKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Panel panel)
            {
                return;
            }

            var previousPageKey = e.OldValue as string;
            if (!string.IsNullOrWhiteSpace(previousPageKey))
            {
                UnregisterPanel(previousPageKey, panel);
            }

            panel.Loaded -= OnPanelLoaded;
            panel.Unloaded -= OnPanelUnloaded;

            if (!string.IsNullOrWhiteSpace(e.NewValue as string))
            {
                panel.Loaded += OnPanelLoaded;
                panel.Unloaded += OnPanelUnloaded;
            }
        }

        private static void OnPanelLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Panel panel)
            {
                return;
            }

            var pageKey = GetPageKey(panel);
            if (string.IsNullOrWhiteSpace(pageKey))
            {
                return;
            }

            var tracker = panel.GetValue(TrackerProperty) as LayoutTracker;
            if (tracker == null)
            {
                tracker = new LayoutTracker
                {
                    PageKey = pageKey,
                    DefaultOrder = GetOrderedKeyedChildren(panel).Select(GetPanelKey).ToList()
                };
                panel.SetValue(TrackerProperty, tracker);
            }
            else
            {
                tracker.PageKey = pageKey;
            }

            RegisterPanel(pageKey, panel);
            AttachPanelMenus(panel);
            AttachDragDrop(panel);
            AttachExpansionTracking(panel);
            ScheduleApplyPageLayout(pageKey, panel.Dispatcher);
        }

        private static void OnPanelUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Panel panel)
            {
                return;
            }

            var pageKey = GetPageKey(panel);
            if (!string.IsNullOrWhiteSpace(pageKey))
            {
                UnregisterPanel(pageKey, panel);
            }
        }

        private static void RegisterPanel(string pageKey, Panel panel)
        {
            lock (PanelsByPageKey)
            {
                if (!PanelsByPageKey.TryGetValue(pageKey, out var panels))
                {
                    panels = new List<Panel>();
                    PanelsByPageKey[pageKey] = panels;
                }

                panels.RemoveAll(x => x == null || !x.IsLoaded);
                if (!panels.Contains(panel))
                {
                    panels.Add(panel);
                }
            }
        }

        private static void UnregisterPanel(string pageKey, Panel panel)
        {
            lock (PanelsByPageKey)
            {
                if (!PanelsByPageKey.TryGetValue(pageKey, out var panels))
                {
                    return;
                }

                panels.RemoveAll(x => x == null || x == panel || !x.IsLoaded);
                if (panels.Count == 0)
                {
                    PanelsByPageKey.Remove(pageKey);
                }
            }
        }

        private static List<Panel> GetPanelsForPage(string pageKey)
        {
            lock (PanelsByPageKey)
            {
                if (!PanelsByPageKey.TryGetValue(pageKey, out var panels))
                {
                    return new List<Panel>();
                }

                panels.RemoveAll(x => x == null || !x.IsLoaded);
                return panels.ToList();
            }
        }

        private static void ScheduleApplyPageLayout(string pageKey, Dispatcher dispatcher)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
            {
                return;
            }

            lock (PendingApplyPages)
            {
                if (!PendingApplyPages.Add(pageKey))
                {
                    return;
                }
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                lock (PendingApplyPages)
                {
                    PendingApplyPages.Remove(pageKey);
                }

                ApplySavedLayout(pageKey);
            }), DispatcherPriority.Loaded);
        }

        private static void ApplySavedLayout(string pageKey)
        {
            var panels = GetPanelsForPage(pageKey);
            if (panels.Count == 0)
            {
                return;
            }

            BeginApplying(pageKey);
            try
            {
                var placements = PanelLayoutStore.GetPlacements(pageKey);
                if (placements.Count == 0)
                {
                    ApplyLegacySinglePanelOrderFallback(pageKey, panels);
                    return;
                }

                // Be resilient to malformed persisted data with duplicate panel keys.
                var placementByKey = placements
                    .GroupBy(x => x.PanelKey, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Order).First(), StringComparer.Ordinal);
                var allKeyed = panels
                    .SelectMany(GetOrderedKeyedChildren)
                    .GroupBy(GetPanelKey, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .ToList();

                var elementByKey = allKeyed.ToDictionary(GetPanelKey, x => x, StringComparer.Ordinal);
                var panelByColumn = panels
                    .GroupBy(GetColumnKey, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                var plannedByPanel = panels.ToDictionary(x => x, _ => new List<UIElement>());

                foreach (var placement in placements.OrderBy(x => x.Order))
                {
                    if (!elementByKey.TryGetValue(placement.PanelKey, out var child))
                    {
                        continue;
                    }

                    if (!panelByColumn.TryGetValue(placement.ColumnKey, out var destinationPanel))
                    {
                        destinationPanel = panels[0];
                    }

                    plannedByPanel[destinationPanel].Add(child);
                    elementByKey.Remove(placement.PanelKey);
                }

                foreach (var child in elementByKey.Values)
                {
                    var currentParent = GetParentPanel(child);
                    if (currentParent != null && plannedByPanel.TryGetValue(currentParent, out var plan))
                    {
                        plan.Add(child);
                    }
                    else
                    {
                        plannedByPanel[panels[0]].Add(child);
                    }
                }

                foreach (var panel in panels)
                {
                    ApplyOrderedKeyedChildren(panel, plannedByPanel[panel]);
                    AttachPanelMenus(panel);
                    AttachDragDrop(panel);
                    AttachExpansionTracking(panel);
                }

                // Always clear stale heights on panels that are marked non-resizable,
                // even if there is no persisted placement entry for them.
                foreach (var panel in panels)
                {
                    foreach (var child in GetOrderedKeyedChildren(panel).OfType<FrameworkElement>())
                    {
                        if (!GetIsHeightResizable(child))
                        {
                            child.ClearValue(FrameworkElement.HeightProperty);
                        }
                    }
                }

                foreach (var panel in panels)
                {
                    foreach (var child in GetOrderedKeyedChildren(panel).OfType<FrameworkElement>())
                    {
                        var key = GetPanelKey(child);
                        if (!placementByKey.TryGetValue(key, out var placement))
                        {
                            continue;
                        }

                        if (GetIsHeightResizable(child) && placement.Height.HasValue)
                        {
                            child.Height = Math.Max(GetMinPanelHeight(child), placement.Height.Value);
                        }
                        else if (!GetIsHeightResizable(child))
                        {
                            child.ClearValue(FrameworkElement.HeightProperty);
                        }

                        if (placement.IsExpanded.HasValue)
                        {
                            var expander = FindFirstExpander(child);
                            if (expander != null)
                            {
                                expander.IsExpanded = placement.IsExpanded.Value;
                            }
                        }
                    }
                }
            }
            finally
            {
                EndApplying(pageKey);
            }
        }

        private static void ApplyLegacySinglePanelOrderFallback(string pageKey, IReadOnlyList<Panel> panels)
        {
            if (panels.Count != 1)
            {
                return;
            }

            var panel = panels[0];
            var saved = PanelLayoutStore.GetOrder(pageKey);
            if (saved.Count == 0)
            {
                return;
            }

            var currentKeyed = GetOrderedKeyedChildren(panel).ToList();
            if (currentKeyed.Count <= 1)
            {
                return;
            }

            var keyedById = currentKeyed.ToDictionary(GetPanelKey, x => x, StringComparer.Ordinal);
            var ordered = new List<UIElement>();

            foreach (var key in saved)
            {
                if (keyedById.TryGetValue(key, out var child))
                {
                    ordered.Add(child);
                    keyedById.Remove(key);
                }
            }

            ordered.AddRange(currentKeyed.Where(x => keyedById.ContainsKey(GetPanelKey(x))));
            ApplyOrderedKeyedChildren(panel, ordered);
        }

        private static void AttachPanelMenus(Panel panel)
        {
            foreach (var child in GetOrderedKeyedChildren(panel).OfType<FrameworkElement>())
            {
                if (child.ContextMenu != null)
                {
                    continue;
                }

                var menu = new ContextMenu();

                var moveUp = new MenuItem { Header = "Move Panel Up" };
                moveUp.Click += (_, __) => MovePanel(GetCurrentOrFallbackPanel(child, panel), child, -1);

                var moveDown = new MenuItem { Header = "Move Panel Down" };
                moveDown.Click += (_, __) => MovePanel(GetCurrentOrFallbackPanel(child, panel), child, +1);

                var resetHeight = new MenuItem { Header = "Reset Panel Height" };
                resetHeight.Click += (_, __) =>
                {
                    child.ClearValue(FrameworkElement.HeightProperty);
                    SaveCurrentLayout(GetCurrentOrFallbackPanel(child, panel));
                };

                var reset = new MenuItem { Header = "Reset Panel Order" };
                reset.Click += (_, __) => ResetPanelOrder(GetCurrentOrFallbackPanel(child, panel));

                menu.Items.Add(moveUp);
                menu.Items.Add(moveDown);
                menu.Items.Add(new Separator());
                menu.Items.Add(resetHeight);
                menu.Items.Add(new Separator());
                menu.Items.Add(reset);

                menu.Opened += (_, __) =>
                {
                    var activePanel = GetCurrentOrFallbackPanel(child, panel);
                    var keyed = GetOrderedKeyedChildren(activePanel).ToList();
                    var idx = keyed.IndexOf(child);
                    moveUp.IsEnabled = idx > 0;
                    moveDown.IsEnabled = idx >= 0 && idx < keyed.Count - 1;
                };

                child.ContextMenu = menu;
                // Don't overwrite a tooltip the panel already carries.
                if (child.ToolTip == null)
                {
                    child.ToolTip = "Drag to reorder, drag near bottom edge to resize, or right-click for panel actions";
                }
            }
        }

        private static void AttachDragDrop(Panel panel)
        {
            var tracker = panel.GetValue(TrackerProperty) as LayoutTracker;
            if (tracker == null)
            {
                return;
            }

            if (!tracker.MouseEventsAttached)
            {
                panel.PreviewMouseLeftButtonDown += OnPanelPreviewMouseLeftButtonDown;
                panel.PreviewMouseLeftButtonUp += OnPanelPreviewMouseLeftButtonUp;
                panel.PreviewMouseMove += OnPanelPreviewMouseMove;
                tracker.MouseEventsAttached = true;
            }

            panel.AllowDrop = true;
            panel.DragOver -= OnPanelDragOver;
            panel.Drop -= OnPanelDrop;
            panel.DragOver += OnPanelDragOver;
            panel.Drop += OnPanelDrop;

            foreach (var child in GetOrderedKeyedChildren(panel).OfType<FrameworkElement>())
            {
                child.AllowDrop = true;
                child.DragOver -= OnPanelChildDragOver;
                child.Drop -= OnPanelChildDrop;
                child.DragOver += OnPanelChildDragOver;
                child.Drop += OnPanelChildDrop;
            }
        }

        private static void AttachExpansionTracking(Panel panel)
        {
            foreach (var child in GetOrderedKeyedChildren(panel).OfType<FrameworkElement>())
            {
                if ((bool)child.GetValue(ExpanderTrackerAttachedProperty))
                {
                    continue;
                }

                var expander = FindFirstExpander(child);
                if (expander == null)
                {
                    continue;
                }

                expander.Expanded += (_, __) => SaveCurrentLayout(panel);
                expander.Collapsed += (_, __) => SaveCurrentLayout(panel);
                child.SetValue(ExpanderTrackerAttachedProperty, true);
            }
        }

        private static void OnPanelPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Panel panel)
            {
                return;
            }

            var tracker = panel.GetValue(TrackerProperty) as LayoutTracker;
            if (tracker == null)
            {
                return;
            }

            tracker.DragStartPoint = e.GetPosition(panel);
            tracker.PendingDragPanelKey = null;

            var clicked = FindAncestorWithPanelKey(e.OriginalSource as DependencyObject);
            if (clicked == null)
            {
                return;
            }

            if (IsInteractiveControlInPath(e.OriginalSource as DependencyObject, clicked))
            {
                return;
            }

            var clickPos = e.GetPosition(clicked);
            if (GetIsHeightResizable(clicked) && IsInResizeGrip(clicked, clickPos))
            {
                tracker.IsResizing = true;
                tracker.ResizingElement = clicked;
                tracker.ResizeStartPoint = e.GetPosition(panel);
                tracker.ResizeStartHeight = double.IsNaN(clicked.Height) ? clicked.ActualHeight : clicked.Height;
                panel.CaptureMouse();
                panel.Cursor = Cursors.SizeNS;
                e.Handled = true;
                return;
            }

            tracker.PendingDragPanelKey = GetPanelKey(clicked);
        }

        private static void OnPanelPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Panel panel)
            {
                return;
            }

            var tracker = panel.GetValue(TrackerProperty) as LayoutTracker;
            if (tracker == null)
            {
                return;
            }

            if (tracker.IsResizing)
            {
                EndResize(panel, tracker, save: true);
                e.Handled = true;
                return;
            }

            panel.ClearValue(FrameworkElement.CursorProperty);
        }

        private static void OnPanelPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not Panel panel)
            {
                return;
            }

            var tracker = panel.GetValue(TrackerProperty) as LayoutTracker;
            if (tracker == null)
            {
                return;
            }

            if (tracker.IsResizing)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    EndResize(panel, tracker, save: true);
                    return;
                }

                if (tracker.ResizingElement == null)
                {
                    EndResize(panel, tracker, save: false);
                    return;
                }

                var delta = e.GetPosition(panel).Y - tracker.ResizeStartPoint.Y;
                var minHeight = GetMinPanelHeight(tracker.ResizingElement);
                var nextHeight = Math.Max(minHeight, tracker.ResizeStartHeight + delta);
                tracker.ResizingElement.Height = nextHeight;
                panel.Cursor = Cursors.SizeNS;
                e.Handled = true;
                return;
            }

            var hovered = FindAncestorWithPanelKey(e.OriginalSource as DependencyObject);
            if (hovered != null &&
                !IsInteractiveControlInPath(e.OriginalSource as DependencyObject, hovered) &&
                IsInResizeGrip(hovered, e.GetPosition(hovered)))
            {
                panel.Cursor = Cursors.SizeNS;
            }
            else
            {
                panel.ClearValue(FrameworkElement.CursorProperty);
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(tracker.PendingDragPanelKey))
            {
                return;
            }

            var pos = e.GetPosition(panel);
            if (Math.Abs(pos.X - tracker.DragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - tracker.DragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            var sourceKey = tracker.PendingDragPanelKey;
            tracker.PendingDragPanelKey = null;

            var pageKey = GetPageKey(panel);
            if (string.IsNullOrWhiteSpace(pageKey))
            {
                return;
            }

            var data = new DataObject(GetDragFormat(pageKey), sourceKey);
            DragDrop.DoDragDrop(panel, data, DragDropEffects.Move);
        }

        private static void OnPanelDragOver(object sender, DragEventArgs e)
        {
            if (sender is not Panel panel)
            {
                return;
            }

            if (!TryGetDragSourceKey(panel, e, out var sourceKey))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = FindElementByPanelKey(GetPageKey(panel), sourceKey) != null
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private static void OnPanelDrop(object sender, DragEventArgs e)
        {
            if (sender is not Panel panel)
            {
                return;
            }

            if (!TryGetDragSourceKey(panel, e, out var sourceKey))
            {
                return;
            }

            MovePanelKey(pageKey: GetPageKey(panel), sourceKey: sourceKey, targetPanel: panel, dropTarget: null, insertAfter: false);
            e.Handled = true;
        }

        private static void OnPanelChildDragOver(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement target || target.Parent is not Panel panel)
            {
                return;
            }

            if (!TryGetDragSourceKey(panel, e, out var sourceKey))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var targetKey = GetPanelKey(target);
            e.Effects = !string.IsNullOrWhiteSpace(sourceKey) && !string.Equals(sourceKey, targetKey, StringComparison.Ordinal)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        private static void OnPanelChildDrop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement target || target.Parent is not Panel panel)
            {
                return;
            }

            if (!TryGetDragSourceKey(panel, e, out var sourceKey))
            {
                return;
            }

            var dropPos = e.GetPosition(target);
            var insertAfter = dropPos.Y > target.ActualHeight / 2d;
            MovePanelKey(pageKey: GetPageKey(panel), sourceKey: sourceKey, targetPanel: panel, dropTarget: target, insertAfter: insertAfter);
            e.Handled = true;
        }

        private static bool TryGetDragSourceKey(Panel panel, DragEventArgs e, out string sourceKey)
        {
            sourceKey = string.Empty;
            var pageKey = GetPageKey(panel);
            if (string.IsNullOrWhiteSpace(pageKey))
            {
                return false;
            }

            var format = GetDragFormat(pageKey);
            if (!e.Data.GetDataPresent(format))
            {
                return false;
            }

            sourceKey = e.Data.GetData(format) as string ?? string.Empty;
            return !string.IsNullOrWhiteSpace(sourceKey);
        }

        private static void MovePanelKey(string pageKey, string sourceKey, Panel targetPanel, UIElement? dropTarget, bool insertAfter)
        {
            if (string.IsNullOrWhiteSpace(pageKey) || string.IsNullOrWhiteSpace(sourceKey))
            {
                return;
            }

            var source = FindElementByPanelKey(pageKey, sourceKey);
            if (source == null)
            {
                return;
            }

            var sourcePanel = GetParentPanel(source);
            if (sourcePanel == null)
            {
                return;
            }

            var sourceOrdered = GetOrderedKeyedChildren(sourcePanel).ToList();
            sourceOrdered.Remove(source);

            var targetOrdered = sourcePanel == targetPanel
                ? sourceOrdered
                : GetOrderedKeyedChildren(targetPanel).ToList();

            var insertIndex = targetOrdered.Count;
            if (dropTarget != null)
            {
                var targetIndex = targetOrdered.IndexOf(dropTarget);
                if (targetIndex >= 0)
                {
                    insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
                }
            }

            if (insertIndex < 0)
            {
                insertIndex = 0;
            }
            if (insertIndex > targetOrdered.Count)
            {
                insertIndex = targetOrdered.Count;
            }

            targetOrdered.Insert(insertIndex, source);

            ApplyOrderedKeyedChildren(targetPanel, targetOrdered);
            AttachPanelMenus(targetPanel);
            AttachDragDrop(targetPanel);
            AttachExpansionTracking(targetPanel);
            if (sourcePanel != targetPanel)
            {
                ApplyOrderedKeyedChildren(sourcePanel, sourceOrdered);
                AttachPanelMenus(sourcePanel);
                AttachDragDrop(sourcePanel);
                AttachExpansionTracking(sourcePanel);
            }

            SaveCurrentLayout(targetPanel);
        }

        private static FrameworkElement? FindElementByPanelKey(string pageKey, string panelKey)
        {
            var panels = GetPanelsForPage(pageKey);
            foreach (var panel in panels)
            {
                foreach (var child in GetOrderedKeyedChildren(panel).OfType<FrameworkElement>())
                {
                    if (string.Equals(GetPanelKey(child), panelKey, StringComparison.Ordinal))
                    {
                        return child;
                    }
                }
            }

            return null;
        }

        private static void MovePanel(Panel panel, UIElement child, int delta)
        {
            var keyed = GetOrderedKeyedChildren(panel).ToList();
            var currentIndex = keyed.IndexOf(child);
            if (currentIndex < 0)
            {
                return;
            }

            var targetIndex = currentIndex + delta;
            if (targetIndex < 0 || targetIndex >= keyed.Count)
            {
                return;
            }

            (keyed[currentIndex], keyed[targetIndex]) = (keyed[targetIndex], keyed[currentIndex]);
            ApplyOrderedKeyedChildren(panel, keyed);
            SaveCurrentLayout(panel);
        }

        private static void ResetPanelOrder(Panel panel)
        {
            var tracker = panel.GetValue(TrackerProperty) as LayoutTracker;
            if (tracker == null || tracker.DefaultOrder.Count == 0)
            {
                return;
            }

            var currentByKey = GetOrderedKeyedChildren(panel).ToDictionary(GetPanelKey, x => x, StringComparer.Ordinal);
            var ordered = new List<UIElement>();

            foreach (var key in tracker.DefaultOrder)
            {
                if (currentByKey.TryGetValue(key, out var child))
                {
                    ordered.Add(child);
                    currentByKey.Remove(key);
                }
            }

            ordered.AddRange(currentByKey.Values);
            ApplyOrderedKeyedChildren(panel, ordered);
            PanelLayoutStore.RemovePlacements(tracker.PageKey);
            PanelLayoutStore.RemoveOrder(tracker.PageKey);
        }

        private static void SaveCurrentLayout(Panel panel)
        {
            var pageKey = GetPageKey(panel);
            if (string.IsNullOrWhiteSpace(pageKey) || IsApplying(pageKey))
            {
                return;
            }

            var panels = GetPanelsForPage(pageKey);
            if (panels.Count == 0)
            {
                panels.Add(panel);
            }

            var placements = new List<PanelLayoutStore.PanelPlacementState>();
            var order = 0;
            foreach (var pagePanel in panels)
            {
                var columnKey = string.IsNullOrWhiteSpace(GetColumnKey(pagePanel)) ? "default" : GetColumnKey(pagePanel);
                foreach (var child in GetOrderedKeyedChildren(pagePanel).OfType<FrameworkElement>())
                {
                    var key = GetPanelKey(child);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    var expander = FindFirstExpander(child);
                    placements.Add(new PanelLayoutStore.PanelPlacementState
                    {
                        PanelKey = key,
                        ColumnKey = columnKey,
                        Order = order++,
                        Height = GetIsHeightResizable(child) ? GetSavedHeight(child) : null,
                        IsExpanded = expander?.IsExpanded
                    });
                }
            }

            // v2 placements are the authoritative store; the legacy order file is only
            // read as a fallback for layouts saved before v2 existed, never written.
            PanelLayoutStore.SavePlacements(pageKey, placements);
        }

        private static double? GetSavedHeight(FrameworkElement element)
        {
            if (double.IsNaN(element.Height) || double.IsInfinity(element.Height) || element.Height <= 0)
            {
                return null;
            }

            return element.Height;
        }

        private static IEnumerable<UIElement> GetOrderedKeyedChildren(Panel panel)
        {
            var keyed = panel.Children.Cast<UIElement>()
                .Where(x => !string.IsNullOrWhiteSpace(GetPanelKey(x)));

            if (panel is Grid)
            {
                return keyed
                    .OrderBy(Grid.GetRow)
                    .ThenBy(Grid.GetColumn)
                    .ToList();
            }

            return keyed.ToList();
        }

        private static void ApplyOrderedKeyedChildren(Panel panel, IReadOnlyList<UIElement> orderedKeyed)
        {
            if (panel is Grid grid)
            {
                ApplyOrderedGridChildren(grid, orderedKeyed);
                return;
            }

            ApplyOrderedFlowChildren(panel, orderedKeyed);
        }

        private static void ApplyOrderedGridChildren(Grid grid, IReadOnlyList<UIElement> orderedKeyed)
        {
            ApplyDefaultPanelSpacing(orderedKeyed);

            var rowSlots = GetOrderedKeyedChildren(grid)
                .Select(Grid.GetRow)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            if (rowSlots.Count == 0)
            {
                rowSlots.Add(0);
            }

            while (rowSlots.Count < orderedKeyed.Count)
            {
                var nextRow = rowSlots[rowSlots.Count - 1] + 1;
                rowSlots.Add(nextRow);
                while (grid.RowDefinitions.Count <= nextRow)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }
            }

            var currentKeyed = GetOrderedKeyedChildren(grid).ToList();
            foreach (var child in currentKeyed)
            {
                if (!orderedKeyed.Contains(child))
                {
                    grid.Children.Remove(child);
                }
            }

            for (int i = 0; i < orderedKeyed.Count; i++)
            {
                var child = orderedKeyed[i];
                var parent = GetParentPanel(child);
                if (parent != null && parent != grid)
                {
                    parent.Children.Remove(child);
                }

                if (GetParentPanel(child) != grid)
                {
                    grid.Children.Add(child);
                }

                Grid.SetRow(child, rowSlots[i]);
            }
        }

        private static void ApplyOrderedFlowChildren(Panel panel, IReadOnlyList<UIElement> orderedKeyed)
        {
            ApplyDefaultPanelSpacing(orderedKeyed);

            var originalChildren = panel.Children.Cast<UIElement>().ToList();
            var keyedQueue = new Queue<UIElement>(orderedKeyed);
            var rebuilt = new List<UIElement>(originalChildren.Count + Math.Max(0, orderedKeyed.Count - originalChildren.Count));

            // Preserve non-keyed element positions (headers, static controls) and only remap keyed slots.
            foreach (var original in originalChildren)
            {
                if (string.IsNullOrWhiteSpace(GetPanelKey(original)))
                {
                    rebuilt.Add(original);
                    continue;
                }

                if (keyedQueue.Count > 0)
                {
                    rebuilt.Add(keyedQueue.Dequeue());
                }
            }

            // If new keyed panels were inserted from another column, append remaining keyed items.
            while (keyedQueue.Count > 0)
            {
                rebuilt.Add(keyedQueue.Dequeue());
            }

            panel.Children.Clear();

            foreach (var child in rebuilt)
            {
                var parent = GetParentPanel(child);
                if (parent != null && parent != panel)
                {
                    parent.Children.Remove(child);
                }

                if (GetParentPanel(child) != panel)
                {
                    panel.Children.Add(child);
                }
            }
        }

        private static void ApplyDefaultPanelSpacing(IEnumerable<UIElement> elements)
        {
            foreach (var child in elements.OfType<FrameworkElement>())
            {
                var localMargin = child.ReadLocalValue(FrameworkElement.MarginProperty);
                if (localMargin == DependencyProperty.UnsetValue)
                {
                    child.Margin = DefaultPanelSpacing;
                    child.SetValue(AutoPanelMarginAppliedProperty, true);
                    continue;
                }

                var autoMarginApplied = (bool)child.GetValue(AutoPanelMarginAppliedProperty);
                if (!autoMarginApplied || child.Margin != new Thickness(0))
                {
                    continue;
                }

                child.Margin = DefaultPanelSpacing;
                child.SetValue(AutoPanelMarginAppliedProperty, true);
            }
        }

        private static Panel? GetParentPanel(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent is Panel visualPanel)
            {
                return visualPanel;
            }

            if (child is FrameworkElement fe && fe.Parent is Panel logicalPanel)
            {
                return logicalPanel;
            }

            return null;
        }

        private static bool IsInResizeGrip(FrameworkElement panelElement, Point relativePos)
        {
            return panelElement.ActualHeight > 0 && relativePos.Y >= panelElement.ActualHeight - ResizeHandleHeight;
        }

        private static bool IsInteractiveControlInPath(DependencyObject? start, DependencyObject panelElement)
        {
            var current = start;
            while (current != null && current != panelElement)
            {
                if (current is TextBoxBase ||
                    current is ComboBox ||
                    current is Selector ||
                    current is ButtonBase ||
                    current is Slider ||
                    current is ScrollBar ||
                    current is Thumb ||
                    current is DataGrid ||
                    current is DataGridColumnHeader ||
                    current is PasswordBox)
                {
                    return true;
                }

                current = GetParentObject(current);
            }

            return false;
        }

        private static DependencyObject? GetParentObject(DependencyObject child)
        {
            var visualParent = VisualTreeHelper.GetParent(child);
            if (visualParent != null)
            {
                return visualParent;
            }

            if (child is FrameworkElement fe)
            {
                return fe.Parent;
            }

            if (child is FrameworkContentElement fce)
            {
                return fce.Parent;
            }

            return null;
        }

        private static void EndResize(Panel panel, LayoutTracker tracker, bool save)
        {
            tracker.IsResizing = false;
            tracker.ResizingElement = null;
            tracker.PendingDragPanelKey = null;

            if (Mouse.Captured == panel)
            {
                Mouse.Capture(null);
            }

            panel.ClearValue(FrameworkElement.CursorProperty);

            if (save)
            {
                SaveCurrentLayout(panel);
            }
        }

        private static Panel GetCurrentOrFallbackPanel(FrameworkElement child, Panel fallback)
        {
            return GetParentPanel(child) ?? fallback;
        }

        private static void BeginApplying(string pageKey)
        {
            lock (ApplyingPagesSync)
            {
                ApplyingPages.Add(pageKey);
            }
        }

        private static void EndApplying(string pageKey)
        {
            lock (ApplyingPagesSync)
            {
                ApplyingPages.Remove(pageKey);
            }
        }

        private static bool IsApplying(string pageKey)
        {
            lock (ApplyingPagesSync)
            {
                return ApplyingPages.Contains(pageKey);
            }
        }

        private static string GetDragFormat(string pageKey) => $"{DragFormatPrefix}:{pageKey}";

        private static FrameworkElement? FindAncestorWithPanelKey(DependencyObject? start)
        {
            var current = start;
            while (current != null)
            {
                if (current is FrameworkElement fe && !string.IsNullOrWhiteSpace(GetPanelKey(fe)))
                {
                    return fe;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static Expander? FindFirstExpander(DependencyObject root)
        {
            if (root is Expander expander)
            {
                return expander;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var match = FindFirstExpander(child);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private sealed class LayoutTracker
        {
            public string PageKey { get; set; } = string.Empty;
            public List<string> DefaultOrder { get; set; } = new();
            public bool MouseEventsAttached { get; set; }
            public Point DragStartPoint { get; set; }
            public string? PendingDragPanelKey { get; set; }
            public bool IsResizing { get; set; }
            public FrameworkElement? ResizingElement { get; set; }
            public Point ResizeStartPoint { get; set; }
            public double ResizeStartHeight { get; set; }
        }
    }
}
