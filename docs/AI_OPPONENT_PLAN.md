# AI opponent improvement plan

Status: proposed, not started
Baseline: `corp-heuristic-v2` and `runner-heuristic-v1` (Standard and Easy
profiles), frozen in `data/m5/benchmarks/frozen-profiles-v1.json`
Depends on: [engine plan](ENGINE_ARCHITECTURE_PLAN.md) phases E1 (simulation
`Apply`) and E5 (determinization) for phases A3 and A4 below
Order: see the [improvement roadmap](IMPROVEMENT_ROADMAP.md)

## 1. Where the bots are today

Both policies score each engine-issued action on its own, looking one step
ahead, with hand-tuned integer weights. `OfflineAgent` takes the highest
priority, then the highest score, and breaks near-ties with the seeded RNG.
There's no turn-level plan, no simulation of what happens next and no model of
hidden information. The architecture around the bots is good and should stay:

- They see only filtered observations and choose among engine-issued action IDs.
- They're deterministic, with checkpointed agent state and transactional commit.
- Decision logs record why each action was chosen.
- There's a benchmark harness with paired seeds, held-out splits and
  replay-verified results.

What the evidence says:

| Signal | Value | Reading |
|---|---|---|
| Standard self-play, 1,000 games | Corp 743, Runner 257 | The Runner policy is much weaker, or both are weak in ways that favor the Corp |
| Tactical suites | Corp 39/40, Runner 38/40 | Saturated and tuned with held-out feedback, so they no longer separate good policies from better ones |
| Policy latency p95 | under 0.8 ms against 50 ms and 200 ms budgets | About 60–400× headroom for search within the existing latency contract |
| Share of benchmark time spent in policy | about 10% | The bots are cheap; engine overhead dominates (see engine plan section 2) |

Observed in a live game (25 September 2026, seed 42, Basic vs Basic, human
Runner): the Corp installed ICE and an agenda in a remote on turn 1, rezzed
Whitespace to stop the run, then advanced three times and scored on turn 2.
Its plan was fine. That fast-advance happens to come out of greedy per-action
scoring, but greedy scoring can't reliably find it or avoid its failure cases.

## 2. Diagnosed weaknesses (with code references)

1. **The memory is collected but never used.** `AgentKnowledge` records
   installs, rezzes, advancements and reveals (`OfflineAgentCheckpoint.cs`).
   No policy reads it; `knowledge` is only passed through
   (`RunnerHeuristicPolicy.cs:59`).
2. **The Runner ignores advancement on face-down cards.**
   `RunnerValues.Server` gives an unknown root card a flat 190
   (`RunnerValues.cs:30`), and `RunnerValues.Trash` gives an unknown
   definition 70 (`:45`). A face-down card with three advancement counters is
   the strongest signal in the game, and the bot treats it like any other card.
3. **The Corp doesn't model the Runner's breakers.** Agenda install value is
   `ice count × 110 + upgrades × 170`
   (`CorpHeuristicPolicy.Development.cs:32`). It ignores whether the installed
   breakers already handle those ICE types, and whether the ICE is rezzable
   with current credits.
4. **No turn planning.** Clicks are chosen greedily. Lines like
   install → advance → advance, "click for credits now, run last", or
   "draw until you find the breaker, then run" only happen by accident of
   scoring. `Advance` checks `remaining <= min(clicks, credits)`, but nothing
   plans the whole sequence.
5. **Unknown ICE is a flat penalty.** `Protection` counts unknown ICE and
   applies a fixed risk (260–450). It doesn't use a prior over the Gateway ICE
   the Corp could have, or what has already been seen.
6. **No sense of the score race.** There's no term for "the opponent is at 5–6
   points" (the Runner must pressure, the Corp must protect) or for game
   phase.
7. **The weights are opaque.** There are hundreds of literal integers (80,
   110, 140, 160, 180, 190, 220, 260 ...). They can't be tuned
   systematically and interact in ways nobody can predict.
8. **Easy isn't a designed difficulty.** Easy differs by near-tie tolerance
   and parameter scales. The M5 gate notes that self-play doesn't show Easy
   feels easier to people.

## 3. Principles

- The information boundary never weakens. Search runs only on states sampled
  from the agent's own observation and knowledge (engine phase E5). The
  existing hidden-state twin tests are a hard gate: twins with identical
  observations must give identical decisions for the same seed.
- Determinism stays: all sampling and tie-breaking use the checkpointed
  `DeterministicRandom`.
- Measure before and after every policy version. A new version is accepted on
  ladder evidence (A1), not on tactical-suite scores alone.
- Latency stays within the existing budgets (50 ms ordinary p95, 200 ms run and
  nested choices p95) on the recorded reference hardware.

## 4. Work breakdown

Sizes: S is a few days, M is one to two weeks, L is several weeks.

### A1. Evaluation that can tell policies apart (M) — do this first

- **Rating ladder.** Keep a frozen anchor pool: contract v6, `corp-heuristic-v2`
  and `runner-heuristic-v1` Standard, both Easy profiles, and a random-legal
  agent (engine E3). Each candidate plays paired seeds in both seats against
  every anchor. Report an Elo or Bradley–Terry rating per seat with confidence
  intervals, using the existing `BenchmarkSuites.Compare`.
- **Sequential stopping.** Use SPRT-style early stopping so comparisons end
  once the result is clear, instead of always running 1,000 games.
- **Per-side reporting.** Always report Corp and Runner strength separately.
  The 74/26 split hides which side is weak.
- **Human game corpus.** When a human plays, save the private decision log and
  final replay (opt-in, local only; the M5 logs already support this). Build a
  small review tool: step through a human game with the bot's evaluation of
  each position next to it. This is where most new weakness reports will come
  from.
- **Tactical suite v2.** Author new positions separately from any tuning, in
  new categories: advancement inference, score-race pressure, turn sequencing,
  breaker-coverage-aware installs, and deck-out and hand-size danger. Keep
  held-out positions truly held out; nobody looks at them until a release
  evaluation.

Gate: the ladder separates the current Standard from Easy, and each from
random-legal, with non-overlapping intervals per seat.

### Tutorial bot frozen (Wave 4 decision)

The tutorial decks (the Catalyst and Syndicate, basic and expanded) are for
learning the game: the bot there has to be playable and beatable, not ever
stronger. So their opponent is frozen at C1's versions (Corp v7, Runner v6),
whatever the current defaults are (`OfflineMatchFactory.CreatePlayerOpponent`),
and saves keep their recorded version as always. Bot tuning targets the legal
decks: the ladder defaults to the Gateway pool, and ladder gates for bot
changes are judged there. The tutorial decks stay a regression check (games
finish, the frozen bot and saves work) and the frozen M5 suites keep their
own pinned versions.

### A1b. Ladder over a deck pool (M) — with C1 in Wave 4

The ladder today plays every entrant pairing on four preset combinations
(tutorial starter and expanded, each seat) with the same seeds for every
entrant: 640 games for two entrants at 40 seeds, about eight minutes on six
workers, and intervals of about ±50 Elo. Crossing the pairings with twelve
Gateway deck pairings at the same seeds would triple that, and every later
card set would multiply it again. A deck matrix doesn't scale, so:

- **A fixed game budget, not seeds per pairing.** How precisely a rating is
  known depends on the number of games, not on how many decks they're spread
  over. Plan a budget (say 1,280 games) and assign deck pairings to seeds
  round-robin, so each pairing gets an equal share and the rating is the
  average over the pool. Adding decks then costs no time; it only spreads
  the same games more thinly.
- **Common deals.** Every entrant plays each seed with the same deck pairing
  and the same shuffles, as today, and differences use the paired
  bootstrap. This cancels most of the deal and deck variance.
- **Sequential stopping.** Implement the SPRT-style stopping planned in A1:
  test "the candidate is at least as strong as the baseline" in batches and
  stop once the result is clear. Most broken changes fail within a few
  hundred games; only close calls use the full budget.
- **Tiers.** A *screen* (a small budget with sequential stopping) for every
  experiment, a *gate* (the full budget, fixed seeds) before a policy
  version ships, and *targeted* runs for card-specific changes, limited to
  decks containing that card, plus a small run over the whole pool to check
  for regressions.
- **Per-deck warnings, not per-deck gates.** Report each deck's result
  (from the games that deck appeared in) and flag any deck where the
  candidate is worse by a wide margin (for example, more than 100 Elo with
  the interval clear of zero). With many decks, precise per-deck ratings
  would need the full matrix again, so they are diagnostics only.
- **A versioned pool.** The ladder's deck pool is versioned like a policy.
  A new deck joins at a pool version bump, and ratings are only compared
  within one pool version. The tutorial decks stay the frozen M5 benchmark.
- **Faster games.** Wall time is per game. Profile a full ladder game before
  any bigger pool (validator, observation and view building are the
  candidates), and keep games single-threaded so the workers scale with
  cores.

When more cards arrive (Elevation, then Startup), the pool can grow to
dozens of decks at the same budget. At that size, add deck strength to the
rating model (a Bradley–Terry model with deck terms), so a strong deck's
wins aren't credited to its pilot, and sample the pool weighted by how often
decks are played rather than uniformly. Decklist sampling for hidden cards
(A3) already assumes the bot knows which list its opponent plays; with a
pool, the host passes the chosen list, as with presets.

### A2. Fix the heuristics without search (M)

These are cheap and independently measurable. Each is a policy version bump
with ladder evidence.

- **Advancement inference (Runner).** Value an unknown remote root card by
  its advancement counters and turns-since-install, using remembered
  `AgentKnowledge` history ("installed on turn T, advanced twice since").
  Face-down plus advanced plus protected should rank close to a known agenda.
- **Use memory.** Remember cards accessed in remotes and HQ, and treat
  known-trashable economy assets as targets. The Corp remembers which ICE the
  Runner has already broken, and at what cost.
- **Agenda density.** Estimate the agenda points left in R&D and HQ: the
  deck's total (a preset constant) minus scored, stolen and seen agendas,
  divided by the cards remaining. Use it to value central runs.
- **Breaker coverage (Corp).** When choosing where to install an agenda or ICE,
  score each server by the Runner's actual cost to get in, reusing the
  `RunnerEncounterPlanner` cost model from the Corp's point of view on public
  information.
- **Score race.** Add a game-phase term from both sides' agenda points and
  turn number. Near 7, raise the Runner's value on centrals and on remotes that
  are being advanced; the Corp raises its protection and rez-reserve weights.
- **Named, documented weights.** Move literal weights into a versioned profile
  record with names (`CreditValueLow`, `UnknownIceRisk`, ...), serialized with
  the configuration digest. No change in behavior; this prepares for A5.

Gate: each change moves its own side's ladder rating up without lowering the
other side's, and the Runner's rating against the frozen Corp improves
noticeably. The target is Standard self-play with the Runner winning 40–60%,
after checking the preset matchups themselves aren't lopsided.

*Wave 1 outcome* ([record](WAVE_1_PROGRESS.md)): Runner v2 gained +102 Elo
[66, 145] over the M5 Runner, mostly from seeing public advancement counters
on face-down cards, which the observation hadn't exposed at all. Corp v3's
breaker-coverage and agenda-safety terms didn't measurably change Corp
strength, so the Corp's next gains are expected from A3. Card memory for
hidden cards wasn't possible: they have no ID in the observation.

### A3. Turn planner (L) — needs engine E1 and E5

The main structural upgrade for turn-action decisions:

- When the bot gets a `TurnAction` decision with clicks left, enumerate
  sequences of its own turn actions with a beam search (width k, depth up to
  the remaining clicks). Apply them in `Simulation` mode on a determinized
  state (E5). Every zone hidden from the acting side is sampled, including
  its own R&D or stack order: the Corp can't see its own R&D, so a planner
  that kept the real order would use future draws and break the hidden-state
  twin guarantee.
- Score the end-of-turn state with a static evaluation built from the A2
  features: credits, cards, board, agenda progress, protection margins and
  the opponent's threat.
- Runs inside the plan are summarized with the existing encounter cost model
  instead of simulating every nested decision, at least at first.
- Execute the first action of the best sequence and re-plan after every
  accepted action, because new information (a rez, an access) changes the
  plan. Cache the plan in `AgentObjective` so re-planning is cheap when
  nothing has changed.
- Budget: with E1 at under 0.5 ms per simulated `Apply`, a beam of 8 × depth 4
  × 8 samples is about 256 applies, around 100 ms. Tune to the 50 ms p95
  budget, or ask for a larger budget for turn-start decisions only and
  document it.

Gate: the ladder shows a clear gain in both seats over A2, and the new
turn-sequencing tactical positions pass.

*Wave 3 outcome* ([record](WAVE_3_PROGRESS.md#a3-turn-planner)): the
planning infrastructure works (a determinizer attached to the observation,
twin-safe deterministic rollouts, Corp v5 and Runner v3, the
`standard-plan` ladder entrant), but over 640 ladder games it was level with
the non-planning Standard in both seats. It changed a quarter to two fifths
of turn actions, so the end-of-turn evaluation couldn't tell better turns
from worse ones. It ships off and moves to Wave 4, where it is rebuilt on the
run model below (A4a) and a fitted evaluation (A5).

**Wave 4 revision.** Score the end of a planned turn with terms the run model
supplies instead of card counts:
- per server, the Runner's cheapest cost to get in (clicks and credits) and
  whether it can get in at all this turn or next;
- per server, its potential: known or inferred agendas, advancement, trash
  targets, and for R&D and HQ the agenda density and how much is already
  known;
- the score race, money after reserves (rez reserve for the Corp, break
  reserve for the Runner) and hand quality.

The heuristic blend from Wave 3 stays, so planning corrects the heuristic
rather than replacing it.

**Deal hidden cards from the opponent's decklist.** In solo play the
opponent always plays a known preset, so sample its unseen cards with
`OpponentCardPool.Decklist` (the preset list minus every copy already seen)
instead of `AnyOf` over every Gateway card of its side. `AnyOf` gives, for
example, an R&D far richer in agendas than any real deck, which skews run
values. Using the decklist is an information choice, not a rules one: a human
facing a preset can know its list too. Record it in the profile
(`opponent-pool`: `decklist` or `any`) so it's part of the configuration
digest, and let difficulty set it: Standard and Hard use the decklist; Easy
uses `any` or no sampling at all. A host passes the preset's list with the
determinizer; the agent never reads it from the authoritative state.

### A4a. Whole-run model (M) — first in Wave 4

The Runner's encounter planner handles one encounter at a time. A run is a
path through every piece of ICE, so plan it as one:
- Enumerate how to get through a server's ICE, outermost to innermost, with
  the clicks and credits available: which breaker handles each piece, pumps
  and breaks, which subroutines to let fire, and where jacking out is the best
  exit. Take the cheapest complete path; a path that fires a serious
  subroutine (end the run, trash a program, install more ICE) is avoided when
  there is another.
- Describe each subroutine's effect in a small typed vocabulary (net damage,
  tag, end the run, lose credits, pay credits, a minor, moderate or serious
  effect) derived from the behavior catalog, not from card text.
- Unknown ICE uses a prior over the ICE the Corp could have, with the Corp's
  credits as the rez budget.
- Cache the result per server for the decision, and invalidate it when the
  board, credits or clicks change.

The same model serves both sides: the Runner uses it to pick runs and
installs, and the Corp uses it from the Runner's point of view (public
information only) to judge where an agenda is safe, replacing the A2
breaker-coverage estimate.

Also from this pass:
- **HQ and R&D knowledge.** Track how much of HQ and the top of R&D is known
  from accesses, reveals and draws since, and let it lower the value of
  running a server the Runner already knows.
- **Cards worth keeping.** One ranking of hand cards by current need
  (missing breaker types, economy, the next install) for discard, mulligan
  and trash choices.

Design note: [chiriboga](https://github.com/drbo6/chiriboga) (a JavaScript
Netrunner solo bot) inspired this pass, particularly its run calculator, its
per-server cost and potential caches and its HQ knowledge estimate. It is
GPL-3.0, so this is a clean-room design from its ideas; no code is copied.

### A4. Determinized search for runs and responses (L) — needs E1 and E5

- **Run decisions** (whether to run, where, when to jack out, what to break,
  what to trash): perfect-information Monte Carlo. Sample N states consistent
  with the observation, roll each candidate forward through the run with the
  A2 policy as the default for both sides, and average the evaluation.
  Upgrade to information-set MCTS (ISMCTS) only if PIMC's known weaknesses
  show up in review.
- **Corp responses** (rez or not on approach, how to pay for and order
  triggers): the same method, sampling the Runner's grip and stack.
- The opponent model in rollouts is the A2 heuristic. Keep this configurable
  so Easy can use a weaker model.
- Decisions stay fully determined by the seed. Search results never leave the
  agent. Samples are Simulation-provenance states and can't be serialized
  (engine E1).

Gate: the ladder shows a gain, hidden-state twins stay identical, and p95
latency is within budget.

### A5. Tune the evaluation (M)

- With named weights (A2) and a fast ladder (A1), tune the static evaluation by
  self-play: CMA-ES or coordinate descent over the weight vector against the
  anchor pool, per side. Check against a held-out seed set.
- Fit the static evaluation to game outcomes from self-play (logistic
  regression on the A4a and A2 features), rather than leaving it hand-tuned;
  this is what A3's planner needs. Self-play is cheap with Simulation mode and
  the ladder, and every game is fully observed. Keep the model small, linear
  or shallow, and deterministic, with weights stored as a versioned JSON file
  under `data/`. Hold out seeds, and check the fitted evaluation against the
  hand weights on the ladder.
- **Expert iteration.** Once A3 or A4 search beats the heuristic on the
  ladder, use its choices and its search values as training targets: they
  carry more signal per position than a game's final outcome. Each round
  refits the evaluation on self-play of the current best version, then must
  beat the previous round on held-out seeds before it replaces it; a round
  that doesn't is discarded. Keep the targets, the seeds and each round's
  weights under `data/` so every round can be reproduced.

Gate: the tuned weights beat the hand weights on held-out seeds.

### A6. Difficulty and personality (S → M)

Wave 0's head-to-head ladder found Easy and Standard statistically
indistinguishable in both seats (960 games). A first distinct Easy (A6a) is
therefore scheduled in Wave 1 using today's knobs; the compute-based tiers
below still depend on A3.

- **Difficulty from compute and knowledge, not noise.** Easy uses fewer
  samples, a shallower beam and the `Empty` opponent-pool policy. Standard
  uses `GatewayPool`. An optional Hard level uses more samples and the planner
  on every decision. Noise from near-ties stays small and only picks among
  genuinely equal options.
- **Personalities** as presets on the fitted evaluation (A5), after it
  exists, rather than on the heuristics' literal weights:
  - Corp: "glacier" (taxing ICE, slow scoring), "rush" (early agendas) and
    "asset spam".
  - Runner: "aggressive" (runs early and often) and "rig-builder".
  A personality shifts a few named evaluation terms (score-race urgency,
  protection margin, run pressure, economy) from the fitted values, so its
  style comes from what it values, and the planner and search keep playing
  it well. Each is validated on the ladder so it stays within one rating band
  of its difficulty, and a style check confirms it plays differently (for
  example "rush" scores its first agenda earlier than Standard).
- **Human-facing pacing is a UI concern** (see the
  [UI plan](UI_UX_REDESIGN_PLAN.md) phase U3). The bot still decides
  instantly, and the presentation shows its actions at readable speed.

Gate: human playtest notes confirm Easy, Standard and Hard feel ordered, and
the ladder shows the same order.

### A7. Formats and deck-aware play (with the next card set)

One bot for every format, with each format's tuned data kept separate. The
policies reason from each card's behavior definition, so a new card is
understood once its behavior exists; the heuristics, the whole-run model
(A4a) and the planner (A3) stay one codebase. Separate bots per format would
multiply every repair and drift apart. What does change with the card pool
is data:

- **Per-format parameters and weights.** The fitted evaluation (A5), the
  heuristics' named scales and the hidden-card priors (what an unrezzed
  piece of ICE or an advanced card probably is) are fitted per deck pool and
  versioned with it: for example `data/ai/evaluation-gateway-v1.json`,
  `evaluation-startup-v1.json`. A game loads the file for its format (the
  ladder's pool version, A1b) and falls back to a fit on all pools when none
  exists. The frozen tutorial bot is the first case of a pinned setup for one
  pool.
- **Split only where the data says so.** Formats are mostly nested (Gateway
  within Startup within Standard). When a pool is added, fit once on all
  pools and once per pool, and compare them on held-out games of each; keep
  a per-pool fit only where it is clearly better. `ai-fit-evaluation` and the
  deck-pool ladder make this a routine check, and each refit is an ordinary A5
  round (it must beat the previous weights on held-out seeds).
- **Deck-aware features.** Within any format, decks want different plans: a
  Jinteki trap deck bluffs with advanced cards, an NBN deck advances fast
  and punishes tags, a Criminal deck pressures HQ with run events. Rather
  than a bot per deck, give the evaluation features that describe the bot's
  own deck and the opponent's known list: its archetype or a few measured
  traits (share of traps, tag punishment, ICE density, agenda sizes, economy
  type), taken from the decklist the bot already knows (A3's decklist
  sampling). The fit then learns how each plan values the board, the
  heuristics can read the same traits, and A6's personalities stay a
  separate, deliberate layer on top.

Gate: on held-out games of each pool, the chosen per-format and deck-aware
fits predict outcomes at least as well as one general fit, and the ladder on
each pool shows no deck warning against the previous version.

### Human game data (deferred)

Jinteki.net has a replay format but no public replay source, games there are
almost all Standard (a few Startup), and only games whose every card we
implement can be replayed in our engine. Donated replays wouldn't reach a
useful volume. So human data from Jinteki waits until at least Startup
content is implemented. Until then the local, opt-in human corpus (A1) is for
review, and self-play (A5) is for fitting. If this is picked up later, ask
the Jinteki maintainers first rather than scraping spectator sessions:
spectators don't see hidden information, and the players haven't agreed to
having their games collected.

## 5. Sequencing

```text
A1 ladder + human corpus ──> gates everything below
C1 Gateway decks + A1b deck-pool ladder ──> first in Wave 4; the rest is measured on the pool
A2 heuristic fixes ────────> A5 tuning
E1 + E5 (engine) ──> A3 infrastructure (Wave 3)
Wave 4: A4a run model ──> A5 fitted evaluation ──> A3 planner on A4a + A5 ──> A4 run search
A6 difficulty/personality after A3 (needs compute knobs)
A7 per-format data and deck-aware features with the next card set (needs A1b pools and A5 fitting)
```

A1 and A2 need no engine work and can start immediately. A3 is the largest
single strength gain. A4 adds most for the Runner.

## 6. Verification for every policy version

- A new `PolicyVersion` string, with the configuration digest covering the
  weights.
- Ladder report per seat with intervals; the paired comparison against the
  previous version and the frozen anchors.
- Tactical v1 regressions don't get worse, and tactical v2 results are
  reported.
- Hidden-state twin suite passes and performance gate passes with the updated
  budget table.
- A 1,000-game Standard suite completes with zero caps or failures, and all
  replays are verified.
