using System;
using System.Windows;
using System.Windows.Controls;
using SharpBuilder.Core.Services;
using SharpBuilder.Editor.Wpf.ViewModels;
using SharpBuilder.Editor.Wpf.Views;

namespace MESharp;

internal sealed class SharpBuilderHostWindow : Window
{
    public SharpBuilderHostWindow()
    {
        EnsureThemeResources();

        Title = "SharpBuilder (In-Game)";
        Width = 1400;
        Height = 900;
        MinWidth = 1180;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = TryFindResource("MahApps.Brushes.ThemeBackground") as System.Windows.Media.Brush;
        Foreground = TryFindResource("MahApps.Brushes.IdealForeground") as System.Windows.Media.Brush;

        var catalog = new NodeCatalogService();
        var scriptService = new GraphScriptService(catalog);
        var executionEngine = new GraphExecutionEngine(catalog, new NodeExecutorRegistry());
        var vm = new NodeEditorViewModel(scriptService, executionEngine, catalog);

        // Dispose the view model when the window closes so its dashboard DispatcherTimer is
        // stopped. Otherwise the timer keeps ticking on the shared WPF dispatcher (pinning this
        // tool's AssemblyLoadContext and crashing any other tool hosted on the same thread).
        Closed += (_, _) => vm.Dispose();

        Content = new Grid
        {
            Children =
            {
                new NodeEditorControl
                {
                    DataContext = vm
                }
            }
        };
    }

    private static void EnsureThemeResources()
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        AddIfMissing(app.Resources.MergedDictionaries,
            "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml");
        AddIfMissing(app.Resources.MergedDictionaries,
            "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml");
        AddIfMissing(app.Resources.MergedDictionaries,
            "pack://application:,,,/MahApps.Metro;component/Styles/Themes/Dark.Blue.xaml");
    }

    private static void AddIfMissing(System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries, string uri)
    {
        foreach (var dictionary in dictionaries)
        {
            if (dictionary.Source?.OriginalString?.Equals(uri, StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(uri, UriKind.Absolute)
        });
    }
}
