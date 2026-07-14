# MESharp Foundry

Foundry is Atom's privileged teaching and pipeline-coordination UI. It remains separate from the
production Atom runtime.

## Capture workspace

The **Capture** tab configures stable target classes, verifies live projections, records synchronized
frames and truth, marks complete cycles, and records explicit absent-scene intervals. Recent sessions
show capture coverage and can be selected later in the Pipeline workspace.

## Pipeline workspace

The **Pipeline** tab coordinates the external Atom Lab toolchain through `wsl.exe`. Python, Torch,
and CUDA run in WSL and never load into the injected game process.

The development workflow is deliberately gated:

1. select a repository task, multi-cycle positive session, and validation-negative session;
2. validate both recordings and export the cycle-separated dataset;
3. generate and complete the in-Foundry training/validation visual-truth review (test labels stay hidden);
4. apply the review, train the SSDLite detector, and evaluate validation;
5. after validation passes, select fresh positive and negative-only recordings;
6. prepare and run the one-time acceptance test without retraining.

Foundry checks actual per-class projected/negative counts before assigning session roles. Training
streams epoch progress, supports cancellation, and clears stale metrics when a component is replaced.
Validation and acceptance metrics include ONNX artifact and dataset hashes; stale results cannot unlock the
acceptance stage. A completed acceptance receipt prevents repeating the gate for the same artifact.

Tasks are discovered from the Atom repository's `tasks/*.task.json` files. The selected task owns
class identity, label geometry, recipe parameters, review scope, and acceptance policy; the UI no
longer contains bank/portable branches or duplicates those settings.

Pipeline settings are user-local in `%LOCALAPPDATA%\Atom\Foundry\foundry_tool.json`. The repository,
WSL distribution, and Python executable remain machine-specific. `ATOM_REPOSITORY` and
`ATOM_WSL_DISTRIBUTION` may supply defaults.
