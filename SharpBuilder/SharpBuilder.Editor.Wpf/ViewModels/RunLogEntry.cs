using System;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public enum RunLogKind
{
	Info,
	Success,
	Fail,
	Error
}

/// <summary>One line in the run log panel.</summary>
public sealed record RunLogEntry(DateTime Timestamp, RunLogKind Kind, string Message)
{
	public string TimeLabel => Timestamp.ToString("HH:mm:ss");
}
