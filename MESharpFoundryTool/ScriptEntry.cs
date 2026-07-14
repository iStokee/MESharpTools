using System.Reflection;
using System.Windows;
using System.Windows.Media;
using MESharp.Scripting;
using MESharp.Services;

namespace MESharp;

public static class ScriptEntry
{
    private static readonly FoundryScript Script = new();
    public static void Initialize() => Script.Initialize();
    public static void Shutdown() => Script.Shutdown();
}

internal sealed class FoundryScript : WpfScriptBase
{
    protected override Window CreateMainWindow()
    {
        var app = Application.Current ?? throw new InvalidOperationException("WPF Application.Current is unavailable.");
        BootstrapResources(app);
        TryApplyTheme();
        return new FoundryWindow();
    }

    /// <summary>
    /// Merges MahApps + the tool's own theme dictionaries. The shared WPF host never applies an
    /// App.xaml, so the tool bootstraps its own Application.Resources (idempotent so reloads /
    /// coexistence don't duplicate dictionaries).
    /// </summary>
    private static void BootstrapResources(Application app)
    {
        var assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "MESharpFoundryTool";

        var mahAppsUris = new[]
        {
            "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Controls.Buttons.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml"
        };
        foreach (var uri in mahAppsUris)
        {
            TryMergeDictionary(app, uri, swallowErrors: true);
        }

        // Keep the script theme after third-party dictionaries so implicit control styles win.
        // ThemeManager.ApplyTheme swaps this for Dark.xaml when the saved setting is dark.
        TryMergeDictionary(app, $"pack://application:,,,/{assemblyName};component/Themes/Light.xaml", swallowErrors: false);

        var acrylicColor = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
        EnsureResource(app, "AcrylicBackgroundColor", () => acrylicColor);
        EnsureResource(app, "AcrylicBackgroundBrush", () => CreateFrozenBrush(acrylicColor));

        var primaryColor = Color.FromArgb(0xFF, 0x42, 0xA5, 0xF5);
        EnsureResource(app, "PrimaryColor", () => primaryColor);
        EnsureResource(app, "PrimaryBrush", () => CreateFrozenBrush(primaryColor));
        EnsureResource(app, "PrimaryForegroundBrush", () => CreateFrozenBrush(Colors.White));
        EnsureResource(app, "PrimarySoftBrush", () => CreateFrozenBrush(Color.FromArgb(0x33, 0x42, 0xA5, 0xF5)));
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze) brush.Freeze();
        return brush;
    }

    private static void EnsureResource(Application app, object key, Func<object> factory)
    {
        if (!app.Resources.Contains(key))
        {
            app.Resources[key] = factory();
        }
    }

    private static void TryMergeDictionary(Application app, string uriString, bool swallowErrors)
    {
        try
        {
            var targetUri = new Uri(uriString, UriKind.Absolute);
            foreach (var existing in app.Resources.MergedDictionaries)
            {
                if (existing.Source == null) continue;
                if (Uri.Compare(existing.Source, targetUri, UriComponents.AbsoluteUri, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return;
                }
            }

            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = targetUri });
        }
        catch (Exception ex)
        {
            if (!swallowErrors) throw;
            Console.WriteLine($"[FoundryTool] Skipped resource '{uriString}': {ex.Message}");
        }
    }

    private static void TryApplyTheme()
    {
        try
        {
            var settings = ThemeManager.LoadSettings();
            ThemeManager.ApplyTheme(settings);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FoundryTool] Theme apply during bootstrap failed: " + ex);
        }
    }
}
