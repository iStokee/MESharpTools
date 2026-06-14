using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MESharp.Converters
{
	/// <summary>
	/// Converts boolean values to tick/minus glyphs for quick status indicators.
	/// </summary>
	public class BoolToIconConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is bool flag)
			{
				return flag ? "✓" : "−";
			}
			return "—";
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> throw new NotImplementedException();
	}
}

	/// <summary>
	/// Converts boolean values to green/red SolidColorBrushes.
	/// </summary>
//	public class BoolToColorConverter : IValueConverter
//	{
//		private static readonly SolidColorBrush TrueBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113));
//		private static readonly SolidColorBrush FalseBrush = new SolidColorBrush(Color.FromRgb(231, 76, 60));
//		private static readonly SolidColorBrush UnknownBrush = new SolidColorBrush(Colors.Gray);

//		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
//		{
//			return value is bool flag ? (flag ? TrueBrush : FalseBrush) : UnknownBrush;
//		}

//		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
//			=> throw new NotImplementedException();
//	}
//}
