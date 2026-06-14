using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MESharp.API;

namespace SharpBuilder.Editor.Wpf.ViewModels;

/// <summary>
/// Snapshots the player's inventory over the session and accumulates per-item inflow/outflow so the
/// dashboard Items tab can show gained/lost, net, per-hour, and GP rates. Values come from the
/// cache-backed high-alch value exposed on each item, keeping the tracker activity-agnostic.
/// </summary>
public sealed class ItemTracker
{
	private readonly DateTime _startUtc;
	private readonly Dictionary<int, DashboardItemRow> _byId = new();

	public ItemTracker(DateTime startUtc)
	{
		_startUtc = startUtc;
	}

	public ObservableCollection<DashboardItemRow> Rows { get; } = new();

	/// <summary>Total net GP/hour across every tracked item.</summary>
	public long TotalGpPerHour { get; private set; }

	/// <summary>Count of items that have seen any flow this session.</summary>
	public int ActiveCount { get; private set; }

	/// <summary>
	/// Reads the current inventory and folds it into the running totals. Returns false (with a
	/// reason) when the inventory cannot be read — e.g. outside the injected game runtime.
	/// </summary>
	public bool TryUpdate(out string? error)
	{
		try
		{
			var items = Inventory.GetItemsDetailed();
			var elapsedHours = (DateTime.UtcNow - _startUtc).TotalHours;

			// Aggregate stack counts per item id for this snapshot.
			var snapshot = new Dictionary<int, (long Count, string Name, int Value)>();
			foreach (var item in items)
			{
				if (item.Id <= 0)
					continue;

				var existing = snapshot.TryGetValue(item.Id, out var prior) ? prior.Count : 0;
				snapshot[item.Id] = (existing + Math.Max(0, item.Stack), item.Name, item.HighAlch);
			}

			foreach (var entry in snapshot)
			{
				var row = GetOrAdd(entry.Key, entry.Value.Name);
				row.Observe(entry.Value.Count, entry.Value.Value, elapsedHours);
			}

			// Items fully gone from the inventory drop to a count of zero.
			foreach (var row in _byId.Values)
			{
				if (!snapshot.ContainsKey(row.Id))
					row.Observe(0, row.UnitValue, elapsedHours);
			}

			TotalGpPerHour = _byId.Values.Sum(r => r.GpPerHour);
			ActiveCount = _byId.Values.Count(r => r.IsActive);

			error = null;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private DashboardItemRow GetOrAdd(int id, string name)
	{
		if (_byId.TryGetValue(id, out var row))
			return row;

		row = new DashboardItemRow(id, string.IsNullOrWhiteSpace(name) ? $"Item {id}" : name);
		_byId[id] = row;
		Rows.Add(row);
		return row;
	}
}
