using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpBuilder.SessionAgent;

/// <summary>
/// Config-driven autostart, ported from the (now deprecated) SharpBuilder.Runner so existing
/// runner configs keep working unchanged. Resolution order (first hit wins):
///   1. SHARPBUILDER_RUNNER_CONFIG environment variable (full path to a config json)
///   2. runner.&lt;pid&gt;.config.json in the SharpBuilder graph folder (per-session, written by an
///      orchestrator — legacy; prefer commanding the agent over its pipe instead)
///   3. runner.config.json in the SharpBuilder graph folder (machine-wide default)
/// Unlike the Runner, the agent never scaffolds a template config: it is always loaded, so an
/// absent config simply means "start idle and wait for pipe commands".
/// </summary>
public static class AgentAutostart
{
	public sealed class AutostartConfig
	{
		[JsonPropertyName("script")] public string? Script { get; set; }
		[JsonPropertyName("loop")] public bool Loop { get; set; } = true;
		[JsonPropertyName("signals")] public Dictionary<string, bool>? Signals { get; set; }
	}

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	/// <summary>
	/// Resolves a config and, if one names a valid graph, loads and starts it on the core.
	/// Returns a human-readable outcome line for the console; never throws.
	/// </summary>
	public static string Apply(SessionAgentCore core, string scriptsDirectory, Func<string?>? envOverride = null)
	{
		try
		{
			var configPath = ResolveConfigPath(scriptsDirectory, envOverride ?? (static () => Environment.GetEnvironmentVariable("SHARPBUILDER_RUNNER_CONFIG")));
			if (configPath == null)
				return "No autostart config; idle until commanded.";

			AutostartConfig? config;
			try
			{
				config = JsonSerializer.Deserialize<AutostartConfig>(File.ReadAllText(configPath), JsonOptions);
			}
			catch (Exception ex)
			{
				return $"Autostart config '{configPath}' unreadable: {ex.Message}";
			}

			if (string.IsNullOrWhiteSpace(config?.Script))
				return $"Autostart config '{configPath}' has no 'script'; idle.";
			if (!File.Exists(config.Script))
				return $"Autostart graph not found: {config.Script}";

			var (ok, error, graph) = core.LoadGraphFromPath(config.Script);
			if (!ok || graph == null)
				return $"Autostart load failed: {error}";

			if (config.Signals != null)
			{
				foreach (var (key, value) in config.Signals)
					core.SetSignal(key, value);
			}

			var start = core.StartRun(config.Loop);
			return start.Ok
				? $"Autostarted '{graph.Name}' (loop={config.Loop}) from {configPath}."
				: $"Autostart of '{graph.Name}' blocked: {string.Join("; ", start.Errors)}";
		}
		catch (Exception ex)
		{
			return $"Autostart failed: {ex.Message}";
		}
	}

	public static string? ResolveConfigPath(string scriptsDirectory, Func<string?> envOverride)
	{
		var fromEnv = envOverride();
		if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
			return fromEnv;

		var perSession = Path.Combine(scriptsDirectory, $"runner.{Environment.ProcessId}.config.json");
		if (File.Exists(perSession))
			return perSession;

		var machineDefault = Path.Combine(scriptsDirectory, "runner.config.json");
		return File.Exists(machineDefault) ? machineDefault : null;
	}
}
