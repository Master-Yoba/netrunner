# M4.12 — Hand fan and direct board actions

The local hand moves into a lower-right area beside the action dock. Its shallow
fan, overlapping faces, side count and eased hover lift follow the supplied
`reference hand.mov`. The reference stays local and is excluded from Git.

## Interaction and architecture

- `HandView.tscn` and `HandFanControl` own the fan and hover geometry. Resting
  footprints decide pointer targets so animation cannot make hovering oscillate.
  Keyboard focus lifts cards too; Reduced motion switches poses immediately.
  The production card controls and drag controller remain in use.
- Newly observed local hand cards accompany a decrease in the local deck count
  with a deck-to-hand flight. The card starts on the correct back, flips during
  an eased arc, and lands in the fan. Known cards returning from other zones
  are excluded. Multiple draws are staggered; skipping restores all arrivals.
- The main play surface excludes the hand and action dock. `TableWorkspace`
  coordinates the hand footprint, board, dock and history widths. Component
  scenes retain editable layout and appearance properties.
- Credit indicators combine a credit icon with the current public balance.
  The local indicator and Stack/R&D pile glow only when their corresponding
  issued action is available. Clicks submit that current action ID through the
  existing submission path, including busy, recovery and opponent-turn guards.
- Archives opens from the pile or server heading, including when empty.
  Browsing uses the existing viewer-filtered pile projection. The former top
  Archives and R&D buttons are removed; the piles show public card counts.
- During end-of-turn discard, highlighted hand cards drag to the local Heap or
  Archives. Only issued `TurnDiscard` choices admit these destinations; other
  card choices retain their existing interactions. Invalid drops return to hand.
  Accepted selections leave the fan and show a pending count until the engine
  commits the complete discard set. This presentation is rebuilt from the current
  candidates after a resume; authoritative hand and pile counts stay unchanged
  until the discard resolves. Per-card buttons move under **All legal actions**.
  Heap uses the same card-sized, browsable pile component as Archives.
- Runner **Run** stages a local server choice. Only issued StartRun destinations
  are selectable. Selecting one submits its ID; Cancel, Escape or right-click
  spend nothing. Decision changes invalidate the staged selection.
- Credit/draw leave the ordinary action dock. **All legal actions** remains the
  fallback, alongside existing play/install menus. Rules, agents, randomness,
  authoritative state and replay formats do not change.

## Acceptance

Use `make interaction-lab` and select either **hand and board actions** scenario.
Verify both seats, five-card and crowded fans, moving between adjacent cards,
keyboard hover, Reduced motion, all existing drag destinations, active/disabled
credit and deck controls, empty/nonempty Archives, Run selection and cancellation.
Use **Runner: discard from hand** and **Corp: discard from hand** for two-card
discards, wrong-pile drops, pending counts, pile browsing and the menu fallback.
Engine projection tests verify selections after save/restore and equivalence
between menu and drag submissions.

The native regression entry point is `--m4-12-ui-smoke`; the existing
`--m4-11-ui-smoke` gate includes these checks too. `--m4-12-screenshots` saves
synthetic evidence under `docs/evidence/m4-12`. Native smoke input is isolated
from unrelated desktop pointer events. Final verification is recorded in
[M4.12 progress](M4_12_UI_PROGRESS.md).
