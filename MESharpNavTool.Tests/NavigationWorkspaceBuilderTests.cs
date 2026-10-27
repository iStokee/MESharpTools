using System.Text.Json;
using System.IO;
using MESharp.CacheDerivation;
using MESharp.Data;
using Xunit;

namespace MESharpNavTool.Tests;

public sealed class NavigationWorkspaceBuilderTests
{
    [Fact]
    public void FailedRebuildRetainsPreviousCompleteGeneration()
    {
        var root = Path.Combine(Path.GetTempPath(), "mesharp-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "source");
            var derived = Path.Combine(root, "derived");
            Directory.CreateDirectory(Path.Combine(source, "tiles"));
            Directory.CreateDirectory(Path.Combine(source, "locations"));
            File.WriteAllText(Path.Combine(source, "location_configs.json"), "[]");
            File.WriteAllText(Path.Combine(source, "locations", "10_10.json"), "[]");
            File.WriteAllText(Path.Combine(source, "tiles", "10_10.json"),
                JsonSerializer.Serialize(new { data = Enumerable.Repeat(new { settings = 0 }, 4 * 4096) }));

            var build = NavigationWorkspaceBuilder.BuildFromDump(source, derivedRoot: derived);
            Assert.Equal(1, build.Squares);
            Assert.True(File.Exists(Path.Combine(build.Directory, "collision", "10_10.json")));
            var pointer = Path.Combine(derived, "active.json");
            var before = File.ReadAllText(pointer);

            File.WriteAllText(Path.Combine(source, "tiles", "10_10.json"), "{}");
            Assert.Throws<InvalidDataException>(() => NavigationWorkspaceBuilder.BuildFromDump(source, derivedRoot: derived));
            Assert.Equal(before, File.ReadAllText(pointer));
            Assert.True(File.Exists(Path.Combine(build.Directory, "build.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AtomicReplacementKeepsBackupAndLeavesNoTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "mesharp-atomic-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, "routes.json");
            AtomicFile.WriteAllText(path, "old");
            AtomicFile.WriteAllText(path, "new", keepBackup: true);
            Assert.Equal("new", File.ReadAllText(path));
            Assert.Equal("old", File.ReadAllText(path + ".bak"));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
