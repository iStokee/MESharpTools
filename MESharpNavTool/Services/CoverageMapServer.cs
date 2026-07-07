using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MESharp.API;

namespace MESharp.Services
{
    /// <summary>
    /// Tiny localhost HTTP server hosting the webwalk coverage map (tools/webwalk_map/index.html,
    /// copied to the app output) with live graph and route data. Raw TcpListener rather than
    /// HttpListener so no URL ACL / admin rights are needed. The map page fetches
    /// "webwalk_graph.json" and "routes.json" relative to itself, which this server answers
    /// from the in-memory services — seeds and core routes included, no export step needed.
    /// </summary>
    internal static class CoverageMapServer
    {
        private static readonly object ServerSync = new();
        private static TcpListener? _listener;
        private static CancellationTokenSource? _cts;
        private static int _port;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static bool IsRunning => _listener != null;

        /// <summary>True while a record-from-map session is in flight (hub status strip).</summary>
        public static bool IsRecording
        {
            get { lock (RecordSync) { return _recordCts != null; } }
        }

        /// <summary>Start (if needed) and return the map URL.</summary>
        public static string Start()
        {
            lock (ServerSync)
            {
                if (_listener != null)
                    return $"http://127.0.0.1:{_port}/";

                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                _listener = listener;
                _port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _cts = new CancellationTokenSource();

                var token = _cts.Token;
                _ = Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        TcpClient client;
                        try { client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false); }
                        catch { break; }
                        _ = Task.Run(() => HandleClient(client), token);
                    }
                }, token);

                return $"http://127.0.0.1:{_port}/";
            }
        }

        public static void Stop()
        {
            lock (ServerSync)
            {
                try { _cts?.Cancel(); } catch { }
                try { _listener?.Stop(); } catch { }
                _listener = null;
                _cts = null;
            }

            // In-flight travel started from the map must not outlive the server —
            // otherwise the character keeps walking after the tool closes, and the
            // task's closure pins this tool's collectible ALC.
            lock (TravelSync)
            {
                try { _travelCts?.Cancel(); } catch { }
            }

            // A recording session must not outlive the server (pump user balance,
            // hot-reload collectibility).
            lock (RecordSync)
            {
                if (_recordCts != null)
                {
                    try { _recordCts.Cancel(); } catch { }
                    _recordCts = null;
                    _recordSamples = null;
                    try { DoActionDebugSignals.StopNativePump(); } catch { }
                }
            }
        }

        private static void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    stream.ReadTimeout = 5000;
                    var requestLine = ReadRequestLine(stream);
                    var method = ParseMethod(requestLine);
                    var path = ParsePath(requestLine);
                    var requestBody = ReadHeadersAndBody(stream, out var host);

                    // DNS-rebinding guard: the browser always sends the Host it navigated to.
                    // Anything other than this loopback listener gets refused, so a hostile web
                    // page cannot drive the authoring/travel API through a rebound hostname.
                    if (!IsExpectedHost(host))
                    {
                        WriteResponse(stream, "403 Forbidden", "text/plain",
                            Encoding.UTF8.GetBytes("Forbidden: unexpected Host header."));
                        return;
                    }

                    byte[] body;
                    string contentType;
                    var status = "200 OK";

                    if (method == "POST")
                    {
                        body = JsonSerializer.SerializeToUtf8Bytes(HandleApiPost(path, requestBody), JsonOptions);
                        contentType = "application/json";
                        WriteResponse(stream, status, contentType, body);
                        return;
                    }

                    switch (path)
                    {
                        case "/player.json":
                            body = JsonSerializer.SerializeToUtf8Bytes(GetPlayerState(), JsonOptions);
                            contentType = "application/json";
                            break;

                        case "/record.json":
                            body = JsonSerializer.SerializeToUtf8Bytes(GetRecorderState(), JsonOptions);
                            contentType = "application/json";
                            break;

                        case "/presence.json":
                            body = JsonSerializer.SerializeToUtf8Bytes(GetPresenceState(), JsonOptions);
                            contentType = "application/json";
                            break;

                        case "/coverage.json":
                            body = JsonSerializer.SerializeToUtf8Bytes(GetCoverageState(), JsonOptions);
                            contentType = "application/json";
                            break;

                        case "/survey.json":
                            body = JsonSerializer.SerializeToUtf8Bytes(GetSurveyState(ParseQuery(requestLine)), JsonOptions);
                            contentType = "application/json";
                            break;

                        case "/":
                        case "/index.html":
                            var html = LoadIndexHtml();
                            if (html != null)
                            {
                                body = html;
                                contentType = "text/html; charset=utf-8";
                            }
                            else
                            {
                                body = Encoding.UTF8.GetBytes("Embedded webwalk map page not found.");
                                contentType = "text/plain";
                                status = "404 Not Found";
                            }
                            break;

                        case "/webwalk_graph.json":
                            body = JsonSerializer.SerializeToUtf8Bytes(WebwalkGraph.GetGraph(), JsonOptions);
                            contentType = "application/json";
                            break;

                        case "/routes.json":
                            var routes = Webwalking.GetRoutes().Select(r => new
                            {
                                id = r.Id,
                                name = r.Name,
                                category = r.Category,
                                tags = r.Tags,
                                waypoints = r.Waypoints.Select(w => new { x = w.Point.X, y = w.Point.Y, z = w.Point.Z }).ToArray()
                            }).ToArray();
                            body = JsonSerializer.SerializeToUtf8Bytes(routes, JsonOptions);
                            contentType = "application/json";
                            break;

                        default:
                            body = Encoding.UTF8.GetBytes("Not found.");
                            contentType = "text/plain";
                            status = "404 Not Found";
                            break;
                    }

                    WriteResponse(stream, status, contentType, body);
                }
            }
            catch
            {
                // Per-request failures must never take down the host app.
            }
        }

        private static void WriteResponse(NetworkStream stream, string status, string contentType, byte[] body)
        {
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n\r\n");
            stream.Write(header, 0, header.Length);
            stream.Write(body, 0, body.Length);
        }

        // ── Live player state (R2-A) ─────────────────────────────────────────────

        // ── Show-on-map focus ────────────────────────────────────────────────────
        // Table panes (Routes, Graph Data) queue a tile here; the hub flips to the
        // Map section and the page picks the focus up on its next /player.json poll.

        private static readonly object FocusSync = new();
        private static WorldPoint? _pendingFocus;

        /// <summary>Raised when a pane asks the map to focus a tile.</summary>
        public static event Action? FocusRequested;

        /// <summary>Queue a one-shot map focus; delivered with the next /player.json poll.</summary>
        public static void RequestFocus(WorldPoint point)
        {
            lock (FocusSync) { _pendingFocus = point; }
            try { FocusRequested?.Invoke(); } catch { }
        }

        private static object GetPlayerState()
        {
            object? focus = null;
            lock (FocusSync)
            {
                if (_pendingFocus is { } f)
                {
                    focus = new { x = f.X, y = f.Y, z = f.Z };
                    _pendingFocus = null;
                }
            }

            object? travel;
            lock (TravelSync)
            {
                travel = _travelStatus == null ? null : new { running = _travelRunning, message = _travelStatus };
            }

            try
            {
                var pos = Traversal.GetCurrentPosition();
                if (pos.X <= 0 && pos.Y <= 0)
                    return new { available = false, focus, travel };
                return new { available = true, x = pos.X, y = pos.Y, z = pos.Z, focus, travel };
            }
            catch
            {
                return new { available = false, focus, travel };
            }
        }

        // ── Multi-session presence + world-knowledge coverage ────────────────────

        /// <summary>All characters that have checked in recently, across every running session.</summary>
        private static object GetPresenceState()
        {
            try
            {
                var self = PresenceStore.SessionId;
                var now = DateTime.UtcNow;
                var sessions = PresenceStore.ReadAll()
                    .Where(p => p.LoggedIn)
                    .Select(p => new
                    {
                        sessionId = p.SessionId,
                        character = string.IsNullOrWhiteSpace(p.Character) ? "(unknown)" : p.Character,
                        x = p.X,
                        y = p.Y,
                        z = p.Z,
                        self = p.SessionId == self,
                        ageMs = (long)Math.Max(0, (now - p.LastSeenUtc).TotalMilliseconds)
                    })
                    .ToArray();
                return new { available = true, sessions };
            }
            catch
            {
                return new { available = false, sessions = Array.Empty<object>() };
            }
        }

        /// <summary>
        /// The survey-coverage raster within the requested tile bbox: green(clear)/red(blocked) tiles
        /// pulses have swept. Bbox-bounded + area-capped so a zoomed-out map can't request the world.
        /// </summary>
        private static object GetSurveyState(System.Collections.Generic.Dictionary<string, string> q)
        {
            try
            {
                var plane = QInt(q, "plane", 0);
                var minX = QInt(q, "minX", int.MinValue);
                var maxX = QInt(q, "maxX", int.MinValue);
                var minY = QInt(q, "minY", int.MinValue);
                var maxY = QInt(q, "maxY", int.MinValue);
                if (minX == int.MinValue || maxX == int.MinValue || minY == int.MinValue || maxY == int.MinValue)
                    return new { available = true, tiles = Array.Empty<object>(), note = "bbox required" };

                // Downsample factor: at navigation zoom the viewport spans far more than 40k tiles, so
                // the client asks for a coarser `step` (NxN blocks). We aggregate tiles into blocks so
                // the payload + render count stay bounded at any zoom — coverage stays visible when
                // zoomed out, just chunkier. step=1 is full per-tile resolution.
                var step = Math.Clamp(QInt(q, "step", 1), 1, 64);

                var area = (long)(maxX - minX + 1) * (maxY - minY + 1);
                if (area <= 0 || area > 4_000_000L)
                    return new { available = true, step, tiles = Array.Empty<object>(), note = "zoom in" };

                long blocks = ((maxX - minX) / step + 1L) * ((maxY - minY) / step + 1L);
                if (blocks > 25_000)
                    return new { available = true, step, tiles = Array.Empty<object>(), note = "zoom in" };

                var tiles = PulseStoreSurvey(plane, minX, minY, maxX, maxY, step);
                return new { available = true, step, tiles };
            }
            catch
            {
                return new { available = false, tiles = Array.Empty<object>() };
            }
        }

        private static object[] PulseStoreSurvey(int plane, int minX, int minY, int maxX, int maxY, int step)
        {
            var cells = SurveyStore.Default.TilesInBounds(plane, minX, minY, maxX, maxY);
            if (step <= 1)
                return cells.Select(c => (object)new { x = c.X, y = c.Y, s = (int)c.Status }).ToArray();

            // Aggregate into step×step blocks; a block is "blocked" (red) if it contains ANY blocked
            // tile (barriers stay visible when zoomed out), otherwise "clear" (green) if any clear tile.
            var blockStatus = new System.Collections.Generic.Dictionary<(int X, int Y), int>();
            foreach (var c in cells)
            {
                var bx = FloorDiv(c.X, step) * step;
                var by = FloorDiv(c.Y, step) * step;
                var s = (int)c.Status;
                if (!blockStatus.TryGetValue((bx, by), out var cur) || s > cur)
                    blockStatus[(bx, by)] = s; // Blocked(2) > Clear(1) > Unscanned(0)
            }
            return blockStatus.Select(kv => (object)new { x = kv.Key.X, y = kv.Key.Y, s = kv.Value }).ToArray();
        }

        private static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

        private static int QInt(System.Collections.Generic.Dictionary<string, string> q, string key, int fallback)
            => q.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;

        private static System.Collections.Generic.Dictionary<string, string> ParseQuery(string requestLine)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var parts = requestLine.Split(' ');
                if (parts.Length < 2) return dict;
                var target = parts[1];
                var q = target.IndexOf('?');
                if (q < 0) return dict;
                foreach (var kv in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = kv.IndexOf('=');
                    if (eq > 0) dict[Uri.UnescapeDataString(kv[..eq])] = Uri.UnescapeDataString(kv[(eq + 1)..]);
                }
            }
            catch { }
            return dict;
        }

        /// <summary>The observed world-knowledge layer: every obstacle prior pulses have surveyed.</summary>
        private static object GetCoverageState()
        {
            try
            {
                var now = DateTime.UtcNow;
                var obstacles = PulseStore.Default.AllKnown()
                    .Select(o => new
                    {
                        x = o.X,
                        y = o.Y,
                        z = o.Z,
                        name = string.IsNullOrWhiteSpace(o.Name) ? o.Class.ToString() : o.Name,
                        @class = o.Class.ToString(),
                        verb = o.Verb,
                        count = o.ObservationCount,
                        confidence = Math.Round(PulseConfidence.Compute(o, now), 2)
                    })
                    .ToArray();
                return new
                {
                    available = true,
                    gridsAvailable = CollisionPathfinder.IsAvailable(),
                    obstacles
                };
            }
            catch
            {
                return new { available = false, obstacles = Array.Empty<object>() };
            }
        }

        // ── Authoring + route factory API (R2-B, R7) ─────────────────────────────

        private static readonly object TravelSync = new();
        private static CancellationTokenSource? _travelCts;
        private static string? _travelStatus;
        private static bool _travelRunning;

        /// <summary>
        /// Cancel any in-flight travel and start <paramref name="run"/> on a fresh token.
        /// The task's outcome (including exceptions, which were previously unobserved and
        /// therefore invisible) is captured into the travel status served with /player.json.
        /// </summary>
        private static void StartTravelTask(string description, Func<CancellationToken, Task> run)
        {
            CancellationTokenSource myCts;
            lock (TravelSync)
            {
                try { _travelCts?.Cancel(); } catch { }
                myCts = new CancellationTokenSource();
                _travelCts = myCts;
                _travelRunning = true;
                _travelStatus = description + "…";
            }

            var ct = myCts.Token;
            _ = Task.Run(async () =>
            {
                string status;
                try
                {
                    await run(ct).ConfigureAwait(false);
                    status = description + " finished.";
                }
                catch (OperationCanceledException)
                {
                    status = description + " cancelled.";
                }
                catch (Exception ex)
                {
                    status = description + " failed: " + ex.Message;
                }

                lock (TravelSync)
                {
                    // Only report if a newer travel hasn't taken over the status line.
                    if (ReferenceEquals(_travelCts, myCts))
                    {
                        _travelStatus = status;
                        _travelRunning = false;
                    }
                }
            }, CancellationToken.None);
        }

        // Click-distance slider bounds (tiles). Default mirrors CollisionPathfinder.CreateDraftRoute's
        // own default; the max is one map square — a generous upper bound on a single reliable click.
        internal const int DefaultMaxStrideTiles = 22;
        internal const int MinMaxStrideTiles = 4;
        internal const int MaxMaxStrideTiles = 64;

        private static int ClampStride(int value) => Math.Clamp(value, MinMaxStrideTiles, MaxMaxStrideTiles);

        private static object HandleApiPost(string path, string body)
        {
            try
            {
                using var doc = string.IsNullOrWhiteSpace(body) ? JsonDocument.Parse("{}") : JsonDocument.Parse(body);
                var payload = doc.RootElement;

                switch (path)
                {
                    case "/api/node":
                    {
                        var node = new WebwalkGraphNode
                        {
                            Id = GetString(payload, "id") ?? string.Empty,
                            Name = GetString(payload, "name") ?? string.Empty,
                            X = GetInt(payload, "x"),
                            Y = GetInt(payload, "y"),
                            Z = GetInt(payload, "z"),
                            Tags = (GetString(payload, "tags") ?? string.Empty)
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .ToList()
                        };
                        if (string.IsNullOrWhiteSpace(node.Id) && !string.IsNullOrWhiteSpace(node.Name))
                            node.Id = node.Name.Trim().ToLowerInvariant().Replace(' ', '.');
                        var ok = WebwalkGraph.TrySaveNode(node, out var error, source: "human");
                        return new { succeeded = ok, error, nodeId = node.Id };
                    }

                    case "/api/edge":
                    {
                        var edge = new WebwalkGraphEdge
                        {
                            Id = GetString(payload, "id") ?? string.Empty,
                            FromNodeId = GetString(payload, "fromNodeId") ?? string.Empty,
                            ToNodeId = GetString(payload, "toNodeId") ?? string.Empty,
                            Kind = GetString(payload, "kind") ?? "route",
                            RouteId = GetString(payload, "routeId"),
                            CostMs = GetInt(payload, "costMs", 15000),
                            Reversible = payload.TryGetProperty("reversible", out var rev) && rev.ValueKind == JsonValueKind.True
                        };
                        if (string.IsNullOrWhiteSpace(edge.Id))
                            edge.Id = $"edge.{edge.FromNodeId}.to_{edge.ToNodeId}".Replace("*", "anywhere");
                        var ok = WebwalkGraph.TrySaveEdge(edge, out var error, source: "human");
                        return new { succeeded = ok, error, edgeId = edge.Id };
                    }

                    case "/api/area":
                    {
                        var area = new WebwalkArea
                        {
                            Id = GetString(payload, "id") ?? string.Empty,
                            Name = GetString(payload, "name") ?? string.Empty,
                            Z = GetInt(payload, "z"),
                            MinX = GetInt(payload, "minX"),
                            MinY = GetInt(payload, "minY"),
                            MaxX = GetInt(payload, "maxX"),
                            MaxY = GetInt(payload, "maxY"),
                            Tags = (GetString(payload, "tags") ?? string.Empty)
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .ToList()
                        };
                        if (payload.TryGetProperty("polygon", out var polyEl) && polyEl.ValueKind == JsonValueKind.Array)
                        {
                            var verts = new List<AreaVertex>();
                            foreach (var v in polyEl.EnumerateArray())
                                verts.Add(new AreaVertex(GetInt(v, "x"), GetInt(v, "y")));
                            if (verts.Count >= 3) area.Polygon = verts;
                        }
                        if (string.IsNullOrWhiteSpace(area.Id) && !string.IsNullOrWhiteSpace(area.Name))
                            area.Id = "area." + area.Name.Trim().ToLowerInvariant().Replace(' ', '.');
                        var ok = WebwalkGraph.TrySaveArea(area, out var error, source: "human");
                        return new { succeeded = ok, error, areaId = area.Id };
                    }

                    case "/api/area_delete":
                    {
                        var id = GetString(payload, "id") ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(id))
                            return new { succeeded = false, error = "id required." };
                        var deleted = WebwalkGraph.TryDeleteArea(id, out var error);
                        return new { succeeded = deleted, error, areaId = id };
                    }

                    case "/api/codegen":
                        return GenerateCsharp(payload);

                    case "/api/generate_route":
                    {
                        var from = new WorldPoint(GetInt(payload, "fromX"), GetInt(payload, "fromY"), GetInt(payload, "fromZ"));
                        var to = new WorldPoint(GetInt(payload, "toX"), GetInt(payload, "toY"), GetInt(payload, "toZ"));
                        var name = GetString(payload, "name") ?? $"generated {from.X},{from.Y} to {to.X},{to.Y}";
                        var save = payload.TryGetProperty("save", out var s) && s.ValueKind == JsonValueKind.True;
                        var maxStride = ClampStride(GetInt(payload, "maxStride", DefaultMaxStrideTiles));

                        var route = CollisionPathfinder.CreateDraftRoute(name, from, to, out var error, maxStrideTiles: maxStride);
                        if (route == null)
                            return new { succeeded = false, error, gridsAvailable = CollisionPathfinder.IsAvailable() };

                        string? saveError = null;
                        var saved = save && Webwalking.TrySaveRoute(route, out saveError);
                        return new
                        {
                            succeeded = true,
                            saved,
                            saveError,
                            routeId = route.Id,
                            name = route.Name,
                            waypoints = route.Waypoints.Select(w => new { x = w.X, y = w.Y, z = w.Z }).ToArray()
                        };
                    }

                    case "/api/plan":
                    {
                        // Multi-plane plan preview: the graph planner stitches per-plane collision
                        // walk legs to transport edges (teleport/route/shortcut/stairs), so it can
                        // cross planes where the raw collision pathfinder can't. Returns the ordered
                        // legs with geometry for the map to draw; /api/travel runs the equivalent plan.
                        var from = new WorldPoint(GetInt(payload, "fromX"), GetInt(payload, "fromY"), GetInt(payload, "fromZ"));
                        var to = new WorldPoint(GetInt(payload, "toX"), GetInt(payload, "toY"), GetInt(payload, "toZ"));
                        // Live profile so the preview reflects what THIS account can actually use —
                        // a charged glory, the standard spellbook, agility shortcuts at level, etc.
                        // Degrades to empty (lodestones + walking) when no game session is attached.
                        var plan = WebwalkGraph.FindPath(from, to, WebwalkProfile.FromGameState());
                        if (plan == null)
                            return new
                            {
                                succeeded = false,
                                error = $"No plan from {from} to {to} — no route/transport connects them.",
                                gridsAvailable = CollisionPathfinder.IsAvailable()
                            };
                        return new { succeeded = true, totalCostMs = plan.TotalCostMs, legs = BuildPlanLegs(from, to, plan) };
                    }

                    case "/api/travel":
                    {
                        var to = new WorldPoint(GetInt(payload, "x"), GetInt(payload, "y"), GetInt(payload, "z"));
                        // Same live profile as /api/plan so the run uses the previewed teleports
                        // (and skips any the account can't currently afford, e.g. a depleted glory).
                        var travelProfile = WebwalkProfile.FromGameState();
                        StartTravelTask($"Travel to {to}", ct => Navigation.TravelToAsync(to, travelProfile, ct));
                        return new { succeeded = true, message = $"Travel to {to} started." };
                    }

                    case "/api/travel_area":
                    {
                        var areaId = GetString(payload, "areaId") ?? GetString(payload, "id") ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(areaId))
                            return new { succeeded = false, message = "areaId required." };
                        var areaProfile = WebwalkProfile.FromGameState();
                        StartTravelTask($"Travel into area '{areaId}'", ct => Navigation.TravelToAreaAsync(areaId, areaProfile, ct));
                        return new { succeeded = true, message = $"Travel into area '{areaId}' started." };
                    }

                    case "/api/travel_stop":
                        lock (TravelSync) { try { _travelCts?.Cancel(); } catch { } }
                        return new { succeeded = true, message = "Travel cancelled." };

                    case "/api/walk_route":
                    {
                        // Walk an explicit waypoint list — what the collision preview shows IS what runs
                        // (no graph re-plan / teleport detour). Same-plane; runs on the shared travel CTS
                        // so /api/travel_stop cancels it.
                        if (!payload.TryGetProperty("waypoints", out var wpEl) || wpEl.ValueKind != JsonValueKind.Array)
                            return new { succeeded = false, error = "waypoints[] required." };
                        var pts = new List<WorldPoint>();
                        foreach (var w in wpEl.EnumerateArray())
                            pts.Add(new WorldPoint(GetInt(w, "x"), GetInt(w, "y"), GetInt(w, "z")));
                        if (pts.Count == 0)
                            return new { succeeded = false, error = "waypoints[] empty." };

                        StartTravelTask($"Walk of {pts.Count} previewed waypoint(s)", ct => Traversal.WalkPathAsync(
                            pts, waypointDistance: 2, timeoutMs: Math.Max(30000, pts.Count * 12000), cancellationToken: ct));
                        return new { succeeded = true, message = $"Walking {pts.Count} previewed waypoint(s)." };
                    }

                    case "/api/scan":
                    {
                        // Ad-hoc scan: pulse the player's current spot (or an explicit tile) and fold
                        // it into the world model, even while standing still.
                        WorldPoint center;
                        if (payload.TryGetProperty("x", out _))
                            center = new WorldPoint(GetInt(payload, "x"), GetInt(payload, "y"), GetInt(payload, "z"));
                        else
                            center = Traversal.GetCurrentPosition();
                        if (center.X <= 0 && center.Y <= 0)
                            return new { succeeded = false, error = "No player position to scan (not logged in?)." };
                        var snap = PulseSnapshot.ScanAndRecord(center, surveyRadius: GetInt(payload, "radius", 14));
                        return new
                        {
                            succeeded = true,
                            center = new { x = center.X, y = center.Y, z = center.Z },
                            obstacles = snap.Obstacles.Count
                        };
                    }

                    case "/api/scan_mode":
                    {
                        // Toggle continuous "scanning mode" (capture-as-you-move). Defaults to ON.
                        PulseStore.CaptureEnabled = !(payload.TryGetProperty("on", out var on) && on.ValueKind == JsonValueKind.False);
                        return new { succeeded = true, scanning = PulseStore.CaptureEnabled };
                    }

                    // ── Record-from-map (R2-C) ───────────────────────────────────
                    case "/api/record_start":
                        return StartRecording();

                    case "/api/record_stop":
                        return StopRecording();

                    case "/api/record_save":
                        return SavePendingSegment(GetInt(payload, "index", -1), GetString(payload, "name"));

                    case "/api/record_discard":
                        lock (RecordSync) { _pendingSegments = null; _pendingObstacles = null; }
                        return new { succeeded = true, message = "Pending segments discarded." };

                    default:
                        return new { succeeded = false, error = $"Unknown API endpoint '{path}'." };
                }
            }
            catch (Exception ex)
            {
                return new { succeeded = false, error = ex.Message };
            }
        }

        /// <summary>
        /// Turn a graph plan into ordered, drawable legs. Each leg carries its kind, resolved
        /// from/to tiles, a planeChange flag, and (for walk/route legs) the waypoint polyline —
        /// walk legs are traced through real collision tiles, route legs use the route's own
        /// waypoints (sliced/reversed exactly as the plan will run them). Teleport/transport legs
        /// have no polyline; the map draws them as a labelled jump / plane-change marker.
        /// </summary>
        private static object[] BuildPlanLegs(WorldPoint from, WorldPoint to, WebwalkGraphPath path)
        {
            var nodes = WebwalkGraph.GetGraph().Nodes
                .GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            WorldPoint? NodePos(string id) => nodes.TryGetValue(id, out var n) ? n.Position : null;

            var legs = new List<object>();
            var cursor = from;
            foreach (var e in path.Edges)
            {
                var kind = string.IsNullOrWhiteSpace(e.Kind) ? "route" : e.Kind.ToLowerInvariant();
                var fromPos = cursor;
                WorldPoint toPos;
                List<WorldPoint>? waypoints = null;

                if (kind == "route" && !string.IsNullOrWhiteSpace(e.RouteId) &&
                    Webwalking.TryGetRoute(e.RouteId!, out var route) && route.Waypoints.Count > 0)
                {
                    var wp = route.Waypoints.Select(w => w.Point).ToList();
                    if (e.IsTrailSubRange)
                    {
                        var s = Math.Clamp(e.SubRangeStart!.Value, 0, wp.Count - 1);
                        var en = Math.Clamp(e.SubRangeEnd!.Value, 0, wp.Count - 1);
                        if (s <= en) wp = wp.GetRange(s, en - s + 1);
                    }
                    if (e.IsReversedTraversal) wp.Reverse();
                    waypoints = wp;
                    fromPos = wp[0];
                    toPos = wp[^1];
                }
                else
                {
                    toPos = string.Equals(e.ToNodeId, WebwalkGraph.VirtualDestNodeId, StringComparison.Ordinal)
                        ? to
                        : NodePos(e.ToNodeId) ?? e.SyntheticWalkTarget ?? to;

                    // Same-plane walk legs: trace the actual collision tiles so the line hugs geometry.
                    if (kind == "walk" && fromPos.Z == toPos.Z)
                    {
                        var r = CollisionPathfinder.FindPath(fromPos, toPos, maxExpansions: 60_000);
                        if (r.Succeeded && r.Tiles.Count > 0) waypoints = r.Tiles.ToList();
                    }
                }

                legs.Add(new
                {
                    kind,
                    routeId = e.RouteId,
                    costMs = e.EffectiveCostMs,
                    planeChange = fromPos.Z != toPos.Z,
                    from = new { x = fromPos.X, y = fromPos.Y, z = fromPos.Z },
                    to = new { x = toPos.X, y = toPos.Y, z = toPos.Z },
                    waypoints = waypoints?.Select(w => new { x = w.X, y = w.Y, z = w.Z }).ToArray()
                });
                cursor = toPos;
            }
            return legs.ToArray();
        }

        // ── Record-from-map (R2-C) ───────────────────────────────────────────────
        // The map drives the recorder: start, free-roam, stop → auto-segmented
        // (teleports + dwell stops) pending segments render on the map for accept
        // (save as route) or discard. Same WebwalkAuthoring pipeline as the WPF
        // Segment & Save, minus the form UI.

        private static readonly object RecordSync = new();
        private static List<WebwalkRecordedSample>? _recordSamples;
        private static CancellationTokenSource? _recordCts;
        private static IReadOnlyList<WebwalkTrailSegment>? _pendingSegments;
        private static IReadOnlyList<WebwalkAuthoring.WebwalkObstacleCandidate>? _pendingObstacles;
        private static DateTime _recordStartUtc;

        private const int RecordSampleIntervalMs = 600;

        private static object StartRecording()
        {
            lock (RecordSync)
            {
                if (_recordCts != null)
                    return new { succeeded = false, error = "Already recording." };

                _recordSamples = new List<WebwalkRecordedSample>();
                _pendingSegments = null;
                _pendingObstacles = null;
                _recordStartUtc = DateTime.UtcNow;
                // Click-awareness: doors/shortcuts/stairs clicked while recording
                // surface as obstacle candidates (native DoAction hook via the pump).
                DoActionDebugSignals.StartNativePump();
                _recordCts = new CancellationTokenSource();
                var ct = _recordCts.Token;

                _ = Task.Run(async () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            var pos = Traversal.GetCurrentPosition();
                            if (pos.X > 0 || pos.Y > 0)
                            {
                                lock (RecordSync)
                                    _recordSamples?.Add(new WebwalkRecordedSample { Position = pos });
                            }
                        }
                        catch { /* no session this tick — keep sampling */ }

                        try { await Task.Delay(RecordSampleIntervalMs, ct).ConfigureAwait(false); }
                        catch { break; }
                    }
                }, ct);

                return new { succeeded = true, message = "Recording started — walk the path, then stop." };
            }
        }

        private static object StopRecording()
        {
            List<WebwalkRecordedSample> samples;
            lock (RecordSync)
            {
                if (_recordCts == null)
                    return new { succeeded = false, error = "Not recording." };
                _recordCts.Cancel();
                _recordCts = null;
                samples = _recordSamples ?? new List<WebwalkRecordedSample>();
                _recordSamples = null;
            }

            IReadOnlyList<WebwalkAuthoring.WebwalkObstacleCandidate> obstacles;
            try
            {
                var signals = DoActionDebugSignals.Snapshot(500);
                obstacles = WebwalkAuthoring.CollectObstacleCandidates(signals, _recordStartUtc, DateTime.UtcNow);
            }
            catch
            {
                obstacles = Array.Empty<WebwalkAuthoring.WebwalkObstacleCandidate>();
            }
            finally
            {
                DoActionDebugSignals.StopNativePump();
            }

            var segments = WebwalkAuthoring.SegmentSamples(samples);
            lock (RecordSync)
            {
                _pendingSegments = segments;
                _pendingObstacles = obstacles;
            }

            return new
            {
                succeeded = true,
                sampleCount = samples.Count,
                segmentCount = segments.Count,
                obstacleCount = obstacles.Count,
                message = segments.Count == 0
                    ? "No segments produced (trail too short?)."
                    : $"{segments.Count} segment(s) ready — accept or discard them on the map." +
                      (obstacles.Count > 0 ? $" {obstacles.Count} object click(s) captured as obstacle candidates." : "")
            };
        }

        private static object GetRecorderState()
        {
            List<WebwalkRecordedSample>? live;
            IReadOnlyList<WebwalkTrailSegment>? pending;
            bool recording;
            lock (RecordSync)
            {
                recording = _recordCts != null;
                live = _recordSamples == null ? null : new List<WebwalkRecordedSample>(_recordSamples);
                pending = _pendingSegments;
            }

            IReadOnlyList<WebwalkAuthoring.WebwalkObstacleCandidate>? obstacles;
            lock (RecordSync)
                obstacles = _pendingObstacles;

            return new
            {
                recording,
                sampleCount = live?.Count ?? 0,
                trail = Decimate(live?.Select(s => s.Position).ToList(), 400),
                obstacles = obstacles?.Select(o => new
                {
                    x = o.Tile.X, y = o.Tile.Y, z = o.Tile.Z,
                    objectId = o.ObjectId,
                    actionOp = o.ActionOp
                }).ToArray(),
                pending = pending?.Select((s, i) => new
                {
                    index = i,
                    kind = s.Kind == WebwalkSegmentKind.Teleport ? "teleport" : "walk",
                    start = new { x = s.Start.X, y = s.Start.Y, z = s.Start.Z },
                    end = new { x = s.End.X, y = s.End.Y, z = s.End.Z },
                    distanceTiles = s.DistanceTiles,
                    durationMs = s.DurationMs,
                    endedAtDwell = s.EndedAtDwell,
                    points = Decimate(s.Samples.Select(p => p.Position).ToList(), 200)
                }).ToArray()
            };
        }

        private static object SavePendingSegment(int index, string? name)
        {
            IReadOnlyList<WebwalkTrailSegment>? pending;
            lock (RecordSync)
                pending = _pendingSegments;

            if (pending == null || index < 0 || index >= pending.Count)
                return new { succeeded = false, error = $"No pending segment with index {index}." };

            var segment = pending[index];
            if (segment.Kind == WebwalkSegmentKind.Teleport)
                return new { succeeded = false, error = "Teleport segments are edge candidates, not routes — author the teleport edge instead." };

            if (string.IsNullOrWhiteSpace(name))
                name = $"recorded {segment.Start.X},{segment.Start.Y} to {segment.End.X},{segment.End.Y}";

            var route = WebwalkAuthoring.CreateRouteFromSegment(name, segment, new WebwalkRecordingOptions());

            IReadOnlyList<WebwalkAuthoring.WebwalkObstacleCandidate>? obstacles;
            lock (RecordSync)
                obstacles = _pendingObstacles;
            var transitions = obstacles == null ? 0 : WebwalkAuthoring.ApplyTransitions(route, obstacles);

            var saved = Webwalking.TrySaveRoute(route, out var error);
            return new { succeeded = saved, error, routeId = route.Id, name = route.Name, transitionsMarked = transitions };
        }

        private static object[]? Decimate(IReadOnlyList<WorldPoint>? points, int maxPoints)
        {
            if (points == null) return null;
            var step = points.Count <= maxPoints ? 1 : (int)Math.Ceiling(points.Count / (double)maxPoints);
            var output = new List<object>(Math.Min(points.Count, maxPoints) + 1);
            for (var k = 0; k < points.Count; k += step)
                output.Add(new { x = points[k].X, y = points[k].Y, z = points[k].Z });
            if (points.Count > 0 && (points.Count - 1) % step != 0)
                output.Add(new { x = points[^1].X, y = points[^1].Y, z = points[^1].Z });
            return output.ToArray();
        }

        private static string? GetString(JsonElement payload, string name) =>
            payload.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

        private static int GetInt(JsonElement payload, string name, int fallback = 0) =>
            payload.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : fallback;

        // ── Map → C# code generation (Explv-style "copy as code") ────────────────────
        // Turns a map selection into paste-ready MESharp C#. kind:
        //   "travel"  {x,y,z}                     → Navigation.TravelToAsync(...)
        //   "path"    {waypoints:[{x,y,z}], name} → WorldPoint[] + Traversal.WalkPathAsync + route author
        //   "area"    {minX..maxY|polygon, name}  → WebwalkArea def + TrySaveArea + TravelToArea
        private static object GenerateCsharp(JsonElement payload)
        {
            var kind = (GetString(payload, "kind") ?? string.Empty).Trim().ToLowerInvariant();
            var name = GetString(payload, "name");
            var snippets = new List<object>();
            void Add(string label, string code) => snippets.Add(new { label, language = "csharp", code });

            static string Id(string? n, string prefix) =>
                string.IsNullOrWhiteSpace(n) ? prefix + "1" : prefix + n!.Trim().ToLowerInvariant().Replace(' ', '.');

            switch (kind)
            {
                case "travel":
                {
                    var x = GetInt(payload, "x"); var y = GetInt(payload, "y"); var z = GetInt(payload, "z");
                    Add("Travel to tile (planner: teleports + walk)",
                        $"await Navigation.TravelToAsync(new WorldPoint({x}, {y}, {z}), WebwalkProfile.FromGameState());");
                    Add("Walk only (no teleports)",
                        $"await Traversal.SmartWalkToAsync(new WorldPoint({x}, {y}, {z}), arrivalDistance: 3);");
                    break;
                }
                case "path":
                {
                    if (!payload.TryGetProperty("waypoints", out var wp) || wp.ValueKind != JsonValueKind.Array)
                        return new { succeeded = false, error = "path codegen needs waypoints[]." };
                    var pts = wp.EnumerateArray()
                        .Select(w => new WorldPoint(GetInt(w, "x"), GetInt(w, "y"), GetInt(w, "z"))).ToList();
                    if (pts.Count == 0) return new { succeeded = false, error = "waypoints[] empty." };

                    var arr = string.Join(",\n    ", pts.Select(p => $"new WorldPoint({p.X}, {p.Y}, {p.Z})"));
                    Add("Walk an explicit waypoint list",
                        $"var waypoints = new[]\n{{\n    {arr}\n}};\nawait Traversal.WalkPathAsync(waypoints, waypointDistance: 2);");

                    var routeId = Id(name, "route.");
                    var wpAuthor = string.Join(",\n        ",
                        pts.Select(p => $"new WebwalkingWaypoint {{ Point = new WorldPoint({p.X}, {p.Y}, {p.Z}) }}"));
                    Add("Author as a reusable route",
                        $"var route = new WebwalkingRoute\n{{\n    Id = \"{routeId}\",\n    Name = \"{name ?? routeId}\",\n    Waypoints = new List<WebwalkingWaypoint>\n    {{\n        {wpAuthor}\n    }}\n}};\nWebwalking.TrySaveRoute(route, out _);");
                    break;
                }
                case "area":
                {
                    var areaId = Id(name, "area.");
                    var hasPoly = payload.TryGetProperty("polygon", out var polyEl) && polyEl.ValueKind == JsonValueKind.Array
                                  && polyEl.GetArrayLength() >= 3;
                    string body;
                    if (hasPoly)
                    {
                        var verts = string.Join(",\n        ",
                            polyEl.EnumerateArray().Select(v => $"new AreaVertex({GetInt(v, "x")}, {GetInt(v, "y")})"));
                        body = $"    Z = {GetInt(payload, "z")},\n    Polygon = new()\n    {{\n        {verts}\n    }}";
                    }
                    else
                    {
                        body = $"    Z = {GetInt(payload, "z")},\n    MinX = {GetInt(payload, "minX")}, MinY = {GetInt(payload, "minY")},\n    MaxX = {GetInt(payload, "maxX")}, MaxY = {GetInt(payload, "maxY")}";
                    }
                    Add("Define + save the area",
                        $"var area = new WebwalkArea\n{{\n    Id = \"{areaId}\",\n    Name = \"{name ?? areaId}\",\n{body}\n}};\nWebwalkGraph.TrySaveArea(area, out _, \"human\");");
                    Add("Travel into the area / membership check",
                        $"await Navigation.TravelToAreaAsync(\"{areaId}\");\nbool inside = WebwalkGraph.FindAreasContaining(LocalPlayer.GetTileWorldPoint()).Any(a => a.Id == \"{areaId}\");");
                    break;
                }
                default:
                    return new { succeeded = false, error = $"Unknown codegen kind '{kind}'. Use travel|path|area." };
            }

            return new { succeeded = true, kind, snippets = snippets.ToArray() };
        }

        private static bool IsExpectedHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;
            var expectedPort = _port;
            return string.Equals(host, $"127.0.0.1:{expectedPort}", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, $"localhost:{expectedPort}", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Consumes headers and returns the request body (POST) using Content-Length.</summary>
        private static string ReadHeadersAndBody(NetworkStream stream, out string? host)
        {
            host = null;
            var contentLength = 0;
            string line;
            while ((line = ReadRequestLine(stream)).Length > 0)
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                var name = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(value, out var parsed))
                {
                    contentLength = Math.Min(parsed, 1024 * 1024);
                }
                else if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    host = value;
                }
            }

            if (contentLength <= 0)
                return string.Empty;

            var buffer = new byte[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var n = stream.Read(buffer, read, contentLength - read);
                if (n <= 0) break;
                read += n;
            }
            return Encoding.UTF8.GetString(buffer, 0, read);
        }

        private static string ParseMethod(string requestLine)
        {
            var idx = requestLine.IndexOf(' ');
            return idx > 0 ? requestLine[..idx].ToUpperInvariant() : "GET";
        }

        private static byte[]? LoadIndexHtml()
        {
            // Dev override: a copy under %USERPROFILE%\MemoryError\webwalk_map wins over the embedded page.
            try
            {
                var overridePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "MemoryError", "webwalk_map", "index.html");
                if (File.Exists(overridePath))
                    return File.ReadAllBytes(overridePath);
            }
            catch { }

            try
            {
                using var resource = typeof(CoverageMapServer).Assembly.GetManifestResourceStream("MESharp.webwalk_map.index.html");
                if (resource == null) return null;
                using var ms = new MemoryStream();
                resource.CopyTo(ms);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static string ReadRequestLine(NetworkStream stream)
        {
            var sb = new StringBuilder(128);
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                if (b == '\n') break;
                if (b != '\r') sb.Append((char)b);
                if (sb.Length > 2048) break;
            }
            return sb.ToString();
        }

        private static string ParsePath(string requestLine)
        {
            // "GET /path HTTP/1.1"
            var parts = requestLine.Split(' ');
            if (parts.Length < 2) return "/";
            var path = parts[1];
            var query = path.IndexOf('?');
            return query >= 0 ? path[..query] : path;
        }
    }
}
