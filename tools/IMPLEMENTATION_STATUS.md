# Tool system implementation and validation

This tracks the end-to-end implementation plan. Completion is not claimed while the live checks
below remain unverified. No public release has been created by this work.

## Implemented and locally verified

| Plan area | Implementation | Evidence |
| --- | --- | --- |
| Source / available / installed separation | `ToolSources`, `AvailableTool`, `ToolRegistry` | `ToolCatalogTests`, `ToolSourceRefreshTests` |
| Repository and releases URL registration | Normalization, persisted source list, native Add/Remove controls | URL and empty-source persistence tests; native build |
| Complete schema 2 catalogs | Stable ids, versions, hashes, minimum interop, pinned asset URLs | Catalog validation tests and staged five-tool bundle |
| Ownership and compatibility | Local/custom ownership preserved; conflicting sources cannot install; incompatible tools disabled | Catalog refresh and recovery tests |
| Refresh authority and errors | One request stream per repository, bounded concurrency, independent custom feeds, retained stale display, removed entries replaced | Source refresh tests |
| Cross-process installation | Shared file handle lock and unique transaction paths | Lock tests and a separate deployment process that reports actual contention |
| Commit / recovery | DLL swap, runtime reload, metadata commit, and recovery under reload gate | Real compiled fixture scripts, initializer failure, manifest rename failure, restart failure, post-commit cleanup failure tests |
| Partial load cleanup | Outer failure cleanup detaches assembly resolvers and unloads contexts | Loader code and recovery fixture tests |
| Background operations | Queued launch/reload/update/close, busy and completed states | Busy/completed test; native build |
| Render-thread status | Background-generated serialized snapshots | Status-read test holds reload gate while querying snapshot |
| Installed identity | DLL metadata checked against registration | Implementation in `GetInstalledVersion`; recovery fixtures |
| Publisher | Stage once; publish reviewed directory; baseline version/hash checks; draft upload/download verification | Mocked GitHub pipeline execution test and real staging/validation |
| Local deployment | All ten marked projects import shared target; same installation lock; no interop copying | Marked-project audit, actual temporary-root MSBuild deployment, suppression test |
| Dependency version isolation | Tool version properties removed from referenced project builds | Capture tool stamped 2.3.4.0 while interop remains 1.1.0.0 |
| Documentation | Source, developer, publisher, and verification workflows | `../README.md` |

## Recorded checks

- Full interop suite: 390 passed after the offline completion audit, including cancellation at
  the commit gate, stalled-body cancellation, oversized response rejection, downgrade rejection,
  installation diagnostic recovery, strict catalog versions, and partial-load context cleanup.
- Focused tooling lifecycle/version suite: 24 passed.
- Native debug build/link passed using x64 MSBuild, `PreferredToolArchitecture=x64`, and
  `PostBuildEventUseInBuild=false`. Existing native compiler/linker warnings remain.
- All five public tools staged and validated as local test bundle `artifacts/bundle/tools-v9001`.
  This is an offline test tag, not a suggested public release number. The earlier `tools-v9000`
  directory is an incomplete failed staging attempt and must not be published.
- `tools/tests/Test-ToolReleasePipeline.ps1` passed using a mocked `gh` boundary. It performs
  no network publication and checks version enforcement, baseline lookup failure, repository
  mismatch, failed draft verification download, and corrupted-upload rejection. Publisher history
  parsing uses JSON directly rather than embedded jq quotes through Windows PowerShell.
- A standalone temporary .NET harness invoked the real loader preflight against all five DLLs
  in `tools-v9001`; every tool passed. This validates entry points and their immediate metadata
  dependencies without invoking Initialize or opening WPF windows.
- Shared deployment target exercised against a temporary directory; suppression exercised
  with a second temporary output root. The user-profile installation was not used for this test.
- Follow-up replacement regression: Windows PowerShell 5.1 converted `$null` to an empty backup
  path in `File.Replace`, breaking deployment over existing installations. Calls now use
  `[NullString]::Value`. The two deployment tests cover fresh and existing installations (including
  a BOM-bearing legacy manifest), and all seven user-reported targets were successfully deployed
  to the user's configured tool directory. This did not launch or reload running tools.

Do not run independent WPF builds against the same output directories concurrently. An initial
staging attempt overlapped the tooling test build and failed on generated XAML; the subsequent
sequential staging succeeded. The initial 32-bit native link exhausted its heap; the x64 retry passed.

## Remaining validation and audit

1. Reconnect an injected client using the rebuilt native DLL and interop 1.1.0. Discovery still
   advertises PID 30528, but its bridge times out and that process no longer exists locally.
   A fresh process is needed to replace hosted interop;
   a script reload cannot replace the default-context API assembly.
2. Exercise native source registration/removal, discovery, clean installation, operation feedback,
   failure messages, and source tooltips. Capture the rendered UI.
3. Exercise two injected clients updating the same tool, including their independent running
   versions. The shared-file exclusion is tested across processes, but two game clients are not.
4. Measure frame timing during status polling and slow shutdown/initialization. Local tests prove
   the snapshot query does not wait for the reload gate; they do not measure game frame impact.
5. Repeat WPF reloads and inspect retained contexts / stale bindings. Process-wide resolver hooks
   still emit pinning diagnostics; narrowing them requires WPF compatibility evidence. Do not
   remove these hooks solely on the basis of simple non-WPF fixtures.
6. Confirm a staged real tool's initialization/shutdown and recovery in the updated client.
7. Close the completion audit after the live evidence above is available. The offline audit covered
   release baseline/error cases, download limits/cancellation, ownership, recovery, and artifact
   validation. TOOLS_V1 preserves the first ten TOOL fields; busy/source fields are appended and
   SOURCE records are ignored by old parsers. Final native rendering still requires live evidence.

## Build locations

- Native debug output: `ME/MemoryError/x64/Build_DLL_debug/XInput1_4.dll` from repository root.
- Managed API: `C#/csharp_interop/bin/Debug/net10.0-windows/csharp_interop.dll`.
- Offline staged tool bundle: `C#/MESharpTools/artifacts/bundle/tools-v9001`.

Do not copy a second interop DLL into tool directories. Deploy the API through the existing native
host packaging path, then start a fresh injected test process. Public publication is separate from
implementation validation and has not been performed.
