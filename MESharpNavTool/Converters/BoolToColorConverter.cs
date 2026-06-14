using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MESharp.Converters
{
	/// <summary>
	/// Converts a boolean value to a color brush.
	/// True = Green, False = Red
	/// </summary>
	public class BoolToColorConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is bool boolValue)
			{
                return FindBrush(boolValue ? "StatusTrueBrush" : "StatusFalseBrush", boolValue ? Brushes.LightGreen : Brushes.LightCoral);
			}
            return FindBrush("StatusUnknownBrush", Brushes.Gray);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}

        private static Brush FindBrush(string key, Brush fallback)
        {
            var app = Application.Current;
            if (app?.Resources[key] is Brush brush)
            {
                return brush;
            }

            return fallback;
        }
	}
}
