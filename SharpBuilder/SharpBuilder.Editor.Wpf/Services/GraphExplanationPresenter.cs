using System;
using System.Collections.Generic;
using System.Linq;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;

namespace SharpBuilder.Editor.Wpf.Services;

public sealed record GraphExplanationPresentation(string Summary, IReadOnlyList<string> Lines);

public static class GraphExplanationPresenter
{
	public static GraphExplanationPresentation Present(GraphExplanation explanation)
	{
		if (explanation == null) throw new ArgumentNullException(nameof(explanation));

		var lines = new List<string>();
		var issueSummary = explanation.Issues.Count == 0
			? "no validation issues"
			: $"{explanation.Issues.Count} validation issue{(explanation.Issues.Count == 1 ? string.Empty : "s")}";
		var summary = $"{explanation.Nodes.Count} nodes, {explanation.Signals.Count} signals, {issueSummary}";

		lines.Add(explanation.RequiresGameApi
			? "Requires in-game API: yes"
			: "Requires in-game API: no");
		lines.Add(explanation.HasAdvancedNodes
			? "Advanced native-capture nodes: present"
			: "Advanced native-capture nodes: none");

		foreach (var issue in explanation.Issues)
			lines.Add(issue.ToString());

		foreach (var signal in explanation.Signals)
		{
			var publishers = signal.Publishers.Count == 0 ? "external only" : string.Join(", ", signal.Publishers);
			var readers = signal.Readers.Count == 0 ? "none" : string.Join(", ", signal.Readers);
			lines.Add($"Signal '{signal.Key}': written by {publishers}; read by {readers}");
		}

		foreach (var node in explanation.Nodes.Where(n => n.Maturity != NodeMaturity.Stable || !n.IsImplemented))
		{
			var maturity = node.Maturity == NodeMaturity.Stable ? "stable" : node.Maturity.ToString().ToLowerInvariant();
			var implemented = node.IsImplemented ? "implemented" : "not implemented";
			lines.Add($"Node '{node.Title}': {maturity}, {implemented}");
		}

		return new GraphExplanationPresentation(summary, lines);
	}
}
