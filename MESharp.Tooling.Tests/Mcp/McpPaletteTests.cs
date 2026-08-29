using System.Windows.Media;
using csharp_interop.McpTools;
using csharp_interop.Tests;
using Xunit;

namespace MESharp.Tooling.Tests.Mcp;

public sealed class McpPaletteTests
{
    [Fact]
    public void Palette_round_trips_dark_light_dark_without_freezing_shared_brushes()
    {
        StaTestRunner.Run(() =>
        {
            McpPalette.Apply(isDark: true);
            var dark = McpPalette.BgWindowBrush.Color;

            McpPalette.Apply(isDark: false);
            var light = McpPalette.BgWindowBrush.Color;

            McpPalette.Apply(isDark: true);
            var darkAgain = McpPalette.BgWindowBrush.Color;

            Assert.NotEqual(dark, light);
            Assert.Equal(dark, darkAgain);
            Assert.True(IsDark(dark));
            Assert.False(IsDark(light));
        });
    }

    private static bool IsDark(Color color) => color.R + color.G + color.B < 384;
}
