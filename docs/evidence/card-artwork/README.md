# Artwork rendering evidence

These captures use the generated geometric card-face fixture from
`Main.CardArtworkSmoke.cs`. No official card-front artwork is included.
The concealed card uses the existing committed generic NSG back.

- `faces-1920.png` and `faces-2560.png`: portrait and sideways card faces,
  selected/actionable and target cues, counters, a concealed card, and text fallback.
- `inspection-1920.png` and `inspection-2560.png`: upright artwork next to
  current rules and state, with the underlying table behind the modal.
- `lab-runner-1920.png` and `lab-corp-1920.png`: interaction-lab boards using
  the cache, including newly drawn cards.
- `hover-details-1920.png` and `hover-details-2560.png`: a single artwork preview
  with subtypes, current stats, advancement/typed counters, use state, hosting,
  rules, and interaction hints.
- `symbol-inspection-1920.png` and `symbol-inspection-2560.png`: full inspection
  with inline click, credit, recurring-credit, memory, link, subroutine, and trash
  glyphs. Hover and lab captures also show the matching board resource icons.
- `score-modal-1920.png` and `score-modal-2560.png`: a score-area browser above
  the entire board, including raised hand cards and pile counters.
- `pile-inspection-1920.png` and `pile-inspection-2560.png`: inspection opened
  from a pile, covering both the pile browser and board.
- `hover-dismissed-1920.png` and `hover-dismissed-2560.png`: the board after
  clicking a card and pressing Escape; selection and the hover preview are gone.

The bundled game glyphs are by [Null Signal Games](../../../src/Netrunner.Godot/icons/SOURCE.md),
under CC BY-ND 4.0, recolored for this UI.

Regenerate with Godot arguments `-- --card-art-smoke --card-art-screenshots`.
The smoke additionally checks both hover orientations, retained inspection on
ordinary snapshot updates, immediate clearing on concealment, texture reuse,
all 19 lab scenarios, lab draws for both seats, and missing/corrupt image
fallback. It checks suppression of native card/pile tooltips and clearing of
hover details, including concealed hosts. Symbol checks cover normalized and legacy
tokens, all bundled glyphs, text fallback, literal BBCode, and clearing on dismissal.
Modal checks cover all four public piles, nested inspection, focus restoration,
Escape selection clearing and dismissal order, and held-key repeats. Native
pixel checks confirm an element at the maximum Z index is visible before a modal
opens and fully covered afterward, both on the board and inside the pile browser.
Pointer regressions click hand/rig/identity/ICE/asset cards, move into empty space,
clear selection with and without the pointer over the card, and re-enter it.
They also check keyboard previews/activation and preserve mouse focus when
returning from inspection or a public pile, at both resolutions.
Real-cache captures are written only under
ignored `artifacts/card-artwork` by `--card-art-cache-smoke --card-art-screenshots`.
