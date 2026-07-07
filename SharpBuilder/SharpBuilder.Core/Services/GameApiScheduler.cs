using System;
using System.Threading;
using System.Threading.Tasks;

namespace SharpBuilder.Core.Services;

/// <summary>
/// Serializes all native game-API access (P/Invoke into XInput1_4.dll) onto a single lane.
///
/// SharpBuilder reaches the game from two independent threads: the graph executor (running nodes
/// on a background task) and the dashboard refresh (XP/items/identity captures on their own task).
/// Letting those threads call into the client concurrently is unsafe — the native side reads game
/// memory and drives interface state that is not thread-safe. Funnelling every call through this
/// scheduler guarantees mutual exclusion: at most one game operation is in flight at any moment.
/// </summary>
public interface IGameApiScheduler
{
	/// <summary>
	/// Acquires exclusive use of the game-API lane. Dispose the returned token to release it.
	/// Use this when you need to hold the lane across an <c>await</c> (e.g. a whole node execution).
	/// </summary>
	Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default);

	/// <summary>Runs a synchronous game operation under exclusive access.</summary>
	Task RunAsync(Action operation, CancellationToken cancellationToken = default);

	/// <summary>Runs a synchronous game read under exclusive access and returns its result.</summary>
	Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IGameApiScheduler"/> backed by a single binary semaphore. Waiters are served
/// fairly enough that short dashboard reads interleave with the executor between nodes; a long node
/// that holds the lane for its full duration simply makes the next dashboard read wait its turn
/// rather than racing it.
/// </summary>
public sealed class GameApiScheduler : IGameApiScheduler
{
	private readonly SemaphoreSlim _gate = new(1, 1);

	public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		return new Releaser(_gate);
	}

	public async Task RunAsync(Action operation, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			operation();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			return operation();
		}
		finally
		{
			_gate.Release();
		}
	}

	private sealed class Releaser : IDisposable
	{
		private SemaphoreSlim? _gate;

		public Releaser(SemaphoreSlim gate) => _gate = gate;

		// Exchange guards against a double-dispose releasing the semaphore twice.
		public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
	}
}

/// <summary>
/// Process-wide game-API lane. The executor engine and the WPF dashboard both go through this single
/// instance so their native calls can never overlap. Swappable for tests.
/// </summary>
public static class GameApi
{
	public static IGameApiScheduler Scheduler { get; set; } = new GameApiScheduler();
}

/// <summary>
/// Marks an executor that drives the game-API lane itself — gating its discrete native operations
/// through <see cref="GameApi.Scheduler"/> while releasing the lane during long waits (e.g. polling
/// for a Make-X craft to finish). The engine does <b>not</b> hold the lane around these executors, so
/// the dashboard can read game state in the gaps between their polls. A plain executor (no marker) is
/// instead wrapped by the engine for its whole duration.
/// </summary>
public interface IGameApiSelfManaged
{
}

/// <summary>
/// Convenience helpers for <see cref="IGameApiSelfManaged"/> executors: run a single native op on the
/// shared lane, or poll a gated condition while sleeping <b>outside</b> the lane so the dashboard can
/// read game state between polls. Use these instead of touching the game directly so a long-running
/// node (alch-all, drop-all, Make-X, …) never blocks the UI for its whole duration.
/// </summary>
internal static class GameLane
{
	/// <summary>Runs one synchronous native mutation/read under exclusive game-API access.</summary>
	public static Task Run(Action operation, CancellationToken cancellationToken = default)
		=> GameApi.Scheduler.RunAsync(operation, cancellationToken);

	/// <summary>Runs one synchronous native read under exclusive game-API access and returns its result.</summary>
	public static Task<T> Run<T>(Func<T> operation, CancellationToken cancellationToken = default)
		=> GameApi.Scheduler.RunAsync(operation, cancellationToken);

	/// <summary>
	/// Polls a gated condition every <paramref name="pollMs"/> until it is true or the timeout elapses.
	/// Only the read is gated; the wait between polls happens off the lane.
	/// </summary>
	public static async Task<bool> PollUntil(Func<bool> condition, int timeoutMs, CancellationToken cancellationToken, int pollMs = 100)
	{
		var deadline = Environment.TickCount64 + Math.Max(0, timeoutMs);
		while (true)
		{
			if (await GameApi.Scheduler.RunAsync(condition, cancellationToken).ConfigureAwait(false))
				return true;
			if (Environment.TickCount64 >= deadline)
				return false;
			await Task.Delay(pollMs, cancellationToken).ConfigureAwait(false);
		}
	}
}
