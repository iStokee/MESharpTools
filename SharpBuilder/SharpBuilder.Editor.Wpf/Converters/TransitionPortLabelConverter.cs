using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Editor.Wpf.Converters;

/// <summary>
/// Renders a transition's firing criteria as a compact port-row label so the
/// branching logic is readable directly on the canvas card.
/// Expects bindings: Trigger, ConditionKey, ExpectedValue, IsFallback, Label.
/// </summary>
public class TransitionPortLabelConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		var trigger = values.Length > 0 && values[0] is TransitionTrigger t ? t : TransitionTrigger.Any;
		var conditionKey = values.Length > 1 ? values[1] as string : null;
		var expected = values.Length > 2 && values[2] is bool b && b;
		var isFallback = values.Length > 3 && values[3] is bool f && f;
		var label = values.Length > 4 ? values[4] as string : null;

		var parts = new List<string>();

		if (trigger == TransitionTrigger.OnSuccess)
			parts.Add("✓ success");
		else if (trigger == TransitionTrigger.OnFail)
			parts.Add("✗ fail");

		if (!string.IsNullOrWhiteSpace(conditionKey))
			parts.Add($"{conditionKey}={(expected ? "true" : "false")}");

		if (isFallback)
			parts.Add("fallback");

		if (parts.Count == 0)
			return string.IsNullOrWhiteSpace(label) ? "any" : label!;

		return string.Join(" · ", parts);
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		var results = new object[targetTypes.Length];
		Array.Fill(results, Binding.DoNothing);
		return results;
	}
}
