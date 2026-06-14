using System.Windows;
using System.Windows.Controls;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Editor.Wpf.Utilities;

/// <summary>
/// Chooses parameter editor templates based on the parameter type and multiplicity.
/// </summary>
public class NodeParameterTemplateSelector : DataTemplateSelector
{
	public DataTemplate? StringTemplate { get; set; }
	public DataTemplate? NumberTemplate { get; set; }
	public DataTemplate? BoolTemplate { get; set; }
	public DataTemplate? EnumTemplate { get; set; }
	public DataTemplate? EnumInlineTemplate { get; set; }
	public DataTemplate? ListTemplate { get; set; }

	public override DataTemplate? SelectTemplate(object item, DependencyObject container)
	{
		if (item is not NodeParamBinding binding)
			return base.SelectTemplate(item, container);

		if (binding.Definition.AllowMultiple || binding.Definition.Type == NodeParamType.List)
			return ListTemplate ?? StringTemplate;

		// An enum that reveals an inline companion (e.g. quantity → count) uses a combined editor.
		if (binding.Definition.Type == NodeParamType.Enum && binding.HasInlineCompanion)
			return EnumInlineTemplate ?? EnumTemplate ?? StringTemplate;

		return binding.Definition.Type switch
		{
			NodeParamType.Bool => BoolTemplate,
			NodeParamType.Enum => EnumTemplate ?? StringTemplate,
			NodeParamType.Number => NumberTemplate ?? StringTemplate,
			_ => StringTemplate
		};
	}
}
