using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SharpBuilder.Editor.Wpf.ViewModels;

/// <summary>
/// One row of the dashboard Items table. Tracks cumulative inflow/outflow of a single item id
/// across the session and derives per-hour and GP rates. GP is valued from the cache-backed high
/// alchemy value, which is item-agnostic so the table works for fishing, cooking, woodcutting, etc.
/// </summary>
public sealed class DashboardItemRow : INotifyPropertyChanged
{
	private string _name;
	private long _in;
	private long _out;
	private int _unitValue;
	private double _perHour;
	private long _gpPerHour;

	private bool _hasBaseline;
	private long _lastCount;

	public DashboardItemRow(int id, string name)
	{
		Id = id;
		_name = name;
	}

	public int Id { get; }

	public string Name
	{
		get => _name;
		private set => SetProperty(ref _name, value);
	}

	/// <summary>Total units gained over the session.</summary>
	public long In { get => _in; private set => SetProperty(ref _in, value); }

	/// <summary>Total units lost over the session.</summary>
	public long Out { get => _out; private set => SetProperty(ref _out, value); }

	/// <summary>Net change (In − Out).</summary>
	public long Net => _in - _out;

	/// <summary>Cache high-alch value of a single unit, used as the GP proxy.</summary>
	public int UnitValue { get => _unitValue; private set => SetProperty(ref _unitValue, value); }

	/// <summary>Net units per hour.</summary>
	public double PerHour { get => _perHour; private set => SetProperty(ref _perHour, value); }

	/// <summary>Net GP value per hour (Net × UnitValue ÷ hours).</summary>
	public long GpPerHour { get => _gpPerHour; private set => SetProperty(ref _gpPerHour, value); }

	/// <summary>True once any flow has been observed for this item.</summary>
	public bool IsActive => _in > 0 || _out > 0;

	/// <summary>
	/// Folds a fresh inventory count for this item into the cumulative totals and recomputes rates.
	/// The first call establishes the baseline; later calls accumulate inflow/outflow deltas.
	/// </summary>
	public void Observe(long currentCount, int unitValue, double elapsedHours)
	{
		if (unitValue > 0)
			UnitValue = unitValue;

		if (!_hasBaseline)
		{
			_hasBaseline = true;
			_lastCount = currentCount;
			return;
		}

		var delta = currentCount - _lastCount;
		if (delta > 0)
			In += delta;
		else if (delta < 0)
			Out += -delta;
		_lastCount = currentCount;

		var hours = elapsedHours <= 0 ? double.Epsilon : elapsedHours;
		PerHour = Net / hours;
		GpPerHour = (long)Math.Round(Net * (double)UnitValue / hours);

		OnPropertyChanged(nameof(Net));
		OnPropertyChanged(nameof(IsActive));
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (Equals(field, value)) return;
		field = value;
		OnPropertyChanged(propertyName);
	}

	private void OnPropertyChanged(string? propertyName) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
