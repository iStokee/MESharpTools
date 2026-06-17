using System;
using System.Linq;
using MESharp.API;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Editor.Wpf.Services;

public sealed record CaptureCalibrationResult(string Status, string DriftState, bool Changed);

public sealed class CaptureCalibrationService
{
	public bool CanCapture(NodeDefinition? definition)
	{
		if (definition == null) return false;
		return string.Equals(definition.CategoryId, "npcs", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(definition.CategoryId, "objects", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(definition.Id, "loot.pickup", StringComparison.OrdinalIgnoreCase);
	}

	public CaptureCalibrationResult Capture(NodeModel? node, NodeDefinition? definition)
	{
		if (node == null || definition == null || !CanCapture(definition))
		{
			return new CaptureCalibrationResult("Select an Object/NPC interaction node first.", "none", false);
		}

		var kind = DesiredCaptureKind(definition);
		var capture = DoActionDebugSignals.LatestCapture(kind) ?? DoActionDebugSignals.LatestCapture();
		if (capture == null)
		{
			return new CaptureCalibrationResult(
				"No in-game click captured yet — click the target in the game, then Capture.",
				"none",
				false);
		}

		var oldAction = FindParamValue(node, "actionIndex");
		var oldOffset = FindParamValue(node, "offset");

		var filledAny = false;
		filledAny |= SetParamRaw(node, definition, "id", capture.Id > 0 ? capture.Id.ToString() : null);

		if (capture.Id > 0 && GetParamType(definition, "id") == null)
		{
			foreach (var listKey in new[] { "target", "name", "objectIds" })
			{
				if (SetParamRaw(node, definition, listKey, capture.Id.ToString()))
				{
					filledAny = true;
					break;
				}
			}
		}

		if (GetParamType(definition, "actionIndex") == NodeParamType.Number)
			filledAny |= SetParamRaw(node, definition, "actionIndex", capture.ActionOpcode.ToString());
		filledAny |= SetParamRaw(node, definition, "offset", capture.Offset.ToString());
		if (capture.Distance > 0)
			filledAny |= SetParamRaw(node, definition, "maxDistance", capture.Distance.ToString());

		if (!filledAny)
		{
			return new CaptureCalibrationResult(
				$"Captured {kind} (id {capture.Id}, action {capture.ActionOpcode}, offset {capture.Offset}) but this node has no matching fields.",
				"none",
				false);
		}

		var hadValues = !string.IsNullOrWhiteSpace(oldAction) || !string.IsNullOrWhiteSpace(oldOffset);
		var matched = NumbersEqual(oldAction, capture.ActionOpcode.ToString()) && NumbersEqual(oldOffset, capture.Offset.ToString());
		var driftState = !hadValues ? "filled" : matched ? "match" : "drift";
		var status = driftState switch
		{
			"match" => $"Match — {kind} action {capture.ActionOpcode}, offset {capture.Offset} (id {capture.Id}).",
			"drift" => $"Drift — updated to action {capture.ActionOpcode}, offset {capture.Offset} (id {capture.Id}).",
			_ => $"Filled from {kind} click — action {capture.ActionOpcode}, offset {capture.Offset} (id {capture.Id})."
		};

		return new CaptureCalibrationResult(status, driftState, true);
	}

	private static string DesiredCaptureKind(NodeDefinition definition)
	{
		if (string.Equals(definition.CategoryId, "npcs", StringComparison.OrdinalIgnoreCase)) return "NPC";
		if (string.Equals(definition.Id, "loot.pickup", StringComparison.OrdinalIgnoreCase)) return "GroundItem";
		return "Object";
	}

	private static NodeParamType? GetParamType(NodeDefinition definition, string key)
		=> definition.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.Type;

	private static string? FindParamValue(NodeModel node, string key)
		=> node.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.RawValue;

	private static bool SetParamRaw(NodeModel node, NodeDefinition definition, string key, string? value)
	{
		if (value == null) return false;
		var parameterDefinition = definition.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
		if (parameterDefinition == null) return false;
		var parameterValue = node.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
		if (parameterValue == null) return false;

		if (parameterDefinition.Type == NodeParamType.Enum &&
		    parameterDefinition.EnumValues != null &&
		    int.TryParse(value, out var numeric))
		{
			var match = parameterDefinition.EnumValues.FirstOrDefault(e => ParseTrailingInt(e) == numeric);
			parameterValue.RawValue = match ?? value;
		}
		else
		{
			parameterValue.RawValue = value;
		}

		return true;
	}

	private static bool NumbersEqual(string? a, string? b)
	{
		var parsedA = ParseTrailingInt(a);
		var parsedB = ParseTrailingInt(b);
		return parsedA.HasValue && parsedB.HasValue && parsedA.Value == parsedB.Value;
	}

	private static int? ParseTrailingInt(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return null;
		if (int.TryParse(text.Trim(), out var n)) return n;
		var eq = text.LastIndexOf('=');
		return eq >= 0 && int.TryParse(text[(eq + 1)..].Trim(), out n) ? n : null;
	}
}
