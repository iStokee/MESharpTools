using System;
using System.Windows;
using System.Windows.Controls;
using MESharp.Services;
using MESharp.ViewModels;
using MESharp.Views;

namespace MESharp
{
    /// <summary>
    /// Standalone host window for the DoAction tool. Owns the <see cref="DoActionSignalsViewModel"/>, the
    /// <see cref="NamedVerbTesterViewModel"/> and the <see cref="TraceRecorderViewModel"/> (shown as tabs) and
    /// tears them down on close so the tool's collectible AssemblyLoadContext can be reclaimed and it coexists
    /// cleanly with other tools on the shared WPF dispatcher (WpfScriptHost paradigm).
    ///
    /// Only the *selected* tab's VM is activated (its native pump / refresh timers run), so switching tabs stops
    /// the work behind the hidden ones — the native DoAction hook isn't left decoding clicks for a tab nobody is
    /// looking at, and idle timers don't churn.
    /// </summary>
    internal sealed class DoActionHubWindow : Window
    {
        private readonly DoActionSignalsViewModel _signalsVm;
        private readonly NamedVerbTesterViewModel _verbsVm;
        private readonly TraceRecorderViewModel _recorderVm;

        private readonly TabControl _tabs;
        private readonly IActivatableViewModel[] _vmsByTab;
        private IActivatableViewModel? _activeVm;

        public DoActionHubWindow()
        {
            Title = "MESharp DoAction";
            MinWidth = 1000;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ApplySavedSize();
            SetResourceReference(BackgroundProperty, "App.BackgroundBrush");
            SetResourceReference(ForegroundProperty, "App.ForegroundBrush");

            _signalsVm = new DoActionSignalsViewModel();
            _verbsVm = new NamedVerbTesterViewModel();
            _recorderVm = new TraceRecorderViewModel();

            _tabs = new TabControl();
            _tabs.Items.Add(new TabItem
            {
                Header = "DoAction Signals",
                Content = new DoActionSignalsView { DataContext = _signalsVm }
            });
            _tabs.Items.Add(new TabItem
            {
                Header = "Named Verbs",
                Content = new NamedVerbTesterView { DataContext = _verbsVm }
            });
            _tabs.Items.Add(new TabItem
            {
                Header = "Trace Recorder",
                Content = new TraceRecorderView { DataContext = _recorderVm }
            });
            Content = _tabs;

            // Index-aligned with the tab order above; drives per-tab activation.
            _vmsByTab = new IActivatableViewModel[] { _signalsVm, _verbsVm, _recorderVm };

            _tabs.SelectionChanged += OnTabSelectionChanged;
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            ActivateTab(_tabs.SelectedIndex);
        }

        private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // TabControl.SelectionChanged bubbles up from any Selector inside a tab (a DataGrid/ListBox row
            // selection). Only react to the TabControl's own selection change.
            if (!ReferenceEquals(e.OriginalSource, _tabs))
            {
                return;
            }

            ActivateTab(_tabs.SelectedIndex);
        }

        private void ActivateTab(int index)
        {
            if (index < 0 || index >= _vmsByTab.Length)
            {
                return;
            }

            var next = _vmsByTab[index];
            if (ReferenceEquals(next, _activeVm))
            {
                return;
            }

            try { _activeVm?.OnDeactivated(); }
            catch (Exception ex) { Console.WriteLine("[DoActionTool] Tab deactivate failed: " + ex.Message); }

            _activeVm = next;

            try { _activeVm.OnActivated(); }
            catch (Exception ex) { Console.WriteLine("[DoActionTool] Tab activate failed: " + ex.Message); }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            Closed -= OnClosed;
            _tabs.SelectionChanged -= OnTabSelectionChanged;

            SaveSize();

            // Stops the native DoAction pump (refcounted) and the refresh timers so the hook isn't left decoding
            // clicks for nobody and the timers don't pin this tool's ALC. Deactivate only the active tab (the
            // others are already deactivated), then dispose them all.
            try
            {
                _activeVm?.OnDeactivated();
                _activeVm = null;

                _signalsVm.Dispose();
                _verbsVm.Dispose();
                _recorderVm.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DoActionTool] VM dispose failed: " + ex.Message);
            }
        }

        private void ApplySavedSize()
        {
            // Fall back to the tool's default footprint when nothing is persisted yet.
            Width = 1280;
            Height = 800;
            try
            {
                var settings = ThemeManager.LoadSettings();
                if (settings.WindowWidth is { } w && w >= MinWidth)
                {
                    Width = w;
                }
                if (settings.WindowHeight is { } h && h >= MinHeight)
                {
                    Height = h;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DoActionTool] Restore window size failed: " + ex.Message);
            }
        }

        private void SaveSize()
        {
            try
            {
                // Persist the restored (non-maximized) size so a maximized close doesn't save the full screen.
                var normal = WindowState == WindowState.Normal;
                var w = normal ? ActualWidth : RestoreBounds.Width;
                var h = normal ? ActualHeight : RestoreBounds.Height;
                ThemeManager.SaveWindowSize(w, h);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DoActionTool] Save window size failed: " + ex.Message);
            }
        }
    }
}
