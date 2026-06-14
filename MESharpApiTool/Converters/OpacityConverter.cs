using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace csharp_interop.Documentation.Converters
{
    public class OpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                if (double.TryParse(parameter as string, out double opacity))
                {
                    return new SolidColorBrush(brush.Color) { Opacity = opacity };
                }
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
