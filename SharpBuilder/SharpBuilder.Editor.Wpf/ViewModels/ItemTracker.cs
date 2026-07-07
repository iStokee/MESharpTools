using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MESharp.API;

namespace SharpBuilder.Editor.Wpf.ViewModels;

/// <summary>
/// Snapshots the player's inventory over the session and accumulates per-item inflow/outflow so the
/// dashboard Items tab can show gained/lost, net, per-hour, and GP rates. Inventory item values come
/// from cache-backed high-alch metadata; coin-pouch movement is tracked as raw GP.
/// </summary>
public sealed class ItemTracker
{
	private const int MoneyPouchRowId = -995;

	private DateTime _startUtc;
	private readonly Dictionary<int, DashboardItemRow> _byId = new();

	public ItemTracker(DateTime startUtc)
	{
		_startUtc = startUtc;
	}

	/// <summary>Clears all tracked items and re-baselines the session clock to <paramref name="startUtc"/>.</summary>
	public void Reset(DateTime startUtc)
	{
		_startUtc = startUtc;
		_byId.Clear();
		Rows.Clear();
		TotalGpPerHour = 0;
		ActiveCount = 0;
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
		if (!TryCapture(out var snapshot, out error) || snapshot == null)
			return false;

		Apply(snapshot);
		return true;
	}

	/// <summary>Reads inventory and coin-pouch state without mutating WPF-bound rows.</summary>
	public bool TryCapture(out ItemTrackerSnapshot? snapshot, out string? error)
	{
		try
		{
			var items = Inventory.GetItemsDetailed();
			var elapsedHours = (DateTime.UtcNow - _startUtc).TotalHours;

			// Aggregate stack counts per item id for this snapshot.
			var itemsById = new Dictionary<int, ItemTrackerSnapshotItem>();
			foreach (var item in items)
			{
				if (item.Id <= 0)
					continue;

				var existing = itemsById.TryGetValue(item.Id, out var prior) ? prior.Count : 0;
				itemsById[item.Id] = new ItemTrackerSnapshotItem(
					existing + Math.Max(0, item.Stack),
					item.Name,
					item.HighAlch);
			}

			snapshot = new ItemTrackerSnapshot(itemsById, Math.Max(0, MoneyPouch.Amount), elapsedHours);
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			snapshot = null;
			error = ex.Message;
			return false;
		}
	}

	/// <summary>Applies a captured inventory and coin-pouch snapshot to WPF-bound rows.</summary>
	public void Apply(ItemTrackerSnapshot snapshot)
	{
		foreach (var entry in snapshot.Items)
		{
			var row = GetOrAdd(entry.Key, entry.Value.Name);
			row.Observe(entry.Value.Count, entry.Value.Value, snapshot.ElapsedHours);
		}

		// Items fully gone from the inventory drop to a count of zero.
		foreach (var row in _byId.Values)
		{
			if (row.Id != MoneyPouchRowId && !snapshot.Items.ContainsKey(row.Id))
				row.Observe(0, row.UnitValue, snapshot.ElapsedHours);
		}

		var moneyPouch = GetOrAdd(MoneyPouchRowId, "Money pouch");
		moneyPouch.Observe(snapshot.MoneyPouchAmount, 1, snapshot.ElapsedHours);

		TotalGpPerHour = _byId.Values.Sum(r => r.GpPerHour);
		ActiveCount = _byId.Values.Count(r => r.IsActive);
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

public sealed record ItemTrackerSnapshot(
	IReadOnlyDictionary<int, ItemTrackerSnapshotItem> Items,
	int MoneyPouchAmount,
	double ElapsedHours);

public sealed record ItemTrackerSnapshotItem(long Count, string Name, int Value);
