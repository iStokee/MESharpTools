using System.Text.RegularExpressions;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
using Xunit;

namespace SharpBuilder.Core.Tests;

public class NodeCatalogServiceTests
{
	private readonly NodeCatalogService _catalog = new();

	[Fact]
	public void Definitions_AreUniqueAndReferenceKnownCategories()
	{
		var categoryIds = _catalog.Categories.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var definitionIds = _catalog.Definitions.Select(d => d.Id).ToList();

		Assert.Equal(definitionIds.Count, definitionIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
		Assert.All(_catalog.Definitions, definition =>
		{
			Assert.False(string.IsNullOrWhiteSpace(definition.Id));
			Assert.Contains(definition.CategoryId, categoryIds);
		});
	}

	[Fact]
	public void Categories_HaveStablePaletteMetadata()
	{
		var ids = _catalog.Categories.Select(c => c.Id).ToList();
		var hexColor = new Regex("^#[0-9A-Fa-f]{6}$");

		Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
		Assert.All(_catalog.Categories, category =>
		{
			Assert.False(string.IsNullOrWhiteSpace(category.Title));
			Assert.False(string.IsNullOrWhiteSpace(category.Icon));
			Assert.False(string.IsNullOrWhiteSpace(category.Slug));
			Assert.Matches(hexColor, category.AccentColor);
		});
	}

	[Fact]
	public void GetDefinition_IsCaseInsensitive()
	{
		var definition = _catalog.GetDefinition("INVENTORY.DROP");

		Assert.NotNull(definition);
		Assert.Equal("inventory.drop", definition.Id);
	}

	[Fact]
	public void InteractionCatalog_KeepsSimpleNpcInteractAndMarksNativeCaptureNodesAdvanced()
	{
		var npcInteract = _catalog.GetDefinition("npcs.interact");
		Assert.NotNull(npcInteract);
		Assert.Equal(NodeMaturity.Stable, npcInteract.Maturity);
		Assert.Contains(npcInteract.Parameters, p => p.Key == "name" && !p.IsAdvanced);
		Assert.Contains(npcInteract.Parameters, p => p.Key == "actionIndex" && p.IsAdvanced);
		Assert.Contains(npcInteract.Parameters, p => p.Key == "offset" && p.IsAdvanced);

		Assert.Equal(NodeMaturity.Advanced, _catalog.GetDefinition("npcs.attack")?.Maturity);
		Assert.Equal(NodeMaturity.Advanced, _catalog.GetDefinition("objects.interact")?.Maturity);
		Assert.Equal(NodeMaturity.Advanced, _catalog.GetDefinition("objects.interactHighlighted")?.Maturity);
		Assert.Equal(NodeMaturity.Advanced, _catalog.GetDefinition("actions.interaction")?.Maturity);
	}

	[Theory]
	[InlineData(NodeType.Start, NodeCatalogDefaults.StartId)]
	[InlineData(NodeType.Terminal, NodeCatalogDefaults.TerminalId)]
	[InlineData(NodeType.Condition, NodeCatalogDefaults.BooleanConditionId)]
	[InlineData(NodeType.Action, NodeCatalogDefaults.GenericActionId)]
	public void GetDefaultDefinitionForType_ReturnsExpectedDefinition(NodeType type, string expectedId)
	{
		var definition = _catalog.GetDefaultDefinitionForType(type);

		Assert.Equal(expectedId, definition.Id);
	}

	[Fact]
	public void ImplementedDefinitions_HaveExecutableRegistryEntries()
	{
		var registry = new NodeExecutorRegistry();
		var field = typeof(NodeExecutorRegistry).GetField("_executors", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		var executors = Assert.IsAssignableFrom<IReadOnlyDictionary<string, INodeExecutor>>(field?.GetValue(registry));

		foreach (var definition in _catalog.Definitions.Where(d => d.IsImplemented))
		{
			Assert.Contains(definition.Id, executors.Keys, StringComparer.OrdinalIgnoreCase);
		}
	}

	[Fact]
	public void RequiredEnumParameters_HaveChoices()
	{
		var enumParameters = _catalog.Definitions
			.SelectMany(d => d.Parameters.Select(p => (Definition: d, Parameter: p)))
			.Where(pair => pair.Parameter.Type == NodeParamType.Enum)
			.ToList();

		Assert.NotEmpty(enumParameters);
		Assert.All(enumParameters, pair =>
		{
			Assert.NotNull(pair.Parameter.EnumValues);
			Assert.NotEmpty(pair.Parameter.EnumValues!);
			Assert.All(pair.Parameter.EnumValues!, value => Assert.False(string.IsNullOrWhiteSpace(value)));
		});
	}
}
