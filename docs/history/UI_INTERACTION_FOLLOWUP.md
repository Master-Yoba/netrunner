# Priority, hover previews and card action cues

## Behavior

Full matches automatically submit Pass Priority when it is the human player's
only issued legal action. Any other available action keeps the decision manual.
Access continuation, end turn, and other single-choice decisions are unchanged.
Automatic actions go through the serialized coordinator and durable trace;
recovery remains paused until explicitly continued.

The visible history omits pass-only chatter. Meaningful effects resolving on a
pass remain visible, and the diagnostic action journal retains every submission.

A viewer-known card enlarges on hover or keyboard focus without moving the
source card's hit area. The preview is nonmodal, bounded by the viewport, and
clears when the card is concealed or no longer present. It consumes only the
filtered presentation model. Existing explicit inspection remains available.

White card borders identify issued play/install/ability actions for the human
player. These cues clear when the decision changes or the opponent is acting;
they do not infer affordability or abilities from card text.

This UI uses white for available source actions, blue for the selected card,
and cyan for choice targets. The reference's other colors are documented below;
they are not all introduced as additional states in this change.

## Chiriboga reference

Studied [the renderer](https://github.com/drbo6/chiriboga/blob/dev/cardrenderer/cardrenderer.js),
particularly `UpdateGlow`, pointer enter/exit, and `ToggleZoom` (8 September 2026).
The behavior is a reference; implementation uses the existing Godot controls.

| Reference color | Meaning in Chiriboga |
| --- | --- |
| White | Available card interaction; valid server or host destination |
| Purple | Hovered actionable card, toggled selection, valid drag release, or an ability currently resolving |
| Yellow | Hovered choice that will not immediately activate and needs another step |
| Blue | Card currently being accessed |
| Red | Noninteractive counter emphasis; not a general card danger indicator |

Chiriboga enlarges cards on pointer entry and restores them on exit. Our overlay
keeps the original board geometry stable and preserves hidden-information rules.

## Validation

- Match suite: 145 tests, including both human seats, manual alternatives,
  paused recovery, durable auto-pass replay, and preservation of pass effects.
- Official native UI smoke: both seats at 1920×1080 and 2560×1440, issued-action
  glows, quiet history, hover/focus previews, hidden-card clearing, stable source
  geometry, viewport bounds, and existing run/access/keyboard interactions.
- Native screenshots: `docs/evidence/m4-9/actionable-cards-1920.png` and
  `docs/evidence/m4-9/hover-*.png`.
- Official Corp-seat smoke completes setup, mulligan, turns and outcome at
  62 accepted actions. Formatting and secret scanning pass.
