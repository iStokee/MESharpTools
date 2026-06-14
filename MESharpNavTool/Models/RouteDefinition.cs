using System;
using System.Collections.Generic;

namespace MESharp.Models
{
    public class RouteDefinition
    {
        public const int CurrentSchemaVersion = 3;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public List<string> Tags { get; set; } = new();
        public List<RouteWaypoint> Waypoints { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime SavedAt { get; set; } = DateTime.UtcNow;

        public void Normalize()
        {
            if (SchemaVersion <= 0)
            {
                SchemaVersion = CurrentSchemaVersion;
            }

            Id ??= string.Empty;
            Name ??= string.Empty;
            Description ??= string.Empty;
            Category ??= string.Empty;
            Tags ??= new List<string>();
            Waypoints ??= new List<RouteWaypoint>();

            if (string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name))
            {
                Id = Name.Trim().ToLowerInvariant().Replace(' ', '_');
            }

            if (CreatedAt == default)
            {
                CreatedAt = SavedAt == default ? DateTime.UtcNow : SavedAt;
            }

            if (SavedAt == default)
            {
                SavedAt = DateTime.UtcNow;
            }

            foreach (var waypoint in Waypoints)
            {
                waypoint?.Normalize();
            }
        }
    }

    public class RouteWaypoint
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int AreaRadius { get; set; } = 0;
        public int ArrivalDistance { get; set; } = 2;
        public int TimeoutMs { get; set; } = 8000;
        public int JitterTiles { get; set; } = 1;
        public bool ChainWhileMoving { get; set; } = true;
        public bool IsTransition { get; set; }
        public List<int> TransitionObjectIds { get; set; } = new();

        public void Normalize()
        {
            Id ??= string.Empty;
            Label ??= string.Empty;
            AreaRadius = Math.Clamp(AreaRadius, 0, 25);
            ArrivalDistance = Math.Clamp(ArrivalDistance, 0, 25);
            TimeoutMs = Math.Clamp(TimeoutMs, 1000, 120000);
            JitterTiles = Math.Clamp(JitterTiles, 0, 8);
            TransitionObjectIds ??= new List<int>();

            if (string.IsNullOrWhiteSpace(Id))
            {
                Id = Guid.NewGuid().ToString("N");
            }
        }

        public bool IsWithinArea(int x, int y, int z)
        {
            if (z != Z)
            {
                return false;
            }

            var radius = Math.Max(0, AreaRadius);
            return Math.Abs(X - x) <= radius && Math.Abs(Y - y) <= radius;
        }

        public override string ToString()
        {
            var areaTag = AreaRadius > 0 ? $" r{AreaRadius}" : string.Empty;
            var labelTag = string.IsNullOrWhiteSpace(Label) ? string.Empty : $" [{Label}]";
            return $"{X},{Y},{Z}{areaTag}{labelTag}";
        }
    }
}
