using System;
using System.Collections.Generic;
using System.Linq;
using MESharp.API;
using MESharp.Models;

namespace MESharp.Services
{
    /// <summary>
    /// Thin adapter between the Routes pane and the shared <see cref="Webwalking"/> route store.
    /// All persistence goes through the engine's per-route upsert/delete (lock-guarded, atomic,
    /// refuses core-route overwrites and unreadable stores) — the tool never rewrites the whole
    /// routes.json itself, so it can no longer clobber routes saved by the map server, the Graph
    /// pane recorder, or other sessions.
    /// </summary>
    internal static class RouteStore
    {
        public static string? LastError { get; private set; }

        /// <summary>Catalog snapshot: built-in core routes plus everything in routes.json.</summary>
        public static IReadOnlyList<RouteDefinition> Load()
        {
            LastError = null;
            Webwalking.ReloadRoutes();

            var merged = new Dictionary<string, RouteDefinition>(StringComparer.OrdinalIgnoreCase);

            static string BuildKey(RouteDefinition route) =>
                !string.IsNullOrWhiteSpace(route.Id) ? $"id:{route.Id.Trim()}" : $"name:{route.Name.Trim()}";

            foreach (var core in NavigationRouteSeeds.GetCoreRoutes())
            {
                core.Normalize();
                merged[BuildKey(core)] = core;
            }

            foreach (var stored in Webwalking.GetStoredRoutes())
            {
                var route = ConvertFromStored(stored);
                if (route != null && !string.IsNullOrWhiteSpace(route.Name))
                {
                    merged[BuildKey(route)] = route;
                }
            }

            LastError = Webwalking.LastLoadError;
            return merged.Values.ToList();
        }

        /// <summary>Insert or update a single route in the shared store.</summary>
        public static bool TryUpsert(RouteDefinition route, out string? error)
        {
            LastError = null;
            if (route == null)
            {
                error = LastError = "Route is null.";
                return false;
            }

            route.Normalize();
            if (!Webwalking.TrySaveRoute(ConvertToStored(route), out error))
            {
                LastError = error;
                return false;
            }

            return true;
        }

        /// <summary>Delete a single route from the shared store. Core routes are refused by the engine.</summary>
        public static bool TryDelete(RouteDefinition route, out string? error)
        {
            LastError = null;
            var key = !string.IsNullOrWhiteSpace(route?.Id) ? route!.Id : route?.Name ?? string.Empty;
            if (!Webwalking.TryDeleteRoute(key, out error))
            {
                LastError = error;
                return false;
            }

            return true;
        }

        public static bool IsCoreRoute(RouteDefinition? route)
        {
            if (route == null) return false;
            return Webwalking.GetCoreRoutes().Any(core =>
                string.Equals(core.Id, route.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(core.Name, route.Name, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetStorePath() => Webwalking.GetRouteStorePath();

        private static RouteDefinition? ConvertFromStored(WebwalkingStoredRoute? route)
        {
            if (route == null) return null;

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
