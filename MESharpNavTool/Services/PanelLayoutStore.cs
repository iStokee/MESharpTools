using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MESharp.Services
{
    public static class PanelLayoutStore
    {
        private static readonly object SyncRoot = new object();
        private static readonly string LayoutDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MESharp",
            "WPFScript");
        private static readonly string LayoutFilePath = Path.Combine(LayoutDirectory, "panel-layouts.json");
        private static readonly string LayoutStateFilePath = Path.Combine(LayoutDirectory, "panel-layouts-v2.json");

        private static Dictionary<string, List<string>>? _layouts;
        private static Dictionary<string, PageLayoutState>? _layoutStates;

        public static IReadOnlyList<string> GetOrder(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
            {
                return Array.Empty<string>();
            }

            lock (SyncRoot)
            {
                EnsureLoaded();
                if (_layouts != null && _layouts.TryGetValue(pageKey, out var order))
                {
                    return order.ToList();
                }
            }

            return Array.Empty<string>();
        }

        public static void SaveOrder(string pageKey, IEnumerable<string> orderedPanelKeys)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
            {
                return;
            }

            var keys = orderedPanelKeys?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? new List<string>();

            lock (SyncRoot)
            {
                EnsureLoaded();
                _layouts![pageKey] = keys;
                PersistLegacy();
            }
        }

        public static void RemoveOrder(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
            {
                return;
            }

            lock (SyncRoot)
            {
                EnsureLoaded();
                if (_layouts != null && _layouts.Remove(pageKey))
                {
                    PersistLegacy();
                }
            }
        }

        public static IReadOnlyList<PanelPlacementState> GetPlacements(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
            {
                return Array.Empty<PanelPlacementState>();
            }

            lock (SyncRoot)
            {
                EnsureStateLoaded();
                if (_layoutStates != null && _layoutStates.TryGetValue(pageKey, out var state))
                {
                    return state.Panels
                        .OrderBy(x => x.Order)
                        .Select(ClonePlacement)
                        .ToList();
                }
            }

            return Array.Empty<PanelPlacementState>();
        }

        public static void SavePlacements(string pageKey, IEnumerable<PanelPlacementState> placements)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
            {
                return;
            }

            var normalized = (placements ?? Array.Empty<PanelPlacementState>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.PanelKey))
                .GroupBy(x => x.PanelKey, StringComparer.Ordinal)
                .Select(g => g.OrderBy(x => x.Order).First())
                .OrderBy(x => x.Order)
                .Select((x, idx) => new PanelPlacementState
                {
                    PanelKey = x.PanelKey,
                    ColumnKey = string.IsNullOrWhiteSpace(x.ColumnKey) ? "default" : x.ColumnKey,
                    Order = idx,
                    Height = SanitizeHeight(x.Height),
                    IsExpanded = x.IsExpanded
                })
                .ToList();

            lock (SyncRoot)
            {
                EnsureStateLoaded();
                var state = GetOrCreateState(pageKey);
                state.Panels = normalized;
                PersistState();
            }
        }

        public static void RemovePlacements(string pageKey)
        {
            if (string.IsNullOrWhiteSpace(pageKey))
            {
                return;
            }

            lock (SyncRoot)
            {
                EnsureStateLoaded();
                if (_layoutStates != null && _layoutStates.Remove(pageKey))
                {
                    PersistState();
                }
            }
        }

        public static IReadOnlyList<string> GetColumnWidths(string layoutKey)
        {
            if (string.IsNullOrWhiteSpace(layoutKey))
            {
                return Array.Empty<string>();
            }

            lock (SyncRoot)
            {
                EnsureStateLoaded();
                if (_layoutStates != null &&
                    _layoutStates.TryGetValue(layoutKey, out var state) &&
                    state.ColumnWidths.Count > 0)
                {
                    return state.ColumnWidths.ToList();
                }
            }

            return Array.Empty<string>();
        }

        public static void SaveColumnWidths(string layoutKey, IEnumerable<string> widths)
        {
            if (string.IsNullOrWhiteSpace(layoutKey))
            {
                return;
            }

            var normalized = (widths ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            lock (SyncRoot)
            {
                EnsureStateLoaded();
                var state = GetOrCreateState(layoutKey);
                state.ColumnWidths = normalized;
                PersistState();
            }
        }

        private static PanelPlacementState ClonePlacement(PanelPlacementState source)
        {
            return new PanelPlacementState
            {
                PanelKey = source.PanelKey,
                ColumnKey = source.ColumnKey,
                Order = source.Order,
                Height = source.Height,
                IsExpanded = source.IsExpanded
            };
        }

        private static double? SanitizeHeight(double? height)
        {
            if (!height.HasValue || double.IsNaN(height.Value) || double.IsInfinity(height.Value) || height.Value <= 0)
            {
                return null;
            }

            return Math.Round(height.Value, 2, MidpointRounding.AwayFromZero);
        }

        private static PageLayoutState GetOrCreateState(string key)
        {
            if (_layoutStates == null)
            {
                _layoutStates = new Dictionary<string, PageLayoutState>(StringComparer.Ordinal);
            }

            if (!_layoutStates.TryGetValue(key, out var state))
            {
                state = new PageLayoutState();
                _layoutStates[key] = state;
            }

            return state;
        }

        private static void EnsureLoaded()
        {
            if (_layouts != null)
            {
                return;
            }

            try
            {
                if (!File.Exists(LayoutFilePath))
                {
                    _layouts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    return;
                }

                var json = File.ReadAllText(LayoutFilePath);
                _layouts = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)
                           ?? new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }
            catch
            {
                _layouts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            }
        }

        private static void EnsureStateLoaded()
        {
            if (_layoutStates != null)
            {
                return;
            }

            try
            {
                if (!File.Exists(LayoutStateFilePath))
                {
                    _layoutStates = new Dictionary<string, PageLayoutState>(StringComparer.Ordinal);
                    return;
                }

                var json = File.ReadAllText(LayoutStateFilePath);
                _layoutStates = JsonSerializer.Deserialize<Dictionary<string, PageLayoutState>>(json)
                                ?? new Dictionary<string, PageLayoutState>(StringComparer.Ordinal);

                foreach (var kvp in _layoutStates)
                {
                    kvp.Value.Panels ??= new List<PanelPlacementState>();
                    kvp.Value.ColumnWidths ??= new List<string>();
                }
            }
            catch
            {
                _layoutStates = new Dictionary<string, PageLayoutState>(StringComparer.Ordinal);
            }
        }

        private static void PersistLegacy()
        {
            try
            {
                Directory.CreateDirectory(LayoutDirectory);
                var json = JsonSerializer.Serialize(_layouts, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(LayoutFilePath, json);
            }
            catch
            {
                // Keep UI responsive; layout persistence failures should never break the app.
            }
        }

        private static void PersistState()
        {
            try
            {
                Directory.CreateDirectory(LayoutDirectory);
                var json = JsonSerializer.Serialize(_layoutStates, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(LayoutStateFilePath, json);
            }
            catch
            {
                // Keep UI responsive; layout persistence failures should never break the app.
            }
        }

        public sealed class PanelPlacementState
        {
            public string PanelKey { get; set; } = string.Empty;
            public string ColumnKey { get; set; } = "default";
            public int Order { get; set; }
            public double? Height { get; set; }
            public bool? IsExpanded { get; set; }
        }

        private sealed class PageLayoutState
        {
            public List<PanelPlacementState> Panels { get; set; } = new List<PanelPlacementState>();
            public List<string> ColumnWidths { get; set; } = new List<string>();
        }
    }
}
