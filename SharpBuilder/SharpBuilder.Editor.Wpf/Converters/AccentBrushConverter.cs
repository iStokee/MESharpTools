using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SharpBuilder.Editor.Wpf.Converters;

/// <summary>
/// Converts a category accent hex string (#RRGGBB) into a frozen brush.
/// ConverterParameter selects the variant: "Solid" (default), "Muted" (33 alpha), "Surface" (1C alpha).
/// </summary>
public sealed class AccentBrushConverter : IValueConverter
{
	private static readonly Color FallbackColor = Color.FromRgb(0x4A, 0xB6, 0xFF);

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var color = ParseColor(value as string);
		var alpha = (parameter as string)?.ToLowerInvariant() switch
		{
			"muted" => (byte)0x33,
			"surface" => (byte)0x1C,
			_ => (byte)0xFF
		};

		var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
		brush.Freeze();
		return brush;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();

	private static Color ParseColor(string? hex)
	{
		if (string.IsNullOrWhiteSpace(hex))
			return FallbackColor;

		try
		{
			return (Color)ColorConverter.ConvertFromString(hex);
		}
		catch (FormatException)
		{
			return FallbackColor;
		}
	}
}
