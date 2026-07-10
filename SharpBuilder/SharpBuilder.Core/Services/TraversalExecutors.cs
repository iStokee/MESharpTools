using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;
using MESharp.API.Input;
using TraversalApi = MESharp.API.Traversal;

namespace SharpBuilder.Core.Services;

// Self-managed: the lodestone dispatch (open map + click) is gated as a short op, then the teleport
// animation + arrival are awaited off the lane so the dashboard keeps updating while travelling.
internal sealed class LodestoneTeleportExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var destination = ParameterHelper.ToString(context.Parameters, "destination");
		var timeout = ParameterHelper.ToInt(context.Parameters, "timeoutMs") ?? 12000;
		if (string.IsNullOrWhiteSpace(destination))
			return NodeExecutionResult.Fail();

		var dispatched = await GameLane.Run(() => TraversalApi.Lodestone(destination, Math.Min(timeout, 8000)), cancellationToken);
		if (!dispatched)
			return NodeExecutionResult.Fail();

		// Teleport animation plays for a moment before arrival; then hold the node until movement and
		// the arrival animation settle so the next node doesn't act mid-teleport. All off the lane.
		await Task.Delay(1500, cancellationToken);
		await WaitForTeleportSettle(Math.Max(3000, timeout), cancellationToken);
		return NodeExecutionResult.Success();
	}

	private static async Task WaitForTeleportSettle(int timeoutMs, CancellationToken cancellationToken)
	{
		var deadline = Environment.TickCount64 + timeoutMs;
		var idleStreak = 0;
		while (Environment.TickCount64 < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// One gated read covers both signals; movement unlocked + idle animation == arrived.
			var settled = await GameLane.Run(() =>
			{
				var moving = LocalPlayer.IsMoving();
				int animation;
				try { animation = LocalPlayer.GetAnimation(); }
				catch { animation = -1; }
				return !moving && animation <= 0;
			}, cancellationToken);

			if (settled)
			{
				if (++idleStreak >= 2)
					return;
			}
			else
			{
				idleStreak = 0;
			}

			await Task.Delay(300, cancellationToken);
		}
	}
}

// Self-managed: dispatches the walk click then polls arrival with gated reads (re-clicking when the
// player goes stationary), sleeping off the lane between polls so a long walk never freezes the UI.
// Unlike the old fire-and-forget node, this completes only once the player actually arrives.
internal sealed class WalkExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var coords = ParameterHelper.ToCoordinateList(context.Parameters, "target");
		if (coords.Count == 0)
			return NodeExecutionResult.Fail();

		var arrival = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "stopShort") ?? 2);
		var timeout = ParameterHelper.ToInt(context.Parameters, "timeoutMs") ?? 8000;
		var jitter = ParameterHelper.ToInt(context.Parameters, "jitter") ?? 1;

		// Walk waypoints in order; intermediate ones use a looser arrival window than the final tile.
		for (var i = 0; i < coords.Count; i++)
		{
			var c = coords[i];
			var isLast = i == coords.Count - 1;
			var reached = await WalkToTile(c.x, c.y, c.z, isLast ? arrival : Math.Max(arrival, 4), timeout, jitter, cancellationToken);
			if (!reached)
				return NodeExecutionResult.Fail();
		}

		return NodeExecutionResult.Success();
	}

	private static async Task<bool> WalkToTile(int x, int y, int z, int arrival, int timeoutMs, int jitter, CancellationToken cancellationToken)
	{
		var deadline = Environment.TickCount64 + Math.Max(1000, timeoutMs);
		await GameLane.Run(() => TraversalApi.WalkTo(x, y, z, 0, 8000, jitter), cancellationToken);

		var stationary = 0;
		while (Environment.TickCount64 < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (await GameLane.Run(() => TraversalApi.IsWithinDistance(x, y, z, arrival), cancellationToken))
				return true;

			// Re-click only after two consecutive stationary reads (mirrors Traversal.WalkToAsync):
			// a single stationary read often just catches a tick boundary, and re-clicking every poll
			// spams input.
			if (!await GameLane.Run(() => LocalPlayer.IsMoving(), cancellationToken))
			{
				if (++stationary >= 2)
				{
					await GameLane.Run(() => TraversalApi.WalkTo(x, y, z, 0, 8000, jitter), cancellationToken);
					stationary = 0;
				}
			}
			else
			{
				stationary = 0;
			}

			await Task.Delay(300, cancellationToken);
		}

		return await GameLane.Run(() => TraversalApi.IsWithinDistance(x, y, z, arrival), cancellationToken);
	}
}

// Self-managed: a pure wait must not hold the game-API lane (it touches no game state), otherwise the
// dashboard would freeze for the whole delay.
internal sealed class WaitExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var delay = ParameterHelper.ToInt(context.Parameters, "delayMs") ?? 0;
		if (delay > 0)
			await Task.Delay(delay, cancellationToken);

		return NodeExecutionResult.Success();
	}
}

// Self-managed: see WaitExecutor — a pure delay should never hold the lane.
internal sealed class WaitRangeExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var minDelay = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "minDelayMs") ?? 0);
		var maxDelay = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "maxDelayMs") ?? minDelay);
		if (maxDelay < minDelay)
			(maxDelay, minDelay) = (minDelay, maxDelay);

		var delay = maxDelay == minDelay
			? minDelay
			: Random.Shared.Next(minDelay, maxDelay + 1);

		if (delay > 0)
			await Task.Delay(delay, cancellationToken);

		return NodeExecutionResult.Success();
	}
}

internal sealed class SkillRequirementExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var skill = (ParameterHelper.ToString(context.Parameters, "skill") ?? string.Empty).Trim();
		var level = ParameterHelper.ToInt(context.Parameters, "level") ?? 0;

		if (!TryResolveSkill(skill, out var skillName))
		{
			// An unknown skill must fail loudly — defaulting to any skill could wrongly pass the gate.
			ExecutorLog.Write("Skills", context, $"unknown skill '{skill}'");
			return Task.FromResult(NodeExecutionResult.Fail(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				[$"skill.{skill}.met"] = false
			}));
		}

		var current = Skills.Get(skillName).CurrentLevel;
		var ok = current >= level;
		var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
		{
			[$"skill.{skill}.met"] = ok
		};
		return Task.FromResult(ok ? NodeExecutionResult.Success(outputs) : NodeExecutionResult.Fail(outputs));
	}

	private static bool TryResolveSkill(string token, out SkillName skill)
	{
		skill = default;
		if (string.IsNullOrWhiteSpace(token))
			return false;

		if (Enum.TryParse(token, ignoreCase: true, out skill))
			return true;

		// Common spellings that don't match the enum names.
		switch (token.ToLowerInvariant())
		{
			case "defense":
				skill = SkillName.Defence;
				return true;
			case "runecraft":
				skill = SkillName.Runecrafting;
				return true;
			default:
				return false;
		}
	}
}

// Self-managed: gate each key press but keep the inter-key delays off the lane.
internal sealed class KeyboardSendExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var keys = ParameterHelper.ToString(context.Parameters, "keys");
		var delayMs = ParameterHelper.ToInt(context.Parameters, "delayMs") ?? 0;

		if (string.IsNullOrWhiteSpace(keys))
			return NodeExecutionResult.Fail();

		var tokens = keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var resolved = new List<Keyboard.VirtualKey>();
		foreach (var token in tokens)
		{
			if (!KeyboardTokenResolver.TryResolve(token, out var key))
			{
				ExecutorLog.Write("Input", context, $"unrecognized key token '{token}'");
				return NodeExecutionResult.Fail();
			}

			resolved.Add(key);
		}

		// Single non-modifier key (the common case, e.g. an action-bar keybind): use the
		// direct game-input path, which is immune to window focus and ImGui capture.
		if (resolved.Count == 1 && (int)resolved[0] is >= '0' and <= '9' or >= 'A' and <= 'Z')
		{
			var single = resolved[0];
			var pressed = await GameLane.Run(() => KeyboardTokenResolver.Press(single), cancellationToken);
			if (delayMs > 0)
				await Task.Delay(delayMs, cancellationToken);
			return pressed ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
		}

		var ok = true;
		foreach (var key in resolved)
		{
			ok &= await GameLane.Run(() => Keyboard.KeyDown(key), cancellationToken);
			await Task.Delay(35, cancellationToken);
		}

		for (var i = resolved.Count - 1; i >= 0; i--)
		{
			var releaseKey = resolved[i];
			ok &= await GameLane.Run(() => Keyboard.KeyUp(releaseKey), cancellationToken);
			await Task.Delay(35, cancellationToken);
		}

		if (delayMs > 0)
			await Task.Delay(delayMs, cancellationToken);

		return ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
	}

}
