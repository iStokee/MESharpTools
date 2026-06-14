using System.Collections.Generic;
using System.Windows;
using MahApps.Metro.IconPacks;

namespace MESharp.Views
{
    public sealed class HelpTopic
    {
        public string Title { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public bool HasCode => !string.IsNullOrWhiteSpace(Code);
    }

    public sealed class HelpSection
    {
        public string Title { get; init; } = string.Empty;
        public string Intro { get; init; } = string.Empty;
        public bool HasIntro => !string.IsNullOrWhiteSpace(Intro);
        public List<HelpTopic> Topics { get; init; } = new();
    }

    public sealed class HelpContent
    {
        public string Title { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public string FooterText { get; init; } = string.Empty;
        public PackIconMaterialKind IconKind { get; init; } = PackIconMaterialKind.HelpCircleOutline;
        public List<HelpSection> Sections { get; init; } = new();
    }

    /// <summary>
    /// Generic styled "?" modal for the Navigation hub sections. Content is a
    /// plain <see cref="HelpContent"/> object so each section can describe its
    /// own buttons/fields without a bespoke window (see <see cref="NavHelpContent"/>).
    /// </summary>
    public partial class SectionHelpWindow : Window
    {
        public SectionHelpWindow(HelpContent content)
        {
            InitializeComponent();
            DataContext = content;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }

    /// <summary>Help content for the Navigation hub sections (Map, Travel, Routes, Graph Data).</summary>
    public static class NavHelpContent
    {
        public static HelpContent Map() => new()
        {
            Title = "Coverage Map",
            Subtitle = "The map is the primary authoring surface for the webwalk graph: it shows live data (nodes, edges, routes, your player) and lets you create graph content by clicking the world directly. Edits land in the same files the Routes and Graph Data sections edit.",
            FooterText = "The same map is served at the local URL shown in the toolbar — 'Browser' opens it externally.",
            IconKind = PackIconMaterialKind.MapSearch,
            Sections = new()
            {
                new HelpSection
                {
                    Title = "Modes (buttons in the map sidebar)",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Browse", Body = "Default mode. Pan and zoom freely; click any node, edge, or route trail for a detail popup with its id, tags, and cost." },
                        new HelpTopic { Title = "Path A→B", Body = "Click two tiles. A collision-aware path is generated between them and drawn on the map. The popup offers 'Save as route' (adds it to the route catalog) and 'Travel here' (walks the live player along it)." },
                        new HelpTopic { Title = "+ Node", Body = "Click a tile to place a graph node there. Fill in name, id (blank = derived from name), and tags in the editor panel, then Save. The node is written straight into the webwalk graph." },
                        new HelpTopic { Title = "+ Edge", Body = "Click two existing nodes (from → to). Pick the route the edge executes, set cost, and optionally mark it reversible, then Save." },
                        new HelpTopic { Title = "● Record", Body = "Toggles the route recorder: walk in-game and your trail is drawn live. On stop, the trail is split into walked segments — each gets a 'save as route' popup. Teleport jumps show as dashed magenta edge candidates, and clicked obstacles (doors, stairs) are captured as transition candidates. While recording, the REC chip in the hub status strip lights up." },
                    },
                },
                new HelpSection
                {
                    Title = "Sidebar controls",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Plane", Body = "Which floor/level (z) to display; nodes and edges are filtered to it." },
                        new HelpTopic { Title = "Layers", Body = "Show/hide Nodes, Edges, Wildcard teleport targets (edges usable from anywhere, e.g. lodestones), and Route waypoint trails." },
                        new HelpTopic { Title = "File pickers", Body = "Graph JSON / Routes JSON pickers are only needed when viewing the page standalone in a browser without a session; inside this app the live data loads automatically." },
                        new HelpTopic { Title = "Legend / Stats", Body = "Color key for the layers and counts for the loaded graph." },
                    },
                },
                new HelpSection
                {
                    Title = "Live session",
                    Intro = "When a game session is injected, your player appears as a live marker and the view follows it. Authoring actions that touch the game (Travel here, Record) need that session; pure viewing and node/edge editing work without one.",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Show on Map", Body = "Buttons in the Routes and Graph Data sections jump this map to the selected route or node." },
                        new HelpTopic { Title = "Toolbar", Body = "Reload refreshes the map page so it picks up route/graph edits made in the other sections. Browser opens the same live map in your default browser (handy for a second monitor); both views talk to the same local server." },
                    },
                },
            },
        };

        public static HelpContent Routes() => new()
        {
            Title = "Routes",
            Subtitle = "Webwalking is macro navigation: it selects and executes reusable waypoint routes (teleport/walk/transition chains). It does not replace low-level movement APIs — scripts can still use Traversal directly when needed.",
            FooterText = "Save after edits so scripts consume the latest route pack. Keep route IDs stable once scripts depend on them.",
            IconKind = PackIconMaterialKind.MapSearch,
            Sections = new()
            {
                new HelpSection
                {
                    Title = "Workflow (the three panels)",
                    Intro = "Work left to right. The checklist strip at the top tracks where you are.",
                    Topics = new()
                    {
                        new HelpTopic { Title = "1 · Catalog", Body = "Pick or create a route. Search by name, category, or tags; double-click to load into the editor. New Draft starts blank, Clone copies the selection, Show on Map jumps the Map section to the route's start." },
                        new HelpTopic { Title = "2 · Route Setup", Body = "Edit route metadata and the waypoint list. Add waypoints from your current tile or the target helper, insert/reorder/remove, and mark the route enabled. For a route segment, the first waypoint is normally the first click target from the expected start node — include the start tile itself only when you want explicit preflight clarity (e.g. a bank tile for a bank-origin route)." },
                        new HelpTopic { Title = "3 · Waypoint + Run", Body = "Edit the selected waypoint (position, label, and advanced arrival/jitter/transition settings), then Save Route and run the draft or the selected catalog route. Per-waypoint results land in the execution log." },
                    },
                },
                new HelpSection
                {
                    Title = "Metadata guidance",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Name / Id", Body = "Stable script-facing identifier, e.g. core.wildy_wall.to_green_dragons. Keep it stable after scripts adopt it." },
                        new HelpTopic { Title = "Category", Body = "High-level grouping (core, slayer, quest, skilling, bank). Pick from the dropdown or type a new one." },
                        new HelpTopic { Title = "Tags", Body = "Discovery labels for scripts (dragons, bank, wilderness, …) — used by FindRoutes and webwalk tag travel." },
                        new HelpTopic { Title = "Enabled", Body = "Temporarily disable unsafe/broken routes without deleting them. Disabled routes are skipped by scripts and the planner." },
                    },
                },
                new HelpSection
                {
                    Title = "Using in scripts",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Run by id", Body = "Use route IDs where possible for deterministic behavior:", Code = "await Webwalking.RunRouteAsync(WebwalkingRoutes.CoreWildyWallToGreenDragons, ct);" },
                        new HelpTopic { Title = "Find by metadata", Body = "Or select flexibly by category/tag:", Code = "var candidates = Webwalking.FindRoutes(category: \"core\", tag: \"dragons\");" },
                        new HelpTopic { Title = "Diagnostics", Body = "For per-waypoint results, use the detailed runner:", Code = "var result = await Webwalking.RunRouteDetailedAsync(routeId, ct);" },
                    },
                },
                new HelpSection
                {
                    Title = "Best practices",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Small segments", Body = "Prefer small reusable route segments over huge monolithic routes — the webwalk graph chains them for you." },
                        new HelpTopic { Title = "Transitions", Body = "Use transition waypoints for ladders/gates/doors and keep their object IDs updated." },
                        new HelpTopic { Title = "Failing routes", Body = "When a route fails repeatedly, disable it and fix it here rather than hot-patching each script." },
                    },
                },
            },
        };

        public static HelpContent Travel() => new()
        {
            Title = "Travel",
            Subtitle = "Diagnostics surface for the movement and teleport APIs. Use it to verify a primitive works before relying on it in a route or graph edge. Reusable travel content is authored in Routes and Graph Data; everyday point-to-point travel is easiest from the Map (Path A→B → Travel).",
            FooterText = "Per-action results land in the Activity Log panel on the right.",
            IconKind = PackIconMaterialKind.CompassOutline,
            Sections = new()
            {
                new HelpSection
                {
                    Title = "Webwalk Travel (the smart one)",
                    Intro = "The full navigation stack in one box — what scripts call when they use Navigation.ResolveAndTravelAsync.",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Destination", Body = "Accepts a graph node id (bank.edgeville), a node tag (bank — nearest match wins), or a route id/name. The dropdown lists everything known; typing anything else is fine. Press Enter or click Travel." },
                        new HelpTopic { Title = "Travel / Stop", Body = "Plans a path over the webwalk graph (lodestones included), executes edge-by-edge with automatic replanning on failure, and logs per-edge results. Stop cancels at the next safe point." },
                    },
                },
                new HelpSection
                {
                    Title = "Movement probes (low level)",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Tile Movement Probe", Body = "Tests Traversal.WalkTo against one tile: enter X/Y/Z (or 'Use current tile' and nudge with the arrows), set an optional stop-short distance, timeout, and click jitter. 'Walk' dispatches and waits for arrival; 'Click only' dispatches the click without waiting." },
                        new HelpTopic { Title = "Waypoint Probe", Body = "Quick multi-tile movement test. One coordinate per line (x,y,z). 'Walk path' runs them with arrival checks; 'Click path' fires the clicks without waiting. For anything reusable, record a real route in Routes instead." },
                    },
                },
                new HelpSection
                {
                    Title = "Teleports",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Lodestone Network", Body = "Teleport via the lodestone interface. Pick a destination from the list, or type a partial name ('priff', 'edge') — name matching also reaches seasonal lodestones that aren't in the enum. 'Wait stop' blocks until the player stops moving; 'Wait arrival' polls until the player is near the expected tile." },
                        new HelpTopic { Title = "Dispatch Probes", Body = "Raw Teleports API calls: spellbook (by index or name), spirit tree, glider, fairy ring, quiver, and jewelry teleports. Useful for checking whether an action recipe works before it becomes a supported graph edge kind." },
                        new HelpTopic { Title = "Minimap (API)", Body = "Clicks minimap icons by id, optionally at specific coordinates — the primitive behind icon-based travel helpers." },
                    },
                },
            },
        };

        public static HelpContent GraphData() => new()
        {
            Title = "Graph Data",
            Subtitle = "Table/detail editor for the webwalk graph: nodes (named places), edges (how to travel between them), and the routes edges execute. The Map section offers the same authoring by clicking the world; this view is for precise field-level edits and bulk review.",
            FooterText = "All edits write to webwalk_graph.json / routes.json — the same files scripts, MCP agents and the map use.",
            IconKind = PackIconMaterialKind.Graph,
            Sections = new()
            {
                new HelpSection
                {
                    Title = "Concepts",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Node", Body = "A semantic place (bank.edgeville) with a world position and tags. Tags drive tag-based travel ('nearest bank'). Disabled nodes are ignored by the pathfinder; seeds reappear on reload, so disable rather than delete." },
                        new HelpTopic { Title = "Edge", Body = "A way to travel between two nodes. kind=route walks a recorded route; kind=lodestone teleports (From '*' = available from anywhere); kind=shortcut clicks a world object (agility shortcuts, tunnels) and should carry a skill requirement. The pathfinder uses observed cost (real measured time) over the static cost once a plan has run the edge." },
                        new HelpTopic { Title = "Reversible", Body = "Route edges only: lets the pathfinder run the route backwards (waypoints in reverse). Rejected for routes containing transition waypoints (doors/stairs can't be assumed symmetric)." },
                        new HelpTopic { Title = "Requirements", Body = "JSON list gating the edge at plan time, e.g. [{\"type\":\"skill\",\"name\":\"Agility\",\"level\":21}]. Supported types: item, skill, quest, unlock, spellbook. Profiles built from the live session filter edges automatically." },
                    },
                },
                new HelpSection
                {
                    Title = "Panels (left to right)",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Route Recorder", Body = "Walk in-game while it samples your position. 'Save Route' stores the trail as one route; 'Save Route + Edge' also wires it between the From/To nodes in the edge editor; 'Segment & Save' auto-splits a free-roam trail at teleports and dwell stops into multiple routes. Clicked doors/stairs are captured and auto-marked as transitions." },
                        new HelpTopic { Title = "Node / Edge editors", Body = "Create or update entries. Select a row in the tables and 'Load Selected' / 'Load Edge' to edit it — all fields round-trip (enabled, reversible, notes, requirements), so re-saving never strips data. 'Use Current Tile' fills coordinates from the live player." },
                        new HelpTopic { Title = "Tables", Body = "Live view of the graph. Red rows mark broken references (missing node/route or unsupported kind) with the issue named in the Issue column. 'Rev' = reversible, 'Obs' = observed cost from real traversals." },
                        new HelpTopic { Title = "Path Preview", Body = "Dijkstra between two node ids using the same planner scripts use. Preview shows the chosen edges and cost; Run executes the plan live; Stop cancels." },
                        new HelpTopic { Title = "Validation", Body = "Runs the full graph audit: broken refs, orphan/unreachable nodes, endpoint drift, divergent observed costs. The hub rail chip runs this automatically; the button forces a fresh pass with the issue list shown here." },
                    },
                },
                new HelpSection
                {
                    Title = "Workflow",
                    Intro = "Typical loop: record (here or on the Map) → save route → create nodes at its endpoints → wire a route edge → Path Preview to confirm the planner picks it up → travel it once so the edge gains an observed cost.",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Show on Map", Body = "Jumps the Map section to the selected node, switching planes if needed — fastest way to sanity-check a position." },
                        new HelpTopic { Title = "Open File", Body = "Opens webwalk_graph.json directly for hand edits; Refresh reloads it (file wins by id over compiled seeds)." },
                    },
                },
            },
        };
    }
}
