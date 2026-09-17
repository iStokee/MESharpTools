using System;
using System.Threading;
using System.Threading.Tasks;
using MESharp.Services;
using Xunit;

namespace MESharpNavTool.Tests;

public sealed class NavigationRunGateTests
{
    [Fact]
    public async Task MapRunWaitsUntilActiveRunReleasesMovement()
    {
        using var first = NavigationRunGate.TryEnter();
        Assert.NotNull(first);
        using var nested = NavigationRunGate.TryEnter();
        Assert.NotNull(nested);

        Task<bool> pending;
        using (ExecutionContext.SuppressFlow())
        {
            pending = Task.Run(() => NavigationExecutionGate.RunAsync(() => Task.FromResult(true)));
        }
        Assert.False(pending.IsCompleted);

        first.Dispose();
        Assert.True(await pending);
    }
}
