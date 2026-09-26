# Board icebreaker controls

During an encounter, click a matching installed icebreaker to open controls
beside it. Only breakers whose typed interface matches the ICE receive action
highlights and controls; this includes wildcard breakers and matching breakers
that still need to boost their strength. Incompatible cards remain inspectable.
The panel and card badges show current breaker and ICE strength.

Each boost or break has a single payment button. It spends hosted run credits
first and covers any remainder with personal credits. The presenter selects the
existing engine-issued action with the largest hosted portion; it never creates
a payment or changes the ability cost. Boost activates immediately.
Click another matching breaker to switch.

Choose **Break subroutines…** to open selectable subroutine rows beside the
encountered ICE. Click the subroutines you want to break, then choose **Pay and
break selected subroutines** to confirm the displayed payment. You can select
any legal combination, including non-adjacent subroutines. Selection spends
nothing; activating the ability pays once for the selected set. Broken or
resolved subroutines and choices beyond the ability's limit are disabled.
Subroutine text uses the same game icons as card tooltips.

Escape or Back cancels target selection and returns to the breaker controls.
Escape again or Close clears the breaker selection. Controls are unavailable
while an action is being submitted, animations are settling, the opponent is
thinking, or an inspection/pile modal is open. A new decision cancels target
selection; leaving the encounter removes the breaker selection. The default
action dock points to the board; **All legal actions…** provides the fallback
and hides the local panel while it is open. It applies the same matching-breaker
and hosted-first payment rules.

Core materializes one action for every legal target set and payment. The optional
`RunBreakParameters.SubroutineOrdinals` contains a detached, sorted list of exact
encounter ordinals. Hosted-counter actions also retain their opaque subroutine
ID. The reducer validates targets before paying, and the invariant validator
checks the complete target/payment action set. Old saved decisions without the
new field retain their first-unbroken targeting until the next decision is
materialized. Old replay action IDs can differ because newly issued target
alternatives advance the action allocator.

`BoardIcebreakerPresenter` only filters the viewer's issued actions and public
encounter data. Interface compatibility uses Core's typed matcher with the
injected behavior catalog, including its wildcard interface. Filtering and
payment preference do not change engine actions, state, or replay IDs. The Godot submission handler rechecks the current decision,
revision, source, targets, and action ID before using the existing submission
path. Board selection is local UI state, never game state.

The interaction lab includes **Runner → Boost & break with an icebreaker** with an understrength
fracter, an incompatible decoder, three subroutines, and both personal and hosted
credits. It uses the existing artwork cache. Engine-backed tests cover exact
single and non-adjacent targets, one payment per activation, malformed or stale
targets, save/load, legacy decisions, and action-set completeness. Match tests
also drive a real Cleaver/Palisade encounter through the projected actions.

The interaction smoke covers board clicks, current strength, target selection,
hosted-first payments, incompatible breakers, repeated activations, stale callbacks, unavailable
input, Escape, fallback controls, and encounter exit at 1920×1080 and 2560×1440.
Pass `--icebreaker-screenshots` to capture examples; use an empty artwork cache
for screenshots committed to the repository.

The curated M2 proof retains its 32 decisions, verified replay, and identical
event-stream hash. Exact target alternatives change its final allocator/state
hash to `1df2d6682393f40268aa977c5ff5b87d453bfeb5cf99cebd23066ea5dfe75660`.
