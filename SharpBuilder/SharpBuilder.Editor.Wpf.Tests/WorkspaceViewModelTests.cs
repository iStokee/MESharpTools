using System.Linq;
using SharpBuilder.Core.Services;
using SharpBuilder.Editor.Wpf.ViewModels;
using Xunit;

namespace SharpBuilder.Editor.Wpf.Tests;

public class WorkspaceViewModelTests
{
	private static WorkspaceViewModel CreateWorkspace()
	{
		var catalog = new NodeCatalogService();
		return new WorkspaceViewModel(catalog, new GraphScriptService(catalog));
	}

	[Fact]
	public void Constructor_OpensWithASingleActiveCanvas()
	{
		using var ws = CreateWorkspace();

		Assert.Single(ws.Canvases);
		Assert.Same(ws.Canvases[0], ws.ActiveCanvas);
		Assert.Equal(WorkspaceViewModel.LocalSession, ws.SelectedSession);
	}

	[Fact]
	public void NewCanvas_AddsAndActivates()
	{
		using var ws = CreateWorkspace();

		ws.NewCanvasCommand.Execute(null);

		Assert.Equal(2, ws.Canvases.Count);
		Assert.Same(ws.Canvases[1], ws.ActiveCanvas);
	}

	[Fact]
	public void LoadDemo_AddsTwoNamedCanvasesAndSelectsTheFirst()
	{
		using var ws = CreateWorkspace();

		ws.LoadDemoCommand.Execute(null);

		// One starter canvas plus the two demo canvases.
		Assert.Equal(3, ws.Canvases.Count);
		Assert.Contains(ws.Canvases, c => c.Title == "Woodcutting loop (demo)");
		Assert.Contains(ws.Canvases, c => c.Title == "Anti-idle heartbeat (demo)");
		Assert.Equal("Woodcutting loop (demo)", ws.ActiveCanvas!.Title);
	}

	[Fact]
	public void CloseCanvas_RemovesAndDisposesButKeepsAtLeastOne()
	{
		using var ws = CreateWorkspace();
		ws.NewCanvasCommand.Execute(null);
		var second = ws.Canvases[1];

		ws.CloseCanvasCommand.Execute(second);
		Assert.Single(ws.Canvases);
		Assert.DoesNotContain(second, ws.Canvases);

		// Last remaining canvas cannot be closed.
		Assert.False(ws.CloseCanvasCommand.CanExecute(ws.Canvases[0]));
	}

	[Fact]
	public void CloseActiveCanvas_SelectsANeighbour()
	{
		using var ws = CreateWorkspace();
		ws.NewCanvasCommand.Execute(null);
		var active = ws.ActiveCanvas!;

		ws.CloseCanvasCommand.Execute(active);

		Assert.NotNull(ws.ActiveCanvas);
		Assert.NotSame(active, ws.ActiveCanvas);
	}

	[Fact]
	public void CanvasWindowSizeRequest_IsForwardedToTheHost()
	{
		using var ws = CreateWorkspace();
		double? width = null, height = null;
		ws.WindowSizeRequested += (w, h) => { width = w; height = h; };

		var preset = ws.ActiveCanvas!.Editor.WindowSizeOptions.First(o => !o.IsCustom);
		ws.ActiveCanvas.Editor.SelectedWindowSize = preset;

		Assert.Equal(preset.Width, width);
		Assert.Equal(preset.Height, height);
	}

	[Fact]
	public void SetCurrentWindowSize_FlowsToEveryCanvas()
	{
		using var ws = CreateWorkspace();
		ws.NewCanvasCommand.Execute(null);

		ws.SetCurrentWindowSize(1601, 1001);

		Assert.All(ws.Canvases, c =>
		{
			Assert.Equal(1601, c.Editor.SelectedWindowSize!.Width);
			Assert.Equal(1001, c.Editor.SelectedWindowSize!.Height);
		});
	}

	[Fact]
	public void CanvasTitle_TracksGraphRename()
	{
		using var ws = CreateWorkspace();
		var canvas = ws.ActiveCanvas!;

		canvas.Editor.Script.Name = "Renamed graph";

		Assert.Equal("Renamed graph", canvas.Title);
	}
}
