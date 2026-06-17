using System;
using System.IO;
using System.Text.Json;

namespace SharpBuilder.Editor.Wpf.Services;

/// <summary>
/// Persisted user preferences for the SharpBuilder studio shell (currently the window size).
/// Stored as JSON under %LOCALAPPDATA%\MESharp\SharpBuilder so it survives across sessions.
/// </summary>
public sealed class UserSettings
{
	public double WindowWidth { get; set; }
	public double WindowHeight { get; set; }

	/// <summary>Open new canvases with the catalog (left) panel collapsed.</summary>
	public bool StartLeftCollapsed { get; set; }

	/// <summary>Open new canvases with the inspector (right) panel collapsed.</summary>
	public bool StartRightCollapsed { get; set; }

	/// <summary>Keep the mini-map always visible (otherwise it auto-hides after panning).</summary>
	public bool MiniMapAlwaysVisible { get; set; } = true;
}

/// <summary>
/// Loads and saves <see cref="UserSettings"/>. All file access is best-effort: a missing or
/// corrupt file simply yields defaults so a settings problem never blocks the editor.
/// </summary>
public static class UserSettingsStore
{
	private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

	public static string SettingsPath { get; } = BuildPath();

	private static string BuildPath()
	{
		var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		return Path.Combine(root, "MESharp", "SharpBuilder", "settings.json");
	}

	public static UserSettings Load()
	{
		try
		{
			if (File.Exists(SettingsPath))
			{
				var json = File.ReadAllText(SettingsPath);
				var settings = JsonSerializer.Deserialize<UserSettings>(json);
				if (settings != null)
					return settings;
			}
		}
		catch
		{
			// Ignore — fall back to defaults.
		}

		return new UserSettings();
	}

	public static void Save(UserSettings settings)
	{
		if (settings == null)
			return;

		try
		{
			var directory = Path.GetDirectoryName(SettingsPath);
			if (!string.IsNullOrEmpty(directory))
				Directory.CreateDirectory(directory);

			File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
		}
		catch
		{
			// Best-effort persistence; never throw from a settings write.
		}
	}
}
