using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using csharp_interop.McpTools;
using csharp_interop.Tests;
using Xunit;

namespace MESharp.Tooling.Tests.Mcp;

public sealed class McpControlStyleTests
{
    [Fact]
    public void CheckboxKeepsBoxAndCaptionCenteredAcrossCheckStates()
    {
        StaTestRunner.Run(() =>
        {
            var resources = (ResourceDictionary)typeof(McpDashboardWindow)
                .GetMethod("BuildSharedStyles", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
            var checkbox = new CheckBox { Content = "Follow tail", IsThreeState = true };
            var root = new Border { Resources = resources, Child = checkbox };
            foreach (bool? state in new bool?[] { false, true, null })
            {
                checkbox.IsChecked = state;
                root.Measure(new Size(220, 40));
                root.Arrange(new Rect(0, 0, 220, 40));
                root.UpdateLayout();
                var box = Assert.IsType<Border>(checkbox.Template.FindName("Box", checkbox));
                var caption = Assert.IsType<ContentPresenter>(checkbox.Template.FindName("Caption", checkbox));
                var boxCenter = box.TranslatePoint(new Point(box.ActualWidth / 2, box.ActualHeight / 2), checkbox);
                var captionCenter = caption.TranslatePoint(new Point(0, caption.ActualHeight / 2), checkbox);
                Assert.InRange(Math.Abs(boxCenter.Y - captionCenter.Y), 0, 1);
                Assert.InRange(captionCenter.X - (boxCenter.X + box.ActualWidth / 2), 9, 11);
                // Layout rounding can move the 18-DIP box to a nearby device pixel at 125% DPI.
                Assert.InRange(box.ActualHeight, 17, 19);
            }
        });
    }

    [Fact]
    public void EditableDropdownRetainsCustomInputAndFollowsThemeChanges()
    {
        StaTestRunner.Run(() =>
        {
            var resources = (ResourceDictionary)typeof(McpDashboardWindow)
                .GetMethod("BuildSharedStyles", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
            var combo = new ComboBox { IsEditable = true, Width = 200 };
            combo.Items.Add("navigation");
            var root = new Border { Resources = resources, Child = combo };
            root.Measure(new Size(300, 100));
            root.Arrange(new Rect(0, 0, 300, 100));
            combo.ApplyTemplate();
            var editor = Assert.IsType<TextBox>(combo.Template.FindName("PART_EditableTextBox", combo));
            editor.Text = "custom-category";
            StaTestRunner.PumpDispatcher();
            Assert.Equal("custom-category", combo.Text);

            McpPalette.Apply(true);
            var dark = ((SolidColorBrush)combo.Background).Color;
            McpPalette.Apply(false);
            Assert.NotEqual(dark, ((SolidColorBrush)combo.Background).Color);
            Assert.Equal("custom-category", combo.Text);
            McpPalette.Apply(true);
        });
    }

    [Fact]
    public void FieldBoundariesHaveAtLeastThreeToOneContrastInBothThemes()
    {
        StaTestRunner.Run(() =>
        {
            foreach (var dark in new[] { true, false })
            {
                McpPalette.Apply(dark);
                var border = Luminance(McpPalette.BorderLineBrush.Color);
                foreach (var surface in new[] { McpPalette.BgFieldBrush, McpPalette.BgCardBrush })
                {
                    var background = Luminance(surface.Color);
                    var contrast = (Math.Max(border, background) + 0.05) / (Math.Min(border, background) + 0.05);
                    Assert.True(contrast >= 3, $"Boundary contrast {contrast:F2} in {(dark ? "dark" : "light")} theme.");
                }
            }
            McpPalette.Apply(true);
        });
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte component)
        {
            var value = component / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
    }
}
