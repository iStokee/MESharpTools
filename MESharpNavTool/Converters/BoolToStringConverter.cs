using System;
using System.Globalization;
using System.Windows.Data;

namespace MESharp.Converters
{
	public class BoolToStringConverter : IValueConverter
	{
		// ConverterParameter should be in format: "TrueValue|FalseValue"
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (!(parameter is string paramStr) || !paramStr.Contains("|"))
				return value?.ToString() ?? string.Empty;

			var split = paramStr.Split('|');
			if (split.Length != 2) return value?.ToString() ?? string.Empty;

			bool b = value is bool bv && bv;
			return b ? split[0].Trim() : split[1].Trim();
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			// Not needed for one-way use in Content
			throw new NotImplementedException();
		}
	}
}
