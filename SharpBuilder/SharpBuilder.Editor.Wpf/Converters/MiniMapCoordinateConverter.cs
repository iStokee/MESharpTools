using System;
using System.Globalization;
using System.Windows.Data;

namespace SharpBuilder.Editor.Wpf.Converters;

public sealed class MiniMapCoordinateConverter : IValueConverter
{
	/// <summary>Canvas→mini-map scale shared with the viewport-box math in NodeEditorControl.</summary>
	public const double DefaultScale = 0.055;

	/// <summary>Mini-map inner padding shared with the viewport-box math in NodeEditorControl.</summary>
	public const double DefaultOffset = 4;

	public double Scale { get; set; } = DefaultScale;
	public double Offset { get; set; } = DefaultOffset;

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var scale = Scale;
		if (parameter is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
			scale = parsed;

		return value switch
		{
			double d => Math.Max(0, d * scale + Offset),
			int i => Math.Max(0, i * scale + Offset),
			_ => Offset
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> Binding.DoNothing;
}
