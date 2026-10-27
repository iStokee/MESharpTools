using System.Reflection;
using MESharp.Services;
using MESharp.ViewModels;
using Xunit;

namespace MESharpNavTool.Tests;

// Shares the static map server with the authorization tests.
[Collection("Map journey")]
public sealed class JourneyStatusTests
{
    [Fact]
    public async Task CompletingMapTravelUpdatesTheHubWithoutWaitingForItsStatusTimer()
    {
        CoverageMapServer.Stop();
        using var hub = new NavigationHubViewModel();
        var finish = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStateChanged()
        {
            if (!CoverageMapServer.TravelState.Running) completed.TrySetResult();
        }
        CoverageMapServer.TravelStateChanged += OnStateChanged;
        try
        {
            Assert.False(hub.StopJourneyCommand.CanExecute(null));
            var start = typeof(CoverageMapServer).GetMethod("StartTravelStatusTask", BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.True((bool)start.Invoke(null, new object[]
            {
                "Travel to target", (Func<CancellationToken, Task<string>>)(_ => finish.Task), CancellationToken.None
            })!);
            Assert.True(hub.IsJourneyActive);
            Assert.True(hub.StopJourneyCommand.CanExecute(null));
            Assert.Equal("Journey in progress", hub.JourneyHeading);

            finish.SetResult("Travel to target finished.");
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(hub.IsJourneyActive);
            Assert.False(hub.StopJourneyCommand.CanExecute(null));
            Assert.Equal("Last journey", hub.JourneyHeading);
            Assert.Equal("Travel to target finished.", hub.JourneyText);
        }
        finally
        {
            finish.TrySetResult("Travel cancelled.");
            CoverageMapServer.TravelStateChanged -= OnStateChanged;
            CoverageMapServer.Stop();
        }
    }
}

[CollectionDefinition("Map journey", DisableParallelization = true)]
public sealed class MapJourneyCollection;
