using MESharp.Scripting;

namespace MESharp;

public static class ScriptEntry
{
    private static readonly FoundryScript Script = new();
    public static void Initialize() => Script.Initialize();
    public static void Shutdown() => Script.Shutdown();
}

internal sealed class FoundryScript : WpfScriptBase
{
    protected override System.Windows.Window CreateMainWindow() => new FoundryWindow();
}
