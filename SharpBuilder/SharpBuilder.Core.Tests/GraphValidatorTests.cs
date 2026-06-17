using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using Xunit;
using static SharpBuilder.Core.Tests.TestGraphs;

namespace SharpBuilder.Core.Tests;

public class GraphValidatorTests
{
	private readonly NodeCatalogService _catalog = new();
	private readonly GraphValidator _validator;

	public GraphValidatorTests() => _validator = new GraphValidator(_catalog);

	[Fact]
	public void EmptyGraph_IsAnError()
	{
		var issues = _validator.Validate(Graph());

		Assert.Single(issues);
		Assert.Equal(ValidationSeverity.Error, issues[0].Severity);
	}

	[Fact]
	public void UnknownDefinition_WarnsAboutNoOpPlaceholder()
	{
		var node = Node("does.not.exist");

		var issues = _validator.Validate(Graph(node));

		Assert.Contains(issues, i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("no-op placeholder"));
	}

	[Fact]
	public void PlaceholderNode_WarnsInMultiNodeGraph()
	{
		var placeholder = Node(NodeCatalogDefaults.GenericActionId);
		var end = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal);
		Edge(placeholder, end);

		var issues = _validator.Validate(Graph(placeholder, end));

		Assert.Contains(issues, i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("performs no game action"));
	}

	[Fact]
	public void PlaceholderNode_DoesNotWarnWhenItIsTheOnlyNode()
	{
		var placeholder = Node(NodeCatalogDefaults.GenericActionId);

		var issues = _validator.Validate(Graph(placeholder));

		Assert.DoesNotContain(issues, i => i.Message.Contains("performs no game action"));
	}

	[Fact]
	public void NotImplementedDefinition_Warns()
	{
		var shop = Node("actions.shop");
		Param(shop, "mode", "Buy", NodeParamType.Enum);

		var issues = _validator.Validate(Graph(shop));

		Assert.Contains(issues, i => i.Severity == ValidationSeverity.Warning && i.Message.Contains("not implemented"));
	}

	[Fact]
	public void AdvancedDefinition_Warns()
	{
		var interaction = Node("objects.interact");
		Param(interaction, "name", "Gate", NodeParamType.List);

		var issues = _validator.Validate(Graph(interaction));

		Assert.Contains(issues, i =>
			i.Severity == ValidationSeverity.Warning &&
			i.Message.Contains("advanced native-capture node"));
	}

	[Fact]
	public void GameApiNode_IsAnErrorWhenGameApiUnavailable()
	{
		var check = Node("conditions.inventoryFull", NodeType.Condition);
		BoolParam(check, "expected", true);

		var issues = _validator.Validate(Graph(check), gameApiAvailable: false);

		Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("in-game API"));
	}

	[Fact]
	public void NonGameApiNode_IsFineWithoutGameApi()
	{
		var wait = Node("traversal.wait");
		Param(wait, "delayMs", "100", NodeParamType.Number);

		var issues = _validator.Validate(Graph(wait), gameApiAvailable: false);

		Assert.Empty(issues);
	}

	[Fact]
	public void MissingRequiredParameter_IsAnError()
	{
		var drop = Node("inventory.drop");
		// "items" and "quantity" are required and left empty.

		var issues = _validator.Validate(Graph(drop));

		Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("required parameter"));
	}

	[Fact]
	public void TransitionToMissingNode_IsAnError()
	{
		var a = Node("traversal.wait");
		Param(a, "delayMs", "1", NodeParamType.Number);
		a.Transitions.Add(new TransitionModel { FromNodeId = a.Id, ToNodeId = Guid.NewGuid(), Label = "ghost" });

		var issues = _validator.Validate(Graph(a));

		Assert.Contains(issues, i => i.Severity == ValidationSeverity.Error && i.Message.Contains("missing node"));
	}

	[Fact]
	public void TerminalWithOutgoingEdges_Warns()
	{
		var end = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal);
		var other = Node("traversal.wait");
		Param(other, "delayMs", "1", NodeParamType.Number);
		Edge(end, other);
		Edge(other, end);

		var issues = _validator.Validate(Graph(end, other));

		Assert.Contains(issues, i => i.Message.Contains("never be taken"));
	}

	[Fact]
	public void DeadEndNode_WarnsInMultiNodeGraph()
	{
		var a = Node("traversal.wait");
		Param(a, "delayMs", "1", NodeParamType.Number);
		var b = Node("traversal.wait");
		Param(b, "delayMs", "1", NodeParamType.Number);
		Edge(a, b);

		var issues = _validator.Validate(Graph(a, b));

		Assert.Contains(issues, i => i.Message.Contains("no outgoing transitions"));
	}

	[Fact]
	public void MultipleFallbacks_Warn()
	{
		var a = Node("traversal.wait");
		Param(a, "delayMs", "1", NodeParamType.Number);
		var b = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal);
		Edge(a, b, isFallback: true);
		Edge(a, b, isFallback: true);

		var issues = _validator.Validate(Graph(a, b));

		Assert.Contains(issues, i => i.Message.Contains("multiple fallback"));
	}

	[Fact]
	public void DuplicateTransitionsToSameTarget_Warn()
	{
		var a = Node("traversal.wait");
		Param(a, "delayMs", "1", NodeParamType.Number);
		var b = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal);
		Edge(a, b, label: "first");
		Edge(a, b, label: "second");

		var issues = _validator.Validate(Graph(a, b));

		Assert.Contains(issues, i => i.Message.Contains("duplicate transitions"));
	}

	[Fact]
	public void UnreachableNode_Warns()
	{
		var a = Node("traversal.wait");
		Param(a, "delayMs", "1", NodeParamType.Number);
		var island = Node("traversal.wait", title: "Island");
		Param(island, "delayMs", "1", NodeParamType.Number);

		var issues = _validator.Validate(Graph(a, island));

		Assert.Contains(issues, i => i.Message.Contains("unreachable"));
	}

	[Fact]
	public void BooleanCondition_ReadingUnpublishedSignal_Warns()
	{
		var condition = Node(NodeCatalogDefaults.BooleanConditionId, NodeType.Condition);
		Param(condition, "signal", "ghostSignal");
		BoolParam(condition, "expected", true);

		var issues = _validator.Validate(Graph(condition));

		Assert.Contains(issues, i =>
			i.Severity == ValidationSeverity.Warning &&
			i.Message.Contains("ghostSignal") &&
			i.Message.Contains("no node in this graph publishes"));
	}

	[Fact]
	public void EdgeGatedOnUnpublishedSignal_Warns()
	{
		var a = Node("traversal.wait");
		Param(a, "delayMs", "1", NodeParamType.Number);
		var b = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal);
		Edge(a, b, conditionKey: "phantom");
		Edge(a, b, isFallback: true);

		var issues = _validator.Validate(Graph(a, b));

		Assert.Contains(issues, i => i.Message.Contains("phantom") && i.Message.Contains("can never match"));
	}

	[Fact]
	public void EdgeGatedOnSignal_FromFixedPublisher_DoesNotWarn()
	{
		var check = Node("conditions.inventoryFull", NodeType.Condition);
		BoolParam(check, "expected", true);
		var end = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal);
		Edge(check, end, conditionKey: "inventoryFull");
		Edge(check, end, isFallback: true);

		var issues = _validator.Validate(Graph(check, end));

		Assert.DoesNotContain(issues, i => i.Message.Contains("inventoryFull") && i.Message.Contains("never match"));
	}

	[Fact]
	public void EdgeGatedOnSignal_FromSignalParamPublisher_DoesNotWarn()
	{
		var find = Node("npcs.find", NodeType.Condition);
		Param(find, "name", "Fishing spot");
		Param(find, "signal", "hasNearbySpot");
		BoolParam(find, "expected", true);
		var end = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal);
		Edge(find, end, conditionKey: "hasNearbySpot");
		Edge(find, end, isFallback: true);

		var issues = _validator.Validate(Graph(find, end));

		Assert.DoesNotContain(issues, i => i.Message.Contains("hasNearbySpot"));
	}

	[Fact]
	public void EquipmentContains_IsARecognizedPublisher()
	{
		var worn = Node("equipment.contains", NodeType.Condition);
		Param(worn, "items", "Rune scimitar", NodeParamType.List, allowMultiple: true);
		BoolParam(worn, "expected", true);
		var end = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal);
		Edge(worn, end, conditionKey: "equipment.contains");
		Edge(worn, end, isFallback: true);

		var issues = _validator.Validate(Graph(worn, end));

		Assert.DoesNotContain(issues, i => i.Message.Contains("equipment.contains") && i.Message.Contains("never match"));
	}

	[Fact]
	public void FamiliarSummonDefinition_IsAvailableForCombatLoops()
	{
		var definition = _catalog.GetDefinition("familiar.summon");

		Assert.NotNull(definition);
		Assert.Equal("familiar", definition!.CategoryId);
		Assert.Contains(definition.Parameters, p => p.Key == "pouches" && p.IsRequired);
		Assert.Contains(definition.Parameters, p => p.Key == "onlyIfMissing");
	}

	[Fact]
	public void CombatAndAnchorConditions_PublishConfiguredSignals()
	{
		var anchor = Node("conditions.locationRadius", NodeType.Condition, "Anchor");
		Param(anchor, "center", "3200,3200,0", NodeParamType.Coordinate);
		Param(anchor, "radius", "12", NodeParamType.Number);
		Param(anchor, "signal", "atAnchor");
		BoolParam(anchor, "expected", true);

		var health = Node("conditions.healthPercent", NodeType.Condition, "Health");
		Param(health, "comparison", "<=", NodeParamType.Enum);
		Param(health, "threshold", "45", NodeParamType.Number);
		Param(health, "signal", "needsFood");
		BoolParam(health, "expected", true);

		var prayer = Node("conditions.prayerPercent", NodeType.Condition, "Prayer");
		Param(prayer, "comparison", "<=", NodeParamType.Enum);
		Param(prayer, "threshold", "25", NodeParamType.Number);
		Param(prayer, "signal", "needsPrayer");
		BoolParam(prayer, "expected", true);

		var familiar = Node("familiar.check", NodeType.Condition, "Familiar");
		BoolParam(familiar, "expected", true);
		Param(familiar, "signal", "familiarReady");

		var cooldown = Node("conditions.cooldown", NodeType.Condition, "Cooldown");
		Param(cooldown, "intervalMs", "60000", NodeParamType.Number);
		Param(cooldown, "signal", "potionReady");
		BoolParam(cooldown, "startReady", true);
		BoolParam(cooldown, "consumeOnReady", true);
		BoolParam(cooldown, "expected", true);

		var end = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal);
		Edge(anchor, health, conditionKey: "atAnchor");
		Edge(health, prayer, conditionKey: "needsFood");
		Edge(prayer, familiar, conditionKey: "needsPrayer");
		Edge(familiar, cooldown, conditionKey: "familiarReady");
		Edge(cooldown, end, conditionKey: "potionReady");

		var issues = _validator.Validate(Graph(anchor, health, prayer, familiar, cooldown, end));

		Assert.DoesNotContain(issues, i => i.Message.Contains("atAnchor") && i.Message.Contains("never match"));
		Assert.DoesNotContain(issues, i => i.Message.Contains("needsFood") && i.Message.Contains("never match"));
		Assert.DoesNotContain(issues, i => i.Message.Contains("needsPrayer") && i.Message.Contains("never match"));
		Assert.DoesNotContain(issues, i => i.Message.Contains("familiarReady") && i.Message.Contains("never match"));
		Assert.DoesNotContain(issues, i => i.Message.Contains("potionReady") && i.Message.Contains("never match"));
	}

	[Fact]
	public void PowerFishingTemplate_ValidatesClean()
	{
		var service = new GraphScriptService(_catalog);
		var template = service.CreatePowerFishingTemplate();

		var issues = _validator.Validate(template, gameApiAvailable: true);

		Assert.Empty(issues);
	}

	[Fact]
	public void ScriptDashboard_CanFloatUnconnectedWithoutValidationWarnings()
	{
		var start = Node(NodeCatalogDefaults.StartId, NodeType.Start, "Start");
		var end = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "End");
		var dashboard = Node(NodeCatalogDefaults.ScriptDashboardId, NodeType.Action, "Script UI Dashboard");
		Edge(start, end, isFallback: true);

		var issues = _validator.Validate(Graph(start, end, dashboard), gameApiAvailable: true);

		Assert.Empty(issues);
	}
}
