using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MESharp.API;
using MESharp.Models;

namespace MESharp.Services
{
    internal static class RouteStore
    {
        private static readonly string RoutesDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MESharp");
        private static readonly string RoutesFile = Path.Combine(RoutesDirectory, "routes.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string? LastError { get; private set; }

        public static IReadOnlyList<RouteDefinition> Load()
        {
            var loadedRoutes = new List<RouteDefinition>();
            LastError = null;

            try
            {
                if (!File.Exists(RoutesFile))
                {
                    return MergeWithCoreRoutes(loadedRoutes);
                }

                var json = File.ReadAllText(RoutesFile);
                var stored = JsonSerializer.Deserialize<List<WebwalkingStoredRoute>>(json);
                if (stored == null)
                {
                    return MergeWithCoreRoutes(loadedRoutes);
                }

                foreach (var route in stored)
                {
                    route?.Normalize();
                }

                loadedRoutes = stored
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Name))
                    .Select(ConvertFromStored)
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Name))
                    .ToList();
            }
            catch (Exception ex)
            {
                loadedRoutes = new List<RouteDefinition>();
                LastError = $"Route load failed: {ex.Message}";
            }

            return MergeWithCoreRoutes(loadedRoutes);
        }

        public static void Save(IEnumerable<RouteDefinition> routes)
        {
            _ = TrySave(routes, out _);
        }

        public static bool TrySave(IEnumerable<RouteDefinition> routes, out string? error)
        {
            LastError = null;
            error = null;
            try
            {
                Directory.CreateDirectory(RoutesDirectory);
                var normalized = (routes ?? Array.Empty<RouteDefinition>())
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Name))
                    .Select(r =>
                    {
                        r.Normalize();
                        return r;
                    })
                    .ToList();

                var stored = normalized.Select(ConvertToStored).ToList();
                var json = JsonSerializer.Serialize(stored, JsonOptions);

                var tmpFile = RoutesFile + ".tmp";
                File.WriteAllText(tmpFile, json);

                if (File.Exists(RoutesFile))
                {
                    File.Replace(tmpFile, RoutesFile, null);
                }
                else
                {
                    File.Move(tmpFile, RoutesFile);
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Route save failed: {ex.Message}";
                LastError = error;
                try
                {
                    var tmpFile = RoutesFile + ".tmp";
                    if (File.Exists(tmpFile))
                    {
                        File.Delete(tmpFile);
                    }
                }
                catch
                {
                    // ignore cleanup errors
                }

                return false;
            }
        }

        public static string GetStorePath() => RoutesFile;

        private static IReadOnlyList<RouteDefinition> MergeWithCoreRoutes(IEnumerable<RouteDefinition> loadedRoutes)
        {
            var merged = new Dictionary<string, RouteDefinition>(StringComparer.OrdinalIgnoreCase);

            static string BuildKey(RouteDefinition route)
            {
                if (!string.IsNullOrWhiteSpace(route.Id))
                {
                    return $"id:{route.Id.Trim()}";
                }

                return $"name:{route.Name.Trim()}";
            }

            foreach (var core in NavigationRouteSeeds.GetCoreRoutes())
            {
                core.Normalize();
                merged[BuildKey(core)] = core;
            }

            foreach (var route in loadedRoutes ?? Array.Empty<RouteDefinition>())
            {
                route.Normalize();
                merged[BuildKey(route)] = route;
            }

            return merged.Values.ToList();
        }

        private static RouteDefinition ConvertFromStored(WebwalkingStoredRoute route)
        {
            var converted = new RouteDefinition
            {
                SchemaVersion = route.SchemaVersion,
                Id = route.Id,
                Name = route.Name,
                Description = route.Description,
                Category = route.Category,
                IsEnabled = route.IsEnabled,
                Tags = route.Tags?.ToList() ?? new List<string>(),
                CreatedAt = route.CreatedAt,
                SavedAt = route.SavedAt,
                Waypoints = (route.Waypoints ?? new List<WebwalkingStoredWaypoint>())
                    .Select(wp => new RouteWaypoint
                    {
                        Id = wp.Id,
                        Label = wp.Label,
                        X = wp.X,
                        Y = wp.Y,
                        Z = wp.Z,
                        AreaRadius = wp.AreaRadius,
                        ArrivalDistance = wp.ArrivalDistance,
                        TimeoutMs = wp.TimeoutMs,
                        JitterTiles = wp.JitterTiles,
                        ChainWhileMoving = wp.ChainWhileMoving,
                        IsTransition = wp.IsTransition,
                        TransitionObjectIds = wp.TransitionObjectIds?.ToList() ?? new List<int>()
                    })
                    .ToList()
            };

            converted.Normalize();
            return converted;
        }

        private static WebwalkingStoredRoute ConvertToStored(RouteDefinition route)
        {
            var stored = new WebwalkingStoredRoute
            {
                SchemaVersion = route.SchemaVersion,
                Id = route.Id,
                Name = route.Name,
                Description = route.Description,
                Category = route.Category,
                IsEnabled = route.IsEnabled,
                Tags = route.Tags?.ToList() ?? new List<string>(),
                CreatedAt = route.CreatedAt,
                SavedAt = route.SavedAt,
                Waypoints = (route.Waypoints ?? new List<RouteWaypoint>())
                    .Select(wp => new WebwalkingStoredWaypoint
                    {
                        Id = wp.Id,
                        Label = wp.Label,
                        X = wp.X,
                        Y = wp.Y,
                        Z = wp.Z,
                        AreaRadius = wp.AreaRadius,
                        ArrivalDistance = wp.ArrivalDistance,
                        TimeoutMs = wp.TimeoutMs,
                        JitterTiles = wp.JitterTiles,
                        ChainWhileMoving = wp.ChainWhileMoving,
                        IsTransition = wp.IsTransition,
                        TransitionObjectIds = wp.TransitionObjectIds?.ToList() ?? new List<int>()
                    })
                    .ToList()
            };

            stored.Normalize();
            return stored;
        }

    }
}
