using SharpBuilder.Core.Services;
using SharpBuilder.Editor.Wpf.Services;
using Xunit;

namespace SharpBuilder.Editor.Wpf.Tests;

public class CaptureCalibrationServiceTests
{
	[Theory]
	[InlineData("npcs.interact", true)]
	[InlineData("objects.interact", true)]
	[InlineData("loot.pickup", true)]
	[InlineData("inventory.drop", false)]
	public void CanCapture_MatchesInteractionAndLootNodes(string definitionId, bool expected)
	{
		var catalog = new NodeCatalogService();
		var service = new CaptureCalibrationService();

		Assert.Equal(expected, service.CanCapture(catalog.GetDefinition(definitionId)));
	}

	[Fact]
	public void Capture_RejectsMissingSelection()
	{
		var service = new CaptureCalibrationService();

		var result = service.Capture(null, null);

		Assert.False(result.Changed);
		Assert.Equal("none", result.DriftState);
		Assert.Contains("Select an Object/NPC interaction node", result.Status);
	}
}
