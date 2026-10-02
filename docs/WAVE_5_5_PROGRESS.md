# Wave 5.5 progress — memory and inference (A9)

Plan: [AI opponent plan, A9](AI_OPPONENT_PLAN.md#a9-memory-and-inference-m--l-wave-55).
Evidence date: 2 October 2026, on the Linux x86_64 development box.

## Branch

| Item | Branch |
|---|---|
| A9a tracker, A9b Runner inference, and on | `feat/a9-memory` |

## A9a: the tracker, and its accuracy measured

`CardTracker` (in `Netrunner.AI`) holds one bot's beliefs about the opponent's hidden cards. Each belief is a share of
probability that a card of some definition is in a place: HQ, R&D at a known depth, R&D somewhere, face-down Archives,
or a face-down installed slot.

It's built only from the bot's own observations, after every accepted action:
- **Hand-zone flows:** the bot sees each change, so a face-down install, a face-up play, a face-down discard or cards put
  into the deck move belief out of HQ, and draws move R&D's known top into HQ.
- **Deck order:** a resolving shuffle or search, or cards coming into R&D, loses the order.
- **Slots:** a reveal (rez or access) settles a face-down slot, and returns the guesses that were wrong to HQ.
- **Accesses:** cards accessed in R&D or HQ that aren't public when the run ends stayed where they were.
- **Forgetting:** a movement it can't follow forgets, never guesses.

It works by definition, as a player would, not by card instance, so it can't tell copies apart. A guess scales with the
unknown cards around it: one Tithe seen in an HQ of five, with a third of the deck ICE, makes the next ICE install Tithe at
1 / (1 + 4/3) ≈ 43% (`CardTrackerTests`).

**Tiers:**
- **Easy:** keeps at most 4 cards, and forgets what no public event has confirmed within a turn.
- **Standard:** keeps everything for six turns, and knows the pool (every Corp card of the content, three copies).
- **Hard:** keeps everything for six turns, and knows the Corp's decklist.

Both priors count copies from where they are now, so a card in public view isn't also unseen.

**`ai-belief <games-dir> [--trace GAME[:TIER]]`** replays saved games, feeds the Runner's observations to each tier's
tracker, and scores it against the hidden state at each Runner turn start. On 720 ladder games:

| Tier | Slot log loss (prior) | Slot top-1 (prior) | HQ agenda error (prior) | Tracked-HQ precision | Confident guesses (≥ 60%) right |
|---|---|---|---|---|---|
| Easy | 2.521 (2.617) | 11% (5%) | 0.630 (0.643) | 82% | 79% |
| Standard | 2.470 (2.617) | 14% (5%) | 0.620 (0.643) | 73% | 77% |
| Hard | 1.599 (1.656) | 38% (35%) | 0.627 (0.631) | 73% | 82% |

Every tier beats its prior on every measure, and Easy's forgetting shows. The confident-guess column is from 200 of the
games. `BeliefAccuracyTests` holds the gate on the 29 saved-game fixtures.

A trace found one bookkeeping bug: an agenda stolen during an HQ access was recorded as staying in HQ. That bug was fixed
before these numbers.

## A9b: the Runner's inference (Runner v21, the default)

Runner v21 is v20 with the tracker in its checkpoint, and four uses of it:

- **HQ and R&D:**
  - HQ moves from v10's freshness only when the tracker knows at least half of it. HQ's expected agendas are then set
    against the unseen rate.
  - R&D moves only on a known top card: an agenda triples its value; anything else keeps v10's floor for a seen top,
    which a multi-access run still values.
  - A first cut that replaced freshness everywhere shifted many close HQ calls on little knowledge.
- **Tag risk:** known tag punishers in HQ or on R&D's top raise a tag's risk, when the Corp can pay for them next turn.
  - They're read from declared behaviour, not titles: an operation that needs the Runner tagged and trashes a Runner
    card (Retribution), or an agenda whose score damages a tagged Runner (Orbital Superiority).
  - The risk is what the operation would trash, or the damage on score.
- **Traps:** a face-down root card is a trap as likely as the tracker says (a seen Urtica Cipher installed in a naked
  remote). The rest are priced as before, or from the list for Hard. A tracked trap that would flatline at even odds or
  more stops the run.
- **Confident guesses:** a face-down card the tracker puts at 60% or more is planned as that card:
  - face-down ICE as an unrezzed copy the Corp can rez, priced by the run model;
  - a root card at its value.

Hard knows the Corp's list: `OfflineMatchFactory` gives a Hard bot its opponent's decklist, kept in the checkpoint.

**Saved-game cases (`RunnerInferenceV21Tests`)**, from the user's design notes, each found in a ladder game where v20 and
v21 decide differently:

| Case | What v21 knew | v20 | v21 |
|---|---|---|---|
| A seen Retribution | Accessed in HQ on turn 2, still there on turn 4 (true); the Runner tagged | Runs R&D | Clears the tag |
| A seen Urtica | The remote's face-down root at 69% Urtica Cipher (true) | Runs it | Installs instead |
| A seen Whitespace | HQ's face-down ICE at 69% Whitespace (true), no decoder in the rig | Runs HQ | Takes a credit; the run is blocked once rezzed |

No ladder game had a seen upgrade installed later, so that case has no fixture yet.

**Measured:**

| | Seed 1 | Seed 2 | Seed 3 | Seed 4 |
|---|---|---|---|---|
| Runner v21 − v20 (Standard, 160 games a seat) | +75 [7, 147] | −31 [−104, 23] | +16 [−38, 82] | +30 [−31, 97] |
| Hard Runner v21 − Standard v20 | +60 [4, 140] | −31 [−99, 38] | +62 [−6, 116] | +30 [−43, 103] |

The first two seeds ran before HQ's change was limited to real knowledge.

**Against Claude Sonnet 5.5 as Corp** (the same 24 fixed deals):
- **v20:** won 6 (4 of batch 3's 12, and 2 of 12 fresh deals).
- **v21:** won 8 (4 and 4).

The arms cost about $46 at list price. 14 earlier v21 games, on a version since changed, were voided.

**Decision:** Runner v21 is the default. The user skipped the A9b playtest, to work on until the wave is done.
