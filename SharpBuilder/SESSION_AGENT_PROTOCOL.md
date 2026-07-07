# SharpBuilder SessionAgent — Pipe Protocol v1

Surface: `\\.\pipe\MESharp.Builder.<pid>` — served by `SharpBuilder.SessionAgent` (a
hot-reloadable ME script) inside each injected game client. Reference implementation of the
rails in `docs/IPC_CONVENTIONS.md` (newline-delimited UTF-8 JSON, hello handshake,
multi-instance server, cross-integrity ACL). The agent registers/unregisters the surface as
`"builder"` in the session registry (`%LOCALAPPDATA%\MESharp\sessions\<pid>.json`).

One JSON object per line. Unknown `type` values are ignored (forward compatibility).

## Handshake (mandatory first exchange per connection)

```
→ {"type":"hello","version":1,"client":"Studio"}
← {"type":"hello","surface":"Builder","version":1,"pid":12345,"server":"SharpBuilder.SessionAgent/1.0"}
```

Any other message before hello → `{"type":"error"...,"code":"hello-required"}`.
`version` < 1 → `code":"version-mismatch"`.

## Requests

`{"type":"request","id":"<client-chosen>","verb":"...", ...}` →
`{"type":"response","id":"<echoed>","ok":true|false, ...}`. Failures carry `code` + `message`.

| Verb | Extra request fields | Response payload |
|---|---|---|
| `status` | — | `pid`, `graphLoaded`, `graphName`, `graphPath`, `running`, `looping`, `currentNode` (null when idle), `signals` map |
| `load` | `path` — absolute path to a saved `.builder.json` | `graphName`, `nodes`. Errors: `bad-request`, `busy` (run in progress), `load-failed` |
| `start` | `loop` (bool, default true) | `loop`, `warnings[]`. Errors: `no-graph`, `busy`, `validation-failed` (+ `errors[]`) |
| `stop` | — | `running` (state at request), `stopping`. Idempotent |
| `set-signal` | `key`, `value` (bool) | `key`, `value`. Loop runs snapshot signals at each cycle start, so this applies from the next cycle |

## Events

Sent only on connections that subscribed:

```
→ {"type":"subscribe","topics":["run"]}
← {"type":"response","id":null,"ok":true,"subscribed":["run"]}
```

Event lines: `{"type":"event","topic":"run","kind":"<kind>", ...}` with kinds:

| Kind | Fields |
|---|---|
| `graph-loaded` | `graphName`, `path` |
| `run-started` | `loop` |
| `node-entered` | `nodeId`, `title` |
| `node-completed` | `nodeId`, `title`, `status` (`Success`/`Fail`/`Retry`) |
| `transition` | `transitionId`, `label`, `toNodeId` |
| `run-stopping` | — |
| `run-completed` | — |
| `run-faulted` | `message` |

Node/transition ids are the graph's persistent GUIDs, so a client that loaded the same
`.builder.json` can resolve events against its local copy (this is how Studio animates the
canvas trail remotely).

## Semantics & limits (v1)

- One graph + one run per session agent. `load` while running is rejected — stop first.
- The engine runs a **clone** of the loaded graph; the graph on disk / in the agent is never
  mutated by a run.
- Validation runs at `start` with the game-API availability of the *session* (so graphs that
  need the game validate correctly there even if Studio is in design mode).
- Events are fire-and-forget per subscriber: a slow client drops behind; it never stalls the
  engine or other subscribers.
- Deployment: `SharpBuilder.SessionAgent.dll` in `%USERPROFILE%\MemoryError\CSharp_scripts\`.
  Repeat load = hot reload; the pipe closes and reopens.

## Always-on loading

The managed runtime auto-loads the agent at session startup
(`csharp_interop/Services/SharpBuilderAgentAutoload.cs`, called from
`InitializeManagedRuntimeServices`) under script id `SharpBuilderAgent`, so every session
serves its builder surface without manual loading. Opt out with
`"SHARPBUILDER_AGENT_AUTOSTART": false` in `%USERPROFILE%\MemoryError\MMISettings.json`
(same pattern as `MCP_AUTOSTART`). Autoload is silently skipped when the DLL isn't deployed.
A manual `LOAD` under the same id hot-reloads the agent as usual.

## Config autostart (Runner replacement)

On Initialize the agent applies the legacy runner config resolution (first hit wins):
`SHARPBUILDER_RUNNER_CONFIG` env var → `runner.<pid>.config.json` → `runner.config.json`
(both in the SharpBuilder graph folder). If a config names a valid graph, the agent loads it,
seeds `signals`, and starts it with the configured `loop` — equivalent to the old
`SharpBuilder.Runner`, except the run is now observable/controllable over the pipe.
Existing configs keep working unchanged (PascalCase keys accepted). Unlike the Runner, the
agent never scaffolds a template config: no config simply means "idle until commanded".
`SharpBuilder.Runner` is deprecated and kept one release as a fallback.
