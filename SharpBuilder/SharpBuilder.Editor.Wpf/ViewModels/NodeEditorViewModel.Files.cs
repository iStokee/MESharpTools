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
	private bool ConfirmDiscardUnsavedChanges()
	{
		if (!IsDirty)
			return true;

		var result = MessageBox.Show(
			$"\"{Script.Name}\" has unsaved changes. Discard them?",
			"Unsaved changes",
			MessageBoxButton.YesNo,
			MessageBoxImage.Warning);
		return result == MessageBoxResult.Yes;
	}

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
			Filter = "SharpBuilder graph (*.orbitfsm.json)|*.orbitfsm.json|JSON (*.json)|*.json|All files|*.*",
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
		if (string.IsNullOrWhiteSpace(CurrentFilePath))
		{
			var dialog = new SaveFileDialog
			{
				Title = "Save SharpBuilder graph",
				Filter = "SharpBuilder graph (*.orbitfsm.json)|*.orbitfsm.json|JSON (*.json)|*.json|All files|*.*",
				FileName = $"{Script.Name}.orbitfsm.json",
				InitialDirectory = _scriptService.ScriptsDirectory
			};

			if (dialog.ShowDialog() != true)
				return;

			CurrentFilePath = dialog.FileName;
		}

		await _scriptService.SaveAsync(Script, CurrentFilePath);
		IsDirty = false;
		Status = $"Saved to {CurrentFilePath}";
	}

	private async Task ExportScriptAsync()
	{
		var dialog = new SaveFileDialog
		{
			Title = "Export / share SharpBuilder graph",
			Filter = "SharpBuilder graph (*.orbitfsm.json)|*.orbitfsm.json|JSON (*.json)|*.json|All files|*.*",
			FileName = $"{Script.Name}.orbitfsm.json",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
		};

		if (dialog.ShowDialog() != true)
			return;

		await _scriptService.SaveAsync(Script, dialog.FileName);
		Status = $"Exported to {dialog.FileName}";
	}
}
