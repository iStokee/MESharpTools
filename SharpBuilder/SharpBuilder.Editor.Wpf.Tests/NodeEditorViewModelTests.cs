using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using SharpBuilder.Editor.Wpf.ViewModels;
using Xunit;

namespace SharpBuilder.Editor.Wpf.Tests;

public class NodeEditorViewModelTests
{
	private static NodeEditorViewModel CreateViewModel()
	{
		var catalog = new NodeCatalogService();
		return new NodeEditorViewModel(
			new GraphScriptService(catalog),
			new GraphExecutionEngine(catalog, new NodeExecutorRegistry()),
			catalog);
	}

	[Fact]
	public void Constructor_LoadsTemplateAndSelectsStartNode()
	{
		using var vm = CreateViewModel();

		Assert.Equal("Power fishing (template)", vm.Script.Name);
		Assert.NotEmpty(vm.Script.Nodes);
		Assert.Equal(vm.Script.Nodes.First().Id, vm.Script.StartNodeId);
		Assert.Same(vm.Script.Nodes.First(), vm.SelectedNode);
		Assert.NotEmpty(vm.ParameterBindings);
		Assert.False(vm.IsDirty);
		Assert.Contains(vm.Signals, s => s.Key == "inventoryFull");
		Assert.Contains(vm.SignalSuggestions, s => s == "hasNearbySpot");
	}

	[Fact]
	public void FilteredDefinitions_ExcludesNotImplementedAndAdvancedPaletteItemsByDefault()
	{
		using var vm = CreateViewModel();
		vm.SelectedCategory = vm.Categories.Single(c => c.Id == "actions");

		var definitions = vm.FilteredDefinitions.ToList();

		Assert.DoesNotContain(definitions, d => d.Id == "actions.interaction");
		Assert.DoesNotContain(definitions, d => d.Id == "actions.shop");
		Assert.All(definitions, d => Assert.True(d.IsImplemented));
		Assert.All(definitions, d => Assert.Equal(NodeMaturity.Stable, d.Maturity));
	}

	[Fact]
	public void FilteredDefinitions_CanShowAdvancedPaletteItems()
	{
		using var vm = CreateViewModel();
		vm.SelectedCategory = vm.Categories.Single(c => c.Id == "actions");
		vm.ShowAdvancedNodes = true;

		var definitions = vm.FilteredDefinitions.ToList();

		Assert.Contains(definitions, d => d.Id == "actions.interaction");
		Assert.DoesNotContain(definitions, d => d.Id == "actions.shop");
	}

	[Fact]
	public void CreateNodeFromDefinition_AddsNodeWithParametersAndSelectsIt()
	{
		using var vm = CreateViewModel();
		var definition = vm.Definitions.Single(d => d.Id == "inventory.drop");
		var initialCount = vm.Script.Nodes.Count;

		vm.CreateNodeFromDefinitionCommand.Execute(definition);

		Assert.Equal(initialCount + 1, vm.Script.Nodes.Count);
		var node = Assert.Single(vm.Script.Nodes, n => n.DefinitionId == "inventory.drop" && n.Title == "Drop item(s)");
		Assert.Same(node, vm.SelectedNode);
		Assert.Equal(NodeType.Action, node.Type);
		Assert.Contains(node.Parameters, p => p.Key == "items" && p.AllowMultiple);
		Assert.Contains(node.Parameters, p => p.Key == "quantity" && p.Type == NodeParamType.Enum);
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void ExplainGraphCommand_PopulatesDryRunPanel()
	{
		using var vm = CreateViewModel();

		vm.ExplainGraphCommand.Execute(null);

		Assert.True(vm.IsGraphExplanationOpen);
		Assert.Contains("nodes", vm.GraphExplanationSummary);
		Assert.Contains(vm.GraphExplanationLines, line => line.Contains("Requires in-game API"));
		Assert.Contains(vm.GraphExplanationLines, line => line.Contains("inventoryFull"));
	}

	[Fact]
	public void SelectedNodeDefinition_RebuildsNodeParametersForNewDefinition()
	{
		using var vm = CreateViewModel();
		var node = vm.SelectedNode!;
		node.Parameters.Add(new NodeParameterValue { Key = "stale", RawValue = "remove me" });

		vm.SelectedNodeDefinition = vm.Definitions.Single(d => d.Id == "inventory.drop");

		Assert.Equal("inventory.drop", node.DefinitionId);
		Assert.Equal("Drop item(s)", node.DefinitionTitle);
		Assert.Equal(NodeType.Action, node.Type);
		Assert.DoesNotContain(node.Parameters, p => p.Key == "stale");
		Assert.Contains(node.Parameters, p => p.Key == "items");
		Assert.Contains(node.Parameters, p => p.Key == "quantity");
		// "count" is rendered inline beside the quantity dropdown, so it is pulled out of the main list.
		Assert.Equal(node.Parameters.Count - 1, vm.ParameterBindings.Count);
		Assert.DoesNotContain(vm.ParameterBindings, b => b.Definition.Key == "count");
	}

	[Fact]
	public void ParameterBindings_PullCountOutAsInlineCompanionOfQuantity()
	{
		using var vm = CreateViewModel();
		var definition = vm.Definitions.Single(d => d.Id == "inventory.drop");
		vm.CreateNodeFromDefinitionCommand.Execute(definition);

		var quantity = Assert.Single(vm.ParameterBindings, b => b.Definition.Key == "quantity");
		Assert.True(quantity.HasInlineCompanion);
		Assert.Equal("count", quantity.InlineCompanion!.Definition.Key);
		Assert.Equal("Some", quantity.Definition.InlineCompanionVisibleWhen);
		Assert.DoesNotContain(vm.ParameterBindings, b => b.Definition.Key == "count");
	}

	[Fact]
	public void ParameterBindings_RouteAdvancedParamsToTheAdvancedList()
	{
		using var vm = CreateViewModel();
		var definition = vm.Definitions.Single(d => d.Id == "npcs.interact");
		vm.CreateNodeFromDefinitionCommand.Execute(definition);

		// Keep the stable NPC interaction node simple by default; native capture details
		// are still available under Advanced when an activity needs opcode/route tuning.
		Assert.Contains(vm.ParameterBindings, b => b.Definition.Key == "name");
		Assert.Contains(vm.ParameterBindings, b => b.Definition.Key == "id");
		// Niche tuning knobs still route to the Advanced list.
		Assert.True(vm.HasAdvancedParameters);
		Assert.Contains(vm.AdvancedParameterBindings, b => b.Definition.Key == "actionIndex");
		Assert.Contains(vm.AdvancedParameterBindings, b => b.Definition.Key == "offset");
		Assert.Contains(vm.AdvancedParameterBindings, b => b.Definition.Key == "maxDistance");
		Assert.DoesNotContain(vm.ParameterBindings, b => b.Definition.Key == "actionIndex");
	}

	[Fact]
	public void ConnectNodes_AddsFallbackEdgeAndDoesNotDuplicateExistingTarget()
	{
		using var vm = CreateViewModel();
		var from = vm.Script.Nodes[0];
		var to = vm.Script.Nodes[1];
		from.Transitions.Clear();

		vm.ConnectNodes(from, to);
		vm.ConnectNodes(from, to);

		var transition = Assert.Single(from.Transitions);
		Assert.Equal(from.Id, transition.FromNodeId);
		Assert.Equal(to.Id, transition.ToNodeId);
		Assert.True(transition.IsFallback);
		Assert.Same(from, vm.SelectedNode);
		Assert.Same(transition, vm.SelectedTransition);
		Assert.Contains("already links", vm.Status);
	}

	[Fact]
	public void RetargetTransition_ChangesTargetUnlessDuplicateWouldBeCreated()
	{
		using var vm = CreateViewModel();
		var source = vm.Script.Nodes[0];
		var original = source.Transitions[0];
		var duplicateTarget = source.Transitions[1].ToNodeId;
		var newTarget = vm.Script.Nodes.First(n => n.Id != source.Id && n.Id != original.ToNodeId && n.Id != duplicateTarget);

		vm.RetargetTransition(original, newTarget);

		Assert.Equal(newTarget.Id, original.ToNodeId);
		Assert.Same(source, vm.SelectedNode);
		Assert.Same(original, vm.SelectedTransition);

		vm.RetargetTransition(original, vm.Script.Nodes.Single(n => n.Id == duplicateTarget));

		Assert.Equal(newTarget.Id, original.ToNodeId);
		Assert.Contains("already links", vm.Status);
	}

	[Fact]
	public void DeleteSelectedCommand_RemovesSelectedTransitionBeforeSelectedNode()
	{
		using var vm = CreateViewModel();
		var source = vm.Script.Nodes[0];
		var transition = source.Transitions[0];
		vm.SelectedNode = source;
		vm.SelectedTransition = transition;
		var nodeCount = vm.Script.Nodes.Count;

		vm.DeleteSelectedCommand.Execute(null);

		Assert.Equal(nodeCount, vm.Script.Nodes.Count);
		Assert.DoesNotContain(transition, source.Transitions);
		Assert.NotSame(transition, vm.SelectedTransition);
	}

	[Fact]
	public void DeleteSelectedCommand_RemovesNodeAndIncomingTransitions()
	{
		using var vm = CreateViewModel();
		var node = vm.Script.Nodes.Single(n => n.Title == "Look for spot");
		Assert.Contains(vm.Script.Nodes.SelectMany(n => n.Transitions), t => t.ToNodeId == node.Id);

		vm.SelectedNode = node;
		vm.SelectedTransition = null;
		vm.DeleteSelectedCommand.Execute(null);

		Assert.DoesNotContain(node, vm.Script.Nodes);
		Assert.DoesNotContain(vm.Script.Nodes.SelectMany(n => n.Transitions), t => t.ToNodeId == node.Id || t.FromNodeId == node.Id);
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void TransitionPropertyChange_MarksDocumentDirty()
	{
		using var vm = CreateViewModel();
		var transition = vm.Script.Nodes.SelectMany(n => n.Transitions).First();

		transition.Label = "Renamed edge";

		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void AddTransitionCommand_UsesAvailableTargetAndDoesNotDuplicate()
	{
		using var vm = CreateViewModel();
		var source = vm.Script.Nodes[0];
		vm.SelectedNode = source;
		var existingTargets = source.Transitions.Select(t => t.ToNodeId).ToHashSet();

		vm.AddTransitionCommand.Execute(null);

		Assert.Equal(source.Transitions.Count, source.Transitions.Select(t => t.ToNodeId).Distinct().Count());
		Assert.Contains(source.Transitions, t => !existingTargets.Contains(t.ToNodeId));
	}

	[Fact]
	public void UndoRedo_RestoresAddedNode()
	{
		using var vm = CreateViewModel();
		var initialCount = vm.Script.Nodes.Count;
		var definition = vm.Definitions.Single(d => d.Id == "traversal.wait");

		vm.CreateNodeFromDefinitionCommand.Execute(definition);
		Assert.Equal(initialCount + 1, vm.Script.Nodes.Count);
		Assert.True(vm.CanUndo);

		vm.UndoCommand.Execute(null);
		Assert.Equal(initialCount, vm.Script.Nodes.Count);
		Assert.True(vm.CanRedo);

		vm.RedoCommand.Execute(null);
		Assert.Equal(initialCount + 1, vm.Script.Nodes.Count);
	}

	[Fact]
	public void GraphEditBatch_UndoesMultiNodeMoveAsSingleEdit()
	{
		using var vm = CreateViewModel();
		var first = vm.Script.Nodes[0];
		var second = vm.Script.Nodes[1];
		var firstOriginal = (X: first.X, Y: first.Y);
		var secondOriginal = (X: second.X, Y: second.Y);

		vm.BeginGraphEditBatch("Move nodes");
		first.X += 40;
		first.Y += 20;
		second.X += 40;
		second.Y += 20;
		vm.CommitGraphEditBatch();

		Assert.True(vm.CanUndo);

		vm.UndoCommand.Execute(null);

		var restoredFirst = vm.Script.Nodes.Single(n => n.Id == first.Id);
		var restoredSecond = vm.Script.Nodes.Single(n => n.Id == second.Id);
		Assert.Equal(firstOriginal.X, restoredFirst.X);
		Assert.Equal(firstOriginal.Y, restoredFirst.Y);
		Assert.Equal(secondOriginal.X, restoredSecond.X);
		Assert.Equal(secondOriginal.Y, restoredSecond.Y);
	}

	[Fact]
	public void DeleteSelectedCommand_UndoesMultiNodeDeleteAsSingleEdit()
	{
		using var vm = CreateViewModel();
		var first = vm.Script.Nodes[0];
		var second = vm.Script.Nodes[1];
		var initialCount = vm.Script.Nodes.Count;

		vm.SelectNode(first, false);
		vm.SelectNode(second, true);
		vm.DeleteSelectedCommand.Execute(null);

		Assert.Equal(initialCount - 2, vm.Script.Nodes.Count);
		Assert.DoesNotContain(vm.Script.Nodes, n => n.Id == first.Id);
		Assert.DoesNotContain(vm.Script.Nodes, n => n.Id == second.Id);

		vm.UndoCommand.Execute(null);

		Assert.Equal(initialCount, vm.Script.Nodes.Count);
		Assert.Contains(vm.Script.Nodes, n => n.Id == first.Id);
		Assert.Contains(vm.Script.Nodes, n => n.Id == second.Id);
		Assert.True(vm.CanRedo);

		vm.RedoCommand.Execute(null);

		Assert.Equal(initialCount - 2, vm.Script.Nodes.Count);
		Assert.DoesNotContain(vm.Script.Nodes, n => n.Id == first.Id);
		Assert.DoesNotContain(vm.Script.Nodes, n => n.Id == second.Id);
	}

	[Fact]
	public void ParameterEdit_IsUndoable_AfterSelectionChangeCommitsIt()
	{
		using var vm = CreateViewModel();
		var node = vm.Script.Nodes.Single(n => n.DefinitionId == "inventory.drop");
		vm.SelectNode(node, toggle: false);
		var parameter = node.Parameters.Single(p => p.Key == "items");
		var original = parameter.RawValue;

		parameter.RawValue = "Raw lobster";
		// Switching selection commits the pending property edit as its own history entry.
		vm.SelectNode(vm.Script.Nodes[0], toggle: false);

		Assert.True(vm.CanUndo);
		vm.UndoCommand.Execute(null);

		var restored = vm.Script.Nodes.Single(n => n.Id == node.Id).Parameters.Single(p => p.Key == "items");
		Assert.Equal(original, restored.RawValue);

		vm.RedoCommand.Execute(null);
		var redone = vm.Script.Nodes.Single(n => n.Id == node.Id).Parameters.Single(p => p.Key == "items");
		Assert.Equal("Raw lobster", redone.RawValue);
	}

	[Fact]
	public void NodeTitleEdit_IsCapturedByUndo()
	{
		using var vm = CreateViewModel();
		var node = vm.Script.Nodes[1];
		var original = node.Title;

		node.Title = "Renamed node";
		vm.UndoCommand.Execute(null); // undo flushes the pending property edit first, then pops it

		Assert.Equal(original, vm.Script.Nodes.Single(n => n.Id == node.Id).Title);
	}

	[Fact]
	public void UndoRedo_PreservesSelectionById()
	{
		using var vm = CreateViewModel();
		var definition = vm.Definitions.Single(d => d.Id == "traversal.wait");
		var keep = vm.Script.Nodes[1];

		vm.CreateNodeFromDefinitionCommand.Execute(definition);
		vm.SelectNode(keep, toggle: false);

		vm.UndoCommand.Execute(null);

		Assert.NotNull(vm.SelectedNode);
		Assert.Equal(keep.Id, vm.SelectedNode!.Id);
		Assert.Contains(vm.SelectedNodes, n => n.Id == keep.Id);

		vm.RedoCommand.Execute(null);

		Assert.NotNull(vm.SelectedNode);
		Assert.Equal(keep.Id, vm.SelectedNode!.Id);
	}

	[Fact]
	public void SelectionChange_Alone_DoesNotCreateUndoEntries()
	{
		using var vm = CreateViewModel();

		vm.SelectNode(vm.Script.Nodes[1], toggle: false);
		vm.SelectNode(vm.Script.Nodes[2], toggle: false);
		vm.ClearSelection();

		Assert.False(vm.CanUndo);
		Assert.False(vm.IsDirty);
	}

	[Fact]
	public void DeleteTransitionsIntersectingLine_RemovesCrossedTransition()
	{
		using var vm = CreateViewModel();
		var a = new NodeModel { Title = "A", DefinitionId = "traversal.wait", X = 20, Y = 20 };
		var b = new NodeModel { Title = "B", DefinitionId = "traversal.wait", X = 260, Y = 20 };
		var graph = new GraphModel { Name = "cut test", StartNodeId = a.Id };
		graph.Nodes.Add(a);
		graph.Nodes.Add(b);
		a.Transitions.Add(new TransitionModel { FromNodeId = a.Id, ToNodeId = b.Id });
		vm.LoadGraph(graph);

		var removed = vm.DeleteTransitionsIntersectingLine(new System.Windows.Point(255, 0), new System.Windows.Point(255, 180));

		Assert.Equal(1, removed);
		Assert.Empty(a.Transitions);
		Assert.True(vm.IsDirty);
	}

	[Fact]
	public void CollapsingLeftPanel_ShrinksToRailWidthAndRestoresOnExpand()
	{
		using var vm = CreateViewModel();
		vm.LeftColumnWidth = new System.Windows.GridLength(340);

		vm.IsLeftCollapsed = true;

		Assert.Equal(NodeEditorViewModel.CollapsedRailWidth, vm.LeftColumnWidth.Value);

		vm.IsLeftCollapsed = false;

		Assert.Equal(340, vm.LeftColumnWidth.Value);
	}

	[Fact]
	public void CollapsingRightPanel_ShrinksToRailWidthAndRestoresOnExpand()
	{
		using var vm = CreateViewModel();
		vm.RightColumnWidth = new System.Windows.GridLength(380);

		vm.IsRightCollapsed = true;

		Assert.Equal(NodeEditorViewModel.CollapsedRailWidth, vm.RightColumnWidth.Value);

		vm.IsRightCollapsed = false;

		Assert.Equal(380, vm.RightColumnWidth.Value);
	}

	[Fact]
	public void ExpandingPanel_RespectsMinimumWidthFloor()
	{
		using var vm = CreateViewModel();
		vm.LeftColumnWidth = new System.Windows.GridLength(120);

		vm.IsLeftCollapsed = true;
		vm.IsLeftCollapsed = false;

		// The expand path floors the restored width so the panel never reopens unusably narrow.
		Assert.True(vm.LeftColumnWidth.Value >= 220);
	}

	[Fact]
	public void MovingNode_UpdatesOnlyItsEdgesGeometry()
	{
		using var vm = CreateViewModel();
		var node = vm.Script.Nodes[0];
		Assert.NotEmpty(vm.Edges);

		var touching = vm.Edges.Where(e => e.From.Id == node.Id || e.To.Id == node.Id).ToList();
		var untouched = vm.Edges.Except(touching).ToList();
		Assert.NotEmpty(touching);
		var touchingBefore = touching.Select(e => e.Geometry).ToList();
		var untouchedBefore = untouched.Select(e => e.Geometry).ToList();

		// Dragging a node updates X then Y; only edges attached to it must recompute.
		node.X += 24;
		node.Y += 18;

		for (var i = 0; i < touching.Count; i++)
			Assert.NotSame(touchingBefore[i], touching[i].Geometry);
		for (var i = 0; i < untouched.Count; i++)
			Assert.Same(untouchedBefore[i], untouched[i].Geometry);
	}

	[Fact]
	public void Edges_RebuildOnConnectAndRetarget()
	{
		using var vm = CreateViewModel();
		var initialEdgeCount = vm.Edges.Count;
		var from = vm.Script.Nodes[0];
		var to = vm.Script.Nodes.First(n => n.Id != from.Id && from.Transitions.All(t => t.ToNodeId != n.Id));

		vm.ConnectNodes(from, to);

		Assert.Equal(initialEdgeCount + 1, vm.Edges.Count);
		var edge = vm.Edges.Single(e => e.Transition.FromNodeId == from.Id && e.Transition.ToNodeId == to.Id);
		Assert.Same(from, edge.From);
		Assert.Same(to, edge.To);
	}

	[Fact]
	public void DashboardItemRow_AccumulatesInflowOutflowNetAndRates()
	{
		var row = new DashboardItemRow(385, "Shark");

		row.Observe(0, 100, 0);     // baseline snapshot
		row.Observe(5, 100, 1.0);   // +5 gained
		row.Observe(2, 100, 1.0);   // -3 lost

		Assert.Equal(5, row.In);
		Assert.Equal(3, row.Out);
		Assert.Equal(2, row.Net);
		Assert.Equal(100, row.UnitValue);
		Assert.True(row.IsActive);
		Assert.Equal(2.0, row.PerHour, 3);   // net 2 over 1 hour
		Assert.Equal(200, row.GpPerHour);    // net 2 × 100 gp
	}

	[Fact]
	public void DashboardItemRow_ValuesMoneyPouchMovementAsRawGp()
	{
		var row = new DashboardItemRow(-995, "Money pouch");

		row.Observe(1_000, 1, 0);   // baseline snapshot
		row.Observe(1_600, 1, 1.0); // +600 gp gained

		Assert.Equal(600, row.In);
		Assert.Equal(0, row.Out);
		Assert.Equal(600, row.Net);
		Assert.Equal(1, row.UnitValue);
		Assert.Equal(600, row.GpPerHour);
	}

	[Fact]
	public void DashboardToggles_DefaultToExpectedStateAndAreSettable()
	{
		using var vm = CreateViewModel();

		Assert.NotNull(vm.Dashboard.ItemsView);
		Assert.True(vm.Dashboard.ItemsActiveOnly);   // items default to showing only what moved
		Assert.False(vm.Dashboard.XpActiveOnly);     // XP defaults to all skills

		vm.Dashboard.XpActiveOnly = true;
		vm.Dashboard.ItemsActiveOnly = false;

		Assert.True(vm.Dashboard.XpActiveOnly);
		Assert.False(vm.Dashboard.ItemsActiveOnly);
	}

	[Fact]
	public void ShowNodeInfo_UsesDefinitionMetadataAndParameterTips()
	{
		using var vm = CreateViewModel();
		var node = vm.Script.Nodes.Single(n => n.DefinitionId == "inventory.drop");

		vm.ShowNodeInfo(node);

		Assert.True(vm.IsNodeInfoOpen);
		Assert.Equal("Drop item(s)", vm.NodeInfoTitle);
		Assert.Contains("Drop matching items", vm.NodeInfoDescription);
		Assert.Contains(vm.NodeInfoUsageTips, tip => tip.Contains("Item name(s) / id(s)") && tip.Contains("required"));
	}
}
