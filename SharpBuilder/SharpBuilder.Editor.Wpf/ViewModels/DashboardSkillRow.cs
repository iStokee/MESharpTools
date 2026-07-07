using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MESharp.API;

namespace SharpBuilder.Editor.Wpf.ViewModels;

public sealed class DashboardSkillRow : INotifyPropertyChanged
{
	private readonly SkillName _skillName;
	private int _level;
	private int _xp;
	private int _xpGained;
	private double _xpPerHour;
	private int _xpToNext;
	private string _eta = "--";
	private bool _isActive;

	public DashboardSkillRow(SkillName skillName)
	{
		_skillName = skillName;
		Name = skillName.ToString();
		(PrimaryBrush, SecondaryBrush) = CreateBrushes(skillName);
	}

	public SkillName SkillName => _skillName;
	public string Name { get; }
	public Brush PrimaryBrush { get; }
	public Brush SecondaryBrush { get; }

	public int Level { get => _level; private set => SetProperty(ref _level, value); }
	public int Xp { get => _xp; private set => SetProperty(ref _xp, value); }
	public int XpGained { get => _xpGained; private set => SetProperty(ref _xpGained, value); }
	public double XpPerHour { get => _xpPerHour; private set => SetProperty(ref _xpPerHour, value); }
	public int XpToNext { get => _xpToNext; private set => SetProperty(ref _xpToNext, value); }
	public string Eta { get => _eta; private set => SetProperty(ref _eta, value); }
	public bool IsActive { get => _isActive; private set => SetProperty(ref _isActive, value); }

	public bool TryUpdate(SkillSession session, out string? error)
	{
		try
		{
			var snapshot = Skills.Get(_skillName);
			var eta = session.GetTimeToNextLevel(_skillName, snapshot);

			Apply(
				snapshot.CurrentLevel,
				snapshot.Xp,
				session.GetXpGained(_skillName, snapshot.Xp),
				session.GetXpPerHour(_skillName, snapshot.Xp),
				Skills.GetXpToNextLevel(snapshot),
				eta == TimeSpan.MaxValue ? "--" : eta.ToString(@"hh\:mm\:ss"));

			error = null;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	public void Apply(int level, int xp, int xpGained, double xpPerHour, int xpToNext, string eta)
	{
		Level = level;
		Xp = xp;
		XpGained = xpGained;
		XpPerHour = xpPerHour;
		XpToNext = xpToNext;
		Eta = eta;
		IsActive = XpGained > 0;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
	{
		if (Equals(field, value))
			return;

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	private static (Brush Primary, Brush Secondary) CreateBrushes(SkillName skill)
	{
		var pairs = new (string Primary, string Secondary)[]
		{
			("#7c1307", "#f9d108"), ("#4f6198", "#bdc194"), ("#115638", "#822e1a"),
			("#ffffff", "#d11800"), ("#988770", "#23552f"), ("#ffffff", "#ffe118"),
			("#26287f", "#d2cdc6"), ("#58206a", "#76140a"), ("#23552f", "#9b7d40"),
			("#064b4d", "#ceb112"), ("#cdb012", "#7ca1c5"), ("#dabb13", "#e16c14"),
			("#d0b312", "#866347"), ("#585644", "#cdb012"), ("#2b2b21", "#487b8a"),
			("#b99f11", "#096e0d"), ("#849c51", "#28632a"), ("#2c2827", "#7c4067"),
			("#661008", "#272424"), ("#849c51", "#28632a"), ("#ffe118", "#c4c4b5"),
			("#393125", "#6c6545"), ("#7f7463", "#8a590c"), ("#8094aa", "#c4a811"),
			("#de7f44", "#5d2c08"), ("#412c96", "#2c968c"), ("#2466b1", "#ab8a23"),
			("#d2c6b2", "#0c090b"), ("#6500ca", "#38cdbf")
		};

		var pair = pairs[Math.Clamp((int)skill, 0, pairs.Length - 1)];
		return (BrushFrom(pair.Primary), BrushFrom(pair.Secondary));
	}

	private static Brush BrushFrom(string color)
	{
		var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
		brush.Freeze();
		return brush;
	}
}
