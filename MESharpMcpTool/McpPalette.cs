using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media;

namespace csharp_interop.McpTools
{
    internal static class McpPalette
    {
        private static readonly PaletteState State = new();
        public static readonly SolidColorBrush BgWindowBrush = Bound(nameof(State.BgWindow));
        public static readonly SolidColorBrush BgCardBrush = Bound(nameof(State.BgCard));
        public static readonly SolidColorBrush BgFieldBrush = Bound(nameof(State.BgField));
        public static readonly SolidColorBrush BorderLineBrush = Bound(nameof(State.Border));
        public static readonly SolidColorBrush AccentBlueBrush = Bound(nameof(State.AccentBlue));
        public static readonly SolidColorBrush AccentGreenBrush = Bound(nameof(State.AccentGreen));
        public static readonly SolidColorBrush AccentRedBrush = Bound(nameof(State.AccentRed));
        public static readonly SolidColorBrush AccentOrangeBrush = Bound(nameof(State.AccentOrange));
        public static readonly SolidColorBrush AccentYellowBrush = Bound(nameof(State.AccentYellow));
        public static readonly SolidColorBrush AccentForegroundBrush = Bound(nameof(State.AccentForeground));
        public static readonly SolidColorBrush TextBrush = Bound(nameof(State.Text));
        public static readonly SolidColorBrush TextStrongBrush = Bound(nameof(State.TextStrong));
        public static readonly SolidColorBrush TextDimBrush = Bound(nameof(State.TextDim));

        public static Color AccentBlue => AccentBlueBrush.Color;
        public static Color AccentGreen => AccentGreenBrush.Color;
        public static Color AccentRed => AccentRedBrush.Color;
        public static Color AccentOrange => AccentOrangeBrush.Color;
        public static Color AccentYellow => AccentYellowBrush.Color;

        static McpPalette() => Apply(isDark: true);

        public static void Apply(bool isDark)
        {
            State.BgWindow = isDark ? Color.FromRgb(18, 20, 24) : Color.FromRgb(247, 247, 250);
            State.BgCard = isDark ? Color.FromRgb(28, 32, 40) : Color.FromRgb(240, 240, 245);
            State.BgField = isDark ? Color.FromRgb(38, 43, 54) : Colors.White;
            State.Border = isDark ? Color.FromRgb(52, 58, 72) : Color.FromRgb(210, 214, 222);
            State.AccentBlue = isDark ? Color.FromRgb(66, 165, 245) : Color.FromRgb(30, 112, 196);
            State.AccentGreen = isDark ? Color.FromRgb(102, 187, 106) : Color.FromRgb(46, 125, 50);
            State.AccentRed = isDark ? Color.FromRgb(239, 83, 80) : Color.FromRgb(198, 40, 40);
            State.AccentOrange = isDark ? Color.FromRgb(255, 167, 38) : Color.FromRgb(230, 81, 0);
            State.AccentYellow = isDark ? Color.FromRgb(255, 213, 79) : Color.FromRgb(130, 105, 0);
            State.AccentForeground = isDark ? Colors.Black : Colors.White;
            State.Text = isDark ? Color.FromRgb(245, 245, 245) : Color.FromRgb(28, 32, 40);
            State.TextStrong = isDark ? Colors.White : Color.FromRgb(18, 20, 24);
            State.TextDim = isDark ? Color.FromRgb(148, 155, 170) : Color.FromRgb(82, 89, 103);
        }

        private static SolidColorBrush Bound(string path)
        {
            var brush = new SolidColorBrush();
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(path) { Source = State });
            return brush;
        }

        private sealed class PaletteState : INotifyPropertyChanged
        {
            private Color _bgWindow, _bgCard, _bgField, _border, _accentBlue, _accentGreen, _accentRed,
                _accentOrange, _accentYellow, _accentForeground, _text, _textStrong, _textDim;

            public Color BgWindow { get => _bgWindow; set => Set(ref _bgWindow, value); }
            public Color BgCard { get => _bgCard; set => Set(ref _bgCard, value); }
            public Color BgField { get => _bgField; set => Set(ref _bgField, value); }
            public Color Border { get => _border; set => Set(ref _border, value); }
            public Color AccentBlue { get => _accentBlue; set => Set(ref _accentBlue, value); }
            public Color AccentGreen { get => _accentGreen; set => Set(ref _accentGreen, value); }
            public Color AccentRed { get => _accentRed; set => Set(ref _accentRed, value); }
            public Color AccentOrange { get => _accentOrange; set => Set(ref _accentOrange, value); }
            public Color AccentYellow { get => _accentYellow; set => Set(ref _accentYellow, value); }
            public Color AccentForeground { get => _accentForeground; set => Set(ref _accentForeground, value); }
            public Color Text { get => _text; set => Set(ref _text, value); }
            public Color TextStrong { get => _textStrong; set => Set(ref _textStrong, value); }
            public Color TextDim { get => _textDim; set => Set(ref _textDim, value); }

            private void Set(ref Color field, Color value, [CallerMemberName] string? name = null)
            {
                if (field == value) return;
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
