using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using MESharp.Scripting;
using MESharp.Services;

namespace MESharp
{
    /// <summary>
    /// Hot-reload entry point for the MESharp Cache tool (chisel dump browser + game-cache status).
    ///
    /// Hosted on the shared multi-tenant <see cref="WpfScriptHost"/> via <see cref="WpfScriptBase"/>,
    /// so it coexists with other WPF tools (NavTool, SharpBuilder, DebugUtil, ...).
    /// </summary>
    public static class ScriptEntry
    {
        private static readonly CacheToolScript Script = new();

        public static void Initialize() => Script.Initialize();

        public static void Shutdown() => Script.Shutdown();
    }

    internal sealed class CacheToolScript : WpfScriptBase
    {
        protected override Window CreateMainWindow()
        {
            var app = Application.Current ?? throw new InvalidOperationException("WPF Application.Current is unavailable.");
            BootstrapResources(app);
            TryApplyTheme();
            return new CacheHubWindow();
        }

        /// <summary>
        /// Merges MahApps + the tool's theme dictionaries and seeds the Primary* brushes the views bind
        /// to. The shared WPF host applies no App.xaml, so the tool bootstraps its own Application.Resources
        /// (idempotent so reloads/coexistence don't duplicate dictionaries).
        /// </summary>
        private static void BootstrapResources(Application app)
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "MESharpCacheTool";

            var mahAppsUris = new[]
            {
                "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Controls.Buttons.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml"
            };
            foreach (var uri in mahAppsUris)
                TryMergeDictionary(app, uri, swallowErrors: true);

            // Keep the script theme after third-party dictionaries so implicit control styles win.
            var scriptThemeUris = new[]
            {
                $"pack://application:,,,/{assemblyName};component/Themes/Light.xaml",
                $"pack://application:,,,/{assemblyName};component/Themes/CacheHubResources.xaml"
            };
            foreach (var uri in scriptThemeUris)
                TryMergeDictionary(app, uri, swallowErrors: false);

            // MCP-dashboard accent blue (#42A5F5). ThemeManager re-applies the saved accent at runtime;
            // seeding the same family here avoids an indigo flash before that runs.
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
                app.Resources[key] = factory();
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
                        return;
                }
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = targetUri });
            }
            catch (Exception ex)
            {
                if (!swallowErrors) throw;
                Console.WriteLine($"[CacheTool] Skipped resource '{uriString}': {ex.Message}");
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
                Console.WriteLine("[CacheTool] Theme apply during bootstrap failed: " + ex);
            }
        }
    }
}
