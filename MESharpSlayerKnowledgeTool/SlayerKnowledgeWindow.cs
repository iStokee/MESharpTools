using System.Windows;

namespace MESharp;

public sealed class SlayerKnowledgeWindow : Window
{
    public SlayerKnowledgeWindow()
    {
        Title = "MESharp — Slayer Knowledge";
        Width = 1380;
        Height = 880;
        MinWidth = 1080;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "MahApps.Brushes.ThemeBackground");
        SetResourceReference(ForegroundProperty, "MahApps.Brushes.ThemeForeground");
        Content = new SlayerKnowledgeView { DataContext = new SlayerKnowledgeViewModel() };
    }
}
