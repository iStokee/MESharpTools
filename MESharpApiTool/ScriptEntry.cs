using System;
using System.Windows;
using csharp_interop.Documentation;
using MESharp.Scripting;

namespace MESharp
{
    /// <summary>
    /// Hot-reload entry point for the MESharp API browser tool.
    ///
    /// Extracted out of csharp_interop into a standalone, independently live-updatable tool so the
    /// API documentation UI can ship on its own GitHub release cadence. Hosted on the shared
    /// multi-tenant <see cref="WpfScriptHost"/> via <see cref="WpfScriptBase"/>, so it coexists with
    /// the other WPF tools (Navigation, SharpBuilder, MCP dashboard, ...). The browser still reflects
    /// over the live <c>csharp_interop</c> assembly (resolved by name), which ME loads from Build_DLL.
    /// </summary>
    public static class ScriptEntry
    {
        private static readonly ApiToolScript Script = new();

        public static void Initialize() => Script.Initialize();

        public static void Shutdown() => Script.Shutdown();
    }

    internal sealed class ApiToolScript : WpfScriptBase
    {
        protected override Window CreateMainWindow()
        {
            var app = Application.Current ?? throw new InvalidOperationException("WPF Application.Current is unavailable.");
            BootstrapMahAppsResources(app);
            return new ApiDocumentationWindow();
        }

        /// <summary>
        /// The API browser XAML binds to <c>MahApps.Brushes.*</c>/<c>MahApps.Colors.*</c>, which come
        /// from MahApps' theme dictionaries. The shared WPF host applies no App.xaml, so the tool
        /// merges them itself. Idempotent: reloads/coexistence never duplicate dictionaries.
        /// </summary>
        private static void BootstrapMahAppsResources(Application app)
        {
            var uris = new[]
            {
                "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml"
            };
            foreach (var uri in uris)
            {
                TryMergeDictionary(app, uri);
            }
        }

        private static void TryMergeDictionary(Application app, string uriString)
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
                Console.WriteLine($"[ApiTool] Skipped resource '{uriString}': {ex.Message}");
            }
        }
    }
}
