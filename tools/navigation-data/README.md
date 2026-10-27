# Navigation data release source

Place publisher-reviewed navigation data here before staging an umbrella tool release:

- `collision/index.json` and the `collision/*_*.json` shards
- `collision/components/index.json` and component shards (recommended)
- `webwalk_graph.json`, containing publisher-owned topology only
- `routes.json`, containing publisher-owned named routes in the existing stored-route format (optional when no edges reference them)
- `obstacles.json` (optional)

`ToolRelease.ps1` packages only those selected files into `mesharp-navigation-data.zip`. It does
not read `%LocalAppData%\MESharp`, which prevents personal graph edits and runtime observations from
being published accidentally.

Navigation data is required by default. `-AllowMissingNavigationData` is an explicit developer-only escape hatch.
Staging invokes `MESharp.NavigationDataTool --validate` before creating the archive, using the same
validator as installation. Missing nodes/routes, duplicate route identities, unsupported executors,
and corrupt collision shards stop the release.

## Owned cache reader

MESharp has its own read-only NXT reader, object clipping and collision/component builder.
NavTool builds missing terrain automatically; **Status > Build Local Navigation** runs it
manually. No external extractor is needed. Generated walkability goes into the API's existing
`data/navigation/workspace/derived` generation store beside the graph/route/observation tree.
See [implementation and validation notes](../../../../docs/navigation-owned-cache-pipeline.md).

For a standalone build, set `MESHARP_DATA_ROOT` to the deployed ME `data` directory, then run:

```powershell
dotnet run --project 'C#\MESharp.NavigationDataTool' -p:SkipDeployToMe=true -- --build --cache 'C:\ProgramData\Jagex\RuneScape'
```

Terrain and primary placement records can also be captured separately with `--decode-cache`.

```powershell
dotnet run --project 'C#\MESharp.NavigationDataTool' -p:SkipDeployToMe=true -- --inspect-cache 'C:\ProgramData\Jagex\RuneScape' --index 5 --report "$env:TEMP\mesharp-navigation-cache-audit.json"
```

## Existing external terrain import

The terrain reader contract is [mejrs/rs3cache](https://github.com/mejrs/rs3cache):
`rs3.exe --input <cache> --output <source> --dump tiles_each locations_each location_configs`.
It is an external dependency; no compatible executable or Rust toolchain is bundled here.

Provision `rs3.exe` once at `<ME install>/tools/rs3.exe`, on PATH, or via `MESHARP_RS3CACHE_TOOL`.
NavTool automatically starts a local build when terrain is absent and this reader is available.
Developer tools → Status → **Build Local Navigation** runs the same pipeline explicitly.

The pipeline extracts into a fresh `workspace/sources/rs3dump/extractions/<id>` directory, then builds
`workspace/derived/generations/<id>` containing collision, components, obstacles, and ladder candidates.
Only a complete build receives `build.json` and an atomic `workspace/derived/active.json` pointer.
Failed or cancelled builds never replace the previous generation. Published navigation takes precedence
over local workspace terrain. Candidate ladders are not silently enabled as verified transport links.

The offline CLI supports cache/build path overrides and preparing a clean publisher source:

```powershell
dotnet run --project 'C#\MESharp.NavigationDataTool' -p:SkipDeployToMe=true -- --build --extractor 'C:\tools\rs3.exe'
dotnet run --project 'C#\MESharp.NavigationDataTool' -p:SkipDeployToMe=true -- --build --publish-source 'I:\staging\navigation' --version 1.0.0
dotnet run --project 'C#\MESharp.NavigationDataTool' -p:SkipDeployToMe=true -- --validate 'I:\staging\navigation' --version 1.0.0
```

`--publish-source` requires an empty destination. It copies generated terrain/obstacles and exports built-in
seed topology, never the live combined graph, personal routes, capabilities, or runtime observations.
This creates the lodestone/walking foundation; inferred entrances and new transport actions still need review.
