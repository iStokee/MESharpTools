using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using Xunit;
using static SharpBuilder.Core.Tests.TestGraphs;

namespace SharpBuilder.Core.Tests;

public class GraphSignalServiceTests
{
	[Fact]
	public void DiscoverSignalKeys_FindsTransitionAndSignalParameterKeys()
	{
		var publisher = Node("actions.setSignal", title: "Publish");
		Param(publisher, "signal", "ready");
		BoolParam(publisher, "value", true);

		var reader = Node(NodeCatalogDefaults.BooleanConditionId, NodeType.Condition, "Read");
		Param(reader, "signal", "ready");
		BoolParam(reader, "expected", true);
		Edge(publisher, reader, conditionKey: "gateOpen");

		var keys = GraphSignalService.DiscoverSignalKeys(Graph(publisher, reader));

		Assert.Equal(new[] { "gateOpen", "ready" }, keys);
	}
}
