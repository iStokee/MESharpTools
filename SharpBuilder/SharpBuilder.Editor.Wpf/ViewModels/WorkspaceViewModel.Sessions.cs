using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using SharpBuilder.Editor.Wpf.Services;
using Application = System.Windows.Application;

namespace SharpBuilder.Editor.Wpf.ViewModels;

/// <summary>One live injected session as shown in the workspace session rail.</summary>
public sealed record RemoteSessionInfo(int Pid, string? Character, string? ProcessName, string? BuilderPipe)
{
	public bool HasAgent => !string.IsNullOrWhiteSpace(BuilderPipe);

	public string DisplayName =>
		$"{(string.IsNullOrWhiteSpace(Character) ? ProcessName ?? "session" : Character)} · {Pid}{(HasAgent ? "" : "  (no agent)")}";
}

public sealed partial class WorkspaceViewModel
{
	private RemoteSessionInfo? _selectedRemoteSession;
	private string _sessionActionStatus = "";

	/// <summary>Live sessions from the discovery registry (docs/IPC_CONVENTIONS.md §2.6).</summary>
	public ObservableCollection<RemoteSessionInfo> RemoteSessions { get; } = new();

	public RemoteSessionInfo? SelectedRemoteSession
	{
		get => _selectedRemoteSession;
		set
		{
			if (Equals(_selectedRemoteSession, value)) return;
			_selectedRemoteSession = value;
			OnPropertyChanged();
			ObserveSessionCommand?.NotifyCanExecuteChanged();
			RemoteStartCommand?.NotifyCanExecuteChanged();
			RemoteStopCommand?.NotifyCanExecuteChanged();
		}
	}

	/// <summary>One-line feedback for the last session-rail action.</summary>
	public string SessionActionStatus
	{
		get => _sessionActionStatus;
		private set
		{
			if (_sessionActionStatus == value) return;
			_sessionActionStatus = value;
			OnPropertyChanged();
		}
	}

	public IRelayCommand RefreshSessionsCommand { get; private set; } = null!;
	public IAsyncRelayCommand ObserveSessionCommand { get; private set; } = null!;
	public IAsyncRelayCommand RemoteStartCommand { get; private set; } = null!;
	public IAsyncRelayCommand RemoteStopCommand { get; private set; } = null!;

	private void InitializeSessionCommands()
	{
		RefreshSessionsCommand = new RelayCommand(RefreshSessions);
		ObserveSessionCommand = new AsyncRelayCommand(ObserveSelectedSessionAsync, () => SelectedRemoteSession?.HasAgent == true);
		RemoteStartCommand = new AsyncRelayCommand(() => SendRemoteRunCommandAsync("start"), () => SelectedRemoteSession?.HasAgent == true);
		RemoteStopCommand = new AsyncRelayCommand(() => SendRemoteRunCommandAsync("stop"), () => SelectedRemoteSession?.HasAgent == true);
	}

	/// <summary>Re-reads the session registry, keeping the current selection when it survives.</summary>
	public void RefreshSessions()
	{
		var selectedPid = _selectedRemoteSession?.Pid;
		List<RemoteSessionInfo> live;
		try
		{
			live = MESharp.Services.SessionRegistry.ListLiveSessions()
				.Select(s => new RemoteSessionInfo(
					s.Pid,
					s.Character,
					s.ProcessName,
					s.Surfaces.TryGetValue("builder", out var pipe) ? pipe : null))
				.OrderBy(s => s.Pid)
				.ToList();
		}
		catch (Exception ex)
		{
			SessionActionStatus = $"Session discovery failed: {ex.Message}";
			return;
		}

		RemoteSessions.Clear();
		foreach (var session in live)
			RemoteSessions.Add(session);

		SelectedRemoteSession = live.FirstOrDefault(s => s.Pid == selectedPid) ?? live.FirstOrDefault();
		SessionActionStatus = live.Count == 0
			? "No live sessions found"
			: $"{live.Count} session{(live.Count == 1 ? "" : "s")} found";
	}

	/// <summary>
	/// Opens an observer canvas for the selected session: connects to its agent, loads the same
	/// graph the agent has loaded, and mirrors the remote run trail live on the canvas.
	/// </summary>
	private async Task ObserveSelectedSessionAsync()
	{
		var session = SelectedRemoteSession;
		if (session?.BuilderPipe == null)
			return;

		SessionAgentClient? client = null;
		try
		{
			client = await SessionAgentClient.ConnectAsync(session.BuilderPipe);
			var status = await client.RequestAsync("status");
			var graphPath = status.TryGetProperty("graphPath", out var gp) && gp.ValueKind == JsonValueKind.String
				? gp.GetString()
				: null;

			Core.Models.GraphModel graph;
			if (graphPath != null)
			{
				var (loaded, error) = await _scriptService.TryLoadAsync(graphPath);
				graph = loaded ?? _scriptService.CreateNew($"Session {session.Pid} (graph unavailable: {error})");
			}
			else
			{
				graph = _scriptService.CreateNew($"Session {session.Pid} (no graph loaded)");
			}

			var canvas = AddCanvas(graph, activate: true);
			var observer = new RemoteRunObserver(canvas.Editor.Script);
			var dispatcher = Application.Current?.Dispatcher;
			client.EventReceived += evt =>
			{
				if (dispatcher != null && !dispatcher.CheckAccess())
					dispatcher.BeginInvoke(DispatcherPriority.Background, () => observer.Handle(evt));
				else
					observer.Handle(evt);
			};
			await client.SubscribeRunAsync();

			canvas.RemoteAttachment = client;
			SessionActionStatus = $"Observing session {session.Pid}" + (graphPath != null ? $" — {graph.Name}" : "");
			client = null; // ownership transferred to the canvas
		}
		catch (Exception ex)
		{
			SessionActionStatus = $"Observe failed for {session.Pid}: {ex.Message}";
		}
		finally
		{
			client?.Dispose();
		}
	}

	/// <summary>Sends a one-shot start/stop to the selected session's agent.</summary>
	private async Task SendRemoteRunCommandAsync(string verb)
	{
		var session = SelectedRemoteSession;
		if (session?.BuilderPipe == null)
			return;

		try
		{
			using var client = await SessionAgentClient.ConnectAsync(session.BuilderPipe);
			var response = await client.RequestAsync(verb);
			var ok = response.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
			SessionActionStatus = ok
				? $"{verb} sent to session {session.Pid}"
				: $"{verb} rejected by {session.Pid}: {(response.TryGetProperty("message", out var m) ? m.GetString() : response.ToString())}";
		}
		catch (Exception ex)
		{
			SessionActionStatus = $"{verb} failed for {session.Pid}: {ex.Message}";
		}
	}
}
