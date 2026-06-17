using System.Collections.Generic;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

internal static partial class NodeCatalogSections
{
	private static IEnumerable<NodeDefinition> CreateMiscDefinitions()
	{
		yield return new NodeDefinition
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
		};
	}
}
