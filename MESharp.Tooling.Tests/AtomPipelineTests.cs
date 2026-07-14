using MESharp;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace MESharp.Tooling.Tests;

public sealed class AtomPipelineTests
{
    [Fact]
    public void FoundryWindow_hosts_capture_and_pipeline_workspaces()
    {
        csharp_interop.Tests.StaTestRunner.Run(() =>
        {
            var window = new FoundryWindow();
            try
            {
                var tabs = Assert.IsType<TabControl>(window.Content);
                Assert.Collection(tabs.Items.Cast<TabItem>(),
                    capture => Assert.Equal("Capture", capture.Header),
                    pipeline =>
                    {
                        Assert.Equal("Pipeline", pipeline.Header);
                        Assert.IsType<AtomPipelinePanel>(pipeline.Content);
                    });
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void DatasetReviewWindow_loads_generated_per_label_preview()
    {
        var root = Path.Combine(Path.GetTempPath(), $"atom-review-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "items"));
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(
                1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4)));
            using (var stream = File.Create(Path.Combine(root, "items", "sequence-00000001.png"))) encoder.Save(stream);
            File.WriteAllText(Path.Combine(root, "review.json"), """
            {"class":"bank-target","decisions":[
              {"sequence":1,"split":"validation","status":"pending","reason":"","preview":"items/sequence-00000001.png"}
            ]}
            """);

            csharp_interop.Tests.StaTestRunner.Run(() =>
            {
                var window = new DatasetReviewWindow(Path.Combine(root, "review.json"));
                try { Assert.Contains("bank-target", window.Title); }
                finally { window.Close(); }
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(@"C:\Development\MemoryError\Atom", "/mnt/c/Development/MemoryError/Atom")]
    [InlineData(@"D:\Atom Data\session", "/mnt/d/Atom Data/session")]
    [InlineData("/home/user/Atom", "/home/user/Atom")]
    public void WslPath_converts_absolute_paths(string input, string expected) =>
        Assert.Equal(expected, AtomPipelineCommands.WslPath(input));

    [Fact]
    public void WindowsPath_converts_mounted_drive_path() =>
        Assert.Equal(@"C:\Development\MemoryError\Atom", AtomPipelineCommands.WindowsPath("/mnt/c/Development/MemoryError/Atom"));

    [Fact]
    public void BashQuote_prevents_single_quote_from_terminating_argument()
    {
        Assert.Equal("'a'\"'\"'b'", AtomPipelineCommands.BashQuote("a'b"));
    }

    [Fact]
    public void ExportDataset_assigns_validation_and_acceptance_negative_roles()
    {
        var settings = new AtomPipelineSettings
        {
            RepositoryPath = @"C:\Development\MemoryError\Atom",
            PythonExecutable = "~/.venvs/atom/bin/python",
            ClickOffsetY = -70,
            OccludingInterfaceRoots = "517, 1251,517",
        };

        var command = AtomPipelineCommands.ExportDataset(
            settings, @"C:\Sessions\positive", @"C:\Sessions\validation-negative",
            "bank-target", "data/bank-target-acceptance", @"C:\Sessions\test-negative");

        Assert.Contains("\"$HOME\"/'.venvs/atom/bin/python'", command.Script);
        Assert.Contains("'--negative-session' '/mnt/c/Sessions/validation-negative'", command.Script);
        Assert.Contains("'--test-negative-session' '/mnt/c/Sessions/test-negative'", command.Script);
        Assert.Contains("'--click-offset-y' '-70'", command.Script);
        Assert.DoesNotContain("model.pt", command.Script);
        Assert.Equal(1, Count(command.Script, "'517'"));
        Assert.Equal(1, Count(command.Script, "'1251'"));
    }

    [Fact]
    public void Train_and_acceptance_commands_emit_progress_and_separate_receipt()
    {
        var settings = new AtomPipelineSettings
        {
            RepositoryPath = @"C:\Development\MemoryError\Atom",
            PythonExecutable = "~/.venvs/atom/bin/python",
        };

        var train = AtomPipelineCommands.Train(settings, "data/bank-target-reviewed", "components/bank-target-detector");
        var acceptance = AtomPipelineCommands.Evaluate(settings, "data/bank-target-acceptance",
            "components/bank-target-detector", "test", "data/bank-target-acceptance/acceptance.metrics.json");

        Assert.Contains("'--progress-json'", train.Script);
        Assert.Contains("'--metrics-output' 'data/bank-target-acceptance/acceptance.metrics.json'", acceptance.Script);
    }

    [Fact]
    public void Generic_task_commands_use_task_contract_without_target_geometry_flags()
    {
        var settings = new AtomPipelineSettings
        {
            RepositoryPath = @"C:\Development\MemoryError\Atom",
            PythonExecutable = "~/.venvs/atom/bin/python",
        };

        var export = AtomPipelineCommands.ExportTaskDataset(settings, "tasks/bank-target.task.json",
            @"C:\Sessions\positive", @"C:\Sessions\negative", "data/bank-target");
        var train = AtomPipelineCommands.TrainTask(settings, "tasks/bank-target.task.json",
            "data/bank-target-reviewed", "components/bank-target-detector");

        Assert.Contains("'export-task-dataset' 'tasks/bank-target.task.json'", export.Script);
        Assert.DoesNotContain("--click-offset", export.Script);
        Assert.Contains("'train-task' 'tasks/bank-target.task.json'", train.Script);
        Assert.Contains("'--progress-json'", train.Script);
    }

    [Fact]
    public void Task_catalog_discovers_repository_contracts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"atom-task-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "tasks"));
        try
        {
            File.WriteAllText(Path.Combine(root, "tasks", "anything.task.json"), """
            {
              "schemaVersion":"1.0","id":"anything","name":"Anything detector","taskType":"object-detection",
              "classes":["anything"],"labelProvider":{"id":"projected","parameters":{}},
              "recipe":{"id":"recipe","parameters":{}},
              "reviewPolicy":{"kinds":["positive"],"developmentSplits":["train"]},
              "acceptancePolicy":{"minimumPositive":1,"minimumNegative":1,"minimumPrecision":0.9,"minimumRecall":0.9,"maximumP95Milliseconds":75}
            }
            """);
            var task = Assert.Single(AtomTaskItem.LoadFromRepository(root));
            Assert.Equal("anything", task.Id);
            Assert.Equal("anything", task.PrimaryClass);
            Assert.Equal("tasks/anything.task.json", task.RelativePath);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ParseInterfaceRoots_rejects_invalid_values()
    {
        Assert.Throws<ArgumentException>(() => AtomPipelineCommands.ParseInterfaceRoots("517,nope"));
    }

    [Fact]
    public void Session_catalog_uses_actual_per_class_capture_coverage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"atom-foundry-test-{Guid.NewGuid():N}");
        var session = Path.Combine(root, "foundry_20260713_000000");
        Directory.CreateDirectory(session);
        try
        {
            File.WriteAllText(Path.Combine(session, "session.json"), """
            {
              "sessionId":"foundry_test", "cycleCount":3, "eventCount":20, "activity":"test",
              "targetClasses":[{"class":"bank-target","kind":"npc","id":1}],
              "targetCaptureSummary":[{"class":"bank-target","projectedFrames":10,"negativeFrames":4}],
              "captureCapabilities":{"projectedTargetTruth":true,"explicitAbsentSceneLabels":true}
            }
            """);

            var item = Assert.Single(FoundrySessionItem.LoadFromRoot(root));
            Assert.Contains("bank-target", item.ProjectedClasses);
            Assert.Contains("bank-target", item.NegativeClasses);
            Assert.True(item.HasProjectedTruth);
            Assert.True(item.HasExplicitNegatives);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static int Count(string value, string needle)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0; index += needle.Length) count++;
        return count;
    }
}
