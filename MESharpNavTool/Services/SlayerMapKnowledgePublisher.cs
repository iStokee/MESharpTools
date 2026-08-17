using System.Text.Json;
using System.IO;
using MESharp.API;
using MESharp.Slayer;

namespace MESharp.Services;

public static class SlayerMapKnowledgePublisher
{
    public static int Publish(string snapshotPath, Action<string>? log = null, SlayerKnowledgeRepository? repository = null)
    {
        if (!File.Exists(snapshotPath)) return 0;
        repository ??= new SlayerKnowledgeRepository();
        var snapshot = JsonSerializer.Deserialize<WikiMapCatalogSnapshot>(File.ReadAllText(snapshotPath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        if (snapshot is null) return 0;

        var pack = repository.LoadKnowledge();
        if (pack.Assignments.Count == 0)
        {
            log?.Invoke("Slayer knowledge pack is empty; map snapshot retained for later reconciliation.");
            return 0;
        }

        var assignmentByFeature = BuildFeatureIndex(pack);
        var locations = pack.Locations.ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);
        var published = 0;
        foreach (var entry in snapshot.Entries.Where(e => string.IsNullOrWhiteSpace(e.Error)))
        {
            foreach (var map in entry.Maps)
            {
                foreach (var feature in map.Features.Where(f => f.Coordinates.Count > 0))
                {
                    var featureKeys = new[] { feature.WikiTarget, feature.DisplayTitle }
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(SlayerIds.Normalize)
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                    var assignment = featureKeys.Select(k => assignmentByFeature.GetValueOrDefault(k)).FirstOrDefault(a => a is not null);
                    if (assignment is null) continue;

                    var locationId = SlayerIds.Location(assignment.Name, map.Name);
                    var world = map.CoordinateMode == WikiMapCoordinateMode.World;
                    var mapSlug = RuneScapeWikiDungeonImporter.NormalizeIdPart(map.Name);
                    var featureSlug = RuneScapeWikiDungeonImporter.NormalizeIdPart(feature.DisplayTitle);
                    var evidence = new KnowledgeProvenance
                    {
                        SourceKind = "runescape-wiki-advanced-map",
                        SourceId = entry.Title,
                        SourceUrl = "https://runescape.wiki/w/" + Uri.EscapeDataString(entry.Title.Replace(' ', '_')),
                        SourceRevision = entry.RevisionId,
                        ObservedAtUtc = entry.TimestampUtc ?? snapshot.GeneratedAtUtc,
                        Verification = world ? KnowledgeVerificationState.Corroborated : KnowledgeVerificationState.Candidate,
                        Notes = world ? "World-coordinate feature; navigation action/transition still requires verification." : "Map-local feature retained as semantic location pending calibration."
                    };

                    if (!locations.TryGetValue(locationId, out var location))
                    {
                        location = new MonsterLocation
                        {
                            Id = locationId,
                            Name = map.Name,
                            AssignmentId = assignment.Id,
                            MonsterIds = assignment.MonsterIds.ToList(),
                            MapId = map.MapId ?? int.MinValue,
                            Plane = map.Plane,
                            AreaId = world ? $"area.wiki.{mapSlug}.{featureSlug}" : string.Empty,
                            Verification = evidence.Verification
                        };
                        locations[locationId] = location;
                    }
                    location.Evidence.RemoveAll(e => string.Equals(e.SourceKind, evidence.SourceKind, StringComparison.OrdinalIgnoreCase) &&
                                                     string.Equals(e.SourceId, evidence.SourceId, StringComparison.OrdinalIgnoreCase));
                    location.Evidence.Add(evidence);
                    if (!assignment.LocationIds.Contains(locationId, StringComparer.OrdinalIgnoreCase)) assignment.LocationIds.Add(locationId);
                    published++;
                }
            }
        }

        pack.Locations = locations.Values.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
        pack.PublishedAtUtc = DateTime.UtcNow;
        pack.PackVersion = DateTime.UtcNow.ToString("yyyyMMdd.HHmmss");
        repository.Publish(SlayerKnowledgeNormalizer.Normalize(pack));
        log?.Invoke($"Published {published} Advanced-map feature relationship(s) into Slayer knowledge.");
        return published;
    }

    private static Dictionary<string, SlayerAssignmentDefinition> BuildFeatureIndex(SlayerKnowledgePack pack)
    {
        var result = new Dictionary<string, SlayerAssignmentDefinition>(StringComparer.OrdinalIgnoreCase);
        var monsters = pack.Monsters.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in pack.Assignments)
        {
            result.TryAdd(SlayerIds.Normalize(assignment.Name), assignment);
            foreach (var monsterId in assignment.MonsterIds)
            {
                if (!monsters.TryGetValue(monsterId, out var monster)) continue;
                result.TryAdd(SlayerIds.Normalize(monster.Name), assignment);
                foreach (var alias in monster.Aliases) result.TryAdd(SlayerIds.Normalize(alias), assignment);
            }
        }
        return result;
    }
}
