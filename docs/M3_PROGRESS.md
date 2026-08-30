# M3 progress — playable Godot micro-deck slice

Status: starting vertical slice (not an M3 gate claim)

Date: 30 August 2026

Rules/data baseline: Core 26.03 / System Gateway fixture `gateway-m2-3`

## Architecture record

The M2 scenario document and deterministic setup now live in the
Godot-independent `Netrunner.Match` project. CLI proof code references that
project; the Godot project references `Netrunner.Match` and never references
`Netrunner.Cli`. The checked-in `data/m2/representative-run.json` is embedded
by the Match project and can also be loaded from an explicit path, so editor,
packaged, and headless runs do not depend on the process working directory.

`MatchCoordinator` owns one asynchronous worker and is the only presentation
entry point that calls `GameEngine.Apply`. It starts the representative run
through `GameEngine.BeginRunWithEvents`, publishes immutable event batches and
Runner-filtered snapshots, and awaits the Corp `IGameAgent` without blocking
the caller. Human submissions carry the issued decision ID, revision, and
Action ID. Rejections publish feedback while retaining the same filtered
observation; authoritative state hashes are not exposed to presentation code.
An injectable delay hook covers the ten-second-agent-latency proof without
slowing ordinary tests.

`ObservationViewMapper` maps only `Observation` to typed presentation models:
players/resources, hidden-zone counts and owner-visible cards, ordered server
ICE/root, rig, scoring, decision prompt/owner, run focus, encounter
subroutines, access state, legal actions, and outcome. `ObservationBuilder`
attaches viewer-owned prompt metadata as a non-serialized observation property,
preserving the existing Core observation JSON contract without exposing an
opponent prompt or revision.

The Godot shell is container-based and uses a reusable text-card Control with
hover/selection/details feedback, status/counter labels, click-first legal
actions, event animation queue controls, reduced motion, and focused
encounter/access/paid-window text. Worker publications cross a thread-safe
queue so a fast opponent cannot overwrite event batches between rendered
frames. Dynamic board, action, and details content use bounded scroll
viewports, so event history or a larger legal-action set cannot push the
action panel below the window. It is deliberately text-only and uses labels
and borders in addition to colour.

## Evidence

- `tests/Netrunner.Match.Tests/MatchCoordinatorTests.cs` covers embedded
  scenario/start events, hidden-info-safe mapping, stale rejection/state
  preservation, an injected ten-second agent delay, and completion of the
  representative run while retaining every published event batch.
- Existing Core observation goldens and CLI M2 proof tests remain green after
  rehoming the scenario/setup code.
- Godot project C# build succeeds. A headless M3 smoke command is implemented
  in the scene (`M3_SMOKE`) and checks setup, an interactable human Runner
  decision, delayed Corp thinking, rendered-frame progress, and action-panel
  containment after the first submitted action.
- `scripts/verify.sh` passes end to end: 356 tests, deterministic CLI/replay/M2
  proofs, headless Godot editor import, `M3_SMOKE`, format, card-data, and
  secret checks.

The packaged Linux M3 exit gate, automated Godot click-through of the full run,
save/resume UI, and broad Gateway card coverage remain future work. This
document records the starting vertical slice rather than claiming those gates
are complete.
