using System;
using System.Windows;
using System.Windows.Threading;

namespace csharp_interop.Documentation
{
    /// <summary>
    /// Standalone host window for the MESharp API browser.
    /// </summary>
    public sealed class ApiDocumentationWindow : Window
    {
        private readonly ApiDocumentationSettings _settings;
        private readonly ApiDocumentationBrowser _browser;

        // SizeChanged fires continuously while the user drags the window border. Writing the settings
        // JSON to disk on every event was a per-frame I/O storm on the UI thread; debounce so the live
        // size still tracks the browser layout but the disk write only happens once the drag settles.
        private readonly DispatcherTimer _saveDebounce = new() { Interval = TimeSpan.FromMilliseconds(600) };

        public ApiDocumentationWindow()
        {
            _settings = ApiDocumentationSettings.Load();
            _browser = new ApiDocumentationBrowser(_settings);

            Title = "MESharp API Browser";
            Width = _settings.Width;
            Height = _settings.Height;
            MinWidth = 860;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = _browser;

            _saveDebounce.Tick += (_, _) =>
            {
                _saveDebounce.Stop();
                _settings.Save();
            };

            SizeChanged += OnSizeChanged;
            Closing += (_, _) =>
            {
                _saveDebounce.Stop();
                CaptureCurrentSize();
                _settings.Save();
            };
            Loaded += (_, _) => _browser.UpdateCurrentWindowSize(ActualWidth, ActualHeight);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (WindowState != WindowState.Normal)
            {
                return;
            }

            CaptureCurrentSize();
            _browser.UpdateCurrentWindowSize(ActualWidth, ActualHeight);

            // Coalesce rapid resize events into a single disk write after the drag stops.
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        private void CaptureCurrentSize()
        {
            if (WindowState != WindowState.Normal)
            {
                return;
            }

            _settings.Width = ActualWidth > 0 ? ActualWidth : Width;
            _settings.Height = ActualHeight > 0 ? ActualHeight : Height;
        }
    }
}
