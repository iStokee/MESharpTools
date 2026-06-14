using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SharpBuilder.Core.Models;
using SharpBuilder.Editor.Wpf.ViewModels;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using UserControl = System.Windows.Controls.UserControl;

namespace SharpBuilder.Editor.Wpf.Views;

public partial class NodeEditorControl : UserControl
{
	private bool _isPanning;
	private Point _panStart;
	private double _panStartHorizontal;
	private double _panStartVertical;

	// Box selection state
	private bool _isBoxSelecting;
	private Point _boxSelectStart;
	private Rectangle? _selectionBox;

	// Port wiring state (drag from an out-port to another node to create or retarget a transition)
	private bool _isWiring;
	private NodeModel? _wireSourceNode;
	private TransitionModel? _retargetTransition;
	private Point _wireStartPoint;
	private System.Windows.Shapes.Path? _wirePreviewPath;

	// Zoom state
	private double _zoomLevel = 1.0;
	private const double ZoomMin = 0.25;
	private const double ZoomMax = 3.0;
	private const double ZoomStep = 0.1;

	public NodeEditorControl()
	{
		InitializeComponent();

		// If capture is lost mid-gesture (Alt-Tab, popup, etc.) reset all drag state;
		// otherwise stale flags keep eating canvas input and nothing is clickable.
		CanvasScrollViewer.LostMouseCapture += (_, _) => ResetCanvasGestures();
	}

	private void ResetCanvasGestures()
	{
		_isPanning = false;
		CanvasScrollViewer.Cursor = Cursors.Arrow;

		if (_isBoxSelecting)
		{
			_isBoxSelecting = false;
			if (_selectionBox != null)
			{
				SelectionOverlay?.Children.Remove(_selectionBox);
			}
		}

		if (_isWiring)
		{
			_isWiring = false;
			_wireSourceNode = null;
			_retargetTransition = null;
			if (_wirePreviewPath != null)
			{
				SelectionOverlay?.Children.Remove(_wirePreviewPath);
			}
		}
	}

	#region Zoom Handlers

	private void CanvasScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
			return;

		var zoomDelta = e.Delta > 0 ? ZoomStep : -ZoomStep;
		ApplyZoom(_zoomLevel + zoomDelta);
		e.Handled = true;
	}

	private void ZoomInButton_Click(object sender, RoutedEventArgs e)
	{
		ApplyZoom(_zoomLevel + ZoomStep);
	}

	private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
	{
		ApplyZoom(_zoomLevel - ZoomStep);
	}

	private void ZoomResetButton_Click(object sender, RoutedEventArgs e)
	{
		ApplyZoom(1.0);
	}

	private void ApplyZoom(double newZoom)
	{
		_zoomLevel = Math.Clamp(newZoom, ZoomMin, ZoomMax);
		CanvasScaleTransform.ScaleX = _zoomLevel;
		CanvasScaleTransform.ScaleY = _zoomLevel;
		ZoomLevelText.Text = $"{(int)(_zoomLevel * 100)}%";
	}

	#endregion

	private void NodeMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (DataContext is not NodeEditorViewModel vm)
			return;

		// Let the out-port start a wire drag instead of selecting/dragging the node.
		if (e.OriginalSource is System.Windows.Shapes.Ellipse)
			return;

		if (sender is Thumb thumb && thumb.Tag is NodeModel node)
		{
			var toggle = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
			vm.SelectNode(node, toggle);
			thumb.Focus();
			// Do NOT mark the event handled: a handled PreviewMouseLeftButtonDown suppresses
			// the bubbling MouseLeftButtonDown that Thumb needs to start its drag.
		}
	}

	private void NodeDragStarted(object sender, DragStartedEventArgs e)
	{
		if (DataContext is not NodeEditorViewModel vm)
			return;

		if (sender is Thumb thumb && thumb.Tag is NodeModel node)
		{
			// Selection already happened in NodeMouseDown; only ensure membership here so a
			// Ctrl+click drag doesn't toggle the node straight back out of the selection.
			if (!vm.SelectedNodes.Contains(node))
			{
				vm.SelectNode(node, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
			}
			thumb.Focus();
		}
	}

	private void NodeDragDelta(object sender, DragDeltaEventArgs e)
	{
		if (DataContext is not NodeEditorViewModel vm || sender is not Thumb thumb || thumb.Tag is not NodeModel node)
			return;

		// Multi-node drag moves the whole selection; a lone node moves on its own. Avoid allocating
		// a temporary collection on every drag-delta tick (this fires many times per second).
		if (vm.SelectedNodes.Count > 0 && vm.SelectedNodes.Contains(node))
		{
			foreach (var n in vm.SelectedNodes)
			{
				n.X = System.Math.Max(0, n.X + e.HorizontalChange);
				n.Y = System.Math.Max(0, n.Y + e.VerticalChange);
			}
		}
		else
		{
			node.X = System.Math.Max(0, node.X + e.HorizontalChange);
			node.Y = System.Math.Max(0, node.Y + e.VerticalChange);
		}
	}

	private void CanvasScrollViewer_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (sender is not ScrollViewer scroller)
			return;

		if (e.OriginalSource is not DependencyObject sourceElement)
			return;

		// Panning with middle mouse or space + left click
		if (e.MiddleButton == MouseButtonState.Pressed || (e.LeftButton == MouseButtonState.Pressed && Keyboard.IsKeyDown(Key.Space)))
		{
			_isPanning = true;
			_panStart = e.GetPosition(scroller);
			_panStartHorizontal = scroller.HorizontalOffset;
			_panStartVertical = scroller.VerticalOffset;
			scroller.CaptureMouse();
			scroller.Cursor = Cursors.ScrollAll;
			e.Handled = true;
			return;
		}

		// Box selection with left click on canvas background (not on a node)
		if (e.LeftButton == MouseButtonState.Pressed)
		{
			// Don't hijack built-in scroll bar interactions.
			if (HasVisualParent<ScrollBar>(sourceElement) ||
			    HasVisualParent<Track>(sourceElement) ||
			    HasVisualParent<RepeatButton>(sourceElement))
			{
				return;
			}

			// Don't treat clicks on nodes as canvas background clicks.
			if (HasVisualParent<Thumb>(sourceElement))
			{
				return;
			}

			// Only start box-select when the click lands on the canvas surface.
			var isCanvasBackground = sourceElement == CanvasContainer ||
			                         sourceElement == CanvasScrollViewer ||
			                         sourceElement == NodeCanvas ||
			                         sourceElement == SelectionOverlay ||
			                         HasVisualParent<Canvas>(sourceElement);

			if (isCanvasBackground && DataContext is NodeEditorViewModel vm)
			{
				// Clear selection on background click (unless Ctrl is held for box select)
				if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
				{
					vm.ClearSelection();
				}

				// Start box selection
				_isBoxSelecting = true;
				_boxSelectStart = e.GetPosition(CanvasContainer);

				// Create selection box visual
				if (_selectionBox == null)
				{
					_selectionBox = new Rectangle
					{
						Stroke = new SolidColorBrush(Color.FromArgb(180, 0, 122, 204)),
						StrokeThickness = 1,
						Fill = new SolidColorBrush(Color.FromArgb(40, 0, 122, 204)),
						StrokeDashArray = new DoubleCollection { 4, 2 },
						IsHitTestVisible = false
					};
				}

				scroller.CaptureMouse();
				e.Handled = true;
			}
		}
	}

	private void OutPort_OnMouseDown(object sender, MouseButtonEventArgs e)
	{
		// Ghost "+" port: drag out a brand-new transition from this node.
		if (sender is not FrameworkElement port || port.DataContext is not NodeModel node)
			return;

		var startY = Converters.NodeConnectorConverter.OutPortFirstY
			+ node.Transitions.Count * Converters.NodeConnectorConverter.OutPortRowHeight;
		BeginWiring(node, null, new Point(node.X + Converters.NodeConnectorConverter.OutPortX, node.Y + startY), e);
	}

	private void TransitionPort_OnMouseDown(object sender, MouseButtonEventArgs e)
	{
		// Existing transition port: click selects it in the inspector, drag retargets it.
		if (sender is not FrameworkElement port || port.DataContext is not TransitionModel transition)
			return;

		if (DataContext is not NodeEditorViewModel vm)
			return;

		var source = vm.Script.Nodes.FirstOrDefault(n => n.Id == transition.FromNodeId);
		if (source == null)
			return;

		vm.SelectNode(source, toggle: false);
		vm.SelectedTransition = transition;

		var startY = Converters.NodeConnectorConverter.GetOutPortY(source, transition);
		BeginWiring(source, transition, new Point(source.X + Converters.NodeConnectorConverter.OutPortX, source.Y + startY), e);
	}

	private void BeginWiring(NodeModel source, TransitionModel? retarget, Point start, MouseButtonEventArgs e)
	{
		_isWiring = true;
		_wireSourceNode = source;
		_retargetTransition = retarget;
		_wireStartPoint = start;

		if (_wirePreviewPath == null)
		{
			_wirePreviewPath = new System.Windows.Shapes.Path
			{
				Stroke = new SolidColorBrush(Color.FromArgb(220, 0x4A, 0xB6, 0xFF)),
				StrokeThickness = 2,
				StrokeDashArray = new DoubleCollection { 3, 2 },
				IsHitTestVisible = false
			};
		}

		UpdateWirePreview(e.GetPosition(CanvasContainer));
		if (SelectionOverlay != null && !SelectionOverlay.Children.Contains(_wirePreviewPath))
		{
			SelectionOverlay.Children.Add(_wirePreviewPath);
		}

		CanvasScrollViewer.CaptureMouse();
		e.Handled = true;
	}

	private void UpdateWirePreview(Point cursor)
	{
		if (_wirePreviewPath == null || _wireSourceNode == null)
			return;

		var start = _wireStartPoint;

		var controlOffset = Math.Max(40, Math.Abs(cursor.X - start.X) / 2);
		var figure = new PathFigure { StartPoint = start, IsFilled = false };
		figure.Segments.Add(new BezierSegment(
			new Point(start.X + controlOffset, start.Y),
			new Point(cursor.X - controlOffset, cursor.Y),
			cursor,
			true));

		var geometry = new PathGeometry();
		geometry.Figures.Add(figure);
		_wirePreviewPath.Data = geometry;
	}

	private void EndWiring(Point dropPoint)
	{
		var sourceNode = _wireSourceNode;
		var retarget = _retargetTransition;
		_isWiring = false;
		_wireSourceNode = null;
		_retargetTransition = null;

		if (_wirePreviewPath != null)
		{
			SelectionOverlay?.Children.Remove(_wirePreviewPath);
		}

		if (sourceNode == null || DataContext is not NodeEditorViewModel vm)
			return;

		var target = HitTestNode(dropPoint);
		if (target == null || target.Id == sourceNode.Id)
			return;

		if (retarget != null)
		{
			vm.RetargetTransition(retarget, target);
		}
		else
		{
			vm.ConnectNodes(sourceNode, target);
		}
	}

	private NodeModel? HitTestNode(Point pointInCanvas)
	{
		NodeModel? result = null;
		VisualTreeHelper.HitTest(
			CanvasContainer,
			null,
			hit =>
			{
				var current = hit.VisualHit;
				while (current != null)
				{
					if (current is Thumb { Tag: NodeModel node })
					{
						result = node;
						return HitTestResultBehavior.Stop;
					}
					current = VisualTreeHelper.GetParent(current);
				}
				return HitTestResultBehavior.Continue;
			},
			new PointHitTestParameters(pointInCanvas));
		return result;
	}

	private void CanvasScrollViewer_OnPreviewMouseMove(object sender, MouseEventArgs e)
	{
		if (sender is not ScrollViewer scroller)
			return;

		// Handle port wiring
		if (_isWiring)
		{
			UpdateWirePreview(e.GetPosition(CanvasContainer));
			e.Handled = true;
			return;
		}

		// Handle panning
		if (_isPanning)
		{
			var current = e.GetPosition(scroller);
			var delta = current - _panStart;

			scroller.ScrollToHorizontalOffset(_panStartHorizontal - delta.X);
			scroller.ScrollToVerticalOffset(_panStartVertical - delta.Y);
			e.Handled = true;
			return;
		}

		// Handle box selection
		if (_isBoxSelecting && _selectionBox != null)
		{
			var current = e.GetPosition(CanvasContainer);
			var x = Math.Min(_boxSelectStart.X, current.X);
			var y = Math.Min(_boxSelectStart.Y, current.Y);
			var width = Math.Abs(current.X - _boxSelectStart.X);
			var height = Math.Abs(current.Y - _boxSelectStart.Y);

			Canvas.SetLeft(_selectionBox, x);
			Canvas.SetTop(_selectionBox, y);
			_selectionBox.Width = width;
			_selectionBox.Height = height;

			// Add to visual tree if not already added
			if (SelectionOverlay != null && !SelectionOverlay.Children.Contains(_selectionBox))
			{
				SelectionOverlay.Children.Add(_selectionBox);
			}

			e.Handled = true;
		}
	}

	private void CanvasScrollViewer_OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
	{
		if (sender is not ScrollViewer scroller)
			return;

		// End port wiring
		if (_isWiring)
		{
			scroller.ReleaseMouseCapture();
			EndWiring(e.GetPosition(CanvasContainer));
			e.Handled = true;
			return;
		}

		// End panning
		if (_isPanning)
		{
			_isPanning = false;
			scroller.ReleaseMouseCapture();
			scroller.Cursor = Cursors.Arrow;
			e.Handled = true;
			return;
		}

		// End box selection
		if (_isBoxSelecting)
		{
			_isBoxSelecting = false;
			scroller.ReleaseMouseCapture();

			if (_selectionBox != null && DataContext is NodeEditorViewModel vm)
			{
				// Get selection bounds
				var selectRect = new Rect(
					Canvas.GetLeft(_selectionBox),
					Canvas.GetTop(_selectionBox),
					_selectionBox.Width,
					_selectionBox.Height);

				// Select nodes within bounds
				if (selectRect.Width > 5 && selectRect.Height > 5)
				{
					vm.SelectNodesInBounds(selectRect);
				}

				// Remove selection box from canvas
				SelectionOverlay?.Children.Remove(_selectionBox);
			}

			e.Handled = true;
		}
	}

	private void PaletteDefinitionCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (DataContext is not NodeEditorViewModel vm)
			return;

		if (e.OriginalSource is DependencyObject sourceElement &&
		    (HasVisualParent<ButtonBase>(sourceElement) || HasVisualParent<TextBox>(sourceElement) || HasVisualParent<ComboBox>(sourceElement)))
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

	private static bool HasVisualParent<T>(DependencyObject? child) where T : DependencyObject
	{
		var current = child;
		while (current != null)
		{
			if (current is T)
			{
				return true;
			}
			current = GetParentObject(current);
		}

		return false;
	}

	private static DependencyObject? GetParentObject(DependencyObject child)
	{
		if (child is FrameworkElement frameworkElement)
		{
			if (frameworkElement.Parent != null)
				return frameworkElement.Parent;
			if (frameworkElement.TemplatedParent != null)
				return frameworkElement.TemplatedParent;
		}

		if (child is FrameworkContentElement frameworkContentElement && frameworkContentElement.Parent != null)
			return frameworkContentElement.Parent;

		return VisualTreeHelper.GetParent(child);
	}
}
