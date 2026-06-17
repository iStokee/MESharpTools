using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

/// <summary>
/// Provides seeded node categories/definitions and lookup helpers for the visual node editor.
/// </summary>
public class NodeCatalogService
{
	private readonly ReadOnlyCollection<NodeCategory> _categories;
	private readonly ReadOnlyCollection<NodeDefinition> _definitions;
	private readonly IReadOnlyDictionary<string, NodeDefinition> _definitionById;

	public NodeCatalogService()
	{
		var categories = NodeCatalogSections.CreateCategories();
		var definitions = NodeCatalogSections.CreateDefinitions()
			.Select(ApplySignalMetadata)
			.ToList();
		_categories = new ReadOnlyCollection<NodeCategory>(categories.OrderBy(c => c.Order).ToList());
		_definitions = new ReadOnlyCollection<NodeDefinition>(definitions.OrderBy(d => d.Order).ToList());
		_definitionById = _definitions.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
	}

	public IReadOnlyList<NodeCategory> Categories => _categories;
	public IReadOnlyList<NodeDefinition> Definitions => _definitions;


	public NodeDefinition? GetDefinition(string? id)
	{
		if (string.IsNullOrWhiteSpace(id))
			return null;

		return _definitionById.TryGetValue(id, out var def) ? def : null;
	}

	public NodeDefinition GetDefaultDefinitionForType(NodeType type)
	{
		return type switch
		{
			NodeType.Start => GetDefinition(NodeCatalogDefaults.StartId)!,
			NodeType.Terminal => GetDefinition(NodeCatalogDefaults.TerminalId)!,
			NodeType.Condition => GetDefinition(NodeCatalogDefaults.BooleanConditionId)!,
			_ => GetDefinition(NodeCatalogDefaults.GenericActionId)!
		};
	}

	private static NodeDefinition ApplySignalMetadata(NodeDefinition definition)
	{
		return definition.Id switch
		{
			"conditions.inventoryFull" => WithFixedSignal(definition, "inventoryFull"),
			"conditions.inCombat" => WithFixedSignal(definition, "inCombat"),
			"inventory.contains" => WithFixedSignal(definition, "inventory.contains"),
			"inventory.count" => WithFixedSignal(definition, "inventory.count.met"),
			"equipment.contains" => WithFixedSignal(definition, "equipment.contains"),
			"objects.exists" => WithFixedSignal(definition, "objects.exists"),

			"actions.setSignal" => WithSignalParameter(definition, defaultKey: null),
			"npcs.find" => WithSignalParameter(definition, "npcs.found"),
			"objects.find" => WithSignalParameter(definition, "objects.found"),
			"conditions.locationRadius" => WithSignalParameter(definition, "insideAnchor"),
			"conditions.healthPercent" => WithSignalParameter(definition, "healthLow"),
			"conditions.prayerPercent" => WithSignalParameter(definition, "prayerLow"),
			"conditions.cooldown" => WithSignalParameter(definition, "cooldownReady"),
			"familiar.check" => WithSignalParameter(definition, "hasFamiliar"),
			_ => definition
		};

		static NodeDefinition WithFixedSignal(NodeDefinition definition, string key) => new()
		{
			Id = definition.Id,
			Title = definition.Title,
			ShortDescription = definition.ShortDescription,
			Icon = definition.Icon,
			CategoryId = definition.CategoryId,
			Order = definition.Order,
			HasQuery = definition.HasQuery,
			IsImplemented = definition.IsImplemented,
			Maturity = definition.Maturity,
			RequiresGameApi = definition.RequiresGameApi,
			Parameters = definition.Parameters,
			PublishedSignalKey = key
		};

		static NodeDefinition WithSignalParameter(NodeDefinition definition, string? defaultKey) => new()
		{
			Id = definition.Id,
			Title = definition.Title,
			ShortDescription = definition.ShortDescription,
			Icon = definition.Icon,
			CategoryId = definition.CategoryId,
			Order = definition.Order,
			HasQuery = definition.HasQuery,
			IsImplemented = definition.IsImplemented,
			Maturity = definition.Maturity,
			RequiresGameApi = definition.RequiresGameApi,
			Parameters = definition.Parameters,
			PublishedSignalParameterKey = "signal",
			DefaultPublishedSignalKey = defaultKey
		};
	}
}
