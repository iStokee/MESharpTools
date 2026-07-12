using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using MESharp.API;

namespace MESharp;

internal sealed class FoundryRecorder : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private StreamWriter? _writer;
    private Stopwatch? _clock;
    private DateTime _started;
    private DateTime _clickCursor;
    private string? _sessionId;
    private string? _directory;
    private int _sequence;
    private int _cycles;
    private long _lastHeartbeatMs;
    private readonly KeyboardPoller _keyboard = new();
    private readonly HashSet<string> _absentTargets = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? Status;

    public void Start()
    {
        lock (_gate)
        {
            if (_cts is not null) return;
            _started = DateTime.UtcNow; _clickCursor = _started; _sequence = 0; _cycles = 0; _lastHeartbeatMs = -1000;
            _sessionId = $"foundry_{_started:yyyyMMdd_HHmmss}";
            _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Atom", "Foundry", _sessionId);
            Directory.CreateDirectory(Path.Combine(_directory, "frames"));
            _writer = new StreamWriter(new FileStream(Path.Combine(_directory, "events.jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            _clock = Stopwatch.StartNew();
            ScreenshotService.Enabled = true; ScreenshotService.BlackoutChat = false;
            DoActionDebugSignals.Configure(enabled: true);
            DoActionDebugSignals.StartNativePump();
            _keyboard.Start();
            _cts = new(); _loop = Task.Run(() => RunAsync(_cts.Token));
            WriteManifest(false);
            Emit($"Recording to {_directory}. Complete a cycle, then press Mark cycle.");
        }
    }

    public void MarkCycle()
    {
        if (_cts is null) return;
        _cycles++;
        WriteEvent("marker", expert: new Dictionary<string, object?> { ["actionId"] = $"cycle.{_cycles}", ["operation"] = "marker.cycle", ["name"] = $"Cycle {_cycles}" });
        Emit($"Cycle {_cycles} marked. {Math.Max(0, 5 - _cycles)} remaining.");
    }

    public void SetTargetAbsent(string targetClass, bool absent)
    {
        lock (_gate)
        {
            if (absent) _absentTargets.Add(targetClass);
            else _absentTargets.Remove(targetClass);
        }
        if (_cts is not null) WriteEvent("state-change");
    }

    public void Stop()
    {
        CancellationTokenSource? cts; Task? loop;
        lock (_gate) { cts = _cts; loop = _loop; _cts = null; _loop = null; }
        if (cts is null) return;
        cts.Cancel(); try { loop?.Wait(2000); } catch { }
        _keyboard.Dispose();
        try { DoActionDebugSignals.StopNativePump(); } catch { }
        lock (_gate) { _writer?.Dispose(); _writer = null; }
        WriteManifest(true);
        Emit($"Finalized {_cycles} cycles at {_directory}.");
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                DrainClicks();
                DrainKeys();
                if (_clock is not null && _clock.ElapsedMilliseconds - _lastHeartbeatMs >= 1000)
                {
                    WriteEvent("heartbeat");
                    _lastHeartbeatMs = _clock.ElapsedMilliseconds;
                }
            }
            catch (Exception ex) { Emit($"Capture error: {ex.Message}"); }
            try { await Task.Delay(200, token); } catch { break; }
        }
    }

    private void DrainClicks()
    {
        var signals = DoActionDebugSignals.Snapshot(200, includeFailed: true)
            .Where(s => s.TimestampUtc > _clickCursor && string.Equals(s.Surface, "Native", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.TimestampUtc).ToList();
        foreach (var signal in signals)
        {
            var expert = new Dictionary<string, object?>();
            var input = new Dictionary<string, object?> { ["surface"] = signal.Surface, ["operation"] = signal.Operation, ["result"] = signal.Result };
            if (signal.ScreenX.HasValue && signal.ScreenY.HasValue)
                input["point"] = new[] { signal.ScreenX.Value, signal.ScreenY.Value };
            if (DoActionDebugSignals.TryParseCapture(signal.Snippet, out var capture))
            {
                var name = ResolveName(capture);
                var actionId = Slug($"{capture.Kind}.{name}.{capture.ActionOpcode}");
                expert["actionId"] = actionId; expert["operation"] = $"interact.{capture.Kind.ToLowerInvariant()}"; expert["name"] = name;
                expert["parameters"] = new Dictionary<string, object?> { ["id"] = capture.Id, ["action"] = capture.ActionOpcode };
                input["kind"] = capture.Kind; input["id"] = capture.Id; input["name"] = name; input["action"] = capture.ActionOpcode;
                if (capture.Item != 0) input["item"] = capture.Item;
                if (!string.IsNullOrWhiteSpace(capture.InterfacePath)) input["interfacePath"] = capture.InterfacePath;
            }
            else
            {
                expert["actionId"] = Slug($"input.{signal.Operation}"); expert["operation"] = "input.native"; expert["name"] = signal.Operation;
            }
            WriteEvent("input", input, expert, new Dictionary<string, object?> { ["result"] = signal.Result ? "success" : "failure", ["durationMs"] = 1000 });
            _clickCursor = signal.TimestampUtc;
        }
    }

    private void DrainKeys()
    {
        foreach (var key in _keyboard.Drain())
        {
            WriteEvent("input",
                input: new Dictionary<string, object?> { ["surface"] = "Keyboard", ["operation"] = "KeyDown", ["virtualKey"] = key.virtualKey },
                expert: new Dictionary<string, object?>
                {
                    ["actionId"] = $"keyboard.key.{key.virtualKey}", ["operation"] = "input.key", ["name"] = $"Key {key.virtualKey}",
                    ["parameters"] = new Dictionary<string, object?> { ["virtualKey"] = key.virtualKey }
                },
                outcome: new Dictionary<string, object?> { ["result"] = "success", ["durationMs"] = 100 });
        }
    }

    private void WriteEvent(string reason, object? input = null, object? expert = null, object? outcome = null)
    {
        if (_directory is null || _sessionId is null || _clock is null) return;
        var capturePath = ScreenshotService.Capture(_sessionId, force: true) ?? throw new InvalidOperationException("Screenshot capture unavailable.");
        var relative = Path.Combine("frames", $"{_sequence:D8}.png");
        var destination = Path.Combine(_directory, relative);
        File.Copy(capturePath, destination, true);
        var dimensions = PngDimensions(destination);
        var record = new Dictionary<string, object?>
        {
            ["schemaVersion"] = "1.0", ["sessionId"] = _sessionId, ["sequence"] = _sequence++,
            ["monotonicMs"] = _clock.ElapsedMilliseconds, ["capturedAt"] = DateTime.UtcNow.ToString("O"), ["reason"] = reason,
            ["frame"] = new Dictionary<string, object?> { ["path"] = relative.Replace('\\', '/'), ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))).ToLowerInvariant(), ["width"] = dimensions.width, ["height"] = dimensions.height },
            ["input"] = input, ["truth"] = CaptureTruth(), ["expert"] = expert,
            ["validation"] = CurrentSceneValidation(), ["outcome"] = outcome
        };
        lock (_gate) _writer?.WriteLine(JsonSerializer.Serialize(record, Json));
    }

    private object? CurrentSceneValidation()
    {
        lock (_gate)
            return _absentTargets.Count == 0 ? null : new { absentClasses = _absentTargets.OrderBy(value => value).ToArray() };
    }

    private static object CaptureTruth()
    {
        var tile = LocalPlayer.GetTilePosition();
        var inventory = Inventory.GetAll().Where(i => i.Id > 0).Select(i => new { id = i.Id, name = i.Name, amount = i.Amount }).ToList();
        var interfaces = Interfaces.GetAll(-1, true).Where(i => i.Width > 0 && i.Height > 0).Take(500)
            .Select(i => new { path = i.FullIdPath, name = i.Name, text = i.TextItem, bounds = new[] { i.X, i.Y, i.Width, i.Height }, itemId = i.ItemId }).ToList();
        var entities = Objects.GetAll().Where(o => o.HasAction).OrderBy(o => o.Distance).Take(200)
            .Select(o => new { id = o.Id, name = o.Name, action = o.Action, tile = new[] { o.TileX, o.TileY, o.TileZ }, kind = o.Type, distance = o.Distance }).ToList();
        var npcs = Npcs.GetAll().OrderBy(n => n.Distance).Take(200)
            .Select(n => new { id = n.Id, name = n.Name, tile = new[] { n.X, n.Y, tile.z }, distance = n.Distance }).ToList();
        return new { player = new { tile = new[] { tile.x, tile.y, tile.z }, moving = LocalPlayer.IsMoving(), animation = LocalPlayer.GetAnimation() }, inventory, interfaces, entities, npcs };
    }

    private static string ResolveName(DoActionDebugSignals.CaptureInfo capture)
    {
        if (!string.IsNullOrWhiteSpace(capture.Name)) return capture.Name;
        try
        {
            if (string.Equals(capture.Kind, "NPC", StringComparison.OrdinalIgnoreCase))
                return Npcs.GetAll().Where(n => n.Id == capture.Id).OrderBy(n => n.Distance).Select(n => n.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? capture.Kind;
            return Objects.GetAll().Where(o => o.Id == capture.Id).OrderBy(o => o.Distance).Select(o => o.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? capture.Kind;
        }
        catch { return capture.Kind; }
    }

    private void WriteManifest(bool finalized)
    {
        if (_directory is null) return;
        var manifest = new
        {
            schemaVersion = "1.0", sessionId = _sessionId, startedAt = _started.ToString("O"),
            finalizedAt = finalized ? DateTime.UtcNow.ToString("O") : null, cycleCount = _cycles, eventCount = _sequence,
            captureCapabilities = new { nativeGameInput = true, keyboardInput = true, clientCursorPosition = true, explicitAbsentSceneLabels = true },
            privacy = new { chatRedacted = false, keyboardCapturedDuringRecording = true }
        };
        File.WriteAllText(Path.Combine(_directory, "session.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static (int width, int height) PngDimensions(string path)
    {
        using var stream = File.OpenRead(path); Span<byte> header = stackalloc byte[24]; stream.ReadExactly(header);
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..20]), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }

    private static string Slug(string value) => string.Concat(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '.')).Trim('.');
    private void Emit(string message) => Status?.Invoke(message);
    public void Dispose() => Stop();
}
