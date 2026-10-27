using MESharp.API;
using MESharp.ViewModels;
using Xunit;

namespace csharp_interop.Tests;

public sealed class DoActionFeedTests
{
    [Fact]
    public void Refresh_preserves_selected_capture_and_filter_changes_visible_feed()
    {
        StaTestRunner.Run(() =>
        {
            var config = DoActionDebugSignals.GetConfig();
            DoActionDebugSignals.Configure(enabled: true, echoToConsole: false);
            try
            {
                // Managed buffer only: no native pump or gameplay dispatch.
                var marker = Guid.NewGuid().ToString("N");
                var snippet = $"MECAP|Object|id=123|action=46|offset=0|tile=3227,3429,0|name={marker}";
                DoActionDebugSignals.Record("Native", "capture", snippet, true, tile: new WorldPoint(3227,3429,0));
                using var vm = new DoActionSignalsViewModel { AutoRefresh = false, FilterText = marker };
                Assert.Single(vm.ClickHistory);
                vm.SelectedClick = vm.ClickHistory[0];
                vm.SelectedSignal = vm.Signals[0];
                vm.RefreshCommand.Execute(null);
                Assert.NotNull(vm.SelectedClick);
                Assert.NotNull(vm.SelectedSignal);
                Assert.Equal(snippet, vm.SelectedClick.Snippet);
                Assert.False(vm.IsFeedEmpty);
                vm.FilterText = marker + "no-match";
                Assert.Empty(vm.Signals);
                Assert.True(vm.IsFeedEmpty);
                Assert.Null(vm.SelectedClick);
                Assert.False(vm.CopyAllCommand.CanExecute(null));
            }
            finally { DoActionDebugSignals.Configure(config.Enabled, config.EchoToConsole); }
        });
    }

    [Fact]
    public void Copy_visible_follows_active_view()
    {
        StaTestRunner.Run(() =>
        {
            using var vm = new DoActionSignalsViewModel { FilterText = Guid.NewGuid().ToString(), AutoRefresh = false };
            vm.Signals.Add(new DoActionSignalRow());
            Assert.False(vm.CopyAllCommand.CanExecute(null));
            vm.ShowSignalsView = true;
            Assert.True(vm.CopyAllCommand.CanExecute(null));
        });
    }
}
