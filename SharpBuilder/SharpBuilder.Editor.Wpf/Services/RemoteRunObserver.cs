using System;
using System.Linq;
using System.Text.Json;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Editor.Wpf.Services;

/// <summary>
/// Maps SessionAgent run events onto a local copy of the same graph, driving the exact visual
/// state the in-process run trail uses (IsCurrent / IsActive / LastRunStatus / edge IsActive).
/// Works because the agent's events carry the graph's persistent node/transition GUIDs.
/// Call <see cref="Handle"/> on the UI thread.
/// </summary>
public sealed class RemoteRunObserver
{
	private readonly GraphModel _graph;
	private NodeModel? _currentNode;

	public RemoteRunObserver(GraphModel graph) => _graph = graph ?? throw new ArgumentNullException(nameof(graph));

	/// <summary>Human-readable state of the remote run, updated by every handled event.</summary>
	public string LastRunState { get; private set; } = "Attached";

	public void Handle(JsonElement evt)
	{
		if (!evt.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
			return;

		switch (kindEl.GetString())
		{
			case "run-started":
				ClearTrail();
				LastRunState = "Running";
				break;
			case "node-entered":
				if (FindNode(evt) is { } entered)
				{
					if (_currentNode != null && !ReferenceEquals(_currentNode, entered))
						_currentNode.IsCurrent = false;
					_currentNode = entered;
					entered.IsCurrent = true;
					entered.IsActive = true;
				}
				break;
			case "node-completed":
				if (FindNode(evt) is { } completed &&
					evt.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
				{
					completed.LastRunStatus = statusEl.GetString() switch
					{
						"Fail" => NodeRunStatus.Fail,
						"Success" => NodeRunStatus.Success,
						_ => completed.LastRunStatus
					};
				}
				break;
			case "transition":
				if (evt.TryGetProperty("transitionId", out var tid) && tid.TryGetGuid(out var transitionId))
				{
					var transition = _graph.Nodes.SelectMany(n => n.Transitions).FirstOrDefault(t => t.Id == transitionId);
					if (transition != null)
						transition.IsActive = true;
				}
				break;
			case "run-stopping":
				LastRunState = "Stopping";
				break;
			case "run-completed":
				EndRun("Completed");
				break;
			case "run-faulted":
				EndRun(evt.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String
					? $"Faulted: {msg.GetString()}"
					: "Faulted");
				break;
		}
	}

	private NodeModel? FindNode(JsonElement evt)
		=> evt.TryGetProperty("nodeId", out var nid) && nid.TryGetGuid(out var nodeId)
			? _graph.Nodes.FirstOrDefault(n => n.Id == nodeId)
			: null;

	private void EndRun(string state)
	{
		if (_currentNode != null)
		{
			_currentNode.IsCurrent = false;
			_currentNode = null;
		}

		LastRunState = state;
	}

	private void ClearTrail()
	{
		_currentNode = null;
		foreach (var node in _graph.Nodes)
		{
			node.IsCurrent = false;
			node.IsActive = false;
			node.LastRunStatus = NodeRunStatus.None;
			foreach (var transition in node.Transitions)
				transition.IsActive = false;
		}
	}
}
