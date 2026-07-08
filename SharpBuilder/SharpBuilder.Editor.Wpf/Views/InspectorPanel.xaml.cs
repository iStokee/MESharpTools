using UserControl = System.Windows.Controls.UserControl;

namespace SharpBuilder.Editor.Wpf.Views;

/// <summary>
/// Inspector column: script controls, node/transition editors, validation, run log, and
/// signals. Pure bindings against the editor view model — no code-behind behavior.
/// </summary>
public partial class InspectorPanel : UserControl
{
	public InspectorPanel()
	{
		InitializeComponent();
	}
}
