using System.Collections.Generic;
using MESharp.API;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

internal static partial class NodeCatalogSections
{
	private static IEnumerable<NodeDefinition> CreateTraversalDefinitions()
	{
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
	}
}
