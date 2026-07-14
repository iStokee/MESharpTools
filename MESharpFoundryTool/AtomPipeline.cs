using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MESharp;

internal sealed record AtomPipelineCommand(string Name, string Script);
internal sealed record AtomPipelineResult(string Name, int ExitCode, TimeSpan Elapsed)
{
    public bool Succeeded => ExitCode == 0;
}

internal static class AtomPipelineCommands
{
    public static AtomPipelineCommand CheckEnvironment(AtomPipelineSettings settings) =>
        Command(settings, "Check Atom environment", ["-c", "import torch, onnxruntime; print('Torch', torch.__version__); print('CUDA', torch.cuda.is_available())"]);

    public static AtomPipelineCommand ValidateSession(AtomPipelineSettings settings, string sessionPath) =>
        AtomLab(settings, "Validate session", "validate", WslPath(sessionPath));

    public static AtomPipelineCommand ValidateTask(AtomPipelineSettings settings, string taskPath) =>
        AtomLab(settings, "Validate task", "validate-task", taskPath);

    public static AtomPipelineCommand ExportTaskDataset(
        AtomPipelineSettings settings, string taskPath, string positiveSession, string validationNegativeSession,
        string datasetRelativePath, string? testNegativeSession = null)
    {
        var arguments = new List<string>
        {
            "export-task-dataset", taskPath, WslPath(positiveSession),
            "--negative-session", WslPath(validationNegativeSession),
        };
        if (!string.IsNullOrWhiteSpace(testNegativeSession))
        {
            arguments.Add("--test-negative-session");
            arguments.Add(WslPath(testNegativeSession));
        }
        arguments.Add("--output");
        arguments.Add(datasetRelativePath);
        return AtomLab(settings, "Export task dataset", arguments.ToArray());
    }

    public static AtomPipelineCommand ReviewTaskDataset(AtomPipelineSettings settings, string taskPath, string dataset, string review) =>
        AtomLab(settings, "Create task review", "review-task-dataset", taskPath, dataset, "--output", review);

    public static AtomPipelineCommand TrainTask(AtomPipelineSettings settings, string taskPath, string dataset, string component) =>
        AtomLab(settings, "Train task", "train-task", taskPath, dataset, "--output", component, "--progress-json");

    public static AtomPipelineCommand EvaluateTask(
        AtomPipelineSettings settings, string taskPath, string dataset, string component, string split, string? metricsOutput = null)
    {
        var arguments = new List<string> { "evaluate-task", taskPath, dataset, component, "--split", split };
        if (!string.IsNullOrWhiteSpace(metricsOutput))
        {
            arguments.Add("--metrics-output");
            arguments.Add(metricsOutput);
        }
        return AtomLab(settings, $"Evaluate task {split}", arguments.ToArray());
    }

    public static AtomPipelineCommand ExportDataset(
        AtomPipelineSettings settings, string positiveSession, string validationNegativeSession,
        string className, string datasetRelativePath, string? testNegativeSession = null)
    {
        var arguments = new List<string>
        {
            "export-projected-dataset", WslPath(positiveSession), "--class-name", className,
            "--negative-session", WslPath(validationNegativeSession), "--click-offset-y", settings.ClickOffsetY.ToString(),
        };
        foreach (var root in ParseInterfaceRoots(settings.OccludingInterfaceRoots))
        {
            arguments.Add("--occluding-interface-root");
            arguments.Add(root.ToString());
        }
        if (!string.IsNullOrWhiteSpace(testNegativeSession))
        {
            arguments.Add("--test-negative-session");
            arguments.Add(WslPath(testNegativeSession));
        }
        arguments.Add("--output");
        arguments.Add(datasetRelativePath);
        return AtomLab(settings, "Export dataset", arguments.ToArray());
    }

    public static AtomPipelineCommand CreateReview(AtomPipelineSettings settings, string dataset, string review) =>
        AtomLab(settings, "Create development review", "review-dataset", dataset,
            "--split", "train", "--split", "validation", "--output", review);

    public static AtomPipelineCommand ApplyReview(AtomPipelineSettings settings, string dataset, string reviewFile, string output) =>
        AtomLab(settings, "Apply development review", "apply-dataset-review", dataset, reviewFile, "--output", output);

    public static AtomPipelineCommand Train(AtomPipelineSettings settings, string dataset, string component) =>
        AtomLab(settings, "Train detector", "train-detector", dataset, "--output", component,
            "--epochs", settings.Epochs.ToString(), "--progress-json");

    public static AtomPipelineCommand Evaluate(
        AtomPipelineSettings settings, string dataset, string component, string split, string? metricsOutput = null)
    {
        var arguments = new List<string> { "evaluate-detector", dataset, component, "--split", split };
        if (!string.IsNullOrWhiteSpace(metricsOutput))
        {
            arguments.Add("--metrics-output");
            arguments.Add(metricsOutput);
        }
        return AtomLab(settings, $"Evaluate {split}", arguments.ToArray());
    }

    internal static string WindowsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path is required.", nameof(path));
        var normalized = path.Trim();
        if (normalized.Length >= 7 && normalized.StartsWith("/mnt/", StringComparison.Ordinal) &&
            char.IsAsciiLetter(normalized[5]) && normalized[6] == '/')
            return $"{char.ToUpperInvariant(normalized[5])}:\\{normalized[7..].Replace('/', '\\')}";
        if (normalized.StartsWith('/'))
            throw new ArgumentException("The Foundry UI repository must be a Windows path or a WSL /mnt/<drive> path.", nameof(path));
        return Path.GetFullPath(normalized);
    }

    public static IReadOnlyList<int> ParseInterfaceRoots(string value)
    {
        var roots = new List<int>();
        foreach (var part in (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var root) || root <= 0)
                throw new ArgumentException($"Invalid occluding interface root '{part}'.", nameof(value));
            if (!roots.Contains(root)) roots.Add(root);
        }
        return roots;
    }

    internal static string WslPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path is required.", nameof(path));
        var normalized = path.Trim();
        if (normalized.StartsWith('/')) return normalized;
        if (normalized.Length >= 3 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':' && (normalized[2] == '\\' || normalized[2] == '/'))
            return $"/mnt/{char.ToLowerInvariant(normalized[0])}/{normalized[3..].Replace('\\', '/')}";
        throw new ArgumentException($"Path must be an absolute Windows or WSL path: {path}", nameof(path));
    }

    internal static string BashQuote(string value) => $"'{value.Replace("'", "'\"'\"'")}'";

    private static AtomPipelineCommand AtomLab(AtomPipelineSettings settings, string name, params string[] arguments) =>
        Command(settings, name, ["-m", "atom_lab.cli", .. arguments]);

    private static AtomPipelineCommand Command(AtomPipelineSettings settings, string name, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(settings.RepositoryPath)) throw new ArgumentException("Configure the Atom repository path.");
        if (string.IsNullOrWhiteSpace(settings.PythonExecutable)) throw new ArgumentException("Configure the WSL Python executable.");
        var python = settings.PythonExecutable.Trim();
        var pythonToken = python.StartsWith("~/", StringComparison.Ordinal)
            ? $"\"$HOME\"/{BashQuote(python[2..])}"
            : BashQuote(python);
        var script = new StringBuilder("cd ")
            .Append(BashQuote(WslPath(settings.RepositoryPath)))
            .Append(" && ")
            .Append(pythonToken);
        foreach (var argument in arguments) script.Append(' ').Append(BashQuote(argument));
        return new(name, script.ToString());
    }
}

internal sealed class WslAtomPipelineRunner : IDisposable
{
    private readonly SemaphoreSlim _singleJob = new(1, 1);
    private Process? _activeProcess;

    public bool IsRunning => _activeProcess is { HasExited: false };

    public async Task<AtomPipelineResult> RunAsync(
        AtomPipelineCommand command, string distribution, Action<string> output, CancellationToken cancellationToken)
    {
        if (!await _singleJob.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("An Atom pipeline job is already running.");
        var started = Stopwatch.StartNew();
        try
        {
            var info = new ProcessStartInfo("wsl.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrWhiteSpace(distribution))
            {
                info.ArgumentList.Add("--distribution");
                info.ArgumentList.Add(distribution.Trim());
            }
            info.ArgumentList.Add("--exec");
            info.ArgumentList.Add("bash");
            info.ArgumentList.Add("-lc");
            info.ArgumentList.Add(command.Script);
            using var process = new Process { StartInfo = info };
            _activeProcess = process;
            if (!process.Start()) throw new InvalidOperationException("WSL failed to start.");
            var stdout = PumpAsync(process.StandardOutput, output, cancellationToken);
            var stderr = PumpAsync(process.StandardError, line => output($"ERROR · {line}"), cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            return new(command.Name, process.ExitCode, started.Elapsed);
        }
        finally
        {
            _activeProcess = null;
            _singleJob.Release();
        }
    }

    public void Cancel()
    {
        try { if (_activeProcess is { HasExited: false } process) process.Kill(entireProcessTree: true); }
        catch { }
    }

    public void Dispose()
    {
        Cancel();
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> output, CancellationToken token)
    {
        while (await reader.ReadLineAsync(token) is { } line) output(line);
    }
}

internal sealed record FoundrySessionItem(
    string Id, string Directory, string DisplayName, int Cycles, int Events,
    IReadOnlyList<string> TargetClasses, IReadOnlyList<string> ProjectedClasses,
    IReadOnlyList<string> NegativeClasses, bool HasProjectedTruth, bool HasExplicitNegatives)
{
    public static IReadOnlyList<FoundrySessionItem> LoadRecent(int maximum = 50)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atom", "Foundry");
        return LoadFromRoot(root, maximum);
    }

    internal static IReadOnlyList<FoundrySessionItem> LoadFromRoot(string root, int maximum = 50)
    {
        if (!System.IO.Directory.Exists(root)) return [];
        var sessions = new List<FoundrySessionItem>();
        foreach (var directory in System.IO.Directory.GetDirectories(root, "foundry_*").OrderByDescending(value => value).Take(maximum))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "session.json")));
                var manifest = document.RootElement;
                var id = manifest.TryGetProperty("sessionId", out var sessionId) ? sessionId.GetString() ?? Path.GetFileName(directory) : Path.GetFileName(directory);
                var cycles = manifest.TryGetProperty("cycleCount", out var cycleCount) ? cycleCount.GetInt32() : 0;
                var events = manifest.TryGetProperty("eventCount", out var eventCount) ? eventCount.GetInt32() : 0;
                var activity = manifest.TryGetProperty("activity", out var activityValue) ? activityValue.GetString() : null;
                var targets = manifest.TryGetProperty("targetClasses", out var targetValues) && targetValues.ValueKind == JsonValueKind.Array
                    ? targetValues.EnumerateArray().Select(value => value.GetProperty("class").GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray()
                    : [];
                var projected = manifest.TryGetProperty("captureCapabilities", out var capabilities) &&
                                capabilities.TryGetProperty("projectedTargetTruth", out var projectedValue) && projectedValue.GetBoolean();
                var negatives = manifest.TryGetProperty("captureCapabilities", out capabilities) &&
                                capabilities.TryGetProperty("explicitAbsentSceneLabels", out var negativeValue) && negativeValue.GetBoolean();
                var summaries = manifest.TryGetProperty("targetCaptureSummary", out var summaryValues) && summaryValues.ValueKind == JsonValueKind.Array
                    ? summaryValues.EnumerateArray().ToArray()
                    : [];
                var projectedClasses = summaries.Where(value => value.TryGetProperty("projectedFrames", out var count) && count.GetInt32() > 0)
                    .Select(value => value.GetProperty("class").GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
                var negativeClasses = summaries.Where(value => value.TryGetProperty("negativeFrames", out var count) && count.GetInt32() > 0)
                    .Select(value => value.GetProperty("class").GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray();
                var display = $"{id} · {cycles} cycles · {events} events{(string.IsNullOrWhiteSpace(activity) ? "" : $" · {activity}")}";
                sessions.Add(new(id, directory, display, cycles, events, targets, projectedClasses, negativeClasses, projected, negatives));
            }
            catch { }
        }
        return sessions;
    }
}

internal sealed record AtomTaskItem(
    string Id, string Name, string TaskType, string PrimaryClass, string RecipeId, string RelativePath)
{
    public string DisplayName => $"{Name} · {TaskType} · {RecipeId}";

    public static IReadOnlyList<AtomTaskItem> LoadFromRepository(string repository)
    {
        var taskRoot = Path.Combine(repository, "tasks");
        if (!Directory.Exists(taskRoot)) return [];
        var tasks = new List<AtomTaskItem>();
        foreach (var path in Directory.GetFiles(taskRoot, "*.task.json").OrderBy(value => value))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var root = document.RootElement;
                if (root.GetProperty("schemaVersion").GetString() != "1.0") continue;
                var classes = root.GetProperty("classes").EnumerateArray().Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
                if (classes.Length == 0) continue;
                tasks.Add(new(
                    root.GetProperty("id").GetString()!, root.GetProperty("name").GetString()!,
                    root.GetProperty("taskType").GetString()!, classes[0]!,
                    root.GetProperty("recipe").GetProperty("id").GetString()!,
                    Path.GetRelativePath(repository, path).Replace('\\', '/')));
            }
            catch { }
        }
        return tasks;
    }
}
