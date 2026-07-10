using System.Linq;
using Newtonsoft.Json;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using Xunit;
using static SharpBuilder.Core.Tests.TestGraphs;

namespace SharpBuilder.Core.Tests;

public class GraphScriptServiceTests
{
	private readonly NodeCatalogService _catalog = new();

	[Fact]
	public void CreateNew_SeedsStartNodeWithDefinitionAndParameters()
	{
		var service = new GraphScriptService(_catalog);

		var graph = service.CreateNew("Blank");

		var node = Assert.Single(graph.Nodes);
		Assert.Equal("Blank", graph.Name);
		Assert.Equal(node.Id, graph.StartNodeId);
		Assert.Equal(NodeType.Start, node.Type);
		Assert.Equal(NodeCatalogDefaults.StartId, node.DefinitionId);
		Assert.Equal("Start", node.DefinitionTitle);
		Assert.Equal(2, graph.SchemaVersion);
		Assert.Empty(node.Parameters);
	}

	[Fact]
	public async Task SaveAndLoad_RoundTripsGraphAndRefreshesDefinitionMetadata()
	{
		var service = new GraphScriptService(_catalog);
		var graph = service.CreatePowerFishingTemplate();
		var first = graph.Nodes[0];
		first.DefinitionTitle = "stale";
		var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.builder.json");

		try
		{
			await service.SaveAsync(graph, path);

			var loaded = await service.LoadAsync(path);

			Assert.NotNull(loaded);
			Assert.Equal(2, loaded.SchemaVersion);
			Assert.Equal(graph.Nodes.Count, loaded.Nodes.Count);
			var loadedFirst = Assert.Single(loaded.Nodes, n => n.Id == first.Id);
			Assert.Equal(_catalog.GetDefinition(loadedFirst.DefinitionId)!.Title, loadedFirst.DefinitionTitle);
			Assert.All(loaded.Nodes, node =>
			{
				var definition = _catalog.GetDefinition(node.DefinitionId)!;
				Assert.All(definition.Parameters, parameter =>
					Assert.Contains(node.Parameters, value => string.Equals(value.Key, parameter.Key, StringComparison.OrdinalIgnoreCase)));
			});
		}
		finally
		{
			File.Delete(path);
			File.Delete(path + ".tmp");
		}
	}

	[Fact]
	public async Task SaveAndLoad_ExcludesRuntimeVisualState()
	{
		var service = new GraphScriptService(_catalog);
		var graph = service.CreatePowerFishingTemplate();
		var node = graph.Nodes[0];
		var transition = node.Transitions[0];
		node.IsActive = true;
		node.IsCurrent = true;
		node.IsSelected = true;
		node.LastRunStatus = NodeRunStatus.Success;
		transition.IsActive = true;
		var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.builder.json");

		try
		{
			await service.SaveAsync(graph, path);
			var json = await File.ReadAllTextAsync(path);
			var loaded = await service.LoadAsync(path);

			Assert.DoesNotContain("\"IsActive\"", json);
			Assert.DoesNotContain("\"IsSelected\"", json);
			Assert.DoesNotContain("\"IsCurrent\"", json);
			Assert.DoesNotContain("\"LastRunStatus\"", json);
			Assert.NotNull(loaded);
			var loadedNode = Assert.Single(loaded!.Nodes, n => n.Id == node.Id);
			Assert.False(loadedNode.IsActive);
			Assert.False(loadedNode.IsSelected);
			Assert.False(loadedNode.IsCurrent);
			Assert.Equal(NodeRunStatus.None, loadedNode.LastRunStatus);
			Assert.All(loadedNode.Transitions, loadedTransition => Assert.False(loadedTransition.IsActive));
		}
		finally
		{
			File.Delete(path);
			File.Delete(path + ".tmp");
		}
	}

	[Fact]
	public void GraphClone_PreservesDashboardDimensions()
	{
		var graph = Graph(Node(NodeCatalogDefaults.ScriptDashboardId, title: "Dashboard"));
		var node = graph.Nodes[0];
		node.DashboardWidth = 760;
		node.DashboardHeight = 540;

		var clone = GraphCloneService.Clone(graph);
		var clonedNode = Assert.Single(clone.Nodes);

		Assert.Equal(760, clonedNode.DashboardWidth);
		Assert.Equal(540, clonedNode.DashboardHeight);
		Assert.True(GraphCompareService.AreEquivalent(graph, clone));
	}

	[Fact]
	public async Task TryLoadAsync_ReturnsErrorForMissingOrInvalidFiles()
	{
		var service = new GraphScriptService(_catalog);
		var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.builder.json");
		var invalidPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.builder.json");

		try
		{
			var missing = await service.TryLoadAsync(missingPath);
			Assert.Null(missing.Model);
			Assert.Contains("File not found", missing.Error);

			await File.WriteAllTextAsync(invalidPath, "{ invalid json");
			var invalid = await service.TryLoadAsync(invalidPath);

			Assert.Null(invalid.Model);
			Assert.False(string.IsNullOrWhiteSpace(invalid.Error));
		}
		finally
		{
			File.Delete(invalidPath);
		}
	}

	[Fact]
	public async Task LegacyGraphLoad_MigratesActionAndConditionParameters()
	{
		var service = new GraphScriptService(_catalog);
		var action = Node(NodeCatalogDefaults.GenericActionId, title: "Legacy action");
		action.Type = NodeType.Action;
		action.ActionText = "legacy note";
		action.Parameters.Clear();

		var condition = Node(NodeCatalogDefaults.BooleanConditionId, NodeType.Condition, "Legacy condition");
		condition.Parameters.Clear();
		var end = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "End");
		Edge(condition, end, conditionKey: "legacySignal");

		var graph = Graph(action, condition, end);
		graph.SchemaVersion = 1;
		var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.builder.json");

		try
		{
			await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(graph));

			var loaded = await service.LoadAsync(path);

			Assert.NotNull(loaded);
			Assert.Equal(2, loaded.SchemaVersion);
			var loadedAction = loaded.Nodes.Single(n => n.Id == action.Id);
			var actionParam = Assert.Single(loadedAction.Parameters);
			Assert.Equal("action", actionParam.Key);
			Assert.Equal("legacy note", actionParam.RawValue);

			var loadedCondition = loaded.Nodes.Single(n => n.Id == condition.Id);
			Assert.Contains(loadedCondition.Parameters, p => p.Key == "signal" && p.RawValue == "legacySignal");
			Assert.Contains(loadedCondition.Parameters, p => p.Key == "expected" && p.BoolValue);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void PowerFishingTemplate_HasExpectedRoutingAndParameters()
	{
		var service = new GraphScriptService(_catalog);

		var graph = service.CreatePowerFishingTemplate();

		Assert.Equal(6, graph.Nodes.Count);
		Assert.Equal(graph.Nodes.Single(n => n.Title == "Check inventory").Id, graph.StartNodeId);

		var check = graph.Nodes.Single(n => n.Title == "Check inventory");
		var drop = graph.Nodes.Single(n => n.Title == "Drop the catch");
		var find = graph.Nodes.Single(n => n.Title == "Look for spot");
		var move = graph.Nodes.Single(n => n.Title == "Move to spot");
		var fish = graph.Nodes.Single(n => n.Title == "Fish");
		var wait = graph.Nodes.Single(n => n.Title == "Wait while fishing");

		Assert.Contains(check.Transitions, t => t.ToNodeId == drop.Id && t.ConditionKey == "inventoryFull" && t.ExpectedValue);
		Assert.Contains(check.Transitions, t => t.ToNodeId == find.Id && t.IsFallback);
		Assert.Contains(drop.Transitions, t => t.ToNodeId == find.Id && t.IsFallback);
		Assert.Contains(find.Transitions, t => t.ToNodeId == fish.Id && t.ConditionKey == "hasNearbySpot" && t.ExpectedValue);
		Assert.Contains(find.Transitions, t => t.ToNodeId == move.Id && t.IsFallback);
		Assert.Contains(move.Transitions, t => t.ToNodeId == find.Id && t.IsFallback);
		Assert.Contains(fish.Transitions, t => t.ToNodeId == wait.Id && t.IsFallback);
		Assert.Contains(wait.Transitions, t => t.ToNodeId == check.Id && t.IsFallback);
		Assert.Contains(drop.Parameters, p => p.Key == "quantity" && p.RawValue == "All");
		Assert.Contains(find.Parameters, p => p.Key == "signal" && p.RawValue == "hasNearbySpot");
		Assert.Equal("traversal.waitRange", wait.DefinitionId);
		Assert.Contains(wait.Parameters, p => p.Key == "minDelayMs" && p.RawValue == "6000");
		Assert.Contains(wait.Parameters, p => p.Key == "maxDelayMs" && p.RawValue == "8000");
	}

	[Fact]
	public void GreenDhideShieldCraftAlchTemplate_HasExpectedRoutingAndDefaults()
	{
		var service = new GraphScriptService(_catalog);

		var graph = service.CreateGreenDhideShieldCraftAlchTemplate();

		Assert.Equal("Green dhide shield craft-alch", graph.Name);
		Assert.Equal(graph.Nodes.Single(n => n.Title == "Have shields?").Id, graph.StartNodeId);

		var shields = graph.Nodes.Single(n => n.Title == "Have shields?");
		var preset = graph.Nodes.Single(n => n.Title == "Load preset");
		var leather = graph.Nodes.Single(n => n.Title == "Have leather?");
		var natures = graph.Nodes.Single(n => n.Title == "Have natures?");
		var presetLeather = graph.Nodes.Single(n => n.Title == "Preset leather?");
		var presetNatures = graph.Nodes.Single(n => n.Title == "Preset natures?");
		var findPortable = graph.Nodes.Single(n => n.Title == "Find portable crafter");
		var portable = graph.Nodes.Single(n => n.Title == "Use portable crafter");
		var leatherKey = graph.Nodes.Single(n => n.Title == "Dragon leather keybind");
		var make = graph.Nodes.Single(n => n.Title == "Make shields");
		var alch = graph.Nodes.Single(n => n.Title == "Alch shields");
		var stop = graph.Nodes.Single(n => n.Title == "Stop: missing supplies");

		Assert.Equal("bank.loadPreset", preset.DefinitionId);
		Assert.Contains(preset.Parameters, p => p.Key == "method" && p.RawValue == "Keybind");
		Assert.Contains(preset.Parameters, p => p.Key == "keybind" && p.RawValue == "1");
		Assert.Contains(leather.Parameters, p => p.Key == "id" && p.RawValue == "1745");
		Assert.Contains(leather.Parameters, p => p.Key == "min" && p.RawValue == "26");
		Assert.Contains(natures.Parameters, p => p.Key == "id" && p.RawValue == "561");
		Assert.Contains(natures.Parameters, p => p.Key == "min" && p.RawValue == "13");
		Assert.Contains(presetLeather.Parameters, p => p.Key == "id" && p.RawValue == "1745");
		Assert.Contains(presetNatures.Parameters, p => p.Key == "id" && p.RawValue == "561");
		Assert.Contains(findPortable.Parameters, p => p.Key == "signal" && p.RawValue == "hasPortableCrafter");
		Assert.Contains(leatherKey.Parameters, p => p.Key == "keys" && p.RawValue == "F6");
		Assert.Equal("makex.makeItem", make.DefinitionId);
		Assert.Contains(make.Parameters, p => p.Key == "slot" && p.RawValue == "");
		Assert.Contains(make.Parameters, p => p.Key == "category" && p.RawValue == "");
		Assert.Contains(make.Parameters, p => p.Key == "waitComplete" && p.BoolValue);
		Assert.Contains(alch.Parameters, p => p.Key == "keybind" && p.RawValue == "E");
		Assert.Contains(alch.Parameters, p => p.Key == "items" && p.RawValue.Contains("25794"));
		Assert.Contains(alch.Parameters, p => p.Key == "targetMode" && p.RawValue == "KeybindThenItem");
		Assert.Contains(alch.Parameters, p => p.Key == "targetDelayMs" && p.RawValue == "1000");
		Assert.Contains(alch.Parameters, p => p.Key == "recastMode" && p.RawValue == "ItemDisappears");
		Assert.Contains(alch.Parameters, p => p.Key == "disappearTimeoutMs" && p.RawValue == "3500");
		Assert.Contains(alch.Parameters, p => p.Key == "postTargetDelayMs" && p.RawValue == "2500");
		Assert.Contains(alch.Parameters, p => p.Key == "itemAction" && p.RawValue == "110");
		Assert.Contains(alch.Parameters, p => p.Key == "itemOffset" && p.RawValue == $"GeneralInterface_route1 = {MESharp.API.Objects.Offsets.GeneralInterfaceRoute1}");

		Assert.Contains(shields.Transitions, t => t.ToNodeId == alch.Id && t.Trigger == TransitionTrigger.OnSuccess);
		Assert.Contains(shields.Transitions, t => t.ToNodeId == leather.Id && t.Trigger == TransitionTrigger.OnFail);
		Assert.Contains(leather.Transitions, t => t.ToNodeId == graph.Nodes.Single(n => n.Title == "Open nearby bank").Id && t.Trigger == TransitionTrigger.OnFail);
		Assert.Contains(natures.Transitions, t => t.ToNodeId == graph.Nodes.Single(n => n.Title == "Open nearby bank").Id && t.Trigger == TransitionTrigger.OnFail);
		Assert.Contains(findPortable.Transitions, t => t.ToNodeId == portable.Id && t.ConditionKey == "hasPortableCrafter");
		Assert.Contains(findPortable.Transitions, t => t.ToNodeId == leatherKey.Id && t.IsFallback);
		Assert.Contains(presetLeather.Transitions, t => t.ToNodeId == stop.Id && t.Trigger == TransitionTrigger.OnFail);
		Assert.Contains(presetNatures.Transitions, t => t.ToNodeId == stop.Id && t.Trigger == TransitionTrigger.OnFail);
		Assert.Contains(alch.Transitions, t => t.ToNodeId == graph.Nodes.Single(n => n.Title == "Open nearby bank").Id && t.Trigger == TransitionTrigger.OnSuccess);
	}

	[Fact]
	public async Task SavedGreenDhideShieldCraftAlchGraph_Loads()
	{
		var service = new GraphScriptService(_catalog);
		var sharpBuilderRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
		var path = Path.Combine(sharpBuilderRoot, "Scripts", "GreenDhideShieldCraftAlch.builder.json");

		var (graph, error) = await service.TryLoadAsync(path);

		Assert.Null(error);
		Assert.NotNull(graph);
		Assert.Equal("Green dhide shield craft-alch", graph!.Name);
		Assert.Contains(graph.Nodes, n => n.DefinitionId == "bank.loadPreset");
		Assert.Contains(graph.Nodes, n => n.DefinitionId == "inventory.alchAll");
		Assert.All(graph.Nodes, node =>
		{
			Assert.False(node.IsActive);
			Assert.False(node.IsSelected);
			Assert.False(node.IsCurrent);
			Assert.Equal(NodeRunStatus.None, node.LastRunStatus);
			Assert.All(node.Transitions, transition => Assert.False(transition.IsActive));
		});
	}

	[Fact]
	public void CreateMultiCanvasDemo_ReturnsTwoNamedGraphs()
	{
		var service = new GraphScriptService(_catalog);

		var demos = service.CreateMultiCanvasDemo();

		Assert.Equal(2, demos.Count);
		Assert.Equal("Woodcutting loop (demo)", demos[0].Name);
		Assert.Equal("Anti-idle heartbeat (demo)", demos[1].Name);
	}

	[Fact]
	public void DemoGraphs_AreLoopingAndValidateWithoutTheGameApi()
	{
		var service = new GraphScriptService(_catalog);
		var validator = new GraphValidator(_catalog);

		foreach (var graph in service.CreateMultiCanvasDemo())
		{
			Assert.NotNull(graph.StartNodeId);
			Assert.NotEmpty(graph.Nodes);

			// Every node uses an offline-safe definition, so a headless run can animate the demo
			// without an injected client — no validation errors when the game API is unavailable.
			Assert.DoesNotContain(
				validator.Validate(graph, gameApiAvailable: false),
				i => i.Severity == ValidationSeverity.Error);

			// The graph loops back so it keeps animating: at least one fallback edge exists.
			Assert.Contains(graph.Nodes.SelectMany(n => n.Transitions), t => t.IsFallback);
		}
	}

	[Fact]
	public async Task Load_RefreshesDriftedOffsetValuesToCurrentTable()
	{
		var service = new GraphScriptService(_catalog);
		var graph = service.CreatePowerFishingTemplate();
		var fish = graph.Nodes.Single(n => n.Parameters.Any(p => p.RawValue?.StartsWith("InteractNPC_route") == true));
		var offsetParam = fish.Parameters.Single(p => p.RawValue!.StartsWith("InteractNPC_route"));
		offsetParam.RawValue = "InteractNPC_route = 4928"; // pre-update route number
		var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.builder.json");

		try
		{
			await service.SaveAsync(graph, path);

			var loaded = await service.LoadAsync(path);

			Assert.NotNull(loaded);
			var loadedFish = Assert.Single(loaded!.Nodes, n => n.Id == fish.Id);
			var loadedOffset = loadedFish.Parameters.Single(p => p.Key == offsetParam.Key);
			Assert.Equal($"InteractNPC_route = {MESharp.API.Npcs.InteractNPC_route}", loadedOffset.RawValue);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
