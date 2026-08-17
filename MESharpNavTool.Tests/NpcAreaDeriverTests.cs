using System;
using System.IO;
using System.Text.Json;
using MESharp.CacheDerivation;
using Xunit;

namespace MESharpNavTool.Tests;

public sealed class NpcAreaDeriverTests
{
    [Fact]
    public void Preflight_reports_definition_and_placement_inputs_separately()
    {
        using var fixture = new Fixture();
        var options = fixture.Options();

        var missingDefinitions = NpcAreaDeriver.InspectInputs(fixture.Dump, options);
        Assert.False(missingDefinitions.CanDerive);
        Assert.Contains("definitions missing", missingDefinitions.Summary, StringComparison.OrdinalIgnoreCase);

        File.WriteAllText(fixture.ConfigPath, Fixture.Configs);
        var missingPlacements = NpcAreaDeriver.InspectInputs(fixture.Dump, options);
        Assert.False(missingPlacements.CanDerive);
        Assert.Contains("placements missing", missingPlacements.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Independent_wiki_and_live_coordinates_enable_a_corroborated_area()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigPath, Fixture.Configs);
        File.WriteAllText(fixture.WikiPath, """
        { "entries": [{ "maps": [{ "coordinateMode": 1, "plane": 0, "features": [
          { "wikiTarget": "Vannaka", "coordinates": [{ "x": 3147, "y": 9913 }] }
        ] }] }] }
        """);
        File.WriteAllText(fixture.ObservationsPath, """
        { "npcSightings": [
          { "monsterId": "master.vannaka", "npcId": 3, "x": 3147, "y": 9913, "z": 0 }
        ] }
        """);

        var result = NpcAreaDeriver.Derive(fixture.Dump, fixture.Output, fixture.Options());
        using var doc = JsonDocument.Parse(File.ReadAllText(result.SeedPath));
        var area = Assert.Single(doc.RootElement.GetProperty("Areas").EnumerateArray());

        Assert.True(area.GetProperty("Enabled").GetBoolean());
        Assert.Equal(1, result.CorroboratedAreas);
        Assert.Contains("wiki", area.GetProperty("Source").GetString());
        Assert.Contains("live", area.GetProperty("Source").GetString());
    }

    [Fact]
    public void Existing_runtime_npc_sighting_store_is_consumed_as_live_candidate_evidence()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.ConfigPath, Fixture.Configs);
        var legacyPath = Path.Combine(fixture.Dump, "npc_sightings.json");
        File.WriteAllText(legacyPath, """
        { "Sightings": [
          { "c": "slayer", "x": 3147, "y": 9913, "z": 0, "n": "Vannaka" }
        ] }
        """);
        var options = fixture.Options(legacyPath);

        var result = NpcAreaDeriver.Derive(fixture.Dump, fixture.Output, options);
        using var doc = JsonDocument.Parse(File.ReadAllText(result.SeedPath));
        var area = Assert.Single(doc.RootElement.GetProperty("Areas").EnumerateArray());

        Assert.False(area.GetProperty("Enabled").GetBoolean());
        Assert.Equal(1, result.CandidateAreas);
        Assert.Contains("live", area.GetProperty("Source").GetString());
    }

    private sealed class Fixture : IDisposable
    {
        public const string Configs = """
        [
          { "id": 3, "name": "Vannaka", "actions": ["Talk-to", "Get-task"] }
        ]
        """;

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "mesharp_nav_npc_" + Guid.NewGuid().ToString("N"));
            Dump = Path.Combine(Root, "dump");
            Output = Path.Combine(Root, "output");
            Directory.CreateDirectory(Dump);
            Directory.CreateDirectory(Output);
        }

        private string Root { get; }
        public string Dump { get; }
        public string Output { get; }
        public string ConfigPath => Path.Combine(Dump, "npc_configs.json");
        public string WikiPath => Path.Combine(Root, "advanced_maps.snapshot.json");
        public string ObservationsPath => Path.Combine(Root, "local_observations.json");

        public NpcAreaDeriveOptions Options(string? legacyPath = null) => new()
        {
            WikiSnapshotPath = WikiPath,
            LiveObservationsPath = ObservationsPath,
            LegacyLiveSightingsPath = legacyPath ?? Path.Combine(Root, "missing-legacy-sightings.json"),
            UseDefaultChiselDefinitions = false
        };

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
