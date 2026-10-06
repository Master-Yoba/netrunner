# Bot versions: the evidence record

The running record of [BOT_PLAN.md](BOT_PLAN.md): every policy version's evidence (regret, puzzles, lints, ladders) and
the reference costs every step is budgeted against. The steps are in [BOT_PLAN_STEPS.md](BOT_PLAN_STEPS.md).

## Reference costs

### S0.1: one ladder game and one planner decision (4 October 2026)

**Box:** the development VM, 14 vCPUs (QEMU virtual CPU), .NET SDK 10.0.401 building net8.0, Release CLI with the
official card data. Single-threaded: one game at a time, no other load. Corp v30 and Runner v26.

**Commands:**

```bash
engine-bench --profile-game --content gateway-elevation --profile standard --games 8 --rollout-roots 6 --rollout-samples 4
engine-bench --profile-game --content gateway-elevation --profile hard --games 8 --rollout-roots 6 --rollout-samples 4
engine-bench --profile-plan tests/Netrunner.Match.Tests/Fixtures/saved-games/strategy1-petty-cash --sequence 101 --profile hard --repeats 30 --rollout-samples 16
engine-bench --profile-plan tests/Netrunner.Match.Tests/Fixtures/saved-games/strategy1-match-point-draw --sequence 329 --profile hard --repeats 30 --rollout-samples 16
```

Each `--profile-game` run plays one untimed game first; each `--profile-plan` run makes 30 untimed choices first.
Without the warm-up, rollout steps measure about twice as slow (tier-0 JIT code).

**A ladder game, per action** (the Core Battle Box, seed 1, eight self-play games):

| Segment | Standard µs/action | Share | Hard µs/action | Share |
|---|---:|---:|---:|---:|
| Agent decision (`match.decide`) | 1,959 | 40.9% | 5,475 | 66.0% |
| of which the turn planner (`plan`) | 1,680 | 35.1% | 5,205 | 62.8% |
| Authoritative `GameEngine.Apply` | 692 | 14.5% | 683 | 8.2% |
| End-of-game replay verification | 669 | 14.0% | 670 | 8.1% |
| Both agents observing the new state (tracker update) | 572 | 12.0% | 550 | 6.6% |
| Per-action state round trip (`FullMatchRunner.cs:45`) | 537 | 11.2% | 539 | 6.5% |
| Both agents' observation builds | 213 | 4.5% | 220 | 2.6% |
| The deciding seat's observation build | 100 | 2.1% | 104 | 1.3% |
| End-of-game replay JSON round trip | 3 | 0.1% | 4 | 0.0% |
| **Total** | **4,785** | | **8,292** | |

8 Standard games were 4,879 actions in 23.3 s (610 actions, 2.9 s a game); 8 Hard games were 4,059 actions in 33.7 s
(507 actions, 4.2 s a game). The two checks S0.2 makes optional (the per-action round trip and the replay verification)
are **25% of a Standard game** and 15% of a Hard one. The largest cost after them that isn't the agent's decision is the
agents observing every new state (12%), mostly the tracker update.

**A planner decision** (Hard games above: 561 plans, 5,967 rollouts):

| Planner segment | Mean | Share of planning |
|---|---:|---:|
| A plan (one decision, 3–4 samples × width 3) | 37.7 ms | |
| Simulated `Apply`, per step | 121 µs | 51.0% |
| Observation build and read before a heuristic choice, per non-forced step | 215 µs | 33.3% |
| Heuristic choice, per non-forced step | 56 µs | 8.6% |
| Leaf (observation build and fitted evaluation), per rollout | 217 µs | 6.1% |
| Dealing a sample (`ObservationDeterminizer.Sample`, tracker pins) | 75 µs | 0.7% |

The observation step splits about evenly (4 Hard games, re-profiled after S0.3): `ObservationBuilder.Build` 113 µs and
`ObservationReader.Read` 124 µs, the read being the JSON round trip that decodes the observation into features; in
whole-game rollouts the build is 15.7% and the read 17.4% of a step, against 54.8% for simulated `Apply`.

A turn rollout is 14.9 simulated steps and 3.5 ms; 61% of its steps are forced and skip the observation. The heuristic
itself is under a tenth of planning: the cost of a rollout is the engine and the observation it reads, which is what
the rollout policy (plan 4.3, S0.4) targets. Simulated `Apply` here is 121 µs, not the 47 µs of Wave 3's engine bench:
these are Core Battle Box positions with the full card pool's behaviours, not random-legal ones.

At single recorded roots (warm):

| Root | Hard decision | Whole-game rollout from it |
|---|---:|---|
| `strategy1-petty-cash` #101, Corp turn action (early) | 35.1 ms | 792 steps, 230 ms (290 µs/step) |
| `strategy1-match-point-draw` #329, Runner turn action | 51.0 ms | 314 steps, 94.5 ms (301 µs/step) |

**Whole-game rollouts** (the planner's rollout choices for both sides, to the game's end, from 6 evenly spaced turn
starts of each profiled game, 4 samples each; every one finished):

| From | Rollouts | Mean steps | Mean time | Per step | Simulated `Apply` | Observation | Heuristic |
|---|---:|---:|---:|---:|---:|---:|---:|
| Standard games' turn starts | 188 | 261 | 63.7 ms | 244 µs | 58.6% | 31.8% | 9.3% |
| Hard games' turn starts | 188 | 316 | 77.3 ms | 245 µs | 55.1% | 33.2% | 11.4% |

**What this sizes:**

- **S0.2 (ladder speed).** Single-threaded, a Standard box game is 2.9 s, 2.2 s without the two checks. 2,000 games on
  12 workers is then about 6 minutes if the workers scale linearly; S0.2 measures the real wall time, since past ladders
  (about 9 minutes for 240 deals) suggest they don't.
- **S0.3 (budget scaling).** A Hard plan is 38 ms at 3–4 samples, so ×16 is about 0.6 s a plan and about 21 s of
  planning for the ×16 seat per game (about 35 plans a seat). ×1/×4/×16, 480 deals a pairing in both seats, is about
  60,000 CPU-seconds, about 1.4 hours on 12 workers at linear scaling: inside S0.3's two-hour limit, narrowly. Note: the
  profiles cap `plan-samples` at 16 (`CorpPolicyProfiles`, `RunnerPolicyProfiles`), and ×16 needs 48 to 64, so S0.3's
  diagnostic entrants must bypass that validation.
- **S1.2 (the teacher).** At about 75 ms a whole-game rollout from an average turn start (230 ms early in a game), 32
  samples × about 4 meaningful candidates is about 10 s a root, and 8,000 roots about 21 CPU-hours: about 1.8 hours on
  12 workers at linear scaling, inside S1.2's four hours. S0.4's rollout policy should cut the observation third of it.
- **S5 (Standard as a planner).** A Runner Hard decision at a mid-game root is already 51 ms at 4 × 3. Standard at
  8 × 4 is about 2.7 times the rollouts; it fits 50 ms p95 only if the rollout policy roughly triples the step rate,
  which the table above says is not possible from the observation alone (a third of the cost): the simulated `Apply`
  itself has to get cheaper, or Standard plans fewer samples than 8.

### S0.2: the fast ladder mode (4 October 2026)

`ai-ladder --verify-replays-every N` (default 8) checks every Nth game as it is played (the per-action save-and-reload
and the replay's verification) and plays the rest unchecked; every result carries its final state hash.

**Gate: identical game by game.** Core Battle Box, `--entrants standard-c29,standard --deals 240 --workers 12`, seed 1,
once with `--verify-replays-every 1` and once with 8:

| Mode | Games checked | Wall time (build excluded) | Games a second |
|---|---:|---:|---:|
| Every game checked (today's behaviour) | 960 | 577 s | 1.66 |
| Every 8th game checked | 120 | 396 s | 2.42 |

All 960 games are identical between the two (deal seed, winner, action count, capped flag and final state hash), and
so are the ratings and differences; no errors. The fast mode is 31% faster, about what S0.1's 25% of a game predicts
once the decisions of the slower Corp (v29 and v30 both plan) are counted.

**The target is missed.** A two-entrant deal is four games (both entrants in both seats, the self-play pairings
included), so 1,000 deals is 4,000 games, not the 2,000 the plan assumed, and at 2.42 games a second takes about 28
minutes. Ten minutes buys about 1,450 games: **about 360 two-entrant deals**. Later steps size their ladders by that.

**Why: the workers don't scale.** 24 deals (96 games), fast mode:

| Workers | Wall time | Speed-up |
|---|---:|---:|
| 1 | 236 s | 1.0× |
| 6 | 56.5 s | 4.2× |
| 12 | 50.5 s | 4.7× |
| 12, workstation GC (`DOTNET_gcServer=0`) | 103.2 s | 2.3× |
| 12, 256 MB gen 0 (`DOTNET_GCgen0size`) | 46.9 s | 5.0× |

Past six workers the box gains little (the full 240-deal run reached 5.9×, with less tail at the end of a batch). The
VM has 14 vCPUs sharing one 16 MB L3 and 9 GB of memory, so the likely limit is memory traffic, not the GC mode (the
workstation GC is twice as slow; a larger gen 0 gains 7%, within the noise of a 96-game run). The largest remaining
cost that isn't a decision is the agents reading every new state (S0.1: 12% of a game), and it is also the most
allocating: `ObservationReader.Read` decodes each observation by a JSON round trip and `Digest` hashes canonical JSON.
That is the agent's own work, so S0.2 leaves it; S0.4's thin view for rollouts is the first place to cut it, and the
same view would serve `OfflineAgent.Observe`.

### S0.3: budget scaling on the shipped leaf (4 October 2026)

Entrants `standard-x1`, `-x2`, `-x4` and `-x8`: Hard's planner at 1, 2, 4 and 8 times its samples (Corp 3 → 24 samples
of width 3 on set 4, dealing from the tracker and the Runner's list; Runner 4 → 32 samples of width 3 on set 3), each
against each in both seats on the Core Battle Box. S0.1's costs put the default run (×1, ×4, ×16 at 480 deals) at about
4.3 hours on this box, so the step's fallback ran: ×1 to ×8 at 240 deals, `--batch-deals 48 --stop-when-clear`.

```bash
scripts/ai-ladder.sh --content gateway-elevation --entrants standard-x1,standard-x2,standard-x4,standard-x8 \
  --deals 240 --batch-deals 48 --stop-when-clear --workers 12
```

3,840 games (no stop: no seat separated), no errors, none capped, 2.6 hours. Elo against the next smaller budget, with
the 95% interval:

| Seat | ×2 over ×1 | ×4 over ×2 | ×8 over ×4 | ×8 over ×1 |
|---|---|---|---|---|
| Corp | −8 [−39, 18] | −5 [−33, 21] | +3 [−23, 32] | −10 [−36, 15] |
| Runner | −18 [−52, 7] | +2 [−22, 28] | −1 [−30, 22] | −17 [−47, 8] |

**Reading: not monotone in either seat.** Eight times the samples is no stronger than one, for the Corp and for the
Runner alike; every point is at or below ×1. The plan expected the Corp's result and a weak gain for the Runner; there
is none. Runner Hard's 80 Elo over Standard therefore comes from planning at all (the rollout's view of the turn) and
not from a more accurate mean of it: what more samples sharpen is a leaf difference that carries no ranking signal,
scored as a raw log-odds term added to the heuristic's score. This is the plan's diagnosis (section 1.3) measured, and
it puts S4 (the leaf) ahead of any budget change; S5 starts from Standard's budget, not a higher one.

### S0.4: rollout cost, cut where the trace says (4 October 2026)

**The trace.** `dotnet-trace collect --profile dotnet-sampled-thread-time` over `engine-bench --profile-plan
strategy1-match-point-draw --sequence 329 --profile hard --repeats 60 --rollout-samples 32` (60 Runner Hard decisions
and 32 whole-game rollouts, 14.7 s), inclusive time:

| Function | Share of all time |
|---|---:|
| `GameEngine.ApplyCore` (simulated and the replay to the root) | 57.7% |
| `BehaviorCatalogFingerprint.ComputeDefinition` (under `MaterializeAbilityActionParameters`) | 16.3% |
| `ObservationReader.Read` | 13.7% |
| `ObservationBuilder.Build` (95% of it `BuildPublicState`) | 12.7% |
| `RunnerHeuristicPolicy.EvaluateDecision` (the agent's own heuristic, at the root and in rollouts) | about 10% |
| `BehaviorStaticModifierProjection.Sum` | 6.0% |
| `TurnFactsState` copies | 4.9% |

Within `ApplyCore`: `Advance` 62%, `ConsumeAction` 11%, `DeterministicHash.ForState` 8%,
`StateInvariantValidator.Validate` 7%, building the working state 8%. Within the read: JSON parsing 47%, JSON writing
35%, and the legal actions' own parameter round trip 13%.

**The changes**, both exact:

- `BehaviorCatalog.GetDefinitionFingerprint` computes each definition's fingerprint once (the catalog and its
  definitions are immutable); it was recomputed, a SHA-256 over a rebuilt string, for every issued ability action and
  behaviour frame.
- `ObservationReader` builds its decoding options once and decodes from UTF-8 bytes (about a tenth of a read: since
  .NET 7, copies of equal options already share their metadata, so the copy per call was cheaper than it looked).

**Costs after** (the same seeds and games as S0.1; the same action counts, 4,879 and 4,059):

| | S0.1 | S0.4 | Change |
|---|---:|---:|---:|
| A Hard plan | 37.7 ms | 33.2 ms | −12% |
| Simulated `Apply`, per step (Hard games) | 121 µs | 99 µs | −18% |
| Whole-game rollout from a Standard game's turn start | 63.7 ms | 53.8 ms | −16% |
| Whole-game rollout from a Hard game's turn start | 77.3 ms | 67.4 ms | −13% |
| Authoritative `Apply` (Standard games) | 692 µs | 648 µs | −6% |
| A Standard box game, per action | 4,785 µs | 4,505 µs | −6% |

**Gate: identical game by game.** The 240-deal box ladder of S0.2 (`standard-c29,standard`, every 8th game checked)
again: all 960 games identical to S0.2's fast-mode run (deal seed, winner, action count, final state hash), the same
ratings, no errors; 347 s against 396 s (−12%), so about 410 two-entrant deals now fit in 10 minutes.

**Shelved:** an exact direct observation-to-features mapper (at most the read's 14%, but a hand-written copy of the
serializer's semantics over some forty record types) and a thin state-to-features view. The rest of the engine's
simulation cost is step S5.0 of [BOT_PLAN_STEPS.md](BOT_PLAN_STEPS.md), before S5.1.

**For S1.2:** a whole-game rollout from an average turn start is now about 55 to 67 ms, so the teacher at 32 samples of
about 4 candidates is about 8 s a root, and 8,000 roots about 18 CPU-hours: at the 5.9× the box's 12 workers measured
in S0.2, about 3 hours, inside S1.2's four.

## The regret suite

### S1: the Core Battle Box suite (5 October 2026)

**Source.** 960 Standard box games, `scripts/ai-ladder.sh --content gateway-elevation --entrants standard-c29,standard
--deals 240 --save-games` (Runner v26 in every game, Corp v29 or v30). 190 MB of replays; the suite stores each game as
its deal and the index of every chosen action (`games.jsonl.gz`, 210 KB), checked against the game's final state hash.

**Roots** (`ai-regret roots`, 9.5 minutes on 12 workers): per game, 7 turn-action roots drawn from those whose two best
candidate groups (by the latest Standard heuristic) have equal priority and scores within 20%, and 2 run-choice roots.
Candidates are grouped by kind and parameters with card instances read as definitions, so copies of one card are one
choice; the heuristic's best 6 groups are labelled, and a version's choice outside them counts as unlabelled. A deal's
four games share positions until play diverges: 24% of the drawn roots repeated an earlier one, so `ai-regret dedupe`
keeps a position once and holds a deal out whole. **6,512 roots** (Runner 3,960, Corp 2,552; 1,443 held out), plus the
23 box findings of `data/ai/regret/findings.json` (`ai-regret findings`; the 5 NSG Core findings wait for an NSG suite).

**Teacher.** The latest Hard bot of the acting side, having watched the game: samples dealt from its tracker's pins and
the opponent's list, both sides playing the planner's rollout choices. Every candidate is played on the same samples: a
deal where one candidate cannot be played (a nested choice naming a card in a hidden zone the deal replaced) is dropped
for all of them and another dealt, up to twice the samples asked for, and a root with no such deal is left unlabelled
(the review of #101). Two findings were affected: `loop1-002#131` (a Corp trigger) and `loop2-006#140` (Muslihat's
search of the stack) now have 4 and 6 common play-outs instead of 3 against 32. Keeping the cards a viewer's own
pending choice names when the sampler deals is the fix at the root, in `ObservationSampler`, and is left for later.

| Horizon (8 samples, 180 roots) | Median standard error | Median gap, best two | Best tied | Time |
|---|---:|---:|---:|---:|
| Game's end | 0.164 | 0.000 | 64% | 128 s |
| 4 turns, then the leaf (set 4) | 0.059 | 0.023 | 17% | 51 s |

At 32 samples the game's-end horizon projects past S1.2's four hours on this box, and its labels are mostly ties, so
the suite uses the step's fallback: **32 samples, 4 turns then the leaf**. Labelling 8,554 roots took **2 h 6 min** on
12 workers (7,564 s).

**Noise floor** (200 roots labelled again with an independent seed): the two labellings pick different best groups at
**51%** of roots, with a median standard error of 0.031. The roots were drawn where the heuristic's top two are close,
so their best two are often level: the cost of a flip, the first labelling's best minus its value of the second's best,
is 0.018 on average (median 0, 90th percentile 0.061), against a mean spread of 0.084 between a root's best and worst
groups. Agreement therefore does not rank versions here; regret, compared paired on the same labels, does.

**Gate: the suite separates what the ladder could not.** Standard profile, all 6,512 roots, about 2 minutes a version on
12 workers. Paired differences of mean regret (first − second) over the roots both versions' choices are labelled at,
with 200-draw bootstrap intervals:

| Comparison | Regret difference | Ladder (240 deals, Elo) |
|---|---|---|
| Runner v24 − v25 | +0.0025 [0.0019, 0.0033] | v25 over v24: +6 [−28, 49] |
| Runner v24 − v26 | +0.0024 [0.0017, 0.0033] | |
| Runner v25 − v26 | −0.0001 [−0.0002, 0.0002] | v26 over v25: +3 [−35, 52] |
| Corp v28 − v29 | +0.0005 [−0.0002, 0.0012] | v29 over v28: +12 [−23, 54] |
| Corp v28 − v30 | +0.0013 [0.0004, 0.0024] | |
| Corp v29 − v30 | +0.0008 [0.0001, 0.0017] | v30 over v29: +50 [10, 96] |

Runner v24 against v25, which the ladder left unresolved, separates clearly, as do v24 against v26 and Corp v28 against
v30; and the suite agrees with the one change the ladder did resolve (Corp v30 over v29). Where the gains are: Runner
v25's over v24 in turn actions (regret 0.0469 → 0.0438), Corp v29's over v28 in run choices (0.0204 → 0.0192), Corp
v30's in turn actions (0.0354 → 0.0348). v26's self-flatline fixes are too rare to show at the suite's size (v25 and v26
are level).

| Version (Standard) | Regret, all | Turn | Run | Held out | Agreement | Unlabelled |
|---|---|---|---|---|---|---:|
| Runner v24 | 0.0402 [0.0385, 0.0419] | 0.0469 | 0.0110 | 0.0387 | 0.250 | 68 |
| Runner v25 | 0.0377 [0.0361, 0.0394] | 0.0438 | 0.0108 | 0.0351 | 0.259 | 41 |
| Runner v26 | 0.0380 [0.0365, 0.0398] | 0.0442 | 0.0108 | 0.0357 | 0.259 | 0 |
| Corp v28 | 0.0296 [0.0272, 0.0324] | 0.0354 | 0.0204 | 0.0270 | 0.332 | 88 |
| Corp v29 | 0.0292 [0.0265, 0.0319] | 0.0354 | 0.0192 | 0.0266 | 0.337 | 88 |
| Corp v30 | 0.0290 [0.0261, 0.0319] | 0.0348 | 0.0192 | 0.0259 | 0.333 | 0 |

**Bias to remember.** The labelled groups are the latest heuristic's best six, so the latest versions are never
unlabelled; an older version's unlabelled roots (likely its worse choices) drop out of its paired comparison, which
flatters the older version. The ordering above is therefore conservative.

**The first blunder list** (`ai-regret score --blunders 5`), the highest-regret roots of the current defaults. Most are
missed wins the 4-turn horizon sees and the one-step heuristic does not:

| Version | Root | Regret | Chose | Teacher's best |
|---|---|---:|---|---|
| Runner v26 | `…leo-glacier~…muslihat-00215#1295` | 0.75 | Run server:7 (0.25) | Play card 30012 on server:5176 (1.00), or run it (0.97) |
| Runner v26 | `…leo-glacier~…muslihat-00215#1263` | 0.61 | Gain 1 credit (0.39) | Run server:5176 (1.00) |
| Runner v26 | `…untaian-shell-game~…topan-00028#565` | 0.48 | Topan install ability (0.09) | Gain 1 credit (0.57) |
| Runner v26 | `…zwicky-supermodernism~…muslihat-00068#708` | 0.47 | Install card 30008 (0.05) | Play card 30011 (0.52) |
| Corp v30 | `…leo-glacier~…magdalene-00108#486` | 1.00 | Advance card 30069 (0.00) | Gain a credit or install ICE (1.00) |
| Corp v30 | `…zwicky-supermodernism~…topan-00223#305` | 1.00 | Install card (0.00) | Play card 35078 (1.00) |
| Corp v30 | `…nebula-fast-advance~…topan-00085#1011` | 0.94 | Pass priority (0.06) | Rez card 35082 (1.00) |
| Corp v30 | `…untaian-shell-game~…magdalene-00030#561` | 0.88 | Install 35051 in a new remote (0.13) | Install it in server:1516, or advance (1.00) |

## The puzzle suite

### S2: puzzle suite v2 (5 October 2026)

**Format** (`data/ai/puzzles/<pool>`): the regret suite's compact games and `puzzles.json`. A puzzle is a root (a game
and a sequence), a kind, one of the plan's ten categories, a split, and an acceptance:

- `decision`: the bot's choice must match one of `accept` (action kind and a subset of its parameters, card instances
  read as definitions) and none of `reject`. A negative control rejects the tempting action instead of naming the
  answer.
- `line`: the turn is played out from the root by both sides' production bots, and every predicate holds over the puzzle
  side's actions and the state at the turn's end (`installed`, `counters`, `credits`, `grip`, `successfulRun`, `action`,
  `before`, `precedes`, `first`).
- `nested`: the line is played to the first decision of the puzzle side where an action matches `at`, and the choice
  there is judged as a decision.

`ai-puzzles run` scores a set per category; `show`, `find` and `import` are the authoring aids (`show --teacher N`
prints the teacher's value of every choice at a root).

**Content.**

- The 28 saved-decision tests of loops 1 and 2 and strategy 1, as decision puzzles (23 box, 5 NSG Core), split `tuning`:
  the bots were fixed against them. Corp v26 with Runner v22 pass 1 of the 23 box puzzles; the defaults pass all 28.
- 26 **teacher-certified** box puzzles: regret suite roots where the best group led the next by 0.15 or more at 32
  samples, relabelled at 128 samples with an independent seed and kept where the lead held (0.15 or more, the best's
  standard error 0.04 or less; 54 of 95 held) and the accepted actions match no clearly worse group; capped at 8 a
  category. 14 are negative controls: the heuristic's first choice is clearly worse and is rejected. A third (10) is
  held out by a hash of the id.
- **Deferred findings the one-step teacher cannot judge** (none is made a puzzle). The teacher values a first action
  followed by the heuristic's play, so an action whose worth depends on the next click is undervalued whenever the
  heuristic misplays that click, and no horizon fixes it. Petty Cash as the turn's first action, at 23 lean-credit Corp
  turn starts: level with the best alternative or worse, by up to 0.23, at 64 samples and a 4-turn horizon; relabelled
  at 128 samples the sign does not move with the horizon (mean gap −0.023 at 4 turns, −0.027 at 8; 15 level, 8 behind,
  1 ahead at 8). At the worst root (`…leo-glacier~…topan-00202#358`) the gap is the continuation's: with
  `ai-puzzles show --play`, Petty Cash then its best second action scores 0.477 against 0.583 for the best line without
  it, and the two full three-click lines (Petty Cash, install the agenda in the existing remote, install the asset in a
  new one; or the two installs and a credit) leave the Runner at 0.840 ±0.018 and 0.818 ±0.019 at its next decision:
  level. Ritual first (20 Runner turn starts, level or worse in 18) is open to the same bias. Spin Doctor's
  remove-from-game ability is not offered at the 20 turn starts found, so that line cannot be posed there. The loop 2
  games these findings came from no longer replay under today's card behaviour (an action becomes illegal within the
  first 115), so their exact positions could not be used. Mercia's free install, a second Jailbreak on an unchanged R&D,
  GAMEDRAGON™ Pro and the NSG Core items (Synapse Global, Touch-ups) wait for positions.
- **The same bias in the certified puzzles and the regret suite.** The 26 teacher-certified puzzles and the suite's
  labels come from the same one-step teacher, so some may be continuation artifacts, most likely where the right action
  needs a good follow-up in the same turn. They are kept with their source recorded; a teacher that plays the root's own
  turn by a search, not the heuristic, is the fix (plan section 9).

**Baseline** (Standard; box set 49 puzzles, 10 held out; NSG Core set 5):

| Category | v30 / v26 | v29 / v25 | v28 / v24 |
|---|---|---|---|
| sequencing | 2/2 | 0/2 | 0/2 |
| agenda exposure | **0/4** | 0/4 | 0/4 |
| punisher risk | 9/9 | 7/9 | 7/9 |
| run targeting | 4/8 | 4/8 | 4/8 |
| economy | 5/8 | 5/8 | 6/8 |
| access and trash | 3/3 | 3/3 | 3/3 |
| rez timing | 6/10 | 6/10 | 6/10 |
| hand management | 5/5 | 5/5 | 4/5 |
| **Box, all** | **34/49** | 30/49 | 30/49 |
| Box, held out | 5/10 | 5/10 | 5/10 |
| NSG Core | 5/5 | 5/5 | 1/5 |

**Gate:** agenda exposure is at 0 of 4 for the defaults, so the suite has room to move. The teacher-certified puzzles
score the same for all three version pairs (the heuristic's choice at those roots did not change between them); they are
what the planner (S5) is expected to move.

**Short of the target.** 54 puzzles against 60, 8 of the 10 categories (no ICE placement or inference puzzle yet: no ICE
install root held the teacher's lead at 128 samples, and inference needs a position where the right choice depends on a
card the bot can only infer), and 10 of 54 held out against a third (the converted tests cannot be held out). The NSG
Core set has only the 5 converted tests; certified puzzles for it need an NSG regret suite.

## Policy versions

### S3: one live policy (5 October 2026)

**Corp v31 and Runner v27** are the live policies: v30's and v26's behaviour with every version check resolved and the
dead branches deleted. Every earlier version is `LegacyCorpPolicy` or `LegacyRunnerPolicy` under
`src/Netrunner.AI/Legacy/`: the policy files of 94a7404, changed only by the class's name, which
`PolicyVersionTests.TheLegacyFilesAreThePoliciesFrozenAtS3` pins by hash. `OfflinePolicies.Create` routes by version;
the profiles give v31 and v27 v30's and v26's settings; the ladder's `standard-prev` entrant is v30 and v26.

| Policy files | Lines | `Generation` references |
|---|---:|---:|
| Before (9 files, every version) | 2,760 | 135 |
| Live after (Corp v31, Runner v27) | 2,583 | 4 (the context's generation, pinned) |
| Legacy (frozen copies) | 2,760 | 135 |

Besides the resolved checks, the Runner's run code lost its first estimate (`Protection`) and its approach-toll
fallback, which the whole-run model replaced in every live game, and its v16 rules for Creative Commission and Docklands
Pass; the Corp lost v24's end-the-run subroutine value. Those versions were never defaults.

**What stays versioned: the shared helpers.** `HeuristicContext`, `RunnerValues` and `RunModel` still switch on the
generation (about 20 checks), because every helper of the heuristic stack takes a `HeuristicContext` and freezing them
would split shared types (`CardTracker`, the run model) across a type boundary. The live policies pass v30's and v26's
generation (25 and 22). A later change to a helper must leave the older generations' behaviour alone, which the frozen
benchmark digests and the identity ladder below check, or move that helper into `Legacy/` first.

**Gate: identical game by game.** `--entrants standard-prev,standard --deals 240 --verify-replays-every 8`: each deal's
four games (v31 or v30 against v27 or v26) must end in the same state.

| Pool | Deals | Games | Deals whose four games are identical | Errors |
|---|---:|---:|---:|---:|
| Core Battle Box | 240 | 960 | 240 | 0 |
| Gateway | 240 | 960 | 240 | 0 |

The seat-swap pairs are identical because an agent's random stream is seeded from the match seed and its side only;
`RatingLadderTests.TheLiveAndThePreviousVersionShareTheirSeeds` asserts it, so a change that seeds from the version
fails there instead of voiding this gate. `make bot-check` passes (AI 136, Match 1,107, CLI 185), the quick M5 cases
unchanged.

### S1.4: every meaningful group (5 October 2026)

`ai-regret extend` gave every root of the box suite its meaningful groups (those whose priority band is within one of
the best group's, a planned candidate read through the planner's offset): 32,821 groups became 57,109 (5.0 to 8.8 a
root), in 2 min 49 s. The new groups have no labels yet; S1.5 labels them all. `ai-regret score --label-missing`
labelled, on demand, every group the S1.3 versions chose that had no value (68 Runner, 88 Corp, each with its root's
settings and seed), so the table below counts every choice:

| Comparison | S1.3 (choices outside the top six dropped) | S1.4 (every choice labelled) |
|---|---|---|
| Runner v24 − v25 | +0.0025 [0.0019, 0.0033] | +0.0030 [0.0023, 0.0038] |
| Runner v24 − v26 | +0.0024 [0.0017, 0.0033] | +0.0029 [0.0021, 0.0039] |
| Runner v25 − v26 | −0.0001 [−0.0002, 0.0002] | −0.0001 [−0.0004, 0.0002] |
| Corp v28 − v29 | +0.0005 [−0.0002, 0.0012] | +0.0005 [−0.0001, 0.0011] |
| Corp v28 − v30 | +0.0013 [0.0004, 0.0024] | +0.0016 [−0.0004, 0.0033] |
| Corp v29 − v30 | +0.0008 [0.0001, 0.0017] | +0.0011 [−0.0005, 0.0028] |
| Runner v26 − v27, Corp v30 − v31 | | 0.0000 [0.0000, 0.0000] |

**Gate:** no version has an unlabelled choice. The bias the S1.3 record named is confirmed: counting the older versions'
choices outside the heuristic's best six raises every difference. Those choices are rare and costly, so the Corp's
intervals also widen past zero; the Runner's separation holds. The live v31 and v27 choose as v30 and v26 at every root
(S3's agreement check).

| Version (Standard) | Regret, all | Held out |
|---|---|---|
| Runner v24 | 0.0410 [0.0393, 0.0430] | 0.0399 |
| Runner v25 | 0.0380 [0.0364, 0.0397] | 0.0354 |
| Runner v26, v27 | 0.0381 [0.0365, 0.0399] | 0.0359 |
| Corp v28 | 0.0313 [0.0285, 0.0344] | 0.0295 |
| Corp v29 | 0.0309 [0.0281, 0.0338] | 0.0291 |
| Corp v30, v31 | 0.0297 [0.0268, 0.0327] | 0.0263 |

### S1.5: the turn-line teacher (6 October 2026)

**What the teacher values now.** At a turn-action root, a candidate is valued at its best line of the root side's turn,
not at the candidate followed by the heuristic's play of the rest of the turn. A line is a rank path: the candidate,
then the heuristic's k-th distinct choice (copies of one card are one choice) at each later turn action of that turn.
Nested choices, the opponent and later turns are played as before. `TurnPlanner.BestLine` runs a beam of 5 over rank
paths, screens every line with a one-turn play-out on a quarter of the samples, then plays the heuristic's own line and
the best other line to the 4-turn horizon on every sample, and keeps the better. Elsewhere (run, rez and priority
choices) a candidate is valued as before. Lines that leave the turn in the same state share one play-out
(`EngineJson.SearchFingerprint`, an in-process fingerprint that also covers simulated states;
`RegretSuiteTests.LinesThatLeaveTheTurnInOneStateHaveOnePlayOut` checks that equal fingerprints give equal play-outs).
The shipped planner has the same search behind `plan-lines`, which no profile sets (Hard and every frozen digest are
unchanged; the twin tests pass with it on).

**Choosing the settings.** The spec's search, a beam of `plan-width` (3) with every line screened by a full play-out on
a quarter of the samples and the better half played on the rest, did not remove the Petty Cash artifact, and cost more
than the spec's estimate. At the worst root (`…leo-glacier~…topan-00202#358`, 128 samples, 4-turn horizon):

| Search | Petty Cash first | Best group |
|---|---:|---:|
| One-step (S1.2) | 0.257 | 0.487 |
| Beam 3, full play-out screen, better half | 0.388 | 0.509 |
| Beam 4, the same | 0.489 | 0.564 |
| Beam 5, the same | 0.615 | 0.657 |
| Beam 8 (every distinct choice at this root) | 0.615 | 0.657 |
| Beam 5, screened by the leaf at the turn's end, two finalists | 0.257 | 0.564 |
| **Beam 5, one-turn screen on a quarter, two finalists** | **0.615** | **0.618** |

The line Petty Cash needs takes the heuristic's 4th and 5th choices at the next two clicks, so a beam of 3 cannot find
it; the heuristic orders a turn's later clicks poorly (S5.0 records what that means for Standard). The leaf at the
turn's end cannot see why the line is good; a one-turn play-out can. Cost on a 120-root sample of the suite (32 samples,
12 workers):

| Labelling | Time | Against one-step |
|---|---:|---:|
| One-step | 212 s | 1.0× |
| Beam 3, full play-out screen, better half | 1,187 s | 5.6× |
| The same with the play-out memo (labels identical) | 981 s | 4.6× |
| Beam 5, one-turn screen on every sample, two finalists | 1,304 s | 6.2× |
| **Beam 5, one-turn screen on a quarter, two finalists** | **582 s** | **2.75×** |

**Relabelling** the whole box suite (6,512 roots, all 57,197 meaningful groups, none unlabellable) took **7 h 9 min** on
12 workers, at a peak of 1.1 GB. A first run was stopped by the machine's memory reaper at about half way, with nothing
saved, while an unrelated build held 7 GB; `ai-regret label` now rewrites its file every 50 games, whole or not at all,
and `--missing` resumes it. The S1.2 labels are kept as `labels-v1.jsonl.gz` (and `labels-v1-repeat*`); the line
labels are the suite's `labels.jsonl.gz`.

**The Petty Cash check** (the 24 roots of S2's finding, 128 samples, 4-turn horizon): Petty Cash first is within noise
of the best group (the gap at most the two standard errors' sum) at **18 of 24** roots, against 13 for the one-step
teacher; the mean gap falls from 0.026 to 0.010 and the largest from 0.230 to 0.060. The worst root's gap is 0.003.
Of the 6 roots still behind (by 0.014 to 0.060), 5 keep Petty Cash's heuristic line as its best and their gap is the
one-step gap: there Petty Cash is likely worse, not undervalued.

**Recertification** of the 26 teacher-certified puzzles at 128 samples with the line teacher (the same rule: the best
group passes, its standard error at most 0.04, every failing group 0.15 or more behind): **24 hold**. Two move to a
`teacher-v1` split, which `ai-puzzles run` leaves out of the gate (`--split teacher-v1` runs them):
`teacher-7a456081`, whose rejected install now reaches the best line too (0.656, lead 0.000), a continuation artifact
of the one-step teacher; and `teacher-00152981`, whose rejected run's best line closes the lead to 0.142. The
defaults failed both, so they pass 34 of the gate's 47 box puzzles (34 of 49 before).

**Drift** (the 32,977 groups both teachers label; the line teacher never values a group below the one-step teacher on
the same samples, and 21,240 are unchanged):

- The other line won at **35.6%** of those groups (40.4% at turn roots; 37.6% of all 57,197).
- Mean gain **+0.0167** a group; past twice its standard error (unpaired, so conservative) at 4.0% of groups, by
  +0.140 on average, which is **+0.0055** a group.
- The best group changed at **22.6%** of roots, below the noise floor (next), so it says nothing on its own. The best
  value rose by **+0.0108** on average, and by **+0.0023** counted only where the rise passes twice its standard
  error.

**The labels carry a lift.** Each group's value is the better of two play-outs on the same samples, which sits above
the better line's true value by about half a standard error where the two are close, as these roots were drawn to be.
Most of the mean rise above is that lift. Every group gets the same treatment, so paired regret between versions stays
fair, but values are not comparable with `labels-v1`'s.

**Noise floor** with the line teacher (the 145 roots of the S1.2 repeat, repeat seed 2): the two labellings pick
different best groups at **57%** of roots (one-step on the same roots: 50%), median standard error 0.032; a flip costs
0.024 on average (median 0.005, 90th percentile 0.068) against a mean spread of 0.121. The line labels have 8.8 groups
a root against the one-step labels' 6 there, so more flips are expected.

**Gate: S1.3's separation on the new labels.** Every choice of every version is labelled (no on-demand labels needed),
which is S1.4's gate.

| Comparison | S1.4 (one-step, every choice) | S1.5 (line teacher) |
|---|---|---|
| Runner v24 − v25 | +0.0030 [0.0023, 0.0038] | +0.0030 [0.0022, 0.0038] |
| Runner v24 − v26 | +0.0029 [0.0021, 0.0039] | +0.0030 [0.0022, 0.0038] |
| Runner v25 − v26 | −0.0001 [−0.0004, 0.0002] | 0.0000 [−0.0004, 0.0004] |
| Corp v28 − v29 | +0.0005 [−0.0001, 0.0011] | +0.0005 [−0.0001, 0.0011] |
| Corp v28 − v30 | +0.0016 [−0.0004, 0.0033] | +0.0008 [−0.0007, 0.0025] |
| Corp v29 − v30 | +0.0011 [−0.0005, 0.0028] | +0.0003 [−0.0009, 0.0017] |
| Runner v26 − v27, Corp v30 − v31 | 0.0000 [0.0000, 0.0000] | 0.0000 [0.0000, 0.0000] |

The Runner passes: v24 separates from v25 and v26 as before. **The Corp does not**: no Corp pair separates, as at S1.4
(where counting every choice already widened the Corp's intervals past zero), and the line teacher narrows the
differences further. The Corp's versions differ in few turn-action choices (v28 and v29 regret the same 0.0406 at turn
roots, v30 0.0402), and the line teacher rates more of a turn's orderings alike, which is what it is for. Separating
the Corp's versions needs more Corp roots where they choose differently; no step of the plan adds them yet, so this
gate is open for the Corp.

| Version (Standard) | Regret, all | Turn | Run | Held out | Agreement |
|---|---|---|---|---|---|
| Runner v24 | 0.0477 [0.0458, 0.0496] | 0.0559 | 0.0110 | 0.0495 | 0.220 |
| Runner v25 | 0.0447 [0.0427, 0.0463] | 0.0524 | 0.0108 | 0.0447 | 0.228 |
| Runner v26, v27 | 0.0447 [0.0428, 0.0464] | 0.0523 | 0.0108 | 0.0448 | 0.227 |
| Corp v28 | 0.0331 [0.0304, 0.0362] | 0.0406 | 0.0204 | 0.0338 | 0.293 |
| Corp v29 | 0.0327 [0.0300, 0.0355] | 0.0406 | 0.0192 | 0.0334 | 0.298 |
| Corp v30, v31 | 0.0324 [0.0297, 0.0354] | 0.0402 | 0.0192 | 0.0307 | 0.290 |

Turn-root regret rises for every version (Runner v26 0.0442 → 0.0523, Corp v30 0.0360 → 0.0402): the line teacher finds
better turns than the heuristic plays, and the lift adds to it. Run-root values are unchanged.

**Known limits, for S5 and S6.** Only one line besides the heuristic's is played to the horizon; if the one-turn screen
misranks, the best line is missed, and a blunder-list root that turns out to be a missed line is the sign to revisit
it. Without the teacher's play-out, `plan-lines` takes the best of every screened line, which is more optimistic than
the teacher and favours candidates whose turn has more later clicks (S5.1's gates check it).

**The Corp's separation: why it fails (6 October 2026).** Regret below is on the line teacher's labels unless named.
`ai-regret score --discordant A,B` lists the roots where two versions chose different groups:

| Pair (Standard, all roots) | Discordant roots | Over the discordant roots | Over every root |
|---|---|---|---|
| Corp v29 − v30 | 272 (all turn roots) | +0.0029 [−0.0087, 0.0149] | +0.0003 [−0.0009, 0.0017] |
| Corp v28 − v30 | 324 (272 turn, 52 run) | +0.0060 [−0.0070, 0.0198] | +0.0008 [−0.0007, 0.0025] |
| Corp v29 − v30, `labels-v1` | 272 | +0.0108 [−0.0017, 0.0245] | +0.0011 [−0.0005, 0.0028] |

272 discordant roots are enough to read, so this is not dilution: v30 is level where it chooses differently, and the
one-step teacher did not separate the pair either. v30's change from v29 was four rules (Petty Cash first, IP
Enforcement, Mercia's free install, an agenda held in HQ); the ladder's +50 Elo was for the four together, never
attributed. Split by agenda installs (value of v30's choice − v29's, two groups relabelled at 128 samples):

| Discordant roots | Roots | 4 turns | 8 turns |
|---|---:|---|---|
| v29 installs an agenda in a remote, v30 holds it | 119 | −0.0021 [−0.0280, 0.0285] | −0.0005 [−0.0279, 0.0299] |
| No agenda install | 144 | +0.0056 [+0.0001, 0.0116] | +0.0059 [−0.0003, 0.0127] |
| Both install an agenda | 8 | −0.0089 | +0.0032 |

v30 is ahead where no agenda is involved and level on the agenda hold. The agenda roots' interval is wide (it cannot
exclude +0.02 a decision, about what a 50 Elo gain spread over a few decisions a game would be), so "level" is a weak
reading. **The horizon is closed as a suspect:** at 8 turns neither mean moves.

**The rollout's Runner is the blind spot.** `ai-regret exposure` follows every agenda the Corp installs in a remote
until it is scored, stolen or trashed, and asks the run model, at the Runner's next turn, whether the Runner can get in
(unrezzed ICE is counted, not priced). Over the suite's games (3,779 agendas):

| Agendas in a remote | Count | Run at least once | Stolen | Scored |
|---|---:|---:|---:|---:|
| The Runner can get in, every ICE known | 299 | 43% | 36% | 60% |
| The Runner can get in, 1 unrezzed ICE | 1,337 | 29% | 17% | 78% |
| The Runner can get in, 2 or more unrezzed | 1,136 | 44% | 16% | 76% |
| The Runner cannot get in | 485 | 9% | 5% | 87% |
| Scored before the Runner's turn | 522 | 0% | 0% | 99% |

Where every ICE is known and the run model says the Runner gets in, it runs fewer than half of those remotes, and
steals 107 of the 129 it runs. In the teacher's own play-outs of v29's install at the 119 agenda roots (128 samples, 4
turns; 15,232 play-outs), the Runner runs the agenda's remote in **19%** of them, steals it in 14%, and the Corp scores
it in 62%; when it runs, it steals 75%. Neither instrument's Runner punishes an exposed agenda: the ladder's is the live
Standard (v26 and v27 here), and the teacher's rollouts model the Runner as v7, the planner's opponent model
(`RunnerHeuristicPolicy.LatestHeuristic`, kept so the shipped Corp planner plays as it did; the Hard teacher sets no
`plan-model`, and at Runner roots it models the Corp as v8 in the same way). So the agenda-hold rule is neutral in
self-play: neither Runner punishes what it guards against (an outside player did, which is why v30 has the rule).

**What closes the Corp gate.** Not a relabel at a longer horizon: a Runner that values a remote the Corp has just
installed into and that it can get in to (Runner v28, a live-policy change gated as any other), and a teacher whose
rollouts model the opponent by the live Standard version (`plan-model 1`), after which the suite is relabelled and the
agenda roots are read again. S1.3's gate stays open for
the Corp until then. S1.6, the targeted-roots sampler, is still worth building for S4.2 and S5.3, but it does not fix
this.

### Runner v28: a face-down remote card is the agenda it may be (6 October 2026)

**Why.** The S1.5 follow-up found that the bot Runners, the live one on the ladder and v7 in the teacher's rollouts,
rarely punish an agenda in a remote, so neither instrument could see the Corp's exposure (an outside player could). The
base rate, from the Core Battle Box suite's games (every card the Corp installed in a remote, unadvanced and face down
at the Runner's next turn; `ai-regret exposure --all-roots`):

| The remote's ICE | Cards | Agenda | Asset | Ambush | Upgrade | Runner v27 runs it at its next turn |
|---|---:|---:|---:|---:|---:|---:|
| None | 1,630 | 0% | 68% | 25% | 7% | 38% |
| 1 | 741 | 71% | 15% | 2% | 13% | 10% |
| 2 | 773 | 78% | 4% | 0% | 19% | 12% |
| 3 or more | 298 | 59% | 0% | 0% | 41% | 11% |

The agenda column counts Tomorrow's Headline, an agenda that is also an ambush (3% behind one or two ICE). The Corp
protects agendas and leaves assets and ambushes naked; the Runner did the opposite. Asked at those decisions (`--version
runner-heuristic-v27`), it scored a run on a protected agenda remote it could get into (every ICE known) at a median of
−720 against 190 for a naked asset: every face-down card was worth a flat 190, which never pays for the ICE. (The
exposure count asks the run model at the Runner's next turn only, not "that turn or the next", so the 43% the S1.5
record quotes is the share of remotes run at all while the card stayed, from the next turn on.)

**The change** (`RunnerValues.ServerV2` at generation 23, `RunnerInference.RootAgenda`). An unadvanced face-down card in
a remote is worth 190 plus the chance it is an agenda times an agenda's value over that (500 and 180 a point, as a known
one), weighted by the profile's inference scale. The chance is the tracker's belief of the slot where it has one, and
otherwise the agenda share of the unseen root cards (the Corp's list for Hard, the pool for Standard) with the odds
tripled behind ICE and cut to a tenth in a naked remote (the table's 71% against a pool share of about 44%). An advanced
card, trap risk and every self-flatline check are as before. v27 stays constructible beside it (generation 22) so the
regret suite can compare them; the legacy policies and frozen digests are unchanged.

**Gates** (labels: the line teacher's, `labels.jsonl.gz`):

- **Regret:** `--discordant runner-heuristic-v27,runner-heuristic-v28`: 398 discordant Runner roots, all turn roots (at
  314 v28 runs where v27 took a credit, an ability, an install, a draw or a card, and at 82 both run, on different
  servers), v27 − v28 **+0.0179 [0.0113, 0.0245]**; over all 3,960 Runner roots +0.0018 [0.0012, 0.0024]; both
  separated. Regret all 0.0447 → 0.0429, held out 0.0448 → 0.0434. No choice needed an on-demand label.
- **Puzzles:** level (box 34 of 47, NSG Core 5 of 5, both versions). The three run-targeting failures are R&D runs,
  which this change does not touch.
- **Ladders** against `standard-prev` (Corp v30, Runner v26 ≡ v27): Core Battle Box, 400 deals, Runner **+44 Elo [11,
  81], separated** (score rate 0.28 → 0.34); Gateway, 240 deals, +11 [−26, 49], level. The Corp sides are identical.
- **Self-flatlines** (`ai-runner-diagnosis`, the box ladder's 1,600 games): 8% against 7%, 0.6 standard errors apart.
  Nearly all are Measured Response (54 against 48 in 800 games each), which needs a successful run the turn before, so
  more runs bring more of them; v26's Measured Response guards hold. Wins 28% → 34%, agenda losses 43% → 36%.
- **Lints** (`ai-lint`, the same games): the Runner lints are zero for both versions and the Corp's rates equal.
- **Exposure in the new games:** v28 runs a protected remote at its next turn 53% of the time against v27's 10%, steals
  28% of the agendas there against 10%, and the Corp scores 62% of them against 80%. Naked remotes: 45% against 38%.
- **Per-slice fuzz** (`scripts/fuzz.sh <pool> --games-per-pairing 8 --verify-replays-every 8`): every pool ok, every
  game finished: official 24 games, Gateway 288, Elevation 1,152, Gateway + Elevation 288, NSG Core 2,592; 543 replays
  verified.

### Teacher v2: the suite relabelled with the live opponent (6 October 2026)

**The teacher.** The S1.5 line search (beam 5, a one-turn screen on a quarter of the samples, two finalists, a 4-turn
horizon, set 4 at the leaf, 32 samples), with the rollouts modelling the opponent by the live Standard heuristic
(`hard-model`, `plan-model 1`): Runner v28 at Corp roots and Corp v31 at Runner roots, where the S1.5 labels
(`labels-v1-lines`, teacher v1-lines) modelled the pinned Runner v7 and Corp v8. Relabelling the box suite (6,512 roots,
57,197 groups, none unlabellable) took **7 h 48 min** on 12 workers. The labels are the suite's `labels.jsonl.gz`;
regret below is on them unless named.

**Noise floor** (the 145 roots of the S1.2 repeat, repeat seed 2): **61%** best-group disagreement (v1-lines 57%),
median standard error 0.032.

**Drift from v1-lines** (every group both label; both teachers take the better of two lines, so the lift that carries is
the same in both and the drift is the opponent's):

| Roots | Mean value change | Past 2 SE up / down | Best group changed |
|---|---:|---|---:|
| Corp turn (1,604) | −0.126 | 1.3% / 60.4% | 71% |
| Corp run (944) | −0.122 | 1.0% / 56.4% | 37% |
| Runner turn (3,229) | −0.023 | 3.5% / 10.8% | 69% |
| Runner run (726) | −0.029 | 4.4% / 13.7% | 30% |

The best group changed at 60.6% of roots, at the noise floor. The Corp's values fell by an eighth of a game: the live
Runner punishes what v7 did not. The Runner's fell a little: Corp v31 is a stronger opponent than v8.

**Recertification** of the 26 teacher-certified puzzles at 128 samples with teacher v2 (the same rule): **14 hold**. Ten
more fail and move to a `teacher-v2` split, out of the gate (`ai-puzzles run --split teacher-v2`): three because the
accepted action is no longer the best (`teacher-44d22315` and `teacher-4da9c5de`, rez choices; `teacher-93609fc4`, an
install), one because its best group's standard error is over 0.04 (`teacher-d907ce15`), and six because the lead falls
under 0.15 as values shrink against the live opponents (`teacher-2920124f`, `-b39f5b92`, `-20e668e7`, `-8b1c9af3`,
`-9570ba07`, `-dc91ae5e`). The two `teacher-v1` puzzles fail again. The gated box set is now 37 puzzles.

**S1.3's table on teacher v2** (labels: `labels.jsonl.gz`, teacher v2; every choice labelled, none on demand):

| Comparison | Every root | The discordant roots |
|---|---|---|
| Runner v24 − v25 | +0.0027 [0.0020, 0.0035] | |
| Runner v24 − v26 | +0.0028 [0.0020, 0.0037] | |
| Runner v25 − v26 | +0.0001 [−0.0003, 0.0004] | |
| Runner v27 − v28 | +0.0014 [0.0008, 0.0020] | |
| Corp v28 − v29 | +0.0007 [−0.0001, 0.0014] | |
| Corp v28 − v30 | **+0.0022 [0.0005, 0.0040]** | **+0.0173 [0.0049, 0.0298]** (324 roots) |
| Corp v29 − v30 | +0.0015 [−0.0001, 0.0033] | **+0.0142 [0.0010, 0.0284]** (272 roots) |
| Runner v26 − v27, Corp v30 − v31 | 0.0000 | |

| Version (Standard) | Regret, all | Turn | Run | Held out |
|---|---|---|---|---|
| Runner v24 | 0.0429 [0.0413, 0.0447] | 0.0505 | 0.0094 | 0.0425 |
| Runner v25 | 0.0402 [0.0386, 0.0419] | 0.0472 | 0.0092 | 0.0387 |
| Runner v26, v27 | 0.0401 [0.0385, 0.0420] | 0.0471 | 0.0092 | 0.0384 |
| Runner v28 | 0.0387 [0.0374, 0.0406] | 0.0454 | 0.0092 | 0.0369 |
| Corp v28 | 0.0449 [0.0419, 0.0476] | 0.0579 | 0.0228 | 0.0476 |
| Corp v29 | 0.0442 [0.0412, 0.0470] | 0.0579 | 0.0210 | 0.0469 |
| Corp v30, v31 | 0.0427 [0.0395, 0.0460] | 0.0555 | 0.0210 | 0.0452 |

**The Runner:** the ordering is unchanged (v24 separates from v25 and v26 by the same margins as on v1-lines), and v28
is ahead of v27 on the new teacher too. The opponent model did not reorder the Runner.

**The Corp:** for the first time the suite agrees with the ladder. v30 separates from v28 over every root and over the
discordant roots, and from v29 over the 272 roots where they choose differently (over every root it falls just short).
Split by agenda installs (value of v30's choice − v29's, the 32-sample labels):

| Discordant roots | Roots | v1-lines | Teacher v2 |
|---|---:|---|---|
| v29 installs an agenda in a remote, v30 holds it | 119 | +0.0007 [−0.0239, 0.0289] | +0.0185 [−0.0099, 0.0463] (v30 better at 58, v29 at 49) |
| No agenda install | 144 | +0.0064 [+0.0001, 0.0129] | +0.0150 [+0.0061, 0.0245] |

The agenda roots move the right way and stay inside zero at 119 roots, as the probe said: the sample is too small, not
the teacher wrong. That is S1.6's reason: targeted roots where Corp v29 and v30 choose differently. S1.3's gate for the
Corp is closed by teacher v2 over the discordant roots; the agenda-hold rule's own reading waits for S1.6.
