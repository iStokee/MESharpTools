using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;
using MESharp.API.Input;
using TraversalApi = MESharp.API.Traversal;

namespace SharpBuilder.Core.Services;

internal sealed class LodestoneTeleportExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var destination = ParameterHelper.ToString(context.Parameters, "destination");
		var timeout = ParameterHelper.ToInt(context.Parameters, "timeoutMs") ?? 12000;

		var ok = !string.IsNullOrWhiteSpace(destination) && TraversalApi.Lodestone(destination, timeout);
		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class WalkExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var coords = ParameterHelper.ToCoordinateList(context.Parameters, "target");
		var stopShort = ParameterHelper.ToInt(context.Parameters, "stopShort") ?? 2;
		var timeout = ParameterHelper.ToInt(context.Parameters, "timeoutMs") ?? 8000;
		var jitter = ParameterHelper.ToInt(context.Parameters, "jitter") ?? 1;

		bool ok;
		if (coords.Count > 1)
		{
			ok = TraversalApi.WalkPath(coords.Select(c => (c.x, c.y, c.z)), stopShort, timeout, jitter);
		}
		else if (coords.Count == 1)
		{
			var c = coords[0];
			ok = TraversalApi.WalkTo(c.x, c.y, c.z, stopShort, timeout, jitter);
		}
		else
		{
			return Task.FromResult(NodeExecutionResult.Fail());
		}

		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class WaitExecutor : INodeExecutor
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var delay = ParameterHelper.ToInt(context.Parameters, "delayMs") ?? 0;
		if (delay > 0)
			await Task.Delay(delay, cancellationToken);

		return NodeExecutionResult.Success();
	}
}

internal sealed class WaitRangeExecutor : INodeExecutor
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
		var skill = ParameterHelper.ToString(context.Parameters, "skill") ?? string.Empty;
		var level = ParameterHelper.ToInt(context.Parameters, "level") ?? 0;
		var skillName = skill.ToLowerInvariant() switch
		{
			"attack" => SkillName.Attack,
			"strength" => SkillName.Strength,
			"defence" => SkillName.Defence,
			"defense" => SkillName.Defence,
			"magic" => SkillName.Magic,
			"ranged" => SkillName.Ranged,
			"prayer" => SkillName.Prayer,
			"mining" => SkillName.Mining,
			"smithing" => SkillName.Smithing,
			"fishing" => SkillName.Fishing,
			"cooking" => SkillName.Cooking,
			"crafting" => SkillName.Crafting,
			"fletching" => SkillName.Fletching,
			"woodcutting" => SkillName.Woodcutting,
			"agility" => SkillName.Agility,
			"slayer" => SkillName.Slayer,
			"herblore" => SkillName.Herblore,
			"runecrafting" => SkillName.Runecrafting,
			_ => SkillName.Attack
		};

		var current = Skills.Get(skillName).CurrentLevel;
		var ok = current >= level;
		var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
		{
			[$"skill.{skill}.met"] = ok
		};
		return Task.FromResult(ok ? NodeExecutionResult.Success(outputs) : NodeExecutionResult.Fail(outputs));
	}
}

internal sealed class KeyboardSendExecutor : INodeExecutor
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
			var key = ResolveKey(token);
			if (key == null)
			{
				Console.WriteLine($"[Executor] Keyboard macro: unrecognized key token '{token}'.");
				return NodeExecutionResult.Fail();
			}

			resolved.Add(key.Value);
		}

		var ok = true;
		foreach (var key in resolved)
		{
			ok &= Keyboard.KeyDown(key);
			await Task.Delay(35, cancellationToken);
		}

		for (var i = resolved.Count - 1; i >= 0; i--)
		{
			ok &= Keyboard.KeyUp(resolved[i]);
			await Task.Delay(35, cancellationToken);
		}

		if (delayMs > 0)
			await Task.Delay(delayMs, cancellationToken);

		return ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
	}

	private static Keyboard.VirtualKey? ResolveKey(string token)
	{
		if (Enum.TryParse<Keyboard.VirtualKey>(token, ignoreCase: true, out var named))
			return named;

		var normalized = token.Trim().ToUpperInvariant();
		return normalized switch
		{
			"CTRL" => Keyboard.VirtualKey.Control,
			"RETURN" => Keyboard.VirtualKey.Enter,
			"ESC" => Keyboard.VirtualKey.Escape,
			_ when normalized.Length == 1 && normalized[0] is >= 'A' and <= 'Z' => (Keyboard.VirtualKey)normalized[0],
			_ when normalized.Length == 1 && normalized[0] is >= '0' and <= '9' => (Keyboard.VirtualKey)normalized[0],
			_ when normalized.Length is 2 or 3 && normalized[0] == 'F' &&
			       int.TryParse(normalized[1..], out var fn) && fn is >= 1 and <= 12 => (Keyboard.VirtualKey)(0x6F + fn),
			_ => null
		};
	}
}
