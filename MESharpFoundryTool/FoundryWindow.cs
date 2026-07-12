using System.Windows;
using System.Windows.Controls;

namespace MESharp;

internal sealed class FoundryWindow : Window
{
    private readonly FoundryRecorder _recorder = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button _start = new() { Content = "Start demonstration", Margin = new(0, 0, 8, 0) };
    private readonly Button _cycle = new() { Content = "Mark cycle", Margin = new(0, 0, 8, 0), IsEnabled = false };
    private readonly Button _stop = new() { Content = "Stop and finalize", IsEnabled = false };
    private readonly CheckBox _bankAbsent = new() { Content = "Bank target absent", Margin = new(0, 0, 16, 0), IsEnabled = false };
    private readonly CheckBox _portableAbsent = new() { Content = "Portable crafter absent", IsEnabled = false };

    public FoundryWindow()
    {
        Title = "MESharp Foundry";
        Width = 680; Height = 260; MinWidth = 560; MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 12, 0, 12) };
        buttons.Children.Add(_start); buttons.Children.Add(_cycle); buttons.Children.Add(_stop);
        var labels = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 12) };
        labels.Children.Add(_bankAbsent); labels.Children.Add(_portableAbsent);
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Record five clean repetitions. Foundry captures mouse and keyboard input, frames, and privileged truth. Check an absent label only while that target is definitely not visible.", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(buttons); root.Children.Add(labels); root.Children.Add(_status); Content = root;
        _start.Click += (_, _) => Start();
        _cycle.Click += (_, _) => _recorder.MarkCycle();
        _stop.Click += (_, _) => Stop();
        _bankAbsent.Checked += (_, _) => _recorder.SetTargetAbsent("bank-target", true);
        _bankAbsent.Unchecked += (_, _) => _recorder.SetTargetAbsent("bank-target", false);
        _portableAbsent.Checked += (_, _) => _recorder.SetTargetAbsent("portable-crafter", true);
        _portableAbsent.Unchecked += (_, _) => _recorder.SetTargetAbsent("portable-crafter", false);
        _recorder.Status += message => Dispatcher.Invoke(() => _status.Text = message);
        Closed += (_, _) => _recorder.Dispose();
    }

    private void Start()
    {
        try
        {
            _recorder.Start(); _start.IsEnabled = false; _cycle.IsEnabled = true; _stop.IsEnabled = true;
            _bankAbsent.IsEnabled = true; _portableAbsent.IsEnabled = true;
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void Stop()
    {
        _recorder.Stop(); _start.IsEnabled = true; _cycle.IsEnabled = false; _stop.IsEnabled = false;
        _bankAbsent.IsChecked = false; _portableAbsent.IsChecked = false;
        _bankAbsent.IsEnabled = false; _portableAbsent.IsEnabled = false;
    }
}
