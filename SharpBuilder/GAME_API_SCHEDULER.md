# SharpBuilder Game-API Scheduler

## Problem

SharpBuilder reaches the RS3 client through `MESharp.API` (P/Invoke into `XInput1_4.dll`) from
**two independent threads**:

1. **Executor lane** — `GraphExecutionEngine.RunAsync` walks the graph on a background task and each
   node's executor calls `MakeX`, `Inventory`, `Skills`, `Objects`, `Bank`, … directly.
2. **Dashboard lane** — a 1 s `DispatcherTimer` spins up `Task.Run` captures that read `Skills`,
   `Inventory` (item tracker), and player identity.

Those two lanes called into the client **concurrently and from arbitrary threadpool threads**. The
native side reads game memory and drives interface state that is not thread-safe, so this is a latent
correctness hazard. It also produced the originally-reported symptom — the dashboard appearing to
update only on node/transition events — because a long, blocking node (e.g. a Make-X craft) and the
timer-driven captures were competing instead of cooperating.

A first fix coalesced dropped dashboard refreshes. This document covers the structural fix: a single
serialized lane for **all** native game access.

## Design

A process-wide lane that guarantees **at most one game operation is in flight at any moment**.

```
IGameApiScheduler
  Task<IDisposable> AcquireAsync(ct)      // hold the lane across an await (whole-node execution)
  Task            RunAsync(Action, ct)    // run one sync game mutation under exclusive access
  Task<T>         RunAsync(Func<T>, ct)   // run one sync game read under exclusive access
```

* **`GameApiScheduler`** — default implementation, a single `SemaphoreSlim(1,1)`. Full mutual
  exclusion (reads and mutations share one gate) because native reads may use shared scratch state,
  so even read/read overlap is unsafe.
* **`GameApi.Scheduler`** — the process-wide singleton both lanes resolve by default. Swappable for
  tests.

### Wiring

* **Engine** (`GraphExecutionEngine`) acquires the lane around `executor.ExecuteAsync` for the whole
  node, then releases it. Short nodes hold it for a few ms; the dashboard interleaves between nodes.
* **Dashboard** (`NodeEditorViewModel.BeginDashboardGameRefresh`) runs `DashboardRefreshService.Capture`
  through `GameApi.Scheduler.RunAsync`. Captures stay coalesced (one in flight; the latest pending
  request runs when the current one finishes).
* **No synchronous UI-thread game reads.** `RefreshDashboard()` now only updates cheap in-process
  fields (clocks, current node, graph/signal summaries). Every game-backed read flows through the
  gated async capture. This also removed per-graph-edit game reads that previously fired on every
  node/parameter change.

### Session / account surfacing

`Capture` also reads the active identity under the lane and reports a `SessionLabel`:

* `Design mode — no game session` when no client is attached,
* `Not logged in` when attached but logged out,
* the player name when logged in.

Surfaced as `NodeEditorViewModel.DashboardSession` and a **Session** card on the dashboard Runtime tab.

## Long-running nodes (self-managed lane)

Holding the lane for a whole node would freeze the dashboard's *game-backed* values while a long node
runs. Any executor that loops or waits over a long horizon instead implements the marker
`IGameApiSelfManaged`: the engine does **not** wrap them, and they drive the lane themselves via the
shared `GameLane` helper —

* each discrete native op (read or action) runs through `GameLane.Run(...)`,
* every wait is a `GameLane.PollUntil(...)` poll loop (or a bare `Task.Delay`) that gates each read
  but sleeps **outside** the lane between polls.

So a long node holds the lane only for ~instant ops; the dashboard reads XP/items in the gaps and
keeps updating live. Polls honour the run cancellation token, so Stop and hot-reload unwind cleanly.
The cheap dashboard fields (graph runtime, builder uptime, current node) were always independent —
they never touch the game.

Self-managed executors today: Make-X make/craft/wait; inventory drop/eat/note, equip, unequip,
**alch-all**; bank load-preset; keyboard send; the pure wait / wait-range nodes (which touch no game
state, so they must never hold the lane during their delay); and the navigation nodes **walk** and
**teleport-lodestone**.

The navigation nodes use `Traversal`'s fire-and-forget dispatch (`WalkTo`/`Lodestone` return after
issuing the click) plus a gated arrival poll: walk re-clicks the tile when the player goes stationary
and completes on `IsWithinDistance`; lodestone settles on `!IsMoving()` + idle animation. Both poll on
the lane but sleep off it, so travel never freezes the dashboard. (This also closed a latent gap — the
old walk node returned before the player actually arrived; it now blocks until arrival or timeout.)

Plain (short) executors stay simplest: no marker, and the engine holds the lane around their whole
body.

## Files

* `SharpBuilder.Core/Services/GameApiScheduler.cs` — interface, implementation, singleton.
* `SharpBuilder.Core/Services/GraphExecutionEngine.cs` — gates the executor lane.
* `SharpBuilder.Editor.Wpf/Services/DashboardRefreshService.cs` — gated capture + session label.
* `SharpBuilder.Editor.Wpf/ViewModels/NodeEditorViewModel.Dashboard.cs` — UI-only `RefreshDashboard`,
  gated `BeginDashboardGameRefresh`.
* `SharpBuilder.Editor.Wpf/Views/NodeEditorControl.xaml` — Session card.

## Verify in game

* Run a long Make-X node: confirm no crashes/garbage reads and the dashboard catches up cleanly when
  the node finishes.
* Confirm the Session card shows the logged-in account in-client and `Design mode` in the standalone
  studio.
