using CommunityToolkit.Mvvm.ComponentModel;

namespace SharpBuilder.Editor.Wpf.Services;

/// <summary>
/// A selectable studio window size. Preset entries carry fixed dimensions; the single
/// <see cref="IsCustom"/> entry mirrors whatever size the window currently has (e.g. after a
/// manual resize) and is what gets persisted across sessions.
/// </summary>
public sealed class WindowSizeOption : ObservableObject
{
	private double _width;
	private double _height;

	public WindowSizeOption(string label, double width, double height, bool isCustom = false)
	{
		Label = label;
		_width = width;
		_height = height;
		IsCustom = isCustom;
	}

	public string Label { get; }
	public bool IsCustom { get; }

	public double Width
	{
		get => _width;
		set
		{
			if (SetProperty(ref _width, value))
				OnPropertyChanged(nameof(DisplayLabel));
		}
	}

	public double Height
	{
		get => _height;
		set
		{
			if (SetProperty(ref _height, value))
				OnPropertyChanged(nameof(DisplayLabel));
		}
	}

	public string DisplayLabel => $"{Label} ({(int)Width}×{(int)Height})";
}
