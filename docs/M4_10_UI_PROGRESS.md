# M4.10 — Editor-friendly UI progress

Status: implementation delivered on `feat/m4-10-editor-ui`; the independent
user authoring exercise and manual playtest acceptance remain pending.

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
| UI-4105: editing and gameplay gate | Pending user acceptance | Evidence below; automated checks do not replace user acceptance |

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
  2.43 ms / p95 2.68 ms; final migrated UI 2.68 ms / p95 2.78 ms. Managed
  allocation was identical: 32,800 bytes total, 273.33 bytes/frame. These are
  single-run measurements, not a statistical benchmark.
- `make verify` passed: 1,397 unit tests plus 15 official-content focused
  tests, formatting, CLI proofs, current 77/77 Gateway coverage, Godot import,
  gallery and UI smoke checks, responsiveness, complete basic/expanded games
  in both seats, fresh setup reloads and secret scanning.
- The last preview-only ICE anchor correction passed its native visual check;
  the final focused smoke and formatting checks passed. An intermediate smoke
  attempt overlapped another full-match process and saw that process create a
  diagnostics folder; rerunning serially passed without errors or warnings.
- Native seed-47042 recovery at action 31 passed with `paused=True` and
  `authoritative=True`.
- Final native UI rerun passed every scenario at both resolutions/seats, with
  zero errors/warnings. Native complete basic matches also passed for Corp
  (928 actions) and Runner (1,715 actions), including new-game reloads.
- Before/after captures show temporary in-memory glow/font edits in the
  production preview scene; no experimental resource values were saved.

## Visual evidence

- [Card preview before](evidence/m4-10/card-preview-before.png) and
  [after](evidence/m4-10/card-preview-after.png) temporary glow/font edits.
- [Setup](evidence/m4-10/setup-1920.png),
  [Corp dense board](evidence/m4-10/dense-corp-1920.png),
  [Runner dense board](evidence/m4-10/dense-runner-2560.png),
  [history](evidence/m4-10/history-corp-2560.png),
  [hover](evidence/m4-10/hover-live-1920.png),
  [R&D access](evidence/m4-10/rd-access.png) and
  [public pile browser](evidence/m4-10/archives-browser-1920.png).

## Outstanding acceptance

22 September update: [M5.0 acceptance reconciliation](M5_PROGRESS.md#scope-authorization-and-acceptance-reconciliation)
records authorization to begin the baseline slice without claiming completion
of the independent editor-persistence exercise below.

The user must independently tune and save the glow using the delivered guide,
reopen the preview and launch a game, then report persistence and usability.
This is acceptance item 9 in the plan and remains pending. Manual exploratory
playtesting in both seats also remains user acceptance; automated native
scenarios are recorded separately. The M4.9 manual usability gate remains
separate. M5 is not unblocked by this implementation alone.

## 9 September — card rendering follow-up

Editor zooming exposed a square header covering the rounded top border and
visible concentric lines in the inner glow. The header now follows the frame's
inner rounded corners. The inner glow is a continuous vertex-colored fade,
submitted as one indexed triangle array and drawn beneath artwork, motifs,
badges and labels. The outer shadow and Inspector controls remain intact.

Reviewed Luna Max's implementation, corrected arc endpoints, and batched the
gradient submission. Compared portrait and ICE cards at 3× scale and inspected
zero/wide glow, sharp/rounded corners and selected/target color samples.
[Before](evidence/m4-10/card-corners-before.png) and
[after](evidence/m4-10/card-corners-after.png) show the rendering correction.

Godot build (zero warnings/errors), `UiEditorSmoke`, native M4.9 UI regression
for both seats/resolutions, and formatting verification passed. The user's
local CardAppearance, M4Theme and project settings were preserved and remain
outside this rendering commit. Manual acceptance remains pending.
A same-session native baseline comparison measured median/p95 8.61/11.09 ms
before the slice versus 8.57/11.31 ms with this fix under the current workload;
these single runs do not indicate a material frame-time regression.

### Bottom status badge layering

The inner glow now renders above the bottom status badge background and below
its text, as requested during review. Its background remains an authored
`StatusBadgeStyle` on each label layout. Style edits/replacement and status
geometry changes trigger redraws; no per-frame polling or cross-card z-order
changes were introduced. Corrected subscription reentry and converted label
coordinates into card space during review.

[Updated enlarged preview](evidence/m4-10/status-glow-after.png).
Build, editor smoke (including status-style replacement/reentry), native UI
checks for both seats/resolutions, and formatting passed. Local user theme and
project edits were preserved.
