using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using SharpBuilder.Editor.Wpf.Converters;
using UserControl = System.Windows.Controls.UserControl;

namespace SharpBuilder.Editor.Wpf.Views;

/// <summary>
/// Canvas mini-map overlay. The hosting editor pushes viewport geometry via
/// <see cref="UpdateViewport"/> on scroll/zoom, reveals it on pans via <see cref="ShowTransient"/>,
/// and applies the persisted always-visible preference via <see cref="SetAlwaysVisible"/>.
/// </summary>
public partial class MiniMapView : UserControl
{
	private const double AutoHideSeconds = 1.8;
	private const double TimerBarFullWidth = 154;

	// Must match the converter that places the node dots so the viewport box lines up.
	private const double Scale = MiniMapCoordinateConverter.DefaultScale;
	private const double Offset = MiniMapCoordinateConverter.DefaultOffset;

	private bool _alwaysVisible = true;

	public MiniMapView()
	{
		InitializeComponent();
		Loaded += (_, _) => ApplyVisibilityMode();
	}

	/// <summary>Applies the persisted mini-map mode: always-on, or hidden until the next pan.</summary>
	public void SetAlwaysVisible(bool alwaysVisible)
	{
		_alwaysVisible = alwaysVisible;
		ApplyVisibilityMode();
	}

	private void ApplyVisibilityMode()
	{
		if (MiniMapPanel == null)
			return;

		MiniMapPanel.BeginAnimation(OpacityProperty, null);
		MiniMapTimerBar.BeginAnimation(WidthProperty, null);

		if (_alwaysVisible)
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
	public void ShowTransient()
	{
		if (MiniMapPanel == null || _alwaysVisible)
			return;

		MiniMapPanel.BeginAnimation(OpacityProperty, null);
		MiniMapPanel.Opacity = 1;
		MiniMapPanel.Visibility = Visibility.Visible;

		// Countdown bar shrinks over the hold time, then the panel fades out.
		MiniMapTimerBar.Visibility = Visibility.Visible;
		MiniMapTimerBar.BeginAnimation(WidthProperty, null);
		var shrink = new DoubleAnimation(TimerBarFullWidth, 0, TimeSpan.FromSeconds(AutoHideSeconds));
		MiniMapTimerBar.BeginAnimation(WidthProperty, shrink);

		var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.35))
		{
			BeginTime = TimeSpan.FromSeconds(AutoHideSeconds)
		};
		fade.Completed += (_, _) =>
		{
			if (!_alwaysVisible)
				MiniMapPanel.Visibility = Visibility.Collapsed;
		};
		MiniMapPanel.BeginAnimation(OpacityProperty, fade);
	}

	/// <summary>Positions the opaque overlay rectangle so it tracks the visible canvas viewport.</summary>
	public void UpdateViewport(ScrollViewer scroller, double zoomLevel)
	{
		if (MiniMapViewport == null || scroller == null || zoomLevel <= 0)
			return;

		// Visible region in canvas (unscaled) coordinates.
		var canvasLeft = scroller.HorizontalOffset / zoomLevel;
		var canvasTop = scroller.VerticalOffset / zoomLevel;
		var canvasWidth = scroller.ViewportWidth / zoomLevel;
		var canvasHeight = scroller.ViewportHeight / zoomLevel;

		Canvas.SetLeft(MiniMapViewport, canvasLeft * Scale + Offset);
		Canvas.SetTop(MiniMapViewport, canvasTop * Scale + Offset);
		MiniMapViewport.Width = Math.Max(2, canvasWidth * Scale);
		MiniMapViewport.Height = Math.Max(2, canvasHeight * Scale);
	}

	private void MiniMapPanel_DragDelta(object sender, DragDeltaEventArgs e)
	{
		if (MiniMapPanel == null)
			return;

		// Anchored bottom-right; translate is negative going left/up. Clamp so it stays on-canvas.
		var host = Parent as FrameworkElement;
		var maxLeft = host != null ? Math.Max(0, host.ActualWidth - MiniMapPanel.ActualWidth - 24) : 600;
		var maxUp = host != null ? Math.Max(0, host.ActualHeight - MiniMapPanel.ActualHeight - 24) : 400;

		MiniMapTranslate.X = Math.Clamp(MiniMapTranslate.X + e.HorizontalChange, -maxLeft, 24);
		MiniMapTranslate.Y = Math.Clamp(MiniMapTranslate.Y + e.VerticalChange, -maxUp, 24);
		e.Handled = true;
	}
}
