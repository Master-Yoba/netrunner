# Slice 4.11 progress

Status: implemented on `feat/m4-11-drag-play`; complete local M4 gate passed.
[PR #18](https://github.com/Master-Yoba/netrunner/pull/18) targets `dev`.
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
- Full official M4 gate: PASS, coverage 77/77, both seats and basic/expanded
  decks, recovery, native UI, formatting and secret scanning. GitHub CI validates
  the pushed slice independently; its current result is visible on PR #18.

## Manual acceptance

Run `make interaction-lab`, select each scenario, and reset freely. Also play a
normal official match from each seat to assess target visibility, drag comfort,
scrolling and the preserved menu fallback. Prior manual UI acceptance statuses
remain unchanged; passing automated fixtures does not declare them complete.

## 2026-09-12 — Centered, smooth dragging

- Drag previews start centered on the pointer and follow movement smoothly,
  above hovered board cards. Destination hit testing still uses the pointer.
- Inspector setting `FollowSmoothing` defaults to 0.04 seconds; zero or Reduced
  motion tracks immediately. The destination hint sits above the preview.
- Build, formatting, and the full interaction smoke passed. Added checks cover
  centering at both resolutions, equivalent following at 60/120 FPS, immediate
  reduced-motion/zero-smoothing tracking, and drops while the image catches up.
- Screenshots refreshed. One screenshot run emitted a Godot `SwapGCHandleForType`
  exception while loading an action-button resource; a subsequent native run
  passed without errors. Log: `/tmp/m411-follow-native.log`.

## 2026-09-12 — Board card alignment

- Runner identity, rig and local hand cards share a top edge beneath matching
  headings. The layout remains authored in the player-area and hand scenes.
- Server root groups and ICE share a centerline, including hosted ICE and lanes
  with scrollbars. Run-marker rows stay reserved so active runs do not shift cards.
- HQ's public hand count appears in the root caption, leaving its identity centered.
- Build and formatting passed. Native interaction checks passed all 15 scenarios
  at 1080p and 1440p; headless table checks covered dense boards and active runs
  from both seats at both resolutions. Editor resource/preview checks also passed.
  Coordinate assertions cover Runner card tops, server rows, root groups and ICE.
- Refreshed the interaction screenshots. Logs: `/tmp/card-alignment-native.log`,
  `/tmp/card-alignment-table.log`, `/tmp/card-alignment-editor.log`.
