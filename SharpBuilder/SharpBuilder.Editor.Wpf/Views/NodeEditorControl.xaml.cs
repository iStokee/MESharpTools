using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;
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
	private TextBlock? _wirePreviewText;

	// Edge cutting state (Shift + drag on the canvas)
	private bool _isCutting;
	private Point _cutStart;
	private System.Windows.Shapes.Line? _cutLine;

	// Zoom state
	private double _zoomLevel = 1.0;
	private const double ZoomMin = 0.25;
	private const double ZoomMax = 3.0;
	private const double ZoomStep = 0.1;

	// Catalog drag-to-canvas state
	private Point _paletteDragStart;
	private NodeDefinition? _paletteDragDefinition;
	private const string PaletteDragFormat = "SharpBuilderNodeDefinition";

	// Mini-map auto-hide
	private const double MiniMapAutoHideSeconds = 1.8;
	private const double MiniMapTimerBarFullWidth = 154;
	private NodeEditorViewModel? _observedViewModel;

	public NodeEditorControl()
	{
		InitializeComponent();

		// If capture is lost mid-gesture (Alt-Tab, popup, etc.) reset all drag state;
		// otherwise stale flags keep eating canvas input and nothing is clickable.
		CanvasScrollViewer.LostMouseCapture += (_, _) => ResetCanvasGestures();

		DataContextChanged += OnDataContextChanged;
		Loaded += (_, _) => ApplyMiniMapVisibilityMode();
	}

	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (_observedViewModel != null)
			_observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

		_observedViewModel = DataContext as NodeEditorViewModel;
		if (_observedViewModel != null)
			_observedViewModel.PropertyChanged += OnViewModelPropertyChanged;

		ApplyMiniMapVisibilityMode();
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(NodeEditorViewModel.MiniMapAlwaysVisible))
			ApplyMiniMapVisibilityMode();
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
			if (_wirePreviewText != null)
			{
				SelectionOverlay?.Children.Remove(_wirePreviewText);
			}
		}

		if (_isCutting)
		{
			_isCutting = false;
			if (_cutLine != null)
			{
				SelectionOverlay?.Children.Remove(_cutLine);
			}
		}
	}

	#region Zoom Handlers

	private void CanvasScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
			return;

		var zoomDelta = e.Delta > 0 ? ZoomStep : -ZoomStep;
		// Anchor the zoom on the point under the cursor so the canvas grows/shrinks in place
		// instead of sliding the content out from under the pointer.
		ApplyZoom(_zoomLevel + zoomDelta, e.GetPosition(CanvasScrollViewer));
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

	private void FitGraphButton_Click(object sender, RoutedEventArgs e)
	{
		FitGraphToView();
	}

	private void CenterSelectionButton_Click(object sender, RoutedEventArgs e)
	{
		if (DataContext is NodeEditorViewModel vm && vm.SelectedNodes.Count > 0)
			CenterBounds(CombineBounds(vm.SelectedNodes.Select(Converters.NodeConnectorConverter.GetNodeBounds)));
	}

	private void ApplyZoom(double newZoom, Point? anchorInViewport = null)
	{
		var oldZoom = _zoomLevel;
		_zoomLevel = Math.Clamp(newZoom, ZoomMin, ZoomMax);
		if (Math.Abs(_zoomLevel - oldZoom) < double.Epsilon)
			return;

		// Default anchor: center of the visible viewport.
		var anchor = anchorInViewport ?? new Point(
			CanvasScrollViewer.ViewportWidth / 2,
			CanvasScrollViewer.ViewportHeight / 2);

		// Canvas-space point currently under the anchor (offsets are in scaled units).
		var contentX = (CanvasScrollViewer.HorizontalOffset + anchor.X) / oldZoom;
		var contentY = (CanvasScrollViewer.VerticalOffset + anchor.Y) / oldZoom;

		CanvasScaleTransform.ScaleX = _zoomLevel;
		CanvasScaleTransform.ScaleY = _zoomLevel;
		ZoomLevelText.Text = $"{(int)(_zoomLevel * 100)}%";

		// Re-measure with the new scale before re-aligning the anchor, otherwise the scroll
		// offsets clamp against the previous extent.
		CanvasScrollViewer.UpdateLayout();
		CanvasScrollViewer.ScrollToHorizontalOffset(Math.Max(0, contentX * _zoomLevel - anchor.X));
		CanvasScrollViewer.ScrollToVerticalOffset(Math.Max(0, contentY * _zoomLevel - anchor.Y));

		UpdateMiniMapViewport();
	}

	private void FitGraphToView()
	{
		if (DataContext is not NodeEditorViewModel vm || vm.Script.Nodes.Count == 0)
			return;

		var bounds = CombineBounds(vm.Script.Nodes.Select(Converters.NodeConnectorConverter.GetNodeBounds));

		var padded = new Rect(bounds.X - 80, bounds.Y - 80, bounds.Width + 160, bounds.Height + 160);
		var zoomX = CanvasScrollViewer.ViewportWidth > 0 ? CanvasScrollViewer.ViewportWidth / Math.Max(1, padded.Width) : 1;
		var zoomY = CanvasScrollViewer.ViewportHeight > 0 ? CanvasScrollViewer.ViewportHeight / Math.Max(1, padded.Height) : 1;
		ApplyZoom(Math.Min(ZoomMax, Math.Max(ZoomMin, Math.Min(zoomX, zoomY))));
		CenterBounds(bounds);
	}

	private static Rect CombineBounds(IEnumerable<Rect> bounds)
	{
		var result = Rect.Empty;
		foreach (var rect in bounds)
		{
			if (result.IsEmpty)
				result = rect;
			else
				result.Union(rect);
		}

		return result.IsEmpty ? new Rect(0, 0, 1, 1) : result;
	}

	private void CenterBounds(Rect bounds)
	{
		var centerX = bounds.X + bounds.Width / 2;
		var centerY = bounds.Y + bounds.Height / 2;
		CanvasScrollViewer.ScrollToHorizontalOffset(Math.Max(0, centerX * _zoomLevel - CanvasScrollViewer.ViewportWidth / 2));
		CanvasScrollViewer.ScrollToVerticalOffset(Math.Max(0, centerY * _zoomLevel - CanvasScrollViewer.ViewportHeight / 2));
		UpdateMiniMapViewport();
	}

	#endregion

	#region Mini map

	// Must match MiniMapCoordinateConverter.Scale / Offset so the viewport box lines up with the dots.
	private const double MiniMapScale = 0.055;
	private const double MiniMapOffset = 4;

	private void CanvasScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
	{
		UpdateMiniMapViewport();

		// A real pan/scroll (not just a layout pass) briefly reveals the auto-hide mini-map.
		if (e.HorizontalChange != 0 || e.VerticalChange != 0)
			ShowMiniMapTransient();
	}

	private bool MiniMapAlwaysVisible => _observedViewModel?.MiniMapAlwaysVisible ?? true;

	/// <summary>Applies the persisted mini-map mode: always-on, or hidden until the next pan.</summary>
	private void ApplyMiniMapVisibilityMode()
	{
		if (MiniMapPanel == null)
			return;

		MiniMapPanel.BeginAnimation(OpacityProperty, null);
		MiniMapTimerBar.BeginAnimation(WidthProperty, null);

		if (MiniMapAlwaysVisible)
		{
			MiniMapPanel.Opacity = 1;
			MiniMapPanel.Visibility = Visibility.Visible;
			MiniMapTimerBar.Visibility = Visibility.Collapsed;
		}
		else
		{
			MiniMapPanel.Visibility = Visibility.Collapsed;
		}
	}

	/// <summary>In auto-hide mode, shows the mini-map and restarts the visible countdown before it fades.</summary>
	private void ShowMiniMapTransient()
	{
		if (MiniMapPanel == null || MiniMapAlwaysVisible)
			return;

		MiniMapPanel.BeginAnimation(OpacityProperty, null);
		MiniMapPanel.Opacity = 1;
		MiniMapPanel.Visibility = Visibility.Visible;

		// Countdown bar shrinks over the hold time, then the panel fades out.
		MiniMapTimerBar.Visibility = Visibility.Visible;
		MiniMapTimerBar.BeginAnimation(WidthProperty, null);
		var shrink = new DoubleAnimation(MiniMapTimerBarFullWidth, 0, TimeSpan.FromSeconds(MiniMapAutoHideSeconds));
		MiniMapTimerBar.BeginAnimation(WidthProperty, shrink);

		var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.35))
		{
			BeginTime = TimeSpan.FromSeconds(MiniMapAutoHideSeconds)
		};
		fade.Completed += (_, _) =>
		{
			if (!MiniMapAlwaysVisible)
				MiniMapPanel.Visibility = Visibility.Collapsed;
		};
		MiniMapPanel.BeginAnimation(OpacityProperty, fade);
	}

	private void MiniMapPanel_DragDelta(object sender, DragDeltaEventArgs e)
	{
		if (MiniMapPanel == null)
			return;

		// Panel is anchored bottom-right; translate is negative going left/up. Clamp so it stays on-canvas.
		var host = MiniMapPanel.Parent as FrameworkElement;
		var maxLeft = host != null ? Math.Max(0, host.ActualWidth - MiniMapPanel.ActualWidth - 24) : 600;
		var maxUp = host != null ? Math.Max(0, host.ActualHeight - MiniMapPanel.ActualHeight - 24) : 400;

		MiniMapTranslate.X = Math.Clamp(MiniMapTranslate.X + e.HorizontalChange, -maxLeft, 24);
		MiniMapTranslate.Y = Math.Clamp(MiniMapTranslate.Y + e.VerticalChange, -maxUp, 24);
		e.Handled = true;
	}

	/// <summary>Positions the opaque overlay rectangle so it tracks the visible viewport on the mini map.</summary>
	private void UpdateMiniMapViewport()
	{
		if (MiniMapViewport == null || _zoomLevel <= 0)
			return;

		// Visible region in canvas (unscaled) coordinates.
		var canvasLeft = CanvasScrollViewer.HorizontalOffset / _zoomLevel;
		var canvasTop = CanvasScrollViewer.VerticalOffset / _zoomLevel;
		var canvasWidth = CanvasScrollViewer.ViewportWidth / _zoomLevel;
		var canvasHeight = CanvasScrollViewer.ViewportHeight / _zoomLevel;

		Canvas.SetLeft(MiniMapViewport, canvasLeft * MiniMapScale + MiniMapOffset);
		Canvas.SetTop(MiniMapViewport, canvasTop * MiniMapScale + MiniMapOffset);
		MiniMapViewport.Width = Math.Max(2, canvasWidth * MiniMapScale);
		MiniMapViewport.Height = Math.Max(2, canvasHeight * MiniMapScale);
	}

	#endregion

	private void DashboardResize_DragDelta(object sender, DragDeltaEventArgs e)
	{
		if (sender is Thumb { DataContext: NodeModel node })
		{
			node.DashboardWidth += e.HorizontalChange;
			node.DashboardHeight += e.VerticalChange;
			e.Handled = true;
		}
	}

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
			vm.BeginGraphEditBatch("Move node");
			thumb.Focus();
		}
	}

	private void NodeDragCompleted(object sender, DragCompletedEventArgs e)
	{
		if (DataContext is NodeEditorViewModel vm)
		{
			vm.CommitGraphEditBatch();
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
				if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
				{
					BeginCutting(e.GetPosition(CanvasContainer));
					scroller.CaptureMouse();
					e.Handled = true;
					return;
				}

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

		UpdateWirePreviewState(cursor);
	}

	private void UpdateWirePreviewState(Point cursor)
	{
		if (_wirePreviewPath == null || _wireSourceNode == null || DataContext is not NodeEditorViewModel vm)
			return;

		var target = HitTestNode(cursor);
		var rule = _retargetTransition == null
			? vm.PreviewConnect(_wireSourceNode, target)
			: vm.PreviewRetarget(_retargetTransition, target);

		_wirePreviewPath.Stroke = new SolidColorBrush(rule.CanConnect
			? (rule.Severity == GraphConnectionRuleSeverity.Warning ? Color.FromRgb(0xFF, 0xC8, 0x57) : Color.FromRgb(0x4A, 0xB6, 0xFF))
			: Color.FromRgb(0xE8, 0x6E, 0x6E));

		if (_wirePreviewText == null)
		{
			_wirePreviewText = new TextBlock
			{
				Foreground = Brushes.White,
				Background = new SolidColorBrush(Color.FromArgb(225, 0x10, 0x16, 0x20)),
				Padding = new Thickness(8, 4, 8, 4),
				IsHitTestVisible = false
			};
		}

		_wirePreviewText.Text = rule.Message;
		Canvas.SetLeft(_wirePreviewText, cursor.X + 12);
		Canvas.SetTop(_wirePreviewText, cursor.Y + 12);
		if (SelectionOverlay != null && !SelectionOverlay.Children.Contains(_wirePreviewText))
		{
			SelectionOverlay.Children.Add(_wirePreviewText);
		}
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
		if (_wirePreviewText != null)
		{
			SelectionOverlay?.Children.Remove(_wirePreviewText);
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

		if (_isCutting)
		{
			UpdateCutting(e.GetPosition(CanvasContainer));
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

		if (_isCutting)
		{
			scroller.ReleaseMouseCapture();
			EndCutting(e.GetPosition(CanvasContainer));
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

	private void BeginCutting(Point start)
	{
		_isCutting = true;
		_cutStart = start;
		if (_cutLine == null)
		{
			_cutLine = new System.Windows.Shapes.Line
			{
				Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x57)),
				StrokeThickness = 2,
				StrokeDashArray = new DoubleCollection { 4, 2 },
				IsHitTestVisible = false
			};
		}

		UpdateCutting(start);
		if (SelectionOverlay != null && !SelectionOverlay.Children.Contains(_cutLine))
		{
			SelectionOverlay.Children.Add(_cutLine);
		}
	}

	private void UpdateCutting(Point current)
	{
		if (_cutLine == null)
			return;

		_cutLine.X1 = _cutStart.X;
		_cutLine.Y1 = _cutStart.Y;
		_cutLine.X2 = current.X;
		_cutLine.Y2 = current.Y;
	}

	private void EndCutting(Point end)
	{
		_isCutting = false;
		if (_cutLine != null)
		{
			SelectionOverlay?.Children.Remove(_cutLine);
		}

		if (DataContext is NodeEditorViewModel vm && (_cutStart - end).Length > 8)
		{
			var crossed = vm.FindTransitionsIntersectingLine(_cutStart, end);
			if (crossed.Count == 0)
			{
				vm.Status = "No transitions crossed";
				return;
			}

			var nodesById = vm.Script.Nodes.ToDictionary(n => n.Id);
			string Describe((NodeModel Node, TransitionModel Transition) item)
			{
				var fromTitle = item.Node.Title;
				var toTitle = nodesById.TryGetValue(item.Transition.ToNodeId, out var to) ? to.Title : "(missing)";
				return $"   {fromTitle} → {toTitle}";
			}

			const int maxListed = 12;
			var lines = crossed.Take(maxListed).Select(Describe).ToList();
			if (crossed.Count > maxListed)
				lines.Add($"   …and {crossed.Count - maxListed} more");

			var prompt = crossed.Count == 1 ? "Remove 1 transition?" : $"Remove {crossed.Count} transitions?";
			var result = System.Windows.MessageBox.Show(
				System.Windows.Window.GetWindow(this),
				prompt + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines),
				"Cut transitions",
				MessageBoxButton.OKCancel,
				MessageBoxImage.Warning,
				MessageBoxResult.Cancel);

			if (result == MessageBoxResult.OK)
				vm.DeleteTransitions(crossed);
			else
				vm.Status = "Cut cancelled";
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

	private void CanvasContainer_OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = e.Data.GetDataPresent(PaletteDragFormat) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	private void CanvasContainer_OnDrop(object sender, DragEventArgs e)
	{
		if (DataContext is not NodeEditorViewModel vm || e.Data.GetData(PaletteDragFormat) is not NodeDefinition definition)
			return;

		// Place the node card's top-left a little up-and-left of the drop point (card ~240 wide).
		var p = e.GetPosition(CanvasContainer);
		vm.DropNodeFromDefinition(definition, new Point(Math.Max(0, p.X - 120), Math.Max(0, p.Y - 24)));
		e.Handled = true;
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
