using System;
using System.Windows;
using MESharp.ViewModels;
using MESharp.Views;

namespace MESharp
{
    /// <summary>
    /// Standalone host window for the DoAction tool. Owns the <see cref="DoActionSignalsViewModel"/>
    /// and tears it down on close so the tool's collectible AssemblyLoadContext can be reclaimed and
    /// it coexists cleanly with other tools on the shared WPF dispatcher (WpfScriptHost paradigm).
    /// </summary>
    internal sealed class DoActionHubWindow : Window
    {
        private readonly DoActionSignalsViewModel _vm;

        public DoActionHubWindow()
        {
            Title = "MESharp DoAction";
            Width = 1280;
            Height = 800;
            MinWidth = 1000;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(BackgroundProperty, "App.BackgroundBrush");
            SetResourceReference(ForegroundProperty, "App.ForegroundBrush");

            _vm = new DoActionSignalsViewModel();
            Content = new DoActionSignalsView { DataContext = _vm };

            // The VM only starts the native pump / refresh timer once activated.
            Loaded += (_, _) => _vm.OnActivated();
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Closed -= OnClosed;

            // Stops the native DoAction pump (refcounted) and the refresh timer so the hook isn't
            // left decoding clicks for nobody and the timer doesn't pin this tool's ALC.
            try
            {
                _vm.OnDeactivated();
                _vm.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DoActionTool] VM dispose failed: " + ex.Message);
            }
        }
    }
}
