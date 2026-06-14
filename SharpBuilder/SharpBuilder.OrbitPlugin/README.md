# SharpBuilder.OrbitPlugin

Launcher plugin that exposes SharpBuilder (`SharpBuilder` tool key) in Orbit without embedding editor code in Orbit core.

## Deployment

1. Build this project to produce `SharpBuilder.OrbitPlugin.dll`.
2. Build `SharpBuilder.Studio` to produce `SharpBuilder.Studio.exe`.
3. Copy both into Orbit's plugin directory:
   - `%USERPROFILE%\\MemoryError\\Orbit_Plugins\\SharpBuilder.OrbitPlugin\\`
4. Start Orbit. Plugin auto-load will pick up the tool.

Optional override:
- Set `SHARPBUILDER_EDITOR_EXE` to a full path of the editor EXE.
