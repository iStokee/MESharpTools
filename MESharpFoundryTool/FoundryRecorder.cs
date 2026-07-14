using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using MESharp.API;

namespace MESharp;

/// <summary>Snapshot of one configured target class as last observed (live truth feedback).</summary>
internal sealed record FoundryTargetStatus(string ClassName, string Kind, int Id, bool Present, float[]? Screen, float? Distance, int[]? Tile);
internal sealed record FoundryTargetCaptureSummary(string ClassName, int Frames, int PresentFrames, int ProjectedFrames, int NegativeFrames);

internal sealed class FoundryRecorder : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    private readonly object _gate = new();
    // WriteEvent runs from both the capture loop and UI actions (Mark cycle, absent toggles);
    // this serializes sequence numbers, frame files, and gap bookkeeping.
    private readonly object _writeLock = new();
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
    private long _lastEventMs;
    private long _largestEventGapMs;
    private int _inputEventsThisCycle;
    private readonly List<int> _inputEventsPerCycle = new();
    private string _activity = "";
    private string _notes = "";
    private IReadOnlyList<FoundryTargetClass> _targets = [];
    private volatile IReadOnlyList<FoundryTargetStatus> _lastTargetStatus = [];
    private readonly Dictionary<string, MutableTargetCaptureSummary> _targetCapture = new(StringComparer.OrdinalIgnoreCase);
    private readonly KeyboardPoller _keyboard = new();
    private readonly HashSet<string> _absentTargets = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? Status;

    public bool IsRecording { get { lock (_gate) return _cts is not null; } }
    public string? SessionDirectory { get { lock (_gate) return _directory; } }
    public int EventCount => Volatile.Read(ref _sequence);
    public int CycleCount => Volatile.Read(ref _cycles);
    public int InputEventsThisCycle => Volatile.Read(ref _inputEventsThisCycle);
    public TimeSpan Elapsed { get { lock (_gate) return _clock?.Elapsed ?? TimeSpan.Zero; } }
    public string LastInputDescription { get; private set; } = "";

    /// <summary>Target truth from the most recent captured event (~1 Hz while recording).</summary>
    public IReadOnlyList<FoundryTargetStatus> LastTargetStatus => _lastTargetStatus;

    /// <summary>Input-event counts of completed cycles (UI-thread access only, like MarkCycle/Stop).</summary>
    public IReadOnlyList<int> CompletedCycleInputCounts => _inputEventsPerCycle;

    /// <summary>Resolves the given bindings against the live scene without creating a session.</summary>
    public static IReadOnlyList<FoundryTargetStatus> ProbeTargets(IEnumerable<FoundryTargetClass> targets)
    {
        var allNpcs = Npcs.GetAll();
        var allObjects = Objects.GetAll();
        var (width, height) = ClientSize();
        var results = new List<FoundryTargetStatus>();
        foreach (var target in targets.Where(t => !string.IsNullOrWhiteSpace(t.ClassName) && t.Id > 0))
            results.Add(ResolveTarget(target, allNpcs, allObjects, width, height));
        return results;
    }

    public void Start(FoundrySettings settings)
    {
        lock (_gate)
        {
            if (_cts is not null) return;
            var configuredTargets = settings.TargetClasses
                .Where(t => !string.IsNullOrWhiteSpace(t.ClassName) && t.Id > 0)
                .ToArray();
            var duplicateClass = configuredTargets.GroupBy(t => t.ClassName.Trim(), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1)?.Key;
            if (duplicateClass is not null)
                throw new InvalidOperationException($"Target class '{duplicateClass}' is configured more than once.");
            var invalidKind = configuredTargets.FirstOrDefault(t =>
                !string.Equals(t.Kind, "npc", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(t.Kind, "object", StringComparison.OrdinalIgnoreCase));
            if (invalidKind is not null)
                throw new InvalidOperationException($"Target class '{invalidKind.ClassName}' has unsupported kind '{invalidKind.Kind}'.");
            _started = DateTime.UtcNow; _clickCursor = _started; _sequence = 0; _cycles = 0; _lastHeartbeatMs = -1000;
            _lastEventMs = 0; _largestEventGapMs = 0; _inputEventsThisCycle = 0; _inputEventsPerCycle.Clear();
            LastInputDescription = "";
            _activity = settings.Activity?.Trim() ?? "";
            _notes = settings.Notes?.Trim() ?? "";
            _targets = configuredTargets
                .Select(t => new FoundryTargetClass { ClassName = t.ClassName.Trim(), Kind = t.Kind, Id = t.Id })
                .ToArray();
            _lastTargetStatus = [];
            _targetCapture.Clear();
            foreach (var target in _targets)
                _targetCapture[target.ClassName] = new MutableTargetCaptureSummary();
            _absentTargets.Clear();
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
        _inputEventsPerCycle.Add(Volatile.Read(ref _inputEventsThisCycle));
        Volatile.Write(ref _inputEventsThisCycle, 0);
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
        Emit(BuildSummary());
    }

    /// <summary>Capture-trust readout shown after Stop; full validation stays in atom_lab.</summary>
    private string BuildSummary()
    {
        var lines = new List<string> { $"Finalized {_cycles} cycles, {_sequence} events at {_directory}." };
        var trailing = Volatile.Read(ref _inputEventsThisCycle);
        for (var index = 0; index < _inputEventsPerCycle.Count; index++)
        {
            var count = _inputEventsPerCycle[index];
            lines.Add($"  Cycle {index + 1}: {count} input event(s){(count == 0 ? "  ⚠ no inputs captured" : "")}");
        }
        if (trailing > 0) lines.Add($"  ⚠ {trailing} input event(s) after the last cycle marker (unmarked work).");
        if (_cycles < 5) lines.Add($"  ⚠ only {_cycles} cycles marked; atom_lab compile expects five.");
        if (_largestEventGapMs > 3000) lines.Add($"  ⚠ largest gap between events was {_largestEventGapMs / 1000.0:F1}s (heartbeat stalled?).");
        foreach (var target in TargetCaptureSummaries())
        {
            lines.Add($"  {target.ClassName}: projected {target.ProjectedFrames}/{target.Frames} frames ({target.PresentFrames} present), {target.NegativeFrames} explicit negative.");
            if (target.ProjectedFrames == 0)
                lines.Add($"    ⚠ no positive projected frames for {target.ClassName}; this target dataset is unusable.");
            if (target.NegativeFrames == 0)
                lines.Add($"    ⚠ no explicit negative scenes for {target.ClassName}; false-positive coverage is incomplete.");
        }
        return string.Join(Environment.NewLine, lines);
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
                LastInputDescription = $"{capture.Kind} '{name}' opcode {capture.ActionOpcode}";
            }
            else
            {
                expert["actionId"] = Slug($"input.{signal.Operation}"); expert["operation"] = "input.native"; expert["name"] = signal.Operation;
                LastInputDescription = signal.Operation;
            }
            Interlocked.Increment(ref _inputEventsThisCycle);
            WriteEvent("input", input, expert, new Dictionary<string, object?> { ["result"] = signal.Result ? "success" : "failure", ["durationMs"] = 1000 });
            _clickCursor = signal.TimestampUtc;
        }
    }

    private void DrainKeys()
    {
        foreach (var key in _keyboard.Drain())
        {
            LastInputDescription = $"Key {key.virtualKey}";
            Interlocked.Increment(ref _inputEventsThisCycle);
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
        lock (_writeLock)
        {
            if (_directory is null || _sessionId is null || _clock is null) return;
            var capturePath = ScreenshotService.Capture(_sessionId, force: true) ?? throw new InvalidOperationException("Screenshot capture unavailable.");
            var relative = Path.Combine("frames", $"{_sequence:D8}.png");
            var destination = Path.Combine(_directory, relative);
            File.Copy(capturePath, destination, true);
            var dimensions = PngDimensions(destination);
            var elapsed = _clock.ElapsedMilliseconds;
            if (_sequence > 0) _largestEventGapMs = Math.Max(_largestEventGapMs, elapsed - _lastEventMs);
            _lastEventMs = elapsed;
            var record = new Dictionary<string, object?>
            {
                ["schemaVersion"] = "1.0", ["sessionId"] = _sessionId, ["sequence"] = _sequence++,
                ["monotonicMs"] = elapsed, ["capturedAt"] = DateTime.UtcNow.ToString("O"), ["reason"] = reason,
                ["frame"] = new Dictionary<string, object?> { ["path"] = relative.Replace('\\', '/'), ["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(destination))).ToLowerInvariant(), ["width"] = dimensions.width, ["height"] = dimensions.height },
                ["input"] = input, ["cursor"] = CaptureCursor(), ["truth"] = CaptureTruth(dimensions.width, dimensions.height), ["expert"] = expert,
                ["validation"] = CurrentSceneValidation(), ["outcome"] = outcome
            };
            lock (_gate) _writer?.WriteLine(JsonSerializer.Serialize(record, Json));
        }
    }

    private object? CurrentSceneValidation()
    {
        lock (_gate)
            return _absentTargets.Count == 0 ? null : new { absentClasses = _absentTargets.OrderBy(value => value).ToArray() };
    }

    private object CaptureTruth(int frameWidth, int frameHeight)
    {
        var tile = LocalPlayer.GetTilePosition();
        var inventory = Inventory.GetAll().Where(i => i.Id > 0).Select(i => new { id = i.Id, name = i.Name, amount = i.Amount }).ToList();
        var interfaces = Interfaces.GetAll(-1, true).Where(i => i.Width > 0 && i.Height > 0).Take(500)
            .Select(i => new { path = i.FullIdPath, name = i.Name, text = i.TextItem, bounds = new[] { i.X, i.Y, i.Width, i.Height }, itemId = i.ItemId }).ToList();
        var allObjects = Objects.GetAll();
        var allNpcs = Npcs.GetAll();
        var entities = allObjects.Where(o => o.HasAction).OrderBy(o => o.Distance).Take(200)
            .Select(o => new { id = o.Id, name = o.Name, action = o.Action, tile = new[] { o.TileX, o.TileY, o.TileZ }, kind = o.Type, distance = o.Distance }).ToList();
        var npcs = allNpcs.OrderBy(n => n.Distance).Take(200)
            .Select(n => new { id = n.Id, name = n.Name, tile = new[] { n.X, n.Y, tile.z }, distance = n.Distance }).ToList();
        return new
        {
            player = new { tile = new[] { tile.x, tile.y, tile.z }, moving = LocalPlayer.IsMoving(), animation = LocalPlayer.GetAnimation() },
            inventory, interfaces, entities, npcs,
            targets = CaptureTargets(allNpcs, allObjects, frameWidth, frameHeight)
        };
    }

    /// <summary>
    /// Records the screen pixels already calculated by the native entity scanner so every frame —
    /// hovered or not, present or absent — carries a usable label.
    /// </summary>
    private List<object> CaptureTargets(IReadOnlyList<Npcs.Npc> allNpcs, IReadOnlyList<Objects.GameObject> allObjects, int frameWidth, int frameHeight)
    {
        var statuses = new List<FoundryTargetStatus>(_targets.Count);
        foreach (var target in _targets)
            statuses.Add(ResolveTarget(target, allNpcs, allObjects, frameWidth, frameHeight));
        HashSet<string> absent;
        lock (_gate) absent = new HashSet<string>(_absentTargets, StringComparer.OrdinalIgnoreCase);
        foreach (var status in statuses)
        {
            var summary = _targetCapture[status.ClassName];
            summary.Frames++;
            if (status.Present) summary.PresentFrames++;
            if (status.Screen is { Length: 2 }) summary.ProjectedFrames++;
            if (absent.Contains(status.ClassName)) summary.NegativeFrames++;
        }
        _lastTargetStatus = statuses;
        return statuses.Select(object (s) => new
        {
            @class = s.ClassName,
            kind = s.Kind,
            id = s.Id,
            present = s.Present,
            screen = s.Screen,
            distance = s.Distance,
            tile = s.Tile,
        }).ToList();
    }

    private static FoundryTargetStatus ResolveTarget(
        FoundryTargetClass target, IReadOnlyList<Npcs.Npc> allNpcs, IReadOnlyList<Objects.GameObject> allObjects,
        int frameWidth, int frameHeight)
    {
        (float x, float y, float z)? pixel = null;
        float distance = 0; int[]? entityTile = null;
        if (string.Equals(target.Kind, "npc", StringComparison.OrdinalIgnoreCase))
        {
            var npc = allNpcs.Where(n => n.Id == target.Id).OrderBy(n => n.Distance).FirstOrDefault();
            if (npc is not null) { pixel = npc.Pixel; distance = npc.Distance; entityTile = new[] { npc.X, npc.Y }; }
        }
        else
        {
            var entity = allObjects.Where(o => o.Id == target.Id).OrderBy(o => o.Distance).FirstOrDefault();
            if (entity is not null) { pixel = entity.Pixel; distance = entity.Distance; entityTile = new[] { entity.TileX, entity.TileY, entity.TileZ }; }
        }

        float[]? screen = pixel is { } p && IsUsableScreenPoint(p.x, p.y, frameWidth, frameHeight)
            ? new[] { p.x, p.y }
            : null;
        return new(target.ClassName, target.Kind.ToLowerInvariant(), target.Id,
            pixel is not null, screen, pixel is null ? null : distance, entityTile);
    }

    private static bool IsUsableScreenPoint(float x, float y, int width, int height) =>
        float.IsFinite(x) && float.IsFinite(y) && width > 0 && height > 0 &&
        x > 0 && y > 0 && x < width && y < height;

    private static (int width, int height) ClientSize()
    {
        try
        {
            var hwnd = Game.GameWindow;
            return hwnd != nint.Zero && GetClientRect(hwnd, out var rect)
                ? (Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top))
                : (0, 0);
        }
        catch { return (0, 0); }
    }

    private static int[]? CaptureCursor()
    {
        try
        {
            if (!GetCursorPos(out var point)) return null;
            var hwnd = Game.GameWindow;
            if (hwnd != nint.Zero && ScreenToClient(hwnd, ref point)) return new[] { point.X, point.Y };
            return null;
        }
        catch { return null; }
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
            activity = string.IsNullOrEmpty(_activity) ? null : _activity,
            notes = string.IsNullOrEmpty(_notes) ? null : _notes,
            targetClasses = _targets.Count == 0 ? null : _targets.Select(t => new { @class = t.ClassName, kind = t.Kind.ToLowerInvariant(), id = t.Id }).ToArray(),
            targetCaptureSummary = _targets.Count == 0 ? null : TargetCaptureSummaries().Select(summary => new
            {
                @class = summary.ClassName, frames = summary.Frames,
                presentFrames = summary.PresentFrames, projectedFrames = summary.ProjectedFrames,
                negativeFrames = summary.NegativeFrames
            }).ToArray(),
            captureCapabilities = new
            {
                nativeGameInput = true, keyboardInput = true, clientCursorPosition = true,
                explicitAbsentSceneLabels = true, projectedTargetTruth = _targets.Count > 0
            },
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
    private IReadOnlyList<FoundryTargetCaptureSummary> TargetCaptureSummaries() => _targets.Select(target =>
    {
        var value = _targetCapture.TryGetValue(target.ClassName, out var summary) ? summary : new MutableTargetCaptureSummary();
        return new FoundryTargetCaptureSummary(target.ClassName, value.Frames, value.PresentFrames, value.ProjectedFrames, value.NegativeFrames);
    }).ToArray();
    private void Emit(string message) => Status?.Invoke(message);
    public void Dispose() => Stop();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hwnd, ref Win32Point point);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out Win32Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect { public int Left; public int Top; public int Right; public int Bottom; }

    private sealed class MutableTargetCaptureSummary
    {
        public int Frames;
        public int PresentFrames;
        public int ProjectedFrames;
        public int NegativeFrames;
    }
}
