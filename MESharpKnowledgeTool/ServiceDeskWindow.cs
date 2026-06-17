using System;
using System.Windows;
using MESharp.ViewModels;

namespace MESharp.Views
{
    /// <summary>
    /// Standalone host window for the KOS 2.0 service desk. Owns the <see cref="ServiceDeskViewModel"/>
    /// and tears it down on close so the tool's collectible AssemblyLoadContext can be reclaimed
    /// (WpfScriptHost paradigm — coexists with the other tools on the shared dispatcher).
    /// </summary>
    public sealed class ServiceDeskWindow : Window
    {
        private readonly ServiceDeskViewModel _vm;

        public ServiceDeskWindow()
        {
            Title = "MESharp Knowledge — Service Desk";
            Width = 1280;
            Height = 840;
            MinWidth = 1040;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(BackgroundProperty, "MahApps.Brushes.ThemeBackground");
            SetResourceReference(ForegroundProperty, "MahApps.Brushes.ThemeForeground");

            _vm = new ServiceDeskViewModel();
            Content = new ServiceDeskView { DataContext = _vm };

            Loaded += (_, _) => _vm.OnActivated();
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Closed -= OnClosed;
            try { _vm.OnDeactivated(); }
            catch (Exception ex) { Console.WriteLine("[KnowledgeTool] dispose failed: " + ex.Message); }
        }
    }
}
