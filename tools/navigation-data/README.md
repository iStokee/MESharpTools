# Navigation data release source

Place publisher-reviewed navigation data here before staging an umbrella tool release:

- `collision/index.json` and the `collision/*_*.json` shards
- `collision/components/index.json` and component shards (recommended)
- `webwalk_graph.json`, containing publisher-owned topology only
- `obstacles.json` (optional)

`ToolRelease.ps1` packages only those selected files into `mesharp-navigation-data.zip`. It does
not read `%LocalAppData%\MESharp`, which prevents personal graph edits and runtime observations from
being published accidentally.
