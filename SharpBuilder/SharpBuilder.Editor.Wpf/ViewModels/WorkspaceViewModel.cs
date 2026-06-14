using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;

namespace SharpBuilder.Editor.Wpf.ViewModels;

/// <summary>
/// Top-level shell for the builder: hosts one or more <see cref="CanvasDocument"/> tabs, each an
/// independent graph/editor/engine, all targeting the same game session. This is "Phase A" of the
/// multi-canvas work — multiple graphs on a single session, which lines up with the hot-reload model
/// (load, edit, and run several routines without unloading one another). The session selector is a
/// stub today (only "Local"); routing canvases to *other* sessions is a later phase that needs ME-side
/// cross-process dispatch.
/// </summary>
public sealed class WorkspaceViewModel : INotifyPropertyChanged, IDisposable
{
	private readonly NodeCatalogService _catalog;
	private readonly GraphScriptService _scriptService;
	private CanvasDocument? _activeCanvas;
	private string _selectedSession = LocalSession;
	private int _untitledCount;

	/// <summary>Placeholder session label until cross-process session routing exists.</summary>
	public const string LocalSession = "Local session";

	public WorkspaceViewModel()
		: this(new NodeCatalogService())
	{
	}

	public WorkspaceViewModel(NodeCatalogService catalog)
		: this(catalog, new GraphScriptService(catalog))
	{
	}

	public WorkspaceViewModel(NodeCatalogService catalog, GraphScriptService scriptService)
	{
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		_scriptService = scriptService ?? throw new ArgumentNullException(nameof(scriptService));

		Sessions = new ReadOnlyObservableCollection<string>(_sessions);
		_sessions.Add(LocalSession);

		NewCanvasCommand = new RelayCommand(() => AddCanvas(activate: true));
		CloseCanvasCommand = new RelayCommand<CanvasDocument?>(CloseCanvas, CanCloseCanvas);
		LoadDemoCommand = new RelayCommand(LoadDemo);

		// Open with a single blank canvas so the shell is never empty.
		AddBlankCanvas(activate: true);
	}

	private readonly ObservableCollection<string> _sessions = new();

	public ObservableCollection<CanvasDocument> Canvases { get; } = new();

	/// <summary>Available game sessions a canvas can target. Stubbed to the local session for now.</summary>
	public ReadOnlyObservableCollection<string> Sessions { get; }

	public string SelectedSession
	{
		get => _selectedSession;
		set
		{
			if (_selectedSession == value) return;
			_selectedSession = value;
			OnPropertyChanged();
		}
	}

	public CanvasDocument? ActiveCanvas
	{
		get => _activeCanvas;
		set
		{
			if (ReferenceEquals(_activeCanvas, value)) return;
			_activeCanvas = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(HasActiveCanvas));
			CloseCanvasCommand.NotifyCanExecuteChanged();
		}
	}

	public bool HasActiveCanvas => _activeCanvas != null;

	public RelayCommand NewCanvasCommand { get; }
	public RelayCommand<CanvasDocument?> CloseCanvasCommand { get; }
	public RelayCommand LoadDemoCommand { get; }

	/// <summary>
	/// Raised when any canvas requests a window resize (via its settings gear). The host window
	/// subscribes once here instead of tracking the active editor.
	/// </summary>
	public event Action<double, double>? WindowSizeRequested;

	/// <summary>Pushes the live window size to every canvas so each gear's "Custom" entry stays accurate.</summary>
	public void SetCurrentWindowSize(double width, double height)
	{
		foreach (var canvas in Canvases)
			canvas.Editor.SetCurrentWindowSize(width, height);
	}

	/// <summary>Adds a new blank canvas and returns it.</summary>
	public CanvasDocument AddBlankCanvas(bool activate)
	{
		var editor = CreateEditor();
		editor.LoadGraph(_scriptService.CreateNew(NextUntitledName()));
		return AddDocument(editor, activate);
	}

	/// <summary>Adds a canvas hosting the supplied graph and returns it.</summary>
	public CanvasDocument AddCanvas(GraphModel graph, bool activate)
	{
		var editor = CreateEditor();
		editor.LoadGraph(graph);
		return AddDocument(editor, activate);
	}

	/// <summary>Adds a new blank canvas (command target).</summary>
	public CanvasDocument AddCanvas(bool activate) => AddBlankCanvas(activate);

	/// <summary>
	/// Loads the built-in demo: a foreground skilling loop and a background anti-idle heartbeat,
	/// each on its own canvas, illustrating two independent routines on one session.
	/// </summary>
	public void LoadDemo()
	{
		CanvasDocument? first = null;
		foreach (var graph in _scriptService.CreateMultiCanvasDemo())
		{
			var canvas = AddCanvas(graph, activate: false);
			first ??= canvas;
		}

		if (first != null)
			ActiveCanvas = first;
	}

	private bool CanCloseCanvas(CanvasDocument? canvas)
		// Keep at least one canvas open so the shell always has content.
		=> canvas != null && Canvases.Count > 1;

	public void CloseCanvas(CanvasDocument? canvas)
	{
		if (canvas == null || !Canvases.Contains(canvas) || Canvases.Count <= 1)
			return;

		var index = Canvases.IndexOf(canvas);
		Canvases.Remove(canvas);
		canvas.Dispose();

		if (ReferenceEquals(ActiveCanvas, canvas) || ActiveCanvas == null)
			ActiveCanvas = Canvases[Math.Min(index, Canvases.Count - 1)];

		CloseCanvasCommand.NotifyCanExecuteChanged();
	}

	private CanvasDocument AddDocument(NodeEditorViewModel editor, bool activate)
	{
		editor.WindowSizeRequested += OnEditorWindowSizeRequested;
		var canvas = new CanvasDocument(editor);
		Canvases.Add(canvas);

		if (activate || ActiveCanvas == null)
			ActiveCanvas = canvas;

		CloseCanvasCommand.NotifyCanExecuteChanged();
		return canvas;
	}

	private NodeEditorViewModel CreateEditor()
	{
		// Share the (stateless) catalog and script loader, but give each canvas its own engine so
		// runs are fully independent — one canvas running must not block another.
		var engine = new GraphExecutionEngine(_catalog, new NodeExecutorRegistry());
		return new NodeEditorViewModel(_scriptService, engine, _catalog);
	}

	private void OnEditorWindowSizeRequested(double width, double height)
		=> WindowSizeRequested?.Invoke(width, height);

	private string NextUntitledName()
	{
		_untitledCount++;
		return _untitledCount == 1 ? "New graph" : $"New graph {_untitledCount}";
	}

	public void Dispose()
	{
		foreach (var canvas in Canvases)
		{
			canvas.Editor.WindowSizeRequested -= OnEditorWindowSizeRequested;
			canvas.Dispose();
		}
		Canvases.Clear();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
