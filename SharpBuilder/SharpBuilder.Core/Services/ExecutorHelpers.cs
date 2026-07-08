using System;
using System.Collections.Generic;
using System.Threading;
using MESharp.API;

namespace SharpBuilder.Core.Services;

/// <summary>
/// Shared building blocks for node executors: condition outcomes, quantity parsing, and the
/// "try ids first, then names" target dispatch used by interaction/inventory nodes.
/// </summary>
internal static class ExecutorHelpers
{
	/// <summary>
	/// Publishes <paramref name="actual"/> under <paramref name="signalKey"/> and maps the
	/// comparison against <paramref name="expected"/> to Success/Fail — the shape shared by
	/// every condition-style node.
	/// </summary>
	public static NodeExecutionResult ConditionOutcome(string signalKey, bool actual, bool expected)
	{
		var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
		{
			[signalKey] = actual
		};
		var status = actual == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
		return new NodeExecutionResult(status, outputs);
	}

	/// <summary>Reads the node's "signal" parameter, falling back to a default key when blank.</summary>
	public static string ResolveSignalKey(IReadOnlyDictionary<string, object?> parameters, string fallback)
	{
		var signal = ParameterHelper.ToString(parameters, "signal");
		return string.IsNullOrWhiteSpace(signal) ? fallback : signal.Trim();
	}

	/// <summary>Tries the id-array dispatch first, then the name-array dispatch, skipping empty lists.</summary>
	public static bool TryIdsThenNames(
		List<int> ids,
		List<string> names,
		Func<int[], bool> byIds,
		Func<string[], bool> byNames)
	{
		var ok = ids.Count > 0 && byIds(ids.ToArray());
		if (!ok && names.Count > 0)
			ok = byNames(names.ToArray());
		return ok;
	}

	/// <summary>
	/// Runs an inventory menu action on the first listed item (by id, then by name) that is
	/// actually present in the inventory.
	/// </summary>
	public static bool InventoryDoActionOnFirst(
		List<int> ids,
		List<string> names,
		int menuIndex,
		int offset,
		CancellationToken cancellationToken)
	{
		foreach (var id in ids)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Inventory.Contains(id))
				continue;

			if (Inventory.DoAction(id, menuIndex, offset))
				return true;
		}

		foreach (var name in names)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Inventory.Contains(name))
				continue;

			if (Inventory.DoAction(name, menuIndex, offset))
				return true;
		}

		return false;
	}
}

/// <summary>
/// Parsed "quantity"/"count" pair ("One" | "Some" | "All") shared by the repeat-until-done
/// inventory executors, including the completion rule for each mode.
/// </summary>
internal readonly struct QuantityPlan
{
	private QuantityPlan(string quantity, int limit)
	{
		Quantity = quantity;
		Limit = limit;
	}

	/// <summary>Normalized quantity token: "one", "some", or "all".</summary>
	public string Quantity { get; }

	/// <summary>Maximum number of actions to perform (int.MaxValue for "all").</summary>
	public int Limit { get; }

	public static QuantityPlan Parse(IReadOnlyDictionary<string, object?> parameters, string defaultQuantity)
	{
		var quantity = (ParameterHelper.ToString(parameters, "quantity") ?? defaultQuantity).Trim().ToLowerInvariant();
		var requested = Math.Max(1, ParameterHelper.ToInt(parameters, "count") ?? 1);
		var limit = quantity switch
		{
			"all" => int.MaxValue,
			"some" => requested,
			_ => 1
		};
		return new QuantityPlan(quantity, limit);
	}

	/// <summary>"all" succeeds when nothing matching remains; "some"/"one" when enough actions ran.</summary>
	public bool IsSatisfied(int performed, bool noneRemaining) => Quantity switch
	{
		"all" => noneRemaining,
		"some" => performed >= Limit,
		_ => performed >= 1
	};
}
