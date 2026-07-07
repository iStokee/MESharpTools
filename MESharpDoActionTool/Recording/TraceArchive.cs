using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MESharp.API;

namespace MESharp.Recording
{
    /// <summary>One saved trace session on disk, with its on-disk footprint (events folder + screenshots).</summary>
    public sealed class TraceSessionInfo
    {
        public string SessionId { get; init; } = string.Empty;
        public string Dir { get; init; } = string.Empty;
        public string? CapturesDir { get; init; }
        public DateTime ModifiedUtc { get; init; }
        public long SizeBytes { get; init; }
        public int Samples { get; init; }
        public int Clicks { get; init; }
        public int Screenshots { get; init; }
    }

    /// <summary>Lists and deletes saved trace sessions (the recorder's output store). Deletion removes both the
    /// trace folder and its paired screenshots folder.</summary>
    public static class TraceArchive
    {
        public static IReadOnlyList<TraceSessionInfo> List()
        {
            var root = TraceRecorder.TracesRoot();
            if (!Directory.Exists(root))
            {
                return Array.Empty<TraceSessionInfo>();
            }

            var result = new List<TraceSessionInfo>();
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                try
                {
                    var sessionId = Path.GetFileName(dir);
                    var capturesDir = SafeCapturesDir(sessionId);
                    var size = DirSize(dir) + (capturesDir != null ? DirSize(capturesDir) : 0);
                    var (samples, clicks, shots) = ReadSummary(dir);
                    result.Add(new TraceSessionInfo
                    {
                        SessionId = sessionId,
                        Dir = dir,
                        CapturesDir = capturesDir,
                        ModifiedUtc = Directory.GetLastWriteTimeUtc(dir),
                        SizeBytes = size,
                        Samples = samples,
                        Clicks = clicks,
                        Screenshots = shots,
                    });
                }
                catch { /* skip unreadable session */ }
            }

            return result.OrderByDescending(s => s.ModifiedUtc).ToList();
        }

        /// <summary>Delete one session's trace folder + screenshots. Returns true if anything was removed.</summary>
        public static bool Delete(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            var removed = false;
            var traceDir = Path.Combine(TraceRecorder.TracesRoot(), sessionId);
            removed |= SafeDeleteDir(traceDir, TraceRecorder.TracesRoot());

            var capturesDir = SafeCapturesDir(sessionId);
            if (capturesDir != null)
            {
                removed |= SafeDeleteDir(capturesDir, RootOf(capturesDir));
            }

            return removed;
        }

        /// <summary>Delete every saved session. Returns how many were removed.</summary>
        public static int DeleteAll()
        {
            var n = 0;
            foreach (var s in List())
            {
                if (Delete(s.SessionId)) n++;
            }
            return n;
        }

        private static string? SafeCapturesDir(string sessionId)
        {
            try { return ScreenshotService.CaptureDir(sessionId); } catch { return null; }
        }

        private static string RootOf(string dir) => Directory.GetParent(dir)?.FullName ?? dir;

        // Only delete a directory that actually lives under the expected MESharp root — never follow a path
        // that escapes it.
        private static bool SafeDeleteDir(string dir, string expectedRoot)
        {
            try
            {
                if (!Directory.Exists(dir)) return false;
                var full = Path.GetFullPath(dir);
                var rootFull = Path.GetFullPath(expectedRoot);
                if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                Directory.Delete(dir, recursive: true);
                return true;
            }
            catch { return false; }
        }

        private static long DirSize(string dir)
        {
            try
            {
                return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
            }
            catch { return 0; }
        }

        private static (int samples, int clicks, int shots) ReadSummary(string dir)
        {
            try
            {
                var path = Path.Combine(dir, "summary.json");
                if (!File.Exists(path)) return (0, 0, 0);
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                var r = doc.RootElement;
                int G(string k) => r.TryGetProperty(k, out var v) && v.TryGetInt32(out var i) ? i : 0;
                return (G("samples"), G("clicks"), G("screenshots"));
            }
            catch { return (0, 0, 0); }
        }
    }
}
