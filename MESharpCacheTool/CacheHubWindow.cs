using System;
using System.Windows;
using MESharp.ViewModels;
using MESharp.Views;

namespace MESharp
{
    /// <summary>
    /// Standalone host window for the Cache tool. Owns the <see cref="CacheHubViewModel"/> and tears
    /// it down on close so the tool's collectible AssemblyLoadContext can be reclaimed and it coexists
    /// cleanly with other tools on the shared WPF dispatcher (WpfScriptHost paradigm).
    /// </summary>
    internal sealed class CacheHubWindow : Window
    {
        private readonly CacheHubViewModel _vm;

        public CacheHubWindow()
        {
            Title = "MESharp Cache";
            Width = 1180;
            Height = 800;
            MinWidth = 900;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(BackgroundProperty, "App.BackgroundBrush");
            SetResourceReference(ForegroundProperty, "App.ForegroundBrush");

            _vm = new CacheHubViewModel();
            Content = new CacheHubView { DataContext = _vm };

            Loaded += (_, _) => _vm.OnActivated();
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Closed -= OnClosed;
            try { _vm.Dispose(); }
            catch (Exception ex) { Console.WriteLine("[CacheTool] Hub dispose failed: " + ex.Message); }
        }
    }
}
