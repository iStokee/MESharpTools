using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace MESharp;

internal sealed class KeyboardPoller : IDisposable
{
    private readonly ConcurrentQueue<(DateTime utc, int virtualKey)> _downs = new();
    private readonly bool[] _pressed = new bool[256];
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    public IReadOnlyList<(DateTime utc, int virtualKey)> Drain()
    {
        var result = new List<(DateTime, int)>();
        while (_downs.TryDequeue(out var key)) result.Add(key);
        return result;
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            // Exclude mouse virtual keys 1-6; mouse actions come from the structured DoAction hook.
            for (var key = 7; key < 256; key++)
            {
                var down = (GetAsyncKeyState(key) & 0x8000) != 0;
                if (down && !_pressed[key]) _downs.Enqueue((DateTime.UtcNow, key));
                _pressed[key] = down;
            }
            try { await Task.Delay(15, token).ConfigureAwait(false); }
            catch { break; }
        }
    }

    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is null) return;
        cts.Cancel();
        try { _loop?.Wait(500); } catch { }
        cts.Dispose();
        _loop = null;
        Array.Clear(_pressed);
        while (_downs.TryDequeue(out _)) { }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
