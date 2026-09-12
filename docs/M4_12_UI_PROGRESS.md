# M4.12 UI refinement progress

Implemented on `feat/m4-12-table-controls`, based on M4.11. All implementation,
integration and review were performed by the primary agent, as requested.

- Lower-right hand fan with side count, stable hover hit targets, smooth lift,
  straightening and slight enlargement; keyboard and Reduced motion support.
- Credit icons and clickable Stack/R&D piles with availability glows and counts.
- Direct Archives browsing; generic Runner Run with legal server cues and cancel.
- Two additional resettable lab scenarios for the hand and board controls.
- Authored hand dimensions coordinate the play area, dock and history rail.
- Known Archives faces retain hover previews. Cards returning to hand regain
  pointer and keyboard input, and central piles follow a replacement board.

## Verification

- Native Godot interaction checks passed at 1920×1080 and 2560×1440 for both
  seats. Credit/draw clicks submit exactly one current action; busy and modal
  states disable them. Archives is browsable when empty and when occupied.
- Run checks cover legal destinations, invalid targets, cancel, Escape, stale
  revisions and selection of the issued action through native server clicks.
- Hand checks cover hover lift/straightening, a stationary pointer, return to
  the resting fan, keyboard focus and Reduced motion. All 15 existing drag
  scenarios also pass at both resolutions.
- Native smoke input is isolated from unrelated desktop pointer events.
  Screenshot capture explicitly draws the viewport, including occluded windows.
- Full `make m4-gate`: PASS, including 1,414 solution tests, official card
  coverage 77/77, both seats with basic/expanded decks, recovery, dense boards,
  editor isolation, formatting and secret scanning. Log: `/tmp/m412-m4-gate-final.log`.
- Native interaction log: `/tmp/m412-native-reviewed.log`.
- The final editor-preview update exposes ordinary credit/deck glows and the
  current dock controls. Editor smoke and formatting verification passed after
  that update (`/tmp/m412-editor-final.log`, `/tmp/m412-format-verify.log`).

## Evidence and quick testing

- [Resting Runner hand](evidence/m4-12/hand-resting-1920.png)
- [Runner hover](evidence/m4-12/hand-runner-1920.png)
- [Corp hover](evidence/m4-12/hand-corp-1920.png)
- [Run selection](evidence/m4-12/run-selection-1920.png)

Run `make interaction-lab` and choose **Runner: hand and board actions** or
**Corp: hand and board actions**. Reset freely to repeat. These are synthetic
presentation fixtures; use `make play` to assess the feel in a real match.
The supplied reference video remains local and ignored by Git. Hand animation
and glow settings are documented in the [editor guide](UI_EDITOR_GUIDE.md).

## 2026-09-12 — Deck-to-hand animation

- Drawn cards leave Stack/R&D on the appropriate NSG back, grow and flip during
  an eased arc, then settle at the correct angle in the hand. Existing hand
  cards ease apart, and multiple arrivals are staggered.
- Incoming hand faces stay hidden until landing. Skip animation, Reduced motion
  and interrupted transitions restore them. Playback waits for the flight before
  presenting the next queued state; repeated snapshots do not replay a draw.
- Animation reads only the filtered hand and public deck counts, excluding
  known cards returning from other zones. The initial board and opponent hand
  identities remain outside the local draw animation.
- `HandView` exposes duration, arc height and stagger timing in the Inspector.
- Validation: native and headless interaction checks passed for both seats at
  1080p/1440p, including deck origin, back-to-face reveal, landing, multiple draws,
  interruption/skip, Reduced motion and hidden-information checks. Existing drag,
  dense table, card motion, editor-preview and formatting checks also passed.
  Logs: `/tmp/draw-native.log`, `/tmp/draw-headless.log`, `/tmp/draw-table.log`,
  `/tmp/draw-editor.log`, `/tmp/draw-format-verify.log`.
- Evidence: [Runner draw](evidence/m4-12/draw-runner-1920.png),
  [Corp draw](evidence/m4-12/draw-corp-1920.png).

## 2026-09-12 — Dragging hand discards

- End-of-turn discard candidates glow and drag to the local Heap or Archives.
  The destination highlight fits the actual pile. Wrong-pile drops return to
  hand; ordinary card choices remain separate. Default per-card buttons are
  replaced by the drag instruction; **All legal actions…** preserves the
  existing confirmation controls for keyboard and recovery use.
- Accepted selections leave the fan immediately and show a pending count at
  the pile. The engine retains selected cards in hand until the complete set
  resolves, so authoritative hand/pile counts are preserved. Selection display
  is reconstructed from the latest issued candidates, including after resume.
- Heap is a card-sized pile with a visible label, public top face, hover preview
  and click-to-browse. Pile faces remain available in contextual choice browsers.
- Two new lab scenarios, **Runner: discard from hand** and **Corp: discard from
  hand**, each exercise a two-card discard without playing a match.
- Validation: 166 Match tests passed, including real-engine menu/drop state and
  event equivalence, save/restore during selection, and exclusions for unrelated
  choices or hidden cards. Native and headless interactions passed for both
  seats at 1080p/1440p: wrong pile, pending selections, complete discard sets,
  top-face hover, empty/nonempty browsing and fallback confirmation. Native
  checks also verify highlight dimensions. Existing drag, draw motion, dense
  table, editor and formatting checks passed. The full M4 gate above predates
  this refinement and was not rerun.
- Logs: `/tmp/discard-match-tests.log`, `/tmp/discard-native.log`,
  `/tmp/discard-headless.log`, `/tmp/discard-table.log`,
  `/tmp/discard-editor.log`, `/tmp/discard-format-verify.log`.
  One editor run emitted a Godot `SwapGCHandleForType` resource-handle error;
  a separate rerun completed cleanly.
- Evidence: [Runner discard](evidence/m4-12/discard-runner-1920.png),
  [Corp discard](evidence/m4-12/discard-corp-1920.png),
  [pending selection](evidence/m4-12/discard-pending-1920.png).

## 2026-09-12 — Discard animation correction

- The first selection previously produced no flight because it remained in the
  authoritative hand. The final selection could produce two flights when the
  first selected card became the pile top: the generic renderer treated that
  newly visible face as a card arriving from the Corp header.
- Each newly accepted choice now gets one hand-to-pile flight with its original
  position, scale and rotation. Previously selected cards are excluded when the
  engine commits the complete set. Exposing an older pile face does not invent
  movement. A final arriving face stays hidden until its flight finishes; Skip,
  Reduced motion and replacement rendering restore it safely. Playback waits
  for pending discard motion, including choices without public history events.
- The original discard interaction smoke used Reduced motion and missed this
  regression. New native and headless checks run with animation enabled for
  both seats at 1080p/1440p, discarding the leftmost and rightmost cards twice.
  They verify one flight per choice, hand origin, travel toward the pile, clean
  completion, repeated observations, pile-top changes, menu choices, Skip and
  Reduced motion. Existing drag/draw, dense-board and editor checks pass, as
  does formatting verification. No engine or Match contracts changed.
- Logs: `/tmp/discard-motion-headless.log`, `/tmp/discard-motion-native.log`,
  `/tmp/discard-motion-table.log`, `/tmp/discard-motion-editor.log`,
  `/tmp/discard-motion-format.log`.
- Evidence: [first rightmost discard](evidence/m4-12/discard-motion-1-1920.png),
  [second rightmost discard](evidence/m4-12/discard-motion-2-1920.png).

## 2026-09-12 — PR review integration

- Reviewed all feedback on #18 and #19 on the combined M4.11/M4.12 branch;
  #19 contains every commit in #18 and supersedes it for the merge into `dev`.
- #19: hand dragging now respects a pending menu confirmation at both press
  and release. The native regression reproduces the issue with an install
  confirmation, verifies that cancellation restores dragging, and also stages
  confirmation during an active gesture.
- #18: the busy submission guard was already present. Contextual buttons,
  card targets, pile-browser choices and server cues now also show their
  unavailable state while submission or board settlement is pending. They
  recover automatically when input becomes available. Confirmation also blocks
  contextual submissions, and target inspection remains available.
- Native regression checks passed at 1080p and 1440p, including attempts through
  disabled buttons, card/server selection and direct callbacks. Smoke input
  isolation covers unrelated desktop focus changes; an explicit focus-loss
  signal still exercises the production cancellation handler.
- Evidence: [contextual controls while settling](evidence/m4-12/choice-settling-1920.png).
  Native log: `/tmp/pr19-review-native.log`. Final full M4 gate and CI results
  are recorded in [PR #19](https://github.com/Master-Yoba/netrunner/pull/19).
