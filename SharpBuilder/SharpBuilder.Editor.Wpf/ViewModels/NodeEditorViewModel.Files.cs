using System;
using System.Threading.Tasks;
using System.Windows;
using SharpBuilder.Core.Models;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public partial class NodeEditorViewModel
{
	/// <summary>
	/// Asks whether unsaved changes may be discarded. Defaults to a message box; swappable for
	/// tests and for hosts that want their own prompt (same pattern as
	/// <see cref="WorkspaceViewModel.ConfirmCloseDirtyCanvas"/>).
	/// </summary>
	public Func<GraphModel, bool> ConfirmDiscardDirtyGraph { get; set; } = script =>
		MessageBox.Show(
			$"\"{script.Name}\" has unsaved changes. Discard them?",
			"Unsaved changes",
			MessageBoxButton.YesNo,
			MessageBoxImage.Warning,
			MessageBoxResult.No) == MessageBoxResult.Yes;

	private bool ConfirmDiscardUnsavedChanges()
		=> !IsDirty || ConfirmDiscardDirtyGraph(Script);

	private void CreateBlankScript()
	{
		if (!ConfirmDiscardUnsavedChanges())
			return;

		StopRun();
		Script = _scriptService.CreateNew("New graph");
		_editHistory.Clear();
		CurrentFilePath = null;
		RefreshSignals();
	}

	private void LoadTemplate()
	{
		if (!ConfirmDiscardUnsavedChanges())
			return;

		StopRun();
		Script = _scriptService.CreatePowerFishingTemplate();
		_editHistory.Clear();
		CurrentFilePath = null;
		RefreshSignals();
	}

	/// <summary>
	/// Replaces the current graph with a pre-built one (e.g. a workspace demo canvas). Unlike the
	/// file/template commands this does not prompt to discard changes - the caller owns that choice,
	/// since a freshly created canvas has nothing to lose.
	/// </summary>
	public void LoadGraph(GraphModel graph, string? filePath = null)
	{
		if (graph == null) throw new ArgumentNullException(nameof(graph));

		StopRun();
		Script = graph;
		_editHistory.Clear();
		CurrentFilePath = filePath;
		RefreshSignals();
	}

	private async Task LoadScriptAsync()
	{
		if (!ConfirmDiscardUnsavedChanges())
			return;

		var dialog = new OpenFileDialog
		{
			Title = "Open SharpBuilder graph",
			Filter = "SharpBuilder graph (*.builder.json)|*.builder.json|JSON (*.json)|*.json|All files|*.*",
			InitialDirectory = _scriptService.ScriptsDirectory
		};

		if (dialog.ShowDialog() != true)
			return;

		var (loaded, error) = await _scriptService.TryLoadAsync(dialog.FileName);
		if (loaded == null)
		{
			MessageBox.Show($"Unable to load the selected graph.\n\n{error}", "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
			return;
		}

		StopRun();
		Script = loaded;
		_editHistory.Clear();
		CurrentFilePath = dialog.FileName;
		RefreshSignals();
	}

	private async Task SaveScriptAsync()
	{
		var hadPath = !string.IsNullOrWhiteSpace(CurrentFilePath);
		if (!hadPath)
		{
			var dialog = new SaveFileDialog
			{
				Title = "Save SharpBuilder graph",
				Filter = "SharpBuilder graph (*.builder.json)|*.builder.json|JSON (*.json)|*.json|All files|*.*",
				FileName = $"{SuggestFileName(Script.Name)}.builder.json",
				InitialDirectory = _scriptService.ScriptsDirectory
			};

			if (dialog.ShowDialog() != true)
				return;

			CurrentFilePath = dialog.FileName;
		}

		try
		{
			await _scriptService.SaveAsync(Script, CurrentFilePath);
			IsDirty = false;
			Status = $"Saved to {CurrentFilePath}";
		}
		catch (Exception ex)
		{
			// A failed first-time save must not leave CurrentFilePath pointing at a file
			// that was never written (the next Ctrl+S would silently retry there).
			if (!hadPath)
				CurrentFilePath = null;
			Status = "Save failed";
			MessageBox.Show($"Unable to save the graph.\n\n{ex.Message}", "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private async Task ExportScriptAsync()
	{
		var dialog = new SaveFileDialog
		{
			Title = "Export / share SharpBuilder graph",
			Filter = "SharpBuilder graph (*.builder.json)|*.builder.json|JSON (*.json)|*.json|All files|*.*",
			FileName = $"{SuggestFileName(Script.Name)}.builder.json",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
		};

		if (dialog.ShowDialog() != true)
			return;

		try
		{
			await _scriptService.SaveAsync(Script, dialog.FileName);
			Status = $"Exported to {dialog.FileName}";
		}
		catch (Exception ex)
		{
			Status = "Export failed";
			MessageBox.Show($"Unable to export the graph.\n\n{ex.Message}", "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	/// <summary>Graph names are free text; strip filename-invalid characters before seeding the save dialog.</summary>
	private static string SuggestFileName(string name)
	{
		var cleaned = string.Concat((name ?? string.Empty).Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
		return string.IsNullOrWhiteSpace(cleaned) ? "untitled" : cleaned;
	}
}
