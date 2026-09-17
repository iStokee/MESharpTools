using System;

namespace MESharp.Services
{
    /// <summary>Tool-facing access to the process-wide navigation execution gate.</summary>
    internal static class NavigationRunGate
    {
        internal static IDisposable? TryEnter() => NavigationExecutionGate.TryEnter();
    }
}
