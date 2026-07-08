using System.Windows;
using System.Windows.Media;

namespace SharpBuilder.Editor.Wpf.Utilities;

/// <summary>Visual/logical tree walking shared by the canvas and catalog gesture handlers.</summary>
internal static class VisualTreeUtils
{
	public static bool HasVisualParent<T>(DependencyObject? child) where T : DependencyObject
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

	public static DependencyObject? GetParentObject(DependencyObject child)
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
