using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace SharpBuilder.Core.Models;

public enum NodeParamType
{
	String,
	Number,
	Bool,
	Enum,
	List,
	Coordinate,
	Entity,
	Item,
	GameObject,
	Npc,
	Area
}

public class NodeCategory
{
	public string Id { get; init; } = string.Empty;
	public string Title { get; init; } = string.Empty;
	public string Description { get; init; } = string.Empty;
	public string Icon { get; init; } = string.Empty;
	public int Order { get; init; }
	public string Slug { get; init; } = string.Empty;

	/// <summary>
	/// Identity color (hex, #RRGGBB) used by editors to tint nodes of this category.
	/// </summary>
	public string AccentColor { get; init; } = "#4AB6FF";
}

public class NodeParam
{
	public string Key { get; init; } = string.Empty;
	public string Label { get; init; } = string.Empty;
	public NodeParamType Type { get; init; } = NodeParamType.String;
	public bool IsRequired { get; init; }
	public bool AllowMultiple { get; init; }
	public bool AllowPartial { get; init; }
	public bool HasQuery { get; init; }
	public string? Placeholder { get; init; }
	public string? DefaultValue { get; init; }
	public IReadOnlyList<string>? EnumValues { get; init; }

	/// <summary>
	/// When true the parameter is tucked under an "Advanced" expander in the inspector so
	/// commonly-edited fields stay front-and-center on node types with many tuning knobs.
	/// </summary>
	public bool IsAdvanced { get; init; }

	/// <summary>
	/// Key of a sibling parameter rendered inline (to the right of this control) instead of as its
	/// own row — e.g. an enum "quantity" that reveals a "count" box only for a specific value.
	/// The companion is hidden from the normal parameter list.
	/// </summary>
	public string? InlineCompanionKey { get; init; }

	/// <summary>
	/// The value of this parameter for which the <see cref="InlineCompanionKey"/> companion is shown.
	/// </summary>
	public string? InlineCompanionVisibleWhen { get; init; }
}

public class NodeDefinition
{
	public string Id { get; init; } = string.Empty;
	public string Title { get; init; } = string.Empty;
	public string ShortDescription { get; init; } = string.Empty;
	public string Icon { get; init; } = string.Empty;
	public string CategoryId { get; init; } = string.Empty;
	public int Order { get; init; }
	public bool HasQuery { get; init; }

	/// <summary>
	/// False while the executor is a placeholder; hidden from the palette so users can't build silent no-ops.
	/// Existing graphs that reference the definition still load and resolve.
	/// </summary>
	public bool IsImplemented { get; init; } = true;

	/// <summary>
	/// True when the executor P/Invokes the injected game client; such nodes can only run inside the game process.
	/// </summary>
	public bool RequiresGameApi { get; init; } = true;

	public IReadOnlyList<NodeParam> Parameters { get; init; } = Array.Empty<NodeParam>();
}

/// <summary>
/// Value holder for a definition parameter. Stored on the node instance and persisted to JSON.
/// </summary>
public class NodeParameterValue : ObservableObject
{
	private string _key = string.Empty;
	private NodeParamType _type;
	private bool _allowMultiple;
	private string _rawValue = string.Empty;
	private bool _boolValue;

	public string Key
	{
		get => _key;
		set => SetProperty(ref _key, value ?? string.Empty);
	}

	public NodeParamType Type
	{
		get => _type;
		set => SetProperty(ref _type, value);
	}

	public bool AllowMultiple
	{
		get => _allowMultiple;
		set => SetProperty(ref _allowMultiple, value);
	}

	/// <summary>
	/// Raw string form for text/number/enum/list parameters. Multi-value fields use newline or comma separation.
	/// </summary>
	public string RawValue
	{
		get => _rawValue;
		set
		{
			if (SetProperty(ref _rawValue, value ?? string.Empty))
			{
				OnPropertyChanged(nameof(Values));
			}
		}
	}

	/// <summary>
	/// Dedicated storage for booleans so the UI doesn't have to parse strings.
	/// </summary>
	public bool BoolValue
	{
		get => _boolValue;
		set => SetProperty(ref _boolValue, value);
	}

	[JsonIgnore]
	public IEnumerable<string> Values => SplitValues();

	public object? GetTypedValue()
	{
		return Type switch
		{
			NodeParamType.Bool => BoolValue,
			NodeParamType.Number => double.TryParse(RawValue, out var numeric) ? numeric : null,
			_ => AllowMultiple ? SplitValues() : RawValue
		};
	}

	public IReadOnlyList<string> SplitValues()
	{
		if (string.IsNullOrWhiteSpace(RawValue))
			return Array.Empty<string>();

		return RawValue
			.Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(v => v.Trim())
			.Where(v => !string.IsNullOrWhiteSpace(v))
			.ToList();
	}
}

public class NodeParamBinding
{
	public NodeParamBinding(NodeParam definition, NodeParameterValue value)
	{
		Definition = definition ?? throw new ArgumentNullException(nameof(definition));
		Value = value ?? throw new ArgumentNullException(nameof(value));
	}

	public NodeParam Definition { get; }
	public NodeParameterValue Value { get; }

	/// <summary>
	/// Optional sibling binding rendered inline beside this control (see
	/// <see cref="NodeParam.InlineCompanionKey"/>). Set by the editor when building bindings.
	/// </summary>
	public NodeParamBinding? InlineCompanion { get; set; }

	/// <summary>True when this binding has an inline companion to render.</summary>
	public bool HasInlineCompanion => InlineCompanion != null;
}

public static class NodeCatalogDefaults
{
	public const string GenericActionId = "actions.generic";
	public const string StartId = "control.start";
	public const string TerminalId = "control.terminal";
	public const string BooleanConditionId = "conditions.boolean";
	public const string ScriptDashboardId = "ui.scriptDashboard";
}
