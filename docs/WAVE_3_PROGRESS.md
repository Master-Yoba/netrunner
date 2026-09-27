# Wave 3 progress

Roadmap: [improvement roadmap](IMPROVEMENT_ROADMAP.md), Wave 3 (real game
feel; the bot plans turns).
Evidence date: 27 September 2026, on the Linux x86_64 development box.

## Branches

Stacked in order on `dev`:

| Item | Branch |
|---|---|
| U3 full catalog | `feat/u3-full-catalog` |
| E2 validator split | `feat/e2-validator-split` |
| A3 turn planner | `feat/a3-turn-planner` |
| U4 layout and interaction | `feat/u4-layout` |

## U3: event timeline, full catalog

The Wave 2 slice covered runs, advances and scores. Every event in the
[UI plan](UI_UX_REDESIGN_PLAN.md) catalog now has a beat, and the snap-mismatch
check covers everything the board shows.

**Events (Match, headless).** `PresentationEventProjector` now also publishes:

| Event | Core source | Notes |
|---|---|---|
| `CardMoved` | `CardMoved`, plus Core's silent moves | Every change of zone, with from and to zones. A search sets cards aside without an event, so the projector follows each card's zone through the action and reports the missing legs. |
| `CardPlayed` | `CardPlayStarted` | The played card and its cost |
| `ClicksChanged` | `ClicksAdjusted`, `TurnClicksAllotted`, `TurnClicksLost` | Totals, so the click pips can refill |
| `DamageDealt` | `DamageDealt` | Kind, amount, cards trashed and the source card |
| `TagsChanged` | `TagsAdjusted` | Delta and total |
| `BreakerPumped` | `BreakerPumped` | The breaker |
| `VirusPurged` | `VirusPurged` | Counters removed |
| `GameEnded` | `GameWon` | Winner and reason |

`CardLeftPlay` is replaced by `CardMoved`. `SubroutinesBroken` names the
breaker, and `CardInstalled` carries its cost.

**Credit attribution.** A credit change names the card that caused it, from
the same action: the install, rez or play whose cost matches a payment, else
a pumped or breaking breaker, an advanced card or a card trashed from a
server or R&D; a gain goes to the played card. Core's credit events don't
carry a source, so this is derived, and a change with no match stays
unattributed.

**Beats.** `BeatPlanner` covers the whole catalog: turn start, draws, credits,
installs, plays, rezzes, advances, scores, runs, approach and encounter,
pumps, breaks, fired subroutines, accesses, steals, trashes and other moves,
damage, tags, purges and the end of the game. To keep playback readable:

- A payment appears on its card's beat ("Runner installs Cleaver · 3
  credits", "Runner breaks 2 subroutines on Whitespace with Cleaver · 2
  credits") instead of as a beat of its own; a played card's gain is added
  to its beat ("Corp plays Hedge Fund · 5 credits · +9 credits").
- Damage names the cards it trashes in one beat.
- Repeated draws or moves by one side read as one beat ("Corp draws 2
  cards", "5 cards go into R&D").
- A successful run ends with "Run complete", since "Run on HQ is successful"
  was already shown before the accesses.

**Pacing.** `BeatPacing` (Match, tested headlessly) sets each beat's length:
the base length divided by the speed setting, plus a hold on results. The
player's own low-emphasis beats (their draws, credits, installs) are brief,
since they just did them. The new **Fast opponent** setting makes the
opponent's low-emphasis beats brief too; runs, scores, damage and results
keep their time. Reduced motion keeps reading time and drops only movement.

**Presenter (Godot).** Beyond the slice's lane focus, ICE emphasis and card
pulses, beats now pulse the credit pools, the clicks and tags line, the
piles and score areas a card moved to, the breaker that broke, and flash a
red wash for damage.

**Auto-pass.** About 70% of decisions are passes. `AutoPassPolicy` decides
which of the player's windows to pass when **Auto-pass optional windows** is
on (the default): windows whose only options are paid abilities, pumps
outside an encounter, or rezzes outside a run. It always stops for an
encounter (breaking, bioroid clicks), an ICE rez, or a rez during a run (for
example an upgrade in the attacked server). **Stop** in the header holds the
next optional window open. Scripted smoke, gate and capture runs keep every
window, as before.

**Presentation audit.** `presentation-audit` replays the fuzz plan's games
through the real coordinator from both seats. The local seat plays
random-legal and the opponent plays as the fuzz case says. It checks every
publication for snap mismatches and every caption for a leaked ID or hidden
card name, and on official content it requires every catalog beat to appear.

**Evidence.**
- **Snap mismatches, official fuzz corpus:** `presentation-audit --content
  official --games 100` replayed the fuzz plan's 100 games from both seats
  (200 seat-games, 187 to the end of the game, 32,392 beats): **zero snap
  mismatches** and zero caption leaks, with every catalog beat present.
  Building it found one gap: Core moves a searched card into its set-aside
  zone without an event, which is now reported as a move.
- **Captures, full scripted games:** `make ui-capture` with
  `--capture-catalog` on expanded presets. The contract agent plays the local
  seat against the Standard bot, as in the client. Seeds were chosen with
  `presentation-audit --game-seeds 1-400 --preset expanded --local contract`,
  which predicted each capture's missing beats exactly.

  | Seat | Seed | Local actions | Beats | Catalog beats missing | Duplicates | Snap mismatches |
  |---|---:|---:|---:|---|---:|---:|
  | Runner | 120 | 54 | 128 | none | 0 | 0 |
  | Corp | 7 | 101 | 401 | none | 0 | 0 |
  | Runner | 42 | 224 | 273 | damage | 0 | 0 |
  | Corp | 42 | 33 | 196 | score, damage | 0 | 0 |

  Contact sheets stay local because they show official card art.
- **Auto-pass:** policy tests for each window shape; in random-legal games on
  both content lanes the player never saw an optional window unless they had
  asked to stop, and a stop request held the next one.
- **Pacing:** `BeatPacing` tests cover the player's own brief beats, Fast
  opponent, holds, reduced motion and speed.

## E2: validator split

The inventory of every validator stage, by tier, with its share of validator
time and why it stays at commit, is in the
[engine plan](ENGINE_ARCHITECTURE_PLAN.md#e2-split-the-invariant-validator-m).
Findings:

- **Validation dominated authoritative Apply.** On the official engine
  benchmark, authoritative Apply took about 810 µs per action against 47 µs in
  Simulation mode, and about 650 µs of that was the validator, which ran
  twice: on the input state and on the output.
- **No stage is a pure audit.** Each can be broken by some single action, so
  none moved to checkpoints. The legal-action checks call the same
  materializers the engine uses to issue actions, but they still catch a
  decision issued before a later change to the state, so they stay.
- **One card-specific exception** (the M5.8 memory-loss suspension for DZMZ
  and Retribution) now lives beside the engine code that creates the frame
  shape, as `GameEngine.IsSuspendedForMemoryLoss`.

**Change: no second validation of the same state.** Authoritative Apply's
input is almost always the output it validated one action earlier. A
`GameState` is immutable and validation depends only on the state and the
catalogs, so a state that passed against the built-in catalog types is
remembered by reference and not validated again
(`StateInvariantValidator.IsCertified`). Other catalog implementations are
always validated in full, and a state built any other way (a forged or
deserialized one) is a new object, so it is always validated.

**Measured** with `engine-bench --content official --games 10 --seed 3`
(4,137 positions, Release, three alternating runs on a quiet machine):

| Build | Authoritative Apply per action | Simulation |
|---|---:|---:|
| Before (U3 branch) | 702, 642, 719 µs (mean 688) | 42 µs |
| E2 | 515, 539, 485 µs (mean 513) | 39 µs |

Authoritative Apply is 25% faster, and the validator runs half as often
(10,734 of 21,480 validations were already-certified inputs). What is left
is the output's validation and the state and event hashing.

**Known-bad corpus.** `KnownBadStateCorpusTests` corrupts real positions from
ten random-legal developer games, one mutation at a time: duplicated IDs,
cards in no zone or two zones, broken zone references, negative counters,
rezzed face-down cards, face-down heap cards, negative credits and clicks,
missing, duplicated or wrongly owned legal actions, dropped or duplicated
resolution frames, a removed run and a won game still awaiting a decision.
The committed manifest records, for each of the 1,487 cases, whether
authoritative Apply rejected it and with which violation codes, as measured
before any optimization; the test requires the same result for every case.

- 1,404 cases are rejected. The 83 accepted are resolution stacks with a
  duplicated top frame (64) or a dropped bottom frame (19): the validator
  doesn't check those shapes today. They are recorded, not fixed here, since
  tightening them could reject legitimate stacks and needs its own review.
- Building the corpus found a crash: a run cursor past the end of its ICE
  zone was reported and then indexed, so Apply threw instead of rejecting.
  It now rejects.

## A3: turn planner

**How the bot plans.** At a turn action with a click left, Corp v5 and Runner
v3 (their Standard profiles) take the heuristic's three best actions. Each is
tried on three states sampled from the bot's observation: it is applied in
Simulation mode, the rest of the turn is played out by both sides'
heuristics, and the position at the end of the turn is scored from the bot's
own observation (`PositionEvaluation`: the score race, money, cards, the
Corp's ICE and advancing agendas, the Runner's rig, tags and damage). A
planned action keeps its heuristic score and gains or loses its rollouts'
difference from the heuristic's own first choice, so planning corrects the
heuristic rather than replacing what the end-of-turn score can't see (run
pressure, information). Easy never plans.

**Information boundary.** A host that holds the state attaches an
`ObservationDeterminizer` to the observation it gives the agent. Its only
capability is dealing samples through the E5 sampler, which never reads a
hidden identity. Unseen opponent cards may be any Gateway card of their side,
since the bot doesn't know the opponent's deck. Planning draws from the
agent's checkpointed random stream. Tests: hidden-state twins (swapped cards
in the opponent's hand or deck) get the same planned action in both seats on
both content lanes, planning repeats from the same checkpoint, and without a
determinizer the planner doesn't run.

**Cost.** A single legal action in a rollout is taken without building an
observation, which cut planning time by about 40% with identical decisions.
Planned turn actions, measured in self-play on a loaded machine: Corp median
30 ms, p95 102 ms; Runner median 60 ms, p95 237 ms (4 samples by 4 actions,
before the cut to 3 by 3). Other decisions are unchanged (p95 under 2 ms).

**Strength so far** (`ai-ladder --entrants standard-w2,standard`, official,
256 games; `standard-w2` is the last policy that doesn't plan):

| Version | Corp | Runner |
|---|---:|---:|
| Rollout value only | −20 [−107, 53] | −41 [−111, 52] |
| Blended with the heuristic | +42 [−31, 123] | +10 [−69, 87] |
| Blended, 640 games | +0 [−44, 57] | +13 [−42, 69] |

The planner does change decisions: in three self-play games it overrode the
heuristic on 25% of the Corp's planned turn actions and 43% of the Runner's.
That it changes this much without changing strength means the end-of-turn
evaluation can't tell better turns from worse ones; more samples or a
different blend won't fix that. Planned turn actions then cost Corp median
21 ms, p95 87 ms and Runner median 28 ms, p95 107 ms (3 samples by 3
actions, on a machine running other jobs), against 50 ms for ordinary
decisions.

**Decision: the planner ships off.** New games keep Corp v4 and Runner v2.
The planner stays as Corp v5 and Runner v3 and the `standard-plan` ladder
entrant, with its tests, so the next attempt starts from working
infrastructure. The A3 gate is not met; see the gate table.

## U4: layout and interaction

- **No boxes or scaffolding.** Server lanes are a faint floor with no border
  and no root or ICE captions; the fields lose their borders; the duplicate
  turn and run line under the header is gone; the hand has no label except
  while choosing discards; the lane's run marker says "Run in progress"
  instead of engine step names.
- **Compact HUD.** Clicks show as pips (●●○), and the Runner's line splits
  over two short lines instead of wrapping.
- **Rig by type.** Programs, then hardware, then resources, with a gap
  between groups.
- **Contextual actions.** Selecting a card or server shows its actions as
  chips beside it. The dock keeps the one-line instruction and the actions
  that belong to no card. "All legal actions…" stays as a quiet dock button
  and F2, as an accessibility and debug fallback.
- **Encounter overlay.** The encountered ICE lists its subroutines beside
  it, each marked broken, fired or "will fire unless broken".
- **Access overlay.** The accessed card opens at the centre of the board,
  with how many accesses have happened.

Evidence: captures at 1920×1080 and 2560×1440 (`NETRUNNER_GODOT_ARGS` now
passes `--resolution` through `play.sh`) of runs, encounters, accesses and a
score, with zero duplicates and zero snap mismatches; `make verify` passes
on the whole stack. The plan's playtest gate (a full game in each seat
without opening "All legal actions…") needs a person.

## Gate status

To be filled in at the end of the wave.
