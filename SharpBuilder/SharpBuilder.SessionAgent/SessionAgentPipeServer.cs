using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace SharpBuilder.SessionAgent;

/// <summary>
/// Named-pipe transport for <see cref="SessionAgentCore"/>: serves
/// <c>\\.\pipe\MESharp.Builder.&lt;pid&gt;</c> following docs/IPC_CONVENTIONS.md — multi-instance
/// (one stuck client can't lock the others out), newline-delimited JSON, cross-integrity ACL,
/// and per-connection event push for run-subscribed clients.
/// </summary>
public sealed class SessionAgentPipeServer : IDisposable
{
	private const int MaxInstances = 8;

	private readonly SessionAgentCore _core;
	private readonly CancellationTokenSource _cts = new();
	private readonly ConcurrentDictionary<ClientConnection, byte> _connections = new();
	private Task? _listenerTask;
	private bool _disposed;

	public SessionAgentPipeServer(SessionAgentCore core)
	{
		_core = core ?? throw new ArgumentNullException(nameof(core));
		_core.EventPublished += BroadcastEvent;
	}

	public static string PipeName => $"MESharp.Builder.{Environment.ProcessId}";

	public void Start()
	{
		if (_disposed)
			return;

		_listenerTask ??= Task.Run(ListenAsync, _cts.Token);
		Console.WriteLine($"[SessionAgent] Listening on pipe '{PipeName}'.");
	}

	private async Task ListenAsync()
	{
		var token = _cts.Token;

		while (!token.IsCancellationRequested)
		{
			NamedPipeServerStream? pipe = null;
			try
			{
				pipe = CreateServerStream();
				await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);

				var connected = pipe;
				pipe = null;
				var connection = new ClientConnection(connected);
				_connections.TryAdd(connection, 0);
				_ = Task.Run(() => ServeConnectionAsync(connection, token), token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex) when (!token.IsCancellationRequested)
			{
				Console.WriteLine($"[SessionAgent] Listener error: {ex.Message}");
				try
				{
					await Task.Delay(200, token).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
			finally
			{
				pipe?.Dispose();
			}
		}
	}

	private static NamedPipeServerStream CreateServerStream()
	{
		// Cross-integrity policy from docs/IPC_CONVENTIONS.md §2.5: SYSTEM, Administrators,
		// and the current user; low mandatory label so a medium-integrity Studio/Orbit can
		// reach a high-integrity injected client.
		try
		{
			var user = WindowsIdentity.GetCurrent().User;
			var sddl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)" +
				(user != null ? $"(A;;GA;;;{user.Value})" : "(A;;GA;;;IU)(A;;GA;;;AU)") +
				"S:(ML;;NW;;;LW)";
			var security = new PipeSecurity();
			security.SetSecurityDescriptorSddlForm(sddl);

			return NamedPipeServerStreamAcl.Create(
				PipeName,
				PipeDirection.InOut,
				MaxInstances,
				PipeTransmissionMode.Byte,
				PipeOptions.Asynchronous,
				inBufferSize: 0,
				outBufferSize: 0,
				security);
		}
		catch (Exception ex)
		{
			// ACL construction should never block the agent from serving same-integrity clients.
			Console.WriteLine($"[SessionAgent] Pipe ACL failed ({ex.Message}); using default security.");
			return new NamedPipeServerStream(PipeName, PipeDirection.InOut, MaxInstances,
				PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
		}
	}

	private async Task ServeConnectionAsync(ClientConnection connection, CancellationToken token)
	{
		try
		{
			using var reader = new StreamReader(connection.Pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);

			while (!token.IsCancellationRequested && connection.Pipe.IsConnected)
			{
				string? line;
				try
				{
					line = await reader.ReadLineAsync().WaitAsync(token).ConfigureAwait(false);
				}
				catch (IOException)
				{
					break;
				}
				catch (ObjectDisposedException)
				{
					break;
				}

				if (line == null)
					break;

				var response = _core.HandleLine(connection.State, line);
				if (response != null)
					await connection.WriteLineAsync(response, token).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			// Shutdown.
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[SessionAgent] Connection error: {ex.Message}");
		}
		finally
		{
			_connections.TryRemove(connection, out _);
			connection.Dispose();
		}
	}

	private void BroadcastEvent(string line)
	{
		foreach (var connection in _connections.Keys)
		{
			if (!connection.State.SubscribedToRun)
				continue;

			// Fire-and-forget per client: a slow or dead subscriber must never stall the
			// engine thread that raised the event, or the other subscribers.
			_ = connection.WriteLineAsync(line, _cts.Token);
		}
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		_core.EventPublished -= BroadcastEvent;

		try
		{
			_cts.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		foreach (var connection in _connections.Keys)
			connection.Dispose();
		_connections.Clear();

		try
		{
			_listenerTask?.Wait(1000);
		}
		catch
		{
			// Teardown race.
		}

		_cts.Dispose();
	}

	private sealed class ClientConnection : IDisposable
	{
		private readonly SemaphoreSlim _writeLock = new(1, 1);
		private bool _disposed;

		public ClientConnection(NamedPipeServerStream pipe) => Pipe = pipe;

		public NamedPipeServerStream Pipe { get; }
		public AgentConnection State { get; } = new();

		public async Task WriteLineAsync(string line, CancellationToken token)
		{
			if (_disposed)
				return;

			var bytes = Encoding.UTF8.GetBytes(line + "\n");
			try
			{
				await _writeLock.WaitAsync(token).ConfigureAwait(false);
				try
				{
					await Pipe.WriteAsync(bytes, token).ConfigureAwait(false);
					await Pipe.FlushAsync(token).ConfigureAwait(false);
				}
				finally
				{
					_writeLock.Release();
				}
			}
			catch
			{
				// Client went away mid-write; the read loop will observe the break and clean up.
			}
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			try
			{
				Pipe.Dispose();
			}
			catch
			{
			}
			_writeLock.Dispose();
		}
	}
}
