using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Xunit.Sdk;

namespace csharp_interop.Tests;

internal static class StaTestRunner
{
    private static readonly BlockingCollection<WorkItem> WorkItems = new();
    private static readonly Thread Thread;

    static StaTestRunner()
    {
        Thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = "csharp_interop.Tests.WpfSta"
        };
        Thread.SetApartmentState(ApartmentState.STA);
        Thread.Start();
    }

    public static void Run(Action action)
    {
        var done = new ManualResetEventSlim(false);
        Exception? exception = null;

        WorkItems.Add(new WorkItem(
            () =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
                finally
                {
                    done.Set();
                }
            }));

        done.Wait();

        if (exception != null)
        {
            throw new XunitException(exception.ToString());
        }
    }

    public static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunLoop()
    {
        var app = Application.Current ?? new Application();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        foreach (var item in WorkItems.GetConsumingEnumerable())
        {
            item.Run();
        }
    }

    private sealed record WorkItem(Action Run);
}
