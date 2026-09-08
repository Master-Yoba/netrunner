# M4.10 — Editor-friendly UI progress

Status: implementation delivered on `feat/m4-10-editor-ui`; final verification
and the independent user authoring exercise are pending.

Specification: [editor-friendly UI plan](M4_10_EDITOR_FRIENDLY_UI_PLAN.md).
Authoring instructions: [UI editor guide](UI_EDITOR_GUIDE.md).

## Checkpoints

| Checkpoint | Status | Evidence |
| --- | --- | --- |
| UI-4100: card editor workflow proof | Passed | Live glow/typography edits, shared resource identity, save/reopen and launch proved before broad migration |
| UI-4101: shared visual foundation | Implemented | Shared CardAppearance, M4Theme, authored portrait/compact/ICE label layouts, safe resource replacement and reentry |
| UI-4102: board and zones | Implemented | GameBoard, ServerLane, PlayerArea, HandView and PileView; notifying BoardLayout |
| UI-4103: decisions and history | Implemented | Authored dock/actions, access, hover, inspection, outcome, pile browser and history rows/details |
| UI-4104: shell and cleanup | Implemented | Main binds authored production composition; fixed production builders removed |
| UI-4105: editing and gameplay gate | Pending user exercise and final checks | Evidence below; automated checks do not replace user acceptance |

## Architecture

- Presentation scenes own fixed composition. Dynamic cards, lanes, history rows
  and action buttons instantiate reusable scenes. Engine state, issued action
  IDs and replay formats are unchanged.
- Shared resources notify consumers when edited. Consumers disconnect on exit,
  reconnect on reentry and rebind replacement resources. Card state remains
  per instance; selection never changes shared defaults.
- `UiPreview.tscn` uses synthetic presentation data for both seats and
  empty/normal/dense boards, card states, decisions and overlays. It does not
  instantiate Main, a coordinator, agents, saves or diagnostics.
- `Main` remains non-tool. Legacy diagnostic/milestone layouts retain their
  isolated builders; normal gameplay uses the authored scene composition.
- Card appearance owns custom drawing/typography, label scenes own anchors,
  BoardLayout owns named board geometry, and history exports own row styling.
  ICE retains the baseline 240×87 lane footprint through a height scale.

## Editor proof

Using the pinned Godot 4.7.2 .NET editor, without rebuilding between edits:

- Glow outer extent 16→24, opacity .65→.75, falloff 2→1.6 and title font 10→12:
  six assertions passed, including immediate label updates and shared binding.
- Lane width 285→301, hand overlap 36→44 and selected color changed through
  exported setters; four assertions passed and both resources saved.
- Quit and restarted the entire editor. Five assertions confirmed the saved
  card, color and layout values. A launched official seed-47042 match reported
  the saved glow, font, lane width and overlap.
- Edited the running match's shared appearance from extent 24→32 and opacity
  .75→.9. Eight live cards shared the changed resource. Stopping and restarting
  restored Local values 24/.75, proving Remote-only changes were discarded.
- The proof used temporary editor automation to invoke the same exported
  setters used by the Inspector. Its addon configuration and scripts were
  removed, and experimental resource values restored after verification.
- Fresh editor previews displayed populated card labels and synthetic boards.
  A stale C# assembly during development required an editor restart.

## Regression and performance evidence

- Godot project builds passed with zero warnings/errors.
- `UiEditorSmoke.tscn` passed: shared change propagation, replacement, sizing,
  stable controls, reentry, hidden-card clearing, three preview reload/free
  cycles, no diagnostic files created and no retained appearance subscriptions.
- Native official `--m4-9-ui-smoke --require-official-content` passed both seats
  at 1920×1080 and 2560×1440: dense boards, 2,000-entry history, runs/access,
  keyboard/hover/unknown-card behavior, selection and motion replacement.
- Paired Apple M5 measurement, 120 idle frames: baseline `dd33647` median
  2.43 ms / p95 2.68 ms; initial migrated UI 2.66 ms / p95 2.78 ms. Managed
  allocation was identical: 32,800 bytes total, 273.33 bytes/frame. These are
  single-run measurements, not a statistical benchmark.
- Full repository verification and final native rerun: pending.

## Outstanding acceptance

The user must independently tune and save the glow using the delivered guide,
reopen the preview and launch a game, then report persistence and usability.
This is acceptance item 9 in the plan and remains pending. Manual exploratory
playtesting in both seats also remains user acceptance; automated native
scenarios are recorded separately. The M4.9 manual usability gate remains
separate. M5 is not unblocked by this implementation alone.
