using System;
using System.Diagnostics;
using System.Windows.Input;
using MESharp.Commands;

namespace MESharp.ViewModels
{
    /// <summary>
    /// The in-app coverage map (navigation hub landing screen). Owns the
    /// CoverageMapServer lifetime and exposes the URL the embedded WebView2 loads;
    /// the heavy lifting (Leaflet page, live graph/route/player data, click-to-author,
    /// generate-route, travel) all lives in the served page + CoverageMapServer API.
    /// </summary>
    public sealed class MapViewModel : BaseViewModel, IActivatableViewModel
    {
        private string _mapUrl = string.Empty;
        private string _status = "Map server not started.";
        private bool _webViewFailed;
        private string _webViewError = string.Empty;

        public string MapUrl { get => _mapUrl; private set => SetProperty(ref _mapUrl, value); }
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        /// <summary>Set by MapView when WebView2 could not be created (runtime missing, init failure).</summary>
        public bool WebViewFailed { get => _webViewFailed; set => SetProperty(ref _webViewFailed, value); }
        public string WebViewError { get => _webViewError; set => SetProperty(ref _webViewError, value); }

        public ICommand OpenInBrowserCommand { get; }

        public MapViewModel()
        {
            OpenInBrowserCommand = new RelayCommand(_ => OpenInBrowser());
            EnsureServer();
        }

        public string EnsureServer()
        {
            try
            {
                var url = Services.CoverageMapServer.Start();
                MapUrl = url;
                Status = $"Map served at {url}";
                return url;
            }
            catch (Exception ex)
            {
                Status = $"Map server failed to start: {ex.Message}";
                return string.Empty;
            }
        }

        private void OpenInBrowser()
        {
            var url = EnsureServer();
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Status = $"Could not open browser: {ex.Message}";
            }
        }

        public void OnActivated() => EnsureServer();
        public void OnDeactivated() { /* server stays up — the map keeps live data while hidden */ }
    }
}
