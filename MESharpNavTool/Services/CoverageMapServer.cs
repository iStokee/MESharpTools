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
            if (_listener != null)
                return $"http://127.0.0.1:{_port}/";

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _cts = new CancellationTokenSource();

            var token = _cts.Token;
            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false); }
                    catch { break; }
                    _ = Task.Run(() => HandleClient(client), token);
                }
            }, token);

            return $"http://127.0.0.1:{_port}/";
        }

        public static void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            _cts = null;

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
                    var requestBody = ReadHeadersAndBody(stream);

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

            try
            {
                var pos = Traversal.GetCurrentPosition();
                if (pos.X <= 0 && pos.Y <= 0)
                    return new { available = false, focus };
                return new { available = true, x = pos.X, y = pos.Y, z = pos.Z, focus };
            }
            catch
            {
                return new { available = false, focus };
            }
        }

        // ── Authoring + route factory API (R2-B, R7) ─────────────────────────────

        private static CancellationTokenSource? _travelCts;

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

                    case "/api/generate_route":
                    {
                        var from = new WorldPoint(GetInt(payload, "fromX"), GetInt(payload, "fromY"), GetInt(payload, "fromZ"));
                        var to = new WorldPoint(GetInt(payload, "toX"), GetInt(payload, "toY"), GetInt(payload, "toZ"));
                        var name = GetString(payload, "name") ?? $"generated {from.X},{from.Y} to {to.X},{to.Y}";
                        var save = payload.TryGetProperty("save", out var s) && s.ValueKind == JsonValueKind.True;

                        var route = CollisionPathfinder.CreateDraftRoute(name, from, to, out var error);
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

                    case "/api/travel":
                    {
                        var to = new WorldPoint(GetInt(payload, "x"), GetInt(payload, "y"), GetInt(payload, "z"));
                        _travelCts?.Cancel();
                        _travelCts = new CancellationTokenSource();
                        var ct = _travelCts.Token;
                        _ = Task.Run(() => Navigation.TravelToAsync(to, cancellationToken: ct), ct);
                        return new { succeeded = true, message = $"Travel to {to} started." };
                    }

                    case "/api/travel_stop":
                        _travelCts?.Cancel();
                        return new { succeeded = true, message = "Travel cancelled." };

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

        /// <summary>Consumes headers and returns the request body (POST) using Content-Length.</summary>
        private static string ReadHeadersAndBody(NetworkStream stream)
        {
            var contentLength = 0;
            string line;
            while ((line = ReadRequestLine(stream)).Length > 0)
            {
                var idx = line.IndexOf(':');
                if (idx > 0 && line[..idx].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(line[(idx + 1)..].Trim(), out var parsed))
                {
                    contentLength = Math.Min(parsed, 1024 * 1024);
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
