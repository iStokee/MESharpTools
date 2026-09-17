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
        private int _loadVersion;
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
            await InitializeWebViewAsync(++_loadVersion);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _loadVersion++;
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

        private async Task InitializeWebViewAsync(int loadVersion)
        {
            var vm = ViewModel;
            WebView2? webView = null;
            try
            {
                PreloadWebView2Loader();

                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MESharp", "WebView2");
                Directory.CreateDirectory(userDataFolder);

                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null, userDataFolder: userDataFolder);
                if (loadVersion != _loadVersion || !IsLoaded) return;

                webView = new WebView2();
                WebHost.Children.Add(webView);
                await webView.EnsureCoreWebView2Async(environment);
                if (loadVersion != _loadVersion || !IsLoaded) return;

                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                var url = vm?.EnsureServer();
                if (string.IsNullOrEmpty(url))
                    throw new InvalidOperationException("Coverage map server did not start.");

                webView.Source = new Uri(url);
                _webView = webView;
                webView = null;
                if (vm != null) vm.WebViewFailed = false;
            }
            catch (Exception ex)
            {
                if (loadVersion == _loadVersion && vm != null)
                {
                    vm.WebViewFailed = true;
                    vm.WebViewError = ex.Message;
                }
                Console.WriteLine($"[Managed] MapView WebView2 init failed: {ex}");
            }
            finally
            {
                if (webView != null)
                {
                    try { WebHost.Children.Remove(webView); webView.Dispose(); } catch { }
                }
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
                _ = InitializeWebViewAsync(++_loadVersion);
            }
        }

        /// <summary>
        /// The managed WebView2 assemblies and native loader are embedded in the tool DLL.
        /// Native libraries must exist on disk before Windows can load them, so extract the
        /// loader under local app data and preload it using its canonical module name.
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

            // Tool live updates install only MESharpNavTool.dll. Keep that path self-contained by
            // extracting the embedded x64 loader to a versioned local-app-data directory on first use.
            try
            {
                using var resource = typeof(MapView).Assembly.GetManifestResourceStream("MESharp.WebView2Loader.x64.dll");
                if (resource != null)
                {
                    var version = typeof(MapView).Assembly.GetName().Version?.ToString() ?? "unknown";
                    var nativeDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MESharp", "Native", version);
                    Directory.CreateDirectory(nativeDir);
                    // Keep the canonical filename: WebView2's managed P/Invokes request this exact
                    // module name after we preload it by full path.
                    var extractedPath = Path.Combine(nativeDir, "WebView2Loader.dll");
                    if (!File.Exists(extractedPath) || new FileInfo(extractedPath).Length != resource.Length)
                    {
                        var tempPath = extractedPath + ".tmp";
                        using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            resource.CopyTo(output);
                        File.Move(tempPath, extractedPath, overwrite: true);
                    }

                    if (NativeLibrary.TryLoad(extractedPath, out _))
                    {
                        _loaderPreloaded = true;
                        return;
                    }
                }
            }
            catch
            {
                // Fall through to WebView2's normal native-library resolution and the UI fallback.
            }

            // Not fatal: if the loader is reachable through normal search paths,
            // CoreWebView2Environment.CreateAsync still works; otherwise it throws
            // and the fallback panel explains the situation.
        }
    }
}
