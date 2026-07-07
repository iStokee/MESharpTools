using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Editor.Wpf.ViewModels;

/// <summary>
/// One canvas in the multi-canvas workspace: a single <see cref="NodeEditorViewModel"/> plus the
/// small amount of tab chrome (title, dirty marker, run indicator) the workspace shell binds to.
/// Each document owns an independent editor and execution engine, so several graphs can be edited
/// and run side by side against the same game session.
/// </summary>
public sealed class CanvasDocument : INotifyPropertyChanged, IDisposable
{
	private string _title = "Canvas";
	private bool _isDirty;
	private bool _isRunning;
	private GraphModel? _watchedGraph;

	public CanvasDocument(NodeEditorViewModel editor)
	{
		Editor = editor ?? throw new ArgumentNullException(nameof(editor));
		Editor.PropertyChanged += OnEditorPropertyChanged;

		SyncTitle();
		_isDirty = editor.IsDirty;
		_isRunning = editor.IsRunning;
	}

	public NodeEditorViewModel Editor { get; }

	/// <summary>Tab title — tracks the underlying graph name.</summary>
	public string Title
	{
		get => _title;
		private set => SetProperty(ref _title, value);
	}

	/// <summary>Mirrors the editor's unsaved-changes flag so the tab can show a dirty dot.</summary>
	public bool IsDirty
	{
		get => _isDirty;
		private set => SetProperty(ref _isDirty, value);
	}

	/// <summary>Mirrors the editor's run state so the tab can show a live indicator.</summary>
	public bool IsRunning
	{
		get => _isRunning;
		private set => SetProperty(ref _isRunning, value);
	}

	private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		switch (e.PropertyName)
		{
			case nameof(NodeEditorViewModel.Script):
				SyncTitle();
				break;
			case nameof(NodeEditorViewModel.IsDirty):
				IsDirty = Editor.IsDirty;
				break;
			case nameof(NodeEditorViewModel.IsRunning):
				IsRunning = Editor.IsRunning;
				break;
		}
	}

	private void SyncTitle()
	{
		// Re-point the rename watcher at the live graph (it changes when a canvas loads a new graph).
		if (!ReferenceEquals(_watchedGraph, Editor.Script))
		{
			if (_watchedGraph != null)
				_watchedGraph.PropertyChanged -= OnGraphPropertyChanged;
			_watchedGraph = Editor.Script;
			if (_watchedGraph != null)
				_watchedGraph.PropertyChanged += OnGraphPropertyChanged;
		}

		var name = Editor.Script?.Name;
		Title = string.IsNullOrWhiteSpace(name) ? "Untitled" : name;
	}

	private void OnGraphPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(GraphModel.Name))
			SyncTitle();
	}

	/// <summary>
	/// When this canvas observes a remote session, the attachment (agent client + event wiring)
	/// lives here so closing the tab tears the connection down. Null for ordinary canvases.
	/// </summary>
	public IDisposable? RemoteAttachment { get; set; }

	/// <summary>True when this canvas mirrors a remote session's run rather than a local one.</summary>
	public bool IsRemote => RemoteAttachment != null;

	public void Dispose()
	{
		try
		{
			RemoteAttachment?.Dispose();
		}
		catch
		{
			// A dead pipe on teardown is not actionable.
		}
		RemoteAttachment = null;

		Editor.PropertyChanged -= OnEditorPropertyChanged;
		if (_watchedGraph != null)
			_watchedGraph.PropertyChanged -= OnGraphPropertyChanged;
		Editor.Dispose();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (Equals(field, value)) return;
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
