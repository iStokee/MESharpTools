using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;
using MESharp.API.Input;

namespace SharpBuilder.Core.Services;

internal sealed class InventoryContainsExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var ids = ParameterHelper.ToIntList(context.Parameters, "ids");
		var names = ParameterHelper.ToStringList(context.Parameters, "names");
		var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);

		var hasIds = ids.Count > 0 && Inventory.ContainsAny(ids.ToArray());
		var hasNames = names.Count > 0 && names.Any(n => Inventory.Contains(n));
		var result = ids.Count == 0 && names.Count == 0 ? Inventory.IsFull : hasIds || hasNames;

		return Task.FromResult(ExecutorHelpers.ConditionOutcome("inventory.contains", result, expected));
	}
}

internal sealed class InventoryCountExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var id = ParameterHelper.ToInt(context.Parameters, "id");
		var name = ParameterHelper.ToString(context.Parameters, "name");
		var min = ParameterHelper.ToInt(context.Parameters, "min");
		var max = ParameterHelper.ToInt(context.Parameters, "max");

		ulong count = 0;
		if (id.HasValue)
			count = Inventory.CountOf(id.Value);
		else if (!string.IsNullOrWhiteSpace(name))
			count = Inventory.CountOf(name);

		var ok = true;
		if (min.HasValue) ok &= count >= (ulong)Math.Max(0, min.Value);
		if (max.HasValue) ok &= count <= (ulong)Math.Max(0, max.Value);

		var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
		{
			["inventory.count.met"] = ok
		};

		return Task.FromResult(ok ? NodeExecutionResult.Success(outputs) : NodeExecutionResult.Fail(outputs));
	}
}

// Self-managed: gates each inventory scan/action but keeps the pacing delays off the lane, so a
// "drop/eat/note all" pass over a full inventory doesn't freeze the dashboard for its whole duration.
internal sealed class InventoryItemsActionExecutor : INodeExecutor, IGameApiSelfManaged
{
	private readonly string _verb;
	private readonly int _paceMs;

	public InventoryItemsActionExecutor(string verb, int paceMs)
	{
		_verb = verb;
		_paceMs = paceMs;
	}

	// Actions that fail to shrink the match count (dropped click, lag) are retried this many times
	// in a row before the pass gives up — mirrors InventoryAlchAllExecutor.
	private const int MaxConsecutiveNoProgress = 3;

	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "items");
		if (ids.Count == 0 && names.Count == 0)
			return NodeExecutionResult.Fail();

		var plan = QuantityPlan.Parse(context.Parameters, "One");
		var limit = plan.Limit;

		var performed = 0;
		var consecutiveNoProgress = 0;
		while (performed < limit)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var matches = await GameLane.Run(() => FindMatches(ids, names), cancellationToken);
			if (matches.Count == 0)
				break;

			var before = TotalAmount(matches);
			var firstId = matches[0].Id;
			if (!await GameLane.Run(() => Act(firstId), cancellationToken))
				break;

			performed++;
			if (performed >= limit)
				break;

			// Wait for the action to actually land instead of only sleeping the pace delay; a lag
			// spike otherwise reads an unchanged count and used to abort the whole pass.
			var progressed = await GameLane.PollUntil(
				() => TotalAmount(FindMatches(ids, names)) < before,
				Math.Max(_paceMs * 2, 1500),
				cancellationToken);
			if (progressed)
			{
				consecutiveNoProgress = 0;
			}
			else if (++consecutiveNoProgress >= MaxConsecutiveNoProgress)
			{
				ExecutorLog.Write("Inventory", context, $"{_verb} made no progress {consecutiveNoProgress} times in a row; leaving pass");
				break;
			}

			await Task.Delay(_paceMs, cancellationToken);
		}

		var remaining = await GameLane.Run(() => FindMatches(ids, names), cancellationToken);
		var ok = plan.IsSatisfied(performed, remaining.Count == 0);
		if (ok)
			return NodeExecutionResult.Success();

		// Retry (engine-bounded) instead of Fail: an unfinished pass is usually transient; genuinely
		// stuck states still reach the fail edge once the engine's retry cap trips.
		ExecutorLog.Write("Inventory", context, $"{_verb} pass incomplete (performed={performed} remaining={remaining.Count}); requesting retry");
		return NodeExecutionResult.Retry();
	}

	private static List<Inventory.Item> FindMatches(List<int> ids, List<string> names)
		=> Inventory.GetAll()
			.Where(i => ExecutorHelpers.MatchesTarget(i, ids, names))
			.ToList();

	private static ulong TotalAmount(List<Inventory.Item> items)
		=> items.Aggregate(0UL, (sum, i) => sum + Math.Max(1UL, i.Amount));

	private bool Act(int id) => _verb switch
	{
		"eat" => Inventory.Eat(id),
		"drop" => Inventory.Drop(id),
		"note" => Inventory.Note(id),
		_ => false
	};
}

internal sealed class InventoryMenuActionExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "items");
		if (ids.Count == 0 && names.Count == 0)
			return Task.FromResult(NodeExecutionResult.Fail());

		var menuIndex = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "menuIndex") ?? 1);
		var offset = ParameterHelper.ToInt(context.Parameters, "offset") ?? Objects.Offsets.GeneralInterfaceRoute;

		var ok = ExecutorHelpers.InventoryDoActionOnFirst(ids, names, menuIndex, offset, cancellationToken);
		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

// Self-managed: gate each contains/equip call; keep the per-item settle delay off the lane.
internal sealed class InventoryEquipExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "items");
		if (ids.Count == 0 && names.Count == 0)
			return NodeExecutionResult.Fail();

		var anyFound = false;
		var allOk = true;

		foreach (var id in ids)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!await GameLane.Run(() => Inventory.Contains(id), cancellationToken))
				continue;
			anyFound = true;
			// Verify the item is actually worn instead of sleeping blind after the click,
			// mirroring EquipmentUnequipExecutor's worn-state check.
			allOk &= await GameLane.Run(() => Inventory.Equip(id), cancellationToken) &&
				await GameLane.PollUntil(() => Equipment.ContainsById(id), 2000, cancellationToken);
		}

		foreach (var name in names)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!await GameLane.Run(() => Inventory.Contains(name), cancellationToken))
				continue;
			anyFound = true;
			allOk &= await GameLane.Run(() => Inventory.Equip(name), cancellationToken) &&
				await GameLane.PollUntil(() => Equipment.ContainsByName(name), 2000, cancellationToken);
		}

		return anyFound && allOk ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
	}
}

internal sealed class EquipmentContainsExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "items");
		var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);

		var worn = ids.Any(Equipment.ContainsById) || names.Any(Equipment.ContainsByName);

		return Task.FromResult(ExecutorHelpers.ConditionOutcome("equipment.contains", worn, expected));
	}
}

// Self-managed: gate the worn-state reads and each unequip; keep the per-item delay off the lane.
internal sealed class EquipmentUnequipExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "items");
		if (ids.Count == 0 && names.Count == 0)
			return NodeExecutionResult.Fail();

		var idsToRemove = await GameLane.Run(() => ids.Where(Equipment.ContainsById).ToList(), cancellationToken);
		foreach (var id in idsToRemove)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await GameLane.Run(() => { Equipment.UnequipById(id); }, cancellationToken);
			await Task.Delay(600, cancellationToken);
		}

		var namesToRemove = await GameLane.Run(() => names.Where(Equipment.ContainsByName).ToList(), cancellationToken);
		foreach (var name in namesToRemove)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await GameLane.Run(() => { Equipment.UnequipByName(name); }, cancellationToken);
			await Task.Delay(600, cancellationToken);
		}

		var clear = await GameLane.Run(
			() => ids.All(id => !Equipment.ContainsById(id)) && names.All(n => !Equipment.ContainsByName(n)),
			cancellationToken);
		return clear ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
	}
}

internal sealed class InventoryUseExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var id = ParameterHelper.ToInt(context.Parameters, "id");
		var name = ParameterHelper.ToString(context.Parameters, "name");
		var action = (ParameterHelper.ToString(context.Parameters, "action") ?? "Use").ToLowerInvariant();

		if (!id.HasValue && string.IsNullOrWhiteSpace(name))
			return Task.FromResult(NodeExecutionResult.Fail());

		bool ok = action switch
		{
			"eat/drink" or "eat" or "drink" => id.HasValue ? Inventory.Eat(id.Value) : Inventory.Eat(name!),
			"drop" => id.HasValue ? Inventory.Drop(id.Value) : Inventory.Drop(name!),
			"equip" => id.HasValue ? Inventory.Equip(id.Value) : Inventory.Equip(name!),
			_ => id.HasValue ? Inventory.Use(id.Value) : Inventory.Use(name!)
		};

		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class InventoryUseOnExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var fromRaw = ParameterHelper.ToString(context.Parameters, "from");
		var toRaw = ParameterHelper.ToString(context.Parameters, "to");

		if (string.IsNullOrWhiteSpace(fromRaw) || string.IsNullOrWhiteSpace(toRaw))
			return Task.FromResult(NodeExecutionResult.Fail());

		bool ok;
		if (int.TryParse(fromRaw, out var fromId) && int.TryParse(toRaw, out var toId))
			ok = Inventory.UseItemOnItem(fromId, toId);
		else
			ok = Inventory.UseItemOnItem(fromRaw, toRaw);

		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

// Self-managed: the open click and the interface-open poll are gated per call so the dashboard
// keeps reading while the player walks to the booth.
internal sealed class BankOpenExecutor : INodeExecutor, IGameApiSelfManaged
{
	// Covers walking to the nearest booth/chest plus the interface-open tick.
	private const int OpenTimeoutMs = 8000;

	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		if (await GameLane.Run(() => Bank.IsOpen, cancellationToken))
			return NodeExecutionResult.Success();

		var clicked = await GameLane.Run(() => Bank.Open(), cancellationToken);
		var open = clicked && await GameLane.PollUntil(() => Bank.IsOpen, OpenTimeoutMs, cancellationToken);
		if (open)
			return NodeExecutionResult.Success();

		// Retry (engine-bounded) instead of Fail: a click dropped mid-animation or a slow walk is
		// transient, and this node's edge is typically an ungated fallback that would otherwise
		// march into deposit/preset with the bank closed.
		ExecutorLog.Write("Bank", context, $"bank not open (clicked={clicked}, waited {OpenTimeoutMs}ms); requesting retry");
		return NodeExecutionResult.Retry();
	}
}

// Self-managed: gates the deposit click and each verification read separately.
internal sealed class BankDepositAllExecutor : INodeExecutor, IGameApiSelfManaged
{
	private const int EmptyTimeoutMs = 3000;
	private const int PollMs = 100;

	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var exceptIds = ParameterHelper.ToIntList(context.Parameters, "exceptIds");
		var exceptNames = ParameterHelper.ToStringList(context.Parameters, "exceptNames");

		if (await GameLane.Run(() => IsEmptyExcept(exceptIds, exceptNames), cancellationToken))
			return NodeExecutionResult.Success();

		var clicked = await GameLane.Run(() =>
		{
			if (exceptIds.Count > 0)
				return Bank.DepositAllExcept(exceptIds.ToArray());
			if (exceptNames.Count > 0)
				return Bank.DepositAllExcept(exceptNames.ToArray());
			return Bank.DepositAll();
		}, cancellationToken);

		var emptied = clicked &&
			await GameLane.PollUntil(() => IsEmptyExcept(exceptIds, exceptNames), EmptyTimeoutMs, cancellationToken, PollMs);
		if (emptied)
			return NodeExecutionResult.Success();

		ExecutorLog.Write("Bank", context, $"inventory not emptied (clicked={clicked}, waited {EmptyTimeoutMs}ms); requesting retry");
		return NodeExecutionResult.Retry();
	}

	private static bool IsEmptyExcept(List<int> exceptIds, List<string> exceptNames)
	{
		return !Inventory.GetAll().Any(item =>
			item.Id > 0 &&
			!exceptIds.Contains(item.Id) &&
			!exceptNames.Any(n => item.Name?.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0));
	}
}

internal sealed class BankWithdrawExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var id = ParameterHelper.ToInt(context.Parameters, "id");
		var name = ParameterHelper.ToString(context.Parameters, "name");
		var amount = ParameterHelper.ToInt(context.Parameters, "amount");

		var actionIndex = amount.HasValue && amount.Value > 1 ? 1 : 0;
		bool ok = false;

		if (id.HasValue)
			ok = Bank.DoActionById(id.Value, actionIndex);
		else if (!string.IsNullOrWhiteSpace(name))
			ok = Bank.DoActionByName(name, actionIndex);

		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

// Self-managed: gate the preset action, then wait for the load to settle off the lane.
internal sealed class BankLoadPresetExecutor : INodeExecutor, IGameApiSelfManaged
{
	// How long to wait for the bank interface if the preceding open is still settling.
	private const int BankOpenGraceMs = 2000;
	private const int PollMs = 100;

	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var method = (ParameterHelper.ToString(context.Parameters, "method") ?? "Keybind").Trim();
		var loadLast = ParameterHelper.ToBool(context.Parameters, "loadLast", false);
		var waitMs = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "waitMs") ?? 1200);

		// Never fire the preset action against a closed bank: the keybind path in particular would
		// land on the action bar instead. Retry (engine-bounded) so a slow open can catch up.
		if (!await GameLane.PollUntil(() => Bank.IsOpen, BankOpenGraceMs, cancellationToken, PollMs))
		{
			ExecutorLog.Write("Bank", context, "bank not open; refusing to send preset action, requesting retry");
			return NodeExecutionResult.Retry();
		}

		var signatureBefore = await GameLane.Run(() => InventorySignature(), cancellationToken);

		bool ok;
		if (loadLast || method.Equals("LastPreset", StringComparison.OrdinalIgnoreCase))
		{
			ok = await GameLane.Run(() => Bank.LoadLastPreset(), cancellationToken);
		}
		else if (method.Equals("PresetSlot", StringComparison.OrdinalIgnoreCase))
		{
			var preset = Math.Clamp(ParameterHelper.ToInt(context.Parameters, "preset") ?? 1, 1, 18);
			ok = await GameLane.Run(() => Bank.LoadPreset(preset), cancellationToken);
		}
		else
		{
			var keybind = ParameterHelper.ToString(context.Parameters, "keybind");
			if (!KeyboardTokenResolver.TryResolve(keybind, out var key))
				return NodeExecutionResult.Fail();

			ok = await GameLane.Run(() => KeyboardTokenResolver.Press(key), cancellationToken);
		}

		if (ok && waitMs > 0)
		{
			// Poll for the inventory to actually change instead of sleeping blind, so downstream
			// supply checks don't read a not-yet-loaded inventory on a slow tick.
			var changed = await GameLane.PollUntil(() => InventorySignature() != signatureBefore, waitMs, cancellationToken, PollMs);
			ExecutorLog.Write("Bank", context, $"preset load inventoryChanged={changed} (waited up to {waitMs}ms)");
		}

		return ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
	}

	private static int InventorySignature()
	{
		var signature = 17;
		foreach (var item in Inventory.GetAll())
		{
			if (item.Id <= 0)
				continue;
			signature = unchecked(signature * 31 + item.Id);
			signature = unchecked(signature * 31 + (int)item.Amount);
		}
		return signature;
	}
}

// Self-managed: gates the close click and each open-state read separately.
internal sealed class BankCloseExecutor : INodeExecutor, IGameApiSelfManaged
{
	private const int CloseTimeoutMs = 2000;
	private const int PollMs = 100;

	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		if (!await GameLane.Run(() => Bank.IsOpen, cancellationToken))
			return NodeExecutionResult.Success();

		await GameLane.Run(() => Bank.Close(), cancellationToken);
		if (await GameLane.PollUntil(() => !Bank.IsOpen, CloseTimeoutMs, cancellationToken, PollMs))
			return NodeExecutionResult.Success();

		ExecutorLog.Write("Bank", context, $"bank still open after close click (waited {CloseTimeoutMs}ms); requesting retry");
		return NodeExecutionResult.Retry();
	}
}


internal sealed class LootPickupExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var names = ParameterHelper.ToStringList(context.Parameters, "names");
		var ids = ParameterHelper.ToIntList(context.Parameters, "ids");
		var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? 20;

		if (ids.Count == 0 && names.Count > 0)
		{
			ids = GroundItems.GetAll(maxDistance)
				.Where(item => names.Any(n => item.Name?.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
				.Select(item => item.Id)
				.Distinct()
				.ToList();
		}

		if (ids.Count == 0)
			return Task.FromResult(NodeExecutionResult.Fail());

		var ok = Loot.TakeByIds(ids, maxDistance);
		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

// Self-managed: alching a full inventory is the longest-horizon node there is (many casts, each with
// animation waits). Every native read/action is gated individually; all the waiting happens off the
// lane so the dashboard keeps updating XP/items cast-by-cast.
internal sealed class InventoryAlchAllExecutor : INodeExecutor, IGameApiSelfManaged
{
	private const int PollMs = 100;

	// "Idle" reports as 0 in this client build (verified in-game 2026-06-22); some paths also use -1.
	// Treat any non-positive value as idle so animation start/end detection is build-agnostic.
	private static bool IsIdle(int animation) => animation <= 0;

	// Casts that fail to shrink the match count (swallowed keybind, misclick, lag) are retried up
	// to this many times in a row before the node gives up.
	private const int MaxConsecutiveFailedCasts = 3;

	// How long the first iteration waits for matches to show up before concluding the inventory is
	// really empty; freshly crafted items can lag a tick or two behind the make-x completion signal.
	private const int EntryGraceMs = 2000;

	// Cap on waiting for the player to go idle before pressing the alch keybind; a prior craft/alch
	// animation still playing gets ability presses swallowed by the game.
	private const int IdleBeforeCastMs = 4000;

	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "items");
		if (ids.Count == 0 && names.Count == 0)
		{
			ExecutorLog.Write("Alch", context, "fail: no item ids/names configured");
			return NodeExecutionResult.Fail();
		}

		var keybind = ParameterHelper.ToString(context.Parameters, "keybind");
		if (!KeyboardTokenResolver.TryResolve(keybind, out var key))
		{
			ExecutorLog.Write("Alch", context, $"fail: unresolvable keybind \"{keybind ?? string.Empty}\"");
			return NodeExecutionResult.Fail();
		}

		var options = AlchOptions.Parse(context.Parameters);

		var casts = 0;
		var consecutiveFailedCasts = 0;
		var firstIteration = true;
		while (casts < options.Plan.Limit)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var before = await GameLane.Run(() => CountMatches(ids, names, options.RequireAlchable), cancellationToken);
			if (before == 0 && firstIteration)
			{
				// Grace poll: crafted items may not be visible in inventory yet.
				await GameLane.PollUntil(() => CountMatches(ids, names, options.RequireAlchable) > 0, EntryGraceMs, cancellationToken, PollMs);
				before = await GameLane.Run(() => CountMatches(ids, names, options.RequireAlchable), cancellationToken);
				ExecutorLog.Write("Alch", context, $"entry grace poll finished count={before}");
			}
			firstIteration = false;
			if (before == 0)
			{
				ExecutorLog.Write("Alch", context, "no matching items remain; leaving cast loop");
				break;
			}

			var idle = await GameLane.PollUntil(() => IsIdle(LocalPlayer.GetAnimation()), IdleBeforeCastMs, cancellationToken, PollMs);
			ExecutorLog.Write("Alch", context, $"cast #{casts + 1}: before={before} playerIdle={idle}");

			if (!await GameLane.Run(() => KeyboardTokenResolver.Press(key), cancellationToken))
			{
				ExecutorLog.Write("Alch", context, "break: keybind press failed");
				break;
			}

			var targetClickedAt = Environment.TickCount64;
			if (options.ClickInventoryTarget)
			{
				if (options.TargetDelayMs > 0)
					await Task.Delay(options.TargetDelayMs, cancellationToken);

				var target = await GameLane.Run(() => FindFirstMatch(ids, names, options.RequireAlchable), cancellationToken);
				if (target == null)
				{
					ExecutorLog.Write("Alch", context, "break: no clickable target found after keybind");
					break;
				}
				if (!await GameLane.Run(() => ClickAlchTarget(target, options.InventoryRoot, options.ItemAction, options.ItemOffset), cancellationToken))
				{
					ExecutorLog.Write("Alch", context, $"break: target click failed id={target.Id} slot={target.Slot}");
					break;
				}

				targetClickedAt = Environment.TickCount64;
			}

			var itemDisappeared = options.RecastOnDisappear &&
				await WaitForCountBelow(ids, names, options.RequireAlchable, before, options.DisappearTimeoutMs, cancellationToken);

			if (!itemDisappeared)
			{
				var animated = await WaitForAnimationStart(options.StartTimeoutMs, cancellationToken);
				if (animated)
					await WaitForAnimationEnd(options.FinishTimeoutMs, cancellationToken);
				else
					await Task.Delay(Math.Min(options.FinishTimeoutMs, 1200), cancellationToken);
				ExecutorLog.Write("Alch", context, $"cast #{casts + 1}: itemDisappeared=false animationStarted={animated}");
			}

			casts++;

			if (options.ClickInventoryTarget && !itemDisappeared)
			{
				var elapsedAfterTarget = Math.Max(0, Environment.TickCount64 - targetClickedAt);
				var remainingPostTargetDelay = options.PostTargetDelayMs - elapsedAfterTarget;
				if (remainingPostTargetDelay > 0)
					await Task.Delay((int)remainingPostTargetDelay, cancellationToken);
			}

			var after = await GameLane.Run(() => CountMatches(ids, names, options.RequireAlchable), cancellationToken);
			if (after >= before)
			{
				consecutiveFailedCasts++;
				ExecutorLog.Write("Alch", context, $"cast #{casts} did not reduce count (before={before} after={after}); consecutive failures={consecutiveFailedCasts}/{MaxConsecutiveFailedCasts}");
				if (consecutiveFailedCasts >= MaxConsecutiveFailedCasts)
				{
					ExecutorLog.Write("Alch", context, "break: too many consecutive failed casts");
					break;
				}
			}
			else
			{
				consecutiveFailedCasts = 0;
			}

			if (options.BetweenCastsMs > 0)
				await Task.Delay(options.BetweenCastsMs, cancellationToken);
		}

		var remaining = await GameLane.Run(() => CountMatches(ids, names, options.RequireAlchable), cancellationToken);
		var ok = options.Plan.IsSatisfied(casts, remaining == 0);
		ExecutorLog.Write("Alch", context, $"done casts={casts} remaining={remaining} ok={ok}");
		if (ok)
			return NodeExecutionResult.Success();

		// Retry (engine-bounded) instead of Fail: an exit with items left is usually transient
		// (swallowed keybind, lag spike). Genuinely stuck states (e.g. out of nature runes) still
		// reach the fail edge once the engine's retry cap degrades this to a failure.
		return NodeExecutionResult.Retry();
	}

	/// <summary>All tunables for the alch loop, parsed once so the loop body reads clean.</summary>
	private sealed record AlchOptions(
		QuantityPlan Plan,
		bool RequireAlchable,
		bool ClickInventoryTarget,
		bool RecastOnDisappear,
		int TargetDelayMs,
		int DisappearTimeoutMs,
		int PostTargetDelayMs,
		int StartTimeoutMs,
		int FinishTimeoutMs,
		int BetweenCastsMs,
		int InventoryRoot,
		int ItemAction,
		int ItemOffset)
	{
		public static AlchOptions Parse(IReadOnlyDictionary<string, object?> parameters)
		{
			var targetMode = (ParameterHelper.ToString(parameters, "targetMode") ?? "KeybindThenItem").Trim();
			var recastMode = (ParameterHelper.ToString(parameters, "recastMode") ?? "ItemDisappears").Trim();
			var clickInventoryTarget = !string.Equals(targetMode, "KeybindOnly", StringComparison.OrdinalIgnoreCase);

			return new AlchOptions(
				Plan: QuantityPlan.Parse(parameters, "All"),
				RequireAlchable: ParameterHelper.ToBool(parameters, "requireAlchable", true),
				ClickInventoryTarget: clickInventoryTarget,
				RecastOnDisappear: clickInventoryTarget &&
					string.Equals(recastMode, "ItemDisappears", StringComparison.OrdinalIgnoreCase),
				TargetDelayMs: Math.Max(0, ParameterHelper.ToInt(parameters, "targetDelayMs") ?? 1000),
				DisappearTimeoutMs: Math.Max(PollMs, ParameterHelper.ToInt(parameters, "disappearTimeoutMs") ?? 3500),
				PostTargetDelayMs: Math.Max(0, ParameterHelper.ToInt(parameters, "postTargetDelayMs") ?? 2500),
				StartTimeoutMs: Math.Max(PollMs, ParameterHelper.ToInt(parameters, "startTimeoutMs") ?? 1500),
				FinishTimeoutMs: Math.Max(PollMs, ParameterHelper.ToInt(parameters, "finishTimeoutMs") ?? 5000),
				BetweenCastsMs: Math.Max(0, ParameterHelper.ToInt(parameters, "betweenCastsMs") ?? 250),
				InventoryRoot: ParameterHelper.ToInt(parameters, "inventoryRoot") ?? 0,
				ItemAction: ParameterHelper.ToInt(parameters, "itemAction") ?? 110,
				ItemOffset: ParameterHelper.ToInt(parameters, "itemOffset") ?? Objects.Offsets.GeneralInterfaceRoute1);
		}
	}

	private static ulong CountMatches(List<int> ids, List<string> names, bool requireAlchable)
	{
		return Inventory.GetAll()
			.Where(item => (!requireAlchable || item.IsAlchable) && ExecutorHelpers.MatchesTarget(item, ids, names))
			.Aggregate(0UL, (sum, item) => sum + Math.Max(1UL, item.Amount));
	}

	private static Inventory.Item? FindFirstMatch(List<int> ids, List<string> names, bool requireAlchable)
	{
		return Inventory.GetAll()
			.FirstOrDefault(item => (!requireAlchable || item.IsAlchable) && ExecutorHelpers.MatchesTarget(item, ids, names));
	}

	private static bool ClickAlchTarget(Inventory.Item item, int configuredRoot, int itemAction, int itemOffset)
	{
		var root = configuredRoot > 0 ? configuredRoot : ResolveInventoryRoot();
		if (root <= 0)
			return false;

		return Interfaces.DoAction(itemAction, item.Id, 1, root, 5, item.Slot, itemOffset);
	}

	private static Task<bool> WaitForCountBelow(
		List<int> ids,
		List<string> names,
		bool requireAlchable,
		ulong previousCount,
		int timeoutMs,
		CancellationToken cancellationToken)
	{
		// Each count read is gated; the wait between reads is off the lane.
		return GameLane.PollUntil(
			() => CountMatches(ids, names, requireAlchable) < previousCount,
			timeoutMs,
			cancellationToken,
			PollMs);
	}

	private static int ResolveInventoryRoot()
	{
		try
		{
			var inv = InventoryInterfaces.ResolveInventoryRoot();
			if (inv.Id1 > 0)
				return inv.Id1;
		}
		catch
		{
			// Fall back to the captured modern inventory root below.
		}

		return 1473;
	}

	private static Task<bool> WaitForAnimationStart(int timeoutMs, CancellationToken cancellationToken)
		=> GameLane.PollUntil(() => !IsIdle(LocalPlayer.GetAnimation()), timeoutMs, cancellationToken, PollMs);

	private static Task WaitForAnimationEnd(int timeoutMs, CancellationToken cancellationToken)
		=> GameLane.PollUntil(() => IsIdle(LocalPlayer.GetAnimation()), timeoutMs, cancellationToken, PollMs);
}

