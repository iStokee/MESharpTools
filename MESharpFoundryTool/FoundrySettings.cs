using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MESharp;

/// <summary>A target class the recorder projects into every frame's truth record.</summary>
internal sealed class FoundryTargetClass
{
    public string ClassName { get; set; } = "";
    /// <summary>"npc" or "object".</summary>
    public string Kind { get; set; } = "npc";
    public int Id { get; set; }
}

/// <summary>Persisted tool configuration: what to record, not how (schema stays in the recorder).</summary>
internal sealed class FoundrySettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atom", "Foundry", "foundry_tool.json");
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public string Activity { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<FoundryTargetClass> TargetClasses { get; set; } = new();
    public AtomPipelineSettings AtomPipeline { get; set; } = new();
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }

    public static FoundrySettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<FoundrySettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { }
    }
}

/// <summary>User-local orchestration settings. Training remains in an external WSL process.</summary>
internal sealed class AtomPipelineSettings
{
    public string RepositoryPath { get; set; } = DefaultRepositoryPath();
    public string PythonExecutable { get; set; } = "~/.venvs/atom/bin/python";
    public string Distribution { get; set; } = Environment.GetEnvironmentVariable("ATOM_WSL_DISTRIBUTION") ?? "Ubuntu";
    public int Epochs { get; set; } = 30;
    public int ClickOffsetY { get; set; } = -70;
    public string OccludingInterfaceRoots { get; set; } = "517,1251,1370,1371";

    private static string DefaultRepositoryPath()
    {
        var configured = Environment.GetEnvironmentVariable("ATOM_REPOSITORY");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        const string developmentDefault = @"C:\Development\MemoryError\Atom";
        return Directory.Exists(developmentDefault) ? developmentDefault : "";
    }
}
