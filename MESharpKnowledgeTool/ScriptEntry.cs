using System;
using System.Reflection;
using System.Windows;
using MESharp.Scripting;

namespace MESharp
{
    /// <summary>
    /// Hot-reload entry point for the MESharp Knowledge tool — the KOS 2.0 verification "service desk".
    /// A standalone, independently live-updatable WPF tool (its own GitHub release cadence), hosted on
    /// the shared multi-tenant <see cref="WpfScriptHost"/> via <see cref="WpfScriptBase"/> so it
    /// coexists with the other tools. Reads/writes the verification ledger in <c>csharp_interop</c>
    /// (loaded by ME from Build_DLL).
    /// </summary>
    public static class ScriptEntry
    {
        private static readonly KnowledgeToolScript Script = new();

        public static void Initialize() => Script.Initialize();

        public static void Shutdown() => Script.Shutdown();
    }

    internal sealed class KnowledgeToolScript : WpfScriptBase
    {
        protected override Window CreateMainWindow()
        {
            var app = Application.Current ?? throw new InvalidOperationException("WPF Application.Current is unavailable.");
            BootstrapResources(app);
            return new Views.ServiceDeskWindow();
        }

        /// <summary>
        /// The service-desk XAML binds to MahApps theme brushes (a dark theme for the dashboard look)
        /// plus this tool's own card/chip styles. The shared WPF host applies no App.xaml, so the tool
        /// merges its dictionaries itself. Idempotent: reloads/coexistence never duplicate dictionaries.
        /// </summary>
        private static void BootstrapResources(Application app)
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName().Name ?? "MESharpKnowledgeTool";

            var uris = new[]
            {
                "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Controls.Buttons.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
                "pack://application:,,,/MahApps.Metro;component/Styles/Themes/Dark.Blue.xaml",
                $"pack://application:,,,/{assemblyName};component/Themes/KnowledgeResources.xaml"
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
                        return;
                }
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = targetUri });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KnowledgeTool] Skipped resource '{uriString}': {ex.Message}");
            }
        }
    }
}
