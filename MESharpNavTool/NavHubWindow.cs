using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using csharp_interop.native;
using MESharp.Services;
using MESharp.ViewModels;
using MESharp.Views;

namespace MESharp
{
    /// <summary>
    /// Standalone host window for the Navigation tool. Owns the <see cref="NavigationHubViewModel"/>
    /// and tears it down on close so the tool's collectible AssemblyLoadContext can be reclaimed and
    /// it coexists cleanly with other tools on the shared WPF dispatcher (WpfScriptHost paradigm).
    /// </summary>
    internal sealed class NavHubWindow : Window
    {
        private readonly NavigationHubViewModel _vm;
        private readonly DispatcherTimer _themeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        private bool _isDarkMode;

        public NavHubWindow()
        {
            Title = "MESharp Navigation";
            Width = 1200;
            Height = 820;
            MinWidth = 940;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(BackgroundProperty, "App.BackgroundBrush");
            SetResourceReference(ForegroundProperty, "App.ForegroundBrush");

            _vm = new NavigationHubViewModel();
            Content = new NavigationHubView { DataContext = _vm };

            var settings = ThemeManager.LoadSettings();
            _isDarkMode = Native_UI.TryGetManagedToolsDarkMode(out var isDark) ? isDark : settings.IsDark;

            Loaded += (_, _) => { _vm.OnActivated(); _themeTimer.Start(); };
            Closing += OnClosing;
            Closed += OnClosed;
            _themeTimer.Tick += OnThemeTimerTick;
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            // Stop/reload must discard the draft and reach Closed so movement and the
            // map listener are torn down. Only an ordinary user close may be cancelled.
            if (!WpfScriptHost.IsShutdownRequested(this) && !_vm.ConfirmClose()) e.Cancel = true;
        }

        private void OnThemeTimerTick(object? sender, EventArgs e)
        {
            if (!Native_UI.TryGetManagedToolsDarkMode(out var isDark) || isDark == _isDarkMode)
            {
                return;
            }

            _isDarkMode = isDark;
            var settings = ThemeManager.LoadSettings();
            settings.IsDark = isDark;
            ThemeManager.ApplyTheme(settings);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Closed -= OnClosed;
            Closing -= OnClosing;
            _themeTimer.Stop();
            _themeTimer.Tick -= OnThemeTimerTick;

            // Stops the hub status timer and disposes child VMs (their refresh timers, run CTS,
            // and CoverageMapServer focus subscriptions).
            try { _vm.Dispose(); }
            catch (Exception ex) { Console.WriteLine("[NavTool] Hub dispose failed: " + ex.Message); }

            // Close the localhost coverage-map listener and cancel any in-flight recording so the
            // TcpListener/thread don't pin this tool's ALC after the window goes away.
            try { CoverageMapServer.Stop(); }
            catch (Exception ex) { Console.WriteLine("[NavTool] CoverageMapServer stop failed: " + ex.Message); }
        }
    }
}
