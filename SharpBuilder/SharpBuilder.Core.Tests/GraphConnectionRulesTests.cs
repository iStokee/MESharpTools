using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using Xunit;
using static SharpBuilder.Core.Tests.TestGraphs;

namespace SharpBuilder.Core.Tests;

public class GraphConnectionRulesTests
{
	private readonly GraphConnectionRules _rules = new();

	[Fact]
	public void CanConnect_BlocksSelfConnection()
	{
		var node = Node("traversal.wait");
		var graph = Graph(node);

		var result = _rules.CanConnect(graph, node, node);

		Assert.False(result.CanConnect);
		Assert.Contains("itself", result.Message);
	}

	[Fact]
	public void CanConnect_BlocksDuplicateTarget()
	{
		var a = Node("traversal.wait");
		var b = Node("traversal.wait");
		var existing = Edge(a, b);
		var graph = Graph(a, b);

		var result = _rules.CanConnect(graph, a, b);

		Assert.False(result.CanConnect);
		Assert.Same(existing, result.ExistingTransition);
		Assert.Contains("already links", result.Message);
	}

	[Fact]
	public void CanConnect_AllowsCycleWithWarning()
	{
		var a = Node("traversal.wait");
		var b = Node("traversal.wait");
		Edge(b, a);
		var graph = Graph(a, b);

		var result = _rules.CanConnect(graph, a, b);

		Assert.True(result.CanConnect);
		Assert.Equal(GraphConnectionRuleSeverity.Warning, result.Severity);
		Assert.Contains("cycle", result.Message);
	}

	[Fact]
	public void CanRetarget_BlocksDuplicateTarget()
	{
		var a = Node("traversal.wait");
		var b = Node("traversal.wait");
		var c = Node("traversal.wait");
		var first = Edge(a, b);
		var duplicate = Edge(a, c);
		var graph = Graph(a, b, c);

		var result = _rules.CanRetarget(graph, first, c);

		Assert.False(result.CanConnect);
		Assert.Same(duplicate, result.ExistingTransition);
	}
}
