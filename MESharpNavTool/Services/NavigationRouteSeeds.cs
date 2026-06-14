using System.Collections.Generic;
using System.Linq;
using MESharp.API;
using MESharp.Models;

namespace MESharp.Services
{
    internal static class NavigationRouteSeeds
    {
        public static IReadOnlyList<RouteDefinition> GetCoreRoutes()
            => Webwalking.GetCoreRoutes().Select(ToRouteDefinition).ToList();

        private static RouteDefinition ToRouteDefinition(WebwalkingRoute r)
        {
            var def = new RouteDefinition
            {
                SchemaVersion = RouteDefinition.CurrentSchemaVersion,
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                Category = r.Category,
                IsEnabled = r.IsEnabled,
                Tags = r.Tags.ToList(),
                Waypoints = r.Waypoints.Select(ToRouteWaypoint).ToList()
            };
            def.Normalize();
            return def;
        }

        private static RouteWaypoint ToRouteWaypoint(WebwalkingWaypoint wp) => new()
        {
            Label = wp.Label,
            X = wp.Point.X,
            Y = wp.Point.Y,
            Z = wp.Point.Z,
            AreaRadius = wp.AreaRadius,
            ArrivalDistance = wp.ArrivalDistance,
            TimeoutMs = wp.TimeoutMs,
            JitterTiles = wp.JitterTiles,
            ChainWhileMoving = wp.ChainWhileMoving,
            IsTransition = wp.IsTransition,
            TransitionObjectIds = wp.TransitionObjectIds?.ToList() ?? new()
        };
    }
}
