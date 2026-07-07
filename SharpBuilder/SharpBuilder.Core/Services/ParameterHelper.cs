using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpBuilder.Core.Services;

internal static class ParameterHelper
{
	public static string? ToString(IReadOnlyDictionary<string, object?> map, string key)
	{
		return map.TryGetValue(key, out var val) ? val?.ToString() : null;
	}

	public static int? ToInt(IReadOnlyDictionary<string, object?> map, string key)
	{
		if (!map.TryGetValue(key, out var val) || val == null) return null;
		if (val is int i) return i;
		if (val is double d) return (int)d;
		var text = val.ToString();
		if (string.IsNullOrWhiteSpace(text)) return null;
		if (int.TryParse(text, out var parsed)) return parsed;
		return OffsetNameResolver.TryResolve(text, out parsed) ? parsed : null;
	}

	public static bool ToBool(IReadOnlyDictionary<string, object?> map, string key, bool fallback = false)
	{
		if (!map.TryGetValue(key, out var val) || val == null) return fallback;
		if (val is bool b) return b;
		return bool.TryParse(val.ToString(), out var parsed) ? parsed : fallback;
	}

	public static List<int> ToIntList(IReadOnlyDictionary<string, object?> map, string key)
	{
		if (!map.TryGetValue(key, out var val) || val == null) return new List<int>();
		if (val is IEnumerable<string> listStrings)
			return listStrings.Select(v => int.TryParse(v, out var i) ? i : (int?)null).Where(i => i.HasValue).Select(i => i!.Value).ToList();
		if (val is IEnumerable<object> listObj)
			return listObj.Select(v => int.TryParse(v?.ToString(), out var i) ? i : (int?)null).Where(i => i.HasValue).Select(i => i!.Value).ToList();
		return val.ToString() is { } single && int.TryParse(single, out var parsed) ? new List<int> { parsed } : new List<int>();
	}

	public static List<string> ToStringList(IReadOnlyDictionary<string, object?> map, string key)
	{
		if (!map.TryGetValue(key, out var val) || val == null) return new List<string>();
		if (val is IEnumerable<string> listStrings) return listStrings.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
		if (val is IEnumerable<object> listObj) return listObj.Select(v => v?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()!;
		var single = val.ToString();
		return string.IsNullOrWhiteSpace(single) ? new List<string>() : new List<string> { single };
	}

	/// <summary>
	/// Parses a mixed target list the way a scripter would write it: numeric tokens become
	/// ids, everything else becomes a name. Each entry is re-split so older saves still work.
	/// </summary>
	public static (List<int> Ids, List<string> Names) ToTargetLists(IReadOnlyDictionary<string, object?> map, string key)
	{
		var ids = new List<int>();
		var names = new List<string>();

		foreach (var entry in ToStringList(map, key))
		{
			var tokens = entry.Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var raw in tokens)
			{
				var token = raw.Trim();
				if (token.Length == 0)
					continue;
				if (int.TryParse(token, out var id))
					ids.Add(id);
				else
					names.Add(token);
			}
		}

		return (ids, names);
	}

	public static List<(int x, int y, int z)> ToCoordinateList(IReadOnlyDictionary<string, object?> map, string key)
	{
		var output = new List<(int, int, int)>();
		var strings = ToStringList(map, key);
		foreach (var s in strings)
		{
			if (string.IsNullOrWhiteSpace(s)) continue;
			var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 2 &&
			    int.TryParse(parts[0], out var x) &&
			    int.TryParse(parts[1], out var y))
			{
				var z = 0;
				if (parts.Length >= 3) int.TryParse(parts[2], out z);
				output.Add((x, y, z));
			}
		}

		return output;
	}
}
