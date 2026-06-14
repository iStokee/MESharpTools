using System;
using System.Windows;
using csharp_interop.McpTools;
using MESharp.Scripting;

namespace MESharp
{
    /// <summary>
    /// Hot-reload entry point for the MESharp MCP dashboard tool.
    ///
    /// Extracted out of csharp_interop into a standalone, independently live-updatable tool. Note the
    /// MCP <em>bridge</em> itself (McpRuntimeService/McpDiagnostics) still lives in csharp_interop and
    /// runs in-process; this tool is only the dashboard UI, which reads that bridge through the shared
    /// interop instance ME loads from Build_DLL. Hosted on the shared multi-tenant
    /// <see cref="WpfScriptHost"/> via <see cref="WpfScriptBase"/> so it coexists with the other tools.
    /// </summary>
    public static class ScriptEntry
    {
        private static readonly McpToolScript Script = new();

        public static void Initialize() => Script.Initialize();

        public static void Shutdown() => Script.Shutdown();
    }

    internal sealed class McpToolScript : WpfScriptBase
    {
        protected override Window CreateMainWindow()
        {
            _ = Application.Current ?? throw new InvalidOperationException("WPF Application.Current is unavailable.");
            // The dashboard is code-built with explicit colors, so no resource bootstrap is required.
            return new McpDashboardWindow();
        }
    }
}
