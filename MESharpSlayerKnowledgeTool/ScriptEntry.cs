using System.Reflection;
using System.Windows;
using MESharp.Scripting;

namespace MESharp;

public static class ScriptEntry
{
    private static readonly SlayerKnowledgeToolScript Script = new();
    public static void Initialize() => Script.Initialize();
    public static void Shutdown() => Script.Shutdown();
}

internal sealed class SlayerKnowledgeToolScript : WpfScriptBase
{
    protected override Window CreateMainWindow()
    {
        var app = Application.Current ?? throw new InvalidOperationException("WPF Application.Current is unavailable.");
        foreach (var uri in new[]
        {
            "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Controls.Buttons.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Themes/Dark.Red.xaml"
        })
        {
            var target = new Uri(uri, UriKind.Absolute);
            if (!app.Resources.MergedDictionaries.Any(d => d.Source == target))
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = target });
        }
        return new SlayerKnowledgeWindow();
    }
}
