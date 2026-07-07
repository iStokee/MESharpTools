# SharpBuilder

SharpBuilder is a visual node editor for building MESharp automation graphs. It lets you create, save, validate, and run `.builder.json` scripts without writing a full C# script by hand.

SharpBuilder is a standalone tool. The main experience is the WPF editor. Graphs are saved as portable JSON files and can be run from the editor, inside the MESharp script host, or headlessly via the runner. An optional Orbit launcher plugin is also included for the day SharpBuilder is hosted inside Orbit.

## What This Repo Contains

- `SharpBuilder.Studio` - standalone desktop editor.
- `SharpBuilder.Editor.Wpf` - reusable WPF node editor control.
- `SharpBuilder.Core` - graph models, node catalog, validation, and execution engine.
- `SharpBuilder.ScriptHost` - in-game WPF host for MESharp.
- `SharpBuilder.Runner` - **deprecated**: config-driven headless runs moved into the SessionAgent (same config files, same precedence). Kept one release as a fallback.
- `SharpBuilder.SessionAgent` - headless per-session control surface: auto-loaded at session startup, serves the `MESharp.Builder.<pid>` pipe so Studio/Orbit can load, run, observe, and stop graphs remotely, and honors legacy runner autostart configs (see `SESSION_AGENT_PROTOCOL.md`).
- `SharpBuilder.OrbitPlugin` - optional Orbit launcher integration (not required to run standalone).
- `SharpBuilder.Core.Tests`, `SharpBuilder.Editor.Wpf.Tests`, and `SharpBuilder.SessionAgent.Tests` - unit tests.

## Requirements

- Windows
- .NET SDK targeting `net10.0-windows`
- Visual Studio or `dotnet` CLI
- MESharp/csharp_interop available at the expected relative path in this workspace

## Build

From this directory:

```powershell
dotnet build SharpBuilder.sln -c Debug
```

Release build:

```powershell
dotnet build SharpBuilder.sln -c Release
```

## Run the Standalone Editor

```powershell
dotnet run --project SharpBuilder.Studio -c Debug
```

The editor opens with a starter power-fishing graph. You can add nodes from the left panel, edit node settings in the right panel, and save/load graph files.

Saved graphs use this extension:

```text
.builder.json
```

By default, graph files are stored under:

```text
%USERPROFILE%\MemoryError\CSharp_scripts\SharpBuilder
```

This keeps SharpBuilder graphs near the MESharp C# script DLLs, but in a subfolder so saved graph JSON does not mix with compiled scripts. Authored graphs that ship with the repo (in `Scripts\`) are deployed into this folder and the standalone tool folder (`%USERPROFILE%\MemoryError\SharpBuilder\`) on build.

## Test

```powershell
dotnet test SharpBuilder.sln -c Debug
```

## Using Graphs In-Game

There are three in-game options:

- `SharpBuilder.ScriptHost` opens the editor UI inside the MESharp script host.
- `SharpBuilder.SessionAgent` (recommended) auto-loads at session startup, runs graphs headlessly (via legacy runner config files or remote commands), and exposes remote observe/control over `MESharp.Builder.<pid>` — Studio's session rail attaches to it.
- `SharpBuilder.Runner` (deprecated) runs a saved graph headlessly from a config file; the SessionAgent supersedes it.

The runner looks for a config file that points at a saved graph. If no config exists, it writes a template config into the SharpBuilder graph folder.

## Deployment

On build, `SharpBuilder.Studio` deploys the standalone editor to its own tool folder:

```text
%USERPROFILE%\MemoryError\SharpBuilder
```

## Optional Orbit Integration

SharpBuilder runs fully standalone and does not depend on Orbit. `SharpBuilder.OrbitPlugin` is an optional launcher plugin for the day SharpBuilder is hosted inside Orbit; its own build copies the plugin into `%USERPROFILE%\MemoryError\Orbit_Plugins\SharpBuilder.OrbitPlugin`, and it resolves the standalone editor from the tool folder above.

## Notes

- Build outputs, Visual Studio state, logs, and generated binaries are ignored by `.gitignore`.
- Keep graph files small and readable; prefer node settings over custom code when possible.
- Put low-level debugging notes and host-specific details in separate docs rather than expanding this README.
