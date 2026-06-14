using System;
using System.Globalization;
using System.Windows.Data;
using SharpBuilder.Editor.Wpf.ViewModels;

namespace SharpBuilder.Editor.Wpf.Converters;

/// <summary>
/// Maps <see cref="CatalogSortMode"/> values to friendly labels for the catalog sort dropdown.
/// </summary>
public class CatalogSortModeLabelConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return value switch
		{
			CatalogSortMode.Category => "Category",
			CatalogSortMode.NameAscending => "Name (A–Z)",
			CatalogSortMode.NameDescending => "Name (Z–A)",
			_ => value?.ToString() ?? string.Empty
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> Binding.DoNothing;
}
