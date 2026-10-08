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

### S1.7: labelling cost (7 October 2026)

**The teacher's version.** Every label names its teacher (`teacherVersion`): **v1** (S1.2, one-step, `labels-v1`),
**v1-lines** (S1.5, the line search with the pinned Runner v7 and Corp v8, `labels-v1-lines`), **v2** (the line search
with the live opponent, `labels.jsonl.gz`); `ai-regret tag` wrote them into the committed files. The teacher plays the
live Hard bot of each side and models the other by the live Standard, so a label is named v2 only while those are Corp
v31 and Runner v28: after a policy bump a new label's name carries the versions instead, and the tagged files keep their
names. `score` prints the labels and teacher beside every table and comparison (`teacher v2 (6512 roots)`), and refuses
a root whose values come from two teachers (a group labelled on demand into a label names the teacher it came from).

**The core** (`ai-regret core --roots 2000 --seed 1`, a marking, not a run): **2,050 roots** in 302 games, 466 held out
(22.7%, the suite's share 22.2%), drawn by deal. `score --core` reads it. **Gate:** the core reads as the whole suite
does within the intervals (teacher v2):

| Comparison | Core | Whole suite |
|---|---|---|
| Runner v24 − v25 | +0.0033 [0.0021, 0.0045] | +0.0027 [0.0020, 0.0035] |
| Runner v24 − v26 | +0.0038 [0.0025, 0.0049] | +0.0028 [0.0020, 0.0037] |
| Runner v27 − v28 | +0.0011 [0.0003, 0.0021] | +0.0014 [0.0008, 0.0020] |
| Corp v28 − v30, every root | +0.0022 [−0.0007, 0.0049] | +0.0022 [0.0005, 0.0040] |
| Corp v28 − v30, discordant | +0.0193 [−0.0041, 0.0434] (95) | +0.0173 [0.0049, 0.0298] (324) |
| Corp v29 − v30, discordant | +0.0188 [−0.0073, 0.0428] (78) | +0.0142 [0.0010, 0.0284] (272) |

Every estimate falls within the other's interval. The Corp's separations need the whole suite's roots (the core has a
third of them), as the plan expects: comparisons use every root both versions are labelled at.

**Adaptive samples** (`ai-regret label --decide 8 --record 8 --extend-to 32`; the deciding samples are discarded, and a
lines root reruns the v2 search on all its recording samples at each step, its memos making the samples already played
free). Against their flat 32-sample v2 labels, on 200 core roots:

- values: mean signed difference **−0.0000** (standard error 0.0008) over 1,695 groups, no lift either way; mean
  absolute difference 0.0216;
- the best group differs at 40.5% of roots, under the noise floor (61%);
- Runner v24 − v28 on the 115 Runner roots: +0.0027 [−0.0001, 0.0063] adaptive, +0.0032 [0.0001, 0.0067] flat;
- **cost: 1,480 s against about 860 s flat.** 181 of the 200 roots extended to 32 recording samples: the suite's roots
  were drawn where the heuristic's top two are close, and with 8.8 groups a root's best two are almost never two
  standard errors apart after 8 samples, so nearly every root pays the 8 deciding samples and the reruns on top of 32.

The adaptive labels are sound but save nothing on the suite's roots, so the core is relabelled flat at 32. Adaptive
samples are kept for S1.6's targeted roots, where the two versions' choices usually differ clearly, and their saving
there is measured on S1.6's first run rather than assumed. One point of the design to watch there: an undecided root
stops extending once its recording samples separate, which is optional stopping on the recorded samples; the signed
difference above shows no lift from it on these roots.

**Pruning's miss rate, per labelling** (the share of roots where a flat 32-sample labelling's best group was pruned):

| Labelling | Roots compared | Prune | Missed |
|---|---:|---:|---:|
| S1.6, Corp v29/v30 and v28/v29 targeted roots (turn sample) | 200 | best 3 | 17% (34) |
| S4.2, the planner pairs' targeted roots (sample, seed 2) | 100 | best 4 | 10% (10) |

S4.2 kept four finalists: the miss rate fell to 10% and the samples rose from 48% of flat's to 62% (1,159 roots in
65 min, 3.4 s a root, against flat's 4.6 s on its sample).

### S1.6: targeted roots (7 October 2026)

**The roots.** `ai-regret diverge` replayed the suite's 974 games and asked two Corp versions (Standard) at every
turn-action and run-choice decision; the positions where they chose different groups became roots, both choices
recorded (`diverge:VERSION=GROUP` in the root's categories), deduplicated by position and held out by deal:

| Pair | Roots (before deduplication) | Kind | Held out | Groups a root | Time |
|---|---:|---|---:|---:|---:|
| Corp v29, v30 | 2,073 (4,514) | turn | 528 | 13.8 | 599 s |
| Corp v28, v29 | 499 (1,096) | run | 66 | 3.3 | 573 s |

179 of the 2,572 were already suite roots (they keep their labels and gain the choices); the other **2,393** were
labelled by teacher v2 and merged (`ai-regret merge`): the suite is now 8,905 roots, 4,945 of them the Corp's.

**Pruning, measured** (`--decide 8 --record 24 --prune 3`: every group 8 deciding samples, then 24 recording samples for
the best three and both chosen groups; a pruned group keeps its deciding value, marked, never a root's best). The 2,393
roots took **2 h 59 min** (10,744 s). Against flat 32-sample labels of 200 targeted turn roots (2,438 s, 12.2 s a root):

- **samples: 48% of flat's** (13.6 groups a root, 4.2 recorded);
- recorded groups: mean signed difference −0.0046 (standard error 0.0015), mean absolute 0.0237. The sign is the
  selection, not a lift: the recorded groups were chosen for their deciding samples, which flat's value includes and the
  pruned value does not;
- the best group differs at 36%, under the noise floor; **flat's best was pruned at 17% of roots**, so a pruned root's
  best is often too low and its absolute regret reads lower (Corp v29 0.0791 against 0.0892);
- the pair's regret difference agrees within the intervals: Corp v29 − v30 −0.0016 [−0.0224, 0.0217] pruned, +0.0038
  [−0.0174, 0.0265] flat;
- noise floor on 200 targeted roots (repeat seed 2): 56% best-group disagreement, median standard error 0.045
  (`labels-targeted-repeat*`, `labels-v2-targeted-repeat*` since S1.8).

**Verdict: pruning pays on targeted roots** (about half the samples, the comparison unchanged) and is kept for them. It
biases a root's absolute regret low, so absolute regret is never quoted from targeted roots: it is quoted on the core,
which relabels flat (S1.7). `bot-check`'s regret line now reads the core's held-out roots.

**Gate: the Corp separates** (teacher v2, every choice labelled, none of a pruned group):

| Comparison | Discordant roots | Over the discordant roots | Every Corp root (4,945) | Before (S1.5's table) |
|---|---:|---|---|---|
| Corp v29 − v30 | 2,198 | **+0.0180 [0.0120, 0.0243]** | +0.0080 [0.0052, 0.0103] | +0.0142 [0.0010, 0.0284] (272) |
| Corp v28 − v30 | 2,717 | **+0.0231 [0.0176, 0.0287]** | +0.0127 [0.0092, 0.0156] | +0.0173 [0.0049, 0.0298] (324) |
| Corp v28 − v29 | 519 | **+0.0451 [0.0332, 0.0593]** | +0.0047 [0.0033, 0.0061] | +0.0007 [−0.0001, 0.0014] (all) |

The every-root column mixes the targeted roots in, so it is a comparison, not an absolute regret. **The agenda-hold
rule**, read where v29 and v30 choose differently:

| Discordant roots (v29 − v30) | Roots (targeted) | Regret difference | v30 better / v29 better |
|---|---:|---|---|
| v29 installs an agenda in a remote, v30 holds it | 1,201 (1,146) | **+0.0241 [0.0141, 0.0340]** | 609 / 505 |
| No agenda install | 945 (881) | +0.0112 [0.0063, 0.0167] | 537 / 328 |
| Both install an agenda | 39 | −0.0126 [−0.0683, 0.0398] | 16 / 21 |
| v30 installs one, v29 does not | 13 | +0.0356 [−0.0286, 0.0990] | 9 / 3 |

At ten times the roots the agenda roots separate: holding the agenda is worth 0.024 a decision against the live Runner
v28, the gain the outside player found and self-play could not see. **S1.3's gate for the Corp is closed** (teacher v2,
Runner v28, and S1.6's roots). The Runner's comparisons are unchanged (v24 − v25 +0.0027 [0.0020, 0.0035], v24 − v26
+0.0028 [0.0020, 0.0037], v26 − v28 +0.0014 [0.0008, 0.0020]).

### S4.1: evaluation feature set 5 (7 October 2026)

`EvaluationFeatures.NamesFor(5)` is set 4's 28 features and 12 more, each from the observing side's view: what its
observation shows, and what its knowledge infers (`Extract(features, catalog, 5, knowledge)`; without knowledge, the
pool prior and no tracker). The Corp reads its own cards; the Runner reads the run model and `RunnerInference`, the
tracker and prior Runner v28 values remotes with.

| Feature | Corp's view | Runner's view |
|---|---|---|
| `exposed-agenda-points` | its agenda points in remotes the Runner can get into now | the same, inferred (`RootAgenda`: the tracker, else the root share moved by protection) |
| `hq-agenda-density` | agendas in its hand over its size | `HqAgendaShare` (tracker) |
| `rd-agenda-density` | agenda points per unseen Corp card (the pool less what it sees) | the same from its prior (the list for Hard) |
| `advancements-left`, `scoring-server-open` | its most advanced agenda's counters still needed, and whether the Runner can get into that server | the most advanced face-down card, its requirement the prior's average |
| `punisher-exposure`, `grip-margin` | a punisher it holds and can pay for against the grip; the grip less the largest damage (punisher or known trap), ±5 | `Punishers`' chance when the grip is short |
| `central-min-cost`, `central-affordable` | the cheaper of HQ and R&D as set 1 prices them, and whether either is open at the Runner's credits | same |
| `money-after-reserve`, `income-cards` | credits less its cheapest unrezzed ICE's rez cost; its rezzed money-making cards | credits less the cheapest central; its money-making rig cards |
| `race-to-move` | the agenda lead in sevenths when it moves next | same |

The score race's own terms (points, near-win, turn, who moves) are set 1's, so only the interaction is new. Archives was
dropped from the central tax: almost never protected, it pinned the term at zero. Each server's run is planned once per
extraction, since set 5 is read at the end of every rollout once S4.2 fits it. `RunnerInference.PunisherDamage` is
`Punishers`' predicate extracted for the Corp's view, the Runner's use unchanged.

**Gate.** A unit test per term on constructed observations, and a twin test (two states differing only in which hidden
card is where give the same features, from either side) in `EvaluationFeaturesTests`; on real box positions
(`RegretSuiteTests.SetFiveReadsRealPositions`, both sides with the teacher's knowledge) every value is finite and every
new term varies except `punisher-exposure`, which is rare in self-play (a castable punisher, the threat met and a grip
under its damage at once, which v26 guards against). No fit yet: S4.2 fits set 5 on the core.

**The leaf's cost** (`ai-regret leaf-cost`, one thread, 8 box games, both sides at every decision, 10,026 positions):
set 5's extraction is **93.1 µs** against set 4's **46.6 µs** (set 5 computes set 4 and then its own terms); with the
observation's build and read (228.0 µs) a leaf is **321.1 µs** against 274.6 µs, 17% more. S0.1 measured the whole leaf
at 217 µs a rollout in the planner, 6.1% of a plan's time; at that share, set 5 adds about 1% to a planner decision.
S5.0's budget reads these numbers.

### S4.2: fitting the leaf to the teacher (7 October 2026)

**The fits** (`ai-fit-evaluation --target teacher --suite data/ai/regret/gateway-elevation --outcome-weight 0.2`; the
core's training roots, teacher v2's root value as a soft target at weight 1 and the game's outcome at weight 0.2,
`LogisticSideModel` on standardised features; report `data/ai/evaluation-v5.report.json`). Held out: the core's
held-out roots, Corp 195 and Runner 268 (622 and 962 trained on):

| Seat | Arm | Teacher log loss | Teacher mean absolute error | Outcome log loss |
|---|---|---:|---:|---:|
| Corp | the training rows' mean teacher value (a constant) | 0.6067 | 0.2254 | 0.7124 |
| Corp | set 4, shipped weights | 0.4930 | 0.1392 | 0.5844 |
| Corp | set 4, fitted to the teacher (`evaluation-v4-teacher.json`) | 0.4847 | 0.1338 | 0.5259 |
| Corp | set 5, fitted to the teacher (`evaluation-v5.json`) | **0.4658** | **0.1149** | 0.5142 |
| Runner | the training rows' mean teacher value | 0.6777 | 0.2068 | 0.6747 |
| Runner | set 4, shipped weights | 0.6386 | 0.1622 | 0.6296 |
| Runner | set 4, fitted to the teacher | 0.5922 | 0.1153 | 0.5538 |
| Runner | set 5, fitted to the teacher | **0.5814** | **0.1068** | 0.5426 |

Set 5 against each set 4 arm, paired root by root (95% bootstrap, 2,000 draws):

| Seat | Against | Teacher log loss | Mean absolute error |
|---|---|---|---|
| Corp | set 4 shipped | −0.0272 [−0.0391, −0.0154] | −0.0242 [−0.0352, −0.0126] |
| Corp | set 4 refitted | −0.0188 [−0.0270, −0.0106] | −0.0189 [−0.0271, −0.0100] |
| Runner | set 4 shipped | −0.0573 [−0.0750, −0.0391] | −0.0554 [−0.0724, −0.0384] |
| Runner | set 4 refitted | −0.0108 [−0.0163, −0.0064] | −0.0085 [−0.0129, −0.0043] |

**The fit gate passes**: set 5 beats both set 4 arms in both seats. The gain is attributed differently by seat: the
Corp's is mostly the features (refitting set 4 recovers about a third of it), the Runner's mostly the refit (about
four fifths). `punisher-exposure` is rare in the core, not absent: non-zero at about 2% of the Corp's rows (mean 0.019)
and as small a share of the Runner's (mean 0.015), and its standardised weights are near zero (Corp 0.007, Runner
0.023). That is expected, since v26 guards against the state it measures (a castable punisher, the threat met and a
grip under its damage), and is not a verdict on the term: self-play rarely shows it, and its unit test covers it.

**The planner arms** (ladder entrants, Hard's settings): `hard` (Corp's set 4, Runner's set 3 with the shipped
weights; set 3 and set 4 read the same from the Runner's side), `hard-set4-teacher` (`plan-evaluation 6`: set 4's
features with the refitted weights) and `hard-set5` (`plan-evaluation 5`). The planner's leaf reads set 5 with its own
knowledge (the tracker, the opponent's list for Hard).

**The roots.** `ai-regret diverge --side S --games 48` (every 20th of the suite's games) for set 5 against each set 4
arm, in each seat; Hard's choices repeat exactly, so a scored root's two choices are the ones `diverge` found:

| Seat | Pair | Discordant roots | Held out | Time |
|---|---|---:|---:|---:|
| Corp | hard-set5, hard | 502 | 121 | 38 s |
| Corp | hard-set5, hard-set4-teacher | 279 | 66 | 39 s |
| Runner | hard-set5, hard | 485 | 119 | 61 s |
| Runner | hard-set5, hard-set4-teacher | 269 | 74 | 60 s |

All turn-action roots (Hard plans run choices with the heuristic). Merged, they are 1,159 roots (the two pairs share
173 Corp and 203 Runner positions), labelled by teacher v2 with four finalists (`--decide 8 --record 24 --prune 4`,
S1.7's miss-rate table): 65 min.

**Gate: the planner on set 5 does not separate from either set 4 arm** (regret difference set 5 minus the other, teacher
v2, labels of the S4.2 roots; negative is set 5 better; no choice unlabelled or of a pruned group):

| Seat | Against | Over the discordant roots | Core held-out, discordant | Core held-out regret (set 5 / other) |
|---|---|---|---|---|
| Corp | set 4 shipped | +0.0059 [−0.0021, 0.0134] (502) | −0.0087 [−0.0242, 0.0055] (53) | 0.0347 / 0.0371 |
| Corp | set 4 refitted | −0.0040 [−0.0117, 0.0039] (279) | −0.0059 [−0.0318, 0.0218] (41) | 0.0347 / 0.0360 |
| Runner | set 4 shipped | −0.0025 [−0.0071, 0.0021] (485) | +0.0078 [−0.0050, 0.0208] (50) | 0.0368 / 0.0354 |
| Runner | set 4 refitted | −0.0029 [−0.0091, 0.0032] (269) | +0.0079 [−0.0080, 0.0263] (32) | 0.0368 / 0.0359 |

(The core columns are the main suite's labels, teacher v2, 466 held-out core roots; Hard Corp's 0.0371 equals the v31
heuristic's by chance: they choose differently at 20 of the 196 roots, and their difference there is −0.0002.)

Every interval spans zero and the signs disagree between the targeted roots and the core. The leaf fits the teacher's
values better by a clear margin, but the planner at Hard's settings does not choose better with it. On the Corp's
targeted roots set 5 is worse where it draws and the shipped leaf advances (42 roots, +0.046) and where it installs and
the shipped leaf takes a credit (20 roots, +0.043).

**The ladder agrees** (box, `hard,hard-set5`, 400 deals, 1,600 games, `--stop-when-clear`, 20 min): level in both
seats, Corp set 5 − set 4 −10 Elo [−42, 18], Runner +14 [−19, 42], not separated.

**Reading.** Not met on the root-value fit: the measurement has two confounds, so it is not a verdict on set 5's
features, and S4.1 is not revisited. The planner's value adds the rollout's log-odds to the heuristic's score, so the
leaf moves only part of a choice; and the leaf was fitted at roots, while the planner reads it where the turn ends.
S4.2b removes both (`docs/BOT_PLAN_STEPS.md`).

### S4.2b: the planner's value in win probability (7 October 2026)

**`plan-value 1`** (S5.1's scoring, brought forward): a planned candidate's value is the mean over the samples of its
leaf as a win probability less the sample's root (the leaf at the position before it); the heuristic's score is not
part of it, and among the top band's candidates within `plan-tie` (0.005) of the best value the heuristic's first is
chosen. Entrants `hard-value` (Hard, set 4 shipped) and `hard-set5-value` (Hard, set 5 fitted to the teacher).

**The roots.** `diverge --games 48` (the same games as S4.2's) found 696 Corp and 692 Runner discordant roots, all turn
actions: more than either set 4 pair did under the old value. 741 of them were S4.2 roots whose labels (same teacher,
settings and seed) already recorded both choices and were kept; the other 647 were labelled with four finalists
(39 min).

**Gate, re-read under `plan-value 1`: set 5 still does not separate from set 4** (set 5 minus set 4, both
`plan-value 1`; teacher v2; no choice unlabelled or of a pruned group):

| Seat | Over the discordant roots | Core held-out, discordant | Core held-out regret (set 5 / set 4 / Hard) |
|---|---|---|---|
| Corp | +0.0028 [−0.0034, 0.0087] (696) | −0.0007 [−0.0180, 0.0173] (53) | 0.0337 / 0.0339 / 0.0371 |
| Runner | −0.0006 [−0.0047, 0.0034] (692) | +0.0022 [−0.0077, 0.0141] (69) | 0.0359 / 0.0354 / 0.0354 |

The Corp's one consistent loss persists: where set 5 draws and set 4 advances (46 roots, +0.044, against 42 and +0.046
under the old value). The leaf fitted at roots still undervalues an advance by the end of the turn, which is what the
end-of-line fit tests.

**End-of-line rows** (`ai-regret label --end-lines`): the core relabelled flat (teacher v2, 32 samples) with recording
on took 2 h 26 min (8,739 s), and its 2,050 labels are identical to the core's, so recording changes nothing it labels.
It recorded **937,110 rows**, 457 a root (8.8 groups × 32 samples × the lines played to the horizon, less the memo's
repeats): Corp 361,630 and Runner 575,480, 7.0 MB compressed (`.tools/`, not committed).

**The fits on those rows** (`ai-fit-evaluation --target end-lines`; the play-out's value from the end-of-turn state the
target, no outcome rows; trained on the core's training deals, held out by deal: Corp 83,389 rows of 196 roots, Runner
127,485 of 270; 323 s, 3 GB; `data/ai/evaluation-v5-lines.json`, `evaluation-v4-lines.json` and the report;
`plan-evaluation` 7 and 8):

| Seat | Arm | Log loss | Mean absolute error |
|---|---|---:|---:|
| Corp | the training rows' mean (a constant) | 0.6474 | 0.2864 |
| Corp | set 4, shipped weights | 0.5420 | 0.2009 |
| Corp | set 4, fitted to the end-of-line values | 0.5246 | 0.2025 |
| Corp | set 5, fitted to the end-of-line values | **0.5003** | **0.1857** |
| Runner | the training rows' mean | 0.6529 | 0.2470 |
| Runner | set 4, shipped weights | 0.5641 | **0.1699** |
| Runner | set 4, fitted to the end-of-line values | 0.5630 | 0.1753 |
| Runner | set 5, fitted to the end-of-line values | **0.5580** | 0.1719 |

Set 5 against each set 4 arm, paired (95% bootstrap over held-out roots, 2,000 draws):

| Seat | Against | Log loss | Mean absolute error |
|---|---|---|---|
| Corp | set 4 shipped | −0.0417 [−0.0598, −0.0241] | −0.0152 [−0.0273, −0.0027] |
| Corp | set 4 refitted | −0.0243 [−0.0341, −0.0156] | −0.0168 [−0.0229, −0.0110] |
| Runner | set 4 shipped | −0.0060 [−0.0157, +0.0049] | +0.0020 [−0.0054, +0.0107] |
| Runner | set 4 refitted | −0.0049 [−0.0075, −0.0026] | −0.0033 [−0.0050, −0.0015] |

The Corp's fit separates from both arms, by more than at roots. The Runner's does not separate from the shipped
weights: set 4's shipped fit (outcomes at Standard's turn starts) already reads end-of-turn states about as well as
anything here, and the refit is no better than it. At the end of the root side's turn the observer never moves next, so
`race-to-move` is always 0 and gets no weight; `punisher-exposure`, near zero at roots, gets weight here (Corp −0.094,
Runner −0.050, standardised).

**Gate, read again under `plan-value 1` on the end-of-line fits** (entrants `hard-set5l-value`, `plan-evaluation 7`,
against `hard-value`, set 4 shipped, and `hard-set4l-value`, `plan-evaluation 8`; `diverge --games 48` on the same games:
505 and 427 Corp, 422 and 211 Runner discordant roots, 1,187 merged, 991 of them already labelled with both choices
recorded and 196 labelled with four finalists, 17 min; teacher v2, no choice unlabelled or of a pruned group):

| Seat | Against | Over the discordant roots | Core held-out, discordant | Core held-out regret (set 5 / other) |
|---|---|---|---|---|
| Corp | set 4 shipped | −0.0060 [−0.0125, +0.0004] (505) | +0.0089 [−0.0049, 0.0221] (49) | 0.0362 / 0.0339 |
| Corp | set 4 refitted | +0.0032 [−0.0037, 0.0106] (427) | +0.0037 [−0.0110, 0.0173] (49) | 0.0362 / 0.0352 |
| Runner | set 4 shipped | −0.0015 [−0.0074, 0.0042] (422) | +0.0015 [−0.0083, 0.0114] (47) | 0.0356 / 0.0354 |
| Runner | set 4 refitted | −0.0025 [−0.0113, 0.0065] (211) | −0.0075 [−0.0172, 0.0023] (25) | 0.0356 / 0.0363 |

**Not met.** No interval clears zero. The Corp against the shipped weights is the nearest yet and has the right sign over
the targeted roots, but the core's held-out roots read the other way, and against the refitted set 4 the Corp's point
is the wrong sign. Removing both confounds did not turn the leaf's better fit into better choices at Hard's settings.

### S4.3: budget scaling on set 5 with plan-value (7 October 2026)

Entrants `hard-set5l-value-x1`, `-x4` and `-x8`: Hard's planner with `plan-value 1` on the end-of-line set 5
(`plan-evaluation 7`) at 1, 4 and 8 times its samples, each against each in both seats on the Core Battle Box:

```bash
scripts/ai-ladder.sh --content gateway-elevation --entrants hard-set5l-value-x1,hard-set5l-value-x4,hard-set5l-value-x8 \
  --deals 400 --batch-deals 48 --stop-when-clear --workers 12
```

3,600 games (no stop: no seat separated), no errors, none capped, 2 h 40 min. Elo against the next smaller budget:

| Seat | ×4 over ×1 | ×8 over ×4 | ×8 over ×1 |
|---|---|---|---|
| Corp | +7 [−17, 28] | −10 [−37, 14] | −3 [−24, 23] |
| Runner | +12 [−13, 36] | +2 [−21, 26] | +14 [−11, 41] |

**Reading: flat; S5 does not start.** No step's lower bound is above zero in either seat. Against S0.3's shipped leaf
(every point at or below ×1) the Runner's points now rise with the budget, and the Corp's do not. By S4.3's rule this
sends the plan back to the end-of-line fit.

**The S4.2 suites** are committed under `data/ai/regret/gateway-elevation-s4.2/`: `teacher-fit` (the set 4 and
teacher-fitted set 5 pairs, with `labels-flat.jsonl.gz`, the 100-root flat sample of S1.7's miss-rate table),
`plan-value` and `end-lines` (the `plan-value 1` pairs). They are not merged into the main suite: a root found by two
pairs keeps one label, whose pruning recorded the choices of the pair that labelled it, so each pair is scored on its
own suite.

### S4.2c: the leaf fitted to rank (7 October 2026)

**Recording.** The end-of-line rows now carry their sample and line (the candidate's group and rank path), so pairs of
end states form on common random numbers. Every line has a row on every sample: a line the memo values gets its row,
and so does one whose game ended inside the turn (`ended: game`, its value the result, which the planner scores
exactly). The core relabelled flat with recording on took 2 h 46 min (9,955 s); its 2,050 labels are identical to the
core's. It recorded **987,180 rows** (947,879 at the turn's end, 39,301 at the game's end), 30,849 of 30,851 lines with
every sample. On 200 held-out core roots each row also has two more play-outs (repeatability, not a ceiling, #117):
the hidden state dealt again as one world for all of a sample's end states (`ObservationDeterminizer.SampleJointly`),
and only the engine's random stream reseeded (`ObservationSampler.Reseeded`).

**The reference order** (#117): two lines of a root ordered by their values' mean difference over every sample (on
common random numbers), counted where it exceeds twice its standard error. Over the held-out deals: Corp 28,716 line
pairs of 129 roots, 10,005 counted; Runner 44,003 of 227, 7,176 counted. It is repeatable:

| Seat | Split halves, counted pairs | Split halves, every pair | Re-dealt second play-out | Reseeded second play-out |
|---|---|---|---|---|
| Corp | 0.995 [0.993, 0.998] | 0.906 [0.885, 0.924] | 0.932 [0.909, 0.954] (57 roots) | 0.985 [0.969, 0.998] |
| Runner | 0.995 [0.992, 0.997] | 0.830 [0.813, 0.847] | 0.920 [0.886, 0.946] (96 roots) | 0.996 [0.993, 0.999] |

**The baseline table, read twice** (gap-weighted accuracy; the first reading is #116's method on these rows: distinct
end states pooled over samples, pairs more than 0.1 apart, up to 50 a root; the second is against the reference order,
on the state pairs of every sample; an independent script reproduces the second):

| Seat | Arm | First reading | Against the reference |
|---|---|---:|---:|
| Corp | set 5, fitted to the end-of-line values | 60.4% | 63.8% |
| Corp | set 4, fitted to the end-of-line values | 56.5% | 63.4% |
| Corp | set 4, general fit (`evaluation-v2`, #116's column) | 55.4% | 63.3% |
| Corp | set 4, the pool's fit (`evaluation-gateway-elevation-v2`, what Hard plays) | 55.1% | **66.6%** |
| Runner | set 5, fitted to the end-of-line values | 64.7% | 74.1% |
| Runner | set 4, fitted to the end-of-line values | 64.5% | 73.9% |
| Runner | set 4, general fit | 63.2% | 71.5% |
| Runner | set 4, the pool's fit | 64.0% | 73.2% |

#116's table measured the general fit as "shipped"; for this pool Hard plays the pool's own fit. **A confound in the
reference:** the teacher scores the horizon with set 4 (`--evaluation 4`), which for this pool is the pool's fit, so
the reference partly rewards agreeing with that leaf four turns on. The pool's fit is the one arm the reference lifts
above the others for the Corp (66.6% against the general fit's 63.3% on the same features).

**The fit** (`ai-fit-evaluation --target pairs`: at most 200 pairs of a sample's distinct end states a training root,
Corp 83,224 and Runner 158,689; linear on the feature difference, both orders, weighted by the gap; then calibrated):
calibrating on the play-out values across roots turned the Corp's slope negative (the level across roots, which a
ranker does not model, outweighed its order), so the slope is now taken within roots (Corp 0.36 and 0.30, Runner 0.44
and 0.43 for sets 4 and 5). Against the reference (`data/ai/evaluation-v5-pairs.report.json`):

| Seat | Arm | State pairs | Mean leaf | Log loss |
|---|---|---|---|---:|
| Corp | set 4, the pool's fit | 0.666 [0.604, 0.725] | 0.673 [0.610, 0.735] | 0.5420 |
| Corp | set 4, fitted to the end-of-line values | 0.634 [0.573, 0.695] | 0.639 [0.577, 0.700] | 0.5243 |
| Corp | set 5, fitted to the end-of-line values | 0.638 [0.581, 0.692] | 0.640 [0.578, 0.697] | 0.5000 |
| Corp | set 4, fitted to rank | 0.585 [0.525, 0.653] | 0.578 [0.514, 0.655] | 1.2912 |
| Corp | set 5, fitted to rank | 0.619 [0.567, 0.674] | 0.615 [0.561, 0.677] | 1.1879 |
| Runner | set 4, the pool's fit | 0.732 [0.703, 0.758] | 0.855 [0.817, 0.889] | 0.5655 |
| Runner | set 4, fitted to the end-of-line values | 0.739 [0.709, 0.768] | 0.852 [0.811, 0.887] | 0.5654 |
| Runner | set 5, fitted to the end-of-line values | 0.741 [0.712, 0.768] | 0.855 [0.817, 0.889] | 0.5603 |
| Runner | set 4, fitted to rank | 0.738 [0.710, 0.765] | 0.865 [0.830, 0.896] | 0.5725 |
| Runner | set 5, fitted to rank | 0.736 [0.708, 0.763] | **0.873 [0.840, 0.903]** | 0.5719 |

Set 5 fitted to rank against the others, paired over roots (state pairs; mean leaf):

| Seat | Against | State pairs | Mean leaf |
|---|---|---|---|
| Corp | set 4, the pool's fit | −0.047 [−0.108, +0.019] | −0.059 [−0.130, +0.015] |
| Corp | set 5, fitted to the end-of-line values | −0.019 [−0.080, +0.048] | −0.025 [−0.097, +0.049] |
| Runner | set 4, the pool's fit | +0.004 [−0.017, +0.024] | +0.018 [−0.005, +0.039] |
| Runner | set 5, fitted to the end-of-line values | −0.005 [−0.018, +0.008] | **+0.018 [+0.004, +0.035]** |

**Fit gate: not met.** On the state pairs, set 5 fitted to rank is not above the baseline in either seat; for the Corp
it is below every value fit, though not separated. The Runner's mean-leaf reading, the order the planner uses, does
improve on the value fits. The calibrated rankers' log loss is poor for the Corp (1.19 and 1.29): a ranker carries no
level across roots, so its calibrated probabilities are often extreme.

**Where this leaves the step.** The Corp's linear ranker is at 62%, under #117's 70%, with the reference repeatable at
99.5%: by the step, the next fit is boosted scalar trees with the pairwise gradient. Before building it, the reference's
dependence on the horizon leaf (above) is the open question: a ranker trained on these values learns, in part, to
agree with the pool's set 4 four turns on.

**The game's-end reference** (the decision after the first reading: no trees on the 4-turn rows). The core relabelled
to the game's end (`--horizon game`, 32 samples, end-of-line recording) took **5 h 0 min** (17,987 s), 1.8 times the
4-turn run, not the 2.5 S1's measurement suggested. No play-out reached the 4,000-decision cap, so no leaf scored any
value: the reference has no leaf in it. The rows are the 4-turn run's end states exactly (987,180, the same lines on
the same samples); only the values differ. Its labels are `hard-model-lines5-screen1-horizon0-set4-…`, not teacher v2.

| Reference | Seat | Held-out roots | Line pairs | Counted (> 2 SE) | Split halves, counted | Split halves, every pair |
|---|---|---:|---:|---:|---|---|
| 4 turns | Corp | 129 | 28,716 | 10,005 | 0.995 [0.993, 0.998] | 0.906 [0.885, 0.924] |
| game's end | Corp | 81 | 19,936 | **3,566** | 0.987 [0.978, 0.993] | 0.779 [0.731, 0.814] |
| 4 turns | Runner | 227 | 44,003 | 7,176 | 0.995 [0.992, 0.997] | 0.830 [0.813, 0.847] |
| game's end | Runner | 151 | 32,800 | **3,097** | 0.980 [0.966, 0.990] | 0.694 [0.671, 0.718] |

Fewer pairs is the price of no leaf: a game's result is 0 or 1, so fewer line pairs separate at twice the paired
standard error (36% of the Corp's 4-turn count, 43% of the Runner's), and the counted ones are as repeatable.

**Every arm against both references** (gap-weighted, the planner's view, mean leaf over the samples, the gate; state
pairs beside it, without samples whose game ended in the turn; the arms of the first reading were fitted on the 4-turn
rows, and the same fits refitted on the game's-end rows are added; `.tools/s42c/arms.py` reproduces the C# report):

| Seat | Arm | 4 turns, mean leaf | Game's end, mean leaf | Game's end, state pairs |
|---|---|---|---|---|
| Corp | set 4, general fit | 0.639 [0.551, 0.725] | 0.604 [0.505, 0.706] | 0.600 |
| Corp | set 4, the pool's fit (shipped) | **0.673** [0.610, 0.735] | 0.624 [0.535, 0.715] | 0.620 |
| Corp | set 4, value-fitted (4-turn rows) | 0.639 [0.577, 0.700] | 0.633 [0.544, 0.720] | 0.628 |
| Corp | set 5, value-fitted (4-turn rows) | 0.640 [0.578, 0.697] | 0.622 [0.536, 0.711] | 0.624 |
| Corp | set 5, fitted to rank (4-turn rows) | 0.615 [0.561, 0.677] | 0.592 [0.523, 0.676] | 0.599 |
| Corp | set 4, value-fitted (game's-end rows) | | **0.645** [0.559, 0.727] | 0.640 |
| Corp | set 5, value-fitted (game's-end rows) | | 0.641 [0.556, 0.725] | 0.633 |
| Corp | set 4, fitted to rank (game's-end rows) | | 0.622 [0.547, 0.706] | 0.636 |
| Corp | set 5, fitted to rank (game's-end rows) | | 0.616 [0.541, 0.704] | 0.628 |
| Runner | set 4, general fit | 0.839 [0.795, 0.876] | 0.726 [0.651, 0.791] | 0.613 |
| Runner | set 4, the pool's fit (shipped) | 0.855 [0.817, 0.889] | 0.661 [0.602, 0.723] | 0.591 |
| Runner | set 4, value-fitted (4-turn rows) | 0.852 [0.811, 0.887] | 0.654 [0.580, 0.726] | 0.594 |
| Runner | set 5, value-fitted (4-turn rows) | 0.855 [0.817, 0.889] | 0.656 [0.586, 0.725] | 0.589 |
| Runner | set 5, fitted to rank (4-turn rows) | **0.873** [0.840, 0.903] | 0.704 [0.634, 0.769] | 0.610 |
| Runner | set 4, value-fitted (game's-end rows) | | 0.667 [0.593, 0.737] | 0.612 |
| Runner | set 5, value-fitted (game's-end rows) | | 0.666 [0.594, 0.736] | 0.608 |
| Runner | set 4, fitted to rank (game's-end rows) | | 0.730 [0.659, 0.792] | 0.626 |
| Runner | set 5, fitted to rank (game's-end rows) | | **0.731** [0.659, 0.794] | 0.617 |

**The confound, measured.** The two references count different pairs, so an arm's own drop mixes the confound with a
harder reference; the arms' differences on one reference do not. The pool's fit leads the Corp's other arms by 0.033 at
4 turns and by 0.002 on the game's end, and against the general fit, the same features with weights fitted to
Standard's outcomes, it goes from +0.034 [−0.019, 0.090] to +0.020 [−0.056, 0.093]. For the Runner the reversal is
clear: the pool's fit reads +0.016 [−0.011, 0.044] over the general fit at 4 turns and **−0.065 [−0.116, −0.016]** on the
game's end, and every arm fitted on the 4-turn rows falls with it (the value fits to 0.654 to 0.656, below the general
fit's 0.726). The 4-turn reference rewarded agreeing with its horizon leaf; a fit to its values learned that agreement.

**Gate (the planner's view against the game's-end reference, above the best value arm with the interval clear): not
met in either seat.**

| Seat | Comparison (game's-end reference, mean leaf) | Difference |
|---|---|---|
| Corp | set 5 fitted to rank − set 4 value-fitted (game's-end rows), the best value arm | −0.030 [−0.130, +0.057] |
| Corp | set 4 value-fitted (game's-end rows) − general fit | +0.041 [−0.028, +0.120] |
| Runner | set 5 fitted to rank − general fit, the best value arm | +0.006 [−0.036, +0.042] |
| Runner | set 5 fitted to rank − set 5 value-fitted (game's-end rows) | **+0.065 [+0.012, +0.129]** |
| Runner | set 4 value-fitted (game's-end rows) − general fit | **−0.059 [−0.109, −0.018]** |

The Runner's rank fit is clear of every arm fitted to play-out values but only level with the general fit; the Corp's
arms all sit between 0.59 and 0.65 on the clean reference, with nothing separated.

**Calibration** (log loss of the held-out values; the ranker's score calibrated by an isotonic fit over all training
rows, which keeps its order): Corp 0.663 against the value fit's 0.500 (4-turn) and 0.693 against 0.512 (game's end,
the game's-end rank fit); Runner 0.570 against 0.560 and 0.617 against 0.541. **No monotone calibration of a ranker is
as good as the value fit's**, since a within-root ranker carries no level across roots; by the decision, a ranker that
reaches the planner takes its tie threshold and static mix on the value fit's scale.

**The boosted scalar trees** (step 6: the Corp's linear arms all sit between 0.59 and 0.65 on the clean reference).
`ai-fit-evaluation --target trees`: `g(set 5)` per side, 200 trees of depth 3, learning rate 0.1, histogram splits over
64 quantile thresholds a feature, Newton steps on the pairwise logistic loss over the game's-end training pairs (as
`--target pairs` forms them: Corp 67,503 pairs over 76,434 states, Runner 135,897 over 142,302), calibrated by an
isotonic fit interpolated between its blocks. Fitted in 6 and 9 s. **Inference: 2.2 µs a state** (score and calibration,
one thread), against set 5's 93 µs extraction, so the leaf's cost is unchanged in practice. The fits and the report are
in `data/ai/s4.2c/` (with the linear arms refitted on the game's-end rows).

| Seat | Arm (game's-end reference, mean leaf) | Accuracy | Trees − arm |
|---|---|---|---|
| Corp | boosted trees | 0.598 [0.489, 0.711] | |
| Corp | set 4, value-fitted (game's-end rows), the best arm | 0.645 [0.560, 0.729] | −0.047 [−0.127, +0.032] |
| Corp | set 5, fitted to rank (game's-end rows) | 0.616 [0.541, 0.704] | −0.018 [−0.145, +0.118] |
| Runner | boosted trees | 0.712 [0.657, 0.759] | |
| Runner | set 5, fitted to rank (game's-end rows), the best arm | 0.731 [0.662, 0.796] | −0.019 [−0.086, +0.051] |
| Runner | set 4, general fit | 0.726 [0.654, 0.790] | −0.014 [−0.082, +0.065] |

On the 4-turn reference the Corp's trees read 0.674, level with the pool's fit (+0.000 [−0.068, +0.067]): the trees
fitted to game results do not inherit the horizon's lift, and on the clean reference they are no better than the
linear arms. They overfit what signal there is: on per-sample pairs of the Corp's game's-end rows they order 59.3% of
training pairs and 53.1% of held-out ones, where the linear fits hold 55% on both (Runner: 56.8% and 53.4%, against
the rank fit's 57.9% and 56.2%). Lighter boosting (60 trees, learning rate 0.05) reads worse on the reference (Corp
0.542, Runner 0.674).

**Reading.** On the reference with no leaf in it, no learned leaf, linear or boosted, value- or rank-fitted, orders the
Corp's end-of-turn states measurably better than another (all between 0.59 and 0.65, 81 held-out roots), and the
Runner's best arms (the rank fit, the general fit, the trees) are level at about 0.71 to 0.73. The per-sample game
result from an end-of-turn state is a weak signal (55% of per-sample pairs ordered by any fit). The gate of S4.2c is
not met in either seat, and the trees do not change that: the limit is what the end-of-turn state says about the game,
not the model's form. The fallback the step kept in view (one opponent turn played out before the leaf, so the leaf
scores a state where exposure has been punished or not) is the next lever; it is not built.

### S4.4: horizon × samples, what ranks and at what cost (8 October 2026)

**The curve.** The planner's-view accuracy of "k samples of horizon h" against the game's-end reference (#117's
order: two lines of a core root ordered by their mean over k samples, common random numbers, credited when they
agree with the order of their game's-end means, gap-weighted, over the pairs the reference counts), from the core's
recorded end-of-line rows, all teacher v2 (lines 5, screen 1), every line on every sample:

```bash
ai-regret label data/ai/regret/gateway-elevation --samples 32 --horizon opponent --lines 5 --screen-turns 1 \
  --roots core-roots.txt --labels labels-core-opponent.jsonl.gz --end-lines end-lines-opponent.jsonl.gz --workers 12
ai-regret horizon-curve data/ai/regret/gateway-elevation --reference end-lines-game.jsonl.gz \
  --horizon "opponent's turn=end-lines-opponent.jsonl.gz" --horizon "4 turns=end-lines.jsonl.gz" --draws 16
```

The opponent's-turn labelling is the one new run (2,050 roots, 87 min); the 4-turn and game's-end rows are S4.2c's.
The turn's-end rows are the game's-end rows' own end-of-turn states scored by the general fit and the pool's fit.
k samples are drawn 16 times and averaged; intervals resample roots (Corp 818 roots, Runner 1,231). Two designs:

- **(a), as the step reads it:** k drawn from all 32 samples against the 32-sample reference. A row that shares its
  samples with the reference reads high (the game's end at k = 32 is 1 by construction, and the 4-turn play-outs start
  from the same sampled states).
- **(b), unbiased:** k drawn from one half of the samples against the reference built from the other half, both halves
  in turn (k up to 16). Nothing in it can read high; at k = 16 the game's-end cell is the reference's own split-half
  reliability.

| Corp, design (b) | k = 4 | k = 8 | k = 16 |
|---|---:|---:|---:|
| Turn's end, general fit | 0.582 | 0.582 | 0.581 |
| Turn's end, pool's fit | 0.601 | 0.600 | 0.599 |
| Opponent's turn, pool's fit | 0.657 | 0.670 | 0.676 |
| 4 turns, pool's fit | 0.759 | 0.792 | **0.820** [0.787, 0.847] |
| Game's end | 0.753 | **0.812** [0.782, 0.838] | 0.855 |

| Runner, design (b) | k = 4 | k = 8 | k = 16 |
|---|---:|---:|---:|
| Turn's end, general fit | 0.685 | 0.698 | 0.708 [0.683, 0.731] |
| Turn's end, pool's fit | 0.667 | 0.677 | 0.685 |
| Opponent's turn, pool's fit | 0.679 | 0.696 | 0.707 |
| 4 turns, pool's fit | 0.672 | 0.709 | 0.737 [0.710, 0.762] |
| Game's end | 0.632 | 0.674 | 0.711 [0.684, 0.737] |

| Design (a), k = 4 / 8 / 16 / 32 | Corp | Runner |
|---|---|---|
| Turn's end, general fit | 0.588 / 0.589 / 0.588 / 0.588 | 0.738 / 0.759 / 0.774 / 0.785 |
| Turn's end, pool's fit | 0.611 / 0.612 / 0.610 / 0.610 | 0.725 / 0.743 / 0.752 / 0.763 |
| Opponent's turn, pool's fit | 0.672 / 0.687 / 0.696 / 0.701 | 0.742 / 0.766 / 0.785 / 0.793 |
| 4 turns, pool's fit | **0.806** / 0.850 / 0.885 / 0.908 | 0.773 / **0.840** / 0.889 / 0.922 |
| Game's end | 0.864 / 0.949 / 0.995 / 1 | **0.822** / 0.928 / 0.993 / 1 |

Intervals are about ±0.03 to ±0.05 throughout.

**The cost.** Measured directly rather than composed from S0.1 and S0.4: `ai-regret decision-cost` times
`RolloutTeacher.Evaluate` (teacher v2, lines 5, screen 1, so every candidate group at the teacher's count) on 41
turn-action roots of the core (`--roots`, the first root not timed), at 4 samples, one thread, the machine otherwise
idle. Corp roots average 13.2 candidate groups, Runner roots 10.5. A root's cost is taken as linear in its samples (each
sample's play-outs are independent), so a cell's cost is the k = 4 cost times k / 4.

| Horizon | Corp, mean / p95 at k = 4 | Runner, mean / p95 at k = 4 | Corp / Runner, one candidate, one sample (mean) |
|---|---:|---:|---:|
| Turn's end | 1.49 s / 6.2 s | 0.91 s / 3.4 s | 28 ms / 22 ms |
| Opponent's turn | 1.98 s / 6.7 s | 1.20 s / 3.7 s | 38 ms / 29 ms |
| 4 turns | 4.11 s / 12.0 s | 2.78 s / 6.1 s | 78 ms / 66 ms |
| Game's end | 7.98 s / 26.0 s | 6.26 s / 9.8 s | 151 ms / 149 ms |

S5.0's expected cuts, as a second column: the trace's items S5.0 targets (the state hash, the invariant check and the
working state's build inside `ApplyCore`, `BehaviorStaticModifierProjection.Sum`, the `TurnFactsState` copies) are
about 24% of all time, so a cell costs at least 0.76 of the figure above after S5.0, if every one of them goes.

**The reading (a reading, not a pass).** The cheapest cell above 80% in the planner's view, with its wall time per
turn-action decision at the teacher's count (mean / p95; after S5.0's cuts):

| Seat | Design (b) | Design (a) |
|---|---|---|
| Corp | game's end, k = 8: 0.812; 16.0 s / 52 s (12.1 s / 39 s); 4 turns at k = 16 reads 0.820 at the same cost (16.4 s / 48 s) | 4 turns, k = 4: 0.806; 4.1 s / 12 s (3.1 s / 9.1 s) |
| Runner | none: the best is 4 turns at k = 16, 0.737, level with the turn's end (0.708) and the game's end (0.711); 11.1 s / 25 s | 4 turns, k = 8: 0.840; 5.6 s / 12 s (4.2 s / 9.3 s) |

No cell under one second reaches 80% in either seat or design: at the teacher's count no cell fits at all (the
cheapest, the turn's end at k = 4, is 1.5 s and 0.9 s on the mean). What fits is set by the beam S5.1 ships: at
S5.1's width of 4 candidates and the per-candidate costs above, one sample of 4 turns costs about 0.31 s for the
Corp and 0.27 s for the Runner on the mean, so the one-second budget buys about 3 samples of 4 turns (Corp 0.759 and
Runner 0.672 at k = 4 in design (b)), 6 to 8 of the opponent's turn (about 0.67 and 0.70), or 1 to 2 of the game's end
(below 0.75), before p95 is counted. The best that fits is therefore 4 turns at about 3 samples for the Corp; for the
Runner every horizon is level, so the cheapest (the turn's end) loses nothing.

**Hard's cell for S5, as read here:** the Corp plays out 4 turns, with as many samples as S5.1's beam leaves inside
the second (about 3 at width 4 today, more after S5.0); the Runner stops at the turn's end, the cheapest cell, since no
horizon ranks its candidates better. Two things S5.1 checks rather than assumes: the 4-turn row ends on the pool's
fit, where S5.1 plans the general fit, and the k = 3 cell is read off the curve, not measured.

**What it says.**

- **The Corp's question is the horizon, not the samples.** At the turn's end the Corp's accuracy is the same at 4
  samples and at 32 (0.58 to 0.61); one opponent turn adds about 7 points; 4 turns or the game's end add about 20.
- **The Runner's candidates are mostly noise to every horizon.** In design (b) every cell lies between 0.63 and 0.74,
  and the game's end itself, 16 samples against the other 16, reads 0.711: the reference's own split-half reliability
  is below the 80% the step asks for. Design (a)'s Runner cells above 80% are the ones that share their samples with
  the reference.
- **The opponent's turn is not where the Corp's signal is.** The fallback S4.2c kept in view (one opponent turn played
  out before the leaf) reaches 0.676 at k = 16 in design (b), about a third of the way from the turn's end to 4 turns.

**S4.3 re-read.** S4.3's entrants were Hard's planner at the turn's-end leaf with 1, 4 and 8 times its samples: the
table's turn's-end row. That row is flat in k for the Corp (0.588 at 4 samples and at 32) and rises slowly for the
Runner (0.738 to 0.785 in design (a), 0.685 to 0.708 in (b)), which is what S4.3's ladder showed: the Corp's points did
not move with the budget and the Runner's rose without separating. More samples of the turn-end leaf buy almost
nothing; the horizon is the lever.

**Notes on the run.**

- **The opponent's horizon is `--horizon opponent`, not the step's `--horizon 1`.** `TurnNumber` counts rounds and
  increments when the Runner's turn ends, so `--horizon 1` from a Runner root stops at the end of the Runner's own
  turn. `opponent` counts player turns: it stops where the root side's next turn begins, for either seat.
- **The opponent's-turn and 4-turn rows end on the pool's fit**, the teacher's leaf for this pool (S1.5's confound): their
  accuracy is the horizon and that leaf together. The game's-end row has no leaf.
- **`--horizon turn`** (the play-out stops at the root side's turn end) was added for the cost probe; the turn's-end
  accuracy is read from the recorded end-of-turn states, not from a fourth labelling.

### S4.5: the apprentice at the root (8 October 2026)

**Gate not met; Standard stays the heuristic in both seats, and the Corp keeps its 3×3 planner.** The apprentice
orders the teacher's choices better on held-out roots in both seats, and in play the Runner's apprentice banks credits
and loses: the teacher it learned from (4 turns and the pool's fit) does not punish that.

**The model.** A scalar scorer `g(root, candidate)` over a candidate's terms (`ApprenticeFeatures`: the action's kind,
the heuristic's band, score and rank, the card's class, cost and agenda points, the target server and its ICE, credits
moved and after) and set 5 for the root, with kind × root interactions for the linear arm. Rows are every labelled turn
root of the suite as the heuristic sees it (`standard-heuristic`, Standard with no planner): 79,827 groups over 6,759
roots, up to 24 pairs a root weighted by the value gap, game and pruned groups at their values; held out by deal.

```bash
ai-fit-evaluation --target apprentice --suite data/ai/regret/gateway-elevation --output-dir <dir> --pairs-per-root 24 --trees 200 --workers 12
```

Pairwise accuracy against the teacher's order (teacher v2 labels), gap-weighted, on held-out pairs:

| Seat, roots | Heuristic | Apprentice, linear | Apprentice, trees | Trees − heuristic |
|---|---:|---:|---:|---:|
| Corp, core held-out (121) | 0.616 | 0.642 | 0.684 | +0.067 [+0.029, +0.106] |
| Corp, every held-out (850) | 0.576 | 0.606 | 0.639 | +0.063 [+0.040, +0.086] |
| Runner, core held-out (224) | 0.612 | 0.664 | 0.676 | +0.064 [+0.037, +0.092] |
| Runner, every held-out (711) | 0.617 | 0.661 | 0.684 | +0.066 [+0.050, +0.082] |

The linear arm is clearly under the teacher's order, so the trees go forward (200 trees, the pairwise gradient of
S4.2c's `BoostedTreeScorer`): `data/ai/apprentice-v1.json`, embedded, with its report. **Inference:** 101 µs (Corp)
and 69 µs (Runner) a root on precomputed features, every candidate included; in play, with its feature extraction,
237 µs a turn-action decision it applies to (`engine-bench --profile-game`, 8 box games, the
`apprentice` segment), against the step's 50 µs target.

**Use.** `apprentice 1` re-orders the heuristic's top priority band of a turn action by `g`; priorities and vetoes are
untouched. Off in every profile; the entrants are `standard-heuristic` and `standard-apprentice` (`RatingLadder`).

**Regret** (`ai-regret score`, teacher v2 labels, held-out, the pair scored paired; `--extend-pruned` first labelled on
full samples the 160 pruned Corp groups the two versions chose, so neither is scored on a pruned value; the Runner had
none). The labels file now carries those 160 groups; no core root had one, so the core's figures are unchanged.

| Seat | Roots | Apprentice | Heuristic | Apprentice − heuristic |
|---|---|---:|---:|---:|
| Corp | core held-out (196) | 0.0301 | 0.0329 | −0.0028 [−0.0069, +0.0010] |
| Corp | core held-out, discordant (60) | | | −0.0091 [−0.0218, +0.0034] |
| Corp | every held-out (1,112) | 0.0492 | 0.0569 | −0.0077 [−0.0134, −0.0028] |
| Corp | every held-out, discordant (391) | | | −0.0219 [−0.0381, −0.0080] |
| Corp | the pair's `diverge` roots (650, 48 games) | | | −0.0077 [−0.0143, −0.0014] |
| Runner | core held-out (270) | 0.0325 | 0.0349 | −0.0024 [−0.0062, +0.0014] |
| Runner | core held-out, discordant (140) | | | −0.0047 [−0.0118, +0.0027] |
| Runner | every held-out (885) | 0.0334 | 0.0369 | −0.0034 [−0.0059, −0.0009] |
| Runner | every held-out, discordant (466) | | | −0.0065 [−0.0113, −0.0020] |
| Runner | the pair's `diverge` roots (1,476) | | | −0.0111 [−0.0140, −0.0083] |

Live Standard on the core held-out roots: Corp 0.0371 (the apprentice is below it, −0.0069 [−0.0127, −0.0014]), Runner
0.0349 (the Runner's Standard is the heuristic). The `diverge` roots were labelled for the pair (1,391 new, 735 reused,
24 samples, horizon 4, `--decide 8 --record 24 --prune 4`).

**Ladders**, three entrants each against each in both seats (`standard-heuristic`, `standard-apprentice`, `standard`):

| Pool | Seat | Apprentice − heuristic | Heuristic − Standard |
|---|---|---:|---:|
| Box, 400 deals (3,600 games, 18 min) | Corp | +24 [−3, +50] | −44 [−70, −20] |
| Box | Runner | **−248 [−277, −223]** | 0 [−21, +24] |
| Gateway, 240 deals (2,160 games, 10 min) | Corp | −12 [−45, +20] | −97 [−131, −63] |
| Gateway | Runner | **−371 [−407, −345]** | 0 [−30, +36] |

The apprentice Runner scores 8% on both pools. No errors and no capped games.

**Puzzles** (`ai-puzzles run --entrant`): the apprentice passes 31/37 on the box set and 4/5 on the NSG set, against the
heuristic's 32/37 and 5/5 (live Standard: 31/37 and 5/5). It gains run-targeting (5/5 against 3/5, the two teacher
puzzles the heuristic misses) and loses punisher-risk (7/9 against 9/9) and an economy puzzle; every new failure is
"Gain 1 credit" or a run into an unknown remote with a grip an ambush flatlines.

**Why the Runner loses: it banks credits.** In 12 sample games against the heuristic (`ai-runner-diagnosis`) the
apprentice Runner ends on 15.5 credits on average against the heuristic's 3.4, with 0.8 agenda points against 3.3. Its
discordant choices on the pair's `diverge` roots are mostly "Gain 1 credit" over a draw (189 roots), a run (185), an
ability (174), a card played (133) or installed (124), and the teacher rates each of those level or slightly better
(−0.001 to −0.015 a root). The teacher plays out 4 turns and scores the end with the pool's fit; over 4 turns a banked
credit costs nothing the leaf sees, and the leaf values credits. The apprentice learned the teacher's order faithfully,
and the order is wrong in this way. This is S4.4's warning made concrete: a 4-turn horizon ending on the pool's fit is
not the game's end.

**Where the apprentice's order differed from the teacher's most** (its blunder list, for S6): on every held-out root
it is more than 0.02 worse than the heuristic at 100 Corp and 100 Runner roots (better at 124 and 143). The Corp's are
mostly "Gain 1 credit" or a different install where the heuristic installs (40 roots), and installs or advances where
the heuristic plays an operation; the worst four lose 0.33 to 0.42 a root (an install, an operation, an advance where
the heuristic plays a card). The Runner's are "Gain 1 credit" where the heuristic plays, draws, runs, installs or uses an
ability (at least 43 roots), and runs on a remote where the heuristic draws.

**Corp Standard's planner stays.** Standard with its 3×3 planner off is below live Standard on both pools (box −44
[−70, −20], Gateway −97 [−131, −63]), so the planner is worth keeping on Corp Standard.

**Cost of a decision** (8 box self-play games each, one thread): live Standard 2,001 µs a decision (the Corp's planner
1,710 µs of it), the heuristic 297 µs, the apprentice 311 µs. Every Standard budget holds; nothing ships, so
`PolicyPerformanceTests` are unchanged (they pass in bot-check).

**Next.** The apprentice is only as good as its teacher's order. A refit on the core's game's-end labels (S4.2c's
recording, 2,050 roots, every line on every sample) would teach it the order that whole games reward; it is not run
here.

### S1.8, the core: teacher v3 is the suite's teacher (8 October 2026)

**Teacher v3** is teacher v2's search played to the game's end and scored by the result: the line search (beam 5,
a one-turn screen on a quarter of the samples, two finalists), the live Standard opponent (`hard-model`), 32 samples,
no leaf. `RegretLabels.VersionOf` names it `v3` while the live versions are Corp v31 and Runner v28.

**The files** (`data/ai/regret/gateway-elevation`):

- `labels.jsonl.gz`: the core's 2,050 roots labelled by v3 (S4.2c's game's-end relabel, every group on all 32
  samples, none pruned), and the 6,855 targeted roots at v2 until S1.8 relabels them, each label naming its teacher.
- `labels-v2.jsonl.gz`: the whole suite at v2 as it stood (with S4.5's 160 extended Corp groups);
  `labels-v2-repeat*` and `labels-v2-targeted-repeat*`: v2's noise floors on the core and on targeted roots.

**Scoring reads one teacher.** `ai-regret score` refuses roots labelled by more than one teacher and names them;
`--teacher-version V` keeps the roots that teacher labelled. A held-out read of the whole suite is therefore either
the core (v3) or the targeted roots (`--teacher-version v2`) until those are relabelled.

**bot-check's line** reads the core's held-out roots on v3 (`--teacher-version v3`):

| Version | Teacher v3, core held-out | Teacher v2, the same roots (before) |
|---|---|---|
| Corp v31 (196 roots) | 0.0579 [0.0459, 0.0719] | 0.0371 [0.0301, 0.0455] |
| Runner v28 (270 roots) | 0.0591 [0.0494, 0.0675] | 0.0349 [0.0291, 0.0404] |

The two columns are different labellings and are not compared: v3's values are win rates at the game's end, where
v2's are a 4-turn play-out ending on the pool's fit, so their scales differ. From here every regret on the core is v3.

**S1.3's table on the core at v3** (`--core --teacher-version v3`, all 2,050 core roots; the targeted roots wait for
their relabel):

| Comparison | Teacher v3, the core |
|---|---|
| Runner v24 − v25 | +0.0033 [0.0019, 0.0050] |
| Runner v24 − v26 | +0.0041 [0.0026, 0.0058] |
| Runner v27 − v28 | +0.0014 [0.0000, 0.0029] |
| Runner v26 − v27 | 0.0000 |
| Corp v28 − v29 | +0.0003 [−0.0005, 0.0010] |
| Corp v28 − v30 | +0.0004 [−0.0028, 0.0030]; discordant (95) +0.0033 [−0.0257, 0.0303] |
| Corp v29 − v30 | +0.0001 [−0.0030, 0.0031]; discordant (78) +0.0012 [−0.0317, 0.0296] |
| Corp v30 − v31 | 0.0000 |

| Version (Standard) | Regret, core | Turn | Run | Held out |
|---|---|---|---|---|
| Runner v24 | 0.0696 [0.0653, 0.0736] | 0.0808 | 0.0190 | 0.0692 |
| Runner v25 | 0.0663 [0.0620, 0.0704] | 0.0768 | 0.0183 | 0.0628 |
| Runner v26, v27 | 0.0655 [0.0615, 0.0694] | 0.0759 | 0.0183 | 0.0628 |
| Runner v28 | 0.0640 [0.0598, 0.0677] | 0.0741 | 0.0183 | 0.0591 |
| Corp v28 | 0.0691 [0.0624, 0.0773] | 0.0934 | 0.0272 | 0.0596 |
| Corp v29 | 0.0688 [0.0618, 0.0769] | 0.0934 | 0.0265 | 0.0598 |
| Corp v30, v31 | 0.0687 [0.0618, 0.0760] | 0.0932 | 0.0265 | 0.0579 |

**The Runner's ordering holds at v3**: v24 below v25 and v26, and v28 above v27, each separated, by margins like v2's
on the core (+0.0033, +0.0038, +0.0011). **The Corp's does not show on the core**, as it did not at v2 (the core holds a
third of the Corp's discordant roots, S1.7); its gate is read once the S1.6 Corp roots are relabelled at v3.

**The Corp's 3×3 planner on v3** (core held-out roots, S4.5b's run): live Standard against the heuristic alone
+0.0029 [−0.0049, +0.0110] (standard 0.0579, the heuristic 0.0550), level, where v2 had the planner worse. The ladder
has it ahead (+36 to +97 Elo); the suite at v3 no longer disagrees, it cannot separate the two.

**Still to do in S1.8:** the S1.6 Corp roots relabelled at v3 (about 1.8 times their v2 labelling), the 26 certified
puzzles re-certified at v3, and the noise floor measured again on the core.

### S4.5b: the apprentice on teacher v3 (8 October 2026)

**Gate not met in either seat; Standard stays the heuristic.** On v3 labels the apprentice no longer beats the
heuristic's order, and in play it is far worse in both seats: the Corp's builds remotes and leaves the centrals open,
the Runner's runs recklessly.

**The diagnostic** (the core's turn roots; a choice's value is its 32-sample game result at v3, its 4-turn play-out
ending on the pool's fit at v2):

| Seat, roots | Teacher | "Gain 1 credit" ranked best | In the top two | Choices ranked above it | Within 1 SE of the best |
|---|---|---:|---:|---:|---:|
| Corp, 518 | v2 | 10.0% | 28.4% | 0.376 | 28.6% |
| Corp, 518 | v3 | 11.4% | 46.5% | 0.283 | 22.4% |
| Runner, 1,006 | v2 | 12.2% | 27.8% | 0.378 | 52.6% |
| Runner, 1,006 | v3 | 13.0% | 38.1% | 0.330 | 41.9% |

Teacher v3 does not rank a credit worse than v2 does: it ranks it best as often and higher on average. What v2 did was
put it level with the best more often (within one standard error at 52.6% of the Runner's roots against 41.9%), which a
pairwise fit weighted by the gap reads as "no worse". The bias S4.5's Runner learned is that flatness, not a credit
ranked first.

**The fit** (`ai-fit-evaluation --target apprentice --teacher-version v3 --version 2`): the core's v3 turn roots only,
1,524 roots and 16,902 rows (S4.5 had 6,759 roots at v2), 90 s. On its held-out pairs:

| Seat | Arm taken forward | Pairwise − heuristic | Top-band regret − heuristic |
|---|---|---:|---:|
| Corp | linear | +0.031 [−0.045, +0.094] | −0.0181 [−0.0359, +0.0005] |
| Runner | trees | +0.000 [−0.025, +0.025] | +0.0036 [−0.0042, +0.0121] |

`data/ai/apprentice-v2.json`, embedded; `apprentice N` now names the document (1 is S4.5's, 2 this one), so
`standard-apprentice` is the latest and `standard-apprentice-v1` S4.5's.

**Regret** (`ai-regret score`, teacher v3, the core's held-out roots, paired):

| Seat | Apprentice v2 | Heuristic | Apprentice v1 | Standard | v2 − heuristic | Discordant |
|---|---:|---:|---:|---:|---|---|
| Corp (196) | 0.0451 | 0.0550 | 0.0474 | 0.0579 | −0.0099 [−0.0218, +0.0019] | −0.0211 [−0.0469, +0.0024] (92) |
| Runner (270) | 0.0622 | 0.0591 | 0.0578 | 0.0591 | +0.0030 [−0.0038, +0.0096] | +0.0052 [−0.0070, +0.0172] (156) |

S4.5's apprentice reads better than the heuristic on the Corp's v3 roots too (heuristic − v1 +0.0077 [+0.0014,
+0.0139]); neither apprentice is separated from the heuristic in the Runner seat.

**Ladders** (three entrants each against each, both seats):

| Pool | Seat | Apprentice v2 − heuristic | Heuristic − Standard |
|---|---|---:|---:|
| Box, 400 deals (3,600 games, 25 min) | Corp | **−216 [−239, −185]** | −36 [−65, −10] |
| Box | Runner | **−145 [−172, −127]** | 0 [−20, +29] |
| Gateway, 240 deals (2,160 games, 12 min) | Corp | **−244 [−276, −208]** | −95 [−126, −63] |
| Gateway | Runner | **−262 [−296, −233]** | 0 [−35, +36] |

No errors and no capped games.

**What the apprentices do in play** (the box games, `ai-runner-diagnosis` and `ai-lint`):

- **Runner:** it no longer banks credits (8.1 at the game's end against the heuristic's 5.4; S4.5's ended on 15.5), but
  it runs more (1.16 runs a turn against 0.92) and flatlines itself in breaches 75 times in 1,200 games against 11,
  and scores 1.4 fewer agenda points a game (3.6 against 5.0). It wins 28% of its games against the heuristic's 48%.
- **Corp:** it leaves a central without ICE while its remotes grow in 0.99 lints a game against the heuristic's 0.08,
  protects an empty remote 0.51 times a game against none, and lets Archives be farmed twice as often (0.28 against
  0.14). It scores 35% across the ladder against the heuristic's 68%.

**Puzzles:** 31/37 and 4/5 (the heuristic 32/37 and 5/5). **Cost:** 416 µs a decision (the apprentice step 344 µs where
it applies) against the heuristic's 297 µs; within Standard's budgets.

**Reading.** Both apprentices read level or better than the heuristic on the suite and lose heavily on the ladder, in
different ways each time. The suite asks one question per root, on states the heuristic reached; a policy that
re-orders the heuristic's top band at every turn reaches states the suite never sampled (a Corp with three remotes and
open centrals, a Runner on its fourth run of the turn) and compounds small errors there. A per-root regret below the
heuristic's says nothing about that, whichever teacher labels the roots. The apprentice is set aside in both seats;
before it returns, the rows would need the apprentice's own states (its games' roots labelled and added, round by
round), not a better teacher alone. Standard stays the heuristic in both seats, and the Corp keeps its planner.

### S4.6: run choices, the Runner's own curve (8 October 2026)

**Reading: no cell beats the heuristic's own run choices; Runner Hard keeps Standard's run choices.**

**The roots:** the core's 224 Runner run-choice roots (165 continue or jack out, 59 trash or not). **The rows:** the
existing end-of-line recordings (teacher v3's game's end, S4.2c's 4 turns, S4.4's opponent's turn) and one new recording
at the run's end (`--horizon run`, new: the play-out stops where the run in progress at the root ends and the pool's fit
scores that state; 224 roots, under a minute). The turn's end is read from the
game's-end rows' end-of-turn states, as in S4.4.

```bash
ai-regret label data/ai/regret/gateway-elevation --samples 32 --horizon run --lines 5 --screen-turns 1 --roots run-roots.txt \
  --labels labels-run.jsonl.gz --end-lines end-lines-run.jsonl.gz --workers 12
ai-regret score data/ai/regret/gateway-elevation --version runner-heuristic-v28 --side runner --core --teacher-version v3 --choices choices.jsonl
ai-regret horizon-curve data/ai/regret/gateway-elevation --reference end-lines-game.jsonl.gz --kind run --horizon "run's end=end-lines-run.jsonl.gz" \
  --horizon "opponent's turn=end-lines-opponent.jsonl.gz" --horizon "4 turns=end-lines.jsonl.gz" --choices choices.jsonl --choice-version runner-heuristic-v28
```

**The planner's-view accuracy is nearly unreadable here:** the game's-end reference separates 17 pairs of choices over
the 224 roots on all 32 samples, and 24 on halves. Run choices are mostly level at the game's end. On those pairs, design
(b): the turn's end 0.66 to 0.75, the run's end 0.63 to 0.65, the opponent's turn 0.64 to 0.73, 4 turns 0.64 to 0.66,
the game's end 0.57 to 0.65, every interval about ±0.18. No cell reaches 80%.

**Pick regret** (`--choices`, new: each cell picks its best choice on one half of the samples and the other half's game
results score the pick, against the heuristic's own choice scored on the same halves, paired by root; design (b)):

| Cell | k = 4 | k = 8 | k = 16 |
|---|---|---|---|
| The heuristic's choice (Runner v28) | 0.0275 [0.0218, 0.0332] | | |
| Run's end | +0.0047 [+0.0004, +0.0095] | +0.0037 [−0.0007, +0.0085] | +0.0035 [−0.0011, +0.0085] |
| Turn's end, general fit | +0.0056 [+0.0010, +0.0103] | +0.0043 [−0.0002, +0.0089] | +0.0021 [−0.0024, +0.0066] |
| Opponent's turn | +0.0056 [+0.0013, +0.0099] | +0.0049 [+0.0005, +0.0093] | +0.0025 [−0.0022, +0.0071] |
| 4 turns | +0.0068 [+0.0022, +0.0117] | +0.0058 [+0.0009, +0.0109] | +0.0052 [−0.0004, +0.0109] |
| Game's end | +0.0054 [+0.0006, +0.0104] | +0.0050 [−0.0003, +0.0106] | +0.0056 [−0.0011, +0.0128] |

(The cells are each one's regret minus the heuristic's: positive is worse.) Every cell is worse than the heuristic at 4
samples and none is better at 16.

**Cost** (`ai-regret decision-cost`, 40 of the roots, 4 samples, one thread; 2.0 choices a root):

| Horizon | Mean | p95 |
|---|---:|---:|
| Run's end | 8 ms | 26 ms |
| Turn's end | 35 ms | 73 ms |
| Opponent's turn | 74 ms | 133 ms |
| 4 turns | 291 ms | 572 ms |
| Game's end | 744 ms | 1,479 ms |

Inside 200 ms: the run's end, the turn's end, and the opponent's turn at 4 samples (about 8 at the mean). None of them
ranks the Runner's run choices better than the heuristic does, so a play-out at a run choice would spend Hard's time
to choose worse. S5.2's Runner Hard keeps Standard's run choices, and its turn actions are Standard's decider too
(S4.4), so Runner Hard has nothing to plan on this pool: the record for S5.2 is that Runner Hard is Standard's Runner
until a horizon ranks something.

### S1.8, the rest: the puzzles, the noise floor and the Corp's targeted roots at v3 (8 October 2026)

**Recertification** of the 26 teacher-certified puzzles at 128 samples with teacher v3, the game's end (S1.5's rule:
the best group passes the puzzle, its standard error is at most 0.04, and every failing group is 0.15 or more behind):
**7 hold**. Of the 14 gated ones, 6 hold (`teacher-9becf188`, `-acda6263`, `-ad565c84`, `-d031e97d`, `-df4609f1`,
`-e0d88291`) and 8 fail and move to a new `teacher-v3` split, out of the gate (`ai-puzzles run --split teacher-v3`).
None fails because its accepted action is no longer the best:

- the lead falls under 0.15 (5): `teacher-4b40e915` (0.148), `-47f84833` (0.141), `-e311a6ac` (0.047), and two where
  nothing the side does changes the result, `-9316ce82` (the best group wins 1.6%) and `-e5d97a1a` (the best 91.4%,
  every group within 0.016);
- the best group's standard error is over 0.04 near an even game (2): `teacher-bfad8833` (0.609 ± 0.043) and
  `-cb2c69f6` (0.617 ± 0.043);
- both (1): `teacher-9bcefcf7` (0.477 ± 0.044, lead 0.141).

The split is named for the teacher the puzzles failed, as `teacher-v1` and `teacher-v2` are (the step's text said
`teacher-v2`, which already names v2's failures). Of the `teacher-v2` puzzles, one holds at v3 (`teacher-b39f5b92`,
lead 0.164) and stays out of the gate; three have v3's best group failing the puzzle (`teacher-20e668e7`, `-4da9c5de`,
`-93609fc4`). The two `teacher-v1` puzzles fail again.

**The gated box set is now 29 puzzles** (26 tuning, 3 held out). Standard (Corp v31, Runner v28) passes 28 of them;
it passed 31 of the 37 before, and five of its six failures are among the eight that moved. The one it still fails,
`teacher-e0d88291` (held out), holds at v3 with a lead of 0.867. On the `teacher-v3` split Standard passes 3 of 8.
