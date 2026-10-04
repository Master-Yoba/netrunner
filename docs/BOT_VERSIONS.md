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
