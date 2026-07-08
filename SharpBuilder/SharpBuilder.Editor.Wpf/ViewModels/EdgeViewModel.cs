using System;
using System.ComponentModel;
using System.Windows.Media;
using SharpBuilder.Core.Models;
using SharpBuilder.Editor.Wpf.Converters;

namespace SharpBuilder.Editor.Wpf.ViewModels;

/// <summary>
/// One drawn connector on the canvas. Owns its geometry and refreshes it when either endpoint
/// node moves, so a node drag only recomputes the affected edges instead of rebuilding the whole
/// connector layer. Structural changes (edges added/removed/retargeted) rebuild the collection in
/// <see cref="NodeEditorViewModel"/> instead.
/// </summary>
public sealed class EdgeViewModel : INotifyPropertyChanged, IDisposable
{
	private Geometry _geometry = Geometry.Empty;

	public EdgeViewModel(NodeModel from, NodeModel to, TransitionModel transition)
	{
		From = from ?? throw new ArgumentNullException(nameof(from));
		To = to ?? throw new ArgumentNullException(nameof(to));
		Transition = transition ?? throw new ArgumentNullException(nameof(transition));

		From.PropertyChanged += OnEndpointPropertyChanged;
		if (!ReferenceEquals(From, To))
			To.PropertyChanged += OnEndpointPropertyChanged;

		UpdateGeometry();
	}

	public NodeModel From { get; }
	public NodeModel To { get; }
	public TransitionModel Transition { get; }

	public Geometry Geometry
	{
		get => _geometry;
		private set
		{
			_geometry = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Geometry)));
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void OnEndpointPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(NodeModel.X) or nameof(NodeModel.Y))
			UpdateGeometry();
	}

	private void UpdateGeometry()
		=> Geometry = NodeConnectorConverter.BuildGeometry(From, To, Transition);

	public void Dispose()
	{
		From.PropertyChanged -= OnEndpointPropertyChanged;
		if (!ReferenceEquals(From, To))
			To.PropertyChanged -= OnEndpointPropertyChanged;
	}
}
