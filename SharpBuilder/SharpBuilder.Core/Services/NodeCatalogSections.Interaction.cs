using System.Collections.Generic;
using MESharp.API;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

internal static partial class NodeCatalogSections
{
	private static IEnumerable<NodeDefinition> CreateInteractionDefinitions()
	{
		yield return new NodeDefinition
		{
		Id = "npcs.find",
		Title = "Find NPC",
		ShortDescription = "Query nearby NPC(s) by name/id and emit a boolean signal.",
		Icon = "Magnify",
		CategoryId = "npcs",
		Order = 1,
		HasQuery = true,
		Parameters = new []
		{
		new NodeParam { Key = "name", Label = "NPC name", Type = NodeParamType.String, HasQuery = true, Placeholder = "Fishing spot / Banker / Green dragon" },
		new NodeParam { Key = "id", Label = "NPC id", Type = NodeParamType.Number, Placeholder = "Optional id filter" },
		new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, Placeholder = "Optional range (tiles)" },
		new NodeParam { Key = "signal", Label = "Output signal", Type = NodeParamType.String, IsRequired = true, Placeholder = "hasNearbyNpc" },
		new NodeParam { Key = "expected", Label = "Expect found?", Type = NodeParamType.Bool, IsRequired = true }
		}
		};
		yield return new NodeDefinition
		{
		Id = "npcs.interact",
		Title = "Interact with NPC",
		ShortDescription = "Interact with an NPC by name/id using the standard NPC action route.",
		Icon = "AccountArrowRight",
		CategoryId = "npcs",
		Order = 2,
		HasQuery = true,
		Parameters = new []
		{
		new NodeParam { Key = "name", Label = "NPC name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, HasQuery = true, Placeholder = "Banker, Fishing spot" },
		new NodeParam { Key = "id", Label = "NPC id", Type = NodeParamType.Number, Placeholder = "Optional" },
		new NodeParam { Key = "actionIndex", Label = "Action opcode (capture)", Type = NodeParamType.Number, DefaultValue = "44", Placeholder = "Native command1_action. Capture from game if the default does not match this NPC.", IsAdvanced = true },
		new NodeParam { Key = "offset", Label = "Route offset", Type = NodeParamType.Enum, EnumValues = OffsetChoices(ActionOffsets.OffsetCategory.Npc), DefaultValue = OffsetChoice("InteractNPC_route", Npcs.InteractNPC_route), Placeholder = "OFF_ACT route used by native DoAction", IsAdvanced = true },
		new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, DefaultValue = "50", Placeholder = "50", IsAdvanced = true }
		}
		};
		yield return new NodeDefinition
		{
		Id = "npcs.attack",
		Title = "Attack NPC (advanced)",
		ShortDescription = "Attack an NPC by name/id using captured native opcode and route offset.",
		Icon = "Sword",
		CategoryId = "npcs",
		Order = 3,
		HasQuery = true,
		Maturity = NodeMaturity.Advanced,
		Parameters = new []
		{
		new NodeParam { Key = "name", Label = "NPC name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, HasQuery = true, Placeholder = "Green dragon, Baby green dragon" },
		new NodeParam { Key = "id", Label = "NPC id", Type = NodeParamType.Number, Placeholder = "Optional" },
		new NodeParam { Key = "actionIndex", Label = "Action opcode (capture)", Type = NodeParamType.Number, DefaultValue = "42", Placeholder = "Native command1_action. 42 (0x2a) = attack NPC. Use \"Capture from game\" to set it from a real click if 42 fails." },
		new NodeParam { Key = "offset", Label = "Route offset", Type = NodeParamType.Enum, EnumValues = OffsetChoices(ActionOffsets.OffsetCategory.Npc), DefaultValue = OffsetChoice("AttackNPC_route", Npcs.AttackNPC_route), Placeholder = "OFF_ACT route used by native DoAction" },
		new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, DefaultValue = "50", Placeholder = "Optional range" }
		}
		};
		yield return new NodeDefinition
		{
		Id = "objects.interact",
		Title = "Object interact (advanced)",
		ShortDescription = "Interact with a world object by name/id using captured native opcode and route offset.",
		Icon = "HammerWrench",
		CategoryId = "objects",
		Order = 0,
		HasQuery = true,
		Maturity = NodeMaturity.Advanced,
		Parameters = new []
		{
		new NodeParam { Key = "name", Label = "Object name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, HasQuery = true, Placeholder = "Bank chest, Gate, 11338" },
		new NodeParam { Key = "id", Label = "Object id", Type = NodeParamType.Number },
		new NodeParam { Key = "actionIndex", Label = "Action opcode (capture)", Type = NodeParamType.Number, DefaultValue = "58", Placeholder = "Native command1_action. 58 (0x3a) = general object action. It varies per object — use \"Capture from game\" to set the exact opcode + offset from a real click." },
		new NodeParam { Key = "offset", Label = "Route offset", Type = NodeParamType.Enum, EnumValues = OffsetChoices(ActionOffsets.OffsetCategory.Object), DefaultValue = OffsetChoice("GeneralObject_route0", Objects.Offsets.GeneralRoute0), Placeholder = "OFF_ACT route used by native DoAction. route0 = first/left-click option" },
		new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, DefaultValue = "50", Placeholder = "50", IsAdvanced = true },
		new NodeParam { Key = "valid", Label = "Require valid flag", Type = NodeParamType.Bool, IsAdvanced = true },
		new NodeParam { Key = "option", Label = "Legacy option", Type = NodeParamType.String, Placeholder = "Optional fallback: Open / Climb / 2", IsAdvanced = true }
		}
		};
		yield return new NodeDefinition
		{
		Id = "objects.interactHighlighted",
		Title = "Object interact highlighted (advanced)",
		ShortDescription = "Screen-click the target object aligned to a highlight marker, useful for Rockertunity-style targets.",
		Icon = "StarFourPoints",
		CategoryId = "objects",
		Order = 1,
		HasQuery = true,
		Maturity = NodeMaturity.Advanced,
		Parameters = new []
		{
		new NodeParam { Key = "objectIds", Label = "Object id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, Placeholder = "113125, 113126, 113127" },
		new NodeParam { Key = "highlightIds", Label = "Highlight marker id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, DefaultValue = "7165, 7164", Placeholder = "7165, 7164" },
		new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, DefaultValue = "50", Placeholder = "50" },
		new NodeParam { Key = "actionIndex", Label = "Click mode", Type = NodeParamType.Enum, EnumValues = new [] { "Left-click = 0", "Right-click / menu = 1", "Dry-run (no click) = 3" }, DefaultValue = "Left-click = 0", Placeholder = "Mouse click mode for the highlighted object (NOT an opcode — this path screen-clicks). Left-click mines/chops." }
		}
		};
		yield return new NodeDefinition
		{
		Id = "objects.find",
		Title = "Find Object",
		ShortDescription = "Query nearby objects by name/id and emit a boolean signal.",
		Icon = "Magnify",
		CategoryId = "objects",
		Order = 2,
		HasQuery = true,
		Parameters = new []
		{
		new NodeParam { Key = "name", Label = "Object name", Type = NodeParamType.String, HasQuery = true, Placeholder = "Fishing spot / Bank chest / Wilderness wall" },
		new NodeParam { Key = "id", Label = "Object id", Type = NodeParamType.Number, Placeholder = "Optional id filter" },
		new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, Placeholder = "Optional range (tiles)" },
		new NodeParam { Key = "signal", Label = "Output signal", Type = NodeParamType.String, IsRequired = true, Placeholder = "hasNearbyObject" },
		new NodeParam { Key = "expected", Label = "Expect found?", Type = NodeParamType.Bool, IsRequired = true }
		}
		};
		yield return new NodeDefinition
		{
		Id = "objects.exists",
		Title = "Object exists?",
		ShortDescription = "Check if an object is nearby (by id/name).",
		Icon = "Magnify",
		CategoryId = "objects",
		Order = 3,
		Parameters = new []
		{
		new NodeParam { Key = "name", Label = "Object name", Type = NodeParamType.String, Placeholder = "Gate" },
		new NodeParam { Key = "id", Label = "Object id", Type = NodeParamType.Number },
		new NodeParam { Key = "expected", Label = "Should exist?", Type = NodeParamType.Bool }
		}
		};
	}
}
