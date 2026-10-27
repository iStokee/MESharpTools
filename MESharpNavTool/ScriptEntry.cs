using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using MESharp.Scripting;
using MESharp.Services.NavigationData;
using MESharp.Services;

namespace MESharp
{
    /// <summary>
    /// Hot-reload entry point for the MESharp Navigation tool (map, webwalk graph, routes).
    ///
    /// Extracted from MESharpDebugUtil into a standalone, independently live-updatable tool.
    /// Hosted on the shared multi-tenant <see cref="WpfScriptHost"/> via <see cref="WpfScriptBase"/>,
    /// so it coexists with other WPF tools (SharpBuilder, DebugUtil, ...).
    /// </summary>
    public static class ScriptEntry
    {
        private static readonly NavToolScript Script = new();
        private static readonly System.Threading.CancellationTokenSource BuildCancellation = new();

        public static void Initialize()
        {
            NavigationDataManager.EnsurePortableLayout();
            NavigationDataManager.CheckForUpdatesInBackground();
            Script.Initialize();
            // The same readiness path is used by the scripting API, including stale-data rebuilds.
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await NavigationReadiness.EnsureReadyAsync(BuildCancellation.Token);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Console.WriteLine("[Navigation build] " + ex.Message); }
                });
            }
        }

        public static void Shutdown()
        {
            BuildCancellation.Cancel();
            Script.Shutdown();
        }
    }

    internal sealed class NavToolScript : WpfScriptBase
    {
        protected override Window CreateMainWindow()
        {
            var app = Application.Current ?? throw new InvalidOperationException("WPF Application.Current is unavailable.");
            BootstrapResources(app);
            TryApplyTheme();
            return new NavHubWindow();
        }

        protected override void OnShutdown()
        {
            // Application.Current is process-wide and survives collectible tool reloads. Remove every
            // dictionary sourced from this tool assembly so the old AssemblyLoadContext is not pinned.
            var app = Application.Current;
            if (app == null) return;
            void RemoveToolDictionaries()
            {
                var assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "MESharpNavTool";
                var marker = $"/{assemblyName};component/";
                for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    var source = app.Resources.MergedDictionaries[i].Source?.ToString();
                    if (source?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true)
                        app.Resources.MergedDictionaries.RemoveAt(i);
                }
            }

            if (app.Dispatcher.CheckAccess()) RemoveToolDictionaries();
            else
            {
                using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(2));
                app.Dispatcher.Invoke(RemoveToolDictionaries, System.Windows.Threading.DispatcherPriority.Send,
                    timeout.Token, TimeSpan.FromSeconds(2));
            }
        }

        /// <summary>
        /// Merges MahApps + the tool's own theme dictionaries and seeds the Primary* brushes the nav
        /// views bind to. The shared WPF host never applies an App.xaml, so the tool bootstraps its
        /// own Application.Resources (idempotent so reloads/coexistence don't duplicate dictionaries).
        /// </summary>
        private static void BootstrapResources(Application app)
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "MESharpNavTool";

            var mahAppsUris = new[]
            {
                "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Controls.Buttons.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml"
            };
            foreach (var uri in mahAppsUris)
            {
                TryMergeDictionary(app, uri, swallowErrors: true, replaceExisting: false);
            }

            // Keep the script theme after third-party dictionaries so implicit control styles win.
            var scriptThemeUris = new[]
            {
                $"pack://application:,,,/{assemblyName};component/Themes/Light.xaml",
                $"pack://application:,,,/{assemblyName};component/Themes/ItemFlagResources.xaml",
                $"pack://application:,,,/{assemblyName};component/Themes/NavHubResources.xaml"
            };
            foreach (var uri in scriptThemeUris)
            {
                TryMergeDictionary(app, uri, swallowErrors: false, replaceExisting: true);
            }

            var acrylicColor = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);
            EnsureResource(app, "AcrylicBackgroundColor", () => acrylicColor);
            EnsureResource(app, "AcrylicBackgroundBrush", () => CreateFrozenBrush(acrylicColor));

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
            {
                app.Resources[key] = factory();
            }
        }

        private static void TryMergeDictionary(Application app, string uriString, bool swallowErrors, bool replaceExisting)
        {
            try
            {
                var targetUri = new Uri(uriString, UriKind.Absolute);
                for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    var existing = app.Resources.MergedDictionaries[i];
                    if (existing.Source == null) continue;
                    if (Uri.Compare(existing.Source, targetUri, UriComponents.AbsoluteUri, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        if (!replaceExisting) return;
                        app.Resources.MergedDictionaries.RemoveAt(i);
                    }
                }

                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = targetUri });
            }
            catch (Exception ex)
            {
                if (!swallowErrors) throw;
                Console.WriteLine($"[NavTool] Skipped resource '{uriString}': {ex.Message}");
            }
        }

        private static void TryApplyTheme()
        {
            try
            {
                var settings = ThemeManager.LoadSettings();
                if (csharp_interop.native.Native_UI.TryGetManagedToolsDarkMode(out var isDark))
                {
                    settings.IsDark = isDark;
                }
                ThemeManager.ApplyTheme(settings);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[NavTool] Theme apply during bootstrap failed: " + ex);
            }
        }
    }
}
