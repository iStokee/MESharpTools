
using System.Collections.Generic;

namespace MESharp.Models
{
public class ThemeSettings
{
    public bool IsDark { get; set; }
    public string? PrimaryColor { get; set; }
    public List<string> CustomColors { get; set; } = new();
    // Optional custom background overrides
    public string? BackgroundLight { get; set; }
    public string? BackgroundDark { get; set; }
    // Persisted main-window size (null = never resized/picked; use XAML defaults)
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
}
}
