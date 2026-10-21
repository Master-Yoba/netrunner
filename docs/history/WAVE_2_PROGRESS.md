# Wave 2 progress

Roadmap: [improvement roadmap](IMPROVEMENT_ROADMAP.md), Wave 2 (the board comes
alive for one run and one score).
Evidence date: 26–27 September 2026, on the Linux x86_64 development box.

## Branches

Stacked in order on `dev`:

| Item | Branch |
|---|---|
| E4b event completeness | `feat/e4b-event-completeness` |
| E5 determinization | `feat/e5-determinization` |
| U2 persistent board | `feat/u2-persistent-board` |
| U3 vertical slice | `feat/u3-beat-slice` |

## E4b: event completeness

**Decision: presentation events are derived in `Netrunner.Match`, not added
to Core.** `GameState.EventSequence` is part of the state hash, so a new Core
event would change every state hash and break replay of existing saves,
session traces and fuzz corpora. Core already carries enough to derive the
missing facts, so `PresentationEventProjector` builds them on the match
worker from the raw events and the authoritative states before and after
each accepted action. `MatchSnapshot.PresentationEvents` publishes them.

**Card references.** `CardKeyTracker` gives each card the viewer can see or
follow a presentation key: a visible card's ID, or an opaque `tracked:N`
handle for a face-down installed card. A handle stays stable while its card
stays installed and hidden, and is retired when the card leaves play or is
revealed, so it never links two separate hidden periods. A reveal publishes
both keys (`PreviousCardKey` → `CardKey`) so the board can flip the card in
place.

**Explicit rules.** `PresentationEventProjector.Rules` classifies every Core
payload type as Projected, ViewOnly (shown by another event or the settled
view) or Bookkeeping. A test fails when Core adds a payload type without a
rule. Raw event filtering for publication is unchanged
(`RunnerEventVisibility` and `CorpEventVisibility` stay fail-closed).

Audit of the Wave 2 slice (advance, score, run start, encounter, subroutine
fires, run end) plus the everyday board changes:

| Board change | Core source | Before E4b | Presentation event |
|---|---|---|---|
| Turn starts | `TurnStarted` | Present | `TurnStarted` (side, turn, clicks) |
| Click spent | `ClickSpent` | Present | `ClickSpent` (clicks left) |
| Credits gained or lost | `CreditsAdjusted` | Present; no source | `CreditsChanged` (delta, total); source not yet attributed |
| Draw | `CardDrawn` | Present | `CardDrawn` (key only when visible) |
| Install | `CardInstalled` | Present, but a Corp install hid the slot from the Runner | `CardInstalled` with a tracked key and server |
| Rez | `CardRezzed`, `IceRezzed` | Present | `CardRezzed`, with the tracked key it had before |
| Advance a face-down card | `AgendaAdvanced`, `BehaviorCardAdvanced` | **Missing for the Runner**: both were suppressed because the card is hidden | `CardAdvanced` with a tracked key, counters placed and total |
| Score, steal | `AgendaScored`, `AgendaStolen` | Present | `AgendaScored`, `AgendaStolen` (points; previous key when it was tracked) |
| Run starts | `RunInitiated` | Present | `RunStarted` (server) |
| Approach and encounter ICE | `IceApproached`, `EncounterInitialized` | Present; hidden ICE suppressed for the Runner | `IceApproached`, `EncounterStarted` with the ICE's tracked key |
| Subroutines broken | `SubroutinesBroken` | Present; no ICE reference | `SubroutinesBroken` with the encountered ICE |
| Subroutine fires | none | **Missing** | `SubroutineFired` (ICE, printed position), derived from the run's resolved-subroutine ledger; when a subroutine ends the run, the unresolved unbroken subroutines up to the first end-the-run one |
| Encounter ends | `EncounterCompleted` | Present | `EncounterEnded` |
| Server approached, run successful | `ServerApproached`, `RunSuccessful` | Present | same names |
| Access | `CardAccessed` | Present | `CardAccessed` |
| Run ends | `RunEnded` | Present | `RunEnded` (reason, success) |

Remaining gaps, outside the slice: credit changes don't say what caused them,
and damage doesn't list the trashed cards per card. Both can be derived the
same way when U3 extends past the slice.

Verification: random-legal games on developer and official content project
every slice kind; no event names a card the Runner can't see; tracked keys
are stable while a card stays hidden; and a face-down advance reaches the
Runner as a tracked card (official).

## E5: determinization

`ObservationSampler.Sample(state, viewer, knowledge, pool, runtime, random)`
in Core returns a simulation state consistent with what the viewer observes.

**Deviation from the plan's signature.** The plan sampled from an
`Observation`, but an observation can't be turned back into a full
`GameState` (it has no rules state or resolution stack). The sampler takes
the authoritative state as a structural template instead. It keeps zones,
instance IDs, public cards, counters and the rules state, strips every card
hidden from the viewer of its identity, and deals new identities using only
the given random source. It never reads a hidden identity, and the twin
tests below prove the output depends only on the observation. A search
agent would call it through its host, which holds the state; that wiring is
A3's job.

- Opponent cards are dealt from an explicit `OpponentCardPool`: `Decklist`
  (the preset list minus visible copies), `AnyOf` (any legal card, per slot)
  or `Placeholders`. ICE positions only receive ICE and server roots only
  agendas, assets or upgrades.
- The viewer's own deck order is also resampled from its own unseen cards.
- `SamplerKnowledge` pins cards the viewer knows right now by position (for
  example the top of R&D after an access); the caller drops a pin when a
  shuffle or move invalidates it.
- The engine's random state is reseeded, so a sample can't predict real
  shuffles or random accesses. Samples are marked simulated: serializing,
  snapshotting, hashing or applying them authoritatively fails.

Verification (developer and official content, random-legal games):
- **Self-consistency:** the observation built from a sample equals the
  original observation, for both viewers.
- **Twins:** twin states whose HQ/R&D cards, or face-down installed ICE, have
  swapped identities have equal observations and produce identical samples
  (cards and random state) for the same seed.
- **Pinned knowledge** is respected.
- **The opponent's card multiset** is preserved, and hidden cards really
  change.
- **Samples can be played forward** in Simulation mode.

## U2: persistent board

The table already reused card nodes by identity; the glitches came from
three places, now fixed:

- **Re-keyed hidden cards.** A face-down card was keyed by its position
  (`server:X:ice:0`), so installing ICE in front of it re-keyed it and it
  animated as if it had moved or arrived. Views now carry per-position
  presentation keys (`ServerViewModel.IceKeys`, `RootKeys`) from E4b's
  tracker, and a face-down card keeps its node for as long as it stays
  hidden. A rezzed or otherwise revealed card keeps its node too
  (`MatchViewModel.RevealedFrom`), so it flips in place instead of flying in
  from HQ. Positional handles remain as aliases for targeting.
- **Duplicate cards during motion.** A flight stand-in flew while the real
  card already sat at its destination. The real card (or the destination
  pile's face) is now held invisible until the stand-in lands, so a card is
  never on screen twice.
- **Bounce-back on drop.** A dropped card returned to the hand until the
  engine answered, then flew out again. A single-action drop now waits at the
  drop point, dimmed, with the hand card hidden; on accept it flies from the
  drop point to its slot, and on rejection it animates back to the hand.

Credit totals count between values instead of jumping; a card whose
presentation changes in place (a new counter, a rez) pulses, as before, and
the reduced-motion path still pulses without flights.

**Deliberate scope choice.** Cards still live in their layout containers,
with flights drawn by a stand-in while the real node is held, rather than
moving every card onto a free overlay layer. That keeps the existing layout
and its smoke tests, and meets the gate's visible contract. The pure,
headless layout function the plan suggests is not done; it becomes worth
doing with U4's layout redesign.

**Evidence.** The capture harness samples every frame for a card visible
twice (a stand-in and the real card at once) and fails on any.
- A Runner capture (seed 42, 33 local actions, 170 frames with motion in
  flight) had **zero duplicates**.
- The same run with the hold disabled failed with 9 duplicates, including
  two face-down cards, so the check is real.
- `make m4-gate` passes (coverage 77/77).
- A coordinator test checks that every server position is keyed (tracked
  exactly when face-down) and that reveals map to the tracked key they had.
  It caught an ICE ordering bug during development.

## U3: event timeline, vertical slice

- **Beats (Match, headless).** `BeatPlanner` maps each publication's
  presentation events to beats: a caption, a base duration (0.3–0.9 s), an
  emphasis and a hold flag. The slice covers turn start, run start,
  approach, rez, encounter, subroutines broken, subroutine fires, server
  reached, run successful, access, run end, advance, score and steal.
  Installs, draws and credits stay with U2's board motion.
- **Presenter (Godot).** `BeatPresenter` plays beats in order:
  - a caption banner in the empty lower half of the server field;
  - the run's server lane stays in focus, with the others dimmed, for the
    whole run;
  - the approached or encountered ICE is enlarged until the Runner passes
    it;
  - fired subroutines, new counters, rezzes, accesses, scores and steals
    pulse;
  - scores, steals and run ends hold briefly.

  Playback waits for the beats' total length divided by the speed setting.
  Skip drains them, and reduced motion keeps the captions without scaling.
- **Hidden counters.** Advancement counters are public, but a face-down
  card showed none. Views now carry `AdvancementByKey`, and card backs draw
  an ADV badge.
- **Snap mismatches.** `BoardDisplayState` holds the slice's board facts:
  advancement per card, points, the run's server and whether an encounter is
  on. Applying a publication's events to the previous view must reproduce
  the next view exactly; any difference is a snap mismatch. The client logs
  mismatches to the session diagnostics, and the capture harness reports
  them.

Event gaps found by the mismatch check and fixed in the projector:

| Gap | Fix |
|---|---|
| A breach can complete (for example on a steal) one action before Core emits `RunEnded` | End the run as soon as it has an end reason, and skip the later event |
| The run disappears without `RunEnded` when the game ends during access | End the run when it vanishes |
| An access reveals a face-down installed card (for example a trap) | `CardAccessed` carries the previous tracked key; any other key change becomes `CardKeyChanged` |
| An advanced card leaving play for any reason other than a score or steal | `CardLeftPlay` for installed cards that move out of servers |

Evidence:
- **Headless snap mismatches:** zero over 80 random-legal official games (40
  seeds, both seats) while developing. The committed test runs 12 seeds per
  seat on each content lane.
- **Captures:** zero snap mismatches and zero duplicates in both seats.
  - As the Corp (seed 42, 70 local actions): 72 beats of 13 kinds, including
    approaching and encountering Karunā and Whitespace, a broken subroutine,
    advances, a score and a steal.
  - As the Runner (51 local actions): 42 beats, including a face-down
    advance building to three counters and then scoring.
- **Fired subroutines** are covered by the headless tests; the scripted
  captures' runs broke or avoided them.

## Gate status

| Criterion | Result |
|---|---|
| Captures show no duplicate or snap-back cards | Pass: every capture frame is checked for a card on screen twice (zero in both seats; a control run without the fix found 9); the lab smoke requires drops to wait for the engine and settle visible |
| A Runner run and a Corp score readable from the board alone | Pass in captures (banners, lane focus, ICE emphasis, counters on face-down cards, score holds); **needs a human playtest to confirm** |
| Zero snap mismatches on the slice's events | Pass: zero over 80 official random-legal games, both seats, and zero in every capture |
| Sampler self-consistency and hidden-state twin tests | Pass on developer and official content |
| `make m4-gate` | Pass (coverage 77/77) |
| `make m5-client-smoke` | Pass (5 cases, 10 phases) |
| `make m5-gate` | Pass; frozen results unchanged (no policy code changed) |

Gates ran with `NETRUNNER_TIMEOUT_SCALE=4`. No policy code changed in this
wave, so no policy performance run was needed.

Worth checking by hand: a run into ICE from the Runner seat (the lane focus,
the ICE growing on approach, the subroutine captions, then the run-end hold),
a face-down card being advanced (the ADV badge on the back), and dragging a
card to install it (it should wait where you dropped it, not bounce back).
