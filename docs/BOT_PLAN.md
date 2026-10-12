# Bot plan: search first (October 2026)

Status: **authoritative** for all bot (AI opponent) work from 4 October 2026. Written on the user's direction after
bot loops 1 and 2 and bot strategy 1. The step-by-step implementation plan is [BOT_PLAN_STEPS.md](BOT_PLAN_STEPS.md).

> **The search-first research closed on 9 October 2026** (section 10). Its instruments stay; the leaf, the apprentice
> and expert iteration are set aside; one bounded experiment decides Corp Hard's play-out planner; the main track is
> the heuristic, improved concept by concept from [STRATEGY_FUNDAMENTALS.md](STRATEGY_FUNDAMENTALS.md). Sections 1 to
> 9 are the record of the research and the instruments' definitions.

This plan supersedes, for new work:

| Document | New status |
|---|---|
| [AI_OPPONENT_PLAN.md](history/AI_OPPONENT_PLAN.md) (A1–A9) | Record of what was built through Wave 5.5. Its open items (A4 run search, A5 expert iteration, A9d) continue here, redesigned. |
| [BOT_IMPROVEMENT_PLAN.md](history/BOT_IMPROVEMENT_PLAN.md) | Closed. LLM review loops are no longer the improvement engine. |
| [BOT_LOOP_WORKFLOW.md](history/BOT_LOOP_WORKFLOW.md) | Kept as the how-to for an outside-player arm, run only when the user asks (section 7). Its section 7 verification order is replaced by section 6 here. |
| [IMPROVEMENT_ROADMAP.md](history/IMPROVEMENT_ROADMAP.md), AI track | The AI track continues here; the roadmap's A* entries are history. |

[BOT_LOOP_1.md](history/BOT_LOOP_1.md), [BOT_LOOP_2.md](history/BOT_LOOP_2.md) and [BOT_STRATEGY_1.md](history/BOT_STRATEGY_1.md) stay as
records; their confirmed findings become puzzles (section 5.3).

## 1. Diagnosis

### 1.1 Where the bots are

The defaults are Corp v30 and Runner v26, Standard profile. The heuristic is a one-step greedy scorer: every
engine-issued action gets an integer `(Priority, Score)` from hand-tuned literal weights, and the highest priority then
the highest score wins (`OfflineAgent`, `RunnerHeuristicPolicy`, `CorpHeuristicPolicy`). The turn planner
(`TurnPlanner`) takes determinized samples of the top heuristic candidates, rolls each out to the end of the turn by
both sides' heuristics, scores the result by a logistic "fitted evaluation" of win probability (`FittedEvaluation`), and
blends that into the heuristic score. Who plans (corrected by S0.1's profile; the profiles in `CorpPolicyProfiles` and
`RunnerPolicyProfiles`):

| Seat | Standard | Hard |
|---|---|---|
| Corp v30 | Planner, 3 samples × width 3, set 4 | The same planner, dealing samples from the tracker and the Runner's list |
| Runner v26 | Heuristic only | Planner, 4 samples × width 3, set 3 |

Runner Hard is about 80 Elo above Standard; Corp Hard is level with Standard at every planner setting tried, and its
default differs from Standard only in how samples are dealt.

On the Core Battle Box (the pool human players meet), Standard's Runner wins 31% against Standard's Corp.

### 1.2 Why the loops stopped paying

Bot loops 1 and 2 (12 Sonnet games each, Codex reviews, fixes in a batch) found real engine bugs and single-card
misvaluations, and those fixes are right. But the ladder could not see them:

| Change | Core Battle Box Elo (240 deals) |
|---|---|
| Runner v25 against v24 | +6 [−28, 49] |
| Corp v29 against v28 | +12 [−23, 54] |
| Runner v26 against v25 | +3 [−35, 52] |
| Corp v30 against v29 | +50 [10, 96] |

A 240-deal ladder resolves about ±40 Elo and takes about 9 minutes; a loop's fixes are worth single digits. The
75-minute verification run (bot-check, ladders, fuzz, gates) answers "did this do harm", never "did this help".

The structural reason: the findings that would matter (agenda exposure, sequencing, draw-then-run, Touch-ups, Spin
Doctor timing) need a comparison of action *sequences*, which a per-action scorer cannot make. Each loop deferred them
with the same note. Each fix that did land became one more gated rule: the Runner policy is on generation 22 with 62
version references in its main file; the Corp policy is on generation 25 with 52 in its development file alone.

### 1.3 Why the planner was given up too early

The Wave 4 and 5 conclusion was "the planner is saturated". The evidence points at the leaf evaluation instead:

- **The leaf is fitted to the wrong target.** `ai-fit-evaluation` samples each side's view at the first action of every
  turn and labels it with who eventually won. Predicting the winner of a position is not ranking actions from it: the
  signal is one bit per game, shared by every position in the game, and dominated by which deck was ahead.
  (argentum-engine, below, tried the same fit and rejected it for the same reason.) Set 4 itself was never fitted:
  sets 2, 3 and 4 all load set 2's weights (`FittedEvaluation.DocumentFor`) and differ only in how one feature is
  extracted, so the weights have not seen the corrected features they are applied to.
- **Compute does not help, which means the leaf is noise.** Corp Hard at 10 samples × width 4 scored 0 Elo against
  3 × 3. If an evaluator carried signal, more rollouts would show it. This is the test argentum calls budget scaling, and
  it failed before anyone ran it as a test. S0.3 then measured it: ×8 samples is no stronger than ×1 in either seat.
  Part of the reason is the next point: more samples sharpen the value of "this action, then the heuristic's play",
  which is the wrong quantity whatever the leaf.
- **The planner's own continuation is the heuristic.** `TurnPlanner.Choose` plays the root side's later decisions in the
  turn with the plain heuristic. A first action that pays off only with the right follow-up (Petty Cash first, install
  then advance twice, draw then run) is undervalued whenever the heuristic misplays the follow-up, and re-planning after
  the action does not help, since the action was already undervalued at the root. S2 found this at a Petty Cash root:
  the first action lost 0.23 with the heuristic's continuation while the full three-click lines were level. The same
  rollouts are the teacher's, so the regret suite's labels carry it too.
- **Rollouts are expensive for the wrong reason.** `ApplyMode.Simulation` costs about 47 µs an action (Wave 3), but
  every simulated decision in `TurnPlanner.Choose` builds a full observation and runs the heuristic with its full work
  budget, so one rollout step costs far more than the engine does.
- **The blend hides the planner.** A planned action keeps its heuristic score and adds the rollout difference from the
  heuristic's own choice, in raw log-odds sums (`Weight` 100). The heuristic's "pressure, information, tempo" terms, which
  the leaf lacks, were kept because the leaf was weak; with a leaf that sees them the blend is a drag.

### 1.4 What self-play cannot see, and what the outside player is for

Both bots share misconceptions. A weakness neither side exploits never moves the ladder: the Corp's agenda exposure was
invisible to self-play because the bot's Runner didn't punish it, and Sonnet did. So outside play (a human or an LLM)
stays, in a different role (section 7): it supplies positions for the puzzle suite and finds new categories; it does not
produce fixes one by one.

## 2. What stays

These are better than most hobby engines have and are not up for redesign:

- **The information boundary.** Agents see filtered observations and choose among engine-issued action IDs; search runs
  only on states sampled from the agent's own observation (`ObservationDeterminizer`); the hidden-state twin tests
  (`ObservationSamplerTests`, `AgentBoundaryScenario`) are a hard gate.
- **Determinism.** Every decision is a function of the observation, the checkpoint and `DeterministicRandom`; agents
  resume from checkpoints; replays verify.
- **Decision logs, saved games, saved-decision regression tests** (`BotLoop1Tests`, `BotLoop2Tests`,
  `BotStrategy1Tests`), the Oddity lints (`ai-lint`), `ai-runner-diagnosis`, `ai-belief`, `ai-style`, and `ai-ladder-diff`
  (each paired game's first departure from its baseline, grouped by kind, with `--reasons`; after chiriboga's replay diff).
- **The whole-run model** (`RunModel`, `RunnerEncounterPlanner`) and **the tracker** (`CardTracker`), which beats its
  prior on every tier.
- **The paired-deal ladder** (`RatingLadder`) with the bootstrap interval and `--stop-when-clear`.
- **Frozen benchmarks and saves** keep replaying: every shipped policy version stays constructible (section 4.4).
- **Latency budgets:** 50 ms p95 for ordinary decisions, 200 ms for run and nested choices (`PolicyPerformanceTests`).

## 3. Principles

1. **Measure at the resolution of the change.** A rare-position fix is verified by its saved-decision test and by a
   count over saved games, not by a ladder. A ladder is for a claim of strength, and is sized to resolve it.
2. **The bot finds its own blunders.** The first reviewer of a version is a larger search budget: where the teacher
   disagrees with the shipped bot, ranked by cost. People and LLMs review that list, not raw games.
3. **Plans, not rules.** Strategic behaviour (sequencing, exposure, draw-then-run) comes from search over action
   sequences scored by an evaluation that sees the position, not from another gated term.
4. **One live policy.** The current version is developed without generation checks; everything shipped before it is
   frozen code (section 4.4).
5. **No change in behaviour without a number.** Every version ships with its regret-suite score, its puzzle-suite score
   per category, its lint counts and, when it claims strength, a ladder with an interval clear of zero.
6. **Outside play only on the user's ask.** Plans and gates never schedule human, Codex or Claude games or reviews.

## 4. Target architecture

### 4.1 The decision pipeline

```text
observation ──> heuristic (candidate generator: top-K actions with priority and score)
            ──> meaningful-candidate filter (drop dominated and bookkeeping actions)
            ──> planner: lines of the root side's own turn actions (a beam over the heuristic's top candidates,
                   to the turn's end); for each of S shared determinized samples, play each line out
                   rollout policy for everything else (nested choices, the opponent, later turns): cheap and
                   deterministic (4.3)
                   leaf: evaluation set 5, as win probability (4.2)
            ──> value(line) = mean over samples of  sigmoid-space delta from the root  +  static mix
            ──> value(candidate) = the best line that begins with it; choose the best; re-plan after every action
```

- **The heuristic generates and orders candidates; it no longer decides** for Standard and Hard. Easy keeps the
  heuristic as its decider, with the forgetful tracker, so it stays beatable and quick.
- **The root side's own turn is searched as lines, not played by the heuristic.** A candidate's value is the best line
  of the root side's turn actions that begins with it (beam width `plan-width`, depth to the turn's end), scored with
  shared samples. The heuristic still plays the nested choices inside those actions, the opponent and every later
  turn. This is what lets the planner find a first action whose worth depends on its follow-up (1.3), and it is the
  same search the teacher labels with (5.1), so labels and play measure one thing. Lines share work, so the cost is a
  few times a one-step search, not width to the power of depth; sequential halving across lines keeps it bounded.
- **Scores are compared in probability space.** Each rollout's leaf is a win probability; the candidate's value is the
  mean of (leaf − root value), so a certain win among three even rollouts reads 0.625 rather than overflowing the sum.
  Wins and losses are 1 and 0, not sentinels.
- **The static mix is a parameter, not a blend of two scorers.** The leaf on the root position after the first action
  (no rollout) is averaged in at weight `static-mix` (start 0.25). The heuristic's score is not added to the value;
  it only orders candidates and breaks ties.
- **Budget is work, not wall clock.** The planner spends simulated decisions (`AgentWorkBudget`), never a stopwatch, so
  ladders are reproducible on any machine. Profiles set `plan-samples`, `plan-width` and the new `rollout-budget`
  (simulated decisions per root). Latency is checked by `PolicyPerformanceTests` on the reference box.
- **Run choices** (continue or jack out, trash or not, rez or not) use the same rollouts to the run's end, as
  `TurnPlanner.AppliesToRun` does now, once the leaf is replaced.
- **Hidden cards** are dealt from the tracker (pins at their belief weight) and the pool, or the decklist for Hard, as
  `TurnPlanner.Pins` does. One shared set of samples serves every candidate at a root (common random numbers).
- **The opponent in the rollouts is the live Standard heuristic** (`plan-model 1`), for the teacher now and for S5's
  Standard when it ships. The planner's pinned model (Runner v7, Corp v8, kept so the shipped Corp planner plays as it
  did) is not a defensible opponent for labels meant to train set 5: the S1.5 follow-up found the teacher could not see
  the cost of an exposed agenda because its v7 Runner, like the live one before Runner v28, did not run the remote.

### 4.2 Evaluation set 5: a leaf trained by search

> **Outcome (8 October 2026, S4.2 to S4.2c).** Falsified at the turn boundary. Against a reference built from play-outs
> to the game's end, no static evaluation of an end-of-turn state orders two lines of one root better than about 65% for
> the Corp and 73% for the Runner: not the shipped fits, not set 5, not value- or rank-fitted, not boosted trees. The
> information the planner needs is in the play that follows the turn, not in the state where it ends. Set 5 stays as
> features and the Runner keeps the general fit as its leaf, but no leaf fit is pursued further. What replaces the leaf
> as the thing search is distilled into is in 4.6 (play at inference, for Hard) and 4.7 (the apprentice at the root,
> for Standard). The text below is kept as the record of what was tried.

**Features.** Set 4 plus the strategic terms the loops named, each computable from the observing side's view and the run
model:

| Term | Definition (observer's view) |
|---|---|
| Exposed agenda points | Agenda points (known or inferred) in remotes the Runner can break this turn or next, by the run model's cheapest path and the Runner's visible credits and breakers |
| HQ agenda density | Expected agendas in HQ (tracker) over HQ size |
| R&D agenda density | Agenda points left in R&D over cards left, from scored, stolen and seen agendas |
| Clicks to score | For the Corp's most advanced agenda: advancement left against clicks and credits, and whether the Runner can break its server first |
| Punisher exposure | Whether a successful run this turn arms a known or likely punishing operation the Runner cannot pay off or survive |
| Grip margin | Cards in grip minus the largest damage the Corp can deal next turn (known cards, pool prior) |
| Central tax | The Runner's cheapest cost into each central, and whether it is affordable this turn |
| Economy tempo | Credits after reserves (rez reserve, break reserve) and credit income per click available |
| Score race | Points to win for each side, turn number, and who moves next |

Features are extracted by `EvaluationFeatures.Extract(features, catalog, 5)` from the agent's observation only. Each has
a name; `data/ai/evaluation-v5.json` stores the fit with its provenance.

**Targets.** Not the game's winner, and not a position's value either. The planner never asks the leaf how good a
position is; it asks which of two end-of-turn states reached from the same root is better. A leaf fitted to predict
values spends its capacity on the level across roots (which deck is ahead), which is easy and useless to a planner:
S4.2 fitted set 5 to the teacher's values and beat set 4 on held-out log loss in both seats; refitted to the values
of end-of-turn states (S4.2b), it ranked pairs of end states from one root at 60% for the Corp and 64% for the Runner
(chance 50%; set 4 shipped 55% and 63%). So the leaf
is fitted **to rank**:

- the rows are the end-of-turn states of the lines the teacher plays (section 5.1 records them), with the play-out
  value from each state;
- the training examples are **pairs** of distinct end states from one root on the same sample (common random
  numbers), the target the sign of their value difference, weighted by its size;
- the model is the same linear form on the feature *difference*, with no bias, so it is a ranker; a one-dimensional
  calibration of bias and slope on the play-out values afterwards gives it back a win-probability scale for the
  planner's delta from the root;
- the fit is gated on within-root pairwise accuracy on held-out pairs against a **reference order**: the sign of the
  difference of the two lines' values averaged over every sample, counted only where that difference exceeds twice
  its standard error, so the reference itself is reliable. The agreement of two single play-outs is not a ceiling on
  that accuracy (two reports that are each right 80% of the time agree only 68% of the time); it is reported as
  repeatability, beside the gate. Log loss is reported, not gated.

The model is always a **scalar scorer** `g(features)`, because the planner evaluates one state at a time and averages
calibrated win probabilities before subtracting the root; only the loss is pairwise. It stays small: linear, or, if the
linear ranker stays well under what the reference order allows, boosted scalar trees trained with the pairwise
gradient, deterministic, exported to JSON and loadable without any runtime beyond `Netrunner.AI`. A model that takes a
pair's difference as its input cannot serve as the leaf. A fit is accepted only by the gates in section 6.

**Per pool.** As with set 2, a pool keeps its own fit only where it beats the general fit on that pool's held-out
roots. The Core Battle Box is the first target.

### 4.3 The rollout policy

A rollout step today costs an observation build plus a full heuristic evaluation. The rollout policy must be:

- **cheap:** no observation rebuild per step; it reads the simulation state through a thin view for the acting side
  (public zones plus that side's own hidden zones), and spends a fixed small work budget;
- **deterministic given the sample and the agent's random stream**, so twins and resumed agents repeat it;
- **plausible, not strong:** it plays to the turn's end; its job is to make the leaf's position realistic. Mistakes it
  makes equally for both sides cancel in the delta.

First implementation: the current heuristic at a reduced work budget with a cached `HeuristicContext` per rollout.
Second, if profiling says so: a distilled table policy (action kind × a few features) fitted to the heuristic's choices.
Profiling in S0 decides which is needed to fit the budgets.

S0 measured it (steps S0.1 and S0.4): the heuristic is about a tenth of a rollout step, simulated `Apply` about half,
and the observation build and read a sixth each. So the rollout policy stays the planner's own per-step choice for now,
which the teacher (5.1) shares, and the cost is taken out of the engine's simulation first (step S5.0); the two
implementations above wait until S5's budgets are measured against that.

### 4.4 One live policy, frozen history

- `LegacyCorpPolicy` and `LegacyRunnerPolicy` are copies of today's `CorpHeuristicPolicy` and `RunnerHeuristicPolicy`
  with every version through v30 and v26, moved under `src/Netrunner.AI/Legacy/` and never edited again except for
  compile fixes. `OfflinePolicies.Create` routes those versions to them. Frozen benchmark digests, saved games and the
  twin tests prove they are unchanged.
- The live `CorpHeuristicPolicy` and `RunnerHeuristicPolicy` start as copies with the generation checks resolved to the
  latest behaviour and the dead branches deleted. They carry one version string each (`corp-heuristic-v31`,
  `runner-heuristic-v27`) and no `Generation`.
- From then on a policy version bump means: the live code changed and its evidence is recorded. The previous live
  version is not kept constructible unless a frozen benchmark or a shipped save needs it; the ladder's "previous
  version" entrant is a frozen *binary* in `.tools/ladder-anchors/` built from the previous commit, not a code path.
- Difficulty and personalities stay as profiles over the live policy (`CorpPolicyProfiles`, `RunnerPolicyProfiles`),
  and the tutorial bot stays frozen at Corp v7 and Runner v6.

### 4.5 Tiers

| Tier | Decider | Knowledge | Budget |
|---|---|---|---|
| Easy | Heuristic | Pool prior, forgetful tracker | None |
| Standard | Heuristic with the apprentice ranker (4.7), or the heuristic alone if the apprentice does not beat it | Pool prior, full tracker | Fits 50 ms / 200 ms p95 on the reference box |
| Hard | Corp: play-out planner over turn lines (4.6). Runner: Standard's decider, with run-choice play-outs where S4.6 shows they rank | Opponent's decklist, full tracker | Turn-action decisions up to about 1 s on the reference box (accepted 8 October 2026); every other decision within 200 ms p95 |

The order must hold on the ladder in both seats; the budget-scaling ladder (5.2) is the standing proof that Hard's
extra compute is worth something. A one-second think on its own turn actions is ordinary for a human-facing opponent;
the UI shows it as thinking, and no other decision waits on it.

### 4.6 Play at inference (Hard)

> **Read (8 October 2026, S4.4).** For the Corp the horizon decides and the samples do not: a turn-end leaf ranks lines
> at about 60% at any sample count, one opponent turn about 67%, and 4 turns or the game's end pass 80% (4 turns at 16
> samples, the game's end at 8). For the Runner no horizon ranks turn lines above the turn-end fit, so Runner Hard
> does not play out turn lines; its budget goes to run choices once S4.6 has measured them. No Corp cell fits one
> second before S5.0 (one sample of one candidate over 4 turns costs about 78 ms), so Corp Hard starts at the samples
> S5.1's beam leaves and S5.0 buys the rest.

The teacher ranks because it plays past the turn: 32 play-outs separate versions, found the Petty Cash line the
turn-end leaf missed, and made the agenda-hold gain visible. Hard is the teacher at a budget it can afford:

- **Horizon.** Lines of the root side's turn (4.1), each played on past the turn's end by the rollout policy for `h`
  opponent and own turns, then scored by the leaf (the general fit) or, when `h` reaches the game's end, by the result.
  `h` is chosen by measurement (S4.4): the cheapest horizon and sample count whose ranking of lines, in the planner's
  view, agrees with the game's-end reference above a set accuracy.
- **Samples.** Common random numbers across every line of a root, as the teacher uses; the value of a line is its mean
  over the samples, and the planner compares means.
- **Budget.** Work units, as before; the wall-clock consequence is Hard's turn-action budget (4.5). S5.0's engine
  cost cuts buy either more samples or a longer horizon at the same budget, and the S4.4 curve says which is worth more.

### 4.7 The apprentice at the root (Standard)

> **Outcome (8 October 2026, S4.5).** Not met under teacher v2, and the failure was the teacher's: the apprentice
> learned v2's order faithfully, including that a banked credit is worth a run, and lost 250 Elo as the Runner. The
> Corp's was level in play. The refit on teacher v3's labels is S4.5b; until it reads, Standard is the heuristic in
> both seats, with the Corp's 3×3 planner, which the ladder keeps.

The regret suite is also a training set: at every root, the teacher's value of every meaningful candidate group. A
ranker **at the root**, over candidates, is what a cheap policy can learn from it:

- **Input:** the root's features (set 5 from the acting side's view) and each candidate's own (its action kind, the
  heuristic's priority and score for it, the card's class and cost, the server's protection and contents by the run
  model, counters and credits it moves).
- **Target:** pairwise over the groups of one root, weighted by the teacher's value gap, so the ranker learns the
  teacher's order, not its level.
- **Use:** it re-orders the heuristic's candidates; the heuristic's priorities and vetoes stay as the generator, so a
  lethal trap's advance or an illegal play is never ranked up. It costs microseconds, so it fits Standard's budget and
  Easy can carry a weakened one.
- **Gate:** the suite's own metric, regret on held-out roots against the live heuristic, with the interval clear, in
  each seat; then the ladder. This is the model S6's rounds refit, in place of the leaf.
- **Ceiling.** A root-level ranker averages over the hidden information and the play the teacher sampled; it can
  learn "hold the agenda when the remote is breakable and HQ is not yet diluted", and it cannot learn what only a
  play-out from this exact position shows. Where it lands between the heuristic and the teacher is S4.5's measurement,
  not a promise.

## 5. Measurement

### 5.1 The regret suite (the per-change instrument)

A fixed corpus of recorded decision roots with teacher labels, scored in minutes, replaces the per-change ladder.

- **Roots:** turn-action decisions with at least two meaningful candidates, and run choices, sampled from Standard
  self-play on each pool (`ai-ladder --save-games`), plus every confirmed finding from the loops and strategy work.
  Every meaningful candidate group at a root is labelled, not only the heuristic's best few: a planner is expected to
  choose outside them, and a choice without a label drops out of regret, which would hide exactly the decisions the
  planner changes. Where a labelling has cut the groups, `score` labels an unlabelled choice on demand.
  **Targeted roots** (S1.6): a paired comparison of two versions has power only at the roots where they choose
  differently, and roots drawn at random hold few of those. `ai-regret diverge` replays the saved games with both
  versions and keeps every decision where their chosen groups differ, so a comparison that matters (two Corp versions,
  or a planner against the heuristic) is measured where it happens, not diluted over thousands of roots where nothing
  changed.
  Each root keeps the acting side's observation, the checkpoint, the legal candidates and the deal, so any policy
  version can be asked to choose there (`BotLoop1Tests.ChooseAtAsync` already does this).
- **Teacher:** the planner at an offline budget (start: 32 samples × every meaningful candidate) with common random
  numbers across candidates, its rollouts modelling the opponent by the live Standard heuristic (4.1; `ai-regret label`
  defaults to it, and a label names its teacher). It searches the root side's own turn as lines (4.1), so a candidate is
  valued at its best follow-up rather than the heuristic's; a label made with the heuristic's continuation (the first
  labelling) is marked as such and replaced. Its rollouts go **deeper than the leaf's horizon**: to the game's end with
  the rollout policy where S0.1's costs allow it, otherwise a fixed number of turns (start 4) and then the leaf. A label
  is then mostly the result of play, not a reading of set 4, so fitting set 5 to it does not inherit set 4's bias. It
  writes each candidate's value, its standard error over the samples, and the horizon it used.
- **Label noise.** A rollout to the game's end returns a win or a loss, so 32 samples give a candidate's value to about
  ±0.09, and common random numbers help less as the rollouts diverge over several turns, while candidates often differ
  by a few points. Regret *differences* between versions stay unbiased under that noise; agreement and "the best
  candidate" do not. So the suite records its **noise floor**: a subset of roots labelled twice with independent sample
  seeds, reporting how often the two labellings pick different best candidates. Agreement is reported against that
  floor, and roots whose top two candidates are within their standard errors count as ties (any of them is "best").
- **Scores per policy version:**
  - **Regret:** mean over roots of (teacher's best value − teacher's value of the version's choice), in win-probability
    points. The main number.
  - **Agreement:** share of roots where the version picks the teacher's best.
  - Both per side, per pool, per category (the puzzle categories, 5.3), with a bootstrap interval over roots.
- **Blunder list:** the roots with the highest regret for the current version, with the candidates and values. This
  is the review list for a person or an LLM (section 7); it is also where new puzzles come from.
- **Teacher drift:** the teacher is only as good as its leaf, so every expert-iteration round (S6) re-labels the core
  (below) and reports how much the labels moved; a round whose relabelling flips more than a set share of best choices
  is inspected before it is accepted.
- **Labelling cost.** A whole-suite relabel is about seven hours on the development box, and S1 changed the teacher
  three times in two days (one-step, the line search, the live opponent model). The suite is sized for the questions it
  answers, so that a relabel is an hour or two and never the critical path:
  - **The teacher is versioned and frozen between milestones.** Teacher v2 was the line search (beam 5, one-turn
    screen), the live Standard opponent, a 4-turn horizon and 32 samples. **Teacher v3 (from 8 October 2026) is the
    same search played to the game's end**, scored by the result, with no leaf anywhere in a label. The move was
    forced by two readings in which the v2 suite and the ladder disagreed, both traced to v2's leaf at its horizon:
    an apprentice that learned v2's order banked credits and lost 250 Elo, and the Corp's 3×3 planner read worse than
    the heuristic on the suite while the ladder had it 44 to 97 Elo better. A teacher v3 label costs about 1.8 times
    a v2 label. Labels and scores name their teacher. A teacher change is a milestone event with a relabel budgeted
    overnight, not something a step does in passing; v3 is the last one planned.
  - **A core and targeted roots.** The core is about 2,000 random roots, held out by deal; it is what a teacher change
    relabels, what absolute regret is quoted on, and what the leaf is fitted on. Targeted roots (`diverge`) are
    labelled when a comparison needs them and keep the labels they got. A paired comparison is valid root by root as
    long as both values at a root come from one teacher, so a suite with roots labelled by different teachers still
    compares versions; only absolute regret needs one teacher, and it is quoted on the core.
  - **Adaptive samples.** A root gets 8 samples that decide how many more it needs, and the recorded values come only
    from the samples drawn after that decision: 8 where the deciding samples showed a clear gap, more where they did
    not. The deciding samples are never recorded, because a stop triggered by a gap that happened to look large would
    otherwise preserve exactly that inflated gap in the labels the leaf is fitted to. Most roots are decided early.
  - **Scheduling.** The relabel and the ladders both want the box to themselves and there is no second box, so relabels
    run overnight and ladders by day; a step never waits on a relabel it could have scheduled the evening before.

CLI: `ai-regret label <games-dir> --output <suite>` and `ai-regret score <suite> [--version V] [--profile P]`.

### 5.2 Ladders that resolve what they measure

- **Speed.** Every ladder game today plays once authoritatively with a full observation per action, serializes and
  deserializes the **whole state before every action** (a save-and-reload check), then serializes, deserializes and
  re-verifies its whole replay at the end (`FullMatchRunner`). A ladder mode keeps both checks on every Nth game (as
  `fuzz --verify-replays-every` does) and skips them otherwise; the fuzz and the verified games keep them on. Profile
  one game and remove the next cost after those. Target: 1,000 deals on the box pool in under 10 minutes on 12
  workers. (A two-entrant deal is four games, both entrants in both seats; S0.2 measured 2.4 games a second, so 10
  minutes buys about 360 deals on this box: see [BOT_VERSIONS.md](BOT_VERSIONS.md).)
- **Size by claim.** A claim of strength uses the deals 10 minutes buy (about 400 on the box pool, S0.2) with
  `--stop-when-clear`; a no-harm check on another pool uses 240. Report the interval, never the point alone.
- **Budget-scaling ladder.** Entrants `standard-x1`, `standard-x4`, `standard-x16` (the same policy at 1×, 4× and 16×
  rollout budget). The result must be monotone with each step's lower bound above zero. A non-monotone result means the
  leaf is not carrying signal; stop and fix the leaf before touching anything else.
- **Anchors.** `standard-m5` (frozen M5), the frozen binary of the previous live version, and `random`. A new version
  is promoted when it beats the previous version and loses to no anchor, in both seats, on the target pool, and is
  level or better on the Gateway pool.

### 5.3 The puzzle suite v2 (the localizing signal)

The M5 tactical suite is at 39/40 and 38/40 and was tuned with held-out feedback; it no longer separates versions. The
new suite is built from recorded decisions and the loop findings:

- **Single-decision puzzles:** "at this root, the right choice is X" (the existing saved-decision tests, moved into a
  manifest under `data/ai/puzzles/` so they are scored, not just asserted).
- **Line puzzles:** "from this root, play to the turn's end; the resulting state satisfies P" (Petty Cash before the
  install; draw, then run the remote; install, advance, advance). Lines are what the planner claims to find and what
  single-decision checks cannot see.
- **Decision puzzles:** nested choices (what to break, what to let fire, what to trash, which card to discard).
- **Categories:** sequencing, agenda exposure, punisher risk, run targeting, economy, access and trash, ICE placement,
  rez timing, hand management, inference. Each has a few negative controls (where the tempting action is wrong).
- **Reporting:** `ai-puzzles <manifest> --version V` prints per-category scores; a version's record includes the table.
  Puzzles are never used as fitting targets; a puzzle that a fit was tuned on is moved to the tuning split.

### 5.4 Lint counts over saved games

`ai-lint` counts the Oddity lints over a saved ladder run. A per-change report compares counts per 1,000 games against
the previous version. New lints come from the blunder list when a class recurs.

## 6. Verification tiers (replace AGENTS.md's bot tiers when S2 lands)

| When | What | Time |
|---|---|---|
| Per change | `make bot-check`; `ai-regret score` on the target pool; `ai-puzzles`; lint counts on the last saved run | ~10 min |
| Per candidate that claims strength | Fast ladder, about 400 deals (what 10 minutes buys), `--stop-when-clear`, target pool; 240 on Gateway for harm | ~15 min |
| Per policy version | Budget-scaling ladder; anchors; `PolicyPerformanceTests` on the reference box; the record in `docs/BOT_VERSIONS.md` | ~30 min |
| Per batch, before push | `make gates REF=<commit>`; per-slice fuzz when the engine or cards changed | ~30 min |

A change that moves no number (regret, puzzles, lints, ladder) is not shipped as a behaviour change.

## 7. Outside play: humans and LLMs

Outside play finds what self-play cannot (1.4). It runs **only when the user asks**, under
[BOT_LOOP_WORKFLOW.md](history/BOT_LOOP_WORKFLOW.md) sections 1–5, and its output changes:

- Each confirmed finding becomes a **puzzle** (5.3) and, where the position is general, a **lint** (5.4). It is not
  fixed as a one-off rule.
- The reviewer is given the **blunder list** (5.1) alongside the games, so its attention goes where the search already
  disagrees with the bot.
- A fixed-deal arm against an outside player compares two versions on the same deals (paired), as a check that the
  self-play gains transfer. Twelve games cannot measure strength; they can surface a category.

## 8. What is borrowed from argentum-engine, and what is not

[argentum-engine](https://github.com/wingedsheep/argentum-engine) (an MTG engine) took a greedy 1-ply AI nobody could
measure to a rollout evaluator that beats it with an interval. Borrowed, as ideas (no code):

- the budget-scaling ladder as the standing health check of a search;
- rollouts averaged in probability space, as deltas from the root, with a static mix;
- a permanent frozen anchor and a promotion rule (beat the previous version, lose to no anchor);
- line and decision puzzles as the localizing signal, categories with negative controls;
- search as the teacher: label decisions with a large offline budget, fit a fast leaf to the labels, keep search at
  inference; reject outcome-only fitting;
- budget as work units; a rollout policy that never simulates.

Not borrowed: MTG's branching-factor findings (Netrunner turn actions offer tens of candidates and a run is a long nested
sequence, so candidate filtering matters here), per-card intent catalogs (the behaviour catalog already types card
effects), and any learned dynamics.

## 9. Risks

- **The teacher repeats the leaf's mistakes.** More samples of a biased leaf do not remove the bias. Mitigation: the
  teacher's rollouts run past the leaf's horizon (to the game's end, or several turns then the leaf), so labels come
  from play, played by the same rollout policy the planner ships with (4.3); deep labels are noisier, so the suite
  measures its own noise floor (5.1) and agreement is read against it; S4.2 fits set 4 to the same labels as a control,
  so a gain is attributed to the target or to the features; the puzzle suite and the ladder gate each round; teacher
  drift is reported; a round that fails either is discarded.
- **The teacher's continuation, and the planner's.** Both value a candidate followed by the heuristic's play, so a
  candidate whose worth depends on the next action in the same turn is undervalued when the heuristic misplays it (S2
  found Petty Cash first undervalued by 0.23 at one root by this alone; the full lines were level). A longer horizon
  does not help, and re-planning after the action does not either. Mitigation: the turn-line search of 4.1, built in
  `TurnPlanner` behind a parameter (S1.5) so the teacher and the shipped planner share it and Hard is unchanged until
  S5; before S4 fits to the labels, the suite is relabelled with it, the change in labels is reported as teacher drift
  is, the certified puzzles are re-certified, and S1.3's separation gate is passed again. The cost falls on S5.0: a
  beam inside 50 ms needs cheaper simulated Apply.
- **A suite that misreads what is optimised against it.** Realised on 8 October: teacher v2's horizon leaf made the
  suite disagree with the ladder twice (section 5.1). Mitigation: teacher v3 has no leaf; every gate that promotes a
  behaviour keeps a ladder beside the suite; an apprentice or planner that beats the suite and loses the ladder is a
  finding about the teacher first.
- **The apprentice reaches the heuristic's level and no further.** A root-level ranker may average away what the
  teacher sees. Mitigation: S4.5 measures it on the suite's metric before any planner work depends on it; if it lands
  level, Standard stays the heuristic with blunder-list fixes and the teacher's gains reach players through Hard.
- **Hard's one-second budget is not enough for the horizon that ranks.** Mitigation: S4.4 measures the curve before
  S5 commits to a cell; S5.0 moves the cost; if no cell inside a second ranks above the target, Hard takes the best
  cell that fits and the record says what it gives up against the teacher.
- **Rollout cost blows the latency budget.** Mitigation: S0 profiles before anything is built on it; the rollout policy
  is designed for cost (4.3); `rollout-budget` is a profile parameter; Standard may plan fewer samples than Hard.
- **Freezing legacy code doubles the policy source for a while.** Mitigation: the legacy files are never edited; they
  are deleted when no frozen benchmark or save needs a version they serve.
- **Self-play blind spots.** Mitigation: section 7, and the Core Battle Box as the target pool, where humans play.
- **The ladder speedup changes results.** Mitigation: the fast mode is validated by replaying a full ladder in both
  modes and comparing results game by game before it is used for a gate.

## 10. After the research (9 October 2026)

### 10.1 What the research found

| Question | Answer | Record |
|---|---|---|
| Does more search budget make the planner stronger? | No. ×8 samples is level with ×1 in both seats, on the shipped leaf and on set 5. | S0.3, S4.3 |
| Can a static evaluation of a turn's end rank the planner's lines? | No. About 65% (Corp) and 73% (Runner) against the game's-end order, whatever the features or the model. | S4.2 to S4.2c |
| How far must a play-out go to rank lines? | The Corp: 4 turns or the game's end, about 81% at 8 to 16 samples. The Runner: no horizon ranks its turn lines or its run choices better than the turn-end fit or the heuristic. | S4.4, S4.6 |
| Can a cheap ranker learn the teacher's order? | Not in a way that survives play: v1 lost 250 Elo as the Runner, v2 lost 145 to 262 in both seats. Regret on the heuristic's own states does not predict play once the ranker moves the game off them. | S4.5, S4.5b |
| How noisy is one root's label? | At teacher v3 two labellings pick different best groups at 62% of roots, and half the roots have a tied best. The suite measures paired differences between versions; it does not say what the right move is at one root. | S1.8 |
| What made the bots stronger in this period? | A valuation change (Runner v28, a face-down remote card is worth its chance of being an agenda: +44 Elo [11, 81]), and the Corp's small Standard planner, which the ladder keeps (+44 box, +97 Gateway over the heuristic alone). | Runner v28, S4.5 |

The reading: the bots' weakness is mostly missing strategic knowledge in the heuristic, which every rollout, teacher
and ranker inherits, not a lack of search. Runner v28 fixed knowledge a new player has on the first day, and it moved
the ladder more than any search step did.

### 10.2 What stays, what is set aside

- **Stays:** the regret suite at teacher v3 (the core in `bot-check`, targeted roots by `diverge`), the puzzle suite,
  the fast ladder, the Oddity lints, S5.0's engine cuts, the Corp's 3×3 Standard planner, Runner Hard's planner, and
  every verification tier of section 6.
- **Set aside:** fitting a leaf (4.2), the apprentice (4.7; `apprentice` stays off in every profile and its models
  stay loadable for the records), the Runner's play-outs (turn lines and run choices), the Standard tier of S5, and
  S6's expert iteration. None of these is reopened without a new reason recorded here.
- **One bounded experiment** (step S5.1c): Corp Hard as the 4-turn play-out planner on the box ladder against
  Standard. It is the one place the research found search signal (S4.4). It passes with a lower bound above zero and
  then ships within Hard's one-second budget; otherwise search work stops and Corp Hard stays as it is.

### 10.3 The heuristic track

[STRATEGY_FUNDAMENTALS.md](STRATEGY_FUNDAMENTALS.md) states how to play each side as rules with IDs (R01 to R24, C01 to
C24), each marked base, matchup, archetype or tuning, plus the mistakes each side should punish. The heuristic is
improved against it, one concept at a time:

- **Audit before fixing.** Each base rule is mapped to the live policy (encoded, partly, missing, or contradicted,
  with the code location). Many are already there in some form (R&D freshness, HQ agenda share, rez and access
  reserves, punisher draws, lethal traps); the gaps are the work.
- **Count before ranking.** Each "mistake to punish" that is observable in a saved game becomes a count over the box
  ladder's games (a lint, 5.4), for both bots. A gap is worked in order of how often the bot makes the mistake and
  what the suite's blunder list says it costs.
- **General concepts, then cards.** A fix states its rule ID and a condition over the position, not a card name. A
  card-specific behaviour is fine where the card really is unusual, and goes through the behaviour catalog.
- **Rule IDs in the reasons.** A changed term's reason string carries its rule ID (`"R16/asset-trash"`), so decision
  logs, the blunder list and the lints name the rule.
- **Each fix ships on the ladder.** A batch of related fixes is one policy version with a puzzle per rule (the guide's
  paired positions: the same position with one fact changed), the regret line, the lint counts, and the box ladder
  (400 deals) with Gateway (240) for harm, as section 6 already says. The Runner comes first: it wins about a third of
  its games against the Corp on the box.
- **Deck notes.** The box pool is seven decks (Nebula Fast Advance, PT Untaian Shell Game, Zwicky Supermodernism, LEO
  Glacier; Topan, MuslihaT, Magdalene). Only the archetype modules those decks use are worked; a short note per deck
  (its plan, key cards, what it fears, how to play against it) checks the archetype knobs (`CorpArchetypes`,
  `RunnerArchetypes`, `AgentPersonalities`) against how the deck should play.
