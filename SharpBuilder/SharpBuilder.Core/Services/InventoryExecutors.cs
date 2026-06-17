using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;

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

		var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
		{
			["inventory.contains"] = result
		};
		var status = result == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
		return Task.FromResult(new NodeExecutionResult(status, outputs));
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

internal sealed class InventoryItemsActionExecutor : INodeExecutor
{
	private readonly string _verb;
	private readonly int _paceMs;

	public InventoryItemsActionExecutor(string verb, int paceMs)
	{
		_verb = verb;
		_paceMs = paceMs;
	}

	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "items");
		if (ids.Count == 0 && names.Count == 0)
			return NodeExecutionResult.Fail();

		var quantity = (ParameterHelper.ToString(context.Parameters, "quantity") ?? "One").Trim().ToLowerInvariant();
		var requested = Math.Max(1, ParameterHelper.ToInt(context.Parameters, "count") ?? 1);
		var limit = quantity switch
		{
			"all" => int.MaxValue,
			"some" => requested,
			_ => 1
		};

		var performed = 0;
		while (performed < limit)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var matches = FindMatches(ids, names);
			if (matches.Count == 0)
				break;

			var before = TotalAmount(matches);
			if (!Act(matches[0].Id))
				break;

			performed++;
			if (performed >= limit)
				break;

			await Task.Delay(_paceMs, cancellationToken);

			if (TotalAmount(FindMatches(ids, names)) >= before)
				break;
		}

		var ok = quantity switch
		{
			"all" => FindMatches(ids, names).Count == 0,
			"some" => performed >= limit,
			_ => performed >= 1
		};

		return ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
	}

	private static List<Inventory.Item> FindMatches(List<int> ids, List<string> names)
		=> Inventory.GetAll()
			.Where(i => i.Id > 0 &&
				(ids.Contains(i.Id) ||
				 names.Any(n => i.Name?.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)))
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

		var ok = false;
		foreach (var id in ids)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Inventory.Contains(id))
				continue;

			ok = Inventory.DoAction(id, menuIndex, offset);
			if (ok)
				break;
		}

		if (!ok)
		{
			foreach (var name in names)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!Inventory.Contains(name))
					continue;

				ok = Inventory.DoAction(name, menuIndex, offset);
				if (ok)
					break;
			}
		}

		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class InventoryEquipExecutor : INodeExecutor
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
			if (!Inventory.Contains(id))
				continue;
			anyFound = true;
			allOk &= Inventory.Equip(id);
			await Task.Delay(600, cancellationToken);
		}

		foreach (var name in names)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Inventory.Contains(name))
				continue;
			anyFound = true;
			allOk &= Inventory.Equip(name);
			await Task.Delay(600, cancellationToken);
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

		var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
		{
			["equipment.contains"] = worn
		};
		var status = worn == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
		return Task.FromResult(new NodeExecutionResult(status, outputs));
	}
}

internal sealed class EquipmentUnequipExecutor : INodeExecutor
{
	public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "items");
		if (ids.Count == 0 && names.Count == 0)
			return NodeExecutionResult.Fail();

		foreach (var id in ids.Where(Equipment.ContainsById))
		{
			cancellationToken.ThrowIfCancellationRequested();
			Equipment.UnequipById(id);
			await Task.Delay(600, cancellationToken);
		}

		foreach (var name in names.Where(Equipment.ContainsByName))
		{
			cancellationToken.ThrowIfCancellationRequested();
			Equipment.UnequipByName(name);
			await Task.Delay(600, cancellationToken);
		}

		var clear = ids.All(id => !Equipment.ContainsById(id)) && names.All(n => !Equipment.ContainsByName(n));
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

internal sealed class BankOpenExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var success = Bank.IsOpen || Bank.Open();
		return Task.FromResult(success ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class BankDepositAllExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var exceptIds = ParameterHelper.ToIntList(context.Parameters, "exceptIds");
		var exceptNames = ParameterHelper.ToStringList(context.Parameters, "exceptNames");

		bool ok;
		if (exceptIds.Count > 0)
			ok = Bank.DepositAllExcept(exceptIds.ToArray());
		else if (exceptNames.Count > 0)
			ok = Bank.DepositAllExcept(exceptNames.ToArray());
		else
			ok = Bank.DepositAll();

		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
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

internal sealed class BankCloseExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		Bank.Close();
		return Task.FromResult(NodeExecutionResult.Success());
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
