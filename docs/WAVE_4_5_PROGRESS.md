# Wave 4.5 progress — automated playtesting

The plan is in `IMPROVEMENT_ROADMAP.md` (Wave 4.5). Bot work was bottlenecked on human playtests: a
session finds a handful of faults, and the ladder can't see them, because it only measures wins and
both seats run the same heuristic.

## P1 Oddity lints

Deterministic detectors over saved self-play games.

- **Saving games:** `scripts/ai-ladder.sh ... --save-games <dir>` saves each game with its deal,
  both entrants, the result and the replay (`LadderGameArchive`).
- **Running the lints:** `scripts/ai-lint.sh <dir>` (or `make ai-lint LINT_ARGS=<dir>`) replays each
  game and reports, per lint, how often each entrant made the play, per game on that side.
  - `--examples N` lists the first findings, and `--json` writes them all.
- **Full state:** the lints read the full state, since nothing is hidden offline, so they can check
  what a bot couldn't see (whether R&D's top card is the one the Runner already accessed).

| Lint | Side | Looks for |
|---|---|---|
| `ice-on-empty-remote` | Corp | ICE on a remote still empty at the end of the Corp's turn, with no agenda or asset in HQ to put there (a glacier stacks its remote first) |
| `upgrade-cannot-fire` | Corp | An upgrade that works only when an agenda is scored from its server, installed in a central |
| `unique-replaces-own-copy` | Corp | A unique card installed over its own copy |
| `unrezzed-ice-runner-profits` | Corp | Approached ICE left unrezzed, though offered, on a server the Runner has already run successfully twice |
| `advanceable-ice-idle` | Corp | Advanceable ICE installed for three turns or more without a counter |
| `archives-farmed` | Corp | The Runner's third successful run on an Archives without ICE, with ICE in HQ to put there |
| `central-open-remotes-grow` | Corp | HQ or R&D without ICE at the end of a Corp turn while two or more remotes hold cards |
| `counters-on-inert-card` | Corp | Advancement counters placed on a card that is neither an agenda nor advanceable |
| `own-agenda-trashed-by-install` | Corp | The Corp trashed its own agenda by installing over it (can be a deliberate denial; triage it) |
| `archives-shuffle-empty` | Corp | An Archives-into-R&D shuffle (Spin Doctor) used with Archives empty |
| `rd-rerun-top-seen` | Runner | R&D run again while its top card is the one the Runner already accessed there this turn |
| `runner-repeats-known-trap` | Runner | The same installed trap (damage on access) accessed a second time |

`OddityLintsTests` ties four of the lints to the recorded playtest games that showed them:
- counters on Palisade (Seamless Launch);
- Offworld Office trashed by Ansel's install;
- Spin Doctor used with Archives empty;
- Urtica Cipher accessed again.

A saved game reads back and lints identically.

### Gate evidence

**Corpus 1:** a Gateway ladder of `standard-w4a` (Corp v9 and Runner v7, the bots before the
28 September repairs) against `standard` (Corp v18 and Runner v13), 240 games on seed 1. Findings
per game on each side:

| Lint | Corp v9 / Runner v7 | Corp v18 / Runner v13 |
|---|---|---|
| ICE on an empty remote | 1.683 | 0.017 |
| Upgrade that can't fire | 0.142 | 0 |
| Central open while remotes grow | 0.108 | 0 |
| Advanceable ICE idle | 0.375 | 0.067 |
| Archives farmed | 0.067 | 0.042 before the ICE-in-HQ condition; 1 game with ICE in HQ |
| Own agenda trashed by an install | 0.225 | 0.017 |
| Counters on an inert card | 0.067 | 0.025 |
| R&D re-run, top card seen | 0.242 | 0 |
| A known trap accessed again | 0.333 | 0 |

The lints find the 28 September playtest's faults in v9's games: empty remotes, an upgrade in the
wrong server, an Archives farmed, advanceable ICE never advanced. They also find the Runner faults
from the 30 September playtests in Runner v7's games (a known trap accessed again) and none in
Runner v13's.

**Faults they found that no playtest had reported,** both fixed in Corp v19:
- **Seamless Launch with nothing to aim at.** Corp v18 played it with no agenda or trap installed and
  put its counters on Nico Campaign or ICE. The planner's fitted evaluation counted any counter in a
  remote as progress. Feature set 3 keeps set 2's weights and counts only counters on agendas and
  advanceable cards; v19 plans with it.
- **An agenda installed into the server being run.** Corp v18 used Ansel 1.0 to install an agenda over
  its own agenda in the remote being run, where the Runner takes the new one instead.

`CorpLintV19Tests` asks v18 and v19 at both positions, in saved ladder games kept as fixtures
(`Fixtures/saved-games`).

**Corpus 2:** `standard` (Corp v18) against `standard-c19`, 240 games. v19 has no
`counters-on-inert-card` findings (v18 had 2) and no agenda installed into the server being run.
Its remaining `own-agenda-trashed-by-install` findings are v17's deliberate denial: the Runner had
no click left for Archives, and the Corp gains a free asset for the agenda the Runner would take anyway.

**Ladder:** Corp v19 is level with v18 on the Gateway ladder (−11, +21, 0 and 0 Elo on four seeds,
240 games each), so it is the default.

**A detector fix found on the way:** `unrezzed-ice-runner-profits` found nothing at first, because the
engine offers the rez of approached ICE as an ordinary card rez, not as the run-ICE rez. It now fires
at a low rate: 0.067 per game for Corp v19, and 0.008 for v9.

**Still open:**
- **`advanceable-ice-idle`:** Pharos left unadvanced (0.05 per game for v19). The heuristic advances it
  only when that leaves its rez reserve intact, so this is left for the LLM reviewer (P2) to judge.
- **`unique-replaces-own-copy` and `archives-shuffle-empty`:** they found nothing in either corpus.
  `archives-shuffle-empty` fires on the Spin Doctor playtest game. `unique-replaces-own-copy` has no
  recorded game to test on yet.
