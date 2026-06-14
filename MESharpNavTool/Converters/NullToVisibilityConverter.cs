using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MESharp.Converters
{
	/// <summary>Not Null → Visible, Null → Collapsed. Also handles counts and collections.</summary>
	public class NullToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			// Handle null
			if (value == null)
				return Visibility.Collapsed;

			// Handle strings
			if (value is string str)
				return string.IsNullOrWhiteSpace(str) ? Visibility.Collapsed : Visibility.Visible;

			// Handle numbers (count, etc.)
			if (value is int intVal)
				return intVal > 0 ? Visibility.Visible : Visibility.Collapsed;

			// Handle collections
			if (value is ICollection collection)
				return collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

			// Default: if it exists, show it
			return Visibility.Visible;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> throw new NotImplementedException();
	}

	/// <summary>Null → Visible, Not Null → Collapsed</summary>
	public class InverseNullToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			=> value == null
				? Visibility.Visible
				: Visibility.Collapsed;

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> throw new NotImplementedException();
	}
}
