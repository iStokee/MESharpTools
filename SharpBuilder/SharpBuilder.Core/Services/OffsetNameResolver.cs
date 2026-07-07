using System;
using System.Linq;
using MESharp.API;

namespace SharpBuilder.Core.Services;

/// <summary>
/// Resolves "Name = value" offset strings (the format stored by route-offset enum params)
/// against the current <see cref="ActionOffsets"/> table, so saved graphs pick up new route
/// values after a game update instead of replaying the number that was current at save time.
/// The numeric tail is only trusted when the name is not a known offset key.
/// </summary>
internal static class OffsetNameResolver
{
	public static bool TryResolve(string? text, out int value)
	{
		value = 0;
		if (string.IsNullOrWhiteSpace(text)) return false;

		var equalsIndex = text.IndexOf('=');
		var name = (equalsIndex >= 0 ? text[..equalsIndex] : text).Trim();

		var known = ActionOffsets.All.FirstOrDefault(o => string.Equals(o.Key, name, StringComparison.OrdinalIgnoreCase));
		if (known != null)
		{
			value = known.Value;
			return true;
		}

		return equalsIndex >= 0 && int.TryParse(text[(equalsIndex + 1)..].Trim(), out value);
	}

	/// <summary>
	/// Returns the current value for a known offset name when <paramref name="text"/> carries a
	/// stale "Name = number" pair, or null when the text is not a drifted offset reference.
	/// </summary>
	public static (string Name, int SavedValue, int CurrentValue)? DetectDrift(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return null;

		var equalsIndex = text.IndexOf('=');
		if (equalsIndex < 0) return null;

		var name = text[..equalsIndex].Trim();
		if (!int.TryParse(text[(equalsIndex + 1)..].Trim(), out var saved)) return null;

		var known = ActionOffsets.All.FirstOrDefault(o => string.Equals(o.Key, name, StringComparison.OrdinalIgnoreCase));
		if (known == null || known.Value == saved) return null;

		return (known.Key, saved, known.Value);
	}
}
