using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;

namespace SharpBuilder.Core.Services;

internal sealed class NpcInteractExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetListsWithId(context.Parameters, "name", "id");
		if (ids.Count == 0 && names.Count == 0)
			return Task.FromResult(NodeExecutionResult.Fail());

		var actionIndex = ParameterHelper.ToInt(context.Parameters, "actionIndex") ?? 44;
		var offset = ParameterHelper.ToInt(context.Parameters, "offset") ?? Npcs.InteractNPC_route;
		var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;
		var ignoreStar = ParameterHelper.ToBool(context.Parameters, "ignoreStar", false);
		var minHealth = ParameterHelper.ToInt(context.Parameters, "minHealth") ?? 0;

		var ok = ExecutorHelpers.TryIdsThenNames(ids, names,
			idArray => Npcs.DoActionByIds(idArray, actionIndex: actionIndex, offset: offset, maxDistance: maxDistance, ignoreStar: ignoreStar, minHealth: minHealth),
			nameArray => Npcs.DoActionByNames(nameArray, actionIndex: actionIndex, offset: offset, maxDistance: maxDistance, ignoreStar: ignoreStar, minHealth: minHealth));

		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class GenericInteractionExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetLists(context.Parameters, "target");
		if (ids.Count == 0 && names.Count == 0)
			return Task.FromResult(NodeExecutionResult.Fail());

		var option = ParameterHelper.ToString(context.Parameters, "option");
		var npcAction = InteractionActionResolver.ResolveActionIndex(option, InteractionKind.Npc);
		var objectAction = InteractionActionResolver.ResolveActionIndex(option, InteractionKind.Object);

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

internal sealed class NpcFindExecutor : INodeExecutor
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
		return Task.FromResult(ExecutorHelpers.ConditionOutcome(signalKey, found, expected));
	}
}

internal sealed class NpcAttackExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetListsWithId(context.Parameters, "name", "id");

		var actionIndex = ParameterHelper.ToInt(context.Parameters, "actionIndex") ?? Npcs.AttackNpcAction;
		var offset = ParameterHelper.ToInt(context.Parameters, "offset") ?? Npcs.AttackNPC_route;
		var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;

		var ok = ExecutorHelpers.TryIdsThenNames(ids, names,
			idArray => Npcs.DoActionByIds(idArray, actionIndex: actionIndex, offset: offset, maxDistance: maxDistance),
			nameArray => Npcs.DoActionByNames(nameArray, actionIndex: actionIndex, offset: offset, maxDistance: maxDistance));

		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class ObjectInteractExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var (ids, names) = ParameterHelper.ToTargetListsWithId(context.Parameters, "name", "id");
		var option = ParameterHelper.ToString(context.Parameters, "option");
		var actionIndex = ParameterHelper.ToInt(context.Parameters, "actionIndex") ??
			InteractionActionResolver.ResolveActionIndex(option, InteractionKind.Object);
		var offset = ParameterHelper.ToInt(context.Parameters, "offset") ?? Objects.Offsets.GeneralRoute0;
		var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;
		var valid = ParameterHelper.ToBool(context.Parameters, "valid", false);

		var ok = ExecutorHelpers.TryIdsThenNames(ids, names,
			idArray => Objects.DoActionByIds(idArray, actionIndex: actionIndex, offset: offset, maxDistance: maxDistance, valid: valid),
			nameArray => Objects.DoActionByNames(nameArray, actionIndex: actionIndex, offset: offset, maxDistance: maxDistance, valid: valid));

		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class ObjectInteractHighlightedExecutor : INodeExecutor
{
	public Task<NodeExecutionResult> ExecuteAsync(NodeExecutionContext context, CancellationToken cancellationToken)
	{
		var objectIds = ParameterHelper.ToIntList(context.Parameters, "objectIds");
		var highlightIds = ParameterHelper.ToIntList(context.Parameters, "highlightIds");
		var maxDistance = ParameterHelper.ToInt(context.Parameters, "maxDistance") ?? int.MaxValue;
		var actionIndex = ParameterHelper.ToInt(context.Parameters, "actionIndex") ?? 0;

		if (objectIds.Count == 0 || highlightIds.Count == 0)
			return Task.FromResult(NodeExecutionResult.Fail());

		var ok = Objects.FindHighlighted(objectIds, highlightIds, maxDistance, actionIndex);
		return Task.FromResult(ok ? NodeExecutionResult.Success() : NodeExecutionResult.Fail());
	}
}

internal sealed class ObjectFindExecutor : INodeExecutor
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
		return Task.FromResult(ExecutorHelpers.ConditionOutcome(signalKey, found, expected));
	}
}

internal sealed class ObjectExistsExecutor : INodeExecutor
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
		return Task.FromResult(ExecutorHelpers.ConditionOutcome("objects.exists", exists, expected));
	}
}

internal enum InteractionKind
{
	Npc,
	Object
}

internal static class InteractionActionResolver
{
	public static int ResolveActionIndex(string? option, InteractionKind kind)
	{
		if (string.IsNullOrWhiteSpace(option))
			return 0;

		var normalized = option.Trim();
		if (int.TryParse(normalized, out var numericIndex))
			return Math.Max(0, numericIndex);

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
}
