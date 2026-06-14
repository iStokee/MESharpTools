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
| `MESharp.Tooling.Tests` | xUnit                        | Covers `ToolVersioning` + the relocated API-browser tests.  |
| `csharp_interop` (ref) | `csharp_interop.dll`          | The shared MESharp API + the tool-update engine (`Services/Tools/`). Lives **outside** this repo. |

**SharpBuilder** keeps its own `SharpBuilder.sln` in `C#/SharpBuilder/`. All four tools are tied
together at runtime by ME's native **Tools menu** + `ToolRegistry`, regardless of which
folder/solution they build from.

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

## Live-update model (mirrors Orbit)

- Each tool is registered in `%USERPROFILE%\MemoryError\tools.json` with its GitHub `repo`,
  `tagPrefix`, `assetName`, and `dllName` (built-ins seeded automatically by `ToolRegistry`).
- The in-client `ToolUpdater` (in `csharp_interop`) polls
  `https://api.github.com/repos/<repo>/releases`, filters by `tagPrefix`, compares the release
  version to the installed DLL's `AssemblyVersion`, and on apply: downloads the DLL → verifies
  sha256 (from the sidecar manifest) → overwrites it in `CSharp_scripts\` → `ScriptLoader.ReloadScript`.
- Status surfaces in ME's native **Tools** menu as a colored dot: green = current, yellow = update
  available, orange/red = stale (major behind / pending too long) or broken.
- **Token-free** updates require this repo to be **public** (GitHub Releases API serves public-repo
  assets without auth). Releases must be authored by the whitelisted account (`ToolRegistry.ExpectedAuthor`).

## Releasing a tool

Per-tool independent versioning via tag-prefixed releases (`navtool-v1.2.0`, `sharpbuilder-v2.1.0`):

```pwsh
# Stage only (dry run):
pwsh tools/ToolRelease.ps1 -Tool navtool -Version 1.2.0
# Build + publish the GitHub release (requires `gh auth login`):
pwsh tools/ToolRelease.ps1 -Tool navtool -Version 1.2.0 -Publish
```

The script stamps `AssemblyVersion=<Version>.0`, builds Release, computes sha256, writes
`<PackId>.manifest.json`, and (with `-Publish`) creates the tagged release with the DLL + manifest
as assets.
