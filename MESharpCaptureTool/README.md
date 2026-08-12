# MESharp Activity Capture

Activity Capture records a human-played or script-driven activity into one self-contained pack under
`%LOCALAPPDATA%\MESharp\activity_captures`. It is the public script-development evidence surface; it does
not export or depend on Knowledge OS.

The initial `dg-rush` profile records Low- or High-complexity floors from spawn through confirmed completion,
whether the operator plays manually or SharpDungeoneering drives the floor. A pack contains:

- `manifest.json` — activity, source, privacy, outcome, and counts
- `events.jsonl` — append-only keyframes, deltas, actions, logs, and script telemetry
- `frames/` — adaptively captured, privacy-masked screenshots linked to their timeline events
- `review.json` — non-destructive notes, excluded ranges, and corrected outcome
- `analysis/dg-rush-report.{json,md}` — deterministic phases, interactions, blockers, and capture gaps

JSON identities are redacted and chat is masked in screenshots by default; use Privacy preview to verify any
additional interface-root masks needed for the current client layout. Start the tool before playing manually,
or select `Script` and provide the loaded script id. SharpDungeoneering
publishes room profiles, planner decisions, objective, door, boss, blocker, gatestone, and completion events
whenever a capture is active. Native actions are enriched with the resolved object/NPC identity and menu action
while retaining a differing raw capture id for diagnosis. The same workflow is
available through the `capture.*` MCP tools.

The state sampler explicitly reads interface roots `942` and `945`; a rootless interface scan only returns
their containers. This retains machine-readable dungeon minimap cells/markers and Current keys children in
addition to the visual screenshot.

The desktop tool is organized by workflow:

- **Capture Library** filters and inspects durable packs, opens/copies their locations, and provides
  confirmed whole-pack deletion.
- **Timeline & Review** keeps events, screenshots, and timestamps together. Notes and excluded ranges
  can be added, edited, or removed without changing `events.jsonl`; corrected outcomes are stored in
  `review.json`.
- **Analysis Report** regenerates, copies, and opens the deterministic report output.
