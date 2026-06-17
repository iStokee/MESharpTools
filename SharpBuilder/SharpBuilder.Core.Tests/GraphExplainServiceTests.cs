using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using Xunit;
using static SharpBuilder.Core.Tests.TestGraphs;

namespace SharpBuilder.Core.Tests;

public class GraphExplainServiceTests
{
	private readonly NodeCatalogService _catalog = new();

	[Fact]
	public void PowerFishingTemplate_ExplainsStableGameBackedGraph()
	{
		var scriptService = new GraphScriptService(_catalog);
		var explainer = new GraphExplainService(_catalog);

		var explanation = explainer.Explain(scriptService.CreatePowerFishingTemplate());

		Assert.Equal("Power fishing (template)", explanation.ScriptName);
		Assert.True(explanation.RequiresGameApi);
		Assert.False(explanation.HasAdvancedNodes);
		Assert.False(explanation.HasErrors);
		Assert.Contains(explanation.Signals, s =>
			s.Key == "inventoryFull" &&
			s.Publishers.Contains("Check inventory") &&
			s.Readers.Any(r => r.Contains("Inventory full")));
		Assert.Contains(explanation.Signals, s =>
			s.Key == "hasNearbySpot" &&
			s.Publishers.Contains("Look for spot") &&
			s.Readers.Any(r => r.Contains("Spot nearby")));
	}

	[Fact]
	public void AdvancedInteractionNode_IsExplainedAndWarned()
	{
		var explainer = new GraphExplainService(_catalog);
		var node = Node("objects.interact", title: "Open gate");
		Param(node, "name", "Gate", NodeParamType.List);

		var explanation = explainer.Explain(Graph(node));

		Assert.True(explanation.HasAdvancedNodes);
		Assert.Contains(explanation.Nodes, n =>
			n.Title == "Open gate" &&
			n.Maturity == NodeMaturity.Advanced &&
			n.RequiresGameApi);
		Assert.Contains(explanation.Issues, i => i.Message.Contains("advanced native-capture node"));
	}

	[Fact]
	public void Explain_ReportsTransitionTargetsAndConditions()
	{
		var explainer = new GraphExplainService(_catalog);
		var check = Node("conditions.inventoryFull", NodeType.Condition, "Check bag");
		BoolParam(check, "expected", true);
		var end = Node(NodeCatalogDefaults.TerminalId, NodeType.Terminal, "Done");
		Edge(check, end, label: "Full", conditionKey: "inventoryFull");

		var explanation = explainer.Explain(Graph(check, end));
		var transition = Assert.Single(explanation.Nodes.Single(n => n.Title == "Check bag").Transitions);

		Assert.Equal("Done", transition.ToNodeTitle);
		Assert.Equal("inventoryFull", transition.ConditionKey);
		Assert.True(transition.ExpectedValue);
	}
}
