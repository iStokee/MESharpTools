using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SharpBuilder.Core.Models;
using UserControl = System.Windows.Controls.UserControl;

namespace SharpBuilder.Editor.Wpf.Views;

public partial class ScriptDashboardCard : UserControl
{
	public ScriptDashboardCard()
	{
		InitializeComponent();
	}

	private void DashboardResize_DragDelta(object sender, DragDeltaEventArgs e)
	{
		if (sender is Thumb { DataContext: NodeModel node })
		{
			node.DashboardWidth += e.HorizontalChange;
			node.DashboardHeight += e.VerticalChange;
			e.Handled = true;
		}
	}
}
