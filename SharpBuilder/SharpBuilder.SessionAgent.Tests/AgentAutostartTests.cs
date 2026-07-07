using System.Text.Json;
using SharpBuilder.Core.Services;
using SharpBuilder.SessionAgent;
using Xunit;

namespace SharpBuilder.SessionAgent.Tests;

public class AgentAutostartTests : IDisposable
{
	private readonly string _dir = Directory.CreateTempSubdirectory("agent-autostart-").FullName;

	public void Dispose()
	{
		try { Directory.Delete(_dir, recursive: true); } catch { }
	}

	private string SaveGraph(string name = "Autostart graph")
	{
		var catalog = new NodeCatalogService();
		var service = new GraphScriptService(catalog);
		var path = Path.Combine(_dir, $"{Guid.NewGuid():N}.builder.json");
		service.SaveAsync(service.CreateNew(name), path).GetAwaiter().GetResult();
		return path;
	}

	private string WriteConfig(string fileName, object config)
	{
		var path = Path.Combine(_dir, fileName);
		File.WriteAllText(path, JsonSerializer.Serialize(config));
		return path;
	}

	[Fact]
	public void ResolveConfigPath_PrefersEnvThenPerPidThenDefault()
	{
		var envConfig = WriteConfig("env.config.json", new { script = "x" });
		var perPid = WriteConfig($"runner.{Environment.ProcessId}.config.json", new { script = "x" });
		var machine = WriteConfig("runner.config.json", new { script = "x" });

		Assert.Equal(envConfig, AgentAutostart.ResolveConfigPath(_dir, () => envConfig));
		Assert.Equal(perPid, AgentAutostart.ResolveConfigPath(_dir, () => null));

		File.Delete(perPid);
		Assert.Equal(machine, AgentAutostart.ResolveConfigPath(_dir, () => null));

		File.Delete(machine);
		Assert.Null(AgentAutostart.ResolveConfigPath(_dir, () => null));
	}

	[Fact]
	public void Apply_WithoutConfig_StaysIdle()
	{
		using var core = new SessionAgentCore();
		var outcome = AgentAutostart.Apply(core, _dir, () => null);
		Assert.Contains("idle", outcome, StringComparison.OrdinalIgnoreCase);
		Assert.False(core.IsRunning);
	}

	[Fact]
	public async Task Apply_WithRunnerStyleConfig_LoadsAndStartsTheGraph()
	{
		var graphPath = SaveGraph();
		// PascalCase keys on purpose: legacy Runner configs were written by Newtonsoft with
		// default (PascalCase) naming and must keep working.
		WriteConfig("runner.config.json", new Dictionary<string, object?>
		{
			["Script"] = graphPath,
			["Loop"] = true,
			["Signals"] = new Dictionary<string, bool> { ["invFull"] = true }
		});

		using var core = new SessionAgentCore();
		var outcome = AgentAutostart.Apply(core, _dir, () => null);

		Assert.StartsWith("Autostarted", outcome);
		await WaitUntilAsync(() => core.IsRunning, "autostarted run did not begin");

		core.StopRun();
		await WaitUntilAsync(() => !core.IsRunning, "run did not stop");
	}

	[Fact]
	public void Apply_WithMissingGraph_ReportsAndStaysIdle()
	{
		WriteConfig("runner.config.json", new { script = Path.Combine(_dir, "missing.builder.json"), loop = true });

		using var core = new SessionAgentCore();
		var outcome = AgentAutostart.Apply(core, _dir, () => null);

		Assert.Contains("not found", outcome);
		Assert.False(core.IsRunning);
	}

	private static async Task WaitUntilAsync(Func<bool> condition, string failure, int timeoutMs = 5000)
	{
		var deadline = Environment.TickCount64 + timeoutMs;
		while (Environment.TickCount64 < deadline)
		{
			if (condition())
				return;
			await Task.Delay(25);
		}

		Assert.Fail(failure);
	}
}
