using System;
using System.Windows;
using System.Windows.Media;
using SharpBuilder.Core.Models;
using Point = System.Windows.Point;

namespace SharpBuilder.Editor.Wpf.Converters;

/// <summary>
/// Node-card port layout constants and the connector geometry builder shared by the edge layer
/// (<see cref="ViewModels.EdgeViewModel"/>), wire previews, and marquee hit-testing.
/// Edges leave the source out-port (right edge) and enter the target in-port (left edge);
/// the port offsets must match the node template in NodeEditorControl.xaml.
/// </summary>
public static class NodeConnectorConverter
{
	public const double NodeWidth = 234;
	public const double OutPortX = NodeWidth - 7;
	public const double InPortX = 7;
	public const double PortY = 31;

	// Out-ports render one row per transition; row i's port center sits at
	// OutPortFirstY + i * OutPortRowHeight from the node top (12px border+padding
	// + 34px header row + 40px description row + 4px section margin + half a 22px row).
	public const double OutPortFirstY = 101;
	public const double OutPortRowHeight = 22;

	/// <summary>Card MinHeight from the node template (cards with few transitions never shrink below this).</summary>
	public const double NodeMinHeight = 130;

	public static double GetOutPortY(NodeModel from, TransitionModel? transition)
	{
		var index = transition == null ? -1 : from.Transitions.IndexOf(transition);
		return index < 0 ? PortY : OutPortFirstY + index * OutPortRowHeight;
	}

	/// <summary>
	/// Approximate on-canvas bounds of a node card, used for marquee hit-testing.
	/// Height grows with the per-transition port rows (plus the ghost "new" row and footer).
	/// </summary>
	public static Rect GetNodeBounds(NodeModel node)
	{
		var portRowsBottom = OutPortFirstY + (node.Transitions.Count + 1) * OutPortRowHeight;
		var height = Math.Max(NodeMinHeight, portRowsBottom + 30);
		return new Rect(node.X, node.Y, NodeWidth, height);
	}

	/// <summary>
	/// Builds the bezier connector between two node cards. Horizontal tangents make edges leave and
	/// enter the ports cleanly, with a minimum bow for short or backward edges. The geometry is
	/// frozen so the render thread can use it without cross-thread guards.
	/// </summary>
	public static Geometry BuildGeometry(NodeModel from, NodeModel to, TransitionModel? transition)
	{
		var start = new Point(from.X + OutPortX, from.Y + GetOutPortY(from, transition));
		var end = new Point(to.X + InPortX, to.Y + PortY);

		var controlOffset = Math.Max(40, Math.Abs(end.X - start.X) / 2);
		var control1 = new Point(start.X + controlOffset, start.Y);
		var control2 = new Point(end.X - controlOffset, end.Y);

		var figure = new PathFigure { StartPoint = start, IsFilled = false, IsClosed = false };
		figure.Segments.Add(new BezierSegment(control1, control2, end, true));

		var geometry = new PathGeometry();
		geometry.Figures.Add(figure);
		geometry.Freeze();
		return geometry;
	}

}
