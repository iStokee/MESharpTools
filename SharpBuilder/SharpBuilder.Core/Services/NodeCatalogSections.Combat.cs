using System.Collections.Generic;
using MESharp.API;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

internal static partial class NodeCatalogSections
{
	private static IEnumerable<NodeDefinition> CreateCombatDefinitions()
	{
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
		{
		Id = "combat.quickHeal",
		Title = "Quick Heal",
		ShortDescription = "Presses the configured Quick Heal action button.",
		Icon = "HeartPlus",
		CategoryId = "combat",
		Order = 3,
		Parameters = Array.Empty<NodeParam>()
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
	}
}
