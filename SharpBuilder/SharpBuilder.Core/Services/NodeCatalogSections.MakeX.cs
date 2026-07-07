using System;
using System.Collections.Generic;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

internal static partial class NodeCatalogSections
{
	private static IEnumerable<NodeDefinition> CreateMakeXDefinitions()
	{
		yield return new NodeDefinition
		{
			Id = "makex.makeItem",
			Title = "Make item (Make-X)",
			ShortDescription = "Open-check, optional category, optional grid-slot select, then craft the current selection at the menu's preset amount. The product grid exposes no item ids, so select by slot index or leave blank if already selected.",
			Icon = "Hammer",
			CategoryId = "makex",
			Order = 0,
			Parameters = new[]
			{
				new NodeParam { Key = "slot", Label = "Grid slot index (optional)", Type = NodeParamType.Number, Placeholder = "Leave blank if the product is already selected" },
				new NodeParam { Key = "category", Label = "Category (optional)", Type = NodeParamType.String, Placeholder = "Green Dragonhide" },
				new NodeParam { Key = "waitComplete", Label = "Wait for craft to finish?", Type = NodeParamType.Bool, DefaultValue = "true" }
			}
		};
		yield return new NodeDefinition
		{
			Id = "makex.isOpen",
			Title = "Make-X open?",
			ShortDescription = "True when the Make-X selection menu is on screen. Publishes the 'makex.isOpen' signal.",
			Icon = "HammerScrewdriver",
			CategoryId = "makex",
			Order = 1,
			HasQuery = true,
			Parameters = new[]
			{
				new NodeParam { Key = "expected", Label = "Expect open?", Type = NodeParamType.Bool, IsRequired = true, DefaultValue = "true" }
			}
		};
		yield return new NodeDefinition
		{
			Id = "makex.selectItem",
			Title = "Select item (Make-X)",
			ShortDescription = "Selects a product in the Make-X grid by name or id.",
			Icon = "CursorDefaultClick",
			CategoryId = "makex",
			Order = 2,
			Parameters = new[]
			{
				new NodeParam { Key = "item", Label = "Item name / id", Type = NodeParamType.String, IsRequired = true, Placeholder = "Green dragonhide chaps / 1099" }
			}
		};
		yield return new NodeDefinition
		{
			Id = "makex.selectCategory",
			Title = "Select category (Make-X)",
			ShortDescription = "Opens the category dropdown and selects an option by name.",
			Icon = "FormatListBulleted",
			CategoryId = "makex",
			Order = 3,
			Parameters = new[]
			{
				new NodeParam { Key = "category", Label = "Category name", Type = NodeParamType.String, IsRequired = true, Placeholder = "Green Dragonhide" }
			}
		};
		yield return new NodeDefinition
		{
			Id = "makex.setAmount",
			Title = "Set amount (Make-X)",
			ShortDescription = "Sets the make quantity (types the value via the custom amount entry).",
			Icon = "Numeric",
			CategoryId = "makex",
			Order = 4,
			Parameters = new[]
			{
				new NodeParam { Key = "amount", Label = "Amount", Type = NodeParamType.Number, IsRequired = true, Placeholder = "28" }
			}
		};
		yield return new NodeDefinition
		{
			Id = "makex.craft",
			Title = "Craft (Make-X)",
			ShortDescription = "Clicks the Craft/Make button, optionally setting the amount first, and optionally waits for completion.",
			Icon = "Anvil",
			CategoryId = "makex",
			Order = 5,
			Parameters = new[]
			{
				new NodeParam { Key = "amount", Label = "Amount (optional)", Type = NodeParamType.Number, Placeholder = "Leave blank to keep current" },
				new NodeParam { Key = "waitComplete", Label = "Wait for craft to finish?", Type = NodeParamType.Bool, DefaultValue = "true" }
			}
		};
		yield return new NodeDefinition
		{
			Id = "makex.waitComplete",
			Title = "Wait for craft (Make-X)",
			ShortDescription = "Blocks until the Make-X progress window closes (the action finished).",
			Icon = "TimerSand",
			CategoryId = "makex",
			Order = 6,
			Parameters = new[]
			{
				new NodeParam { Key = "timeoutMs", Label = "Timeout (ms)", Type = NodeParamType.Number, DefaultValue = "60000" }
			}
		};
	}
}
