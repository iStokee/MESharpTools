using System;
using System.IO;
using System.Linq;
using Xunit;

namespace MESharpNavTool.Tests;

public sealed class MapEmbeddedAssetsTests
{
    [Fact]
    public void HostedMapContainsItsRequiredLeafletAssets()
    {
        var assembly = typeof(MESharp.ScriptEntry).Assembly;
        var names = assembly.GetManifestResourceNames();
        foreach (var name in new[]
        {
            "MESharp.webwalk_map.index.html",
            "MESharp.webwalk_map.leaflet.js",
            "MESharp.webwalk_map.leaflet.css",
            "MESharp.webwalk_map.leaflet.LICENSE",
            "MESharp.webwalk_map.leaflet.images.marker-icon.png",
            "MESharp.webwalk_map.leaflet.images.marker-shadow.png"
        })
        {
            Assert.Contains(name, names);
            using var asset = assembly.GetManifestResourceStream(name);
            Assert.NotNull(asset);
            Assert.True(asset.Length > 0, $"Embedded asset is empty: {name}");
        }

        using var pageStream = assembly.GetManifestResourceStream("MESharp.webwalk_map.index.html")!;
        using var reader = new StreamReader(pageStream);
        var page = reader.ReadToEnd();
        Assert.Contains("href=\"/leaflet.css\"", page, StringComparison.Ordinal);
        Assert.Contains("src=\"/leaflet.js\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("unpkg.com/leaflet", page, StringComparison.OrdinalIgnoreCase);
    }
}
