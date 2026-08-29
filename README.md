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
| `MESharpSlayerKnowledgeTool` | `MESharpSlayerKnowledgeTool.dll` (script) | Slayer assignment/master/location/mechanic curation and knowledge-pack publishing. |
| `MESharpDoActionTool` | `MESharpDoActionTool.dll` (script) | DoAction / input debugging.                          |
| `MESharpCacheTool`   | `MESharpCacheTool.dll` (script) | RS3 cache browser.                                           |
| `SharpBuilder.ScriptHost` | `SharpBuilder.ScriptHost.dll` (script) | SharpBuilder node-editor host.                |
| `MESharp.Tooling.Tests` | xUnit                        | Covers `ToolVersioning` + the relocated API-browser tests.  |
| `csharp_interop` (ref) | `csharp_interop.dll`          | The shared MESharp API + the tool-update engine (`Services/Tools/`). Lives **outside** this repo. |

Navigation's Wiki import enumerates the complete `Category:Advanced maps`, follows nested `Map:`
transclusions, and writes a revisioned semantic snapshot under `%LOCALAPPDATA%/MESharp/Navigation/wiki`.
World-coordinate geometry may be published to the graph; inferred transitions stay disabled and
local-coordinate maps remain calibration candidates until cache or live evidence verifies them.
Matching monster features are published as candidate Slayer locations for review in Slayer Knowledge.

NPC-area derivation combines Chisel NPC definitions with any available legacy spawn dump,
world-coordinate Advanced-map features, and shared live NPC sightings. Navigation exposes separate
steps to refresh definitions and capture visible NPCs. A region remains disabled and tagged
`candidate` with one coordinate source; it becomes enabled and `corroborated` only when two
independent sources agree on the cluster.

Tools are discovered by the `<IsMESharpTool>true</IsMESharpTool>` marker in their `.csproj`, then
explicitly allowed into public releases through `tools/versions.json` (see *Releasing tools*). They are tied together
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

## Registration and live updates

Every installed tool owns a directory under
`%USERPROFILE%\MemoryError\CSharp_scripts\tools\<tool-id>\`. Registration requires only a
`tool.json` and its declared DLL:

```json
{
  "displayName": "Navigation",
  "assetName": "MESharpNavTool.dll",
  "version": "1.2.0",
  "updateUrl": "https://github.com/iStokee/MESharpTools/releases/latest/download/MESharpNavTool.tool.json"
}
```

- `displayName`, `assetName`, and `version` are required. The tool id is the DLL filename stem.
- `updateUrl` is optional. It points to a remote manifest with the same shape; the remote DLL is
  resolved relative to that URL. `sha256` is optional update-integrity metadata.
- Checks are automatic. Installation always requires the user to select **Update**.
- Updates validate the hash when supplied and the downloaded assembly id, retain a rollback copy,
  update `tool.json` atomically, and hot-reload a running tool.
- Tool directories may contain additional assets and dependencies. Registration and updating concern
  the entry DLL; authors distribute the complete directory when other files are required.
- Packaged MESharp builds also include `CSharp_scripts\tools\tools-manifest.json`, an inventory of
  bundled first-party tools and their SHA-256 hashes. Local registration still comes from each
  directory's `tool.json`; users never edit the root inventory to add a tool.
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

`ToolRelease.ps1` auto-discovers every such project, builds each **public** tool at the version in
`tools/versions.json` (keyed by tool **Id == AssemblyName**, default `1.0.0`), and emits **one**
`tools-vN` release with the public DLLs, one `<id>.tool.json` per tool, and the umbrella manifest
used for first-party release metadata. N auto-increments from the newest existing `tools-v*` tag.

```pwsh
# Interactive menu (toggle which tools ship, set versions, dry-run or publish):
pwsh tools/ToolRelease.ps1
# Build + stage the bundle of public tools (dry run - prints paste-ready release fields):
pwsh tools/ToolRelease.ps1 -Stage
# Build + publish one tools-vN release with the public tools (requires `gh auth login`):
pwsh tools/ToolRelease.ps1 -Publish
```

**Choosing what ships:** the menu lists every discovered tool with `[x]` (public) / `[ ]` (internal);
press its number to toggle. Public ids are stored in `versions.json` under `_include`; everything
else is left out of the build/manifest and never appears in-client. New tools are internal by default.

**Add a new tool:** mark its `.csproj`, add a version, and deliberately add its AssemblyName to
`versions.json` `_include` when it is ready for public release.
