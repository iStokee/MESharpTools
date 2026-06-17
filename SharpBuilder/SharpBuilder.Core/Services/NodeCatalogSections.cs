using System.Collections.Generic;
using System.Linq;
using MESharp.API;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

internal static partial class NodeCatalogSections
{
	public static IReadOnlyList<NodeCategory> CreateCategories()
	{
		return new[]
		{
			new NodeCategory { Id = "all", Title = "All", Description = "All node types", Icon = "Apps", Order = -1, Slug = "all", AccentColor = "#4AB6FF" },
			new NodeCategory { Id = "control", Title = "Control", Description = "Start/terminal helpers", Icon = "Rocket", Order = 0, Slug = "control", AccentColor = "#4AB6FF" },
			new NodeCategory { Id = "conditions", Title = "Conditions", Description = "Checks that gate transitions", Icon = "HelpCircle", Order = 1, Slug = "conditions", AccentColor = "#FFC857" },
			new NodeCategory { Id = "actions", Title = "Actions", Description = "Game-facing actions (interaction, traversal, shop)", Icon = "Play", Order = 2, Slug = "actions", AccentColor = "#6EE86E" },
			new NodeCategory { Id = "traversal", Title = "Traversal", Description = "Movement and world hops", Icon = "Map", Order = 3, Slug = "traversal", AccentColor = "#9B8CFF" },
			new NodeCategory { Id = "inventory", Title = "Inventory", Description = "Bag logic, item usage, gear", Icon = "BagPersonal", Order = 4, Slug = "inventory", AccentColor = "#FF9F6E" },
			new NodeCategory { Id = "equipment", Title = "Equipment", Description = "Worn gear checks and unequip", Icon = "TshirtCrew", Order = 5, Slug = "equipment", AccentColor = "#E86E8A" },
			new NodeCategory { Id = "bank", Title = "Bank", Description = "Banking operations and presets", Icon = "Safe", Order = 6, Slug = "bank", AccentColor = "#E8C46E" },
			new NodeCategory { Id = "npcs", Title = "NPCs", Description = "Interact, attack, query NPCs", Icon = "AccountGroup", Order = 7, Slug = "npcs", AccentColor = "#FF6EC7" },
			new NodeCategory { Id = "objects", Title = "Objects", Description = "Interact with world objects", Icon = "CubeOutline", Order = 8, Slug = "objects", AccentColor = "#6EDAE8" },
			new NodeCategory { Id = "loot", Title = "Loot", Description = "Ground item pickup / filters", Icon = "TreasureChest", Order = 9, Slug = "loot", AccentColor = "#C0E86E" },
			new NodeCategory { Id = "combat", Title = "Combat", Description = "Combat sustain and target-loop helpers", Icon = "SwordCross", Order = 10, Slug = "combat", AccentColor = "#F87171" },
			new NodeCategory { Id = "familiar", Title = "Familiar", Description = "Summoning familiar checks and actions", Icon = "Paw", Order = 11, Slug = "familiar", AccentColor = "#7CDEDC" },
			new NodeCategory { Id = "input", Title = "Input", Description = "Keyboard and mouse dispatch", Icon = "Keyboard", Order = 12, Slug = "input", AccentColor = "#B0B8C4" },
			new NodeCategory { Id = "skills", Title = "Skills", Description = "Skill checks and thresholds", Icon = "ChartLine", Order = 13, Slug = "skills", AccentColor = "#6E9FE8" },
			new NodeCategory { Id = "trade", Title = "Trade/UI", Description = "Trade window and UI helpers", Icon = "Handshake", Order = 14, Slug = "trade", AccentColor = "#D49FE8" },
			new NodeCategory { Id = "ui", Title = "Script UI", Description = "Canvas dashboards and visual controls", Icon = "ViewDashboard", Order = 15, Slug = "ui", AccentColor = "#7DD3FC" },
			new NodeCategory { Id = "misc", Title = "Misc", Description = "Utility nodes", Icon = "DotsHorizontal", Order = 16, Slug = "misc", AccentColor = "#8E9AAB" }
		};
	}

	public static IReadOnlyList<NodeDefinition> CreateDefinitions()
	{
		return CreateControlDefinitions()
			.Concat(CreateActionDefinitions())
			.Concat(CreateTraversalDefinitions())
			.Concat(CreateInventoryDefinitions())
			.Concat(CreateInteractionDefinitions())
			.Concat(CreateCombatDefinitions())
			.Concat(CreateMiscDefinitions())
			.ToList();
	}

	private static IReadOnlyList<string> OffsetChoices(params ActionOffsets.OffsetCategory[] categories)
	{
		return ActionOffsets.ForCategories(categories)
			.Select(offset => OffsetChoice(offset.Key, offset.Value))
			.ToArray();
	}

	private static string OffsetChoice(string key, int value) => $"{key} = {value}";
}
