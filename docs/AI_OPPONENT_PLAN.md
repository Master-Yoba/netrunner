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
- Optional later step: fit the static evaluation to game outcomes from
  simulated play (logistic regression on features). Keep it small, linear or
  shallow, and deterministic, with weights stored as a versioned JSON file
  under `data/`.

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
- **Personalities** as weight presets on top of difficulty:
  - Corp: "glacier" (taxing ICE, slow scoring), "rush" (early agendas) and
    "asset spam".
  - Runner: "aggressive" (runs early and often) and "rig-builder".
  Each is validated on the ladder so it stays within one rating band of its
  difficulty.
- **Human-facing pacing is a UI concern** (see the
  [UI plan](UI_UX_REDESIGN_PLAN.md) phase U3). The bot still decides
  instantly, and the presentation shows its actions at readable speed.

Gate: human playtest notes confirm Easy, Standard and Hard feel ordered, and
the ladder shows the same order.

## 5. Sequencing

```text
A1 ladder + human corpus ──> gates everything below
A2 heuristic fixes ────────> A5 tuning
E1 + E5 (engine) ──> A3 turn planner ──> A4 run search
A6 difficulty/personality after A3 (needs compute knobs)
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
