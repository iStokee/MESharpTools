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
		var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.orbitfsm.json");

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
	public async Task TryLoadAsync_ReturnsErrorForMissingOrInvalidFiles()
	{
		var service = new GraphScriptService(_catalog);
		var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.orbitfsm.json");
		var invalidPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.orbitfsm.json");

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
		var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.orbitfsm.json");

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
}
