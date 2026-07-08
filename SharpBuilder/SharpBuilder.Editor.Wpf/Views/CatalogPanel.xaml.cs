using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SharpBuilder.Core.Models;
using SharpBuilder.Editor.Wpf.Utilities;
using SharpBuilder.Editor.Wpf.ViewModels;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using UserControl = System.Windows.Controls.UserControl;

namespace SharpBuilder.Editor.Wpf.Views;

/// <summary>
/// Node catalog column: palette cards start a drag-to-canvas gesture (drop is handled by the
/// canvas in <see cref="NodeEditorControl"/>), a plain click anchors the definition info popup.
/// </summary>
public partial class CatalogPanel : UserControl
{
	/// <summary>Drag-and-drop data format carrying a NodeDefinition from a palette card to the canvas.</summary>
	internal const string PaletteDragFormat = "SharpBuilderNodeDefinition";

	private Point _paletteDragStart;
	private NodeDefinition? _paletteDragDefinition;

	public CatalogPanel()
	{
		InitializeComponent();
	}

	private void PaletteDefinitionCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		// Arm a potential drag; the click (info) still fires via MouseLeftButtonUp if no drag starts.
		if (sender is FrameworkElement { DataContext: NodeDefinition definition })
		{
			_paletteDragStart = e.GetPosition(this);
			_paletteDragDefinition = definition;
		}
	}

	private void PaletteDefinitionCard_MouseMove(object sender, MouseEventArgs e)
	{
		if (_paletteDragDefinition == null || e.LeftButton != MouseButtonState.Pressed)
			return;

		var pos = e.GetPosition(this);
		if (Math.Abs(pos.X - _paletteDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
		    Math.Abs(pos.Y - _paletteDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
			return;

		var data = new DataObject(PaletteDragFormat, _paletteDragDefinition);
		_paletteDragDefinition = null;
		DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
	}

	private void PaletteDefinitionCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		// The press ended without a drag; drop the armed definition so a later mouse-move
		// over the card (e.g. with the button held from elsewhere) can't start a stale drag.
		_paletteDragDefinition = null;

		if (DataContext is not NodeEditorViewModel vm)
			return;

		if (e.OriginalSource is DependencyObject sourceElement &&
		    (VisualTreeUtils.HasVisualParent<ButtonBase>(sourceElement) ||
		     VisualTreeUtils.HasVisualParent<TextBox>(sourceElement) ||
		     VisualTreeUtils.HasVisualParent<ComboBox>(sourceElement)))
		{
			// Let interactive controls inside the card handle their own click behavior.
			return;
		}

		if (sender is FrameworkElement card && card.DataContext is NodeDefinition definition)
		{
			ShowDefinitionInfoNearElement(vm, definition, card);
			e.Handled = true;
		}
	}

	private void PaletteDefinitionInfoButton_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is not NodeEditorViewModel vm)
			return;

		if (sender is FrameworkElement element && element.DataContext is NodeDefinition definition)
		{
			ShowDefinitionInfoNearElement(vm, definition, element);
			e.Handled = true;
		}
	}

	private void NodeInfoPopup_OnClosed(object sender, EventArgs e)
	{
		if (DataContext is NodeEditorViewModel vm && vm.IsNodeInfoOpen)
		{
			vm.IsNodeInfoOpen = false;
		}
	}

	private void ShowDefinitionInfoNearElement(NodeEditorViewModel vm, NodeDefinition definition, FrameworkElement anchor)
	{
		vm.ShowDefinitionInfoCommand.Execute(definition);

		// Anchor node info beside the clicked palette card/control.
		NodeInfoPopup.PlacementTarget = anchor;
		NodeInfoPopup.Placement = PlacementMode.Right;
		NodeInfoPopup.HorizontalOffset = 10;
		NodeInfoPopup.VerticalOffset = 0;
		NodeInfoPopup.IsOpen = vm.IsNodeInfoOpen;
	}
}
