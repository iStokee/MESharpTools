using MESharp.Services;

namespace MESharp;

public static class ScriptEntry
{
    private static readonly UiScriptHostOptions UiOptions = new()
    {
        ScriptName = "SharpBuilder (In-Game)",
        ResourceAssembly = typeof(ScriptEntry).Assembly
    };

    public static void Initialize()
        => WpfScriptHost.Run(() => new SharpBuilderHostWindow(), UiOptions);

    public static void Shutdown()
        => WpfScriptHost.Stop();
}
