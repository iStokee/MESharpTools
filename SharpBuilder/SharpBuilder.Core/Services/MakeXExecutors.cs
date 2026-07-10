using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;

namespace SharpBuilder.Core.Services;

// Self-managed: gates each native op individually and polls the craft-wait with the sleeps OUTSIDE
// the game-API lane, so the dashboard keeps reading XP/items while a long make runs.
internal sealed class MakeXMakeItemExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var slot = ParameterHelper.ToInt(context.Parameters, "slot");
		var category = ParameterHelper.ToString(context.Parameters, "category");
		var waitComplete = ParameterHelper.ToBool(context.Parameters, "waitComplete", true);

		try
		{
			ExecutorLog.Write("MakeX", context, $"begin slot={(slot?.ToString() ?? "(already selected)")} category=\"{category ?? string.Empty}\" waitComplete={waitComplete}");

			var initiallyOpen = await GameLane.Run(() => MakeX.IsOpen(), cancellationToken);
			ExecutorLog.Write("MakeX", context, $"isOpen(before)={initiallyOpen}");
			if (!initiallyOpen)
			{
				// 10s: the opening click from the previous node may still be walking the player to
				// the portable/booth before the interface can appear.
				var opened = await GameLane.PollUntil(() => MakeX.IsOpen(), 10000, cancellationToken);
				ExecutorLog.Write("MakeX", context, $"waitForOpen(10000)={opened}");
				if (!opened)
				{
					return NodeExecutionResult.Fail();
				}
			}

			if (!string.IsNullOrWhiteSpace(category))
			{
				ExecutorLog.Write("MakeX", context, $"selectCategory(\"{category}\")");
				var categorySelected = await GameLane.Run(() => MakeX.SelectCategory(category), cancellationToken);
				ExecutorLog.Write("MakeX", context, $"selectCategory result={categorySelected}");
				await Task.Delay(250, cancellationToken);
				if (!categorySelected)
				{
					return NodeExecutionResult.Fail();
				}
			}

			// The product grid exposes no item ids/text (verified in-game), so selection is by grid
			// slot index. With no slot, craft whatever is already selected (e.g. portable crafter default).
			if (slot.HasValue)
			{
				var selected = await GameLane.Run(() => MakeX.SelectSlot(slot.Value), cancellationToken);
				ExecutorLog.Write("MakeX", context, $"selectSlot({slot.Value})={selected}");
				if (!selected)
				{
					return NodeExecutionResult.Fail();
				}

				await Task.Delay(250, cancellationToken);
			}

			ExecutorLog.Write("MakeX", context, "craft() clicking make button at preset amount");
			var (ok, openAfter, craftingAfter) = await GameLane.Run(() =>
			{
				var clicked = MakeX.Craft();
				return (clicked, MakeX.IsOpen(), MakeX.IsCrafting());
			}, cancellationToken);
			ExecutorLog.Write("MakeX", context, $"craft()={ok} isOpen(afterCraftClick)={openAfter} isCrafting={craftingAfter}");

			if (ok && waitComplete)
			{
				ok = await MakeXCraftWait.WaitForCraftAndIdle(context, cancellationToken);
			}

			return ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			ExecutorLog.Write("MakeX", context, $"exception: {ex.GetType().Name}: {ex.Message}");
			return NodeExecutionResult.Fail();
		}
	}
}

internal sealed class MakeXIsOpenExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
		var isOpen = MakeX.IsOpen();
		ExecutorLog.Write("MakeX", context, $"isOpen={isOpen} expected={expected}");

		var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
		{
			["makex.isOpen"] = isOpen
		};

		return Task.FromResult(isOpen == expected
			? NodeExecutionResult.Success(outputs)
			: NodeExecutionResult.Fail(outputs));
	}
}

internal sealed class MakeXSelectItemExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var item = ParameterHelper.ToString(context.Parameters, "item");
		if (string.IsNullOrWhiteSpace(item))
		{
			ExecutorLog.Write("MakeX", context, "missing required item parameter");
			return Task.FromResult(NodeExecutionResult.Fail());
		}

		var rows = MakeX.GetItems();
		ExecutorLog.Write("MakeX", context, $"visibleRows={rows.Count} {MakeXExecutorLog.FormatRows(rows)}");

		var ok = int.TryParse(item, out var id) ? MakeX.SelectItem(id) : MakeX.SelectItem(item);
		ExecutorLog.Write("MakeX", context, $"selectItem(\"{item}\")={ok}");
		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class MakeXSelectCategoryExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var category = ParameterHelper.ToString(context.Parameters, "category");
		if (string.IsNullOrWhiteSpace(category))
		{
			ExecutorLog.Write("MakeX", context, "missing required category parameter");
			return Task.FromResult(NodeExecutionResult.Fail());
		}

		var ok = MakeX.SelectCategory(category);
		ExecutorLog.Write("MakeX", context, $"selectCategory(\"{category}\")={ok}");
		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class MakeXSetAmountExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var amount = ParameterHelper.ToInt(context.Parameters, "amount");
		if (!amount.HasValue || amount.Value <= 0)
		{
			ExecutorLog.Write("MakeX", context, $"invalid amount={amount?.ToString() ?? "null"}");
			return Task.FromResult(NodeExecutionResult.Fail());
		}

		var ok = MakeX.SetAmount(amount.Value);
		ExecutorLog.Write("MakeX", context, $"setAmount({amount.Value})={ok}");
		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class MakeXCraftExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var amount = ParameterHelper.ToInt(context.Parameters, "amount");
		var waitComplete = ParameterHelper.ToBool(context.Parameters, "waitComplete", true);

		var (ok, openAfter, craftingAfter) = await GameLane.Run(() =>
		{
			var clicked = MakeX.Craft(amount);
			return (clicked, MakeX.IsOpen(), MakeX.IsCrafting());
		}, cancellationToken);
		ExecutorLog.Write("MakeX", context, $"craft({amount?.ToString() ?? "null"})={ok} isOpen(afterCraftClick)={openAfter} isCrafting={craftingAfter}");
		if (ok && waitComplete)
		{
			ok = await MakeXCraftWait.WaitForCraftAndIdle(context, cancellationToken);
		}

		return ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
	}
}

// Shared craft-completion wait for the make executors. Sleeps stay OFF the game-API lane so the
// dashboard keeps reading XP/items while a long make runs.
internal static class MakeXCraftWait
{
	public static async Task<bool> WaitForCraftAndIdle(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		// Wait for the make to actually start (progress window 1251) instead of a fixed delay, so a
		// slow start can't make the completion poll pass while crafting is still pending.
		var started = await GameLane.PollUntil(() => MakeX.IsCrafting(), 2000, cancellationToken);

		var complete = await GameLane.PollUntil(() => !MakeX.IsCrafting(), 60000, cancellationToken);

		// The progress window closes before the final craft animation finishes; downstream nodes
		// (e.g. high alch) get their input swallowed if they act mid-animation. Idle = animation <= 0
		// in this client build. Best-effort: an idle timeout does not fail the node.
		var idle = await GameLane.PollUntil(() => LocalPlayer.GetAnimation() <= 0, 5000, cancellationToken);

		var (openNow, craftingNow) = await GameLane.Run(() => (MakeX.IsOpen(), MakeX.IsCrafting()), cancellationToken);
		ExecutorLog.Write("MakeX", context, $"craftStarted={started} waitForCraftComplete={complete} playerIdle={idle} isOpen={openNow} isCrafting={craftingNow}");
		return complete;
	}
}

internal sealed class MakeXWaitCompleteExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var timeoutMs = ParameterHelper.ToInt(context.Parameters, "timeoutMs") ?? 60000;
		var done = await GameLane.PollUntil(() => !MakeX.IsCrafting(), timeoutMs, cancellationToken);
		var (openNow, craftingNow) = await GameLane.Run(() => (MakeX.IsOpen(), MakeX.IsCrafting()), cancellationToken);
		ExecutorLog.Write("MakeX", context, $"waitForCraftComplete({timeoutMs})={done} isOpen={openNow} isCrafting={craftingNow}");
		return done ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
	}
}

internal static class MakeXExecutorLog
{
	public static string FormatRows(IReadOnlyList<MakeXItem> rows)
	{
		if (rows.Count == 0)
			return "[]";

		return "[" + string.Join("; ", rows.Take(12).Select(row =>
			$"{row.ItemId}:{row.Name}@{row.Component.Id1}/{row.Component.Id2}/{row.Component.Id3}")) +
			(rows.Count > 12 ? "; ..." : string.Empty) + "]";
	}
}
