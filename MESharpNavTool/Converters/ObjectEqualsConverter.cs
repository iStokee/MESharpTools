using System;
using System.Globalization;
using System.Windows.Data;

namespace MESharp.Converters
{
    public class ObjectEqualsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return "Normal";

            bool areEqual = Equals(values[0], values[1]);
            return areEqual ? "Selected" : "Normal";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
