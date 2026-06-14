using System.IO;
using SharpBuilder.Core.Services;
using MESharp.Services;
using Newtonsoft.Json;

namespace MESharp;

/// <summary>
/// Headless runner: executes a saved .orbitfsm.json graph through the standard ME script
/// framework, no editor required.
///
/// Config resolution order (first hit wins), so individual game sessions can run
/// different graphs even though they share the same scripts folder:
///   1. SHARPBUILDER_RUNNER_CONFIG environment variable (full path to a config json)
///   2. runner.&lt;pid&gt;.config.json in the SharpBuilder graph folder (written by an orchestrator
///      such as Orbit before issuing LOAD for that specific client process)
///   3. runner.config.json in the SharpBuilder graph folder (machine-wide default)
/// </summary>
public static class ScriptEntry
{
    private static CancellationTokenSource? _cts;

    public static void Initialize()
    {
        var token = ScriptRuntimeSignals.GetCurrentScriptToken();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = _cts.Token;

        Task.Run(async () =>
        {
            try
            {
                await RunAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SharpBuilder.Runner] Fatal: {ex}");
            }
        }, ct);
    }

    public static void Shutdown()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private static async Task RunAsync(CancellationToken ct)
    {
        var catalog = new NodeCatalogService();
        var scriptService = new GraphScriptService(catalog);

        var config = LoadOrScaffoldConfig(scriptService.ScriptsDirectory);
        if (config == null)
            return;

        var (script, loadError) = await scriptService.TryLoadAsync(config.Script!, ct);
        if (script == null)
        {
            Console.WriteLine($"[SharpBuilder.Runner] Could not load script: {config.Script} ({loadError})");
            return;
        }

        var validator = new GraphValidator(catalog);
        var issues = validator.Validate(script, GameRuntime.IsGameApiAvailable);
        foreach (var issue in issues)
        {
            Console.WriteLine($"[SharpBuilder.Runner] {issue}");
        }

        if (issues.Any(i => i.Severity == ValidationSeverity.Error))
        {
            Console.WriteLine("[SharpBuilder.Runner] Aborting: validation errors.");
            return;
        }

        var engine = new GraphExecutionEngine(catalog, new NodeExecutorRegistry());
        engine.NodeEntered += (_, node) => Console.WriteLine($"[SharpBuilder.Runner] -> {node.Title}");
        engine.NodeCompleted += (_, e) => Console.WriteLine($"[SharpBuilder.Runner]    {e.Node.Title}: {e.Result.Status}");
        engine.Faulted += (_, ex) => Console.WriteLine($"[SharpBuilder.Runner] Engine fault: {ex.Message}");
        engine.Completed += (_, _) => Console.WriteLine("[SharpBuilder.Runner] Run complete.");

        Console.WriteLine($"[SharpBuilder.Runner] Running '{script.Name}' (loop={config.Loop}).");
        await engine.RunAsync(script, config.Signals ?? new Dictionary<string, bool>(), config.Loop, ct);
    }

    private static RunnerConfig? LoadOrScaffoldConfig(string scriptsDirectory)
    {
        var configPath = ResolveConfigPath(scriptsDirectory);

        if (configPath == null)
        {
            // Nothing found anywhere: scaffold the machine-wide default so the user has
            // something concrete to edit.
            var defaultPath = Path.Combine(scriptsDirectory, "runner.config.json");
            Directory.CreateDirectory(scriptsDirectory);
            var template = new RunnerConfig
            {
                Script = Path.Combine(scriptsDirectory, "<your-script>.orbitfsm.json"),
                Loop = true
            };
            File.WriteAllText(defaultPath, JsonConvert.SerializeObject(template, Formatting.Indented));
            Console.WriteLine(
                $"[SharpBuilder.Runner] No config found. A template was written to {defaultPath} — point 'script' at a saved .orbitfsm.json and reload. " +
                $"To target this session only, use runner.{Environment.ProcessId}.config.json or set SHARPBUILDER_RUNNER_CONFIG.");
            return null;
        }

        try
        {
            Console.WriteLine($"[SharpBuilder.Runner] Using config: {configPath}");
            var config = JsonConvert.DeserializeObject<RunnerConfig>(File.ReadAllText(configPath));
            if (string.IsNullOrWhiteSpace(config?.Script) || !File.Exists(config.Script))
            {
                Console.WriteLine($"[SharpBuilder.Runner] Config 'script' is missing or the file does not exist: {config?.Script}");
                return null;
            }

            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SharpBuilder.Runner] Could not parse {configPath}: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveConfigPath(string scriptsDirectory)
    {
        var fromEnv = Environment.GetEnvironmentVariable("SHARPBUILDER_RUNNER_CONFIG");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        var perSession = Path.Combine(scriptsDirectory, $"runner.{Environment.ProcessId}.config.json");
        if (File.Exists(perSession))
            return perSession;

        var machineDefault = Path.Combine(scriptsDirectory, "runner.config.json");
        return File.Exists(machineDefault) ? machineDefault : null;
    }

    private sealed class RunnerConfig
    {
        public string? Script { get; set; }
        public bool Loop { get; set; } = true;

        /// <summary>Optional initial signal values, e.g. { "inventoryFull": false }.</summary>
        public Dictionary<string, bool>? Signals { get; set; }
    }
}
