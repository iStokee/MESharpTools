using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SharpBuilder.Editor.Wpf.Converters;

public sealed class DefinitionIdVisibilityConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var definitionId = value as string;
		var parameterText = parameter as string ?? string.Empty;
		var invert = parameterText.EndsWith("|Invert", StringComparison.OrdinalIgnoreCase);
		var expected = invert ? parameterText[..^7] : parameterText;
		var matched = string.Equals(definitionId, expected, StringComparison.OrdinalIgnoreCase);

		if (invert)
			matched = !matched;

		return matched ? Visibility.Visible : Visibility.Collapsed;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
