using System.Windows.Controls;

namespace MESharp.Views
{
    public partial class WebwalkGraphView : UserControl
    {
        public WebwalkGraphView()
        {
            InitializeComponent();
        }

        private void OnLocationSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && InspectorTabs != null) InspectorTabs.SelectedIndex = 1;
        }

        private void OnConnectionSelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && InspectorTabs != null) InspectorTabs.SelectedIndex = 2;
        }
    }
}
