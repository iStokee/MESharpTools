using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using csharp_interop.Documentation;
using csharp_interop.Documentation.ViewModels;
using Xunit;

namespace csharp_interop.Tests;

public sealed class ApiDocumentationBrowserUiTests
{
    [Fact]
    public void Browser_constructs_with_runtime_theme_resources_and_viewmodel()
    {
        StaTestRunner.Run(() =>
        {
            EnsureWpfApplication();

            var browser = LoadInWindow(new ApiDocumentationBrowser(), out var window);

            try
            {
                var viewModel = Assert.IsType<ApiDocBrowserViewModel>(browser.DataContext);
                Assert.NotEmpty(viewModel.AllClasses);
                Assert.NotNull(Application.Current.Resources["MahApps.Brushes.ThemeBackground"]);
                Assert.NotNull(Application.Current.Resources["MahApps.Brushes.Gray10"]);
                Assert.NotNull(Application.Current.Resources["MahApps.Colors.Accent"]);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Browser_search_box_updates_viewmodel_filter()
    {
        StaTestRunner.Run(() =>
        {
            EnsureWpfApplication();

            var browser = LoadInWindow(new ApiDocumentationBrowser(), out var window);

            try
            {
                var searchBox = Assert.IsType<TextBox>(browser.FindName("SearchBox"));
                var viewModel = Assert.IsType<ApiDocBrowserViewModel>(browser.DataContext);
                var binding = Assert.IsType<BindingExpression>(searchBox.GetBindingExpression(TextBox.TextProperty));

                searchBox.Text = "Inventory";
                binding.UpdateSource();
                StaTestRunner.PumpDispatcher();

                Assert.Equal("Inventory", viewModel.SearchText);
                Assert.NotEmpty(viewModel.DisplayedClasses);
                Assert.Contains(viewModel.DisplayedClasses, apiClass => apiClass.Name == "Inventory");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Browser_can_focus_search_box()
    {
        StaTestRunner.Run(() =>
        {
            EnsureWpfApplication();

            var browser = new ApiDocumentationBrowser();
            var searchBox = Assert.IsType<TextBox>(browser.FindName("SearchBox"));
            var window = new Window { Content = browser, Width = 900, Height = 600, ShowActivated = true };

            try
            {
                window.Show();
                StaTestRunner.PumpDispatcher();
                browser.FocusSearchBox();
                StaTestRunner.PumpDispatcher();

                Assert.True(searchBox.IsKeyboardFocusWithin || searchBox.IsFocused);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Window_hosts_browser_with_expected_shell_properties()
    {
        StaTestRunner.Run(() =>
        {
            EnsureWpfApplication();

            var window = new ApiDocumentationWindow();

            Assert.Equal("MESharp API Browser", window.Title);
            Assert.IsType<ApiDocumentationBrowser>(window.Content);
            Assert.True(window.MinWidth >= 800);
            Assert.True(window.MinHeight >= 500);
        });
    }

    [Fact]
    public void Browser_applies_dark_theme_resources_by_default()
    {
        StaTestRunner.Run(() =>
        {
            EnsureWpfApplication();

            var browser = new ApiDocumentationBrowser();

            var bg = Assert.IsType<SolidColorBrush>(browser.Resources["MahApps.Brushes.ThemeBackground"]);
            var fg = Assert.IsType<SolidColorBrush>(browser.Resources["MahApps.Brushes.ThemeForeground"]);

            // Dark background should be very dark (low luminance)
            Assert.True(bg.Color.R < 0x20 && bg.Color.G < 0x30 && bg.Color.B < 0x30,
                $"Expected dark background, got #{bg.Color.R:X2}{bg.Color.G:X2}{bg.Color.B:X2}");
            // Dark foreground (text) should be near-white
            Assert.True(fg.Color.R > 0xE0 && fg.Color.G > 0xE0 && fg.Color.B > 0xE0,
                $"Expected light foreground, got #{fg.Color.R:X2}{fg.Color.G:X2}{fg.Color.B:X2}");
        });
    }

    [Fact]
    public void Browser_switches_to_light_theme_when_IsDarkMode_toggled_false()
    {
        StaTestRunner.Run(() =>
        {
            EnsureWpfApplication();

            var browser = LoadInWindow(new ApiDocumentationBrowser(), out var window);

            try
            {
                var vm = Assert.IsType<ApiDocBrowserViewModel>(browser.DataContext);

                vm.IsDarkMode = false;
                StaTestRunner.PumpDispatcher();

                var bg = Assert.IsType<SolidColorBrush>(browser.Resources["MahApps.Brushes.ThemeBackground"]);
                var fg = Assert.IsType<SolidColorBrush>(browser.Resources["MahApps.Brushes.ThemeForeground"]);

                // Light background should be near-white
                Assert.True(bg.Color.R > 0xE0 && bg.Color.G > 0xE0 && bg.Color.B > 0xE0,
                    $"Expected light background, got #{bg.Color.R:X2}{bg.Color.G:X2}{bg.Color.B:X2}");
                // Light foreground (text) should be dark
                Assert.True(fg.Color.R < 0x40 && fg.Color.G < 0x50 && fg.Color.B < 0x60,
                    $"Expected dark foreground, got #{fg.Color.R:X2}{fg.Color.G:X2}{fg.Color.B:X2}");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Browser_round_trips_theme_dark_light_dark()
    {
        StaTestRunner.Run(() =>
        {
            EnsureWpfApplication();

            var browser = LoadInWindow(new ApiDocumentationBrowser(), out var window);

            try
            {
                var vm = Assert.IsType<ApiDocBrowserViewModel>(browser.DataContext);

                SolidColorBrush GetBg() =>
                    Assert.IsType<SolidColorBrush>(browser.Resources["MahApps.Brushes.ThemeBackground"]);

                var darkBg = GetBg().Color;

                vm.IsDarkMode = false;
                StaTestRunner.PumpDispatcher();
                var lightBg = GetBg().Color;

                vm.IsDarkMode = true;
                StaTestRunner.PumpDispatcher();
                var darkBgAgain = GetBg().Color;

                Assert.NotEqual(darkBg, lightBg);
                Assert.Equal(darkBg, darkBgAgain);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Browser_search_bar_contains_theme_toggle_button_bound_to_IsDarkMode()
    {
        StaTestRunner.Run(() =>
        {
            EnsureWpfApplication();

            var browser = LoadInWindow(new ApiDocumentationBrowser(), out var window);

            try
            {
                StaTestRunner.PumpDispatcher();

                var vm = Assert.IsType<ApiDocBrowserViewModel>(browser.DataContext);

                // Find the ToggleButton in the visual tree
                var toggle = FindVisualChild<ToggleButton>(browser);
                Assert.NotNull(toggle);

                // IsChecked must match IsDarkMode (dark by default → checked)
                Assert.True(toggle.IsChecked == true);

                vm.IsDarkMode = false;
                StaTestRunner.PumpDispatcher();
                Assert.True(toggle.IsChecked == false);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    private static void EnsureWpfApplication()
    {
        var app = Application.Current ?? new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private static T LoadInWindow<T>(T content, out Window window)
        where T : FrameworkElement
    {
        window = new Window
        {
            Content = content,
            Width = 900,
            Height = 600,
            ShowActivated = false
        };
        window.Show();
        StaTestRunner.PumpDispatcher();
        return content;
    }
}
