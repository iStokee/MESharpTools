using System;
using System.IO;
using System.Text.Json;

namespace MESharp.Services
{
    /// <summary>
    /// Tiny persistence for the diagnostics panel — just the rs3cache dump directory used by the
    /// in-process regenerate buttons. Stored at %LocalAppData%/MESharp/nav_diagnostics.json so it
    /// survives hot reloads and tool restarts. Failures degrade to the default path.
    /// </summary>
    public static class NavDiagnosticsStore
    {
        private static readonly string DefaultDumpDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MESharp", "rs3dump");

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MESharp", "nav_diagnostics.json");

        private sealed class Settings
        {
            public string? DumpDirectory { get; set; }
            public bool TreatWildernessDitchAsCrossable { get; set; } = true;
            // External cache-dump tool (e.g. mejrs/rs3cache). Not shipped with MESharp; the user
            // configures their own invocation. {output} in the args is replaced with DumpDirectory.
            public string? DumpCommand { get; set; }
            public string? DumpArguments { get; set; }
            public string? CollisionAuditDatabase { get; set; }
        }

        public sealed record NavDiagnosticsSettings(
            string DumpDirectory, bool TreatWildernessDitchAsCrossable,
            string DumpCommand, string DumpArguments, string CollisionAuditDatabase);

        public static NavDiagnosticsSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                    if (s != null)
                    {
                        var dir = string.IsNullOrWhiteSpace(s.DumpDirectory) ? DefaultDumpDirectory : s.DumpDirectory!;
                        return new NavDiagnosticsSettings(dir, s.TreatWildernessDitchAsCrossable,
                            s.DumpCommand ?? string.Empty, s.DumpArguments ?? string.Empty,
                            s.CollisionAuditDatabase ?? string.Empty);
                    }
                }
            }
            catch { /* fall through to default */ }
            return new NavDiagnosticsSettings(DefaultDumpDirectory, true, string.Empty, string.Empty, string.Empty);
        }

        public static string LoadDumpDirectory() => Load().DumpDirectory;

        public static void Save(NavDiagnosticsSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var json = JsonSerializer.Serialize(
                    new Settings
                    {
                        DumpDirectory = settings.DumpDirectory,
                        TreatWildernessDitchAsCrossable = settings.TreatWildernessDitchAsCrossable,
                        DumpCommand = settings.DumpCommand,
                        DumpArguments = settings.DumpArguments
                        ,CollisionAuditDatabase = settings.CollisionAuditDatabase
                    },
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { /* best-effort */ }
        }

        public static void SaveDumpDirectory(string dumpDirectory)
        {
            var current = Load();
            Save(current with { DumpDirectory = dumpDirectory });
        }
    }
}
