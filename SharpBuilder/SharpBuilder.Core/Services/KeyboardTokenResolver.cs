using System;
using MESharp.API.Input;

namespace SharpBuilder.Core.Services;

/// <summary>
/// Single parser for user-entered key tokens ("E", "F6", "NUMPAD3", "CTRL", …) used by every
/// keybind-driven node, so the same token always resolves the same way across executors.
/// </summary>
internal static class KeyboardTokenResolver
{
	public static bool TryResolve(string? token, out Keyboard.VirtualKey key)
	{
		key = default;
		if (string.IsNullOrWhiteSpace(token))
			return false;

		var normalized = token.Trim().ToUpperInvariant();

		// Common aliases that don't match the VirtualKey enum names.
		switch (normalized)
		{
			case "CTRL":
				key = Keyboard.VirtualKey.Control;
				return true;
			case "RETURN":
				key = Keyboard.VirtualKey.Enter;
				return true;
			case "ESC":
				key = Keyboard.VirtualKey.Escape;
				return true;
		}

		// Enum.TryParse also accepts bare integers ("1" would become (VirtualKey)1, the left mouse
		// button), but a digit token always means the digit key — handled below.
		if (!char.IsDigit(normalized[0]) &&
			Enum.TryParse<Keyboard.VirtualKey>(normalized, ignoreCase: true, out var named))
		{
			key = named;
			return true;
		}

		if (normalized.StartsWith("NUMPAD", StringComparison.OrdinalIgnoreCase) &&
			int.TryParse(normalized["NUMPAD".Length..], out var numpad) &&
			numpad is >= 0 and <= 9)
		{
			key = (Keyboard.VirtualKey)(0x60 + numpad);
			return true;
		}

		if (normalized.Length == 1 && normalized[0] is >= 'A' and <= 'Z')
		{
			key = (Keyboard.VirtualKey)normalized[0];
			return true;
		}

		if (normalized.Length == 1 && normalized[0] is >= '0' and <= '9')
		{
			key = (Keyboard.VirtualKey)normalized[0];
			return true;
		}

		if (normalized.Length is 2 or 3 &&
			normalized[0] == 'F' &&
			int.TryParse(normalized[1..], out var fn) &&
			fn is >= 1 and <= 12)
		{
			key = (Keyboard.VirtualKey)(0x6F + fn);
			return true;
		}

		return false;
	}

	/// <summary>
	/// Taps a resolved key, preferring the direct game-input path for letter/digit keybinds
	/// (immune to window focus and ImGui capture; falls back to the message queue internally).
	/// </summary>
	public static bool Press(Keyboard.VirtualKey key)
	{
		var vk = (int)key;
		if (vk is >= '0' and <= '9' or >= 'A' and <= 'Z')
			return Keyboard.TapChar((char)vk);
		return Keyboard.Tap(vk);
	}
}
