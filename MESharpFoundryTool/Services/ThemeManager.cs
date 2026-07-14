using MESharp.Models;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace MESharp.Services
{
    public static class ThemeManager
    {
        private static readonly string SettingsFileName = "settings.json";
        private static readonly string LegacySettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MESharp",
            "WPFScript");
        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, SettingsFileName);

        private static Uri GetThemePackUri(string relative)
        {
            var asm = typeof(ThemeManager).Assembly;
            var asmName = asm.GetName().Name;
            return new Uri($"pack://application:,,,/{asmName};component/{relative}", UriKind.Absolute);
        }

        public static void ApplyTheme(ThemeSettings settings)
        {
            if (settings == null) return;

            var app = Application.Current;
            if (app == null) return;

            var dispatcher = GetUsableDispatcher(app);
            if (dispatcher == null)
            {
                return;
            }

            if (!dispatcher.CheckAccess())
            {
                _ = dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        ApplyThemeCore(app, settings);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Managed] ApplyTheme async failed: {ex.Message}");
                    }
                }, DispatcherPriority.Send);
                return;
            }

            ApplyThemeCore(app, settings);
        }

        private static void ApplyThemeCore(Application app, ThemeSettings settings)
        {
            // 1) Swap base theme dictionary (Light/Dark)
            var merged = app.Resources.MergedDictionaries;
            ResourceDictionary? themeDictionary = null;
            int themeIndex = -1;

            // Remove duplicate theme dictionaries while keeping the first instance to reuse.
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                var src = merged[i].Source?.ToString() ?? string.Empty;
                if (src.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                    src.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                    src.Contains("/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                    src.Contains("/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    if (themeDictionary == null)
                    {
                        themeDictionary = merged[i];
                        themeIndex = i;
                    }
                    else
                    {
                        merged.RemoveAt(i);
                    }
                }
            }

            var baseThemePath = settings.IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml";
            var baseThemeUri = GetThemePackUri(baseThemePath);

            if (themeDictionary != null)
            {
                // Ensure the base theme stays ahead of resource dictionaries that depend on it.
                var itemFlagIndex = FindDictionaryIndex(merged, "Themes/ItemFlagResources.xaml");
                if (itemFlagIndex >= 0 && themeIndex > itemFlagIndex)
                {
                    merged.Remove(themeDictionary);
                    themeIndex = itemFlagIndex;
                    merged.Insert(themeIndex, themeDictionary);
                }

                TrySetThemeDictionarySource(merged, themeDictionary, baseThemeUri);
            }
            else
            {
                var insertIndex = FindDictionaryIndex(merged, "Themes/ItemFlagResources.xaml");
                if (insertIndex < 0)
                {
                    insertIndex = merged.Count;
                }

                themeDictionary = new ResourceDictionary { Source = baseThemeUri };
                merged.Insert(insertIndex, themeDictionary);
            }

            EnsureDictionaryPresent(merged, "Themes/ItemFlagResources.xaml");
            EnsureDictionaryPresent(merged, "Themes/NavHubResources.xaml");

            // 2) Apply Primary color (will override the defaults from theme)
            var primary = ParseColor(settings.PrimaryColor) ?? (settings.IsDark ? (Color)ColorConverter.ConvertFromString("#FF7AA2FF") : (Color)ColorConverter.ConvertFromString("#FF3F51B5"));

            var primaryBrush = new SolidColorBrush(primary);
            primaryBrush.Freeze();

            var primaryFg = new SolidColorBrush(GetIdealForeground(primary));
            primaryFg.Freeze();

            // derive a soft tint of primary for selections (about 20% alpha)
            var primarySoft = Color.FromArgb(0x33, primary.R, primary.G, primary.B);
            var primarySoftBrush = new SolidColorBrush(primarySoft);
            primarySoftBrush.Freeze();

            // also expose the raw color so elements using Color resources (drop shadows, etc.) update immediately
            app.Resources["PrimaryColor"] = primary;
            app.Resources["PrimaryBrush"] = primaryBrush;
            app.Resources["PrimaryForegroundBrush"] = primaryFg;
            app.Resources["PrimarySoftBrush"] = primarySoftBrush;

            // Custom background overrides removed per UX feedback
        }

        private static Dispatcher? GetUsableDispatcher(Application app)
        {
            try
            {
                var appDispatcher = app.Dispatcher;
                if (appDispatcher != null && !appDispatcher.HasShutdownStarted && !appDispatcher.HasShutdownFinished)
                {
                    return appDispatcher;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Managed] App dispatcher unavailable for theme apply: {ex.Message}");
            }

            var currentDispatcher = Dispatcher.CurrentDispatcher;
            if (currentDispatcher != null && !currentDispatcher.HasShutdownStarted && !currentDispatcher.HasShutdownFinished)
            {
                return currentDispatcher;
            }

            return null;
        }

        private static void TrySetThemeDictionarySource(
            System.Collections.ObjectModel.Collection<ResourceDictionary> merged,
            ResourceDictionary themeDictionary,
            Uri baseThemeUri)
        {
            try
            {
                themeDictionary.Source = baseThemeUri;
            }
            catch
            {
                var index = merged.IndexOf(themeDictionary);
                if (index >= 0)
                {
                    merged.RemoveAt(index);
                    merged.Insert(index, new ResourceDictionary { Source = baseThemeUri });
                    return;
                }

                merged.Add(new ResourceDictionary { Source = baseThemeUri });
            }
        }

		public static void SaveSettings(ThemeSettings settings)
		{
			try
			{
                // Callers that only know about theme fields must not clobber the
                // persisted window size (and vice versa) — fill nulls from the file.
                if (settings.WindowWidth == null || settings.WindowHeight == null)
                {
                    if (TryLoad(SettingsFilePath, out var existing))
                    {
                        settings.WindowWidth ??= existing.WindowWidth;
                        settings.WindowHeight ??= existing.WindowHeight;
                    }
                }

				string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                Directory.CreateDirectory(SettingsDirectory);
				File.WriteAllText(SettingsFilePath, json);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Managed] Failed to persist theme settings to '{SettingsFilePath}': {ex.Message}");
                TryWriteLegacySettings(settings);
			}
		}

        /// <summary>Persist only the main-window size, leaving theme fields untouched.</summary>
        public static void SaveWindowSize(double width, double height)
        {
            if (width < 200 || height < 200) return;
            var settings = LoadSettings();
            settings.WindowWidth = Math.Round(width);
            settings.WindowHeight = Math.Round(height);
            SaveSettings(settings);
        }

        public static ThemeSettings LoadSettings()
        {
            if (TryLoad(SettingsFilePath, out var settings))
            {
                return settings;
            }

            if (TryLoad(LegacySettingsFilePath, out settings))
            {
                // migrate to new path for future runs
                SaveSettings(settings);
                return settings;
            }

            return CreateDefaultSettings();
        }

        private static Color GetIdealForeground(Color bg)
        {
            // Perceived luminance (WCAG 2.0)
            double L = 0.2126 * bg.ScR + 0.7152 * bg.ScG + 0.0722 * bg.ScB;
            return L > 0.5 ? Colors.Black : Colors.White;
        }

        public static Color? ParseColor(string? nameOrHex)
        {
            if (string.IsNullOrWhiteSpace(nameOrHex)) return null;
            try
            {
                var obj = ColorConverter.ConvertFromString(nameOrHex.Trim());
                if (obj is Color c) return c;
            }
            catch { }
            return null;
        }

        private static bool TryLoad(string path, out ThemeSettings settings)
        {
            settings = new ThemeSettings();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                settings = JsonSerializer.Deserialize<ThemeSettings>(json) ?? CreateDefaultSettings();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Managed] Failed to load theme settings from '{path}': {ex.Message}");
                return false;
            }
        }

        private static void TryWriteLegacySettings(ThemeSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(LegacySettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Managed] Legacy theme settings fallback failed: {ex.Message}");
            }
        }

        private static ThemeSettings CreateDefaultSettings()
        {
            // Default to the dark, MCP-dashboard-matched look so the tools read as one set out of the box.
            return new ThemeSettings { IsDark = true, PrimaryColor = "#FF42A5F5" };
        }

        private static int FindDictionaryIndex(System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries, string resourceName)
        {
            for (int i = 0; i < dictionaries.Count; i++)
            {
                var source = dictionaries[i].Source?.ToString() ?? string.Empty;
                if (source.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase) ||
                    source.Contains("/" + resourceName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void EnsureDictionaryPresent(System.Collections.ObjectModel.Collection<ResourceDictionary> dictionaries, string relativeResourcePath)
        {
            if (FindDictionaryIndex(dictionaries, relativeResourcePath) >= 0)
            {
                return;
            }

            dictionaries.Add(new ResourceDictionary { Source = GetThemePackUri(relativeResourcePath) });
        }
    }
}
