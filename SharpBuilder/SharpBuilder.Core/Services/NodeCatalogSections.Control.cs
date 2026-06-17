using System;
using System.Collections.Generic;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

internal static partial class NodeCatalogSections
{
	private static IEnumerable<NodeDefinition> CreateControlDefinitions()
	{
		yield return new NodeDefinition
		{
			Id = NodeCatalogDefaults.StartId,
			Title = "Start",
			ShortDescription = "Entry point to mark as the graph start.",
			Icon = "Flag",
			CategoryId = "control",
			Order = 0,
			RequiresGameApi = false,
			Parameters = Array.Empty<NodeParam>()
		};
		yield return new NodeDefinition
		{
			Id = NodeCatalogDefaults.TerminalId,
			Title = "End / Terminal",
			ShortDescription = "Terminal node that stops the runner.",
			Icon = "Stop",
			CategoryId = "control",
			Order = 1,
			RequiresGameApi = false,
			Parameters = Array.Empty<NodeParam>()
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
		yield return new NodeDefinition
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
		};
	}
}
