using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using MahApps.Metro.IconPacks;

namespace MESharp.Views.Controls
{
    public partial class SectionHeader : UserControl
    {
        private bool _updatingExpansionState;

        public SectionHeader()
        {
            InitializeComponent();
            ApplyDefaultIconBrushReference();
            Loaded += (_, __) => UpdateExpandStateFromView();
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty DescriptionProperty =
            DependencyProperty.Register(nameof(Description), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty IconKindProperty =
            DependencyProperty.Register(nameof(IconKind), typeof(PackIconMaterialKind), typeof(SectionHeader), new PropertyMetadata(PackIconMaterialKind.CircleOutline));

        public static readonly DependencyProperty IconForegroundProperty =
            DependencyProperty.Register(nameof(IconForeground), typeof(Brush), typeof(SectionHeader), new PropertyMetadata(null));

        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(SectionHeader), new PropertyMetadata(18d));

        public static readonly DependencyProperty TitleFontSizeProperty =
            DependencyProperty.Register(nameof(TitleFontSize), typeof(double), typeof(SectionHeader), new PropertyMetadata(20d));

        public static readonly DependencyProperty HeaderPaddingProperty =
            DependencyProperty.Register(nameof(HeaderPadding), typeof(Thickness), typeof(SectionHeader), new PropertyMetadata(new Thickness(14, 12, 14, 12)));

        public static readonly DependencyProperty HeaderCornerRadiusProperty =
            DependencyProperty.Register(nameof(HeaderCornerRadius), typeof(CornerRadius), typeof(SectionHeader), new PropertyMetadata(new CornerRadius(6)));

        public static readonly DependencyProperty HelpCommandProperty =
            DependencyProperty.Register(nameof(HelpCommand), typeof(ICommand), typeof(SectionHeader), new PropertyMetadata(null));

        public static readonly DependencyProperty HelpCommandParameterProperty =
            DependencyProperty.Register(nameof(HelpCommandParameter), typeof(object), typeof(SectionHeader), new PropertyMetadata(null));

        public static readonly DependencyProperty HelpToolTipProperty =
            DependencyProperty.Register(nameof(HelpToolTip), typeof(string), typeof(SectionHeader), new PropertyMetadata("Open API docs"));

        public static readonly DependencyProperty ShowHelpProperty =
            DependencyProperty.Register(nameof(ShowHelp), typeof(bool), typeof(SectionHeader), new PropertyMetadata(true));

        public static readonly DependencyProperty AdditionalContentProperty =
            DependencyProperty.Register(nameof(AdditionalContent), typeof(object), typeof(SectionHeader), new PropertyMetadata(null));

        public static readonly DependencyProperty RightContentProperty =
            DependencyProperty.Register(nameof(RightContent), typeof(object), typeof(SectionHeader), new PropertyMetadata(null));

        public static readonly DependencyProperty ShowExpandCollapseAllProperty =
            DependencyProperty.Register(nameof(ShowExpandCollapseAll), typeof(bool), typeof(SectionHeader), new PropertyMetadata(true));

        public static readonly DependencyProperty AreSectionsExpandedProperty =
            DependencyProperty.Register(nameof(AreSectionsExpanded), typeof(bool), typeof(SectionHeader), new PropertyMetadata(true));

        public static readonly DependencyProperty ExpandCollapseToolTipProperty =
            DependencyProperty.Register(nameof(ExpandCollapseToolTip), typeof(string), typeof(SectionHeader), new PropertyMetadata("Expand or collapse all sections"));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Description
        {
            get => (string)GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public PackIconMaterialKind IconKind
        {
            get => (PackIconMaterialKind)GetValue(IconKindProperty);
            set => SetValue(IconKindProperty, value);
        }

        public Brush IconForeground
        {
            get => (Brush)GetValue(IconForegroundProperty);
            set => SetValue(IconForegroundProperty, value);
        }

        public double IconSize
        {
            get => (double)GetValue(IconSizeProperty);
            set => SetValue(IconSizeProperty, value);
        }

        public double TitleFontSize
        {
            get => (double)GetValue(TitleFontSizeProperty);
            set => SetValue(TitleFontSizeProperty, value);
        }

        public Thickness HeaderPadding
        {
            get => (Thickness)GetValue(HeaderPaddingProperty);
            set => SetValue(HeaderPaddingProperty, value);
        }

        public CornerRadius HeaderCornerRadius
        {
            get => (CornerRadius)GetValue(HeaderCornerRadiusProperty);
            set => SetValue(HeaderCornerRadiusProperty, value);
        }

        public ICommand HelpCommand
        {
            get => (ICommand)GetValue(HelpCommandProperty);
            set => SetValue(HelpCommandProperty, value);
        }

        public object HelpCommandParameter
        {
            get => GetValue(HelpCommandParameterProperty);
            set => SetValue(HelpCommandParameterProperty, value);
        }

        public string HelpToolTip
        {
            get => (string)GetValue(HelpToolTipProperty);
            set => SetValue(HelpToolTipProperty, value);
        }

        public bool ShowHelp
        {
            get => (bool)GetValue(ShowHelpProperty);
            set => SetValue(ShowHelpProperty, value);
        }

        public object AdditionalContent
        {
            get => GetValue(AdditionalContentProperty);
            set => SetValue(AdditionalContentProperty, value);
        }

        public object RightContent
        {
            get => GetValue(RightContentProperty);
            set => SetValue(RightContentProperty, value);
        }

        public bool ShowExpandCollapseAll
        {
            get => (bool)GetValue(ShowExpandCollapseAllProperty);
            set => SetValue(ShowExpandCollapseAllProperty, value);
        }

        public bool AreSectionsExpanded
        {
            get => (bool)GetValue(AreSectionsExpandedProperty);
            set => SetValue(AreSectionsExpandedProperty, value);
        }

        public string ExpandCollapseToolTip
        {
            get => (string)GetValue(ExpandCollapseToolTipProperty);
            set => SetValue(ExpandCollapseToolTipProperty, value);
        }

        private void ApplyDefaultIconBrushReference()
        {
            // Use a dynamic resource reference so icon color follows theme changes.
            // A one-time brush lookup can capture stale startup colors (e.g. default blue).
            if (ReadLocalValue(IconForegroundProperty) != DependencyProperty.UnsetValue)
            {
                return;
            }

            SetResourceReference(IconForegroundProperty, "PrimaryBrush");
        }

        private void OnExpandCollapseToggleChanged(object sender, RoutedEventArgs e)
        {
            if (_updatingExpansionState)
            {
                return;
            }

            ApplyExpandState(AreSectionsExpanded);
        }

        private void ApplyExpandState(bool expand)
        {
            var scope = FindOwningViewScope();
            if (scope == null)
            {
                return;
            }

            _updatingExpansionState = true;
            try
            {
                foreach (var expander in FindVisualChildren<Expander>(scope))
                {
                    expander.IsExpanded = expand;
                }

                AreSectionsExpanded = expand;
            }
            finally
            {
                _updatingExpansionState = false;
            }
        }

        private void UpdateExpandStateFromView()
        {
            var scope = FindOwningViewScope();
            if (scope == null)
            {
                return;
            }

            var expanders = FindVisualChildren<Expander>(scope);
            if (expanders.Count == 0)
            {
                return;
            }

            AreSectionsExpanded = expanders.TrueForAll(x => x.IsExpanded);
        }

        private DependencyObject? FindOwningViewScope()
        {
            DependencyObject? current = this;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is UserControl uc && uc != this)
                {
                    return uc;
                }
            }

            return Window.GetWindow(this);
        }

        private static System.Collections.Generic.List<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            var result = new System.Collections.Generic.List<T>();
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                {
                    result.Add(match);
                }

                result.AddRange(FindVisualChildren<T>(child));
            }

            return result;
        }
    }
}
