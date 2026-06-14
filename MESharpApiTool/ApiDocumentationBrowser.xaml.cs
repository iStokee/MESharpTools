using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using csharp_interop.native;
using csharp_interop.Documentation.ViewModels;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Control = System.Windows.Controls.Control;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using UserControl = System.Windows.Controls.UserControl;

namespace csharp_interop.Documentation
{
	/// <summary>
	/// Interaction logic for ApiDocumentationBrowser.xaml
	/// </summary>
	public partial class ApiDocumentationBrowser : UserControl
	{
		private readonly ApiDocumentationSettings _settings;
		private bool _applyingSizePreset;

		public ApiDocumentationBrowser(ApiDocumentationSettings settings = null)
		{
			_settings = settings ?? new ApiDocumentationSettings();
			EnsureStyleStubs();
			InitializeComponent();
			ApplyTheme(_settings.IsDarkMode);
			SidebarColumn.Width = new GridLength(_settings.SidebarWidth);

			var vm = new ApiDocBrowserViewModel(_settings);
			DataContext = vm;
			vm.PropertyChanged += OnViewModelPropertyChanged;
		}

		private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(ApiDocBrowserViewModel.IsDarkMode) &&
			    DataContext is ApiDocBrowserViewModel vm)
			{
				ApplyTheme(vm.IsDarkMode);
				_settings.IsDarkMode = vm.IsDarkMode;
				_settings.Save();
			}
		}

		protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
		{
			base.OnPreviewKeyDown(e);

			if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
			{
				FocusSearchBox();
				e.Handled = true;
				return;
			}

			if (DataContext is not ApiDocBrowserViewModel vm)
			{
				return;
			}

			if (e.Key == Key.Enter && vm.HasResults)
			{
				vm.SelectFirstResult();
				e.Handled = true;
				return;
			}

			if (e.Key == Key.Escape && vm.HasSearchText)
			{
				vm.SearchText = string.Empty;
				FocusSearchBox();
				e.Handled = true;
			}
		}

		internal void FocusSearchBox()
		{
			SearchBox.Focus();
			SearchBox.SelectAll();
		}

		internal void UpdateCurrentWindowSize(double width, double height)
		{
			if (_applyingSizePreset)
			{
				return;
			}

			if (DataContext is ApiDocBrowserViewModel vm)
			{
				vm.UpdateCurrentSize(width, height);
			}
		}

		private void SizePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (DataContext is not ApiDocBrowserViewModel { SelectedSizeOption: { } option } ||
			    option.IsCustom ||
			    Window.GetWindow(this) is not Window window)
			{
				return;
			}

			_applyingSizePreset = true;
			try
			{
				window.Width = option.Width;
				window.Height = option.Height;
				_settings.Width = option.Width;
				_settings.Height = option.Height;
				_settings.Save();
			}
			finally
			{
				_applyingSizePreset = false;
			}
		}

		private void SidebarSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
		{
			_settings.SidebarWidth = SidebarColumn.ActualWidth;
			_settings.Save();
		}

		/// <summary>
		/// Applies dark or light theme by writing all brush/color resources into this control's
		/// own resource dictionary. DynamicResource bindings in descendants auto-update.
		/// </summary>
		private void ApplyTheme(bool isDark)
		{
			if (Native_UI.TryGetImGuiTheme(out var imguiTheme))
			{
				ApplyImGuiTheme(imguiTheme, isDark);
				return;
			}

			if (isDark)
			{
				SetColor("MahApps.Colors.Accent",  "#FF2FB7D4");
				SetColor("MahApps.Colors.Accent3", "#FF16758C");

				SetBrush("MahApps.Brushes.ThemeBackground", "#FF101820");
				SetBrush("MahApps.Brushes.ThemeForeground", "#FFF3F6FA");
				SetBrush("MahApps.Brushes.Text",            "#FFF3F6FA");
				SetBrush("MahApps.Brushes.Accent",          "#FF2FB7D4");
				SetBrush("MahApps.Brushes.Accent2",         "#FF6ED7EA");
				SetBrush("MahApps.Brushes.Accent4",         "#FF163844");
				SetBrush("MahApps.Brushes.Gray",            "#FFB5C0CC");
				SetBrush("MahApps.Brushes.Gray1",           "#FFF2F4F8");
				SetBrush("MahApps.Brushes.Gray2",           "#FFCAD3DC");
				SetBrush("MahApps.Brushes.Gray3",           "#FFA8B4C2");
				SetBrush("MahApps.Brushes.Gray4",           "#FF8794A4");
				SetBrush("MahApps.Brushes.Gray6",           "#FF485461");
				SetBrush("MahApps.Brushes.Gray7",           "#FF3D4755");
				SetBrush("MahApps.Brushes.Gray8",           "#FF262F3B");
				SetBrush("MahApps.Brushes.Gray9",           "#FF1C242F");
				SetBrush("MahApps.Brushes.Gray10",          "#FF0B1118");
			}
			else
			{
				// Accent is darkened vs the dark-theme value so white text on the selected
				// list item (Accent background) meets WCAG AA contrast (≥4.5:1).
				SetColor("MahApps.Colors.Accent",  "#FF0C6B82");
				SetColor("MahApps.Colors.Accent3", "#FF094E62");

				SetBrush("MahApps.Brushes.ThemeBackground", "#FFF8FAFB");
				SetBrush("MahApps.Brushes.ThemeForeground", "#FF1C2A3A");
				SetBrush("MahApps.Brushes.Text",            "#FF1C2A3A");
				SetBrush("MahApps.Brushes.Accent",          "#FF0C6B82");
				SetBrush("MahApps.Brushes.Accent2",         "#FF2FB7D4");
				SetBrush("MahApps.Brushes.Accent4",         "#FFE0F5F9");
				SetBrush("MahApps.Brushes.Gray",            "#FF4A6275");
				SetBrush("MahApps.Brushes.Gray1",           "#FFD4E0E8");
				SetBrush("MahApps.Brushes.Gray2",           "#FFB8CCD8");
				SetBrush("MahApps.Brushes.Gray3",           "#FF7A96AA");
				SetBrush("MahApps.Brushes.Gray4",           "#FF5A7A8E");
				SetBrush("MahApps.Brushes.Gray6",           "#FF94ABB8");
				SetBrush("MahApps.Brushes.Gray7",           "#FFD4E0E8");
				SetBrush("MahApps.Brushes.Gray8",           "#FFE8F0F4");
				SetBrush("MahApps.Brushes.Gray9",           "#FFF0F5F7");
				SetBrush("MahApps.Brushes.Gray10",          "#FFE8F2F5");
			}
		}

		private void ApplyImGuiTheme(Native_UI.ImGuiThemeFlat theme, bool isDark)
		{
			var imguiWindowBg = Opaque(FromArgb(theme.WindowBg));
			var imguiText = Opaque(FromArgb(theme.Text));
			var imguiTextDisabled = Opaque(FromArgb(theme.TextDisabled));
			var imguiBorder = Composite(FromArgb(theme.Border), imguiWindowBg);
			var imguiFrameBg = Composite(FromArgb(theme.FrameBg), imguiWindowBg);
			var imguiFrameBgHovered = Composite(FromArgb(theme.FrameBgHovered), imguiWindowBg);
			var imguiButton = Composite(FromArgb(theme.Button), imguiWindowBg);
			var imguiButtonActive = Composite(FromArgb(theme.ButtonActive), imguiWindowBg);
			var imguiHeaderActive = Composite(FromArgb(theme.HeaderActive), imguiWindowBg);
			var accent = ChooseAccent(FromArgb(theme.CheckMark), imguiHeaderActive, imguiButtonActive, imguiText);
			var windowBg = isDark ? imguiWindowBg : Color.FromRgb(248, 250, 251);
			var text = isDark ? imguiText : Color.FromRgb(28, 42, 58);
			var textDisabled = isDark ? imguiTextDisabled : Color.FromRgb(74, 98, 117);
			var border = isDark ? imguiBorder : Blend(imguiBorder, Color.FromRgb(148, 171, 184), 0.35);
			var frameBg = isDark ? imguiFrameBg : Color.FromRgb(232, 242, 245);
			var frameBgHovered = isDark ? imguiFrameBgHovered : Color.FromRgb(224, 245, 249);
			var button = isDark ? imguiButton : Color.FromRgb(240, 245, 247);
			var popupBg = isDark ? Composite(FromArgb(theme.PopupBg), imguiWindowBg) : Color.FromRgb(248, 250, 251);

			if (!isDark && ContrastRatio(accent, Color.FromRgb(255, 255, 255)) < 4.5)
			{
				accent = DarkenUntilReadable(accent, Color.FromRgb(255, 255, 255));
			}

			var accent2 = AdjustForAccentVariant(accent, windowBg, isDark ? 0.18 : 0.12);
			var accent3 = AdjustForAccentVariant(accent, windowBg, -0.16);
			var accent4 = Blend(accent, windowBg, IsDarkColor(windowBg) ? 0.24 : 0.14);
			var gray1 = Blend(text, windowBg, 0.88);
			var gray2 = Blend(text, windowBg, 0.72);
			var gray3 = Blend(text, windowBg, 0.56);
			var gray4 = Blend(text, windowBg, 0.42);
			var gray6 = Blend(border, text, 0.42);
			var gray7 = Blend(frameBgHovered, windowBg, 0.64);
			var gray8 = Blend(frameBg, windowBg, 0.74);
			var gray9 = Blend(button, windowBg, 0.82);
			var gray10 = Blend(popupBg, windowBg, 0.70);

			SetColor("MahApps.Colors.Accent", accent);
			SetColor("MahApps.Colors.Accent3", accent3);

			SetBrush("MahApps.Brushes.ThemeBackground", windowBg);
			SetBrush("MahApps.Brushes.ThemeForeground", text);
			SetBrush("MahApps.Brushes.Text", text);
			SetBrush("MahApps.Brushes.Accent", accent);
			SetBrush("MahApps.Brushes.Accent2", accent2);
			SetBrush("MahApps.Brushes.Accent4", accent4);
			SetBrush("MahApps.Brushes.Gray", textDisabled);
			SetBrush("MahApps.Brushes.Gray1", gray1);
			SetBrush("MahApps.Brushes.Gray2", gray2);
			SetBrush("MahApps.Brushes.Gray3", gray3);
			SetBrush("MahApps.Brushes.Gray4", gray4);
			SetBrush("MahApps.Brushes.Gray6", gray6);
			SetBrush("MahApps.Brushes.Gray7", gray7);
			SetBrush("MahApps.Brushes.Gray8", gray8);
			SetBrush("MahApps.Brushes.Gray9", gray9);
			SetBrush("MahApps.Brushes.Gray10", gray10);
		}

		private void SetBrush(string key, string hex)
		{
			var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
			brush.Freeze();
			Resources[key] = brush;
			if (Application.Current != null)
			{
				Application.Current.Resources[key] = brush;
			}
		}

		private void SetBrush(string key, Color color)
		{
			var brush = new SolidColorBrush(color);
			brush.Freeze();
			Resources[key] = brush;
			if (Application.Current != null)
			{
				Application.Current.Resources[key] = brush;
			}
		}

		private void SetColor(string key, string hex)
		{
			var color = (Color)ColorConverter.ConvertFromString(hex);
			Resources[key] = color;
			if (Application.Current != null)
			{
				Application.Current.Resources[key] = color;
			}
		}

		private void SetColor(string key, Color color)
		{
			Resources[key] = color;
			if (Application.Current != null)
			{
				Application.Current.Resources[key] = color;
			}
		}

		private static Color FromArgb(uint argb)
		{
			return Color.FromArgb(
				(byte)((argb >> 24) & 0xFF),
				(byte)((argb >> 16) & 0xFF),
				(byte)((argb >> 8) & 0xFF),
				(byte)(argb & 0xFF));
		}

		private static Color Opaque(Color color)
		{
			return Color.FromRgb(color.R, color.G, color.B);
		}

		private static Color Composite(Color foreground, Color background)
		{
			var alpha = foreground.A / 255.0;
			return Color.FromRgb(
				(byte)Math.Round(foreground.R * alpha + background.R * (1.0 - alpha)),
				(byte)Math.Round(foreground.G * alpha + background.G * (1.0 - alpha)),
				(byte)Math.Round(foreground.B * alpha + background.B * (1.0 - alpha)));
		}

		private static Color Blend(Color foreground, Color background, double amount)
		{
			amount = Math.Clamp(amount, 0.0, 1.0);
			return Color.FromRgb(
				(byte)Math.Round(foreground.R * amount + background.R * (1.0 - amount)),
				(byte)Math.Round(foreground.G * amount + background.G * (1.0 - amount)),
				(byte)Math.Round(foreground.B * amount + background.B * (1.0 - amount)));
		}

		private static Color ChooseAccent(Color checkMark, Color headerActive, Color buttonActive, Color text)
		{
			var candidates = new[] { Opaque(checkMark), headerActive, buttonActive, text };
			return candidates
				.OrderByDescending(color => Saturation(color) * 2.0 + Math.Abs(Luminance(color) - 0.5))
				.First();
		}

		private static Color AdjustForAccentVariant(Color accent, Color background, double amount)
		{
			var target = amount >= 0
				? Color.FromRgb(255, 255, 255)
				: Color.FromRgb(0, 0, 0);
			return Blend(target, accent, Math.Abs(amount));
		}

		private static bool IsDarkColor(Color color)
		{
			return Luminance(color) < 0.5;
		}

		private static double Luminance(Color color)
		{
			return ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255.0;
		}

		private static double Saturation(Color color)
		{
			var max = Math.Max(color.R, Math.Max(color.G, color.B));
			var min = Math.Min(color.R, Math.Min(color.G, color.B));
			return max == 0 ? 0.0 : (max - min) / (double)max;
		}

		private static Color DarkenUntilReadable(Color color, Color background)
		{
			var adjusted = color;
			while (ContrastRatio(adjusted, background) < 4.5 && Luminance(adjusted) > 0.05)
			{
				adjusted = Blend(Color.FromRgb(0, 0, 0), adjusted, 0.08);
			}

			return adjusted;
		}

		private static double ContrastRatio(Color first, Color second)
		{
			var firstLum = RelativeLuminance(first);
			var secondLum = RelativeLuminance(second);
			var lighter = Math.Max(firstLum, secondLum);
			var darker = Math.Min(firstLum, secondLum);
			return (lighter + 0.05) / (darker + 0.05);
		}

		private static double RelativeLuminance(Color color)
		{
			static double Convert(byte channel)
			{
				var value = channel / 255.0;
				return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
			}

			return 0.2126 * Convert(color.R) + 0.7152 * Convert(color.G) + 0.0722 * Convert(color.B);
		}

		/// <summary>
		/// Adds minimal style stubs for controls that normally come from MahApps, so the browser
		/// renders correctly when running standalone (no MahApps loaded in the host application).
		/// </summary>
		private void EnsureStyleStubs()
		{
			bool StyleMissing(string key) =>
				Application.Current?.Resources.Contains(key) != true && !Resources.Contains(key);

			if (StyleMissing("MahApps.Styles.Button.Chromeless"))
				Resources.Add("MahApps.Styles.Button.Chromeless", CreateButtonStyle());

			if (StyleMissing("MahApps.Styles.ListBoxItem"))
				Resources.Add("MahApps.Styles.ListBoxItem", CreateListBoxItemStyle());

			if (StyleMissing("MahApps.Styles.ScrollBar"))
				Resources.Add("MahApps.Styles.ScrollBar", CreateScrollBarStyle(typeof(ScrollBar)));
		}

		private static Style CreateButtonStyle()
		{
			var style = new Style(typeof(Button));
			style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
			style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
			style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
			style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
			return style;
		}

		private static Style CreateListBoxItemStyle()
		{
			var style = new Style(typeof(ListBoxItem));
			style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4)));
			style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
			return style;
		}

		private static Style CreateScrollBarStyle(System.Type targetType)
		{
			var style = new Style(targetType);
			style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
			style.Setters.Add(new Setter(FrameworkElement.SnapsToDevicePixelsProperty, true));
			return style;
		}
	}
}
