using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;

namespace SharpBuilder.Core.Services;

internal sealed class InCombatExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
		var actual = LocalPlayer.IsInCombat() || LocalPlayer.IsInCombatVarbit() || LocalPlayer.IsTargeting();
		return Task.FromResult(ExecutorHelpers.ConditionOutcome("inCombat", actual, expected));
	}
}

internal sealed class InventoryFullExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
		var isFull = Inventory.IsFull;
		return Task.FromResult(ExecutorHelpers.ConditionOutcome("inventoryFull", isFull, expected));
	}
}

internal sealed class LocationRadiusExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var centers = ParameterHelper.ToCoordinateList(context.Parameters, "center");
		if (centers.Count == 0)
			return Task.FromResult(NodeExecutionResult.Fail());

		var radius = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "radius") ?? 0);
		var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
		var signalKey = ExecutorHelpers.ResolveSignalKey(context.Parameters, "insideAnchor");
		var center = centers[0];
		var distance = LocalPlayer.DistanceTo(center.x, center.y, center.z);
		var inside = distance <= radius;

		return Task.FromResult(ExecutorHelpers.ConditionOutcome(signalKey, inside, expected));
	}
}

internal sealed class PercentResourceExecutor : INodeExecutor
{
	private readonly string _resourceName;
	private readonly Func<int> _readPercent;

	public PercentResourceExecutor(string resourceName, Func<int> readPercent)
	{
		_resourceName = resourceName;
		_readPercent = readPercent;
	}

	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var comparison = ParameterHelper.ToString(context.Parameters, "comparison") ?? "<=";
		var threshold = Math.Clamp(ParameterHelper.ToInt(context.Parameters, "threshold") ?? 0, 0, 100);
		var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
		var signalKey = ExecutorHelpers.ResolveSignalKey(context.Parameters, $"{_resourceName}.threshold");
		var percent = Math.Clamp(_readPercent(), 0, 100);
		var matched = Compare(percent, threshold, comparison);

		return Task.FromResult(ExecutorHelpers.ConditionOutcome(signalKey, matched, expected));
	}

	private static bool Compare(int current, int threshold, string comparison)
	{
		return comparison.Trim() switch
		{
			">=" => current >= threshold,
			">" => current > threshold,
			"<" => current < threshold,
			"==" => current == threshold,
			_ => current <= threshold
		};
	}
}

internal sealed class CooldownExecutor : INodeExecutor
{
	private readonly Dictionary<Guid, DateTime> _lastConsumedUtcByNode = new();

	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var intervalMs = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "intervalMs") ?? 0);
		var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
		var startReady = ParameterHelper.ToBool(context.Parameters, "startReady", true);
		var consumeOnReady = ParameterHelper.ToBool(context.Parameters, "consumeOnReady", true);
		var signalKey = ExecutorHelpers.ResolveSignalKey(context.Parameters, "cooldownReady");

		var now = DateTime.UtcNow;
		var hasLast = _lastConsumedUtcByNode.TryGetValue(context.Node.Id, out var lastConsumedUtc);
		var ready = !hasLast ? startReady : now - lastConsumedUtc >= TimeSpan.FromMilliseconds(intervalMs);

		if (ready && consumeOnReady)
		{
			_lastConsumedUtcByNode[context.Node.Id] = now;
		}
		else if (!hasLast && !startReady)
		{
			_lastConsumedUtcByNode[context.Node.Id] = now;
		}

		return Task.FromResult(ExecutorHelpers.ConditionOutcome(signalKey, ready, expected));
	}
}

internal sealed class QuickHealExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var ok = ActionButtons.QuickHeal();
		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class QuickPrayerExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var mode = (ParameterHelper.ToString(context.Parameters, "mode") ?? "Toggle").Trim();
		var enabled = LocalPlayer.IsQuickPrayerEnabled();
		var shouldToggle = mode.Equals("Toggle", StringComparison.OrdinalIgnoreCase) ||
			(mode.Equals("Enable", StringComparison.OrdinalIgnoreCase) && !enabled) ||
			(mode.Equals("Disable", StringComparison.OrdinalIgnoreCase) && enabled);

		if (!shouldToggle)
			return Task.FromResult(NodeExecutionResult.Success());

		var ok = ActionButtons.QuickPrayer();
		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class FamiliarCheckExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
		var nameFilter = ParameterHelper.ToString(context.Parameters, "name");
		var minTime = ParameterHelper.ToInt(context.Parameters, "minTimeRemaining");
		var minSpellPoints = ParameterHelper.ToInt(context.Parameters, "minSpellPoints");
		var minHealth = ParameterHelper.ToInt(context.Parameters, "minHealth");
		var signalKey = ExecutorHelpers.ResolveSignalKey(context.Parameters, "hasFamiliar");

		var matched = Familiar.HasFamiliar();
		if (matched && !string.IsNullOrWhiteSpace(nameFilter))
			matched = Familiar.GetName().IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
		if (matched && minTime.HasValue)
			matched = Familiar.GetTimeRemaining() >= minTime.Value;
		if (matched && minSpellPoints.HasValue)
			matched = Familiar.GetSpellPoints() >= minSpellPoints.Value;
		if (matched && minHealth.HasValue)
			matched = Familiar.GetHealth() >= minHealth.Value;

		return Task.FromResult(ExecutorHelpers.ConditionOutcome(signalKey, matched, expected));
	}
}

internal sealed class FamiliarActionExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var mode = ParameterHelper.ToString(context.Parameters, "mode") ?? "SpecialAttack";
		var order = Math.Clamp(ParameterHelper.ToInt(context.Parameters, "order") ?? 1, 1, 7);
		var ok = mode.Equals("ActionButton", StringComparison.OrdinalIgnoreCase)
			? ActionButtons.Familiar(order)
			: Familiar.CastSpecialAttack();

		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class FamiliarSummonExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var onlyIfMissing = ParameterHelper.ToBool(context.Parameters, "onlyIfMissing", true);
		if (onlyIfMissing && Familiar.HasFamiliar())
			return Task.FromResult(NodeExecutionResult.Success());

		var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "pouches");
		if (ids.Count == 0 && names.Count == 0)
			return Task.FromResult(NodeExecutionResult.Fail());

		var menuIndex = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "menuIndex") ?? 1);
		var offset = ParameterHelper.ToInt(context.Parameters, "offset") ?? Objects.Offsets.GeneralInterfaceRoute;

		var ok = ExecutorHelpers.InventoryDoActionOnFirst(ids, names, menuIndex, offset, cancellationToken);
		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}
