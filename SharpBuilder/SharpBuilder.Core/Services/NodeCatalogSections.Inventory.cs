using System.Collections.Generic;
using MESharp.API;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

internal static partial class NodeCatalogSections
{
	private static IEnumerable<NodeDefinition> CreateInventoryDefinitions()
	{
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
		{
		Id = "bank.open",
		Title = "Open bank",
		ShortDescription = "Open nearest bank booth/chest.",
		Icon = "Safe",
		CategoryId = "bank",
		Order = 0,
		Parameters = Array.Empty<NodeParam>()
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
		{
			Id = "bank.loadPreset",
			Title = "Load bank preset",
			ShortDescription = "Load a bank preset after the bank is open, either by pressing a configured preset keybind or by native preset slot.",
			Icon = "PackageVariantClosed",
			CategoryId = "bank",
			Order = 3,
			Parameters = new []
			{
				new NodeParam { Key = "method", Label = "Method", Type = NodeParamType.Enum, EnumValues = new [] { "Keybind", "PresetSlot", "LastPreset" }, IsRequired = true, DefaultValue = "Keybind" },
				new NodeParam { Key = "keybind", Label = "Preset keybind", Type = NodeParamType.String, DefaultValue = "1", Placeholder = "1 / F1 / NumPad1" },
				new NodeParam { Key = "preset", Label = "Preset number", Type = NodeParamType.Number, DefaultValue = "1", Placeholder = "1-18 for PresetSlot" },
				new NodeParam { Key = "loadLast", Label = "Load last preset", Type = NodeParamType.Bool },
				new NodeParam { Key = "waitMs", Label = "Wait after load (ms)", Type = NodeParamType.Number, DefaultValue = "1200" }
			}
		};
		yield return new NodeDefinition
		{
		Id = "bank.close",
		Title = "Close bank",
		ShortDescription = "Close the bank interface.",
		Icon = "CloseCircle",
		CategoryId = "bank",
		Order = 4,
		Parameters = Array.Empty<NodeParam>()
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
		{
			Id = "inventory.alchAll",
			Title = "High alch item(s)",
			ShortDescription = "Tap a configured High Alchemy/action-bar keybind and wait through the player animation until matching inventory items are gone.",
			Icon = "AutoFix",
			CategoryId = "inventory",
			Order = 8,
			Parameters = new []
			{
				new NodeParam { Key = "items", Label = "Item name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, DefaultValue = "Green dragonhide shield, 25794", Placeholder = "Green dragonhide shield, 25794" },
				new NodeParam { Key = "keybind", Label = "High alch keybind", Type = NodeParamType.String, IsRequired = true, DefaultValue = "F5", Placeholder = "F5 / 5 / A" },
				new NodeParam { Key = "targetMode", Label = "Target mode", Type = NodeParamType.Enum, EnumValues = new [] { "KeybindThenItem", "KeybindOnly" }, IsRequired = true, DefaultValue = "KeybindThenItem" },
				new NodeParam { Key = "quantity", Label = "Quantity", Type = NodeParamType.Enum, EnumValues = new [] { "One", "Some", "All" }, IsRequired = true, DefaultValue = "All", InlineCompanionKey = "count", InlineCompanionVisibleWhen = "Some" },
				new NodeParam { Key = "count", Label = "Count (for Some)", Type = NodeParamType.Number, Placeholder = "13" },
				new NodeParam { Key = "requireAlchable", Label = "Require alchable metadata", Type = NodeParamType.Bool, DefaultValue = "true" },
				new NodeParam { Key = "targetDelayMs", Label = "Delay before item target (ms)", Type = NodeParamType.Number, DefaultValue = "1000", IsAdvanced = true },
				new NodeParam { Key = "recastMode", Label = "Recast trigger", Type = NodeParamType.Enum, EnumValues = new [] { "ItemDisappears", "FixedDelay" }, DefaultValue = "ItemDisappears", IsAdvanced = true },
				new NodeParam { Key = "disappearTimeoutMs", Label = "Item disappear timeout (ms)", Type = NodeParamType.Number, DefaultValue = "3500", IsAdvanced = true },
				new NodeParam { Key = "postTargetDelayMs", Label = "Delay after item target (ms)", Type = NodeParamType.Number, DefaultValue = "2500", IsAdvanced = true },
				new NodeParam { Key = "startTimeoutMs", Label = "Animation start timeout (ms)", Type = NodeParamType.Number, DefaultValue = "1500", IsAdvanced = true },
				new NodeParam { Key = "finishTimeoutMs", Label = "Animation finish timeout (ms)", Type = NodeParamType.Number, DefaultValue = "5000", IsAdvanced = true },
				new NodeParam { Key = "betweenCastsMs", Label = "Delay between casts (ms)", Type = NodeParamType.Number, DefaultValue = "250", IsAdvanced = true },
				new NodeParam { Key = "inventoryRoot", Label = "Inventory interface root", Type = NodeParamType.Number, DefaultValue = "0", IsAdvanced = true },
				new NodeParam { Key = "itemAction", Label = "Alch item action", Type = NodeParamType.Number, DefaultValue = "110", IsAdvanced = true },
				new NodeParam { Key = "itemOffset", Label = "Alch item route", Type = NodeParamType.Enum, EnumValues = OffsetChoices(ActionOffsets.OffsetCategory.Interface), DefaultValue = OffsetChoice("GeneralInterface_route1", Objects.Offsets.GeneralInterfaceRoute1), Placeholder = "OFF_ACT route used for the alch target click", IsAdvanced = true }
			}
		};
	}
}
