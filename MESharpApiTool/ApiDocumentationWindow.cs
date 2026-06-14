using System.Windows;

namespace csharp_interop.Documentation
{
    /// <summary>
    /// Standalone host window for the MESharp API browser.
    /// </summary>
    public sealed class ApiDocumentationWindow : Window
    {
        private readonly ApiDocumentationSettings _settings;
        private readonly ApiDocumentationBrowser _browser;

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

            SizeChanged += OnSizeChanged;
            Closing += (_, _) => SaveCurrentSize();
            Loaded += (_, _) => _browser.UpdateCurrentWindowSize(ActualWidth, ActualHeight);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (WindowState != WindowState.Normal)
            {
                return;
            }

            SaveCurrentSize();
            _browser.UpdateCurrentWindowSize(ActualWidth, ActualHeight);
        }

        private void SaveCurrentSize()
        {
            if (WindowState != WindowState.Normal)
            {
                return;
            }

            _settings.Width = ActualWidth > 0 ? ActualWidth : Width;
            _settings.Height = ActualHeight > 0 ? ActualHeight : Height;
            _settings.Save();
        }
    }
}
