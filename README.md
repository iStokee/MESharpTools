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

### Install without copying files

With the current MESharp client (interop product version 1.2.0 or newer and the updated Tools UI), open
**Tools & Scripts > Tools > Tool sources**, add this source, then choose **Install** for the tools
you want:

```text
https://github.com/iStokee/MESharpTools/releases
```

The public catalog contains **API Browser**, **Navigation**, and **MCP Dashboard**. New tools require
an explicit install; enable **Automatically update installed tools** for subsequent updates.
You keep the same source URL across releases. Tool authors publish the DLLs and generated manifests
as release assets; users do not need a checkout or manually supplied tool files.

Only published stable catalogs are discovered. Drafts and prereleases are deliberately excluded,
so testing installation through this source requires a normal published catalog release.

The base now ships the separate `MESharp.ToolManager.dll` module. Compatible engine updates can
be published with `-IncludeToolManager` and activate on the next game session, independently of
native/interop releases. See [the module workflow](../MESharp.ToolManager/README.md). Interop
product version 1.2.0 keeps assembly binding identity 1.1.0.0; requirements use the product version.

**Remove** offers hide/unregister, uninstall keeping data, or uninstall including data. Running tools
are closed first. Development-folder files are protected from deletion. Use **Show hidden tools**
and **Restore** to make a hidden tool available again; hidden tools are excluded from auto-updates.

In the native Tools menu, expand **Tool sources**, paste a GitHub repository or releases-page URL,
then select **Add source**. For example: `https://github.com/iStokee/MESharpTools/releases`.
MESharp queries GitHub's API and discovers tools from `tools-manifest.json` in the newest published
stable release containing that asset. Drafts and prereleases are excluded. Discovery is bounded to
1,000 releases. Adding a source discovers its catalog; **Install** is a separate user action.

Sources are persisted in `%USERPROFILE%\MemoryError\CSharp_scripts\tools\sources.json`.
The first-party repository is seeded when this file does not exist. Removing every source writes
an empty list, so it stays empty. Removing a source leaves installed tools available for local launch.

You can also add an absolute local folder path in **Tool sources**. Point it at a folder containing
`tool.json`, or at a parent whose immediate subfolders contain tools. These tools run directly from
their original folders: no copy or install is required. They are marked as development tools and
never updated by repository feeds, even if their manifests contain repository information. Removing
a folder source removes its tools from discovery without deleting files. Duplicate tool ids across
folders are reported and blocked until the conflict is removed. Missing folders are reported without
preventing other sources from loading.

Each GitHub source has an **Automatically update installed tools** checkbox (off by default).
Updates are checked on runtime startup and hourly; closed tools are updated automatically when
compatible updates are available. Running tools stay open and show **Update & reload** for an
explicit update, or update within about a minute after closing. Newly advertised tools still require
**Install**. A failed automatic update is paused for that tool/version until runtime restart or a
newer version is offered; a manual update remains available. Removing a repository clears its
automatic-update opt-in. Preferences are saved in `tools/automatic-updates.json`.

Installed tools own `tools/<id>/tool.json` and `<id>.dll`. A local-only registration still needs only:

```json
{"displayName":"My Tool","assetName":"MyTool.dll","version":"1.0.0"}
```

### Generate tool.json instead of writing it

Projects marked with `IsMESharpTool` and importing `C#/MESharpTool.targets` now generate `tool.json`
next to their output DLL on every build, including builds with `-p:SkipDeployToMe=true`. Add that
output folder as a source to test the tool directly without deployment.

For an existing DLL, run the included Windows PowerShell command:

```powershell
powershell.exe -NoProfile -File tools/New-ToolManifest.ps1 -ToolDll C:\MyTool\MyTool.dll -DisplayName "My Tool"
```

Only `-ToolDll` is required. DLL filename and version come from the assembly; the display name defaults
to the assembly name. The command creates `tool.json` beside the DLL and refuses to overwrite an
existing manifest unless `-Force` is supplied. It does not copy dependencies, embed them, or publish
a release; the DLL still needs the normal MESharp entry point and its required dependencies.

| Field | Local registration | Who supplies it |
| --- | --- | --- |
| `displayName` | Required | Project display name, command option, or assembly-name default |
| `assetName` | Required | Generated from the DLL filename |
| `version` | Required | Generated from the assembly version |
| `updateUrl` | Optional | Legacy/custom update feeds only |
| `source`, `release`, `sha256` | Optional for local registration | Generated by repository publication/installation |
| `minInteropVersion` | Required in a published schema 2 catalog | Release pipeline |

The publication command generates `tools-manifest.json` and per-tool release manifests; authors do
not hand-write those files. `tools/versions.json` remains the place to choose public tools and their
release versions.

Repository installations also record `source`, `release`, and `sha256`. These preserve ownership:
another repository cannot take over the same installed id. If multiple sources advertise the same
uninstalled id, remove the conflicting source before installing. Existing first-party manifests using
the canonical first-party `/latest/download/<id>.tool.json` URL migrate to repository ownership.
Other optional `updateUrl` feeds remain independently supported; their hashes remain optional for
backward compatibility. Catalog-managed downloads always require a SHA-256 hash.

Checks run on runtime initialization, hourly, and when **Check for updates** is selected. Publishing does not
push an immediate refresh to every running client. A failed source check preserves the cached catalog
for display, marks it unavailable for installation, and reports the error. A successful check replaces
that source's catalog, removing withdrawn entries without deleting installed tools.

Launch, Reload, Close, and Update run in the background. Conflicting actions are disabled while a
per-tool operation is active. Status snapshots are generated in the background rather than reading
manifests from the game render thread. Installed versions come from DLL metadata; mismatched local
manifest versions are reported instead of silently showing a current installation.

Install/update downloads a DLL pinned to the exact discovered release, validates hash, identity,
version, minimum interop version, and entry point, and acquires a cross-process installation lock.
DLL replacement, optional reload, metadata commit, and recovery run under the script loader gate.
Failure restores the previous DLL and manifest and attempts to restart a previously running tool.
The UI reports recovery failure separately. Cleanup failure after commit does not undo an update.
Temporary and backup names are unique; `.installation.lock` remains on disk intentionally.
Downloads have a two-minute body timeout and a 256 MiB limit. Cancellation while waiting for the
commit gate prevents replacement; once commit starts, it completes or recovers atomically.

An explicit update of a running tool restarts it. Authors must preserve unsaved state themselves and honor script
cancellation in `MESharp.ScriptEntry.Initialize()` / `Shutdown()`. Collectible assembly contexts can
remain pinned by external references, WPF caches, threads, or process-wide bindings; unload warnings
are diagnostics, not proof that every old object was collected.

## Release contract

Use one repository with complete `tools-vN` umbrella releases. Each tool has its own version; raising
only N does not create a tool update. Each release includes every catalog DLL, its `<id>.tool.json`,
and this schema 2 catalog:

```json
{
  "schema": 2,
  "release": "tools-v2",
  "tools": [{
    "id": "MyTool",
    "displayName": "My Tool",
    "assetName": "MyTool.dll",
    "version": "1.2.0",
    "sha256": "<64 hexadecimal characters>",
    "minInteropVersion": "1.1.0"
  }]
}
```

The client uses release-specific asset URLs returned by GitHub, never `/latest/` for catalog
installation. A malformed or unsupported catalog is an actionable source error. A source means
trusting that repository's published code; catalog hashes verify bytes rather than a separate signer.
Public repositories support token-free discovery. Private repository authentication is not implemented.

The new source protocol requires interop 1.1.0 and the updated native Tools UI. Schema 1 catalogs are not accepted by source discovery; existing installations remain locally launchable during the catalog transition. Roll out the updated base client before offering tools that require interop 1.1.0. Older clients can read the legacy fields but cannot enforce the new compatibility or recovery guarantees.

## Developer workflow

Mark a tool project and import the shared deployment target using the appropriate relative path:

```xml
<IsMESharpTool>true</IsMESharpTool>
<MESharpToolDisplayName>My Tool</MESharpToolDisplayName>
<!-- For a project directly under C#/MESharpTools: -->
<Import Project="..\..\MESharpTool.targets" />
```

The shared target deploys only the woven entry DLL and JSON manifest under `tools/<id>`, using the
same file lock as client installations. It does not copy `csharp_interop.dll` or loose dependencies.
Managed dependencies must be embedded; required native assets need an explicit embedded/extract-on-use
strategy. Build deployment does not reload a running tool: select **Reload** explicitly.

```powershell
dotnet build MESharpApiTool/MESharpApiTool.csproj -c Debug
# Validation/release builds must suppress deployment:
dotnet build MESharpApiTool/MESharpApiTool.csproj -c Debug -p:SkipDeployToMe=true
```

`ToolDeployRoot` redirects local tool deployment for testing. Every marked tool honors
`SkipDeployToMe`. Release tool version overrides do not propagate into shared project references.

## Publisher workflow

1. Set each changed tool's version in `tools/versions.json`. `_include` is the explicit public
   allowlist; new tools remain internal until added. `_toolMinInteropVersions` maps each tool id to
   its oldest tested compatible interop product version. Raise only the requirement of a tool that
   needs a newer API. Carry-forward preserves its published requirement; changing it requires a
   tool version bump. Legacy configurations can still use `_toolMinInteropVersion` as a fallback.
2. Stage a complete bundle. Choose a new bundle number if a staged directory already exists;
   staging never overwrites reviewed artifacts.
3. Review and test the staged bytes. Validation checks hashes, assembly identities/versions,
   compatibility metadata, duplicate ids, per-tool manifest consistency, and navigation assets.
4. Publish that exact directory without rebuilding. Publication compares against the prior catalog,
   rejecting decreased versions and changed DLLs with unchanged versions. It uploads a draft,
   downloads the draft assets, compares every uploaded hash, then publishes. Failed verification
   leaves the draft unpublished for inspection.

Run these commands from `C#/MESharpTools`:

Tools have independent versions in `tools/versions.json`. Before each release, bump only the tools
being changed. Staging compares those versions with the newest published stable catalog: new and
version-bumped tools are built, while unchanged tools reuse the exact published DLL bytes after
hash and assembly validation. Every release still contains the complete `_include` catalog.
Removing an id from `_include` withdraws it from discovery without deleting users' installations.

Shared dependency changes require version bumps for every tool you intend to rebuild with them.
The stager never silently rebuilds an unchanged version. A baseline download/API failure stops
staging rather than treating existing tools as a first release.

```powershell
# Interactive allowlist/version editor and staging:
pwsh tools/ToolRelease.ps1
# Tools-only release (default; data is generated after launch):
pwsh tools/ToolRelease.ps1 -Stage -BundleVersion 2
# Later: bump only MESharpNavTool in versions.json, then stage the next complete catalog:
pwsh tools/ToolRelease.ps1 -Stage -Tools MESharpNavTool
# Offline staging from a previously downloaded complete release:
pwsh tools/ToolRelease.ps1 -Stage -BaselineDirectory C:/release/tools-v2 -BundleVersion 3
# Or include publisher-reviewed navigation data:
pwsh tools/ToolRelease.ps1 -Stage -NavigationDataSource C:/release/navigation -BundleVersion 2
# Optional offline comparison with a downloaded previous catalog:
pwsh tools/ToolRelease.ps1 -ValidateOnly -PublishFrom artifacts/bundle/tools-v2 -PreviousManifest C:/release/previous.json
# Publish reviewed bytes; requires authenticated gh, and performs no build:
pwsh tools/ToolRelease.ps1 -Publish -PublishFrom artifacts/bundle/tools-v2
```

Use `-Repo owner/repository` for another source; generated manifests use that repository. A release
must be a complete catalog snapshot. Separate tool tags and repositories are unnecessary for
independent versioning. Build success alone is not runtime validation: test initialization, shutdown,
and recovery before publishing.

Navigation creates its local data under `%USERPROFILE%\MemoryError\CSharp_scripts\tools\MESharpNavTool\data`.
Tool DLL updates preserve this directory. Full-stack ZIPs contain no generated navigation data;
tool releases include a publisher data package only when `-NavigationDataSource` is explicitly supplied.
Existing `<interop directory>\data\navigation` and legacy LocalAppData navigation files are copied
once without overwriting existing tool data. `MESHARP_DATA_ROOT` retains its explicit override behavior.

## Verification

```powershell
dotnet test ../csharp_interop.Tests/csharp_interop.Tests.csproj -p:SkipDeployToMe=true --filter "FullyQualifiedName~Tool|FullyQualifiedName~ScriptLoaderContextTests"
powershell -NoProfile -ExecutionPolicy Bypass -File tools/tests/Test-ToolReleasePipeline.ps1
```

The publisher test mocks GitHub and performs no external publication. Recovery tests compile fixture
assemblies and exercise the real collectible script loader. Live-client UI, frame timing, and
long-running WPF unload behavior still require an injected client; use bounded, read-only probes.
