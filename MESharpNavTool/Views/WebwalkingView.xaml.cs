using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using MESharp.ViewModels;

namespace MESharp.Views
{
    public partial class WebwalkingView : UserControl
    {
        public WebwalkingView()
        {
            InitializeComponent();
        }

        private void RouteCatalog_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            => LoadRouteAndOpenEditor();

        private void LoadRoute_Click(object sender, RoutedEventArgs e)
            => LoadRouteAndOpenEditor();

        private void LoadRouteAndOpenEditor()
        {
            if (DataContext is WebwalkingViewModel viewModel &&
                viewModel.LoadRouteCommand.CanExecute(null))
            {
                viewModel.LoadRouteCommand.Execute(null);
                if (!viewModel.IsDirty)
                    RouteTabs.SelectedIndex = 2;
            }
        }
    }
}
