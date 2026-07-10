using System;
using System.IO;
using MESharp.Services;
using Xunit;

namespace MESharp.Tooling.Tests
{
    // Surface tests for the tool-loading race fixes: the lock-free loaded-id snapshot and the
    // gate-safe load-or-reload entry points. Full load/reload behavior needs a real script
    // assembly and is covered by the in-game smoke pass; nothing here loads a script.
    public class ScriptLoaderLifecycleTests
    {
        [Fact]
        public void IsScriptLoaded_UnknownId_IsFalse()
        {
            Assert.False(ScriptLoader.IsScriptLoaded("no-such-tool"));
        }

        [Fact]
        public void GetLoadedScriptIds_DoesNotContainUnknownId()
        {
            Assert.DoesNotContain("no-such-tool", ScriptLoader.GetLoadedScriptIds());
        }

        [Fact]
        public void ReloadScriptIfLoaded_NotLoaded_ReturnsFalse_WithoutTouchingPath()
        {
            // Path intentionally does not exist: not-loaded must short-circuit before any file check.
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
            Assert.False(ScriptLoader.ReloadScriptIfLoaded("no-such-tool", missing));
        }

        [Fact]
        public void LoadOrReloadScript_MissingFile_Throws_AndDoesNotRegisterId()
        {
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
            Assert.Throws<FileNotFoundException>(() => ScriptLoader.LoadOrReloadScript("lifecycle-test", missing));
            Assert.False(ScriptLoader.IsScriptLoaded("lifecycle-test"));
        }
    }
}
