using System.Runtime.CompilerServices;
using MESharp.Services;
using SharpBuilder.SessionAgent;

namespace MESharp;

/// <summary>
/// SharpBuilder SessionAgent: hot-reloadable, headless per-session control surface. Hosts the
/// graph engine in-process and serves <c>MESharp.Builder.&lt;pid&gt;</c> so Studio/Orbit can load,
/// run, observe, and stop graphs on this session from a single hub window. Protocol:
/// SESSION_AGENT_PROTOCOL.md; rails: docs/IPC_CONVENTIONS.md.
/// </summary>
public static class ScriptEntry
{
	private static SessionAgentCore? _core;
	private static SessionAgentPipeServer? _server;
	private static CancellationTokenRegistration _shutdownRegistration;

	public static void Initialize()
	{
		var token = ScriptRuntimeSignals.GetCurrentScriptToken();

		_core = new SessionAgentCore();
		_server = new SessionAgentPipeServer(_core);
		_server.Start();

		// Advertise the surface for discovery; no-op on runtimes older than the registry.
		TryRegisterSurface(SessionAgentPipeServer.PipeName);

		// Hot reload cancels this token before Shutdown is invoked; hook it too so the pipe
		// closes promptly even if a hung run delays the Shutdown call.
		_shutdownRegistration = token.Register(Shutdown);

		// Config-driven autostart (legacy runner configs) runs off the entry thread so a slow
		// graph load never delays script initialization.
		var core = _core;
		Task.Run(() =>
		{
			var scriptsDirectory = new SharpBuilder.Core.Services.GraphScriptService().ScriptsDirectory;
			var outcome = AgentAutostart.Apply(core, scriptsDirectory);
			Console.WriteLine($"[SessionAgent] {outcome}");
		});

		Console.WriteLine($"[SessionAgent] Ready (pid {Environment.ProcessId}).");
	}

	public static void Shutdown()
	{
		_shutdownRegistration.Dispose();
		TryRegisterSurface(null);

		_server?.Dispose();
		_server = null;
		_core?.Dispose();
		_core = null;

		Console.WriteLine("[SessionAgent] Stopped.");
	}

	// Isolated + non-inlined so a missing SessionRegistry type (older injected csharp_interop)
	// throws here, inside the catch, instead of failing the JIT of Initialize/Shutdown.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void TryRegisterSurface(string? pipeName)
	{
		try
		{
			SessionRegistry.SetSurface("builder", pipeName);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[SessionAgent] Session registry unavailable (non-fatal): {ex.Message}");
		}
	}
}
