namespace SharpBuilder.Editor.Wpf.Services;

/// <summary>
/// Process-wide editor startup preferences. The studio shell loads these from
/// <see cref="UserSettings"/> before any canvas is created and writes them back on close, so each
/// new <c>NodeEditorViewModel</c> can read its initial state (panel collapse, mini-map behaviour)
/// without threading settings through the workspace.
/// </summary>
public static class EditorPreferences
{
	/// <summary>When true a new canvas opens with the catalog (left) panel collapsed.</summary>
	public static bool StartLeftCollapsed { get; set; }

	/// <summary>When true a new canvas opens with the inspector (right) panel collapsed.</summary>
	public static bool StartRightCollapsed { get; set; }

	/// <summary>
	/// When true the mini-map is always shown. When false it only appears while panning and then
	/// fades out after a short, visible countdown.
	/// </summary>
	public static bool MiniMapAlwaysVisible { get; set; } = true;
}
