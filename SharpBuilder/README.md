# SharpBuilder

SharpBuilder is a visual node editor for building MESharp automation graphs. It lets you create, save, validate, and run `.orbitfsm.json` scripts without writing a full C# script by hand.

The main experience is the WPF editor. Graphs are saved as portable JSON files and can be run from the editor, inside the MESharp script host, or through Orbit integration.

## What This Repo Contains

- `SharpBuilder.Studio` - standalone desktop editor.
- `SharpBuilder.Editor.Wpf` - reusable WPF node editor control.
- `SharpBuilder.Core` - graph models, node catalog, validation, and execution engine.
- `SharpBuilder.ScriptHost` - in-game WPF host for MESharp.
- `SharpBuilder.Runner` - headless in-game runner for saved graphs.
- `SharpBuilder.OrbitPlugin` - Orbit launcher integration.
- `SharpBuilder.Core.Tests` and `SharpBuilder.Editor.Wpf.Tests` - unit tests.

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
.orbitfsm.json
```

By default, graph files are stored under:

```text
%USERPROFILE%\MemoryError\CSharp_scripts\SharpBuilder
```

This keeps SharpBuilder graphs near the MESharp C# script DLLs, but in a subfolder so saved graph JSON does not mix with compiled scripts.

## Test

```powershell
dotnet test SharpBuilder.sln -c Debug
```

## Using Graphs In-Game

There are two in-game options:

- `SharpBuilder.ScriptHost` opens the editor UI inside the MESharp script host.
- `SharpBuilder.Runner` runs a saved graph headlessly.

The runner looks for a config file that points at a saved graph. If no config exists, it writes a template config into the FSM scripts folder.

## Orbit Integration

`SharpBuilder.OrbitPlugin` adds launcher integration for SharpBuilder. The build output is copied into:

```text
%USERPROFILE%\MemoryError\Orbit_Plugins\SharpBuilder.OrbitPlugin
```

The standalone editor is copied there too when `SharpBuilder.Studio` builds.

## Notes

- Build outputs, Visual Studio state, logs, and generated binaries are ignored by `.gitignore`.
- Keep graph files small and readable; prefer node settings over custom code when possible.
- Put low-level debugging notes and host-specific details in separate docs rather than expanding this README.
