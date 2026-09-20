# Board icebreaker controls

During an encounter, click an installed icebreaker to open its controls beside
the card. The panel and card badges show current breaker and ICE strength. The panel shows subroutine status,
and the legal boost/break actions with their exact payments. A click on an
ability submits one action; the panel refreshes after each use so the same
breaker can boost and break repeatedly. Click another breaker to switch.

Escape or Close clears the selection. Controls are unavailable while an action
is being submitted, animations are settling, the opponent is thinking, or an
inspection/pile modal is open. Leaving the encounter removes the selection.
The default action dock points to the board; **All legal actions…** retains the
complete fallback and hides the local panel while it is open.

Ordinary engine break actions currently choose a quantity and break the next
unbroken subroutines in order. Their board buttons explicitly list those
subroutine numbers. This UI does not invent arbitrary subroutine targeting or
choose a hosted/personal payment split for the player. Hosted counter-break
actions retain their issued target description. Core rules and serialized action
contracts are unchanged.

`BoardIcebreakerPresenter` only filters the viewer's issued actions and public
encounter data. The Godot submission handler rechecks the current decision,
revision, source, and action ID before passing the issued action to the existing
submission path. Board selection is local UI state, never game state.

The interaction lab includes **Runner: icebreakers** with an understrength
fracter, an incompatible decoder, two subroutines, and both personal and hosted
credits. It uses the existing artwork-cache support. Engine-backed Match tests
also drive a real Cleaver/Palisade encounter through the projected actions.

The interaction smoke covers board clicks, current strength, exact payments,
repeated activations, switching breakers, stale callbacks, unavailable input,
Escape, fallback controls, and encounter exit at 1920×1080 and 2560×1440.
Pass `--icebreaker-screenshots` to capture examples; use an empty artwork cache
for screenshots committed to the repository.
