using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.IconPacks;
using Orbit.Plugins;

namespace SharpBuilder.OrbitPlugin;

public sealed class SharpBuilderOrbitPlugin : IOrbitPlugin
{
    private const string DefaultEditorExeName = "SharpBuilder.Studio.exe";

    public string Key => "SharpBuilder";
    public string DisplayName => "SharpBuilder";
    public PackIconMaterialKind Icon => PackIconMaterialKind.GraphOutline;

    public string Version => "1.0.0";
    public string Author => "MemoryError";
    public string Description => "Launches SharpBuilder Studio, the standalone visual automation editor, as an external first-class tool.";

    public void OnLoad()
    {
        // No-op: launcher plugin does not require warmup.
    }

    public void OnUnload()
    {
        // No-op: external editor lifecycle is independent from Orbit.
    }

    public FrameworkElement CreateView(object? context = null)
    {
        return new SharpBuilderLauncherView(ResolveEditorPath());
    }

    private static string ResolveEditorPath()
    {
        var configured = Environment.GetEnvironmentVariable("SHARPBUILDER_EDITOR_EXE");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var pluginDir = AppContext.BaseDirectory;
        var byOrbitDir = Path.Combine(pluginDir, DefaultEditorExeName);
        if (File.Exists(byOrbitDir))
        {
            return byOrbitDir;
        }

        var pluginRoot = GetDefaultPluginDirectory();
        var byPluginRoot = Path.Combine(pluginRoot, DefaultEditorExeName);
        if (File.Exists(byPluginRoot))
        {
            return byPluginRoot;
        }

        var byDedicatedFolder = Path.Combine(pluginRoot, "SharpBuilder.OrbitPlugin", DefaultEditorExeName);
        if (File.Exists(byDedicatedFolder))
        {
            return byDedicatedFolder;
        }

        return byDedicatedFolder;
    }

    // Inlined from Orbit's PluginManager so this plugin builds without referencing Orbit.
    private static string GetDefaultPluginDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, "MemoryError", "Orbit_Plugins");
    }

    private sealed class SharpBuilderLauncherView : UserControl
    {
        private readonly string _editorPath;
        private readonly TextBlock _status;

        public SharpBuilderLauncherView(string editorPath)
        {
            _editorPath = editorPath;

            var panel = new StackPanel
            {
                Margin = new Thickness(16)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "SharpBuilder",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Runs SharpBuilder Studio standalone so it can be developed and shipped independently from Orbit.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            _status = new TextBlock
            {
                Text = $"Editor path: {_editorPath}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(_status);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            var launch = new Button
            {
                Content = "Launch SharpBuilder",
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0)
            };
            launch.Click += (_, __) => LaunchEditor();

            var openFolder = new Button
            {
                Content = "Open Plugin Folder",
                Padding = new Thickness(14, 6, 14, 6)
            };
            openFolder.Click += (_, __) => OpenFolder();

            actions.Children.Add(launch);
            actions.Children.Add(openFolder);
            panel.Children.Add(actions);

            Content = panel;
        }

        private void LaunchEditor()
        {
            try
            {
                if (!File.Exists(_editorPath))
                {
                    _status.Text = $"Editor executable not found: {_editorPath}";
                    return;
                }

                var existing = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_editorPath))
                    .FirstOrDefault(p =>
                    {
                        try
                        {
                            return string.Equals(p.MainModule?.FileName, _editorPath, StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false;
                        }
                    });

                if (existing != null)
                {
                    _status.Text = "SharpBuilder is already running.";
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = _editorPath,
                    WorkingDirectory = Path.GetDirectoryName(_editorPath) ?? AppContext.BaseDirectory,
                    UseShellExecute = true
                };
                Process.Start(psi);
                _status.Text = "SharpBuilder launched.";
            }
            catch (Exception ex)
            {
                _status.Text = $"Launch failed: {ex.Message}";
            }
        }

        private void OpenFolder()
        {
            try
            {
                var folder = Path.GetDirectoryName(_editorPath) ?? AppContext.BaseDirectory;
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _status.Text = $"Could not open folder: {ex.Message}";
            }
        }
    }
}
