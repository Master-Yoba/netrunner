# Wave 3 progress

Roadmap: [improvement roadmap](IMPROVEMENT_ROADMAP.md), Wave 3 (real game
feel; the bot plans turns).
Evidence date: 27 September 2026, on the Linux x86_64 development box.

## Branches

Stacked in order on `dev`:

| Item | Branch |
|---|---|
| U3 full catalog | `feat/u3-full-catalog` |

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

## Gate status

To be filled in at the end of the wave.
