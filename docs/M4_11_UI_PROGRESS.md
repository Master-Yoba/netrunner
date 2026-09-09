# Slice 4.11 progress

Status: implemented on `feat/m4-11-drag-play`; final gate in progress.
Independent manual usability acceptance is pending.

The [approved plan](M4_11_DRAG_PLAY_PLAN.md) introduces drag-to-play/install and
board-first immediate resolution while retaining the existing action menus.

## Delivered

- Viewer-safe Match projection of issued plays, installs, hosts and server targets.
- Pointer-following card previews, destination/cost feedback, clipped hit regions,
  edge scrolling, invalid-drop return and Escape/right-click/focus-loss cancellation.
- General and targeted plays, rig/host/root/outermost ICE/new-remote installs.
- Board choices for immediate resolution, explicit ambiguity/finalization controls,
  and viewer-filtered pile choice cards. Run/combat/access retain existing controls.
- Existing decision/action validation and session submission diagnostics; no new
  authoritative state or replay schema. Drag outcomes add concise UI diagnostics.
- Editable scenes and `DragAppearance.tres` settings.
- `make interaction-lab` and a setup-screen entry with 15 resettable scenarios.
  The lab uses synthetic presentation fixtures, not a rules simulator or saved match.
- An adjacent Archives rendering guard now handles a wholly concealed discard pile
  without indexing an empty visible-card list.

## Review and verification

Luna implemented projection/tests, geometry/resources and initial choice controls.
Root reviewed and integrated these, implemented dragging and the lab, and corrected
server-ID handling, run-event staging, ambiguity/finalization, pile-card clipping,
face-down positional hosts and general-play geometry beside the hand.

- Match tests: 162 passed, including 11 projector regressions.
- Engine equivalence: seed 47042 menu/projected-drop paths produce identical
  authoritative state, events, observations and replay trace.
- Native smoke: 15 scenarios at 1920×1080 and 2560×1440, plus stale revisions,
  Escape/right-click/focus-loss/invalid-drop cancellation and exposed face-down ICE
  hosting. Contextual buttons, server cues and pile cards receive native input.
- Screenshots: [hosted install](evidence/m4-11/drag-3-1920.png),
  [outermost ICE](evidence/m4-11/drag-6-1920.png),
  [new remote](evidence/m4-11/drag-7-1920.png).
- Native demonstration: `--m4-11-demo` records hosted installation and a new remote;
  the local recording is `/tmp/m411-drag-demo.avi` (9.6 seconds). It is not committed.
- Full official M4 gate: running. CI will validate the pushed slice independently.

## Manual acceptance

Run `make interaction-lab`, select each scenario, and reset freely. Also play a
normal official match from each seat to assess target visibility, drag comfort,
scrolling and the preserved menu fallback. Prior manual UI acceptance statuses
remain unchanged; passing automated fixtures does not declare them complete.
