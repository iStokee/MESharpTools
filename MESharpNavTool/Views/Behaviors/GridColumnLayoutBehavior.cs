using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MESharp.Services;

namespace MESharp.Views.Behaviors
{
    public static class GridColumnLayoutBehavior
    {
        public static readonly DependencyProperty LayoutKeyProperty =
            DependencyProperty.RegisterAttached(
                "LayoutKey",
                typeof(string),
                typeof(GridColumnLayoutBehavior),
                new PropertyMetadata(null, OnLayoutKeyChanged));

        public static readonly DependencyProperty ColumnIndexesProperty =
            DependencyProperty.RegisterAttached(
                "ColumnIndexes",
                typeof(string),
                typeof(GridColumnLayoutBehavior),
                new PropertyMetadata("0,2"));

        private static readonly DependencyProperty TrackerProperty =
            DependencyProperty.RegisterAttached(
                "Tracker",
                typeof(GridLayoutTracker),
                typeof(GridColumnLayoutBehavior),
                new PropertyMetadata(null));

        public static string GetLayoutKey(DependencyObject obj) => (string)obj.GetValue(LayoutKeyProperty);
        public static void SetLayoutKey(DependencyObject obj, string value) => obj.SetValue(LayoutKeyProperty, value);

        public static string GetColumnIndexes(DependencyObject obj) => (string)obj.GetValue(ColumnIndexesProperty);
        public static void SetColumnIndexes(DependencyObject obj, string value) => obj.SetValue(ColumnIndexesProperty, value);

        private static void OnLayoutKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Grid grid)
            {
                return;
            }

            grid.Loaded -= OnGridLoaded;
            if (!string.IsNullOrWhiteSpace(e.NewValue as string))
            {
                grid.Loaded += OnGridLoaded;
            }
        }

        private static void OnGridLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Grid grid)
            {
                return;
            }

            var layoutKey = GetLayoutKey(grid);
            if (string.IsNullOrWhiteSpace(layoutKey))
            {
                return;
            }

            ApplySavedColumnWidths(grid, layoutKey);
            AttachSplitters(grid);
        }

        private static void AttachSplitters(Grid grid)
        {
            var tracker = grid.GetValue(TrackerProperty) as GridLayoutTracker;
            if (tracker == null)
            {
                tracker = new GridLayoutTracker();
                grid.SetValue(TrackerProperty, tracker);
            }

            if (tracker.SplitterEventsAttached)
            {
                return;
            }

            foreach (var splitter in grid.Children.OfType<GridSplitter>())
            {
                splitter.DragCompleted -= OnGridSplitterDragCompleted;
                splitter.DragCompleted += OnGridSplitterDragCompleted;
            }

            tracker.SplitterEventsAttached = true;
        }

        private static void OnGridSplitterDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (sender is not GridSplitter splitter || splitter.Parent is not Grid grid)
            {
                return;
            }

            SaveColumnWidths(grid);
        }

        private static void ApplySavedColumnWidths(Grid grid, string layoutKey)
        {
            var saved = PanelLayoutStore.GetColumnWidths(layoutKey);
            if (saved.Count == 0)
            {
                return;
            }

            var indexes = ParseIndexes(GetColumnIndexes(grid));
            if (indexes.Count == 0)
            {
                return;
            }

            var limit = Math.Min(indexes.Count, saved.Count);
            for (int i = 0; i < limit; i++)
            {
                var colIndex = indexes[i];
                if (colIndex < 0 || colIndex >= grid.ColumnDefinitions.Count)
                {
                    continue;
                }

                if (TryParseGridLength(saved[i], out var width))
                {
                    grid.ColumnDefinitions[colIndex].Width = width;
                }
            }
        }

        private static void SaveColumnWidths(Grid grid)
        {
            var layoutKey = GetLayoutKey(grid);
            if (string.IsNullOrWhiteSpace(layoutKey))
            {
                return;
            }

            var indexes = ParseIndexes(GetColumnIndexes(grid));
            if (indexes.Count == 0)
            {
                return;
            }

            var widths = new List<string>(indexes.Count);
            foreach (var colIndex in indexes)
            {
                if (colIndex < 0 || colIndex >= grid.ColumnDefinitions.Count)
                {
                    continue;
                }

                widths.Add(SerializeGridLength(grid.ColumnDefinitions[colIndex].Width));
            }

            if (widths.Count > 0)
            {
                PanelLayoutStore.SaveColumnWidths(layoutKey, widths);
            }
        }

        private static List<int> ParseIndexes(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<int>();
            }

            return raw
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) ? idx : -1)
                .Where(x => x >= 0)
                .Distinct()
                .ToList();
        }

        private static string SerializeGridLength(GridLength width)
        {
            if (width.IsAuto)
            {
                return "Auto";
            }

            if (width.IsStar)
            {
                return $"{width.Value.ToString(CultureInfo.InvariantCulture)}*";
            }

            return width.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryParseGridLength(string raw, out GridLength width)
        {
            width = GridLength.Auto;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            var trimmed = raw.Trim();
            if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                width = GridLength.Auto;
                return true;
            }

            if (trimmed.EndsWith("*", StringComparison.Ordinal))
            {
                var starRaw = trimmed.Substring(0, trimmed.Length - 1);
                if (string.IsNullOrWhiteSpace(starRaw))
                {
                    width = new GridLength(1, GridUnitType.Star);
                    return true;
                }

                if (double.TryParse(starRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var starValue) && starValue > 0)
                {
                    width = new GridLength(starValue, GridUnitType.Star);
                    return true;
                }

                return false;
            }

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixelValue) && pixelValue > 0)
            {
                width = new GridLength(pixelValue, GridUnitType.Pixel);
                return true;
            }

            return false;
        }

        private sealed class GridLayoutTracker
        {
            public bool SplitterEventsAttached { get; set; }
        }
    }
}
