using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace MESharp.Models
{
    public class ColorOption
    {
        public string Name { get; set; }
        public string Hex { get; set; }
        public SolidColorBrush Brush { get; set; }

        public static List<ColorOption> Defaults()
        {
            string[] pairs = new[]
            {
                "Blue,#2196F3",
                "Indigo,#3F51B5",
                "DeepPurple,#673AB7",
                "Purple,#9C27B0",
                "Pink,#E91E63",
                "Red,#F44336",
                "Orange,#FF9800",
                "Amber,#FFC107",
                "Yellow,#FFEB3B",
                "Lime,#CDDC39",
                "Green,#4CAF50",
                "Teal,#009688",
                "Cyan,#00BCD4",
                "BlueGrey,#607D8B",
                "Grey,#9E9E9E",
                "Brown,#795548"
            };

            return pairs.Select(p =>
            {
                var parts = p.Split(',');
                var color = (Color)ColorConverter.ConvertFromString(parts[1]);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return new ColorOption
                {
                    Name = parts[0],
                    Hex = parts[1],
                    Brush = brush
                };
            }).ToList();
        }

        public static ColorOption MatchOrDefault(IEnumerable<ColorOption> options, string value)
        {
            if (options == null) return null;
            if (!string.IsNullOrWhiteSpace(value))
            {
                var match = options.FirstOrDefault(o => o.Hex.Equals(value, StringComparison.OrdinalIgnoreCase) || o.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return options.FirstOrDefault(o => o.Name == "BlueGrey") ?? options.FirstOrDefault();
        }
    }
}

