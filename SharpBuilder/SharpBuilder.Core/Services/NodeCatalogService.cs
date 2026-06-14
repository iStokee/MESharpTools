using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MESharp.API;
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
		var categories = new[]
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

		var definitions = new List<NodeDefinition>
		{
			new NodeDefinition
			{
				Id = NodeCatalogDefaults.StartId,
				Title = "Start",
				ShortDescription = "Entry point to mark as the graph start.",
				Icon = "Flag",
				CategoryId = "control",
				Order = 0,
				RequiresGameApi = false,
				Parameters = Array.Empty<NodeParam>()
			},
			new NodeDefinition
			{
				Id = NodeCatalogDefaults.TerminalId,
				Title = "End / Terminal",
				ShortDescription = "Terminal node that stops the runner.",
				Icon = "Stop",
				CategoryId = "control",
				Order = 1,
				RequiresGameApi = false,
				Parameters = Array.Empty<NodeParam>()
			},
			new NodeDefinition
			{
				Id = NodeCatalogDefaults.BooleanConditionId,
				Title = "Boolean Condition",
				ShortDescription = "Evaluates a boolean signal and branches flows.",
				Icon = "Help",
				CategoryId = "conditions",
				Order = 0,
				RequiresGameApi = false,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam
					{
						Key = "signal",
						Label = "Signal key",
						Type = NodeParamType.String,
						IsRequired = true,
						Placeholder = "inventoryFull / hasNearbySpot / custom"
					},
					new NodeParam
					{
						Key = "expected",
						Label = "Expect true?",
						Type = NodeParamType.Bool,
						IsRequired = true
					}
				}
			},
			new NodeDefinition
			{
				Id = NodeCatalogDefaults.GenericActionId,
				Title = "Placeholder / Note",
				ShortDescription = "Performs NO game action — a labeled sketch step. Only the dwell delay applies; swap it for a real node before relying on it.",
				Icon = "NoteOutline",
				CategoryId = "actions",
				Order = 0,
				RequiresGameApi = false,
				Parameters = new []
				{
					new NodeParam
					{
						Key = "action",
						Label = "Notes (not executed)",
						Type = NodeParamType.String,
						Placeholder = "Describe the intended step, then replace with a real node"
					}
				}
			},
			new NodeDefinition
			{
				Id = NodeCatalogDefaults.ScriptDashboardId,
				Title = "Script UI Dashboard",
				ShortDescription = "Free-floating tabbed runtime and XP panel rendered directly on the node canvas.",
				Icon = "ViewDashboardOutline",
				CategoryId = "ui",
				Order = 0,
				RequiresGameApi = false,
				Parameters = new []
				{
					new NodeParam { Key = "collapsed", Label = "Collapse panel", Type = NodeParamType.Bool },
					new NodeParam { Key = "showOnlyActiveXp", Label = "XP: show only active", Type = NodeParamType.Bool },
					new NodeParam { Key = "refreshSeconds", Label = "Refresh seconds", Type = NodeParamType.Number, DefaultValue = "2" }
				}
			},
			new NodeDefinition
			{
				Id = "actions.interaction",
				Title = "Interaction",
				ShortDescription = "Interact with a target (entity/object/NPC) with a menu option.",
				Icon = "CursorDefaultClick",
				CategoryId = "actions",
				Order = 1,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam { Key = "target", Label = "Target name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, HasQuery = true, AllowPartial = true, Placeholder = "Bank booth, Mud rune altar, 12345" },
					new NodeParam { Key = "option", Label = "Interaction option", Type = NodeParamType.String, Placeholder = "Interact / Talk-to / Trade / Use / 2 (case-insensitive)" },
					new NodeParam { Key = "allowPartial", Label = "Allow partial match", Type = NodeParamType.Bool, AllowPartial = true }
				}
			},
			new NodeDefinition
			{
				Id = "actions.shop",
				Title = "Shop",
				ShortDescription = "Buy or sell items via shop interface.",
				Icon = "Store",
				CategoryId = "actions",
				Order = 2,
				IsImplemented = false,
				Parameters = new []
				{
					new NodeParam { Key = "mode", Label = "Mode", Type = NodeParamType.Enum, EnumValues = new [] { "Buy", "Sell" }, IsRequired = true },
					new NodeParam { Key = "items", Label = "Items", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "Item name(s) separated by newlines" },
					new NodeParam { Key = "quantity", Label = "Quantity", Type = NodeParamType.Number, Placeholder = "0 = all" }
				}
			},
			new NodeDefinition
			{
				Id = "traversal.worldhop",
				Title = "World hop",
				ShortDescription = "Switch to a target world, optionally filtered by region/type.",
				Icon = "Earth",
				CategoryId = "traversal",
				Order = 0,
				IsImplemented = false,
				Parameters = new []
				{
					new NodeParam { Key = "world", Label = "World", Type = NodeParamType.Number, Placeholder = "E.g., 1-140" },
					new NodeParam { Key = "preferMembers", Label = "Members only", Type = NodeParamType.Bool },
					new NodeParam { Key = "region", Label = "Region", Type = NodeParamType.Enum, EnumValues = new [] { "US", "EU", "AUS" } }
				}
			},
			new NodeDefinition
			{
				Id = "traversal.walk",
				Title = "Walk / traverse path",
				ShortDescription = "Traverse to coordinate(s) or area.",
				Icon = "MapMarkerPath",
				CategoryId = "traversal",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "target", Label = "Target tile(s)", Type = NodeParamType.Coordinate, AllowMultiple = true, Placeholder = "x,y or x,y,z" },
					new NodeParam { Key = "area", Label = "Target area", Type = NodeParamType.Area, AllowPartial = true, Placeholder = "Named area or polygon list" },
					new NodeParam { Key = "stopShort", Label = "Stop short (tiles)", Type = NodeParamType.Number, Placeholder = "2" },
					new NodeParam { Key = "timeoutMs", Label = "Timeout per segment (ms)", Type = NodeParamType.Number, Placeholder = "8000" },
					new NodeParam { Key = "jitter", Label = "Jitter (tiles)", Type = NodeParamType.Number, Placeholder = "1" }
				}
			},
			new NodeDefinition
			{
				Id = "keyboard.send",
				Title = "Keyboard macro",
				ShortDescription = "Send keys or text to the game client.",
				Icon = "KeyboardOutline",
				CategoryId = "input",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "keys", Label = "Keys / text", Type = NodeParamType.String, IsRequired = true, Placeholder = "e.g., Ctrl+1 or ::home" },
					new NodeParam { Key = "delayMs", Label = "Delay after send (ms)", Type = NodeParamType.Number }
				}
			},
			new NodeDefinition
			{
				Id = "input.click",
				Title = "Mouse click",
				ShortDescription = "Click at a screen coordinate or center of client.",
				Icon = "CursorDefault",
				CategoryId = "input",
				Order = 1,
				IsImplemented = false,
				Parameters = new []
				{
					new NodeParam { Key = "x", Label = "X", Type = NodeParamType.Number, Placeholder = "Screen X (optional)" },
					new NodeParam { Key = "y", Label = "Y", Type = NodeParamType.Number, Placeholder = "Screen Y (optional)" },
					new NodeParam { Key = "button", Label = "Button", Type = NodeParamType.Enum, EnumValues = new [] { "Left", "Right" }, Placeholder = "Left" }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.contains",
				Title = "Inventory contains",
				ShortDescription = "Check if inventory has any of the specified items.",
				Icon = "BagPersonal",
				CategoryId = "inventory",
				Order = 0,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam { Key = "ids", Label = "Item id(s)", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "7946, 379" },
					new NodeParam { Key = "names", Label = "Item name(s)", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "Lobster\nNature rune" },
					new NodeParam { Key = "expected", Label = "Expect present?", Type = NodeParamType.Bool, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.count",
				Title = "Inventory count",
				ShortDescription = "Emit or guard on count of an item by id or name.",
				Icon = "Counter",
				CategoryId = "inventory",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "id", Label = "Item id", Type = NodeParamType.Number, Placeholder = "Optional if using name" },
					new NodeParam { Key = "name", Label = "Item name", Type = NodeParamType.String, Placeholder = "Shark" },
					new NodeParam { Key = "min", Label = "Min count", Type = NodeParamType.Number },
					new NodeParam { Key = "max", Label = "Max count", Type = NodeParamType.Number }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.drop",
				Title = "Drop item(s)",
				ShortDescription = "Drop matching items: One, Some (count), or All until none remain.",
				Icon = "ArrowDownBold",
				CategoryId = "inventory",
				Order = 2,
				Parameters = new []
				{
					new NodeParam { Key = "items", Label = "Item name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, Placeholder = "Raw shrimps, Raw anchovies, 7946" },
					new NodeParam { Key = "quantity", Label = "Quantity", Type = NodeParamType.Enum, EnumValues = new [] { "One", "Some", "All" }, IsRequired = true, InlineCompanionKey = "count", InlineCompanionVisibleWhen = "Some" },
					new NodeParam { Key = "count", Label = "Count (for Some)", Type = NodeParamType.Number, Placeholder = "3" }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.eat",
				Title = "Eat / drink item(s)",
				ShortDescription = "Eat or drink matching items: One, Some (count), or All until none remain.",
				Icon = "FoodApple",
				CategoryId = "inventory",
				Order = 3,
				Parameters = new []
				{
					new NodeParam { Key = "items", Label = "Item name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, Placeholder = "Shark, Saradomin brew, 385" },
					new NodeParam { Key = "quantity", Label = "Quantity", Type = NodeParamType.Enum, EnumValues = new [] { "One", "Some", "All" }, IsRequired = true, InlineCompanionKey = "count", InlineCompanionVisibleWhen = "Some" },
					new NodeParam { Key = "count", Label = "Count (for Some)", Type = NodeParamType.Number, Placeholder = "2" }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.equip",
				Title = "Equip item(s)",
				ShortDescription = "Equip each listed item from the inventory (a gear set in one node).",
				Icon = "TshirtCrew",
				CategoryId = "inventory",
				Order = 4,
				Parameters = new []
				{
					new NodeParam { Key = "items", Label = "Item name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, Placeholder = "Rune scimitar\nRune kiteshield\n1333" }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.use",
				Title = "Use item (first match)",
				ShortDescription = "Acts on the FIRST matching slot only. For bulk operations prefer Drop/Eat/Equip item(s).",
				Icon = "Hand",
				CategoryId = "inventory",
				Order = 5,
				Parameters = new []
				{
					new NodeParam { Key = "id", Label = "Item id", Type = NodeParamType.Number },
					new NodeParam { Key = "name", Label = "Item name", Type = NodeParamType.String },
					new NodeParam { Key = "action", Label = "Action", Type = NodeParamType.Enum, EnumValues = new [] { "Use", "Eat/Drink", "Drop", "Equip" } }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.action",
				Title = "Inventory menu action",
				ShortDescription = "Run a raw 1-based inventory menu action against the first matching item. Useful for Drink, Activate, Fill, Empty, and other item-specific options.",
				Icon = "CursorDefaultClick",
				CategoryId = "inventory",
				Order = 6,
				Parameters = new []
				{
					new NodeParam { Key = "items", Label = "Item name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, Placeholder = "Prayer potion\nSuper restore\n2434" },
					new NodeParam { Key = "menuIndex", Label = "Menu action index", Type = NodeParamType.Number, IsRequired = true, DefaultValue = "1", Placeholder = "1 = first option" },
					new NodeParam { Key = "offset", Label = "Action route offset", Type = NodeParamType.Number, DefaultValue = "5392", IsAdvanced = true }
				}
			},
			new NodeDefinition
			{
				Id = "equipment.contains",
				Title = "Wearing item?",
				ShortDescription = "Check if any listed item is equipped; publishes the 'equipment.contains' signal.",
				Icon = "ShieldSearch",
				CategoryId = "equipment",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "items", Label = "Item name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, Placeholder = "Rune scimitar, 1333" },
					new NodeParam { Key = "expected", Label = "Expect worn?", Type = NodeParamType.Bool, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "equipment.unequip",
				Title = "Unequip item(s)",
				ShortDescription = "Unequip every listed worn item (needs free inventory space).",
				Icon = "TshirtCrewOutline",
				CategoryId = "equipment",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "items", Label = "Item name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, Placeholder = "Rune scimitar, 1333" }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.useOn",
				Title = "Use item on item",
				ShortDescription = "Use one inventory item on another.",
				Icon = "LinkVariant",
				CategoryId = "inventory",
				Order = 7,
				Parameters = new []
				{
					new NodeParam { Key = "from", Label = "Item (use)", Type = NodeParamType.String, IsRequired = true, Placeholder = "Needle / 1733" },
					new NodeParam { Key = "to", Label = "Item (target)", Type = NodeParamType.String, IsRequired = true, Placeholder = "Thread / 1734" }
				}
			},
			new NodeDefinition
			{
				Id = "bank.open",
				Title = "Open bank",
				ShortDescription = "Open nearest bank booth/chest.",
				Icon = "Safe",
				CategoryId = "bank",
				Order = 0,
				Parameters = Array.Empty<NodeParam>()
			},
			new NodeDefinition
			{
				Id = "bank.depositAll",
				Title = "Deposit all",
				ShortDescription = "Deposit everything (optionally except specific items).",
				Icon = "PackageDown",
				CategoryId = "bank",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "exceptIds", Label = "Keep item ids", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "Notepaper id, food id" },
					new NodeParam { Key = "exceptNames", Label = "Keep item names", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "Shark, Coins" }
				}
			},
			new NodeDefinition
			{
				Id = "bank.withdraw",
				Title = "Withdraw",
				ShortDescription = "Withdraw item(s) by id or name.",
				Icon = "PackageUp",
				CategoryId = "bank",
				Order = 2,
				Parameters = new []
				{
					new NodeParam { Key = "id", Label = "Item id", Type = NodeParamType.Number },
					new NodeParam { Key = "name", Label = "Item name", Type = NodeParamType.String },
					new NodeParam { Key = "amount", Label = "Amount", Type = NodeParamType.Number, Placeholder = "0 = all" }
				}
			},
			new NodeDefinition
			{
				Id = "bank.close",
				Title = "Close bank",
				ShortDescription = "Close the bank interface.",
				Icon = "CloseCircle",
				CategoryId = "bank",
				Order = 3,
				Parameters = Array.Empty<NodeParam>()
			},
				new NodeDefinition
				{
					Id = "npcs.interact",
					Title = "NPC interact",
					ShortDescription = "Interact with an NPC by name/id using a native action opcode and route offset.",
					Icon = "AccountVoice",
					CategoryId = "npcs",
					Order = 0,
					HasQuery = true,
					Parameters = new []
					{
						new NodeParam { Key = "target", Label = "NPC name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, HasQuery = true, IsRequired = true, Placeholder = "Banker, Fisherman, 3299" },
						new NodeParam { Key = "actionIndex", Label = "Action opcode (capture)", Type = NodeParamType.Number, DefaultValue = "60", Placeholder = "Native command1_action. Varies per NPC/action — use \"Capture from game\" to set it from a real click. 60/0x3c was a fishing-spot click." },
						new NodeParam { Key = "offset", Label = "Route offset", Type = NodeParamType.Enum, EnumValues = OffsetChoices(ActionOffsets.OffsetCategory.Npc), DefaultValue = OffsetChoice("InteractNPC_route", Npcs.InteractNPC_route), Placeholder = "OFF_ACT route used by native DoAction" },
						new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, DefaultValue = "50", Placeholder = "50", IsAdvanced = true },
						new NodeParam { Key = "ignoreStar", Label = "Ignore star marker", Type = NodeParamType.Bool, IsAdvanced = true },
						new NodeParam { Key = "minHealth", Label = "Min health", Type = NodeParamType.Number, DefaultValue = "0", IsAdvanced = true },
						new NodeParam { Key = "option", Label = "Legacy option", Type = NodeParamType.String, Placeholder = "Optional fallback: Talk-to / Trade / 2", IsAdvanced = true },
						new NodeParam { Key = "allowPartial", Label = "Allow partial", Type = NodeParamType.Bool, IsAdvanced = true }
					}
				},
			new NodeDefinition
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
			},
			new NodeDefinition
			{
				Id = "npcs.attack",
				Title = "Attack NPC",
				ShortDescription = "Attack an NPC by name or id.",
				Icon = "Sword",
				CategoryId = "npcs",
				Order = 2,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam { Key = "name", Label = "NPC name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, HasQuery = true, Placeholder = "Green dragon, Baby green dragon" },
					new NodeParam { Key = "id", Label = "NPC id", Type = NodeParamType.Number, Placeholder = "Optional" },
					new NodeParam { Key = "actionIndex", Label = "Action opcode (capture)", Type = NodeParamType.Number, DefaultValue = "42", Placeholder = "Native command1_action. 42 (0x2a) = attack NPC. Use \"Capture from game\" to set it from a real click if 42 fails." },
					new NodeParam { Key = "offset", Label = "Route offset", Type = NodeParamType.Enum, EnumValues = OffsetChoices(ActionOffsets.OffsetCategory.Npc), DefaultValue = OffsetChoice("AttackNPC_route", Npcs.AttackNPC_route), Placeholder = "OFF_ACT route used by native DoAction" },
					new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, DefaultValue = "50", Placeholder = "Optional range" }
				}
			},
			new NodeDefinition
			{
				Id = "objects.interact",
				Title = "Object interact",
				ShortDescription = "Interact with a world object by name/id using a native action opcode and route offset.",
					Icon = "HammerWrench",
					CategoryId = "objects",
					Order = 0,
					HasQuery = true,
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
			},
			new NodeDefinition
			{
				Id = "objects.interactHighlighted",
				Title = "Object interact highlighted",
				ShortDescription = "Click the target object aligned to a highlight marker, useful for Rockertunity-style targets.",
				Icon = "StarFourPoints",
				CategoryId = "objects",
				Order = 1,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam { Key = "objectIds", Label = "Object id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, Placeholder = "113125, 113126, 113127" },
					new NodeParam { Key = "highlightIds", Label = "Highlight marker id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, DefaultValue = "7165, 7164", Placeholder = "7165, 7164" },
					new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, DefaultValue = "50", Placeholder = "50" },
					new NodeParam { Key = "actionIndex", Label = "Click mode", Type = NodeParamType.Enum, EnumValues = new [] { "Left-click = 0", "Right-click / menu = 1", "Dry-run (no click) = 3" }, DefaultValue = "Left-click = 0", Placeholder = "Mouse click mode for the highlighted object (NOT an opcode — this path screen-clicks). Left-click mines/chops." }
				}
			},
			new NodeDefinition
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
			},
			new NodeDefinition
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
			},
			new NodeDefinition
			{
				Id = "loot.pickup",
				Title = "Loot ground items",
				ShortDescription = "Pick up ground items by name/id filter.",
				Icon = "TreasureChest",
				CategoryId = "loot",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "names", Label = "Item names", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "Bones\nGreen dragonhide" },
					new NodeParam { Key = "ids", Label = "Item ids", Type = NodeParamType.List, AllowMultiple = true, Placeholder = "526\n1753" },
					new NodeParam { Key = "maxDistance", Label = "Max distance", Type = NodeParamType.Number, Placeholder = "Optional range" }
				}
			},
			new NodeDefinition
			{
				Id = "conditions.locationRadius",
				Title = "Inside location radius?",
				ShortDescription = "Checks whether the local player is inside a center/radius anchor and publishes a signal.",
				Icon = "MapMarkerRadius",
				CategoryId = "conditions",
				Order = 4,
				Parameters = new []
				{
					new NodeParam { Key = "center", Label = "Center tile", Type = NodeParamType.Coordinate, IsRequired = true, Placeholder = "3200,3200,0" },
					new NodeParam { Key = "radius", Label = "Radius (tiles)", Type = NodeParamType.Number, IsRequired = true, DefaultValue = "12" },
					new NodeParam { Key = "signal", Label = "Output signal", Type = NodeParamType.String, DefaultValue = "insideAnchor", Placeholder = "insideAnchor" },
					new NodeParam { Key = "expected", Label = "Expect inside?", Type = NodeParamType.Bool, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "conditions.healthPercent",
				Title = "Health percent",
				ShortDescription = "Branches on the local player's health percentage.",
				Icon = "HeartPulse",
				CategoryId = "combat",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "comparison", Label = "Comparison", Type = NodeParamType.Enum, EnumValues = new [] { "<=", ">=", "<", ">", "==" }, IsRequired = true, DefaultValue = "<=" },
					new NodeParam { Key = "threshold", Label = "Threshold %", Type = NodeParamType.Number, IsRequired = true, DefaultValue = "45" },
					new NodeParam { Key = "signal", Label = "Output signal", Type = NodeParamType.String, DefaultValue = "healthLow", Placeholder = "healthLow" },
					new NodeParam { Key = "expected", Label = "Expect match?", Type = NodeParamType.Bool, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "conditions.prayerPercent",
				Title = "Prayer percent",
				ShortDescription = "Branches on the local player's prayer percentage.",
				Icon = "HandsPray",
				CategoryId = "combat",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "comparison", Label = "Comparison", Type = NodeParamType.Enum, EnumValues = new [] { "<=", ">=", "<", ">", "==" }, IsRequired = true, DefaultValue = "<=" },
					new NodeParam { Key = "threshold", Label = "Threshold %", Type = NodeParamType.Number, IsRequired = true, DefaultValue = "25" },
					new NodeParam { Key = "signal", Label = "Output signal", Type = NodeParamType.String, DefaultValue = "prayerLow", Placeholder = "prayerLow" },
					new NodeParam { Key = "expected", Label = "Expect match?", Type = NodeParamType.Bool, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "conditions.cooldown",
				Title = "Cooldown ready?",
				ShortDescription = "Publishes a signal when a per-node timer has elapsed; use it to throttle potions, familiar specs, retries, and recovery actions.",
				Icon = "TimerOutline",
				CategoryId = "combat",
				Order = 2,
				HasQuery = true,
				Parameters = new []
				{
					new NodeParam { Key = "intervalMs", Label = "Interval (ms)", Type = NodeParamType.Number, IsRequired = true, DefaultValue = "60000" },
					new NodeParam { Key = "signal", Label = "Output signal", Type = NodeParamType.String, DefaultValue = "cooldownReady", Placeholder = "potionReady" },
					new NodeParam { Key = "startReady", Label = "Ready on first check", Type = NodeParamType.Bool, DefaultValue = "true" },
					new NodeParam { Key = "consumeOnReady", Label = "Start cooldown when ready", Type = NodeParamType.Bool, DefaultValue = "true" },
					new NodeParam { Key = "expected", Label = "Expect ready?", Type = NodeParamType.Bool, IsRequired = true, DefaultValue = "true" }
				}
			},
			new NodeDefinition
			{
				Id = "combat.quickHeal",
				Title = "Quick Heal",
				ShortDescription = "Presses the configured Quick Heal action button.",
				Icon = "HeartPlus",
				CategoryId = "combat",
				Order = 3,
				Parameters = Array.Empty<NodeParam>()
			},
			new NodeDefinition
			{
				Id = "combat.quickPrayer",
				Title = "Quick Prayer",
				ShortDescription = "Toggles quick prayer, or only toggles when a requested state differs from current state.",
				Icon = "ShieldCross",
				CategoryId = "combat",
				Order = 4,
				Parameters = new []
				{
					new NodeParam { Key = "mode", Label = "Mode", Type = NodeParamType.Enum, EnumValues = new [] { "Toggle", "Enable", "Disable" }, IsRequired = true, DefaultValue = "Toggle" }
				}
			},
			new NodeDefinition
			{
				Id = "familiar.check",
				Title = "Familiar check",
				ShortDescription = "Checks active familiar state, optional name, remaining time, spell points, and health.",
				Icon = "AccountHeart",
				CategoryId = "familiar",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "expected", Label = "Expect familiar?", Type = NodeParamType.Bool, IsRequired = true },
					new NodeParam { Key = "name", Label = "Name contains", Type = NodeParamType.String, Placeholder = "Optional familiar name" },
					new NodeParam { Key = "minTimeRemaining", Label = "Min time remaining", Type = NodeParamType.Number },
					new NodeParam { Key = "minSpellPoints", Label = "Min spell points", Type = NodeParamType.Number },
					new NodeParam { Key = "minHealth", Label = "Min health", Type = NodeParamType.Number },
					new NodeParam { Key = "signal", Label = "Output signal", Type = NodeParamType.String, DefaultValue = "hasFamiliar", Placeholder = "hasFamiliar" }
				}
			},
			new NodeDefinition
			{
				Id = "familiar.action",
				Title = "Familiar action",
				ShortDescription = "Uses the familiar action button or casts the familiar special attack.",
				Icon = "AutoFix",
				CategoryId = "familiar",
				Order = 1,
				Parameters = new []
				{
					new NodeParam { Key = "mode", Label = "Mode", Type = NodeParamType.Enum, EnumValues = new [] { "SpecialAttack", "ActionButton" }, IsRequired = true, DefaultValue = "SpecialAttack" },
					new NodeParam { Key = "order", Label = "Action order", Type = NodeParamType.Number, DefaultValue = "1", InlineCompanionKey = null }
				}
			},
			new NodeDefinition
			{
				Id = "familiar.summon",
				Title = "Summon familiar",
				ShortDescription = "Summons from the first matching pouch in inventory, optionally skipping when a familiar is already active.",
				Icon = "AccountPlus",
				CategoryId = "familiar",
				Order = 2,
				Parameters = new []
				{
					new NodeParam { Key = "pouches", Label = "Pouch name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, Placeholder = "Pack yak pouch\nWar tortoise pouch" },
					new NodeParam { Key = "menuIndex", Label = "Menu action index", Type = NodeParamType.Number, IsRequired = true, DefaultValue = "1", Placeholder = "1 = first option" },
					new NodeParam { Key = "offset", Label = "Action route offset", Type = NodeParamType.Number, DefaultValue = "5392", IsAdvanced = true },
					new NodeParam { Key = "onlyIfMissing", Label = "Only if no familiar", Type = NodeParamType.Bool, DefaultValue = "true" }
				}
			},
			new NodeDefinition
			{
				Id = "conditions.inCombat",
				Title = "In combat?",
				ShortDescription = "Check the combat state.",
				Icon = "ShieldCheck",
				CategoryId = "conditions",
				Order = 2,
				Parameters = new []
				{
					new NodeParam { Key = "expected", Label = "Expect in combat?", Type = NodeParamType.Bool, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "conditions.inventoryFull",
				Title = "Inventory full?",
				ShortDescription = "Check whether the inventory is full.",
				Icon = "BagChecked",
				CategoryId = "conditions",
				Order = 3,
				Parameters = new []
				{
					new NodeParam { Key = "expected", Label = "Expect full?", Type = NodeParamType.Bool, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "inventory.note",
				Title = "Note item(s)",
				ShortDescription = "Uses magic notepaper on matching inventory items: One, Some (count), or All.",
				Icon = "NoteEdit",
				CategoryId = "inventory",
				Order = 7,
				Parameters = new []
				{
					new NodeParam { Key = "items", Label = "Item name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, Placeholder = "Dragon bones, Green dragonhide, 536" },
					new NodeParam { Key = "quantity", Label = "Quantity", Type = NodeParamType.Enum, EnumValues = new [] { "One", "Some", "All" }, IsRequired = true, DefaultValue = "One", InlineCompanionKey = "count", InlineCompanionVisibleWhen = "Some" },
					new NodeParam { Key = "count", Label = "Count (for Some)", Type = NodeParamType.Number, Placeholder = "3" }
				}
			},
			new NodeDefinition
			{
				Id = "traversal.teleportLodestone",
				Title = "Teleport (lodestone)",
				ShortDescription = "Teleport via lodestone network.",
				Icon = "MapMarker",
				CategoryId = "traversal",
				Order = 2,
				Parameters = new []
				{
					new NodeParam { Key = "destination", Label = "Destination", Type = NodeParamType.Enum, EnumValues = new [] { "Edgeville", "Lumbridge", "Varrock", "Falador", "Port Sarim", "Draynor", "Burthorpe" }, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "traversal.wait",
				Title = "Wait / pause",
				ShortDescription = "Sleep for a duration (ms).",
				Icon = "TimerSand",
				CategoryId = "traversal",
				Order = 3,
				RequiresGameApi = false,
				Parameters = new []
				{
					new NodeParam { Key = "delayMs", Label = "Delay (ms)", Type = NodeParamType.Number, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "traversal.waitRange",
				Title = "Wait range",
				ShortDescription = "Sleep for a random duration between two millisecond values.",
				Icon = "TimerSand",
				CategoryId = "traversal",
				Order = 4,
				RequiresGameApi = false,
				Parameters = new []
				{
					new NodeParam { Key = "minDelayMs", Label = "Minimum delay (ms)", Type = NodeParamType.Number, IsRequired = true },
					new NodeParam { Key = "maxDelayMs", Label = "Maximum delay (ms)", Type = NodeParamType.Number, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "skills.requireLevel",
				Title = "Require skill level",
				ShortDescription = "Guard based on a minimum skill level.",
				Icon = "ChartLine",
				CategoryId = "skills",
				Order = 0,
				Parameters = new []
				{
					new NodeParam { Key = "skill", Label = "Skill", Type = NodeParamType.Enum, EnumValues = new [] { "Attack", "Strength", "Defence", "Magic", "Ranged", "Prayer", "Mining", "Smithing", "Fishing", "Cooking", "Crafting", "Fletching", "Woodcutting", "Agility", "Slayer", "Herblore", "Runecrafting" }, IsRequired = true },
					new NodeParam { Key = "level", Label = "Min level", Type = NodeParamType.Number, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "trade.accept",
				Title = "Trade accept / confirm",
				ShortDescription = "Accept or confirm an open trade window.",
				Icon = "Handshake",
				CategoryId = "trade",
				Order = 0,
				IsImplemented = false,
				Parameters = new []
				{
					new NodeParam { Key = "stage", Label = "Stage", Type = NodeParamType.Enum, EnumValues = new [] { "First", "Second" }, IsRequired = true }
				}
			},
			new NodeDefinition
			{
				Id = "actions.setSignal",
				Title = "Set signal",
				ShortDescription = "Set or clear a runtime signal key.",
				Icon = "ToggleSwitch",
				CategoryId = "misc",
				Order = 0,
				RequiresGameApi = false,
				Parameters = new []
				{
					new NodeParam { Key = "signal", Label = "Signal key", Type = NodeParamType.String, IsRequired = true, Placeholder = "inventoryFull" },
					new NodeParam { Key = "value", Label = "Value", Type = NodeParamType.Bool, IsRequired = true }
				}
			}
		};

		_categories = new ReadOnlyCollection<NodeCategory>(categories.OrderBy(c => c.Order).ToList());
		_definitions = new ReadOnlyCollection<NodeDefinition>(definitions.OrderBy(d => d.Order).ToList());
		_definitionById = _definitions.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
	}

		public IReadOnlyList<NodeCategory> Categories => _categories;
		public IReadOnlyList<NodeDefinition> Definitions => _definitions;

		private static IReadOnlyList<string> OffsetChoices(params ActionOffsets.OffsetCategory[] categories)
		{
			return ActionOffsets.ForCategories(categories)
				.Select(offset => OffsetChoice(offset.Key, offset.Value))
				.ToArray();
		}

		private static string OffsetChoice(string key, int value) => $"{key} = {value}";

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
}
