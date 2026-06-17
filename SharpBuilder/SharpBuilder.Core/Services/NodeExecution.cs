using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;
using SharpBuilder.Core.Models;

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

}
