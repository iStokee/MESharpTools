using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;

namespace MESharp;

/// <summary>Human-in-the-loop review for Atom's generated positive-label previews.</summary>
internal sealed class DatasetReviewWindow : Window
{
    private readonly string _reviewPath;
    private readonly JsonObject _review;
    private readonly JsonArray _decisions;
    private int _index;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _heading = new() { FontSize = 16, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _progress = new();
    private readonly TextBlock _status = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBox _reason = new() { MinHeight = 52, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    private readonly Button _previous = Button("← Previous");
    private readonly Button _next = Button("Next →");

    public DatasetReviewWindow(string reviewPath)
    {
        _reviewPath = Path.GetFullPath(reviewPath);
        _review = JsonNode.Parse(File.ReadAllText(_reviewPath)) as JsonObject
            ?? throw new InvalidDataException("Review file must contain a JSON object.");
        _decisions = _review["decisions"] as JsonArray
            ?? throw new InvalidDataException("Review file contains no decisions.");
        if (_decisions.Count == 0) throw new InvalidDataException("Review contains no labels.");

        Title = $"Atom visual-truth review · {_review["class"]?.GetValue<string>()}";
        Width = 900; Height = 720; MinWidth = 680; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "App.BackgroundBrush");
        SetResourceReference(ForegroundProperty, "App.ForegroundBrush");

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_heading);
        Grid.SetColumn(_progress, 1); header.Children.Add(_progress);
        root.Children.Add(header);

        var imageBorder = new Border { Margin = new Thickness(0, 12, 0, 12), Padding = new Thickness(8), BorderThickness = new Thickness(1), Child = _image };
        imageBorder.SetResourceReference(Border.BackgroundProperty, "App.CardBrush");
        imageBorder.SetResourceReference(Border.BorderBrushProperty, "App.BorderBrush");
        Grid.SetRow(imageBorder, 1); root.Children.Add(imageBorder);

        var controls = new StackPanel();
        controls.Children.Add(_status);
        controls.Children.Add(new TextBlock { Text = "EXCLUSION REASON", FontSize = 10, Margin = new Thickness(0, 8, 0, 2) });
        controls.Children.Add(_reason);
        var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var done = Button("Save and close");
        DockPanel.SetDock(done, Dock.Right); buttons.Children.Add(done);
        var keep = Button("Keep label");
        var exclude = Button("Exclude label");
        keep.Margin = new Thickness(8, 0, 8, 0);
        exclude.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(_previous); buttons.Children.Add(keep); buttons.Children.Add(exclude); buttons.Children.Add(_next);
        controls.Children.Add(buttons);
        Grid.SetRow(controls, 2); root.Children.Add(controls);
        Content = root;

        _previous.Click += (_, _) => Navigate(-1);
        _next.Click += (_, _) => Navigate(1);
        keep.Click += (_, _) => Decide("keep");
        exclude.Click += (_, _) => Decide("exclude");
        done.Click += (_, _) => { SaveReason(); Persist(); DialogResult = true; };
        LoadDecision();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Left) { Navigate(-1); e.Handled = true; return; }
        if (e.Key == Key.Right) { Navigate(1); e.Handled = true; return; }
        if (_reason.IsKeyboardFocusWithin) return;
        if (e.Key == Key.K) { Decide("keep"); e.Handled = true; }
        else if (e.Key == Key.X)
        {
            if (string.IsNullOrWhiteSpace(_reason.Text)) _reason.Focus();
            else Decide("exclude");
            e.Handled = true;
        }
    }

    private JsonObject Current => _decisions[_index] as JsonObject
        ?? throw new InvalidDataException($"Review decision {_index} is invalid.");

    private void LoadDecision()
    {
        var decision = Current;
        var sequence = decision["sequence"]?.GetValue<long>() ?? -1;
        var split = decision["split"]?.GetValue<string>() ?? "unknown";
        var kind = decision["kind"]?.GetValue<string>() ?? "positive";
        var source = decision["sourceSession"]?.GetValue<string>() ?? _review["sourceSession"]?.GetValue<string>() ?? "unknown";
        var state = decision["status"]?.GetValue<string>() ?? "pending";
        _heading.Text = $"{kind.ToUpperInvariant()} · {source} · sequence {sequence} · {split}";
        _reason.Text = decision["reason"]?.GetValue<string>() ?? "";
        _status.Text = state switch
        {
            "keep" => "KEEP · sample is trustworthy",
            "exclude" => "EXCLUDE · sample is invalid or ambiguous",
            _ when kind == "positive" => "PENDING · inspect the red box and green action point (K keep, X exclude)",
            _ when kind == "negative" => "PENDING · confirm the configured class is truly absent (K keep, X exclude)",
            _ => "PENDING · inspect temporal alignment around the recorded action (K keep, X exclude)",
        };
        _status.Foreground = state switch { "keep" => Brushes.ForestGreen, "exclude" => Brushes.OrangeRed, _ => Brushes.Goldenrod };
        _progress.Text = $"{_index + 1}/{_decisions.Count} · {CompletedCount()}/{_decisions.Count} decided";
        _previous.IsEnabled = _index > 0;
        _next.IsEnabled = _index + 1 < _decisions.Count;

        var preview = decision["preview"]?.GetValue<string>()
            ?? throw new InvalidDataException("This review predates per-label previews. Generate the review again.");
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_reviewPath)!, preview.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(path)) throw new FileNotFoundException("Review preview is missing.", path);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze();
        _image.Source = bitmap;
    }

    private void Decide(string state)
    {
        var reason = _reason.Text.Trim();
        if (state == "exclude" && string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show(this, "Enter a short reason before excluding this label.", "Review reason required", MessageBoxButton.OK, MessageBoxImage.Information);
            _reason.Focus();
            return;
        }
        Current["status"] = state;
        Current["reason"] = state == "keep" ? "" : reason;
        Persist();
        if (_index + 1 < _decisions.Count) _index++;
        LoadDecision();
    }

    private void Navigate(int delta)
    {
        SaveReason();
        _index = Math.Clamp(_index + delta, 0, _decisions.Count - 1);
        LoadDecision();
    }

    private void SaveReason()
    {
        if (Current["status"]?.GetValue<string>() == "exclude" && !string.IsNullOrWhiteSpace(_reason.Text))
            Current["reason"] = _reason.Text.Trim();
    }

    private int CompletedCount() => _decisions.Count(value =>
        value?["status"]?.GetValue<string>() is "keep" or "exclude");

    private void Persist()
    {
        var temporary = _reviewPath + ".tmp";
        File.WriteAllText(temporary, _review.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        File.Move(temporary, _reviewPath, overwrite: true);
    }

    private static Button Button(string text) => new() { Content = text, Padding = new Thickness(10, 5, 10, 5) };
}
