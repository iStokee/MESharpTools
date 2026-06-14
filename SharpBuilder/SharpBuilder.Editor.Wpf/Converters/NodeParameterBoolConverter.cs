using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Editor.Wpf.Converters;

public sealed class NodeParameterBoolConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var node = value as NodeModel;
		var parameterText = parameter as string ?? string.Empty;
		var asVisibility = parameterText.EndsWith("|Visibility", StringComparison.OrdinalIgnoreCase);
		var invert = parameterText.Contains("|Invert", StringComparison.OrdinalIgnoreCase);
		var key = parameterText.Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

		var result = node?.Parameters.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))?.BoolValue ?? false;
		if (invert)
			result = !result;

		return asVisibility ? (result ? Visibility.Visible : Visibility.Collapsed) : result;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
