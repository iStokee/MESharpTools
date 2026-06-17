# MESharpTools

Public monorepo of hot-reloadable **MESharp tools** — standalone WPF utilities loaded into the ME
runtime as collectible scripts (like SharpBuilder). Each tool is independently versioned and
**live-updatable**: publish a GitHub release here and the in-client `ToolUpdater` detects it and
hot-reloads the new DLL with no `csharp_interop.dll`/ME redeploy.

## Projects

This solution (`MESharpTools.slnx`) contains only what lives in this folder, plus the shared
`csharp_interop` API it compiles against:

| Project              | Output                          | Notes                                                        |
|----------------------|---------------------------------|--------------------------------------------------------------|
| `MESharp.Tooling`    | shared lib (Costura-embedded)   | `BaseViewModel`, `IActivatableViewModel`, `RelayCommand`.    |
| `MESharpApiTool`     | `MESharpApiTool.dll` (script)   | API browser (extracted from `csharp_interop/Documentation`). |
| `MESharpMcpTool`     | `MESharpMcpTool.dll` (script)   | MCP dashboard (extracted from `csharp_interop/McpTools`).    |
| `MESharpNavTool`     | `MESharpNavTool.dll` (script)   | Map / webwalk graph / routes (extracted from DebugUtil).     |
| `MESharpKnowledgeTool` | `MESharpKnowledgeTool.dll` (script) | Knowledge OS UI.                                    |
| `MESharpDoActionTool` | `MESharpDoActionTool.dll` (script) | DoAction / input debugging.                          |
| `MESharpCacheTool`   | `MESharpCacheTool.dll` (script) | RS3 cache browser.                                           |
| `SharpBuilder.ScriptHost` | `SharpBuilder.ScriptHost.dll` (script) | SharpBuilder node-editor host.                |
| `MESharp.Tooling.Tests` | xUnit                        | Covers `ToolVersioning` + the relocated API-browser tests.  |
| `csharp_interop` (ref) | `csharp_interop.dll`          | The shared MESharp API + the tool-update engine (`Services/Tools/`). Lives **outside** this repo. |

Tools are **auto-discovered** by the `<IsMESharpTool>true</IsMESharpTool>` marker in their `.csproj`
(see *Releasing tools*), so adding a tool requires no edit to a central list. They are tied together
at runtime by ME's native **Tools menu** + `ToolRegistry`, regardless of which folder/solution they
build from.

## Repo layout: nested in the MemoryError monorepo

This repo's root is `C#/MESharpTools/`. It is a **nested working repo inside the private
MemoryError monorepo** and is built from that full checkout — the tool projects
`ProjectReference` `..\..\csharp_interop\csharp_interop.csproj` and `MESharpNavTool` embeds
`..\..\..\tools\webwalk_map\index.html`, both of which live in the monorepo, *outside this repo root*.

**`csharp_interop` source can never be published from here:** it sits above this repo's root, so git
in `MESharpTools/` cannot track or commit it. Tools also **never ship** the interop binary — the
woven build excludes it (`FodyWeavers.xml` `ExcludeAssemblies="csharp_interop"`) and each PostBuild
copies only the tool's own DLL. Publishing a tool (source or binary) therefore exposes neither
interop's source nor its DLL; tools only consume its *public API* at compile time, and ME provides
the real `csharp_interop.dll` at runtime.

Release artifacts (the tool DLLs) are built by the maintainer from the monorepo via
`tools/ToolRelease.ps1`, which has `csharp_interop` available.

## Live-update model (umbrella release)

One **umbrella release** tagged `tools-vN` bundles every tool. It carries each tool's woven DLL plus a
single `tools-manifest.json`:

```json
{ "schema": 1, "release": "tools-v3", "generatedAtUtc": "…",
  "tools": [
    { "id": "MESharpNavTool", "displayName": "Navigation",
      "assetName": "MESharpNavTool.dll", "version": "1.2.0", "sha256": "…" },
    … ] }
```

- The in-client `ToolUpdater` (in `csharp_interop`) fetches the newest `tools-v*` release, reads the
  manifest, and per tool compares the manifest `version` to the installed DLL's `AssemblyVersion`. On
  apply it downloads only the changed tool's DLL → verifies sha256 → overwrites it in `CSharp_scripts\`
  → `ScriptLoader.ReloadScript` (hot reload, no ME restart).
- **Auto-discovery:** tools the manifest lists but the registry doesn't know are merged in
  automatically (`ToolRegistry.MergeDiscovered`), so a newly-released tool appears in the native
  **Tools** menu with no `csharp_interop` change.
- **Presence-driven UI:** a tool is shown only if it is **downloadable** (in the manifest) or
  **present on disk** in `CSharp_scripts`. Tools you have not shipped and don't have locally never
  appear (no "missing" rows). Build/drop a tool DLL into `CSharp_scripts` and re-check → it shows up.
  `ToolRegistry.KnownCatalog()` only supplies a friendly display name for recognized DLLs found on disk.
- Status surfaces as a colored dot: green = current, yellow = update available, orange/red = stale
  (major behind / pending too long) or broken.
- **Token-free** updates require this repo to be **public**. The umbrella release must be authored by
  the whitelisted account (`ToolRegistry.ExpectedAuthor`).

## Releasing tools

Tools **self-register** in their `.csproj` — no central catalog to maintain:

```xml
<IsMESharpTool>true</IsMESharpTool>
<MESharpToolDisplayName>Navigation</MESharpToolDisplayName>
```

`ToolRelease.ps1` auto-discovers every such project, builds each **active** tool at the version in
`tools/versions.json` (keyed by tool **Id == AssemblyName**, default `1.0.0`), and emits **one**
`tools-vN` release with the active DLLs + `tools-manifest.json`. N auto-increments from the newest
existing `tools-v*` tag.

```pwsh
# Interactive menu (toggle which tools ship, set versions, dry-run or publish):
pwsh tools/ToolRelease.ps1
# Build + stage the bundle of active tools (dry run - prints paste-ready release fields):
pwsh tools/ToolRelease.ps1 -Stage
# Build + publish one tools-vN release with the active tools (requires `gh auth login`):
pwsh tools/ToolRelease.ps1 -Publish
```

**Choosing what ships:** the menu lists every discovered tool with `[x]` (active) / `[ ]` (excluded);
press its number to toggle. Excluded ids are stored in `versions.json` under `_exclude` and are left
out of the build/manifest, so an unfinished tool is simply not released (and, being absent from the
manifest, never appears in-client). New tools are active by default.

**Add a new tool:** mark its `.csproj` (two lines above) and optionally add a `versions.json` entry -
it is then discovered, bundled, released, and surfaced in-client automatically.
