using System.Windows.Controls;
using System.Windows.Input;
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
        {
            if (DataContext is WebwalkingViewModel viewModel &&
                viewModel.LoadRouteCommand.CanExecute(null))
            {
                viewModel.LoadRouteCommand.Execute(null);
            }
        }
    }
}
