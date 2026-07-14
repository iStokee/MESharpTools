using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MESharp;

/// <summary>A thin UI over Atom Lab. All expensive work stays in a cancellable WSL child process.</summary>
internal sealed class AtomPipelinePanel : UserControl, IDisposable
{
    private readonly FoundrySettings _foundrySettings;
    private readonly AtomPipelineSettings _settings;
    private readonly WslAtomPipelineRunner _runner = new();
    private CancellationTokenSource? _jobCancellation;
    private IReadOnlyList<FoundrySessionItem> _sessionItems = [];

    private readonly TextBox _repository = new();
    private readonly TextBox _python = new();
    private readonly TextBox _distribution = new();
    private readonly ComboBox _developmentPositive = SessionCombo();
    private readonly ComboBox _validationNegative = SessionCombo();
    private readonly ComboBox _task = new() { MinWidth = 280, DisplayMemberPath = nameof(AtomTaskItem.DisplayName) };
    private readonly TextBlock _taskDetails = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _acceptancePositive = SessionCombo();
    private readonly ComboBox _acceptanceNegative = SessionCombo();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _artifacts = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _log = new()
    {
        IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Consolas"), MinHeight = 180,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly List<Button> _jobButtons = [];
    private readonly Button _cancel = new() { Content = "Cancel job", IsEnabled = false, Padding = new(10, 4, 10, 4) };

    public AtomPipelinePanel(FoundrySettings settings)
    {
        _foundrySettings = settings;
        _settings = settings.AtomPipeline ??= new AtomPipelineSettings();
        _repository.Text = _settings.RepositoryPath;
        _python.Text = _settings.PythonExecutable;
        _distribution.Text = _settings.Distribution;

        var content = new StackPanel { Margin = new Thickness(16, 12, 16, 20) };
        content.Children.Add(Heading("Atom pipeline", "Coordinate Foundry sessions, visual-truth review, CUDA training, and deployment-artifact evaluation. Training runs outside the game process through WSL."));
        content.Children.Add(BuildEnvironmentCard());
        content.Children.Add(BuildDevelopmentCard());
        content.Children.Add(BuildAcceptanceCard());
        content.Children.Add(BuildStatusCard());
        Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        _developmentPositive.SelectionChanged += (_, _) => RefreshTasks();
        _task.SelectionChanged += (_, _) => { RefreshTaskDetails(); RefreshArtifactStatus(); };
        _repository.TextChanged += (_, _) => { RefreshTasks(); RefreshArtifactStatus(); };
        _cancel.Click += (_, _) => _jobCancellation?.Cancel();
        RefreshSessions();
        SetStatus("READY · Select development sessions and a training task, then validate the environment.", true);
    }

    private UIElement BuildEnvironmentCard()
    {
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fields.Children.Add(AtColumn(Labeled("Atom repository (Windows or /mnt path)", _repository), 0));
        fields.Children.Add(AtColumn(Labeled("WSL Python", _python), 1));
        fields.Children.Add(AtColumn(Labeled("WSL distribution", _distribution), 2));
        ((FrameworkElement)fields.Children[0]).Margin = new(0, 0, 10, 0);
        ((FrameworkElement)fields.Children[1]).Margin = new(0, 0, 10, 0);
        var buttons = Buttons(
            JobButton("Check environment", () => [AtomPipelineCommands.CheckEnvironment(ReadSettings())]),
            PlainButton("Refresh sessions", RefreshSessions));
        return Card(Section("ENVIRONMENT"), fields, buttons);
    }

    private UIElement BuildDevelopmentCard()
    {
        var sessions = new Grid();
        sessions.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        sessions.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        sessions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sessions.Children.Add(AtColumn(Labeled("Development positive session", _developmentPositive), 0));
        sessions.Children.Add(AtColumn(Labeled("Validation negative session", _validationNegative), 1));
        sessions.Children.Add(AtColumn(Labeled("Training task", _task), 2));
        ((FrameworkElement)sessions.Children[0]).Margin = new(0, 0, 10, 0);
        ((FrameworkElement)sessions.Children[1]).Margin = new(0, 0, 10, 0);

        _taskDetails.Margin = new(0, 8, 0, 0);
        _taskDetails.SetResourceReference(TextBlock.ForegroundProperty, "App.SubtleForegroundBrush");

        var firstRow = Buttons(
            JobButton("Validate recordings", ValidateDevelopmentCommands),
            JobButton("1 · Export dataset", ExportDevelopmentCommands),
            JobButton("2 · Create review", CreateReviewCommands),
            PlainButton("Review labels", OpenReview));
        var secondRow = Buttons(
            JobButton("3 · Apply review", ApplyReviewCommands),
            JobButton("4 · Train detector", TrainCommands),
            JobButton("5 · Validate model", ValidateModelCommands));
        var guidance = Subtle("Review training and validation labels before training; the test cycle is never shown. A model must pass isolated validation before fresh acceptance data is used.");
        return Card(Section("DEVELOPMENT RECIPE"), sessions, _taskDetails, guidance, firstRow, secondRow);
    }

    private UIElement BuildAcceptanceCard()
    {
        var sessions = new Grid();
        sessions.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        sessions.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        sessions.Children.Add(AtColumn(Labeled("Fresh positive session", _acceptancePositive), 0));
        sessions.Children.Add(AtColumn(Labeled("Fresh negative-only session", _acceptanceNegative), 1));
        ((FrameworkElement)sessions.Children[0]).Margin = new(0, 0, 10, 0);
        var warning = Subtle("Record these only after the recipe is frozen. Preparing acceptance does not retrain the component; it creates a separate dataset and evaluates its test split.");
        return Card(Section("FINAL ACCEPTANCE"), sessions, warning,
            Buttons(JobButton("Prepare acceptance dataset", PrepareAcceptanceCommands),
                    JobButton("Run acceptance test", AcceptanceTestCommands)));
    }

    private UIElement BuildStatusCard()
    {
        var top = new DockPanel();
        DockPanel.SetDock(_cancel, Dock.Right);
        top.Children.Add(_cancel);
        top.Children.Add(_status);
        _artifacts.Margin = new(0, 6, 0, 8);
        _artifacts.SetResourceReference(TextBlock.ForegroundProperty, "App.SubtleForegroundBrush");
        return Card(Section("PIPELINE STATUS"), top, _artifacts, _log);
    }

    private IReadOnlyList<AtomPipelineCommand> ValidateDevelopmentCommands()
    {
        var (positive, negative, task) = DevelopmentSelection();
        return [AtomPipelineCommands.ValidateTask(ReadSettings(), task.RelativePath),
                AtomPipelineCommands.ValidateSession(ReadSettings(), positive.Directory),
                AtomPipelineCommands.ValidateSession(ReadSettings(), negative.Directory)];
    }

    private IReadOnlyList<AtomPipelineCommand> ExportDevelopmentCommands()
    {
        var (positive, negative, task) = DevelopmentSelection();
        return [AtomPipelineCommands.ExportTaskDataset(ReadSettings(), task.RelativePath, positive.Directory, negative.Directory,
            DevelopmentDataset(task.Id))];
    }

    private IReadOnlyList<AtomPipelineCommand> CreateReviewCommands()
    {
        var task = SelectedTask();
        return [AtomPipelineCommands.ReviewTaskDataset(ReadSettings(), task.RelativePath, DevelopmentDataset(task.Id), ReviewDirectory(task.Id))];
    }

    private IReadOnlyList<AtomPipelineCommand> ApplyReviewCommands()
    {
        var task = SelectedTask();
        return [AtomPipelineCommands.ApplyReview(ReadSettings(), DevelopmentDataset(task.Id),
            $"{ReviewDirectory(task.Id)}/review.json", ReviewedDataset(task.Id))];
    }

    private IReadOnlyList<AtomPipelineCommand> TrainCommands()
    {
        var task = SelectedTask();
        RequireFile(Path.Combine(WindowsRepository(), ReviewedDataset(task.Id).Replace('/', Path.DirectorySeparatorChar), "dataset.json"),
                    "Apply the completed review before training.");
        return [AtomPipelineCommands.TrainTask(ReadSettings(), task.RelativePath, ReviewedDataset(task.Id), ComponentDirectory(task.Id))];
    }

    private IReadOnlyList<AtomPipelineCommand> ValidateModelCommands()
    {
        var task = SelectedTask();
        RequireFile(Path.Combine(WindowsRepository(), ComponentDirectory(task.Id).Replace('/', Path.DirectorySeparatorChar), "component.json"),
                    "Train the detector before validation.");
        return [AtomPipelineCommands.EvaluateTask(ReadSettings(), task.RelativePath, ReviewedDataset(task.Id), ComponentDirectory(task.Id), "validation")];
    }

    private IReadOnlyList<AtomPipelineCommand> PrepareAcceptanceCommands()
    {
        var (_, validationNegative, task) = DevelopmentSelection();
        var positive = RequireSession(_acceptancePositive, "Select a fresh positive acceptance session.");
        var negative = RequireSession(_acceptanceNegative, "Select a fresh negative-only acceptance session.");
        if (positive.Id == negative.Id || positive.Id == validationNegative.Id)
            throw new InvalidOperationException("Acceptance sessions must be distinct from each other and from validation negatives.");
        RequireClassCoverage(positive, task.PrimaryClass, positive: true);
        RequireClassCoverage(negative, task.PrimaryClass, positive: false);
        if (positive.Cycles < 3) throw new InvalidOperationException("A fresh positive acceptance session needs at least three marked cycles.");
        RequirePassedValidation(task.Id);
        if (MetricsMatchCurrentComponent(AcceptanceReceipt(task.Id), task.Id))
            throw new InvalidOperationException("This recipe already has an acceptance result. Freeze a new component version before another gate.");
        return [AtomPipelineCommands.ExportTaskDataset(ReadSettings(), task.RelativePath, positive.Directory, validationNegative.Directory,
            AcceptanceDataset(task.Id), negative.Directory)];
    }

    private IReadOnlyList<AtomPipelineCommand> AcceptanceTestCommands()
    {
        var task = SelectedTask();
        var target = task.Id;
        RequirePassedValidation(target);
        RequireFile(Path.Combine(WindowsRepository(), AcceptanceDataset(target).Replace('/', Path.DirectorySeparatorChar), "dataset.json"),
                    "Prepare the fresh acceptance dataset first.");
        if (MetricsMatchCurrentComponent(AcceptanceReceipt(target), target))
            throw new InvalidOperationException("The one-time acceptance test has already been run for this component.");
        var answer = MessageBox.Show(
            "Run the one-time acceptance test now? Do not use its results to tune this recipe.",
            "Atom acceptance gate", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) throw new OperationCanceledException("Acceptance test cancelled.");
        return [AtomPipelineCommands.EvaluateTask(ReadSettings(), task.RelativePath, AcceptanceDataset(target), ComponentDirectory(target), "test",
            $"{AcceptanceDataset(target)}/acceptance.metrics.json")];
    }

    private async Task RunJobsAsync(Func<IReadOnlyList<AtomPipelineCommand>> commandFactory)
    {
        try
        {
            var commands = commandFactory();
            SaveSettings();
            SetRunning(true);
            _jobCancellation = new CancellationTokenSource();
            foreach (var command in commands)
            {
                AppendLog($"> {command.Name}");
                SetStatus($"RUNNING · {command.Name}", true);
                var result = await _runner.RunAsync(command, _settings.Distribution,
                    line => Dispatcher.Invoke(() => HandleJobOutput(line)), _jobCancellation.Token);
                if (!result.Succeeded)
                    throw new InvalidOperationException($"{result.Name} failed with exit code {result.ExitCode}.");
                AppendLog($"✓ {result.Name} completed in {result.Elapsed:g}");
            }
            SetStatus("COMPLETE · Pipeline step succeeded.", true);
        }
        catch (OperationCanceledException) { SetStatus("CANCELLED · No further pipeline steps were run.", false); }
        catch (Exception ex)
        {
            AppendLog($"ERROR · {ex.Message}");
            SetStatus($"FAILED · {ex.Message}", false);
        }
        finally
        {
            _jobCancellation?.Dispose();
            _jobCancellation = null;
            SetRunning(false);
            RefreshArtifactStatus();
        }
    }

    private void RefreshSessions()
    {
        var previous = new[] { SelectedId(_developmentPositive), SelectedId(_validationNegative), SelectedId(_acceptancePositive), SelectedId(_acceptanceNegative) };
        _sessionItems = FoundrySessionItem.LoadRecent();
        FillSessions(_developmentPositive, previous[0]);
        FillSessions(_validationNegative, previous[1]);
        FillSessions(_acceptancePositive, previous[2]);
        FillSessions(_acceptanceNegative, previous[3]);
        if (_developmentPositive.SelectedItem is null)
            _developmentPositive.SelectedItem = _sessionItems.FirstOrDefault(item => item.Cycles >= 2 && item.HasProjectedTruth);
        if (_validationNegative.SelectedItem is null)
            _validationNegative.SelectedItem = _sessionItems.FirstOrDefault(item => item.Id != SelectedId(_developmentPositive) && item.HasExplicitNegatives);
        RefreshTasks();
        AppendLog($"Loaded {_sessionItems.Count} Foundry sessions.");
    }

    private void FillSessions(ComboBox combo, string? selectedId)
    {
        combo.ItemsSource = null;
        combo.ItemsSource = _sessionItems;
        combo.SelectedItem = _sessionItems.FirstOrDefault(item => item.Id == selectedId);
    }

    private void RefreshTasks()
    {
        var selected = (_task.SelectedItem as AtomTaskItem)?.Id;
        IReadOnlyList<AtomTaskItem> tasks;
        try { tasks = AtomTaskItem.LoadFromRepository(WindowsRepository()); }
        catch { tasks = []; }
        var classes = (_developmentPositive.SelectedItem as FoundrySessionItem)?.TargetClasses ?? [];
        var applicable = tasks.Where(value => classes.Contains(value.PrimaryClass, StringComparer.OrdinalIgnoreCase)).ToArray();
        _task.ItemsSource = applicable;
        _task.SelectedItem = applicable.FirstOrDefault(value => value.Id == selected) ?? applicable.FirstOrDefault();
        RefreshTaskDetails();
        RefreshArtifactStatus();
    }

    private void RefreshTaskDetails()
    {
        _taskDetails.Text = _task.SelectedItem is AtomTaskItem task
            ? $"{task.TaskType} · class {task.PrimaryClass} · recipe {task.RecipeId} · {task.RelativePath}"
            : "No repository task matches the selected session's recorded classes.";
    }

    private void RefreshArtifactStatus()
    {
        try
        {
            var target = (_task.SelectedItem as AtomTaskItem)?.Id;
            var root = WindowsRepository();
            if (string.IsNullOrWhiteSpace(target)) { _artifacts.Text = "Select a training task to inspect artifacts."; return; }
            var dataset = File.Exists(Path.Combine(root, DevelopmentDataset(target), "dataset.json"));
            var review = File.Exists(Path.Combine(root, ReviewDirectory(target), "review.json"));
            var reviewed = File.Exists(Path.Combine(root, ReviewedDataset(target), "dataset.json"));
            var component = File.Exists(Path.Combine(root, ComponentDirectory(target), "component.json"));
            var validation = ReadMetrics(Path.Combine(root, ComponentDirectory(target), "metrics.validation.json"));
            var acceptance = ReadMetrics(AcceptanceReceipt(target));
            var artifactHash = CurrentArtifactHash(target);
            var validationDatasetHash = DatasetHash(ReviewedDataset(target));
            var acceptanceDatasetHash = DatasetHash(AcceptanceDataset(target));
            _artifacts.Text = $"Dataset {Badge(dataset)}   Review {Badge(review)}   Reviewed {Badge(reviewed)}   Component {Badge(component)}   " +
                              $"Validation {MetricBadge(validation, artifactHash, validationDatasetHash)}   Test {MetricBadge(acceptance, artifactHash, acceptanceDatasetHash)}";
        }
        catch (Exception ex) { _artifacts.Text = ex.Message; }
    }

    private static string Badge(bool value) => value ? "✓" : "—";
    private static string MetricBadge(JsonElement? metrics, string? artifactHash, string? datasetHash)
    {
        if (metrics is null) return "—";
        if (string.IsNullOrWhiteSpace(artifactHash) || !metrics.Value.TryGetProperty("artifactSha256", out var recordedHash) ||
            recordedHash.GetString() != artifactHash) return "STALE";
        if (string.IsNullOrWhiteSpace(datasetHash) || !metrics.Value.TryGetProperty("datasetSha256", out var recordedDataset) ||
            recordedDataset.GetString() != datasetHash) return "STALE";
        return metrics.Value.TryGetProperty("passed", out var passed) && passed.GetBoolean() ? "PASS" : "FAIL";
    }

    private static JsonElement? ReadMetrics(string path)
    {
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private void RequirePassedValidation(string target)
    {
        var metrics = ReadMetrics(Path.Combine(WindowsRepository(), ComponentDirectory(target), "metrics.validation.json"));
        if (metrics is null || !MetricsMatchCurrentComponent(metrics.Value, target, ReviewedDataset(target)) ||
            !metrics.Value.TryGetProperty("passed", out var passed) || !passed.GetBoolean())
            throw new InvalidOperationException("The current component has not passed validation; acceptance remains locked.");
    }

    private bool MetricsMatchCurrentComponent(string path, string target)
    {
        var metrics = ReadMetrics(path);
        return metrics is not null && MetricsMatchCurrentComponent(metrics.Value, target);
    }

    private bool MetricsMatchCurrentComponent(JsonElement metrics, string target, string? dataset = null)
    {
        if (!metrics.TryGetProperty("artifactSha256", out var recordedHash) || recordedHash.GetString() != CurrentArtifactHash(target))
            return false;
        return dataset is null || metrics.TryGetProperty("datasetSha256", out var recordedDataset) && recordedDataset.GetString() == DatasetHash(dataset);
    }

    private string? CurrentArtifactHash(string target)
    {
        var path = Path.Combine(WindowsRepository(), ComponentDirectory(target), "component.json");
        if (!File.Exists(path)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("artifactSha256", out var hash) ? hash.GetString() : null;
    }

    private string? DatasetHash(string relativePath)
    {
        var path = Path.Combine(WindowsRepository(), relativePath.Replace('/', Path.DirectorySeparatorChar), "dataset.json");
        return File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() : null;
    }

    private AtomPipelineSettings ReadSettings()
    {
        _settings.RepositoryPath = _repository.Text.Trim();
        _settings.PythonExecutable = _python.Text.Trim();
        _settings.Distribution = _distribution.Text.Trim();
        return _settings;
    }

    private void SaveSettings() { ReadSettings(); _foundrySettings.Save(); }
    private string WindowsRepository() => AtomPipelineCommands.WindowsPath(_repository.Text.Trim());
    private string AcceptanceReceipt(string target) => Path.Combine(WindowsRepository(), AcceptanceDataset(target).Replace('/', Path.DirectorySeparatorChar), "acceptance.metrics.json");

    private (FoundrySessionItem Positive, FoundrySessionItem Negative, AtomTaskItem Task) DevelopmentSelection()
    {
        var positive = RequireSession(_developmentPositive, "Select a development positive session.");
        var negative = RequireSession(_validationNegative, "Select a validation negative session.");
        if (positive.Id == negative.Id) throw new InvalidOperationException("Positive and negative sessions must be distinct.");
        var task = SelectedTask();
        RequireClassCoverage(positive, task.PrimaryClass, positive: true);
        RequireClassCoverage(negative, task.PrimaryClass, positive: false);
        if (positive.Cycles < 3) throw new InvalidOperationException("Development datasets need at least three marked cycles for train, validation, and diagnostic splits.");
        return (positive, negative, task);
    }

    private static void RequireClassCoverage(FoundrySessionItem session, string target, bool positive)
    {
        var classes = positive ? session.ProjectedClasses : session.NegativeClasses;
        if (!classes.Contains(target, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Session {session.Id} has no recorded {(positive ? "projected positives" : "explicit negatives")} for {target}.");
    }

    private AtomTaskItem SelectedTask() => _task.SelectedItem as AtomTaskItem ?? throw new InvalidOperationException("Select a training task.");
    private static FoundrySessionItem RequireSession(ComboBox combo, string message) => combo.SelectedItem as FoundrySessionItem ?? throw new InvalidOperationException(message);
    private static string? SelectedId(ComboBox combo) => (combo.SelectedItem as FoundrySessionItem)?.Id;
    private static void RequireFile(string path, string message) { if (!File.Exists(path)) throw new InvalidOperationException(message); }

    private static string DevelopmentDataset(string target) => $"data/{target}";
    private static string ReviewDirectory(string target) => $"data/{target}-development-review";
    private static string ReviewedDataset(string target) => $"data/{target}-reviewed";
    private static string AcceptanceDataset(string target) => $"data/{target}-acceptance";
    private static string ComponentDirectory(string target) => $"components/{target}-detector";

    private void OpenReview()
    {
        try
        {
            SaveSettings();
            var path = Path.Combine(WindowsRepository(), ReviewDirectory(SelectedTask().Id).Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(path)) throw new InvalidOperationException("Create the task review first.");
            var review = new DatasetReviewWindow(Path.Combine(path, "review.json")) { Owner = Window.GetWindow(this) };
            review.ShowDialog();
            AppendLog("Visual-truth review saved.");
        }
        catch (Exception ex) { SetStatus($"FAILED · {ex.Message}", false); }
    }

    private Button JobButton(string text, Func<IReadOnlyList<AtomPipelineCommand>> commands)
    {
        var button = PlainButton(text, () => _ = RunJobsAsync(commands));
        _jobButtons.Add(button);
        return button;
    }

    private static Button PlainButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new(10, 4, 10, 4), Margin = new(0, 0, 8, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void SetRunning(bool running)
    {
        foreach (var button in _jobButtons) button.IsEnabled = !running;
        _cancel.IsEnabled = running;
    }

    private void SetStatus(string text, bool ready)
    {
        _status.Text = text;
        _status.Foreground = ready ? Brushes.ForestGreen : Brushes.OrangeRed;
    }

    private void AppendLog(string text)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        _log.ScrollToEnd();
    }

    private void HandleJobOutput(string line)
    {
        AppendLog(line);
        if (!line.StartsWith('{')) return;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("event", out var eventName)) return;
            switch (eventName.GetString())
            {
                case "training-started":
                    SetStatus($"TRAINING · {root.GetProperty("trainingExamples").GetInt32()} examples on {root.GetProperty("device").GetString()}", true);
                    break;
                case "epoch-completed":
                    SetStatus($"TRAINING · epoch {root.GetProperty("epoch").GetInt32()}/{root.GetProperty("epochs").GetInt32()} · loss {root.GetProperty("loss").GetDouble():F4}", true);
                    break;
                case "export-started":
                    SetStatus("EXPORTING · Writing deployment ONNX artifact…", true);
                    break;
            }
        }
        catch (JsonException) { }
    }

    public void Dispose()
    {
        try { SaveSettings(); } catch { }
        _jobCancellation?.Cancel();
        _jobCancellation?.Dispose();
        _runner.Dispose();
    }

    private static ComboBox SessionCombo() => new() { MinWidth = 280, DisplayMemberPath = nameof(FoundrySessionItem.DisplayName) };
    private static StackPanel Buttons(params UIElement[] values)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 10, 0, 0) };
        foreach (var value in values) panel.Children.Add(value);
        return panel;
    }
    private static TextBlock Section(string text)
    {
        var block = new TextBlock { Text = text, FontSize = 11, Margin = new(0, 0, 0, 7) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "App.SubtleForegroundBrush");
        return block;
    }
    private static TextBlock Subtle(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new(0, 7, 0, 0) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "App.SubtleForegroundBrush");
        return block;
    }
    private static UIElement Heading(string title, string caption)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(Subtle(caption));
        return panel;
    }
    private static FrameworkElement Labeled(string label, UIElement value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 10, Margin = new(0, 0, 0, 2) });
        panel.Children.Add(value);
        return panel;
    }
    private static Border Card(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children) panel.Children.Add(child);
        var card = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(1), Child = panel };
        card.SetResourceReference(Border.BackgroundProperty, "App.CardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "App.BorderBrush");
        return card;
    }
    private static UIElement AtColumn(UIElement value, int column) { Grid.SetColumn(value, column); return value; }
}
