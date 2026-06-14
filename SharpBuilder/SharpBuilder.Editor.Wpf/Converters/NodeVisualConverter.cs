using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;

namespace SharpBuilder.Editor.Wpf.Converters;

/// <summary>
/// Maps a node definition id to category-driven visuals so each node family reads
/// distinctly on the canvas and in the palette.
/// ConverterParameter selects the facet: Icon | CategoryIcon | Brush | MutedBrush | SurfaceBrush | CategoryTitle.
/// </summary>
public class NodeVisualConverter : IValueConverter
{
	private static readonly NodeCatalogService Catalog = new();
	private static readonly ConcurrentDictionary<string, SolidColorBrush> BrushCache = new();
	private const PackIconMaterialKind FallbackIcon = PackIconMaterialKind.CheckboxMultipleBlankOutline;

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		var definition = Catalog.GetDefinition(value as string);
		var category = definition == null
			? null
			: Catalog.Categories.FirstOrDefault(c => string.Equals(c.Id, definition.CategoryId, StringComparison.OrdinalIgnoreCase));

		return (parameter as string) switch
		{
			"Icon" => ParseIcon(definition?.Icon),
			"CategoryIcon" => ParseIcon(category?.Icon),
			"Brush" => GetBrush(category?.AccentColor, 0xFF),
			"MutedBrush" => GetBrush(category?.AccentColor, 0x55),
			"SurfaceBrush" => GetBrush(category?.AccentColor, 0x24),
			"CategoryTitle" => category?.Title ?? string.Empty,
			_ => Binding.DoNothing
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}

	private static PackIconMaterialKind ParseIcon(string? name)
	{
		// Unknown kind names throw at render time, so always fall back to a known glyph.
		return !string.IsNullOrWhiteSpace(name) && Enum.TryParse<PackIconMaterialKind>(name, ignoreCase: true, out var kind)
			? kind
			: FallbackIcon;
	}

	private static SolidColorBrush GetBrush(string? hex, byte alpha)
	{
		var key = $"{hex ?? "#4AB6FF"}|{alpha}";
		return BrushCache.GetOrAdd(key, _ =>
		{
			Color color;
			try
			{
				color = (Color)ColorConverter.ConvertFromString(hex ?? "#4AB6FF");
			}
			catch (FormatException)
			{
				color = Color.FromRgb(0x4A, 0xB6, 0xFF);
			}

			var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
			brush.Freeze();
			return brush;
		});
	}
}
