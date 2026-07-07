using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SharpBuilder.Editor.Wpf.Services;

/// <summary>
/// Client for a session's SharpBuilder SessionAgent pipe (<c>MESharp.Builder.&lt;pid&gt;</c>).
/// Speaks protocol v1 (SESSION_AGENT_PROTOCOL.md): newline-JSON, hello handshake, id-correlated
/// request/response, and pushed run events after <see cref="SubscribeRunAsync"/>.
/// Deliberately shares no types with the agent — the wire protocol is the contract.
/// </summary>
public sealed class SessionAgentClient : IDisposable
{
	private const string HelloKey = "hello";
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

	private readonly NamedPipeClientStream _pipe;
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
	private readonly CancellationTokenSource _cts = new();
	private Task? _readLoop;
	private bool _disposed;

	/// <summary>Raised (on a background thread) for every pushed event line; marshal to the UI yourself.</summary>
	public event Action<JsonElement>? EventReceived;

	/// <summary>Raised once when the connection drops or is disposed.</summary>
	public event Action? Disconnected;

	private SessionAgentClient(NamedPipeClientStream pipe) => _pipe = pipe;

	public static async Task<SessionAgentClient> ConnectAsync(
		string pipeName, int timeoutMs = 4000, CancellationToken cancellationToken = default)
	{
		var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		try
		{
			await pipe.ConnectAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			pipe.Dispose();
			throw;
		}

		var client = new SessionAgentClient(pipe);
		client._readLoop = Task.Run(client.ReadLoopAsync);

		try
		{
			var helloTcs = client.Register(HelloKey);
			await client.WriteLineAsync("""{"type":"hello","version":1,"client":"Studio"}""", cancellationToken).ConfigureAwait(false);
			var hello = await helloTcs.Task.WaitAsync(RequestTimeout, cancellationToken).ConfigureAwait(false);
			if (hello.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
				throw new InvalidOperationException($"Agent rejected handshake: {GetString(hello, "message") ?? "unknown"}");
			return client;
		}
		catch
		{
			client.Dispose();
			throw;
		}
	}

	/// <summary>Sends a request and awaits its id-correlated response.</summary>
	public async Task<JsonElement> RequestAsync(
		string verb, IReadOnlyDictionary<string, object?>? extra = null, CancellationToken cancellationToken = default)
	{
		var id = Guid.NewGuid().ToString("N");
		var payload = new Dictionary<string, object?> { ["type"] = "request", ["id"] = id, ["verb"] = verb };
		if (extra != null)
		{
			foreach (var (key, value) in extra)
				payload[key] = value;
		}

		var tcs = Register(id);
		try
		{
			await WriteLineAsync(JsonSerializer.Serialize(payload), cancellationToken).ConfigureAwait(false);
			return await tcs.Task.WaitAsync(RequestTimeout, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_pending.TryRemove(id, out _);
		}
	}

	/// <summary>Asks the agent to push run events on this connection (no confirmation awaited).</summary>
	public Task SubscribeRunAsync(CancellationToken cancellationToken = default)
		=> WriteLineAsync("""{"type":"subscribe","topics":["run"]}""", cancellationToken);

	private TaskCompletionSource<JsonElement> Register(string key)
	{
		var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[key] = tcs;
		return tcs;
	}

	private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
	{
		var bytes = Encoding.UTF8.GetBytes(line + "\n");
		await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await _pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
			await _pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	private async Task ReadLoopAsync()
	{
		try
		{
			using var reader = new StreamReader(_pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
			while (!_cts.Token.IsCancellationRequested)
			{
				var line = await reader.ReadLineAsync().WaitAsync(_cts.Token).ConfigureAwait(false);
				if (line == null)
					break;
				if (string.IsNullOrWhiteSpace(line))
					continue;

				JsonElement root;
				try
				{
					root = JsonDocument.Parse(line).RootElement.Clone();
				}
				catch (JsonException)
				{
					continue;
				}

				switch (GetString(root, "type"))
				{
					case "hello":
						Complete(HelloKey, root);
						break;
					case "event":
						try
						{
							EventReceived?.Invoke(root);
						}
						catch
						{
							// Subscriber faults must not kill the read loop.
						}
						break;
					case "response":
					case "error":
						var id = GetString(root, "id");
						if (id != null)
							Complete(id, root);
						else if (GetString(root, "type") == "error")
							Complete(HelloKey, root); // pre-hello rejection
						break;
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (IOException)
		{
		}
		catch (ObjectDisposedException)
		{
		}
		finally
		{
			FailAllPending();
			try
			{
				Disconnected?.Invoke();
			}
			catch
			{
			}
		}
	}

	private void Complete(string key, JsonElement root)
	{
		if (_pending.TryRemove(key, out var tcs))
			tcs.TrySetResult(root);
	}

	private void FailAllPending()
	{
		foreach (var key in _pending.Keys)
		{
			if (_pending.TryRemove(key, out var tcs))
				tcs.TrySetException(new IOException("Session agent connection closed."));
		}
	}

	private static string? GetString(JsonElement root, string property)
		=> root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		try
		{
			_cts.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}

		try
		{
			_pipe.Dispose();
		}
		catch
		{
		}

		try
		{
			_readLoop?.Wait(1000);
		}
		catch
		{
		}

		_cts.Dispose();
		_writeLock.Dispose();
	}
}
