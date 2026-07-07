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
			MakeXExecutorLog.Write(context, $"begin slot={(slot?.ToString() ?? "(already selected)")} category=\"{category ?? string.Empty}\" waitComplete={waitComplete}");

			var initiallyOpen = await GameLane.Run(() => MakeX.IsOpen(), cancellationToken);
			MakeXExecutorLog.Write(context, $"isOpen(before)={initiallyOpen}");
			if (!initiallyOpen)
			{
				var opened = await GameLane.PollUntil(() => MakeX.IsOpen(), 5000, cancellationToken);
				MakeXExecutorLog.Write(context, $"waitForOpen(5000)={opened}");
				if (!opened)
				{
					return NodeExecutionResult.Fail();
				}
			}

			if (!string.IsNullOrWhiteSpace(category))
			{
				MakeXExecutorLog.Write(context, $"selectCategory(\"{category}\")");
				var categorySelected = await GameLane.Run(() => MakeX.SelectCategory(category), cancellationToken);
				MakeXExecutorLog.Write(context, $"selectCategory result={categorySelected}");
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
				MakeXExecutorLog.Write(context, $"selectSlot({slot.Value})={selected}");
				if (!selected)
				{
					return NodeExecutionResult.Fail();
				}

				await Task.Delay(250, cancellationToken);
			}

			MakeXExecutorLog.Write(context, "craft() clicking make button at preset amount");
			var (ok, openAfter, craftingAfter) = await GameLane.Run(() =>
			{
				var clicked = MakeX.Craft();
				return (clicked, MakeX.IsOpen(), MakeX.IsCrafting());
			}, cancellationToken);
			MakeXExecutorLog.Write(context, $"craft()={ok} isOpen(afterCraftClick)={openAfter} isCrafting={craftingAfter}");

			if (ok && waitComplete)
			{
				// Give the make a moment to actually start so the poll doesn't see the progress
				// window (1251) as not-yet-open and return immediately.
				await Task.Delay(800, cancellationToken);
				var complete = await GameLane.PollUntil(() => !MakeX.IsCrafting(), 60000, cancellationToken);
				var (openNow, craftingNow) = await GameLane.Run(() => (MakeX.IsOpen(), MakeX.IsCrafting()), cancellationToken);
				MakeXExecutorLog.Write(context, $"waitForCraftComplete={complete} isOpen={openNow} isCrafting={craftingNow}");
				ok = complete;
			}

			return ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			MakeXExecutorLog.Write(context, $"exception: {ex.GetType().Name}: {ex.Message}");
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
		MakeXExecutorLog.Write(context, $"isOpen={isOpen} expected={expected}");

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
			MakeXExecutorLog.Write(context, "missing required item parameter");
			return Task.FromResult(NodeExecutionResult.Fail());
		}

		var rows = MakeX.GetItems();
		MakeXExecutorLog.Write(context, $"visibleRows={rows.Count} {MakeXExecutorLog.FormatRows(rows)}");

		var ok = int.TryParse(item, out var id) ? MakeX.SelectItem(id) : MakeX.SelectItem(item);
		MakeXExecutorLog.Write(context, $"selectItem(\"{item}\")={ok}");
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
			MakeXExecutorLog.Write(context, "missing required category parameter");
			return Task.FromResult(NodeExecutionResult.Fail());
		}

		var ok = MakeX.SelectCategory(category);
		MakeXExecutorLog.Write(context, $"selectCategory(\"{category}\")={ok}");
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
			MakeXExecutorLog.Write(context, $"invalid amount={amount?.ToString() ?? "null"}");
			return Task.FromResult(NodeExecutionResult.Fail());
		}

		var ok = MakeX.SetAmount(amount.Value);
		MakeXExecutorLog.Write(context, $"setAmount({amount.Value})={ok}");
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
		MakeXExecutorLog.Write(context, $"craft({amount?.ToString() ?? "null"})={ok} isOpen(afterCraftClick)={openAfter} isCrafting={craftingAfter}");
		if (ok && waitComplete)
		{
			var complete = await GameLane.PollUntil(() => !MakeX.IsCrafting(), 60000, cancellationToken);
			var (openNow, craftingNow) = await GameLane.Run(() => (MakeX.IsOpen(), MakeX.IsCrafting()), cancellationToken);
			MakeXExecutorLog.Write(context, $"waitForCraftComplete={complete} isOpen={openNow} isCrafting={craftingNow}");
			ok = complete;
		}

		return ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
	}
}

internal sealed class MakeXWaitCompleteExecutor : INodeExecutor, IGameApiSelfManaged
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var timeoutMs = ParameterHelper.ToInt(context.Parameters, "timeoutMs") ?? 60000;
		var done = await GameLane.PollUntil(() => !MakeX.IsCrafting(), timeoutMs, cancellationToken);
		var (openNow, craftingNow) = await GameLane.Run(() => (MakeX.IsOpen(), MakeX.IsCrafting()), cancellationToken);
		MakeXExecutorLog.Write(context, $"waitForCraftComplete({timeoutMs})={done} isOpen={openNow} isCrafting={craftingNow}");
		return done ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
	}
}

internal static class MakeXExecutorLog
{
	public static void Write(NodeExecutionContext context, string message)
	{
		Console.WriteLine($"[SharpBuilder.MakeX] {context.Node.Title}: {message}");
	}

	public static string FormatRows(IReadOnlyList<MakeXItem> rows)
	{
		if (rows.Count == 0)
			return "[]";

		return "[" + string.Join("; ", rows.Take(12).Select(row =>
			$"{row.ItemId}:{row.Name}@{row.Component.Id1}/{row.Component.Id2}/{row.Component.Id3}")) +
			(rows.Count > 12 ? "; ..." : string.Empty) + "]";
	}
}
