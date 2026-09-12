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
