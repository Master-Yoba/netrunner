# Wave 4 progress

Roadmap: [improvement roadmap](IMPROVEMENT_ROADMAP.md), Wave 4 (real decks;
polish, and the bot plans turns and searches runs).
Evidence date: 28 September 2026, on the Linux x86_64 development box.

## Branches

| Item | Branch |
|---|---|
| C1 Gateway sample decks | `feat/c1-gateway-decks` |
| A1b ladder over a deck pool | `feat/a1b-deck-pool-ladder` (on C1) |
| U7 main menu | `feat/u7-main-menu` (on A1b) |
| A4a whole-run model | `feat/a4a-run-model` (on U7) |
| A5 fitted evaluation (and A3 on it) | `feat/a5-fitted-evaluation` (on A4a) |

## C1: Gateway sample decks

### Data

The seven System Gateway-only decks from Null Signal Games'
[sample decklists](https://nullsignal.games/players/getting-started-sample-decklists/)
are in `data/decks/gateway-sample-decks.json`, as card codes and
quantities copied from each deck's NetrunnerDB API record (retrieved
28 September 2026), with the published deck names and our own one-line
summaries. The file is separate from `data/m4/presets.json` on purpose: the
frozen M5 manifests pin that file's SHA-256, so changing it would invalidate
every frozen baseline.

`OfficialPresetCatalog` loads both. The teaching decks keep their existing
validation (`Teaching`), and the sample decks (`Gateway`, kind `gateway`)
are checked for structure without metadata, and against the authenticated
metadata for identity, sides, product quantities, minimum deck size,
influence, out-of-faction agendas and agenda points. `DeckValidator` accepts
all seven.

| Side | Deck | Identity | Influence |
|---|---|---|---:|
| Runner | Party Hard | René "Loup" Arcemont: Party Animal | 14 / 15 |
| Runner | Stolen Goods | Zahya Sadeghi: Versatile Smuggler | 14 / 15 |
| Runner | Planning Ahead | Tāo Salonga: Telepresence Magician | 15 / 15 |
| Corp | Discretion Advised | Haas-Bioroid: Precision Design | 15 / 15 |
| Corp | Advanced Yomi | Jinteki: Restoring Humanity | 15 / 15 |
| Corp | Hyper Velocity | NBN: Reality Plus | 15 / 15 |
| Corp | Quick and Dirty | Weyland Consortium: Built to Last | 15 / 15 |

Every Corp deck has 44 cards and 18 agenda points.

### Content IDs

A pairing of named presets is the content ID `corpPresetId~runnerPresetId`.
`RulesFuzzRunner.GatewayContentIds` lists the twelve Gateway pairings, and
`fuzz` and `ai-ladder` take `--content gateway`.

### Rules hardening

The first fuzz over the twelve pairings (360 games) failed 20 times, and a
second round (1,200 games, after the first fixes) 4 times. Six defects,
none reachable with the tutorial decks:

| Defect | Cards | Fix |
|---|---|---|
| A Corp operation's effect raising a trigger that asks for a choice broke the card-play frame rule | Public Trail's tag firing NBN: Reality Plus | The validator lets an executing card-play behavior suspend beneath the effect pipeline its own instruction opened, controlled by the same player; memory-cleanup suspension keeps its stricter check |
| An access trash with a waiting trigger yielded the next breach prompt first, stranding the trigger | René "Loup" Arcemont trashing a card with more to access | As with a steal: the next breach decision waits until the trigger resolves |
| A trigger's own install replacing its source aborted the trigger | Pantograph installing a second Pantograph (a console) | An ability that has reached its own install may find its source in the trash, in the engine and the validator |
| The validator offered a Trojan as a behavior install with no ICE to host it | Pantograph with Tranquilizer in the grip | The validator's install check requires an installed ICE host, as the engine's does |
| An "up to" card selection kept offering more cards past its limit | Longevity Serum, Spin Doctor | Both exact and "up to" limits cap further picks |
| A breaker trashed mid-encounter left its strength boost in the run | Ansel 1.0 trashing a pumped Unity | Run strength adjustments on a card end when it leaves play, and the ICE subroutine cursor no longer writes back the stale run |

Each failing game is a regression test (`GatewayFuzzRegressionTests`).
After the fixes:

| Run | Games | Result |
|---|---:|---|
| Gateway, seed 5 (the seed that failed) | 1,200 | all terminal |
| Gateway, seed 6 | 2,400 | all terminal |
| Gateway, seed 8, differential (Simulation checked against every action) | 600 | all terminal |

### First bot audit

Standard against Standard, 30 games per pairing on the twelve Gateway
pairings and the expanded tutorial decks, counting results and which cards
each bot played, installed, advanced or rezzed. Every game finished; none hit
the action cap. Before the repairs (Corp v6, Runner v5):

| Corp deck | Corp wins | Runner deck | Runner wins |
|---|---:|---|---:|
| Quick and Dirty (Weyland) | 43% | Planning Ahead (Shaper) | 39% |
| Hyper Velocity (NBN) | 61% | Stolen Goods (Criminal) | 46% |
| Advanced Yomi (Jinteki) | 73% | Party Hard (Anarch) | 35% |
| Discretion Advised (HB) | 62% | | |

The card counts showed what the bots didn't understand:

| Finding | Cause | Repair |
|---|---|---|
| NBN never played Public Trail, and Weyland never played Retribution | A tag was worth a third of the Runner's best resource; a choice the Runner makes was valued as if the Corp chose; a trash effect was worth nothing | Corp v7: the Runner's choice is its best option (the Corp's worst), a tag is worth what clearing it costs plus the trash threat, and a trash effect is worth its best legal target |
| Urtica Cipher and Clearinghouse were installed but never advanced, and Clearinghouse never rezzed | Advancing any non-agenda was scored as a lost opportunity, and an asset without income or draw had "no payoff" to rez | Corp v7 advances a trap whose damage grows with its counters, and rezzes one that fires when the Corp's turn begins once it has counters |
| Verbal Plasticity was almost never installed | A resource's value counted only hosted income | Runner v6 values a better basic draw by its future draws |
| Tread Lightly was never played | Its +3 rez cost wasn't valued | Runner v6 values the unrezzed ICE it keeps down. It stays situational: the bot Corp is usually rich enough to pay |
| The Runner walked into advanced traps with too few cards | An advanced unknown card counted only as a possible agenda | Runner v6 weighs its possible damage (2 plus its counters) against the grip, and values trashing an advanced trap by the damage it would deal |

After the repairs (Corp v7, Runner v6), every Corp card is used, flatlines by
Urtica and Clearinghouse are common, and the Runner flatlines less often than
against Corp v7 alone. `GatewayAuditRepairTests` checks the new versions use
the repaired cards more often than the old ones.

On the ladder against Wave 3's pinned versions (`standard-w3`: Corp v6,
Runner v4), with the Corp at v7 and the Runner at v6:

| Pool | Games | Corp v7 | Runner v6 |
|---|---:|---:|---:|
| Gateway (12 pairings, 40 seeds each) | 1,920 | **+36 [9, 67]** | +24 [−6, 53] |
| Expanded tutorial decks (160 seeds) | 640 | **+112 [64, 166]** | −3 [−51, 52] |

A first Gateway run with 14 seeds per pairing (672 games) read +4 and −4,
well inside its noise: 168 games per pairing leave about ±11 games of
spread. That's the case for A1b's fixed budget over the pool. Corp v7 is a
clear gain on both pools, and Runner v6 isn't weaker, so both are the new
defaults; older saves keep their versions.

`scripts/ai-ladder.sh` now passes `--content` through instead of always
adding `--content official`.

## A1b: ladder over a deck pool

`ai-ladder --deals N` plays a fixed budget of deals instead of seeds per
pairing: deal *i* is a seed on the pool's content ID *i* mod the pool size,
and every Corp entrant meets every Runner entrant on every deal, so the
comparison stays paired. A larger pool spreads the same deals thinner rather
than adding games.

- `--batch-deals B --stop-when-clear` plays in batches and stops once every
  seat's adjacent difference is clear of zero at 99% (a wider interval than
  the reported 95%, because each look adds a chance to stop wrongly).
- A two-entrant run reports each deck pairing's paired score difference per
  seat, with a bootstrap interval over its deals, and flags a **warning**
  where the candidate is worse by 14 points or more (about 100 Elo near an
  even score) with the interval below zero. Warnings are diagnostics, not a
  gate.
- Reports carry the pool version (`expanded-v1`, `gateway-v1`); ratings are
  compared only within one.

A 48-deal smoke over the Gateway pool (192 games) ran end to end. On a shared
deal the two versions often play identical games, so many decks show no
difference at all; that's the pairing working, not missing data.

## U7: main menu

The setup scaffold (preset drop-downs, a seed box and developer buttons) is now
the backing form of a game menu (`Main.MainMenu.cs`), so every launch path,
save and smoke that sets decks, side, difficulty or seed still works.

- **Title screen:** Continue (the latest unfinished save, named by side, decks
  and turn), New game, Load game, Settings, Practice board and Quit;
  Developer only with `--developer`. Without the official card data, the
  title explains how to provision it and only the practice board is enabled.
- **New game:** a Gateway decks area (the seven sample decks, with Random
  for the opponent) and a Tutorial area (the Catalyst and Syndicate decks,
  expanded by default). Each deck is a tile with its identity card, name,
  identity and one-line pitch. Then the seat, the opponent's difficulty, and
  Start game, which uses a fresh random seed.
- **Developer:** the original form, for a fixed seed or any preset pairing,
  the metadata gallery and the interaction lab.
- The in-game header controls (new game, skip animation, status) are hidden
  on the menu.

`--main-menu-smoke` (in `make verify`) clicks through the title screen and
both New game areas at 1920 and 2560, and starts Stolen Goods against
Advanced Yomi from the menu, checking the match uses those decks.

## A4a: whole-run model

`RunModel` (in `Netrunner.AI`) plans a run as one path through every piece of
ICE, outermost to innermost, with the credits and clicks available. After
chiriboga's run calculator, as a clean-room design.

- **Subroutine vocabulary.** Each subroutine's effect is read from its typed
  spec: ends the run (or does if the Runner has at most N credits), damage,
  tags, credits lost, pay or take a tag, trash a card (or end the run if
  there's nothing to trash), install ICE, a run restriction, Corp gains. Each
  has a severity: minor, moderate or serious.
- **Crossing a piece of ICE.** Every choice of which subroutines to break is
  priced: a hosted Trojan's counters first, then one matching breaker (pumps
  and breaks) or a bioroid's lost clicks. The rest fire, priced in
  credit-equivalents. The cheapest choice that doesn't end the run or flatline
  the Runner wins; with none, the path is blocked.
- **The path** carries the budget from one piece of ICE to the next, and ends
  with the server's approach toll. Unrezzed ICE is counted, not priced.
- **From the Corp's side**, the same model runs on public information about
  the Runner, with the Corp's own ICE counted as rezzed while its credits last.

**Gate: the model agrees with the engine.** Over 24 Gateway self-play games,
280 runs had every piece of ICE visible when the Runner chose them and a
forecast that it could get in: 258 succeeded (92%), with a mean credit error
of 0.15 on the successful ones; 5 runs the model called blocked were made.
`RunModelValidationTests` keeps this at 85% or more.

**Policies.** Runner v7 costs runs and continuations with the model, and
Corp v8 judges where an agenda is safe with it, in place of counting ICE and
breaker types. The ladder, against C1's versions (`standard-c1`: Corp v7,
Runner v6), 480 Gateway deals each (1,920 games), found two things worth
recording:

- **Ballista.** Its subroutine is "trash 1 program or end the run", and the
  Corp chooses. The first vocabulary read it as a trash the Runner could let
  through, so Runner v7 kept making runs the Corp simply ended, and the
  per-deck report flagged the Runner decks against Quick and Dirty (Weyland).
  It now counts as ending the run, and Diviner's "the run ends if the
  trashed card's cost is odd" as a penalty.
- **Damage that empties the grip** is treated as lethal. An A/B on the same
  seed showed this rule wasn't what moved the results (−17 either way); the
  early +8 to +16 readings were seed variation, which is what the per-deck
  warnings and a second seed caught.

| Gateway, seed 5, after both fixes | Corp v8 | Runner v7 |
|---|---:|---:|
| Elo against `standard-c1` | +13 [−16, 39] | −4 [−29, 24] |

No deck warnings. Corp v8 read +12 to +14 on three seeds; Runner v7 is level.
Neither is weaker, so both ship as the defaults: the model's value is as the
foundation that A5's fitted evaluation and the planner build on, not a
heuristic gain on its own. Older saves keep their versions, and the tutorial
decks keep the frozen C1 bot.

Still to do in A4a: HQ and R&D knowledge (lower the value of a server whose
cards the Runner has already seen) and one ranking of cards worth keeping for
discard, mulligan and trash choices. Both need new tracked state in the
agent's features.

## Tutorial bot frozen

A decision during A4a: the bot on the tutorial decks is frozen at C1's
versions (Corp v7, Runner v6), which the tutorial decks' ladder (+112 for
Corp v7) and playtests stand behind; later bot versions play only the legal
decks. `OfflineMatchFactory.CreatePlayerOpponent` picks the frozen versions
when both decks are teaching decks, and the client uses it for new games.
From here on, ladder gates for bot changes are judged on the Gateway pool,
which `scripts/ai-ladder.sh` now uses by default.

## A5: fitted evaluation

The position evaluation is now fitted to self-play outcomes instead of hand
weights. `EvaluationFeatures` reads 27 features from the observing side's
own view: the score race (and whether either side is within two points),
money on a log scale, cards in hand, installed and rezzed ICE, remotes,
advancement on the board and the Corp's own agenda progress, the Runner's
breaker coverage and rig, tags and core damage, deck sizes, the turn, whose
turn it is, and the whole-run model's view of HQ, R&D and the most advanced
remote (open or not, and what getting in costs). A Corp view prices runs with
its own ICE counted as rezzed while its credits last; a Runner view doesn't
see unrezzed ICE.

`ai-fit-evaluation` plays Standard self-play over the Gateway deck pool, takes each
side's view at the start of every turn, labels it with whether that side
won, and fits one ridge-regularized logistic model per side by deterministic
batch gradient descent. The weights live in `data/ai/evaluation-v1.json`
(embedded in `Netrunner.AI`, checked against the feature list on load), with
its report beside it.

**Gate: the fitted weights beat the hand weights on held-out seeds.** 400
training games (9,611 positions per side) and 120 held-out games on a
separate seed (2,973 positions per side), all on the Gateway pool, with the
run model's grip and Ballista fixes in. The hand evaluation was calibrated
to a probability by a one-feature fit on the same training games:

| Side | Log loss, fitted | Log loss, hand | Base rate only | Accuracy, fitted |
|---|---:|---:|---:|---:|
| Corp | **0.587** | 0.638 | 0.696 | 69.5% |
| Runner | **0.589** | 0.619 | 0.696 | 69.3% |

(An earlier fit over both pools read 0.534 against 0.587; the tutorial decks
are more predictable, and the tutorial bot is frozen, so the shipped weights
are fitted on the legal decks only.)

The largest weights make sense: agenda points and the opponent near 7, the
number of remotes (for the Corp), money, breaker coverage (for the Runner),
and what a run into R&D or the remote would cost.
