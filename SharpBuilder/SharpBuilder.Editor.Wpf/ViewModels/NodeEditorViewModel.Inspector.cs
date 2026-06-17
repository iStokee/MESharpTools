using System;
using System.Collections.Generic;
using System.Linq;
using SharpBuilder.Core.Models;
using SharpBuilder.Core.Services;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public partial class NodeEditorViewModel
{
	private void EnsureDefinition(NodeModel node)
	{
		var definition = _catalogService.GetDefinition(node.DefinitionId) ?? _catalogService.GetDefaultDefinitionForType(node.Type);
		node.DefinitionId = definition.Id;
		node.DefinitionTitle = definition.Title;
		EnsureNodeParameters(node, definition);
	}

	private static NodeType ResolveNodeType(NodeDefinition definition)
	{
		if (string.Equals(definition.Id, NodeCatalogDefaults.StartId, StringComparison.OrdinalIgnoreCase))
			return NodeType.Start;
		if (string.Equals(definition.Id, NodeCatalogDefaults.TerminalId, StringComparison.OrdinalIgnoreCase))
			return NodeType.Terminal;
		if (string.Equals(definition.CategoryId, "conditions", StringComparison.OrdinalIgnoreCase))
			return NodeType.Condition;

		return NodeType.Action;
	}

	private void EnsureNodeParameters(NodeModel node, NodeDefinition definition)
	{
		if (definition.Parameters == null)
			return;

		foreach (var parameter in definition.Parameters)
		{
			var existing = node.Parameters.FirstOrDefault(p => string.Equals(p.Key, parameter.Key, StringComparison.OrdinalIgnoreCase));
			if (existing == null)
			{
				node.Parameters.Add(new NodeParameterValue
				{
					Key = parameter.Key,
					Type = parameter.Type,
					AllowMultiple = parameter.AllowMultiple,
					RawValue = parameter.DefaultValue ?? string.Empty
				});
			}
			else
			{
				existing.Type = parameter.Type;
				existing.AllowMultiple = parameter.AllowMultiple;
			}
		}

		for (var i = node.Parameters.Count - 1; i >= 0; i--)
		{
			if (definition.Parameters.All(p => !string.Equals(p.Key, node.Parameters[i].Key, StringComparison.OrdinalIgnoreCase)))
			{
				node.Parameters.RemoveAt(i);
			}
		}
	}

	private void ApplyDefinitionToNode(NodeModel node, NodeDefinition definition)
	{
		node.DefinitionId = definition.Id;
		node.DefinitionTitle = definition.Title;
		node.Type = ResolveNodeType(definition);
		EnsureNodeParameters(node, definition);
		RefreshSignals();
	}

	private void RefreshParameterBindings()
	{
		ParameterBindings.Clear();
		AdvancedParameterBindings.Clear();

		if (SelectedNode == null)
		{
			OnPropertyChanged(nameof(HasAdvancedParameters));
			return;
		}

		var definition = SelectedNodeDefinition ?? _catalogService.GetDefinition(SelectedNode.DefinitionId);
		if (definition?.Parameters == null)
		{
			OnPropertyChanged(nameof(HasAdvancedParameters));
			return;
		}

		EnsureNodeParameters(SelectedNode, definition);

		// One binding per parameter key (so owners can resolve their inline companions).
		var bindings = new Dictionary<string, NodeParamBinding>(StringComparer.OrdinalIgnoreCase);
		foreach (var parameter in definition.Parameters)
		{
			var value = SelectedNode.Parameters.FirstOrDefault(p => string.Equals(p.Key, parameter.Key, StringComparison.OrdinalIgnoreCase));
			if (value != null)
			{
				bindings[parameter.Key] = new NodeParamBinding(parameter, value);
			}
		}

		// Keys that are rendered inline beside another control are dropped from the main list.
		var inlineCompanionKeys = new HashSet<string>(
			definition.Parameters
				.Where(p => !string.IsNullOrEmpty(p.InlineCompanionKey))
				.Select(p => p.InlineCompanionKey!),
			StringComparer.OrdinalIgnoreCase);

		foreach (var parameter in definition.Parameters)
		{
			if (!bindings.TryGetValue(parameter.Key, out var binding))
				continue;

			if (inlineCompanionKeys.Contains(parameter.Key))
				continue; // rendered inline by its owning parameter

			if (!string.IsNullOrEmpty(parameter.InlineCompanionKey) &&
			    bindings.TryGetValue(parameter.InlineCompanionKey!, out var companion))
			{
				binding.InlineCompanion = companion;
			}

			if (parameter.IsAdvanced)
				AdvancedParameterBindings.Add(binding);
			else
				ParameterBindings.Add(binding);
		}

		OnPropertyChanged(nameof(ParameterBindings));
		OnPropertyChanged(nameof(AdvancedParameterBindings));
		OnPropertyChanged(nameof(HasAdvancedParameters));
	}

	private void AddListEntry(NodeParamBinding? binding)
	{
		if (binding == null)
			return;

		if (!string.IsNullOrWhiteSpace(binding.Value.RawValue))
		{
			binding.Value.RawValue += Environment.NewLine;
		}
	}

	public void ShowNodeInfo(NodeModel? node)
	{
		if (node == null)
		{
			IsNodeInfoOpen = false;
			return;
		}

		var definition = _catalogService.GetDefinition(node.DefinitionId);
		NodeInfoTitle = definition?.Title ?? node.Title;
		NodeInfoDescription = definition?.ShortDescription ?? node.Description;

		NodeInfoUsageTips.Clear();
		if (definition?.Parameters != null)
		{
			foreach (var parameter in definition.Parameters)
			{
				var requirement = parameter.IsRequired ? "required" : "optional";
				var example = string.IsNullOrWhiteSpace(parameter.Placeholder)
					? "Provide a value."
					: parameter.Placeholder;

				NodeInfoUsageTips.Add($"{parameter.Label} [{parameter.Type}, {requirement}] - {example}");
			}
		}

		if (NodeInfoUsageTips.Count == 0)
		{
			NodeInfoUsageTips.Add("No parameters needed. Connect transitions and run.");
		}

		IsNodeInfoOpen = true;
	}

	private void ShowDefinitionInfo(NodeDefinition? definition)
	{
		if (definition == null)
		{
			IsNodeInfoOpen = false;
			return;
		}

		NodeInfoTitle = definition.Title;
		NodeInfoDescription = definition.ShortDescription;
		NodeInfoUsageTips.Clear();

		if (definition.Parameters != null)
		{
			foreach (var parameter in definition.Parameters)
			{
				var requirement = parameter.IsRequired ? "required" : "optional";
				var example = string.IsNullOrWhiteSpace(parameter.Placeholder)
					? "Provide a value."
					: parameter.Placeholder;

				NodeInfoUsageTips.Add($"{parameter.Label} [{parameter.Type}, {requirement}] - {example}");
			}
		}

		if (NodeInfoUsageTips.Count == 0)
		{
			NodeInfoUsageTips.Add("No parameters needed. Add it to the canvas and connect transitions.");
		}

		IsNodeInfoOpen = true;
	}

	private void RemoveListEntry((NodeParamBinding binding, string value)? args)
	{
		if (args == null)
			return;

		var (binding, value) = args.Value;
		var filtered = binding.Value.SplitValues()
			.Where(v => !string.Equals(v, value, StringComparison.OrdinalIgnoreCase))
			.ToList();
		binding.Value.RawValue = string.Join(Environment.NewLine, filtered);
	}
}
