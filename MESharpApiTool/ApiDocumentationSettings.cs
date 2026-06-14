using System;
using System.IO;
using System.Text.Json;

namespace csharp_interop.Documentation
{
	public sealed class ApiDocumentationSettings
	{
		private const double DefaultWidth = 1180;
		private const double DefaultHeight = 780;
		private const double DefaultSidebarWidth = 320;
		private static readonly string SettingsPath = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"MESharp",
			"api-docs-viewer.json");

		public bool IsDarkMode { get; set; } = true;
		public double Width { get; set; } = DefaultWidth;
		public double Height { get; set; } = DefaultHeight;
		public double SidebarWidth { get; set; } = DefaultSidebarWidth;

		public static ApiDocumentationSettings Load()
		{
			try
			{
				if (!File.Exists(SettingsPath))
				{
					return new ApiDocumentationSettings();
				}

				var json = File.ReadAllText(SettingsPath);
				var settings = JsonSerializer.Deserialize<ApiDocumentationSettings>(json) ?? new ApiDocumentationSettings();
				settings.Width = Clamp(settings.Width, 860, 2600, DefaultWidth);
				settings.Height = Clamp(settings.Height, 560, 1800, DefaultHeight);
				settings.SidebarWidth = Clamp(settings.SidebarWidth, 250, 500, DefaultSidebarWidth);
				return settings;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ApiDocs] Failed to load viewer settings: {ex.Message}");
				return new ApiDocumentationSettings();
			}
		}

		public void Save()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
				var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(SettingsPath, json);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ApiDocs] Failed to save viewer settings: {ex.Message}");
			}
		}

		private static double Clamp(double value, double min, double max, double fallback)
		{
			if (double.IsNaN(value) || double.IsInfinity(value))
			{
				return fallback;
			}

			return Math.Min(max, Math.Max(min, value));
		}
	}
}
