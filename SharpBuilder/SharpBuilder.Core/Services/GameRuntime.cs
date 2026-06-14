using System;
using System.Runtime.InteropServices;

namespace SharpBuilder.Core.Services;

/// <summary>
/// Detects whether the process hosts the injected game client. Node executors P/Invoke
/// XInput1_4.dll, so game-backed nodes can only execute when that module is already loaded
/// (i.e. when running inside the RS3 process via ME's .NET host).
/// </summary>
public static class GameRuntime
{
	private static readonly Lazy<bool> _isAvailable = new(() =>
		GetModuleHandleW("XInput1_4.dll") != IntPtr.Zero);

	/// <summary>
	/// True when the ME native module is loaded in this process and game-backed executors can run.
	/// </summary>
	public static bool IsGameApiAvailable => _isAvailable.Value;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern IntPtr GetModuleHandleW(string moduleName);
}
