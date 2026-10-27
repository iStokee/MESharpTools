using System;
using System.Windows;
using System.Windows.Controls;

namespace MESharp.Views
{
    public partial class NavigationHubView : UserControl
    {
        public NavigationHubView()
        {
            InitializeComponent();
        }

        private void OnReloadMapClick(object sender, RoutedEventArgs e) => LiveMap.ReloadPage();

        private void OnMapHelpClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new SectionHelpWindow(NavHelpContent.Map());
                var owner = Window.GetWindow(this);
                if (owner != null) window.Owner = owner;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NavTool] Could not open map help: {ex.Message}");
            }
        }
    }
}
