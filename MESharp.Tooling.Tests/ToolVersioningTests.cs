using System;
using MESharp.Services.Tools;
using Xunit;

namespace MESharp.Tooling.Tests
{
    public class ToolVersioningTests
    {
        // ── Normalize: the 3-part-tag vs 4-part-assembly mismatch this guards against ──────────
        [Fact]
        public void Normalize_TreatsThreeAndFourPart_AsEqual()
        {
            var tag = ToolVersioning.Normalize(new Version("1.2.0"));      // from a release tag
            var assembly = ToolVersioning.Normalize(new Version("1.2.0.0")); // from AssemblyVersion
            Assert.Equal(tag, assembly);
        }

        [Fact]
        public void HasUpdate_IsFalse_WhenInstalledMatchesLatest_AcrossPartCounts()
        {
            // Installed 1.2.0.0 must NOT be considered newer than release tag 1.2.0.
            Assert.False(ToolVersioning.HasUpdate(new Version("1.2.0.0"), new Version("1.2.0")));
        }

        [Theory]
        [InlineData("1.2.0", "1.3.0", true)]
        [InlineData("1.2.0", "1.2.1", true)]
        [InlineData("1.2.0", "1.2.0", false)]
        [InlineData("2.0.0", "1.9.9", false)]
        public void HasUpdate_ComparesNumerically(string installed, string latest, bool expected)
        {
            Assert.Equal(expected, ToolVersioning.HasUpdate(new Version(installed), new Version(latest)));
        }

        [Fact]
        public void HasUpdate_IsFalse_WhenEitherSideNull()
        {
            Assert.False(ToolVersioning.HasUpdate(null, new Version("1.0.0")));
            Assert.False(ToolVersioning.HasUpdate(new Version("1.0.0"), null));
        }

        // ── Tag parsing: per-tool prefixes drive independent versioning ────────────────────────
        [Theory]
        [InlineData("navtool-v1.2.0", "navtool-", "1.2.0")]
        [InlineData("navtool-1.2.0", "navtool-", "1.2.0")]
        [InlineData("sharpbuilder-v2.10.3", "sharpbuilder-", "2.10.3")]
        public void TryParseTag_StripsPrefixAndV(string tag, string prefix, string expected)
        {
            Assert.True(ToolVersioning.TryParseTag(tag, prefix, out var version));
            Assert.Equal(ToolVersioning.Normalize(new Version(expected)), ToolVersioning.Normalize(version));
        }

        [Theory]
        [InlineData("navtool-v1.2.0", "sharpbuilder-")] // wrong prefix → ignored
        [InlineData("v1.2.0", "navtool-")]              // missing prefix
        [InlineData("navtool-vX.Y", "navtool-")]        // unparseable
        public void TryParseTag_RejectsNonMatching(string tag, string prefix)
        {
            Assert.False(ToolVersioning.TryParseTag(tag, prefix, out _));
        }

        // ── Classify: the status-dot decision matrix ───────────────────────────────────────────
        private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(14);
        private static readonly DateTime Now = new(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Classify_Broken_WhenLoadError()
        {
            var kind = ToolVersioning.Classify(new Version("1.0.0"), new Version("1.0.0"), hasLoadError: true, null, Now, StaleAfter);
            Assert.Equal(ToolStatusKind.Broken, kind);
        }

        [Fact]
        public void Classify_UpdateAvailable_WhenNotInstalled()
        {
            var kind = ToolVersioning.Classify(installed: null, latest: new Version("1.0.0"), hasLoadError: false, null, Now, StaleAfter);
            Assert.Equal(ToolStatusKind.UpdateAvailable, kind);
        }

        [Fact]
        public void Classify_Unknown_WhenInstalledButLatestUnknown()
        {
            var kind = ToolVersioning.Classify(new Version("1.0.0"), latest: null, hasLoadError: false, null, Now, StaleAfter);
            Assert.Equal(ToolStatusKind.Unknown, kind);
        }

        [Fact]
        public void Classify_Current_WhenUpToDate()
        {
            var kind = ToolVersioning.Classify(new Version("1.2.0.0"), new Version("1.2.0"), hasLoadError: false, null, Now, StaleAfter);
            Assert.Equal(ToolStatusKind.Current, kind);
        }

        [Fact]
        public void Classify_UpdateAvailable_WhenMinorBehind()
        {
            var firstSeen = Now.AddDays(-1);
            var kind = ToolVersioning.Classify(new Version("1.2.0"), new Version("1.3.0"), hasLoadError: false, firstSeen, Now, StaleAfter);
            Assert.Equal(ToolStatusKind.UpdateAvailable, kind);
        }

        [Fact]
        public void Classify_Stale_WhenMajorBehind()
        {
            var kind = ToolVersioning.Classify(new Version("1.9.0"), new Version("2.0.0"), hasLoadError: false, Now.AddDays(-1), Now, StaleAfter);
            Assert.Equal(ToolStatusKind.Stale, kind);
        }

        [Fact]
        public void Classify_Stale_WhenUpdatePendingTooLong()
        {
            var firstSeen = Now.AddDays(-30); // pending well past the 14-day threshold
            var kind = ToolVersioning.Classify(new Version("1.2.0"), new Version("1.2.1"), hasLoadError: false, firstSeen, Now, StaleAfter);
            Assert.Equal(ToolStatusKind.Stale, kind);
        }
    }
}
