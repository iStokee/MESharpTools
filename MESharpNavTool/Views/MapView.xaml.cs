using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using MESharp.ViewModels;

namespace MESharp.Views
{
    /// <summary>
    /// Hosts the live coverage map in an embedded WebView2 pointed at the in-process
    /// CoverageMapServer. The control is created in code so a missing WebView2 runtime
    /// degrades to the fallback panel instead of crashing XAML load — important inside
    /// the injected game process where we control neither the machine nor the host dir.
    /// </summary>
    public partial class MapView : UserControl
    {
        private WebView2? _webView;
        private bool _initStarted;
        private static bool _loaderPreloaded;

        public MapView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private MapViewModel? ViewModel => DataContext as MapViewModel;

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_initStarted) return;
            _initStarted = true;
            await InitializeWebViewAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // The hub keeps this view alive via Visibility while switching sections;
            // Unloaded only fires when the whole page (or window) goes away. Dispose so
            // the browser processes don't outlive the tool — the map state is cheap to
            // rebuild from the live server on the next visit.
            try
            {
                if (_webView != null)
                {
                    WebHost.Children.Remove(_webView);
                    _webView.Dispose();
                }
            }
            catch { }
            _webView = null;
            _initStarted = false;
        }

        private async Task InitializeWebViewAsync()
        {
            var vm = ViewModel;
            try
            {
                PreloadWebView2Loader();

                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MESharp", "WebView2");
                Directory.CreateDirectory(userDataFolder);

                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null, userDataFolder: userDataFolder);

                var webView = new WebView2();
                WebHost.Children.Add(webView);
                await webView.EnsureCoreWebView2Async(environment);

                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                var url = vm?.EnsureServer();
                if (string.IsNullOrEmpty(url))
                    throw new InvalidOperationException("Coverage map server did not start.");

                webView.Source = new Uri(url);
                _webView = webView;
                if (vm != null) vm.WebViewFailed = false;
            }
            catch (Exception ex)
            {
                try
                {
                    if (_webView != null) WebHost.Children.Remove(_webView);
                }
                catch { }
                _webView = null;
                if (vm != null)
                {
                    vm.WebViewFailed = true;
                    vm.WebViewError = ex.Message;
                }
                Console.WriteLine($"[Managed] MapView WebView2 init failed: {ex}");
            }
        }

        private void OnHelpClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new SectionHelpWindow(NavHelpContent.Map());
                var owner = Window.GetWindow(this);
                if (owner != null)
                {
                    window.Owner = owner;
                }
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Managed] Could not open map help: {ex.Message}");
            }
        }

        private void OnReloadClick(object sender, RoutedEventArgs e)
        {
            if (_webView?.CoreWebView2 != null)
            {
                _webView.Reload();
            }
            else if (!_initStarted || ViewModel?.WebViewFailed == true)
            {
                // Allow retrying after a failed init (e.g. runtime installed meanwhile).
                _initStarted = true;
                _ = InitializeWebViewAsync();
            }
        }

        /// <summary>
        /// The managed WebView2 assemblies are Costura-embedded in the script DLL, but
        /// their P/Invokes need the native WebView2Loader.dll, which cannot ride along.
        /// The build copies it next to the script; loading it once by full path makes
        /// every later "WebView2Loader.dll" import resolve to the loaded module.
        /// </summary>
        private static void PreloadWebView2Loader()
        {
            if (_loaderPreloaded) return;

            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "MemoryError", "CSharp_scripts", "WebView2Loader.dll"),
                Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll"),
                Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "WebView2Loader.dll"),
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path) && NativeLibrary.TryLoad(path, out _))
                {
                    _loaderPreloaded = true;
                    return;
                }
            }
            // Not fatal: if the loader is reachable through normal search paths,
            // CoreWebView2Environment.CreateAsync still works; otherwise it throws
            // and the fallback panel explains the situation.
        }
    }
}
