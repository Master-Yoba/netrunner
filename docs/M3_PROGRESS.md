# M3 progress — playable Godot micro-deck slice

Status: complete for the explicitly substituted native-macOS package gate

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
actions, staged target selection with explicit Confirm/Cancel, an on-demand
card-zoom overlay, event animation queue controls, reduced motion, and focused
encounter/access/paid-window presentations. Worker publications cross a
thread-safe queue so a fast opponent cannot overwrite event batches between
rendered frames. Dynamic board, action, and details content use bounded scroll
viewports, so event history or a larger legal-action set cannot push the action
panel below the window. It is deliberately text-only and uses labels and
borders in addition to colour.

## Evidence

- `tests/Netrunner.Match.Tests/MatchCoordinatorTests.cs` covers embedded
  scenario/start events, hidden-info-safe mapping, stale rejection/state
  preservation, an injected ten-second agent delay, and completion of the
  representative run while retaining every published event batch.
- Existing Core observation goldens and CLI M2 proof tests remain green after
  rehoming the scenario/setup code.
- Godot project C# build succeeds. The headless M3 smoke command in the scene
  (`M3_SMOKE`) drives the deterministic Runner policy through the complete M2
  run after the initial human-style action. It verifies rendered encounter and
  access focus, the first target's local stage/cancel path without a coordinator
  publication or observation mutation, RunEnded/RunOutcome retention, the
  visible slice-complete presentation, no pending action, and action-panel
  containment.
- `scripts/verify.sh` passes end to end: 356 tests, deterministic CLI/replay/M2
  proofs, headless Godot editor import, `M3_SMOKE`, format, card-data, and
  secret checks.
- Native macOS packaging is configured by `scripts/package-macos.sh` and runs
  both bounded smokes: the full-slice `M3_SMOKE` and the real ten-second
  `M3_LATENCY_SMOKE` (one-frame inspection/control, near-60-FPS assertion,
  hover/selection, card zoom, and 1280×720 and 2560×1440 layout checks). The
  fresh universal bundle passed both packaged smokes: the latency hold lasted
  10,253 ms while 1,486 frames rendered (144.9 FPS on the verification host).

The intended completion gate uses the explicitly substituted native macOS
artifact for the current development lane. Linux packaging remains deferred.
Save and resume are M5 deliverables; broad Gateway card coverage is an M4
deliverable, not a remaining M3 gap.
