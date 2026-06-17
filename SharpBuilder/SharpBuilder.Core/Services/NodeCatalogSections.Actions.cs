using System.Collections.Generic;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

internal static partial class NodeCatalogSections
{
	private static IEnumerable<NodeDefinition> CreateActionDefinitions()
	{
		yield return new NodeDefinition
		{
		Id = "actions.interaction",
		Title = "Advanced interaction",
		ShortDescription = "Low-level NPC/object interaction fallback. Prefer typed NPC/Object nodes unless you need native capture tuning.",
		Icon = "CursorDefaultClick",
		CategoryId = "actions",
		Order = 1,
		HasQuery = true,
		Maturity = NodeMaturity.Advanced,
		Parameters = new []
		{
		new NodeParam { Key = "target", Label = "Target name(s) / id(s)", Type = NodeParamType.List, AllowMultiple = true, IsRequired = true, HasQuery = true, AllowPartial = true, Placeholder = "Bank booth, Mud rune altar, 12345" },
		new NodeParam { Key = "option", Label = "Interaction option", Type = NodeParamType.String, Placeholder = "Interact / Talk-to / Trade / Use / 2 (case-insensitive)" },
		new NodeParam { Key = "allowPartial", Label = "Allow partial match", Type = NodeParamType.Bool, AllowPartial = true }
		}
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
	}
}
