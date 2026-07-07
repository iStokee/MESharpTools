using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;

namespace MESharp.Recording
{
    /// <summary>
    /// Records a player-driven session as a neutral demonstration trace: a continuous ~1 Hz heartbeat of
    /// game state (with deltas), plus an extra event each time the player makes a real in-game click/interaction
    /// (carrying the native action opcode + route offset captured by ME's DoAction detour), plus a screenshot at
    /// each significant moment. The output is a self-contained session folder (meta.json + events.jsonl + frames)
    /// that a coding agent reads back to learn how a human actually plays a quest / boss / dungeon floor — ground
    /// truth to seed or correct a script, rather than guessing blindly.
    ///
    /// Domain-neutral on purpose: it captures location, inventory, equipment, nearby objects/NPCs, open
    /// interfaces and (when in Daemonheim) DG object classification. The per-domain interpretation
    /// (trace -> quest recipe, trace -> boss mechanics, trace -> DG puzzle handler) happens offline, off the
    /// trace. This is the reusable capture half only.
    /// </summary>
    public sealed class TraceRecorder
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        private readonly object _sync = new();
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private StreamWriter? _writer;
        private FrameState? _prev;
        private DateTime _startUtc;
        private DateTime _lastHeartbeatUtc;
        private DateTime _lastClickCursorUtc;
        private DateTime _burstUntilUtc;
        private DateTime _lastBurstShotUtc;
        private DateTime _lastPeriodicShotUtc;
        private int _seq;
        private int _heartbeatsSinceKeyframe;
        private bool _priorScreenshotEnabled;

        public bool IsRecording { get { lock (_sync) return _cts != null; } }
        public int SampleCount { get; private set; }
        public int HeartbeatCount { get; private set; }
        public int ClickCount { get; private set; }
        public int ScreenshotCount { get; private set; }
        public string? SessionId { get; private set; }
        public string? SessionDir { get; private set; }

        /// <summary>Status/diagnostic lines for the UI. Raised from the recorder thread.</summary>
        public event Action<string>? Log;

        public TraceRecorderOptions Options { get; private set; } = new();

        public void Start(TraceRecorderOptions? options = null)
        {
            lock (_sync)
            {
                if (_cts != null)
                {
                    return;
                }

                Options = options ?? new TraceRecorderOptions();
                _startUtc = DateTime.UtcNow;
                _lastHeartbeatUtc = DateTime.MinValue;
                _lastClickCursorUtc = _startUtc; // ignore clicks buffered before we armed
                _burstUntilUtc = DateTime.MinValue;
                _lastBurstShotUtc = DateTime.MinValue;
                _lastPeriodicShotUtc = DateTime.MinValue;
                _seq = 0;
                _heartbeatsSinceKeyframe = int.MaxValue; // force a keyframe on the first heartbeat
                _prev = null;
                SampleCount = HeartbeatCount = ClickCount = ScreenshotCount = 0;

                var player = SafeName();
                SessionId = $"trace_{Sanitize(player)}_{_startUtc:yyyyMMdd_HHmmss}";
                SessionDir = Path.Combine(TracesRoot(), SessionId);
                Directory.CreateDirectory(SessionDir);

                if (Options.IncludeNativeClicks || Options.IncludeManagedDoActions)
                {
                    try { DoActionDebugSignals.Configure(enabled: true); } catch { }
                }

                // Arm the native player-click capture + draining pump (refcounted — coexists with the live feed).
                if (Options.IncludeNativeClicks)
                {
                    try
                    {
                        var bridge = DoActionDebugSignals.VerifyNativeBridge();
                        if (!bridge.Available)
                        {
                            Emit($"WARNING: native click bridge unavailable — clicks won't be captured. {bridge.Error}");
                        }
                    }
                    catch (Exception ex) { Emit($"WARNING: bridge check failed: {ex.Message}"); }
                    try { DoActionDebugSignals.StartNativePump(); } catch (Exception ex) { Emit($"WARNING: pump start failed: {ex.Message}"); }
                }

                _priorScreenshotEnabled = ScreenshotService.Enabled;
                if (Options.CaptureScreenshots)
                {
                    ScreenshotService.Enabled = true;
                }

                WriteMeta(player);

                _writer = new StreamWriter(new FileStream(
                    Path.Combine(SessionDir, "events.jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    AutoFlush = true
                };

                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                _loop = Task.Run(() => LoopAsync(token), token);
                Emit($"Recording started → {SessionDir}");
            }
        }

        public void Stop()
        {
            CancellationTokenSource? cts;
            Task? loop;
            StreamWriter? writer;
            lock (_sync)
            {
                cts = _cts;
                loop = _loop;
                writer = _writer;
                _cts = null;
                _loop = null;
                _writer = null;
            }

            if (cts == null)
            {
                return;
            }

            try { cts.Cancel(); } catch { }
            try { loop?.Wait(2000); } catch { }
            if (Options.IncludeNativeClicks)
            {
                try { DoActionDebugSignals.StopNativePump(); } catch { }
            }
            ScreenshotService.Enabled = _priorScreenshotEnabled;

            try { writer?.Flush(); writer?.Dispose(); } catch { }
            WriteSummary();
            Emit($"Recording stopped. {SampleCount} samples ({HeartbeatCount} heartbeats, {ClickCount} clicks, {ScreenshotCount} screenshots) → {SessionDir}");
        }

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (IsSampleable())
                    {
                        // Clicks are time-sensitive: drain them every tick so each interaction lands promptly.
                        DrainClicks();

                        if ((DateTime.UtcNow - _lastHeartbeatUtc).TotalMilliseconds >= Options.HeartbeatMs)
                        {
                            _lastHeartbeatUtc = DateTime.UtcNow;
                            EmitHeartbeat();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Emit($"sample error: {ex.GetType().Name}: {ex.Message}");
                }

                try { await Task.Delay(Options.PollMs, token).ConfigureAwait(false); }
                catch { break; }
            }
        }

        // ── Heartbeat ────────────────────────────────────────────────────────────

        private void EmitHeartbeat()
        {
            var now = DateTime.UtcNow;
            var cur = CaptureFrame();
            var keyframe = _heartbeatsSinceKeyframe >= Options.KeyframeEveryHeartbeats;
            var delta = _prev == null ? null : Diff(_prev, cur);

            var record = new Dictionary<string, object?>
            {
                ["seq"] = _seq++,
                ["tMs"] = (long)(now - _startUtc).TotalMilliseconds,
                ["utc"] = now.ToString("O", CultureInfo.InvariantCulture),
                ["reason"] = "heartbeat",
                ["player"] = cur.Player,
            };
            if (cur.Dg != null)
            {
                record["dg"] = cur.Dg;
            }
            if (delta != null && delta.Count > 0)
            {
                record["delta"] = delta;
            }

            var interfacesOpened = delta != null && delta.ContainsKey("ifOpen");
            if (keyframe)
            {
                record["keyframe"] = BuildKeyframe(cur);
                _heartbeatsSinceKeyframe = 0;
            }
            else
            {
                _heartbeatsSinceKeyframe++;
            }

            // Screenshots are event-first. Burst/periodic modes add visual continuity only where the profile asks.
            string? shot = null;
            if (TryGetHeartbeatScreenshotReason(now, interfacesOpened, keyframe, out var screenshotReason))
            {
                shot = Screenshot();
            }
            if (shot != null)
            {
                record["screenshot"] = shot;
                record["screenshotReason"] = screenshotReason;
            }

            WriteLine(record);
            _prev = cur;
            HeartbeatCount++;
            SampleCount++;
        }

        // ── Clicks ───────────────────────────────────────────────────────────────

        private void DrainClicks()
        {
            if (!Options.IncludeNativeClicks && !Options.IncludeManagedDoActions)
            {
                return;
            }

            // The pump records real player clicks as Surface="Native". MECAP snippets carry the structured
            // {kind,id,name,action,offset,tile} we want; non-MECAP native snippets still get logged raw.
            // Managed API records are included only for profiles that want automation diagnostics.
            IReadOnlyList<DoActionDebugSignals.DoActionSignal> signals;
            try { signals = DoActionDebugSignals.Snapshot(maxCount: 200, includeFailed: true); }
            catch { return; }

            var fresh = signals
                .Where(s =>
                    (Options.IncludeNativeClicks && string.Equals(s.Surface, "Native", StringComparison.OrdinalIgnoreCase))
                    || (Options.IncludeManagedDoActions && !string.Equals(s.Surface, "Native", StringComparison.OrdinalIgnoreCase)))
                .Where(s => s.TimestampUtc > _lastClickCursorUtc)
                .OrderBy(s => s.TimestampUtc)
                .ToList();

            if (fresh.Count == 0)
            {
                return;
            }

            foreach (var sig in fresh)
            {
                var now = DateTime.UtcNow;
                var click = new Dictionary<string, object?>();
                if (DoActionDebugSignals.TryParseCapture(sig.Snippet, out var cap))
                {
                    click["kind"] = cap.Kind;
                    if (cap.Id != 0) click["id"] = cap.Id;
                    // The MECAP capture often leaves name empty for objects; resolve it from the live scene
                    // (the thing was just clicked, so it's almost always still loaded) so the trace self-describes.
                    var name = !string.IsNullOrWhiteSpace(cap.Name) ? cap.Name : ResolveClickName(cap.Kind, cap.Id, cap.Tile);
                    if (!string.IsNullOrWhiteSpace(name)) click["name"] = name;
                    click["action"] = cap.ActionOpcode;
                    click["offset"] = cap.Offset;
                    if (cap.Item != 0) click["item"] = cap.Item;
                    if (!string.IsNullOrWhiteSpace(cap.InterfacePath)) click["if"] = cap.InterfacePath;
                    if (cap.Tile != null) click["tile"] = new[] { cap.Tile.Value.X, cap.Tile.Value.Y, cap.Tile.Value.Z };
                    if (cap.Distance != 0) click["dist"] = cap.Distance;
                }
                else
                {
                    click["kind"] = sig.Kind;
                    click["raw"] = sig.Snippet;
                    if (sig.Tile != null) click["tile"] = new[] { sig.Tile.Value.X, sig.Tile.Value.Y, sig.Tile.Value.Z };
                }

                var record = new Dictionary<string, object?>
                {
                    ["seq"] = _seq++,
                    ["tMs"] = (long)(sig.TimestampUtc - _startUtc).TotalMilliseconds,
                    ["utc"] = sig.TimestampUtc.ToString("O", CultureInfo.InvariantCulture),
                    ["reason"] = "click",
                    ["surface"] = sig.Surface,
                    ["operation"] = sig.Operation,
                    ["result"] = sig.Result,
                    ["click"] = click,
                    ["player"] = CapturePlayer(),
                };

                if (Options.IncludeClickContextFrames)
                {
                    record["context"] = BuildKeyframe(CaptureFrame());
                }

                var shot = Options.CaptureScreenshots ? Screenshot() : null;
                if (shot != null)
                {
                    record["screenshot"] = shot;
                    record["screenshotReason"] = "click";
                    ArmScreenshotBurst(DateTime.UtcNow, eventScreenshotCaptured: true);
                }

                WriteLine(record);
                ClickCount++;
                SampleCount++;
                _lastClickCursorUtc = sig.TimestampUtc;
            }
        }

        // ── State capture ──────────────────────────────────────────────────────────

        private FrameState CaptureFrame()
        {
            var f = new FrameState
            {
                Player = CapturePlayer(),
                Inv = CaptureInventory(),
                Equip = CaptureEquipment(),
                Npcs = CaptureNpcs(),
                Objs = CaptureObjects(),
                Interfaces = CaptureInterfaces(),
                InterfaceComponents = CaptureInterfaceComponents(),
                Chat = CaptureChat(),
            };
            f.Tile = SafeTile();
            f.Dg = CaptureDgSummary();
            return f;
        }

        private Dictionary<string, object?> CapturePlayer()
        {
            var t = SafeTile();
            var p = new Dictionary<string, object?>
            {
                ["tile"] = new[] { t.x, t.y, t.z },
            };
            // Baseline: only reads the proven scripts run on a background loop (safe).
            TryAdd(p, "hp", () => LocalPlayer.GetHealthPercent());
            TryAdd(p, "moving", () => LocalPlayer.IsMoving());
            TryAdd(p, "combat", () => LocalPlayer.IsInCombat());
            var targeting = false;
            TryAdd(p, "targeting", () => targeting = LocalPlayer.IsTargeting());

            if (Options.IncludePlayerExtras)
            {
                TryAdd(p, "pray", () => LocalPlayer.GetPrayerPercent());
                TryAdd(p, "adren", () => LocalPlayer.GetAdrenaline());
                TryAdd(p, "anim", () => LocalPlayer.GetAnimation());
                TryAdd(p, "run", () => LocalPlayer.IsRunEnabled());
                if (targeting)
                {
                    TryAdd(p, "targetHp", () => LocalPlayer.GetTargetHealthPercent());
                }
            }
            return p;
        }

        private Dictionary<int, long> CaptureInventory()
        {
            var map = new Dictionary<int, long>();
            if (!Options.IncludeInventory)
            {
                return map;
            }

            try
            {
                foreach (var it in Inventory.GetAll())
                {
                    if (it.Id <= 0) continue;
                    map[it.Id] = (map.TryGetValue(it.Id, out var a) ? a : 0) + (long)it.Amount;
                    _names[it.Id] = it.Name;
                }
            }
            catch { }
            return map;
        }

        private HashSet<int> CaptureEquipment()
        {
            var set = new HashSet<int>();
            if (!Options.IncludeEquipment)
            {
                return set;
            }
            try
            {
                foreach (var it in Equipment.GetAllItems())
                {
                    if (it.Id > 0) { set.Add(it.Id); _names[it.Id] = it.Name; }
                }
            }
            catch { }
            return set;
        }

        private Dictionary<int, (string name, int hp)> CaptureNpcs()
        {
            var map = new Dictionary<int, (string, int)>();
            if (!Options.IncludeNpcs)
            {
                return map;
            }

            try
            {
                var here = SafeTile();
                foreach (var n in Npcs.GetAll())
                {
                    if (Math.Abs(n.X - here.x) > Options.Radius || Math.Abs(n.Y - here.y) > Options.Radius) continue;
                    var uid = n.UniqueId != 0 ? n.UniqueId : HashCode.Combine(n.Id, n.X, n.Y);
                    map[uid] = (n.Name ?? string.Empty, n.Health);
                    if (map.Count >= Options.MaxNpcsPerFrame)
                    {
                        break;
                    }
                }
            }
            catch { }
            return map;
        }

        private Dictionary<string, ObjInfo> CaptureObjects()
        {
            var map = new Dictionary<string, ObjInfo>();
            if (!Options.IncludeObjects)
            {
                return map;
            }

            try
            {
                var here = SafeTile();
                foreach (var o in Objects.GetAll()
                             .Where(o => o.TileZ == here.z)
                             .Where(o => Math.Abs(o.TileX - here.x) <= Options.Radius && Math.Abs(o.TileY - here.y) <= Options.Radius)
                             .OrderBy(o => o.Distance)
                             .Take(Math.Max(1, Options.MaxObjectsPerFrame)))
                {
                    string? dg = null;
                    if (Options.IncludeDgSignals)
                    {
                        try { dg = Dungeoneering.ClassifyObject(o)?.ToString(); } catch { }
                    }
                    // Keep only objects that are actionable or DG-significant — drop inert scenery so deltas stay legible.
                    if (!Options.IncludeNonActionableObjects && !o.HasAction && dg == null) continue;
                    var key = $"{o.Id}:{o.TileX},{o.TileY},{o.TileZ}";
                    map[key] = new ObjInfo(o.Id, o.Name ?? string.Empty, o.Action ?? string.Empty, o.TileX, o.TileY, o.TileZ, o.Type, o.Distance, dg);
                }
            }
            catch { }
            return map;
        }

        private List<Dictionary<string, object?>> CaptureInterfaces()
        {
            var list = new List<Dictionary<string, object?>>();
            if (!Options.IncludeInterfaces)
            {
                return list;
            }
            try
            {
                foreach (var (status, name) in InterfaceStatus.GetOpenStatuses())
                {
                    list.Add(new Dictionary<string, object?> { ["status"] = status, ["name"] = name });
                }
            }
            catch { }
            return list;
        }

        private List<Dictionary<string, object?>> CaptureInterfaceComponents()
        {
            var list = new List<Dictionary<string, object?>>();
            if (!Options.IncludeInterfaceComponents)
            {
                return list;
            }

            try
            {
                foreach (var c in Interfaces.GetAll(rootId: -1, visibleOnly: true)
                             .Where(c => !string.IsNullOrWhiteSpace(c.TextItem)
                                         || !string.IsNullOrWhiteSpace(c.TextIds)
                                         || !string.IsNullOrWhiteSpace(c.Name)
                                         || c.ItemId > 0
                                         || c.Sprite > 0)
                             .Take(Math.Max(1, Options.MaxInterfaceComponents)))
                {
                    var row = new Dictionary<string, object?>
                    {
                        ["id"] = new[] { c.Id1, c.Id2, c.Id3 },
                        ["path"] = c.FullIdPath,
                        ["xywh"] = new[] { c.X, c.Y, c.Width, c.Height },
                    };
                    if (!string.IsNullOrWhiteSpace(c.Name)) row["name"] = c.Name;
                    if (!string.IsNullOrWhiteSpace(c.TextItem)) row["text"] = c.TextItem;
                    if (!string.IsNullOrWhiteSpace(c.TextIds)) row["textIds"] = c.TextIds;
                    if (c.ItemId > 0) row["item"] = c.ItemId;
                    if (c.ItemStack > 0) row["itemStack"] = c.ItemStack;
                    if (c.Sprite > 0) row["sprite"] = c.Sprite;
                    list.Add(row);
                }
            }
            catch { }

            return list;
        }

        private List<Dictionary<string, object?>> CaptureChat()
        {
            var list = new List<Dictionary<string, object?>>();
            if (!Options.IncludeChat)
            {
                return list;
            }

            try
            {
                foreach (var m in Chat.GetMessages().Take(Math.Clamp(Options.MaxChatMessages, 1, 200)))
                {
                    if (string.IsNullOrWhiteSpace(m.Text) && string.IsNullOrWhiteSpace(m.Name))
                    {
                        continue;
                    }

                    var row = new Dictionary<string, object?>
                    {
                        ["text"] = m.Text,
                        ["timestamp"] = m.Timestamp,
                        ["timeTotal"] = m.TimeTotal
                    };
                    if (!string.IsNullOrWhiteSpace(m.Name)) row["name"] = m.Name;
                    if (!string.IsNullOrWhiteSpace(m.Extra1)) row["extra1"] = m.Extra1;
                    if (!string.IsNullOrWhiteSpace(m.Extra2)) row["extra2"] = m.Extra2;
                    list.Add(row);
                }
            }
            catch { }

            return list;
        }

        private Dictionary<string, object?>? CaptureDgSummary()
        {
            if (!Options.IncludeDgSignals)
            {
                return null;
            }
            try
            {
                var sig = Dungeoneering.GetRoomSignals(maxDistance: Options.Radius, maxCount: 80, includeNpcs: true);
                if (sig == null || sig.Returned == 0) return null;
                var byKind = sig.Items
                    .GroupBy(i => i.Kind)
                    .ToDictionary(g => g.Key, g => (object?)g.Count());
                return new Dictionary<string, object?>
                {
                    ["origin"] = new[] { sig.Origin.X, sig.Origin.Y, sig.Origin.Z },
                    ["counts"] = byKind,
                };
            }
            catch { return null; }
        }

        // ── Delta ────────────────────────────────────────────────────────────────

        private Dictionary<string, object?> Diff(FrameState prev, FrameState cur)
        {
            var d = new Dictionary<string, object?>();

            if (prev.Tile != cur.Tile)
            {
                d["moved"] = new[] { cur.Tile.x, cur.Tile.y, cur.Tile.z };
            }

            var invAdded = new List<object>();
            var invChanged = new List<object>();
            foreach (var (id, amt) in cur.Inv)
            {
                if (!prev.Inv.TryGetValue(id, out var was))
                {
                    invAdded.Add(NameTriple(id, amt));
                }
                else if (was != amt)
                {
                    invChanged.Add(new Dictionary<string, object?> { ["id"] = id, ["name"] = NameOf(id), ["amt"] = amt, ["was"] = was });
                }
            }
            var invRemoved = prev.Inv.Keys.Where(id => !cur.Inv.ContainsKey(id)).Select(id => NameTriple(id, 0)).ToList();
            if (invAdded.Count > 0) d["invAdded"] = invAdded;
            if (invRemoved.Count > 0) d["invRemoved"] = invRemoved;
            if (invChanged.Count > 0) d["invChanged"] = invChanged;

            if (!prev.Equip.SetEquals(cur.Equip))
            {
                var on = cur.Equip.Except(prev.Equip).Select(id => NameTriple(id, 0)).ToList();
                var off = prev.Equip.Except(cur.Equip).Select(id => NameTriple(id, 0)).ToList();
                if (on.Count > 0) d["equipOn"] = on;
                if (off.Count > 0) d["equipOff"] = off;
            }

            var npcsIn = cur.Npcs.Where(kv => !prev.Npcs.ContainsKey(kv.Key))
                .Select(kv => (object)new Dictionary<string, object?> { ["uid"] = kv.Key, ["name"] = kv.Value.name, ["hp"] = kv.Value.hp }).ToList();
            var npcsOut = prev.Npcs.Where(kv => !cur.Npcs.ContainsKey(kv.Key))
                .Select(kv => (object)new Dictionary<string, object?> { ["uid"] = kv.Key, ["name"] = kv.Value.name }).ToList();
            var npcsHp = cur.Npcs.Where(kv => prev.Npcs.TryGetValue(kv.Key, out var p) && p.hp != kv.Value.hp)
                .Select(kv => (object)new Dictionary<string, object?> { ["uid"] = kv.Key, ["name"] = kv.Value.name, ["hp"] = kv.Value.hp }).ToList();
            if (npcsIn.Count > 0) d["npcsIn"] = npcsIn;
            if (npcsOut.Count > 0) d["npcsOut"] = npcsOut;
            if (npcsHp.Count > 0) d["npcsHp"] = npcsHp;

            var objIn = cur.Objs.Where(kv => !prev.Objs.ContainsKey(kv.Key)).Select(kv => ObjJson(kv.Value)).ToList();
            var objOut = prev.Objs.Where(kv => !cur.Objs.ContainsKey(kv.Key)).Select(kv => ObjJson(kv.Value)).ToList();
            if (objIn.Count > 0) d["objIn"] = objIn;
            if (objOut.Count > 0) d["objOut"] = objOut;

            var prevIf = prev.Interfaces.Select(StatusKey).ToHashSet();
            var curIf = cur.Interfaces.Select(StatusKey).ToHashSet();
            var ifOpen = cur.Interfaces.Where(i => !prevIf.Contains(StatusKey(i))).ToList();
            var ifClose = prev.Interfaces.Where(i => !curIf.Contains(StatusKey(i))).ToList();
            if (ifOpen.Count > 0) d["ifOpen"] = ifOpen;
            if (ifClose.Count > 0) d["ifClose"] = ifClose;

            var prevChat = prev.Chat.Select(ChatKey).ToHashSet();
            var chatNew = cur.Chat.Where(i => !prevChat.Contains(ChatKey(i))).ToList();
            if (chatNew.Count > 0) d["chatNew"] = chatNew;

            return d;
        }

        private static string StatusKey(Dictionary<string, object?> i) => Convert.ToString(i.TryGetValue("status", out var s) ? s : null) ?? string.Empty;
        private static string ChatKey(Dictionary<string, object?> i)
            => $"{Convert.ToString(i.TryGetValue("timestamp", out var ts) ? ts : null)}:{Convert.ToString(i.TryGetValue("name", out var n) ? n : null)}:{Convert.ToString(i.TryGetValue("text", out var t) ? t : null)}";

        private Dictionary<string, object?> BuildKeyframe(FrameState cur)
        {
            var frame = new Dictionary<string, object?>
            {
                ["inv"] = cur.Inv.Select(kv => NameTriple(kv.Key, kv.Value)).ToList(),
                ["equip"] = cur.Equip.Select(id => NameTriple(id, 0)).ToList(),
                ["npcs"] = cur.Npcs.Select(kv => (object)new Dictionary<string, object?> { ["uid"] = kv.Key, ["name"] = kv.Value.name, ["hp"] = kv.Value.hp }).ToList(),
                ["objs"] = cur.Objs.Values.Select(ObjJson).ToList(),
                ["interfaces"] = cur.Interfaces,
            };
            if (cur.InterfaceComponents.Count > 0)
            {
                frame["interfaceComponents"] = cur.InterfaceComponents;
            }
            if (cur.Chat.Count > 0)
            {
                frame["chat"] = cur.Chat;
            }
            if (cur.Dg != null)
            {
                frame["dg"] = cur.Dg;
            }

            return frame;
        }

        private static Dictionary<string, object?> ObjJson(ObjInfo o)
        {
            var m = new Dictionary<string, object?>
            {
                ["id"] = o.Id,
                ["name"] = o.Name,
                ["tile"] = new[] { o.X, o.Y, o.Z },
                ["type"] = o.Type,
                ["dist"] = Math.Round(o.Dist, 2),
            };
            if (!string.IsNullOrWhiteSpace(o.Action)) m["action"] = o.Action;
            if (!string.IsNullOrWhiteSpace(o.Dg)) m["dg"] = o.Dg;
            return m;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        // Resolve a clicked entity's name from the live scene (safe reads only). The clicked thing was just
        // interacted with, so it's almost always still loaded; best-effort, returns null when not found.
        private static string? ResolveClickName(string kind, int id, WorldPoint? tile)
        {
            if (id <= 0) return null;
            try
            {
                if (string.Equals(kind, "NPC", StringComparison.OrdinalIgnoreCase))
                {
                    var n = Npcs.GetAll().Where(x => x.Id == id).OrderBy(x => x.Distance).FirstOrDefault();
                    return string.IsNullOrWhiteSpace(n?.Name) ? null : n!.Name;
                }

                // Object / GroundItem / Walk-on-object all live in the object array.
                var matches = Objects.GetAll().Where(o => o.Id == id && !string.IsNullOrWhiteSpace(o.Name));
                if (tile != null)
                {
                    matches = matches.OrderBy(o => Math.Abs(o.TileX - tile.Value.X) + Math.Abs(o.TileY - tile.Value.Y));
                }
                var match = matches.FirstOrDefault();
                return match?.Name;
            }
            catch { return null; }
        }

        private readonly Dictionary<int, string> _names = new();
        private string NameOf(int id) => _names.TryGetValue(id, out var n) ? n : string.Empty;
        private Dictionary<string, object?> NameTriple(int id, long amt)
        {
            var m = new Dictionary<string, object?> { ["id"] = id, ["name"] = NameOf(id) };
            if (amt != 0) m["amt"] = amt;
            return m;
        }

        private bool IsSampleable()
        {
            try
            {
                if (!LocalPlayer.IsLoggedIn() || Game.State != GameState.InGame) return false;
                var t = SafeTile();
                return t.x != 0 || t.y != 0; // (0,0,0) == window-focus spoof / not loaded
            }
            catch { return false; }
        }

        private static (int x, int y, int z) SafeTile()
        {
            try { return LocalPlayer.GetTilePosition(); } catch { return (0, 0, 0); }
        }

        private static string SafeName()
        {
            try { var n = LocalPlayer.Name; return string.IsNullOrWhiteSpace(n) ? "player" : n; } catch { return "player"; }
        }

        private string? Screenshot()
        {
            try
            {
                var path = ScreenshotService.Capture(SessionId ?? "trace");
                if (path != null) ScreenshotCount++;
                return path;
            }
            catch { return null; }
        }

        private bool TryGetHeartbeatScreenshotReason(DateTime now, bool interfacesOpened, bool keyframe, out string reason)
        {
            reason = string.Empty;
            if (!Options.CaptureScreenshots)
            {
                return false;
            }

            if (interfacesOpened)
            {
                ArmScreenshotBurst(now, eventScreenshotCaptured: true);
                reason = "interface";
                return true;
            }

            if (Options.ScreenshotMode == TraceScreenshotMode.Periodic1s)
            {
                var interval = Math.Clamp(Options.PeriodicScreenshotMs, 250, 60_000);
                if ((now - _lastPeriodicShotUtc).TotalMilliseconds >= interval)
                {
                    _lastPeriodicShotUtc = now;
                    reason = "periodic";
                    return true;
                }
            }

            if (Options.ScreenshotMode == TraceScreenshotMode.EventPlusBurst
                && now <= _burstUntilUtc)
            {
                var interval = Math.Clamp(Options.ScreenshotBurstIntervalMs, 250, 60_000);
                if ((now - _lastBurstShotUtc).TotalMilliseconds >= interval)
                {
                    _lastBurstShotUtc = now;
                    reason = "burst";
                    return true;
                }
            }

            if ((Options.ScreenshotMode == TraceScreenshotMode.EventPlusKeyframes || Options.ScreenshotOnKeyframe)
                && keyframe)
            {
                reason = "keyframe";
                return true;
            }

            return false;
        }

        private void ArmScreenshotBurst(DateTime now, bool eventScreenshotCaptured)
        {
            if (Options.ScreenshotMode != TraceScreenshotMode.EventPlusBurst)
            {
                return;
            }

            var seconds = Math.Clamp(Options.ScreenshotBurstSeconds, 1, 300);
            var until = now.AddSeconds(seconds);
            if (until > _burstUntilUtc)
            {
                _burstUntilUtc = until;
            }

            if (eventScreenshotCaptured)
            {
                _lastBurstShotUtc = now;
            }
        }

        private static void TryAdd(Dictionary<string, object?> map, string key, Func<object?> read)
        {
            try { map[key] = read(); } catch { }
        }

        private void WriteLine(Dictionary<string, object?> record)
        {
            try
            {
                var line = JsonSerializer.Serialize(record, Json);
                lock (_sync)
                {
                    _writer?.WriteLine(line);
                }
            }
            catch (Exception ex) { Emit($"write error: {ex.Message}"); }
        }

        private void WriteMeta(string player)
        {
            try
            {
                var meta = new Dictionary<string, object?>
                {
                    ["sessionId"] = SessionId,
                    ["player"] = player,
                    ["startedUtc"] = _startUtc.ToString("O", CultureInfo.InvariantCulture),
                    ["startTile"] = (int[]?)(SafeTile() is var t ? new[] { t.x, t.y, t.z } : null),
                    ["options"] = new Dictionary<string, object?>
                    {
                        ["profileId"] = Options.ProfileId,
                        ["profileName"] = Options.ProfileName,
                        ["heartbeatMs"] = Options.HeartbeatMs,
                        ["pollMs"] = Options.PollMs,
                        ["radius"] = Options.Radius,
                        ["keyframeEveryHeartbeats"] = Options.KeyframeEveryHeartbeats,
                        ["captureScreenshots"] = Options.CaptureScreenshots,
                        ["screenshotMode"] = Options.ScreenshotMode.ToString(),
                        ["screenshotOnKeyframe"] = Options.ScreenshotOnKeyframe,
                        ["screenshotBurstSeconds"] = Options.ScreenshotBurstSeconds,
                        ["screenshotBurstIntervalMs"] = Options.ScreenshotBurstIntervalMs,
                        ["periodicScreenshotMs"] = Options.PeriodicScreenshotMs,
                        ["includeNativeClicks"] = Options.IncludeNativeClicks,
                        ["includeManagedDoActions"] = Options.IncludeManagedDoActions,
                        ["includeInventory"] = Options.IncludeInventory,
                        ["includeObjects"] = Options.IncludeObjects,
                        ["includeNpcs"] = Options.IncludeNpcs,
                        ["includeNonActionableObjects"] = Options.IncludeNonActionableObjects,
                        ["includeChat"] = Options.IncludeChat,
                        ["maxChatMessages"] = Options.MaxChatMessages,
                        ["includeClickContextFrames"] = Options.IncludeClickContextFrames,
                        ["maxObjectsPerFrame"] = Options.MaxObjectsPerFrame,
                        ["maxNpcsPerFrame"] = Options.MaxNpcsPerFrame,
                        ["includePlayerExtras"] = Options.IncludePlayerExtras,
                        ["includeEquipment"] = Options.IncludeEquipment,
                        ["includeInterfaces"] = Options.IncludeInterfaces,
                        ["includeDgSignals"] = Options.IncludeDgSignals,
                        ["includeInterfaceComponents"] = Options.IncludeInterfaceComponents,
                        ["maxInterfaceComponents"] = Options.MaxInterfaceComponents,
                    },
                    ["screenshotsDir"] = SafeCaptureDir(),
                    ["schema"] = "events.jsonl: one JSON object/line; reason=heartbeat|click; heartbeats carry player+delta(+keyframe); clicks carry the native action opcode+offset+tile.",
                };
                File.WriteAllText(Path.Combine(SessionDir!, "meta.json"), JsonSerializer.Serialize(meta, new JsonSerializerOptions(Json) { WriteIndented = true }));
            }
            catch (Exception ex) { Emit($"meta write error: {ex.Message}"); }
        }

        private void WriteSummary()
        {
            try
            {
                if (SessionDir == null) return;
                var summary = new Dictionary<string, object?>
                {
                    ["sessionId"] = SessionId,
                    ["endedUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ["durationMs"] = (long)(DateTime.UtcNow - _startUtc).TotalMilliseconds,
                    ["samples"] = SampleCount,
                    ["heartbeats"] = HeartbeatCount,
                    ["clicks"] = ClickCount,
                    ["screenshots"] = ScreenshotCount,
                    ["profileId"] = Options.ProfileId,
                    ["profileName"] = Options.ProfileName,
                    ["screenshotMode"] = Options.ScreenshotMode.ToString(),
                };
                File.WriteAllText(Path.Combine(SessionDir, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions(Json) { WriteIndented = true }));
            }
            catch { }
        }

        private string? SafeCaptureDir()
        {
            try { return ScreenshotService.CaptureDir(SessionId ?? "trace"); } catch { return null; }
        }

        private void Emit(string msg)
        {
            try { Log?.Invoke(msg); } catch { }
        }

        public static string TracesRoot()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "MESharp", "traces");
        }

        private static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return sb.ToString();
        }

        private readonly record struct ObjInfo(int Id, string Name, string Action, int X, int Y, int Z, int Type, double Dist, string? Dg);

        private sealed class FrameState
        {
            public (int x, int y, int z) Tile;
            public Dictionary<string, object?> Player = new();
            public Dictionary<int, long> Inv = new();
            public HashSet<int> Equip = new();
            public Dictionary<int, (string name, int hp)> Npcs = new();
            public Dictionary<string, ObjInfo> Objs = new();
            public List<Dictionary<string, object?>> Interfaces = new();
            public List<Dictionary<string, object?>> InterfaceComponents = new();
            public List<Dictionary<string, object?>> Chat = new();
            public Dictionary<string, object?>? Dg;
        }
    }
}
