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
            Title = "Live map",
            Subtitle = "Search for a location or preview a journey on the map. Travel uses the attached character's current capabilities. Turn on Authoring mode in the left rail for recording and editing tools.",
            FooterText = "Journey status and Stop are in the top toolbar. Browser opens the same map with its own bottom journey bar.",
            IconKind = PackIconMaterialKind.MapSearch,
            Sections = new()
            {
                new HelpSection
                {
                    Title = "Modes (buttons in the map sidebar)",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Browse", Body = "Default mode. Pan and zoom, search for locations, and click visible locations, areas, connections, or route trails for details. Browsing does not start a journey." },
                        new HelpTopic { Title = "Go here", Body = "Choose Go here, then click a destination. This previews a journey from the player's current position; it does not move the character yet. Use Travel in the preview popup to start. The plan can include supported teleports and walking, filtered by the current character's requirements." },
                        new HelpTopic { Title = "Path A→B", Body = "Click a start tile and then a destination to preview a graph journey, which may include teleports and walking. Travel executes toward that destination from the live player position. If graph planning fails but a local collision path is available, the walking preview offers Travel for those exact waypoints; Save as route is available in Authoring mode." },
                    },
                },
                new HelpSection
                {
                    Title = "Sidebar controls",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Find me / Plane", Body = "The map centers on the player when it first receives a position. Find me recenters the view afterward; it is not a continuous follow switch. Plane chooses the floor to display and the target floor for map clicks; it does not move the character." },
                        new HelpTopic { Title = "Map layers", Body = "Locations, areas, and surveyed terrain start enabled. Connections, teleport connections, recorded trails, presence, and surveyed obstacles start disabled. Authoring mode exposes the technical connection and trail layers. Presence is shared between local sessions." },
                        new HelpTopic { Title = "Surveyed terrain", Body = "Green and red show current cache-derived terrain at previously surveyed tiles, not independent live verification. A zoomed-out red block can contain clear tiles. Missing survey color does not by itself mean a tile is blocked." },
                        new HelpTopic { Title = "Legend / Stats", Body = "The collapsed Legend and Stats panels in the sidebar explain overlay colors and summarize the loaded graph. Open their headings to inspect them." },
                    },
                },
                new HelpSection
                {
                    Title = "Journey toolbar and data",
                    Intro = "Travel and recording require an injected, logged-in game session. A preview is a plan, not confirmation that the journey has been executed successfully.",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Status / Stop", Body = "The top toolbar shows Ready, Journey in progress, or Last journey. Stop appears only while a journey is active and remains available when switching sections. It requests cancellation; it cannot undo an action already sent to the game. Completed or failed results remain visible after Stop disappears." },
                        new HelpTopic { Title = "Preparing navigation data", Body = "A missing or outdated local dataset may need a full cache build before travel starts. The journey status reports build progress while the request waits. Closing the tool cancels its startup build. A build with different collision options, such as Wilderness ditch crossability, may need rebuilding for live navigation." },
                        new HelpTopic { Title = "Reload / Browser", Body = "Reload refreshes the map page; it does not cancel the active journey or rebuild terrain. Browser opens the same local map and shares its journey and authoring state. The browser page has its own bottom status bar because the tool's top toolbar is not present there." },
                        new HelpTopic { Title = "Show on map", Body = "Actions in the route and location editors focus the selected route or location on this map. Browsing another section does not stop a journey." },
                    },
                },
                new HelpSection
                {
                    Title = "Authoring mode",
                    Intro = "Enable Authoring mode in the left rail to expose these controls. Creating a location or connection does not verify that it can be traversed.",
                    Topics = new()
                    {
                        new HelpTopic { Title = "+ Node / + Edge / + Area", Body = "Place a location by clicking a tile, connect two existing locations with a saved route, or click opposite corners of a rectangular area. Save the editor to persist the definition." },
                        new HelpTopic { Title = "Record route", Body = "Records your live trail. Stop recording with the same button, then review and save the resulting walked segments. Teleport jumps and obstacle clicks are candidates for connection authoring, not proof of a complete executable network. The REC indicator shows an active recorder." },
                        new HelpTopic { Title = "Source files / Scanning", Body = "Hosted views load navigation data automatically. Graph and Routes JSON pickers support manual viewing inputs. Scan here captures nearby observations; Scanning mode controls passive capture as you move. Neither control generates collision terrain." },
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
                    Title = "Workflow (the four tabs)",
                    Intro = "Start with Saved Routes or Record, then review edits and test. The checklist strip at the top tracks where you are.",
                    Topics = new()
                    {
                        new HelpTopic { Title = "Saved Routes", Body = "Pick or create a route. Search by name, category, or tags; double-click to load into the editor. New Draft starts blank, Clone copies the selection, Show on Map jumps to the route's start." },
                        new HelpTopic { Title = "Record", Body = "Open the map, start Record route, walk in-game, and stop to review and save the captured segments." },
                        new HelpTopic { Title = "Review / Edit", Body = "Edit route metadata and its waypoint list. Add, reorder, or remove waypoints and mark the route enabled." },
                        new HelpTopic { Title = "Test", Body = "Edit the selected waypoint, save the route, then run the draft or selected catalog route. Per-waypoint results appear in the execution log." },
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
            Subtitle = "Choose a known destination and travel. Turn on Diagnostics to inspect the movement and teleport probes used for development.",
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
