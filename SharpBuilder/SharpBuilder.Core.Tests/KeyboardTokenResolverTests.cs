using MESharp.API.Input;
using SharpBuilder.Core.Services;
using Xunit;

namespace SharpBuilder.Core.Tests;

public class KeyboardTokenResolverTests
{
	[Theory]
	[InlineData("1", 0x31)]
	[InlineData("0", 0x30)]
	[InlineData("9", 0x39)]
	public void TryResolve_DigitTokens_MapToDigitKeys_NotRawEnumValues(string token, int expectedVk)
	{
		// Enum.TryParse would happily parse "1" as (VirtualKey)1 — the left mouse button.
		Assert.True(KeyboardTokenResolver.TryResolve(token, out var key));
		Assert.Equal(expectedVk, (int)key);
	}

	[Theory]
	[InlineData("F1", 0x70)]
	[InlineData("F6", 0x75)]
	[InlineData("F12", 0x7B)]
	public void TryResolve_FunctionKeys(string token, int expectedVk)
	{
		Assert.True(KeyboardTokenResolver.TryResolve(token, out var key));
		Assert.Equal(expectedVk, (int)key);
	}

	[Theory]
	[InlineData("e", 0x45)]
	[InlineData("Numpad5", 0x65)]
	public void TryResolve_LettersAndNumpad(string token, int expectedVk)
	{
		Assert.True(KeyboardTokenResolver.TryResolve(token, out var key));
		Assert.Equal(expectedVk, (int)key);
	}

	[Fact]
	public void TryResolve_NamedVirtualKeys_StillParse()
	{
		Assert.True(KeyboardTokenResolver.TryResolve("Escape", out var key));
		Assert.Equal(Keyboard.VirtualKey.Escape, key);
	}

	[Theory]
	[InlineData("CTRL", nameof(Keyboard.VirtualKey.Control))]
	[InlineData("ctrl", nameof(Keyboard.VirtualKey.Control))]
	[InlineData("RETURN", nameof(Keyboard.VirtualKey.Enter))]
	[InlineData("ESC", nameof(Keyboard.VirtualKey.Escape))]
	public void TryResolve_CommonAliases_MapToNamedKeys(string token, string expectedName)
	{
		// These aliases came from the keyboard.send macro parser; after unification every
		// keybind-driven node must accept them.
		Assert.True(KeyboardTokenResolver.TryResolve(token, out var key));
		Assert.Equal(expectedName, key.ToString());
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("NotAKey")]
	public void TryResolve_InvalidTokens_Fail(string token)
	{
		Assert.False(KeyboardTokenResolver.TryResolve(token, out _));
	}
}
