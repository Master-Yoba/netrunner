# M2 implementation progress

Last updated: 2026-08-28

Status: paused by user request

Checkpoint commit: `52a06e6 checkpoint: save M2 engine progress`

Branch: `dev`

## Milestone goal

M2 delivers a deterministic, headless representative Netrunner run using the
Null Signal Comprehensive Rules 26.03 timing model. The complete acceptance
contract is in [M2_ARCHITECTURE.md](M2_ARCHITECTURE.md).

## Completed and accepted

### Architecture and scope

- Defined the four dependency-ordered M2 implementation slices.
- Recorded the authoritative run timing, effect/checkpoint, access,
  observation, persistence, and exit-gate contracts.
- Selected the representative System Gateway cards: Palisade, Whitespace,
  Cleaver, Buzzsaw, Overclock, Offworld Office, and Nico Campaign.
- Kept official card data as local ignored input; it is not redistributed by
  this repository.

### Slice 1: run timing skeleton

- Added optional, serializable `RunState` and its invariants.
- Added the CR 6.9 run phases and timing steps.
- Added alternating paid-window priority and consecutive-pass handling.
- Added approached-ICE rez opportunity without a separate decline-rez action.
- Added approach, encounter boundary, movement, jack-out, success, placeholder
  breach boundary, run-end cleanup, and typed timing events.
- Preserved save/load at each run prompt and M1 no-run serialization behavior.
- The focused run timing suite passed before moving to Slice 2.

### Slice 2A: costs and reusable rule effects

- Added atomic credit/click costs and ordered effect bundles.
- Added deterministic net, meat, and core damage plus flatline victory.
- Added tags, memory state, installed program memory costs, mandatory memory
  cleanup, card movement, named counters, and virus purge.
- Added encounter/run/turn lingering-effect expiry.
- Added typed events and deep state/frame invariants for these primitives.
- The focused rule-effect suite passed before moving to Slice 2B.

### Slice 2B: checkpoint vertical

- Added serializable imminent/resolved occurrence snapshots.
- Added typed trigger and interrupt registrations and materialized invocations.
- Added effect checkpoints, persisted instruction cursors, and nested child
  effect resolution.
- Added mandatory and optional trigger handling.
- Added same-controller trigger-order choices.
- Added partial/full damage prevention and replacement effects.
- Added source definition/zone snapshots and trigger/interrupt recursion
  suppression fields.
- Added exact checkpoint legal-action validation.
- Changed cross-controller trigger materialization to start with the active
  player, following CR 9.2.8b, instead of sorting priority by identifier text.
- Six initial checkpoint tests pass: no registrations, mandatory trigger,
  optional use/decline, and partial/full damage prevention.

## Validation at the pause point

- `dotnet build Netrunner.sln --no-restore --nologo -m:1`: passed.
- Build result: 0 warnings and 0 errors.
- `git diff --check`: passed before the checkpoint commit.
- The six-test `CheckpointTests` vertical passed before the final
  active-player ordering refinement.
- Earlier Slice 2 full regression runs passed. A fresh full test and release
  verification run is still required when work resumes.

## Exact pause point

Luna Max was stopped while expanding Slice 2B tests. No partially written test
was left in the worktree. Production code for the checkpoint vertical is
present, but Slice 2B is not accepted yet because its adversarial and
persistence coverage is incomplete.

The next task is to finish `CheckpointTests.cs` with these cases:

1. simultaneous same-controller triggers and explicit next-trigger ordering;
2. active-player precedence between controllers;
3. replacement resolves instead of the original effect;
4. a materialized source snapshot survives the source moving afterward;
5. nested save/load at every yielded checkpoint decision;
6. identical state, event trace, and hashes after reload;
7. invalid/tampered action rollback and action-bijection rejection;
8. recursion suppression resolves a self-trigger exactly once;
9. no-applicable-registration fast path.

After adding those tests, review any failures, correct the reducer, then run the
focused checkpoint suite and the full solution suite. Only then mark Slice 2B
accepted.

## Remaining M2 work

### Slice 3: representative run behavior

- Introduce explicit Core card-behavior specifications populated by the Cards
  layer; do not parse rules text at runtime.
- Charge real rez costs and initialize printed ordered ICE subroutines.
- Implement strength, breaker interface matching, pump effects, partial/full
  breaking, ordered unbroken subroutine resolution, and end-the-run cleanup.
- Implement Palisade, Whitespace, Cleaver, Buzzsaw, and Overclock behavior.
- Replace the placeholder breach with deterministic remote, HQ, R&D, and
  Archives candidate selection and sequential access.
- Implement pay-trash, no-action, and mandatory agenda-steal access paths for
  Nico Campaign and Offworld Office.
- Add `ObservationBuilder.Build(state, viewer)` and exact negative tests proving
  hidden Corp and Runner information never leaks.

### Slice 4: milestone proof and exit gate

- Add checked-in M2 scenarios covering every required timing state/window, with
  CR 26.03 citations and Jinteki.net cross-check notes.
- Add the curated representative run and deterministic micro-deck setup.
- Add scripted-human and contract-agent headless coordinators using only
  filtered observations and issued legal action IDs.
- Add save/load-at-every-decision and deterministic replay evidence.
- Add automated and human-readable timing-trace validation.
- Add the M2 CLI proof command and tests.
- Run formatting, analyzers, all solution tests, secret scanning, replay and
  determinism gates, and the existing Godot headless smoke test.

## Resume commands

```bash
git switch dev
git log -2 --oneline
.tools/dotnet/dotnet build Netrunner.sln --no-restore --nologo -m:1
.tools/dotnet/dotnet test tests/Netrunner.Core.Tests/Netrunner.Core.Tests.csproj \
  --no-restore --no-build --nologo --filter FullyQualifiedName~CheckpointTests
```

Do not modify `src/Netrunner.Godot/addons/godot_ai`; it is user-owned and
outside the M2 work.
