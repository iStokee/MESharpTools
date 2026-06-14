using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;
using MESharp.API.Input;
using SharpBuilder.Core.Models;
using TraversalApi = MESharp.API.Traversal;

namespace SharpBuilder.Core.Services;

public enum NodeExecutionStatus
{
	Success,
	Fail,
	Retry
}

public record NodeExecutionResult(NodeExecutionStatus Status, IReadOnlyDictionary<string, bool>? Outputs = null)
{
	public static NodeExecutionResult Success(IDictionary<string, bool>? outputs = null)
		=> new(NodeExecutionStatus.Success, outputs != null ? new Dictionary<string, bool>(outputs) : null);

	public static NodeExecutionResult Fail(IDictionary<string, bool>? outputs = null)
		=> new(NodeExecutionStatus.Fail, outputs != null ? new Dictionary<string, bool>(outputs) : null);

	public static NodeExecutionResult Retry(IDictionary<string, bool>? outputs = null)
		=> new(NodeExecutionStatus.Retry, outputs != null ? new Dictionary<string, bool>(outputs) : null);
}

public class NodeExecutionContext
{
	public NodeExecutionContext(
		NodeModel node,
		NodeDefinition definition,
		IReadOnlyDictionary<string, bool> signals,
		IReadOnlyDictionary<string, object?> parameters)
	{
		Node = node ?? throw new ArgumentNullException(nameof(node));
		Definition = definition ?? throw new ArgumentNullException(nameof(definition));
		Signals = signals ?? throw new ArgumentNullException(nameof(signals));
		Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
	}

	public NodeModel Node { get; }
	public NodeDefinition Definition { get; }
	public IReadOnlyDictionary<string, bool> Signals { get; }
	public IReadOnlyDictionary<string, object?> Parameters { get; }
}

public interface INodeExecutor
{
	Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Registry of executors keyed by definition id, with a safe default that respects dwell delays.
/// </summary>
public class NodeExecutorRegistry
{
	private readonly Dictionary<string, INodeExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);
	private readonly INodeExecutor _default;

	public NodeExecutorRegistry()
	{
		_default = new DwellOnlyExecutor();
		Register(NodeCatalogDefaults.GenericActionId, _default);
		Register(NodeCatalogDefaults.StartId, new NoOpExecutor());
		Register(NodeCatalogDefaults.TerminalId, new NoOpExecutor());
		Register(NodeCatalogDefaults.BooleanConditionId, new BooleanConditionExecutor());
		Register(NodeCatalogDefaults.ScriptDashboardId, new NoOpExecutor());

		// Core actions
		Register("actions.interaction", new GenericInteractionExecutor());
		Register("actions.shop", new NotImplementedExecutor("Shop"));
		Register("actions.setSignal", new SetSignalExecutor());

		// Traversal / timing
		Register("traversal.worldhop", new NotImplementedExecutor("World hop"));
		Register("traversal.walk", new WalkExecutor());
		Register("traversal.teleportLodestone", new LodestoneTeleportExecutor());
		Register("traversal.wait", new WaitExecutor());
		Register("traversal.waitRange", new WaitRangeExecutor());

		// Input
		Register("keyboard.send", new KeyboardSendExecutor());
		Register("input.click", new NotImplementedExecutor("Mouse click"));

		// Inventory / equipment / bank
		Register("inventory.contains", new InventoryContainsExecutor());
		Register("inventory.count", new InventoryCountExecutor());
			Register("inventory.drop", new InventoryItemsActionExecutor("drop", paceMs: 600));
			Register("inventory.eat", new InventoryItemsActionExecutor("eat", paceMs: 1800));
			Register("inventory.equip", new InventoryEquipExecutor());
			Register("inventory.use", new InventoryUseExecutor());
			Register("inventory.action", new InventoryMenuActionExecutor());
			Register("inventory.useOn", new InventoryUseOnExecutor());
			Register("inventory.note", new InventoryItemsActionExecutor("note", paceMs: 900));
		Register("equipment.contains", new EquipmentContainsExecutor());
		Register("equipment.unequip", new EquipmentUnequipExecutor());
		Register("bank.open", new BankOpenExecutor());
		Register("bank.depositAll", new BankDepositAllExecutor());
		Register("bank.withdraw", new BankWithdrawExecutor());
		Register("bank.close", new BankCloseExecutor());

		// NPCs / objects / loot
		Register("npcs.interact", new NpcInteractExecutor());
		Register("npcs.find", new NpcFindExecutor());
		Register("npcs.attack", new NpcAttackExecutor());
		Register("objects.interact", new ObjectInteractExecutor());
		Register("objects.interactHighlighted", new ObjectInteractHighlightedExecutor());
		Register("objects.find", new ObjectFindExecutor());
		Register("objects.exists", new ObjectExistsExecutor());
		Register("loot.pickup", new LootPickupExecutor());

		// Conditions / skills
			Register("conditions.locationRadius", new LocationRadiusExecutor());
			Register("conditions.healthPercent", new PercentResourceExecutor("health", () => LocalPlayer.GetHealthPercent()));
			Register("conditions.prayerPercent", new PercentResourceExecutor("prayer", () => LocalPlayer.GetPrayerPercent()));
			Register("conditions.cooldown", new CooldownExecutor());
			Register("conditions.inCombat", new InCombatExecutor());
		Register("conditions.inventoryFull", new InventoryFullExecutor());
		Register("skills.requireLevel", new SkillRequirementExecutor());
		Register("combat.quickHeal", new QuickHealExecutor());
			Register("combat.quickPrayer", new QuickPrayerExecutor());
			Register("familiar.check", new FamiliarCheckExecutor());
			Register("familiar.action", new FamiliarActionExecutor());
			Register("familiar.summon", new FamiliarSummonExecutor());

		// Trade
		Register("trade.accept", new NotImplementedExecutor("Trade accept"));
	}

	public void Register(string definitionId, INodeExecutor executor)
	{
		if (string.IsNullOrWhiteSpace(definitionId)) throw new ArgumentNullException(nameof(definitionId));
		_executors[definitionId] = executor ?? throw new ArgumentNullException(nameof(executor));
	}

	public INodeExecutor Resolve(string? definitionId)
	{
		if (!string.IsNullOrWhiteSpace(definitionId) && _executors.TryGetValue(definitionId, out var executor))
		{
			return executor;
		}

		return _default;
	}

	// Dwell delays are applied by the engine after each node, so executors stay delay-free.
	private sealed class DwellOnlyExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			return Task.FromResult(NodeExecutionResult.Success());
		}
	}

	private sealed class NoOpExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			return Task.FromResult(NodeExecutionResult.Success());
		}
	}

	private sealed class NotImplementedExecutor : INodeExecutor
	{
		private readonly string _feature;

		public NotImplementedExecutor(string feature) => _feature = feature;

		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			Console.WriteLine($"[Executor] {_feature} ({context.Definition.Id}) is not implemented yet; reporting failure so OnFail/fallback edges can route around it.");
			return Task.FromResult(NodeExecutionResult.Fail());
		}
	}

	private sealed class BooleanConditionExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var parameters = context.Parameters;
			parameters.TryGetValue("signal", out var signalKeyObj);
			parameters.TryGetValue("expected", out var expectedObj);

			var signalKey = signalKeyObj?.ToString() ?? string.Empty;
			var expectsTrue = expectedObj is bool b ? b : true;

			var actual = false;
			if (!string.IsNullOrWhiteSpace(signalKey))
			{
				context.Signals.TryGetValue(signalKey, out actual);
			}

			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			if (!string.IsNullOrWhiteSpace(signalKey))
			{
				outputs[signalKey] = actual;
			}

			var status = actual == expectsTrue ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class SetSignalExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			context.Parameters.TryGetValue("signal", out var signalKeyObj);
			context.Parameters.TryGetValue("value", out var valueObj);

			var key = signalKeyObj?.ToString() ?? string.Empty;
			var value = valueObj is bool b ? b : true;

			if (string.IsNullOrWhiteSpace(key))
			{
				return Task.FromResult(NodeExecutionResult.Fail());
			}

			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				[key] = value
			};

			return Task.FromResult(NodeExecutionResult.Success(outputs));
		}
	}

	private sealed class InventoryContainsExecutor : INodeExecutor
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

	private sealed class InventoryCountExecutor : INodeExecutor
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

	/// <summary>
	/// Script-style item action over a mixed list of names/ids: drops or eats matching
	/// inventory items honoring a One / Some (count) / All quantity mode.
	/// </summary>
	private sealed class InventoryItemsActionExecutor : INodeExecutor
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

				// The action may report success without taking effect; stop instead of spinning.
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

		// Stack-aware progress metric: eating one dose from a stack changes Amount, not slot count.
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

	private sealed class InventoryMenuActionExecutor : INodeExecutor
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

	private sealed class InventoryEquipExecutor : INodeExecutor
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

	private sealed class EquipmentContainsExecutor : INodeExecutor
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

	private sealed class EquipmentUnequipExecutor : INodeExecutor
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

			// Postcondition: success only when none of the listed items remain worn
			// (unequip needs free inventory space, so this can legitimately fail).
			var clear = ids.All(id => !Equipment.ContainsById(id)) && names.All(n => !Equipment.ContainsByName(n));
			return clear ? NodeExecutionResult.Success() : NodeExecutionResult.Fail();
		}
	}

	private sealed class InventoryUseExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var action = (ParameterHelper.ToString(context.Parameters, "action") ?? "Use").ToLowerInvariant();

			if (!id.HasValue && string.IsNullOrWhiteSpace(name))
				return Task.FromResult(NodeExecutionResult.Fail());

			// The guard above ensures name is non-empty whenever id is absent.
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

	private sealed class InventoryUseOnExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var fromRaw = ParameterHelper.ToString(context.Parameters, "from");
			var toRaw = ParameterHelper.ToString(context.Parameters, "to");

			if (string.IsNullOrWhiteSpace(fromRaw) || string.IsNullOrWhiteSpace(toRaw))
			{
				return Task.FromResult(NodeExecutionResult.Fail());
			}

			bool ok;
			if (int.TryParse(fromRaw, out var fromId) && int.TryParse(toRaw, out var toId))
				ok = Inventory.UseItemOnItem(fromId, toId);
			else
				ok = Inventory.UseItemOnItem(fromRaw, toRaw);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class BankOpenExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var success = Bank.IsOpen || Bank.Open();
			return Task.FromResult(success ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class BankDepositAllExecutor : INodeExecutor
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

	private sealed class BankWithdrawExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var amount = ParameterHelper.ToInt(context.Parameters, "amount");

			// Without a dedicated API for quantities, use default action/quantity menu slot.
			var actionIndex = amount.HasValue && amount.Value > 1 ? 1 : 0;
			bool ok = false;

			if (id.HasValue)
				ok = Bank.DoActionById(id.Value, actionIndex);
			else if (!string.IsNullOrWhiteSpace(name))
				ok = Bank.DoActionByName(name, actionIndex);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class BankCloseExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			Bank.Close();
			return Task.FromResult(NodeExecutionResult.Success());
		}
	}

	private sealed class NpcInteractExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "target");
			// Keep compatibility with older parameter key but current native API is exact-name action dispatch.
			_ = ParameterHelper.ToBool(context.Parameters, "allowPartial", false) ||
			    ParameterHelper.ToBool(context.Parameters, "acceptPartial", false);

			if (ids.Count == 0 && names.Count == 0)
				return Task.FromResult(NodeExecutionResult.Fail());

			var option = ParameterHelper.ToString(context.Parameters, "option");
			var actionIndex = ParameterHelper.ToInt(context.Parameters, "actionIndex") ?? ResolveActionIndex(option, InteractionKind.Npc);
			var offset = ParameterHelper.ToInt(context.Parameters, "offset") ?? Npcs.InteractNPC_route;
			var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;
			var ignoreStar = ParameterHelper.ToBool(context.Parameters, "ignoreStar", false);
			var minHealth = ParameterHelper.ToInt(context.Parameters, "minHealth") ?? 0;

			var ok = ids.Count > 0 && Npcs.DoActionByIds(ids.ToArray(), actionIndex: actionIndex, offset: offset, maxDistance: maxDistance, ignoreStar: ignoreStar, minHealth: minHealth);
			if (!ok && names.Count > 0)
				ok = Npcs.DoActionByNames(names.ToArray(), actionIndex: actionIndex, offset: offset, maxDistance: maxDistance, ignoreStar: ignoreStar, minHealth: minHealth);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class GenericInteractionExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "target");
			if (ids.Count == 0 && names.Count == 0)
				return Task.FromResult(NodeExecutionResult.Fail());

			var option = ParameterHelper.ToString(context.Parameters, "option");
			var npcAction = ResolveActionIndex(option, InteractionKind.Npc);
			var objectAction = ResolveActionIndex(option, InteractionKind.Object);

			var ok = ids.Count > 0 && Npcs.DoActionByIds(ids.ToArray(), actionIndex: npcAction);
			if (!ok && names.Count > 0)
				ok = Npcs.DoActionByNames(names.ToArray(), actionIndex: npcAction);
			if (!ok && ids.Count > 0)
				ok = Objects.DoActionByIds(ids.ToArray(), actionIndex: objectAction);
			if (!ok && names.Count > 0)
				ok = Objects.DoActionByNames(names.ToArray(), actionIndex: objectAction, maxDistance: int.MaxValue, valid: true);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class NpcFindExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;
			var signal = ParameterHelper.ToString(context.Parameters, "signal");
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);

			var found = Npcs.GetAll().Any(n =>
				(!id.HasValue || n.Id == id.Value) &&
				(string.IsNullOrWhiteSpace(name) || n.Name?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) &&
				n.Distance <= maxDistance);

			var signalKey = string.IsNullOrWhiteSpace(signal) ? "npcs.found" : signal.Trim();
			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				[signalKey] = found
			};

			var status = found == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class NpcAttackExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "name");
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			if (id.HasValue && !ids.Contains(id.Value))
				ids.Add(id.Value);
			// Attack is just a DoAction with the attack opcode/route; expose both so the node is
			// generic across NPCs (0x2a / AttackNPC_route are the captured defaults).
			var actionIndex = ParameterHelper.ToInt(context.Parameters, "actionIndex") ?? Npcs.AttackNpcAction;
			var offset = ParameterHelper.ToInt(context.Parameters, "offset") ?? Npcs.AttackNPC_route;
			var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;

			var ok = ids.Count > 0 && Npcs.DoActionByIds(ids.ToArray(), actionIndex: actionIndex, offset: offset, maxDistance: maxDistance);
			if (!ok && names.Count > 0)
				ok = Npcs.DoActionByNames(names.ToArray(), actionIndex: actionIndex, offset: offset, maxDistance: maxDistance);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class ObjectInteractExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "name");
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			if (id.HasValue && !ids.Contains(id.Value))
				ids.Add(id.Value);
			var option = ParameterHelper.ToString(context.Parameters, "option");
			var actionIndex = ParameterHelper.ToInt(context.Parameters, "actionIndex") ?? ResolveActionIndex(option, InteractionKind.Object);
			var offset = ParameterHelper.ToInt(context.Parameters, "offset") ?? Objects.Offsets.GeneralRoute0;
			var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;
			var valid = ParameterHelper.ToBool(context.Parameters, "valid", false);

			var ok = ids.Count > 0 && Objects.DoActionByIds(ids.ToArray(), actionIndex: actionIndex, offset: offset, maxDistance: maxDistance, valid: valid);
			if (!ok && names.Count > 0)
				ok = Objects.DoActionByNames(names.ToArray(), actionIndex: actionIndex, offset: offset, maxDistance: maxDistance, valid: valid);

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class ObjectInteractHighlightedExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var objectIds = ParameterHelper.ToIntList(context.Parameters, "objectIds");
			var highlightIds = ParameterHelper.ToIntList(context.Parameters, "highlightIds");
			var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;
			var actionIndex = ParameterHelper.ToInt(context.Parameters, "actionIndex") ?? 0;

			if (objectIds.Count == 0 || highlightIds.Count == 0)
			{
				return Task.FromResult(NodeExecutionResult.Fail());
			}

			var ok = Objects.FindHighlighted(objectIds, highlightIds, maxDistance, actionIndex);
			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private enum InteractionKind
	{
		Npc,
		Object
	}

	private static int ResolveActionIndex(string? option, InteractionKind kind)
	{
		if (string.IsNullOrWhiteSpace(option))
			return 0;

		var normalized = option.Trim();
		if (int.TryParse(normalized, out var numericIndex))
		{
			return Math.Max(0, numericIndex);
		}

		return kind switch
		{
			InteractionKind.Npc => normalized.ToLowerInvariant() switch
			{
				"talk" or "talk-to" => 0,
				"trade" => 1,
				"pickpocket" => 2,
				"attack" => 3,
				"use" => 0,
				_ => 0
			},
			InteractionKind.Object => normalized.ToLowerInvariant() switch
			{
				"interact" => 0,
				"use" => 0,
				"open" => 0,
				"climb" => 1,
				"search" => 1,
				"enter" => 1,
				"close" => 2,
				"examine" => 3,
				_ => 0
			},
			_ => 0
		};
	}

	private sealed class ObjectFindExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;
			var signal = ParameterHelper.ToString(context.Parameters, "signal");
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);

			var found = Objects.GetAll().Any(o =>
				(!id.HasValue || o.Id == id.Value) &&
				(string.IsNullOrWhiteSpace(name) || o.Name?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) &&
				o.Distance <= maxDistance);

			var signalKey = string.IsNullOrWhiteSpace(signal) ? "objects.found" : signal.Trim();
			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				[signalKey] = found
			};

			var status = found == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class ObjectExistsExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var name = ParameterHelper.ToString(context.Parameters, "name");
			var id = ParameterHelper.ToInt(context.Parameters, "id");
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);

			var objs = id.HasValue
				? Objects.GetAll().Where(o => o.Id == id.Value)
				: Objects.GetAll().Where(o => string.IsNullOrWhiteSpace(name) || o.Name?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);

			var exists = objs.Any();
			var status = exists == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["objects.exists"] = exists
			};
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class LootPickupExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var names = ParameterHelper.ToStringList(context.Parameters, "names");
			var ids = ParameterHelper.ToIntList(context.Parameters, "ids");
			var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? 20;

			if (ids.Count == 0 && names.Count > 0)
			{
				// Resolve names against current ground loot so we can use the id-based take API.
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

	private sealed class InCombatExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
			var actual = LocalPlayer.IsInCombat() || LocalPlayer.IsInCombatVarbit() || LocalPlayer.IsTargeting();
			var status = actual == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["inCombat"] = actual
			};
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class InventoryFullExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
			var isFull = Inventory.IsFull;
			var status = isFull == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["inventoryFull"] = isFull
			};
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class LocationRadiusExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var centers = ParameterHelper.ToCoordinateList(context.Parameters, "center");
			if (centers.Count == 0)
				return Task.FromResult(NodeExecutionResult.Fail());

			var radius = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "radius") ?? 0);
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
			var signal = ParameterHelper.ToString(context.Parameters, "signal");
			var signalKey = string.IsNullOrWhiteSpace(signal) ? "insideAnchor" : signal.Trim();
			var center = centers[0];
			var distance = LocalPlayer.DistanceTo(center.x, center.y, center.z);
			var inside = distance <= radius;

			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				[signalKey] = inside
			};
			var status = inside == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class PercentResourceExecutor : INodeExecutor
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
			var signal = ParameterHelper.ToString(context.Parameters, "signal");
			var signalKey = string.IsNullOrWhiteSpace(signal) ? $"{_resourceName}.threshold" : signal.Trim();
			var percent = Math.Clamp(_readPercent(), 0, 100);
			var matched = Compare(percent, threshold, comparison);

			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				[signalKey] = matched
			};
			var status = matched == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			return Task.FromResult(new NodeExecutionResult(status, outputs));
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

	private sealed class CooldownExecutor : INodeExecutor
	{
		private readonly Dictionary<Guid, DateTime> _lastConsumedUtcByNode = new();

		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var intervalMs = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "intervalMs") ?? 0);
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
			var startReady = ParameterHelper.ToBool(context.Parameters, "startReady", true);
			var consumeOnReady = ParameterHelper.ToBool(context.Parameters, "consumeOnReady", true);
			var signal = ParameterHelper.ToString(context.Parameters, "signal");
			var signalKey = string.IsNullOrWhiteSpace(signal) ? "cooldownReady" : signal.Trim();

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

			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				[signalKey] = ready
			};
			var status = ready == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class QuickHealExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var ok = ActionButtons.QuickHeal();
			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class QuickPrayerExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var mode = (ParameterHelper.ToString(context.Parameters, "mode") ?? "Toggle").Trim();
			var enabled = LocalPlayer.IsQuickPrayerEnabled();
			var shouldToggle = mode.Equals("Toggle", StringComparison.OrdinalIgnoreCase) ||
				(mode.Equals("Enable", StringComparison.OrdinalIgnoreCase) && !enabled) ||
				(mode.Equals("Disable", StringComparison.OrdinalIgnoreCase) && enabled);

			if (!shouldToggle)
			{
				return Task.FromResult(NodeExecutionResult.Success());
			}

			var ok = ActionButtons.QuickPrayer();
			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class FamiliarCheckExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var expected = ParameterHelper.ToBool(context.Parameters, "expected", true);
			var nameFilter = ParameterHelper.ToString(context.Parameters, "name");
			var minTime = ParameterHelper.ToInt(context.Parameters, "minTimeRemaining");
			var minSpellPoints = ParameterHelper.ToInt(context.Parameters, "minSpellPoints");
			var minHealth = ParameterHelper.ToInt(context.Parameters, "minHealth");
			var signal = ParameterHelper.ToString(context.Parameters, "signal");
			var signalKey = string.IsNullOrWhiteSpace(signal) ? "hasFamiliar" : signal.Trim();

			var matched = Familiar.HasFamiliar();
			if (matched && !string.IsNullOrWhiteSpace(nameFilter))
			{
				matched = Familiar.GetName().IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
			}

			if (matched && minTime.HasValue)
			{
				matched = Familiar.GetTimeRemaining() >= minTime.Value;
			}

			if (matched && minSpellPoints.HasValue)
			{
				matched = Familiar.GetSpellPoints() >= minSpellPoints.Value;
			}

			if (matched && minHealth.HasValue)
			{
				matched = Familiar.GetHealth() >= minHealth.Value;
			}

			var outputs = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				[signalKey] = matched
			};
			var status = matched == expected ? NodeExecutionStatus.Success : NodeExecutionStatus.Fail;
			return Task.FromResult(new NodeExecutionResult(status, outputs));
		}
	}

	private sealed class FamiliarActionExecutor : INodeExecutor
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

	private sealed class FamiliarSummonExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var onlyIfMissing = ParameterHelper.ToBool(context.Parameters, "onlyIfMissing", true);
			if (onlyIfMissing && Familiar.HasFamiliar())
			{
				return Task.FromResult(NodeExecutionResult.Success());
			}

			var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "pouches");
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

	private sealed class LodestoneTeleportExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var destination = ParameterHelper.ToString(context.Parameters, "destination");
			var timeout = ParameterHelper.ToInt(context.Parameters, "timeoutMs") ?? 12000;

			bool ok = false;
			if (!string.IsNullOrWhiteSpace(destination))
			{
				ok = TraversalApi.Lodestone(destination, timeout);
			}

			return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
		}
	}

	private sealed class WalkExecutor : INodeExecutor
	{
		public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			// Try to parse coordinates; support single or multiple waypoints.
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

	private sealed class WaitExecutor : INodeExecutor
	{
		public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var delay = ParameterHelper.ToInt(context.Parameters, "delayMs") ?? 0;
			if (delay > 0)
			{
				await Task.Delay(delay, cancellationToken);
			}
			return NodeExecutionResult.Success();
		}
	}

	private sealed class WaitRangeExecutor : INodeExecutor
	{
		public async Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
		{
			var minDelay = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "minDelayMs") ?? 0);
			var maxDelay = Math.Max(0, ParameterHelper.ToInt(context.Parameters, "maxDelayMs") ?? minDelay);
			if (maxDelay < minDelay)
			{
				(maxDelay, minDelay) = (minDelay, maxDelay);
			}

			var delay = maxDelay == minDelay
				? minDelay
				: Random.Shared.Next(minDelay, maxDelay + 1);

			if (delay > 0)
			{
				await Task.Delay(delay, cancellationToken);
			}

			return NodeExecutionResult.Success();
		}
	}

	private sealed class SkillRequirementExecutor : INodeExecutor
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

	private sealed class KeyboardSendExecutor : INodeExecutor
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

			// Press the chord in order, then release in reverse, so modifiers wrap the main key.
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
			{
				await Task.Delay(delayMs, cancellationToken);
			}

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
				// Letters and digits map straight onto their Win32 virtual-key codes.
				_ when normalized.Length == 1 && normalized[0] is >= 'A' and <= 'Z' => (Keyboard.VirtualKey)normalized[0],
				_ when normalized.Length == 1 && normalized[0] is >= '0' and <= '9' => (Keyboard.VirtualKey)normalized[0],
				// F1-F12 occupy 0x70-0x7B.
				_ when normalized.Length is 2 or 3 && normalized[0] == 'F' &&
				       int.TryParse(normalized[1..], out var fn) && fn is >= 1 and <= 12 => (Keyboard.VirtualKey)(0x6F + fn),
				_ => null
			};
		}
	}

	private static class ParameterHelper
	{
		public static string? ToString(IReadOnlyDictionary<string, object?> map, string key)
		{
			return map.TryGetValue(key, out var val) ? val?.ToString() : null;
		}

			public static int? ToInt(IReadOnlyDictionary<string, object?> map, string key)
			{
				if (!map.TryGetValue(key, out var val) || val == null) return null;
				if (val is int i) return i;
				if (val is double d) return (int)d;
				var text = val.ToString();
				if (string.IsNullOrWhiteSpace(text)) return null;
				if (int.TryParse(text, out var parsed)) return parsed;
				var equalsIndex = text.LastIndexOf('=');
				return equalsIndex >= 0 && int.TryParse(text[(equalsIndex + 1)..].Trim(), out parsed) ? parsed : null;
			}

		public static bool ToBool(IReadOnlyDictionary<string, object?> map, string key, bool fallback = false)
		{
			if (!map.TryGetValue(key, out var val) || val == null) return fallback;
			if (val is bool b) return b;
			return bool.TryParse(val.ToString(), out var parsed) ? parsed : fallback;
		}

		public static List<int> ToIntList(IReadOnlyDictionary<string, object?> map, string key)
		{
			if (!map.TryGetValue(key, out var val) || val == null) return new List<int>();
			if (val is IEnumerable<string> listStrings)
				return listStrings.Select(v => int.TryParse(v, out var i) ? i : (int?)null).Where(i => i.HasValue).Select(i => i!.Value).ToList();
			if (val is IEnumerable<object> listObj)
				return listObj.Select(v => int.TryParse(v?.ToString(), out var i) ? i : (int?)null).Where(i => i.HasValue).Select(i => i!.Value).ToList();
			return val.ToString() is { } single && int.TryParse(single, out var parsed) ? new List<int> { parsed } : new List<int>();
		}

		public static List<string> ToStringList(IReadOnlyDictionary<string, object?> map, string key)
		{
			if (!map.TryGetValue(key, out var val) || val == null) return new List<string>();
			if (val is IEnumerable<string> listStrings) return listStrings.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
			if (val is IEnumerable<object> listObj) return listObj.Select(v => v?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()!;
			var single = val.ToString();
			return string.IsNullOrWhiteSpace(single) ? new List<string>() : new List<string> { single };
		}

		/// <summary>
		/// Parses a mixed target list the way a scripter would write it: numeric tokens become
		/// ids, everything else becomes a name (substring match). Each entry is re-split on
		/// newline/comma/semicolon so single-string values from older saves still work.
		/// </summary>
		public static (List<int> Ids, List<string> Names) ToTargetLists(IReadOnlyDictionary<string, object?> map, string key)
		{
			var ids = new List<int>();
			var names = new List<string>();

			foreach (var entry in ToStringList(map, key))
			{
				var tokens = entry.Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var raw in tokens)
				{
					var token = raw.Trim();
					if (token.Length == 0)
						continue;
					if (int.TryParse(token, out var id))
						ids.Add(id);
					else
						names.Add(token);
				}
			}

			return (ids, names);
		}

		public static List<(int x, int y, int z)> ToCoordinateList(IReadOnlyDictionary<string, object?> map, string key)
		{
			var output = new List<(int, int, int)>();
			var strings = ToStringList(map, key);
			foreach (var s in strings)
			{
				if (string.IsNullOrWhiteSpace(s)) continue;
				var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 2 &&
				    int.TryParse(parts[0], out var x) &&
				    int.TryParse(parts[1], out var y))
				{
					var z = 0;
					if (parts.Length >= 3) int.TryParse(parts[2], out z);
					output.Add((x, y, z));
				}
			}

			return output;
		}
	}
}
