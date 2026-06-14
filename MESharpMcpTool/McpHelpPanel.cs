using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;

namespace csharp_interop.McpTools
{
    /// <summary>
    /// Static help content for the MCP dashboard: how the MCP stack fits together,
    /// what each tab does, and how the knowledge system is meant to be used.
    /// </summary>
    internal static class McpHelpPanel
    {
        private static readonly Color BgCard = Color.FromRgb(28, 32, 40);
        private static readonly Color BorderCol = Color.FromRgb(52, 58, 72);
        private static readonly Color AccentBlue = Color.FromRgb(66, 165, 245);
        private static readonly Color TextDim = Color.FromRgb(148, 155, 170);
        private static readonly FontFamily Mono = new("Consolas");

        public static UIElement Build()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var right = new StackPanel();
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            // ── Left column: the MCP stack ────────────────────────────────────
            left.Children.Add(Section("How the MCP stack fits together",
                Para("MESharp.McpServer.exe is a stdio MCP server that agents (Claude Code, etc.) launch as an MCP client entry. "
                   + "For game work it connects to this process over the named pipe shown in the Overview tab and forwards commands "
                   + "to the in-process bridge (McpRuntimeService) hosted inside the .NET runtime."),
                Para("Startup is controlled by MCP_AUTOSTART in %USERPROFILE%\\MemoryError\\MMISettings.json. When enabled and "
                   + "MESharp.McpServer.exe is present next to csharp_interop.dll, the bridge starts automatically with the runtime. "
                   + "The bridge is single-instance per game process."),
                Para("Use the Overview tab's \"Copy config\" button to register the server with an MCP client; the snippet pins this "
                   + "session via the MESHARP_SESSION_PID environment variable. Without it, agents discover sessions with session.list "
                   + "and session.select.")));

            left.Children.Add(Section("Dashboard tabs",
                Bullet("Overview", "Bridge status, configuration, and a ready-to-copy MCP client config."),
                Bullet("Activity", "Live feed of bridge requests with per-command totals. Failures show in red with error codes."),
                Bullet("Tools", "Catalog of every command the in-process bridge advertises (system.get_capabilities is the agent-facing equivalent). Double-click copies a tool name."),
                Bullet("Knowledge", "Curation UI for the shared knowledge store that agents read and write through the kb.* MCP tools."),
                Bullet("Logs", "Runtime log buffer (console output), pre-filtered to MCP lines.")));

            left.Children.Add(Section("Tool safety labels",
                Bullet("safe / blocking-safe", "Read-only probes. blocking-safe may wait but never acts."),
                Bullet("mutating", "Clicks, walks, teleports, or otherwise changes game state."),
                Bullet("runtime-mutating", "Loads, reloads, pauses, or unloads scripts in the managed runtime."),
                Bullet("filesystem / filesystem-build", "Writes route/graph stores or runs dotnet build."),
                Bullet("depends-on-code / depends-on-probe", "Safety depends on the snippet or probe being executed."),
                Para("Agents are told to prefer read-only probes before mutating actions and to snapshot before/after gameplay-affecting commands.")));

            left.Children.Add(Section("Beyond the bridge",
                Para("The MCP server also exposes tools that never touch the game and therefore do not appear in the Tools tab: "
                   + "mcp.discover and mcp.get_health (bootstrap), session.* (pick a game process), script.discover_projects / "
                   + "script.open_project / script.dev_cycle_active (project iteration), observe.* and snippet.run_observed "
                   + "(before/after evidence), manuals.* (scriptmaking manuals), runtime.get_recent_traces / runtime.get_errors, "
                   + "agent.mission_* (multi-agent coordination), and bridge.call (generic pass-through)."),
                Para("Reference: docs/MCP_AGENT_DISCOVERY_WORKFLOW.md and docs/scriptmaking-manuals in the repository.")));

            // ── Right column: the knowledge system ───────────────────────────
            right.Children.Add(Section("The knowledge system",
                Para("The knowledge store holds durable findings so lessons survive across sessions and agents. Agents are expected to "
                   + "search it before probing (kb.search), ask questions they cannot answer (kb.ask), attach answers with evidence "
                   + "(kb.answer), and record what happened when knowledge was applied (kb.outcome)."),
                Para("The Knowledge tab edits the same store. Typical human jobs: answer items in \"Waiting on User\", promote validated "
                   + "answers, retire stale knowledge, and keep Question Debt short.")));

            right.Children.Add(Section("Item lifecycle",
                LegendRow(Color.FromRgb(120, 144, 156), "draft", "being written, not yet actionable"),
                LegendRow(Color.FromRgb(33, 150, 243), "open", "inquiry waiting for investigation"),
                LegendRow(Color.FromRgb(255, 235, 59), "investigating", "actively being probed"),
                LegendRow(Color.FromRgb(255, 193, 7), "partial", "partly answered"),
                LegendRow(Color.FromRgb(255, 152, 0), "waiting-user", "blocked on a human decision"),
                LegendRow(Color.FromRgb(158, 158, 158), "answered", "answer recorded, not yet validated"),
                LegendRow(Color.FromRgb(76, 175, 80), "validated", "confirmed by real outcomes"),
                LegendRow(Color.FromRgb(102, 187, 106), "promoted", "reusable knowledge (bold green in lists)"),
                LegendRow(Color.FromRgb(158, 158, 158), "stale", "probably outdated — re-verify before trusting"),
                LegendRow(Color.FromRgb(239, 83, 80), "deprecated", "known wrong, kept for history"),
                LegendRow(Color.FromRgb(84, 110, 122), "archived", "hidden from day-to-day views"),
                LegendRow(Color.FromRgb(244, 67, 54), "● BLOCKING", "any status: red dot means the item blocks progress")));

            right.Children.Add(Section("Confidence and Question Debt",
                Para("Confidence is a 0–100 trust score. kb.outcome recalculates it once an item has at least two recorded results: "
                   + "round(100 × validations / (validations + failures)), clamped to 5–99."),
                Para("Question Debt (preset and checkbox) collects what still needs attention: open or investigating inquiries and "
                   + "hypotheses, answers that have never been validated, and stale knowledge.")));

            right.Children.Add(Section("kb.* tools agents use against this store",
                Bullet("kb.create / kb.get / kb.update / kb.delete / kb.search", "CRUD and search."),
                Bullet("kb.ask", "Create an inquiry (an open question)."),
                Bullet("kb.answer", "Attach an answer to an inquiry and update its status."),
                Bullet("kb.link", "Bidirectionally link two items (fills Related Item IDs)."),
                Bullet("kb.review", "Mark reviewed; adjust status, confidence, and notes."),
                Bullet("kb.promote", "Promote an answered item to reusable knowledge."),
                Bullet("kb.outcome", "Record an application result and recalculate confidence."),
                Bullet("kb.reflect", "Batch post-task reflection: lessons, questions, and outcomes in one call."),
                Para("A second kb flavor (kb.list / kb.read / kb.write_finding) writes markdown findings to docs/agent-knowledge "
                   + "for repo-level lessons; those notes are not shown in this dashboard.")));

            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = grid
            };
            return scroller;
        }

        // ── Builders ──────────────────────────────────────────────────────────

        private static Border Section(string title, params UIElement[] children)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(AccentBlue),
                Margin = new Thickness(0, 0, 0, 8)
            });
            foreach (var child in children)
                stack.Children.Add(child);

            return new Border
            {
                Background = new SolidColorBrush(BgCard),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 12),
                Child = stack
            };
        }

        private static TextBlock Para(string text) => new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.WhiteSmoke,
            Margin = new Thickness(0, 0, 0, 8)
        };

        private static UIElement Bullet(string term, string text)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 150 });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var termText = new TextBlock
            {
                Text = term,
                FontFamily = Mono,
                FontSize = 12,
                Foreground = new SolidColorBrush(AccentBlue),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            row.Children.Add(termText);

            var bodyText = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.WhiteSmoke
            };
            Grid.SetColumn(bodyText, 1);
            row.Children.Add(bodyText);
            return row;
        }

        private static UIElement LegendRow(Color dotColor, string status, string description)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = new SolidColorBrush(dotColor),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = status,
                FontFamily = Mono,
                FontSize = 12,
                Width = 110,
                Foreground = Brushes.WhiteSmoke,
                VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = new SolidColorBrush(TextDim),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            return row;
        }
    }
}
