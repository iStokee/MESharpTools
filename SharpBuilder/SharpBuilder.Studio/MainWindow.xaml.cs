using System;
using System.Linq;
using System.Windows;
using SharpBuilder.Core.Services;
using SharpBuilder.Editor.Wpf.Services;
using SharpBuilder.Editor.Wpf.ViewModels;
using MahApps.Metro.Controls;

namespace SharpBuilder.Studio;

public partial class MainWindow : MetroWindow
{
    private readonly WorkspaceViewModel _workspace;

    public MainWindow()
    {
        InitializeComponent();

        // Load persisted preferences first. Editor startup prefs (panel collapse, mini-map mode) must
        // be applied before the workspace is constructed because it creates its first canvas — and that
        // canvas's view-model reads these at construction time.
        var settings = UserSettingsStore.Load();
        EditorPreferences.StartLeftCollapsed = settings.StartLeftCollapsed;
        EditorPreferences.StartRightCollapsed = settings.StartRightCollapsed;
        EditorPreferences.MiniMapAlwaysVisible = settings.MiniMapAlwaysVisible;

        // Catalog and script loader are stateless, so all canvases share them; each canvas spins up
        // its own editor + execution engine inside the workspace.
        var catalog = new NodeCatalogService();
        var scriptService = new GraphScriptService(catalog);

        _workspace = new WorkspaceViewModel(catalog, scriptService);
        DataContext = _workspace;

        // Restore the persisted window size before the window is shown so it opens at the saved size.
        if (settings.WindowWidth >= MinWidth && settings.WindowHeight >= MinHeight)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        _workspace.SetCurrentWindowSize(Width, Height);
        _workspace.WindowSizeRequested += OnWindowSizeRequested;

        SizeChanged += OnWindowSizeChanged;
        Closing += OnWindowClosing;
    }

    private void OnWindowSizeRequested(double width, double height)
    {
        // A preset was chosen from a canvas's gear menu; normalize first so the size actually takes effect.
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        Width = Math.Max(MinWidth, width);
        Height = Math.Max(MinHeight, height);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WindowState == WindowState.Normal)
            _workspace.SetCurrentWindowSize(ActualWidth, ActualHeight);
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var dirty = _workspace.Canvases.Where(c => c.IsDirty).Select(c => c.Title).ToList();
        if (dirty.Count > 0)
        {
            var result = MessageBox.Show(
                this,
                $"Unsaved changes in: {string.Join(", ", dirty)}.\n\nClose without saving?",
                "Unsaved changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        UserSettingsStore.Save(new UserSettings
        {
            WindowWidth = WindowState == WindowState.Normal ? ActualWidth : Width,
            WindowHeight = WindowState == WindowState.Normal ? ActualHeight : Height,
            StartLeftCollapsed = EditorPreferences.StartLeftCollapsed,
            StartRightCollapsed = EditorPreferences.StartRightCollapsed,
            MiniMapAlwaysVisible = EditorPreferences.MiniMapAlwaysVisible
        });

        _workspace.WindowSizeRequested -= OnWindowSizeRequested;
        SizeChanged -= OnWindowSizeChanged;
        _workspace.Dispose();
    }
}
