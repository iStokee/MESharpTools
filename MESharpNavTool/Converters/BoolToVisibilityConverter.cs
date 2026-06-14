using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MESharp.Converters
{
	/// <summary>True → Visible, False → Collapsed</summary>
	public class BoolToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			=> (value is bool b && b)
				? Visibility.Visible
				: Visibility.Collapsed;

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> value is Visibility v && v == Visibility.Visible;
	}

	/// <summary>True → Collapsed, False → Visible</summary>
	public class InverseBoolToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			=> (value is bool b && b)
				? Visibility.Collapsed
				: Visibility.Visible;

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> value is Visibility v && v != Visibility.Visible;
	}
}
