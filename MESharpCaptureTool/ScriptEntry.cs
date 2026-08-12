using System.Windows;
using MESharp.Scripting;

namespace MESharp;

public static class ScriptEntry
{
    private static readonly CaptureToolScript Script = new();
    public static void Initialize() => Script.Initialize();
    public static void Shutdown() => Script.Shutdown();
}

internal sealed class CaptureToolScript : WpfScriptBase
{
    protected override Window CreateMainWindow() => new ActivityCaptureWindow();
}
