using System;
using System.Windows;
using System.Windows.Media;
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

        public NavHubWindow()
        {
            Title = "MESharp Navigation";
            Width = 1200;
            Height = 820;
            MinWidth = 1000;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(BackgroundProperty, "App.BackgroundBrush");
            SetResourceReference(ForegroundProperty, "App.ForegroundBrush");

            _vm = new NavigationHubViewModel();
            Content = new NavigationHubView { DataContext = _vm };

            Loaded += (_, _) => _vm.OnActivated();
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Closed -= OnClosed;

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
