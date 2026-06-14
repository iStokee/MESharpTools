using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace MESharp.Converters
{
	public class ToOpacityBrushConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is SolidColorBrush brush)
			{
				double opacity = 0.22;
				if (parameter != null && double.TryParse(parameter.ToString(), out var op))
					opacity = op;
				var color = brush.Color;
				color.A = (byte)(opacity * 255);
				return new SolidColorBrush(color);
			}
			return value;
		}
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
	}

}
