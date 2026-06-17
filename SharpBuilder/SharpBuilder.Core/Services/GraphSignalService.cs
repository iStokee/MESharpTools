using System;
using System.Collections.Generic;
using System.Linq;
using SharpBuilder.Core.Models;

namespace SharpBuilder.Core.Services;

public static class GraphSignalService
{
	public static IReadOnlyList<string> DiscoverSignalKeys(GraphModel script)
	{
		if (script == null) throw new ArgumentNullException(nameof(script));

		var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var transition in script.Nodes.SelectMany(n => n.Transitions).Where(t => t.HasCondition))
		{
			var key = transition.ConditionKey.Trim();
			if (!string.IsNullOrWhiteSpace(key))
				keys.Add(key);
		}

		foreach (var node in script.Nodes)
		{
			foreach (var param in node.Parameters.Where(p => string.Equals(p.Key, "signal", StringComparison.OrdinalIgnoreCase)))
			{
				foreach (var value in param.SplitValues())
					keys.Add(value);

				if (!param.AllowMultiple && !string.IsNullOrWhiteSpace(param.RawValue))
					keys.Add(param.RawValue.Trim());
			}
		}

		return keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
	}
}
