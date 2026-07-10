using System.Collections.Generic;
using SharpBuilder.Core.Services;
using Xunit;

namespace SharpBuilder.Core.Tests;

/// <summary>
/// Guards the interaction-node target contract: npcs.interact / npcs.attack / objects.interact
/// catalogs expose "name" (mixed name/id list) + "id" parameters, and the executors resolve
/// targets via ToTargetListsWithId. A key mismatch here silently no-ops the node (the
/// npcs.interact "target" regression).
/// </summary>
public class ParameterHelperTargetTests
{
	[Fact]
	public void ToTargetListsWithId_SplitsMixedNameListIntoIdsAndNames()
	{
		var map = new Dictionary<string, object?> { ["name"] = "Banker, 5081; Fishing spot" };

		var (ids, names) = ParameterHelper.ToTargetListsWithId(map, "name", "id");

		Assert.Equal(new List<int> { 5081 }, ids);
		Assert.Equal(new List<string> { "Banker", "Fishing spot" }, names);
	}

	[Fact]
	public void ToTargetListsWithId_MergesDedicatedIdParameter()
	{
		var map = new Dictionary<string, object?> { ["id"] = "5081" };

		var (ids, names) = ParameterHelper.ToTargetListsWithId(map, "name", "id");

		Assert.Equal(new List<int> { 5081 }, ids);
		Assert.Empty(names);
	}

	[Fact]
	public void ToTargetListsWithId_DoesNotDuplicateIdAlreadyInList()
	{
		var map = new Dictionary<string, object?>
		{
			["name"] = "5081",
			["id"] = 5081
		};

		var (ids, names) = ParameterHelper.ToTargetListsWithId(map, "name", "id");

		Assert.Equal(new List<int> { 5081 }, ids);
		Assert.Empty(names);
	}

	[Fact]
	public void ToTargetListsWithId_EmptyParametersYieldNoTargets()
	{
		var map = new Dictionary<string, object?>();

		var (ids, names) = ParameterHelper.ToTargetListsWithId(map, "name", "id");

		Assert.Empty(ids);
		Assert.Empty(names);
	}
}
