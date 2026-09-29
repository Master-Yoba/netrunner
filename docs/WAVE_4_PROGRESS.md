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
| A4 run search | `feat/a4-run-search` (on A5) |
| U6 onboarding and playtest loop, U5 sound and motion, A4a extras | `feat/u6-onboarding` (on A4) |
| A8 archetypes and the self-play review | `feat/a8-corp-archetypes` (on U6) |

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

## A3 on A4a and A5: the turn planner returns

The Wave 3 planner (A3) is back with the fitted evaluation scoring its
rollouts: Corp v9 and Runner v8 keep v8's and v7's choices and plan turn
actions (3 samples by 3 candidates), blended with the heuristic as before.

**Gate: a clear ladder gain within the latency budget.** Gateway pool, 480
deals (960 games per seat) against the default Standard, on two seeds:

| Seed | Corp v9 | Runner v8 |
|---|---:|---:|
| 5 | **+50** [21, 78] | +22 [−10, 53] |
| 6 | **+44** [14, 72] | +5 [−25, 34] |

No deck pairing warns; on seed 5 the Corp's gain is largest with Advanced
Yomi against Party Hard (+0.20). In 8 Gateway games the Corp's ordinary
decisions (turn actions, timing and setup) had a p95 of 30 ms against the
50 ms budget (median 0.4 ms, slowest 128 ms), on a machine running a ladder;
run decisions are unchanged (p95 1.5 ms). Runner v8's planned turn actions
cost a median of about 100 ms.

**Decision: Corp v9 ships; the Runner keeps v7.** New Gateway games use Corp
v9, which plans. The Runner planner isn't a separated gain on either seed and
costs more, so it stays a ladder entrant (`standard-plan-fit`). The planner's
model of the Corp in rollouts stays v8, which doesn't plan. The tutorial
decks keep the frozen C1 bot.
## A4: run search

The planner's rollouts (A3) now also search choices inside a run. Each
candidate is tried on states sampled from the agent's observation, played out
by both heuristics until the run ends, and scored there with the fitted
evaluation (A5); the result adjusts the heuristic's score the same way turn
planning does. Two new versions keep their heuristic's choices otherwise and
plan no turn actions, so a ladder against the default measures the search
alone:

- **Runner v9** (v7 plus search): continue or jack out, and trash an
  accessed card or not. 4 samples, the heuristic's best 3 candidates;
  about 50 ms per searched decision.
- **Corp v10** (v8 plus search): rez a card during a run or not (the
  approached ICE, an upgrade or an asset). 4 samples, 2 candidates.

**Gate: Runner v9 is weaker.** Gateway pool, seed 5, 480 deals against the
default Standard: Runner v9 −66 [−96, −32], with per-deck warnings on four
pairings (Party Hard and Planning Ahead against Hyper Velocity and Quick and
Dirty, Stolen Goods against Advanced Yomi). Scoring at the end of the run
undervalues what a run earns later (the Corp's next turn, pressure on the
remote), and the sampled Corp cards come uniformly from every Corp card
rather than a deck's agenda density, so continuing looks worse than it is.
Runner v9 stays a ladder entrant (`standard-run`) and doesn't ship.

**Corp v10 is level.** Same pool and seed: Corp v10 +16 [−13, 44] against
v8, no deck warnings. Not a separated gain, so it doesn't ship either; it
stays the `standard-rez` entrant. Both entrants pin the versions they were
measured against (Corp v8, Runner v7). A next attempt should score the
run's rollouts later than the run's end (at the end of the Corp's next turn)
and sample the Corp's hidden cards from a deck-like mix; Corp v9's planning
and v10's rez search could then be combined.

## A4a extras: central memory and the keep ranking

**Central memory.** The agent's knowledge now records the Corp's public deck
and hand sizes when a successful R&D or HQ run ends (for R&D only when the
access left the top card in place). Runner v10 is v7 plus that memory: R&D
is worth 30% while its top card is one already seen (the Corp hasn't drawn
since), and HQ is worth the share of it the Corp has drawn since the last
access (at least 30%). The memory is an optional field, so older
checkpoints still load.

**Gate: a clear ladder gain.** Gateway pool, 480 deals against the default
Standard (Corp v9 on both sides): Runner v10 **+47** [19, 77] on seed 5 and
**+53** [24, 83] on seed 6, no deck warnings. The decision cost is v7's.

**Decision: Runner v10 ships.** New Gateway games use Corp v9 and Runner v10.
The planner's model of the Runner in Corp v9's rollouts stays v7, so the
shipped Corp plays as measured. `standard-w4a` keeps the earlier defaults
(Corp v9, Runner v7) as a ladder entrant.

**One keep ranking.** Already in place for discards and forced trashes:
both sides rank those by the same card-value function (`CardValue` for the
Corp, `RunnerValues.Card` for the Runner). Only the mulligan keeps its own
rule (economy plus ICE, or economy plus a breaker); no change was made.

## Playtest repairs: Corp v11 (28 September)

A Runner playtest against Quick and Dirty (Weyland) found Corp v9 spawning
remotes with one ICE and nothing behind them, installing Malapert Data
Vault in HQ (the second copy replacing the first), leaving HQ behind a lone
Tithe, never rezzing Archives' Funhouse while the Runner ran Archives for
money, and never advancing Pharos. `ai-review --side corp` (new) replayed
the bot's own decisions and showed the causes:

- The planner's fitted evaluation (v1) gave the plain number of remotes one
  of its largest Corp weights: winning Corps have more remotes, so the
  planner learned that spawning them wins. **Evaluation v2** (feature set 2)
  counts occupied and empty remotes separately; refitted on 400 Gateway
  games it weighs occupied remotes +0.46 and empty ones −0.02 (v1: +0.32 for
  any remote), and beats the hand weights on held-out games (Corp log loss
  0.600 against 0.640, Runner 0.606 against 0.630).
- ICE on a new remote scored the same with or without anything to protect,
  and central ICE had no thinness bonus. **v11:** ICE goes into a new or
  empty remote only while an agenda or asset in hand needs a home (so
  asset-heavy decks still go wide), and HQ and R&D are worth protecting
  until each has two pieces.
- Upgrades had no notion of where they work. **v11:** an upgrade that pays
  off when an agenda is scored from its own root goes only into a remote,
  and a unique upgrade never replaces its own copy.
- An approached ICE's rez was judged by the server's contents alone, as a
  one-off. **v11:** it also counts the runs it denies: the Runner's visible
  success rewards and how often it has run that server successfully.
- Advancing anything but an agenda or trap scored −20. **v11:** advanceable
  ICE is advanced toward its strength threshold on a server worth
  protecting, and an identity that pays for a card's first advancement
  (Built to Last) makes starting one cheaper.

`CorpPlaytestV11Tests` replays the playtest (fixture
`playtest-weyland-remotes-replay.json`) and asks v11 at the six positions
where v9 went wrong; all six fail on v9.

**Ladder:** v11 against v9 on the Gateway pool, 480 deals: −17 [−47, 12] on
seed 5 and +13 [−15, 42] on seed 6, no deck warnings. Level, as expected:
the bot Runner doesn't farm Archives or punish thin centrals, so self-play
can't reward these repairs. **Decision: Corp v11 ships** (not weaker, and it
fixes plays a human exploits); `standard-w4b` keeps Corp v9 with Runner v10
as a ladder entrant.

## A8: archetypes (Corp v12, Runner v11)

Instead of one policy for every deck, each bot plays to its deck's
**archetype**: a profile of plan-level knobs read by the same heuristic,
with a primary and an optional secondary archetype blended 70/30.

| Side | Archetype | What the profile changes |
|---|---|---|
| Corp | Glacier | At most two remotes; three ICE on the scoring remote before an agenda goes in unless it scores this turn |
| Corp | Rush | One ICE is enough for an agenda; early agenda installs behind ICE are worth more |
| Corp | Fast advance | Agendas go in only when they score this turn |
| Corp | Shell game | Up to three remotes; traps are worth more to install and advance as bait; agendas stay unadvanced until the turn they score |
| Corp | Kill | Damage is worth more |
| Corp | Tag punishment | Tags and damage are worth more, and so is rezzing ICE that tags |
| Corp | Horizontal | Up to four remotes; assets are worth more |
| Runner | Big rig | Rig pieces are worth more while its breakers don't cover every ICE type, and it runs less until they do |
| Runner | Aggro | Every affordable run is worth more |
| Runner | Central pressure | R&D and HQ are worth a little more (1.15, 1.1): Gateway Corps score from remotes, and a stronger focus let them |
| Runner | Econ denial | Trashing is worth more |
| Runner | Virus | Viruses are worth more to install |

The Gateway decks declare theirs in `gateway-sample-decks.json`
(`archetypes`), following the roadmap's mapping and the deck authors' notes
on NetrunnerDB: Discretion Advised glacier and fast advance, Advanced Yomi
shell game and horizontal, Hyper Velocity tag punishment and rush, Quick and
Dirty rush and tag punishment; Party Hard virus and big rig, Stolen Goods
aggro and central pressure, Planning Ahead big rig and central pressure. A
deck without a declaration gets its archetypes inferred from its cards
(traps, tag sources and punishments, damage, assets, fast-advance cards,
taxing upgrades, the cost of its ICE, viruses, run events and run payoffs,
multi-access); from their cards alone the HB deck reads as a glacier, the
Jinteki deck as a shell game and the NBN deck as tag punishment
(`CorpArchetypeTests`). The match factory sets the bot's archetypes from its
own deck, which it knows.

**Ladder** (Gateway pool, 480 deals against the previous default, seeds 5
and 6):

| Version | Seed 5 | Seed 6 |
|---|---:|---:|
| Corp v12 (archetypes) vs v11 | **+36** [8, 65] | **+48** [18, 79] |
| Runner v11 (archetypes and the review fixes) vs v10 | −32 [−62, −3] | −32 [−58, −4] |
| Runner v11's fixes alone, no archetypes (v12) vs v10 | +29 [−13, 75] | |

No Corp deck warns; ten of twelve Corp pairings gain on seed 5. The Runner
archetypes lose most against the rush and tag decks (Hyper Velocity, Quick
and Dirty), where a big rig that sets up and an aggro deck that runs into
tags are punished; making the big rig drop its restraint once the Corp has
points didn't help in a 16-game check. The review fixes alone are level,
with two pairings warning (Stolen Goods against Hyper Velocity −0.25,
Planning Ahead against Quick and Dirty −0.23).

**Decision: Corp v12 and Runner v12 ship.** New Gateway games use the
archetype Corp and the Runner with the review fixes and no archetype
profile; Runner v11 (`standard-rarch`) stays a ladder entrant for tuning
the Runner archetypes. After its ladder, Corp v12 also holds an economy
asset's rez until the last windows before its turn (a rez mid-way through
the Runner's turn showed the asset to a Runner who could still trash it, and
auto-pass skipped that window for a human); the asset pays out the same, so
the ladder wasn't rerun.

### Self-play review

`ai-selfplay` (new) plays one bot-versus-bot game and writes its replay and
a readable log of every real choice on both sides with a board summary at
each turn's start. Reviewing ten such games of the archetype bots found:

- **The Runner ran possible lethal traps.** Two games ended in flatlines
  when the Runner, with two cards in its grip, ran a naked remote holding
  an advanced Urtica Cipher. The trap checks since Runner v6 read only
  visible root cards; a face-down card is a null entry whose counters are
  published separately, so they never fired. v11 reads them, and weighs the
  risk by how likely a trap is (15%, or 60% once the Runner has seen this
  Corp play one), with a flatline weighed as a lost game and a hard stop
  after a trap was seen. A first version that treated every advanced card
  as a trap stopped the Runner contesting advanced remotes; against Weyland
  and NBN, whose Gateway decks have no such traps, it won 3 of 16 quick
  games where v10 won 8.
- **The Corp's planner exposed an agenda.** The NBN Corp installed an
  agenda it couldn't score into a new remote without ICE; the planner's
  turn-end horizon can't see the steal. v12's heuristic vetoes it.
- **The Runner stopped running against unrezzed ICE.** Party Hard ran once
  in 25 turns against Hyper Velocity: each unrezzed ICE cost a flat 260 of
  risk, so the Runner never ran and the Corp never rezzed. v11 scales that
  risk by what the Corp can pay to rez and the ICE types its breakers cover,
  and values a run that makes the Corp rez or reveal ICE (19 runs in the
  same game).
- **A client-facing crash.** A Runner program installed with Pantograph
  during the Corp's turn used DZMZ Optimizer's first-install discount, so
  the use sat in both players' turn facts; the view mapper took that for a
  duplicate and threw, which would have stopped the client. Fixed in the
  mapper, with a regression test on the recorded game.

`SelfPlayReviewTests` and `FirstXUsePresentationTests` replay the games at
those positions.

## Playtest follow-up (29 September)

Two Runner games (Stolen Goods against Quick and Dirty and Hyper Velocity)
with written feedback. `ai-review --side corp` on both sessions showed the
causes of the Corp's faults.

**UI and sound:**
- Access all of Archives runs without per-card playback, behind a progress
  bar in the dock ("Accessing Archives… 3/7"); it flashed the dock and
  cards before.
- The access panel's Read card button is gone; clicking the card opens it.
- The draw and install sounds are silent until replaced; the rest stay as
  placeholders.
- The icebreaker panel closes once every subroutine is broken, and
  auto-pass continues an encounter with nothing left to break, which saves
  two of the run's clicks.
- The card hover preview draws above the run panels (it was under the
  icebreaker panel).
- **Beneficial effects resolve on their own:** an optional trigger that
  only gains credits, counters, clicks or cards for the player, with
  nothing to pay, target or choose (Pennyshaver, Conduit, Zahya), is used
  for the player, and a group of only such triggers is ordered for them.
  Triggers with a cost, a target, a choice or a downside still ask. Settings
  has "Resolve beneficial effects automatically" (on), and scripted runs
  keep every decision.
- The hover tilt in the hand is gone; instead a dragged card leans into its
  motion (a tilting copy over the untransformed ghost).
- Damage's red wash, shake and sound play when the cards leave the hand,
  not after the beats queued before them.

**Corp v13** (V12 plus repairs; `CorpPlaytestV13Tests` asks it at the
recorded positions, and all four cases fail on v12):
- Malapert Data Vault was rezzed during a run as "an upgrade in the
  attacked server"; it only pays off when the Corp scores from its server,
  so such upgrades are rezzed in the Corp's turn before a score.
- Public Trail was played with the last click, so the tag was cleared
  before it could be punished; a tag counts for its punishment only when
  the Corp can still punish it this turn (a click and credits to trash a
  resource, or a punishing card it can afford).
- Archives got ICE only on turn 8 while the Runner farmed it; ICE there is
  worth 150 per successful Archives run the Corp has seen (up to 450) plus
  60 per visible run payoff (Pennyshaver, Red Team, Zahya).
- A tagged Runner's resources were never trashed: they were valued as the
  Corp's own cards, which never beat a click and two credits. They're now
  valued by what they earn the Runner (install cost, hosted credits, run
  payoffs, abilities), weighted by the tag archetype.
- The Runner could bait the Corp into rezzing ICE on a server that didn't
  matter and then run the agenda's server; an ICE rez elsewhere now keeps
  the credits to rez the ICE protecting installed agendas.
- Agendas went in as soon as they were drawn, so HQ never held any; an
  agenda that can't score this turn waits in HQ until its server's ICE can
  be rezzed and the agenda finished next turn, unless HQ is flooded (two or
  more agendas or a full hand).

**Ladder:** v13 against v12, Gateway pool: −3 [−35, 25] on seed 5 and +18
[−9, 49] on seed 6, no deck warnings; level, as expected, since the bot
Runner doesn't bait or farm. **Corp v13 ships** as the default;
`standard-w4d` keeps Corp v12 with Runner v12.

**The run UI** needs more than these fixes: a run still spreads over the
breaker panel, the access panel, the dock and the server. The roadmap's U8
proposes one run HUD at the attacked server.

## U6: onboarding and playtest loop

**First-game hints.** `OnboardingHints` (Match) picks at most one hint for
the viewer's own live decision, the first time it offers what the hint
explains: the basics of clicks at the first action window, protecting
servers with ICE, advancing and scoring, rezzing on approach, making a run,
breaking ICE, and accessing cards. The client shows it as a small card in the
table's free top-right corner with "Got it" and "Hide hints". Each hint shows
once; after the first finished game hints turn themselves off, and Settings
has a "First-game hints" toggle to bring them back (with a fresh set).

**Opponent intent cues (optional).** `OpponentIntentCues` reads only the
public board: which of the Corp's remotes carry advancement counters, and an
opponent within two points of winning. They show above the hint, and
Settings can turn them off.

**Preferences.** Hints seen, the hint and cue toggles, and whether a game has
been finished live in `user://preferences.cfg`. Scripted smoke, capture and
gate runs never read or write it.

**Playtest protocol.** The client now logs "All legal actions…", Skip
animation, Stop at next window, and hints shown, dismissed and turned off to
`ui-diagnostics.jsonl`. `netrunner-cli playtest-summary <session dir or zip>`
reads a session and prints the protocol's numbers: time per turn of the local
player, submissions and rejections, drops and invalid drops, card
inspections, and those controls. On the 27 September playtest (Corp,
expanded decks): 13 turns, median 28 s, longest 63 s, 3 invalid drops of 17,
no rejected submissions.

**Verification.** `OnboardingHintsTests` and `PlaytestSummaryTests`; a new
`--onboarding-smoke` in `make verify` starts a Gateway game from the menu as
the Corp, keeps the opening hand, and checks that the basics hint appears at
the first action window inside the viewport, that "Got it" records it and
moves on to the ICE hint, and that "Hide hints" turns hints off and clears
the Settings toggle.

## U5: sound (first part)

Every beat the table plays now has a sound. `SoundCues` (Match) maps a beat
to one of 20 cues: click, credit, card slide, place and flip, rez hum,
advance, score and steal stingers heard from the viewer's side (rising when
the points are the viewer's, falling when they are the opponent's), run
start, encounter, break, subroutine fire, run success and failure, damage,
tag, "your turn", and win and loss. `SoundBank` (Godot) synthesizes every cue
at startup from tones, sweeps and filtered noise, so the game ships no audio
files and needs no license notice. Six voices play at once, and a cue
repeated within 40 ms is dropped. Settings has a Sound toggle and an effects
volume, saved with the other preferences. There is no music yet.

The onboarding smoke checks that every cue is synthesized, lasts under two
seconds and doesn't clip, and that a played action's beats reach the sound
bank.

Motion: a rezzed card now turns over (it narrows to its edge and opens again
with a slight overshoot), and damage shakes the table briefly on top of the
existing red wash, harder for more damage. Both are skipped with reduced
motion, and stopping motion mid-beat puts the card and table back.

Idle life: during a run, the attacked server's lane glows slowly (only the
lane's own background, not its cards), driven per frame so a rebuilt lane
never leaves a stale animation; reduced motion keeps it still.

Still to do in U5: the visual identity (palette, type and table surface,
which want a playtest screenshot review), idle life on the active identity,
and music.

Hover tilt (experimental, for the playtest): the hovered hand card leans up
to 5° toward the pointer, following the pointer's position over the card's
resting footprint, so it holds still under a still pointer. Settings has
"Tilt hovered cards (experimental)", on by default for players; scripted
runs keep it off, so the smokes' straight-card checks still hold, and the
board-actions smoke checks the lean, its stillness and turning it off.

## U5: chrome and neon (visual identity)

The look chosen on the 29 September mockups
([roadmap](IMPROVEMENT_ROADMAP.md#wave-4--polish-and-the-bot-plans-turns-and-searches-runs))
is in the client, in three slices:

- **U5a, foundations.** Rajdhani (display), IBM Plex Sans (body) and IBM
  Plex Mono (numbers and state) are bundled under the OFL and set in the
  theme. `FactionPalette` holds the chosen colours per faction. A
  `TableBackdrop` draws the halves behind the board: the Corp's brushed
  steel tinted to its faction, with a double rule, ruler ticks and a faint
  wordmark along the edge facing the Runner; the Runner's near-black with a
  dot grid and a neon edge. Identity plates show the official Null Signal
  faction glyph (CC BY-ND, `icons/SOURCE.md`) and the faction name; server
  lanes are steel slabs with corner brackets in the Corp's colour, and the
  centrals carry the Null Signal HQ, R&D and Archives icons.
- **U5b, the opponent's hand and the Corp seat.** The opponent's hand is a
  face-down fan peeking from the top edge, a quarter of each card showing,
  driven by the public hand size only. Draws fly into it; installs and
  discards leave from it; a card the opponent plays flies from it to the
  middle of the table, holds face up for half a second, then goes to the
  discard pile, with any cards it draws following. At the Corp seat the
  board is reversed: the server field sits on the Corp's (bottom) edge and
  each server builds upward toward the Runner, root nearest the Corp and
  the server name on the Corp's edge.
- **U5c, interaction motion.** Every button brightens on hover and dips
  when pressed, and pop-up panels ease in. Hovering never changes geometry,
  so hit targets stay put. Reduced motion and Skip animation turn it off.

Evidence (synthetic tutorial decks, no card art):
[Runner seat](evidence/u5/runner-seat-chrome-neon.png),
[Corp seat](evidence/u5/corp-seat-reversed-board.png),
[opponent's played card](evidence/u5/opponent-play-reveal.png).

Still to do: the bypass preview and music (the run HUD, the connection
through the ICE, derezzed ICE and counter shapes landed with U8, below).

## U8: run HUD

A run now lives in one panel beside the attacked server, at both seats
(Settings has a Run HUD toggle, on by default; scripted smokes keep the older
panels so their checks hold, and `--run-hud` forces it on for captures).

- **Runner seat:**
  - **Header and ICE:** the header names the server and where the run is
    (`ICE 2 OF 3 · ENCOUNTER`). The encountered ICE lists its subroutines
    with printed text and status (unbroken, broken, fired).
  - **Breakers:** each breaker appears with its strength against the ICE.
    Matching breakers get their boost and single-subroutine breaks inline.
    Incompatible ones are greyed out with the reason ("can't break
    barriers").
  - **Break all:** it shows a printed-cost quote (`BREAK ALL · 3 ¢`) and
    boosts and breaks one issued action at a time. `BreakAllPresenter` (Match)
    re-plans after every snapshot and stops as soon as no step is issued. Its
    Match test checks that the quote equals what the engine charges.
  - **Footer and access:** the footer holds Continue (or Let it fire while
    subroutines are unbroken) and Jack out. The access panel docks under the
    HUD. Breach choices stay on the board.
- **Corp seat:** the HUD offers Rez for the approached ICE or another card,
  with its cost and Leave unrezzed. It also lists abilities usable now, for
  example a scored agenda's ("Score area · usable now").
- **Board:** the run is drawn as a neon connection from the Runner's edge of
  the attacked server straight through the ICE it has passed to the contact
  point on the current ICE (lit in the Corp's colour), with dim dashes to the
  root. Passed rezzed ICE derezzes: the frame keeps its shape but splits into
  magenta and cyan, with scanlines, slices knocked out of line (they shift now
  and then unless motion is reduced), a burnt aperture the connection runs
  through, pixel shards and a SEVERED tag. Passed unrezzed ICE stays whole,
  marked PASSED. It is drawn the right way up at the Corp seat.
- **Counters:** counters on cards are shape badges: an amber diamond for
  advancement, an acid-green hexagon for virus counters, a gold coin for hosted
  credits and a steel disc for any other counter. The text chips remain in
  tooltips and inspection.

`--run-hud-smoke` (in `make verify`) covers:
- the HUD replacing the panels and the dock's run controls;
- both breakers;
- Break all spending exactly its 3 ¢ quote over three issued actions;
- Continue, and the HUD closing;
- a passed rezzed ICE drawing the connection;
- the Corp seat's Rez and Leave unrezzed.

Not done yet from the mockups: the bypass preview (a card like Fransofia Ward
still offers its bypass through the existing trigger controls), and a warning
chip on the Runner's header for a Corp agenda that can fire during the run.

Evidence (synthetic lab decks, no card art):
[encounter](evidence/u8/run-hud-encounter.png),
[after Break all](evidence/u8/run-hud-broken.png),
[passed ICE derezzed](evidence/u8/run-hud-passed-ice.png),
[Corp seat](evidence/u8/run-hud-corp.png).

### Run HUD soak

Playtests kept finding HUD glitches that hand-built smoke positions missed. `make run-hud-soak`
(`scripts/run-hud-soak.sh [--games N] [--jobs N] [--seed S] [--max-actions N]`) plays whole
Gateway games through the headless client with the HUD on. It alternates seats, each game in its
own launch and several at once. The local seat is played by a seeded, run-happy chooser: it runs
and installs often, picks boosts, breaks, Break all, accesses and the occasional jack out at
random, and presses the HUD's own buttons. On every frame of every run it checks that the HUD:

- stays visible for the whole run (except while Archives' Access all runs);
- changes height at most once at one position of the run, and never goes back;
- never grows wider than 480 px;
- stays inside the table and clear of the history panel.

It also checks that the player's run decision always shows an enabled control once the table
settles, and that pressing a HUD button submits. Violations go to each game's `soak.json` with the
seat, snapshot and action count. The HUD also writes its visibility and height changes to the UI
log, so a playtest zip shows the same.

The soak's first runs caught the height flicker the playtests reported: every frame the HUD asked
for less than its content's minimum size, and Godot enforced the minimum, so it flipped between
two sizes. It also caught wrapped labels that fed their last layout's width back into the HUD's
height. Both are fixed.

## Trigger automation audit

The client resolves the player's trigger decisions that can only help them.
It uses an optional trigger that gains something and asks nothing, and it
orders triggers when none of them can hurt. That decision is made per
**effect kind**, not per card. `BeneficialTriggers.Verdicts` classifies every
`BehaviorInstructionKind` as one of:

- a gain;
- a gain only in some situations: Zahya's per-access credits need an access,
  and a Corp draw must leave R&D a card;
- harmless, or harmless only in a mandatory trigger (an emptied card trashing
  itself);
- the player's decision.

Two tests keep this current as cards arrive:

- **`EveryEffectKindIsClassified`** fails when a new effect kind has no
  verdict, so a new card's effect can never slip silently into "ask" or
  "automatic".
- **`EveryCardTriggerHasAReviewedVerdict`** compares every card trigger's
  verdict with `tests/Netrunner.Match.Tests/Fixtures/trigger-automation-audit.txt`.
  It fails on any new, changed or removed line and writes the new list next to
  the fixture (`.actual`) for review. Accepting it means reading each changed
  line, then replacing the fixture.

## Corp v14: economy assets rezzed at the end of the Runner's turn

Playtest, 29 September (Discretion Advised against Stolen Goods): Corp v13
rezzed Nico Campaign in its own action phase. Nico then sat rezzed, a known
target, through the Runner's whole turn, although it only pays when the Corp's
turn begins. v12 already held such assets back during the Runner's turn until
its last windows. Corp v14 also holds them back in the Corp's own turn. A rez
that pays at once, such as a draw, still goes ahead.

`CorpPlaytestV14Tests` asks both versions at that position of the recorded
game: v13 rezzes, v14 waits. On the Gateway ladder v14 is level with v13
(+11 Elo on both seeds, 240 games each, intervals about ±80), so it is the
default.

## Gate status

| Item | Result |
|---|---|
| C1 Gateway sample decks | Done: seven legal decks, fuzz-clean on the Gateway pool, playable from the menu |
| A1b ladder over a deck pool | Done: a fixed deal budget over the pool, sequential stopping and per-deck warnings |
| U7 main menu | Done: title, New game with Tutorial and Gateway areas, Load, Settings; `--main-menu-smoke` |
| A4a whole-run model | Done: 92% of fully visible forecast runs got in; Corp v8 and Runner v7 ship |
| A5 fitted evaluation | Done: beats the hand weights on held-out games in both seats |
| A3 turn planner on A4a and A5 | **Corp ships** (v9: +50 and +44 on two seeds, p95 30 ms); Runner v8 level, off |
| A4 run search | **Doesn't ship**: Runner v9 −66 (weaker), Corp v10 +16 (level); kept as ladder entrants |
| U6 onboarding and playtest loop | Done: hints, intent cues, `playtest-summary`; placement **needs the playtest** |
| U5 art direction, feel and audio | Partly: synthesized sound, rez flip, damage shake, lane glow, experimental hover tilt; chrome and neon look, faction glyphs, opponent hand fan, reversed Corp seat, UI motion. The run HUD (U8), music and identity idle life are open |
| A4a extras (HQ and R&D knowledge, one keep ranking) | **Runner v10 ships** (central memory: +47 and +53 on two seeds); the keep ranking already exists for discards and trashes |
| A8 archetypes | **Corp v12 ships** (+36 and +48); Runner archetypes (v11) weaker (−32), kept as an entrant; Runner v12 ships the review fixes alone (+29) |
| U8 run HUD | Done: one panel per run at both seats, Break all over issued actions, the run drawn through the ICE; `--run-hud-smoke` in `make verify`. **Needs the playtest** to judge speed against the old panels |

Default bots after Wave 4: Corp v14 (plans turn actions, plays to its deck's
archetype, with the 29 September repairs and the economy-asset rez timing) and Runner v12 (central memory and the self-play review fixes)
on the legal decks; the tutorial decks keep the frozen C1 bot (Corp v7, Runner v6).
