# Bot plan: implementation steps

The steps that carry out [BOT_PLAN.md](BOT_PLAN.md). Each step says what to build, where, how it is accepted, and what
to record. Steps are ordered; a step starts only when the one before it has met its gate, except where marked
independent. Sizes: S is one to two days, M is three to six days, L is one to two weeks.

Conventions for every step:

- Work on a feature branch off `dev` (`feat/bot-plan-s<n>`), commit checkpoints, open a PR into `dev` when the gate is
  met. Attach the evidence named under **Record**.
- Run `make bot-check` per change; the full gates (`make gates REF=<commit>`) once per step, before the push.
- The CLI runs on the server GC: give ladders and labelling runs the machine to themselves; 12 workers.
- No human, Codex or Claude games or reviews are part of any step. Section 7 of the plan says when they run.
- Keep the information boundary: every new reader of game state used by a policy reads an observation or a
  determinized sample, never the authoritative state. The twin tests must pass unchanged.

## S0. Measure before building (M)

**Why first.** Every later step is budgeted against these numbers, and the ladder's speed sets the resolution of every
gate.

### S0.1 Profile one ladder game and one planner decision

- Add `engine-bench --profile-game` (in `src/Netrunner.Cli/CliCommands.EngineBench.cs`) that plays one Standard
  self-play game on the box pool and reports time per action split into: observation build (`ObservationBuilder.Build`),
  agent decision, the per-action state round trip (`EngineJson.Serialize` and `DeserializeState`,
  `FullMatchRunner.cs:45`), authoritative `GameEngine.Apply`, and at the end replay serialization and
  `ReplayVerifier.Verify`. Also report the cost of one rollout step at the end-of-turn horizon and of a whole-game
  rollout, so S0.3 and S1.2 can be sized.
- Add `--profile-plan` that takes a saved game and a sequence, replays to it, and times one `TurnPlanner.Plan` call
  split into: sampling (`ObservationDeterminizer.Sample`), simulated `Apply`, `ObservationBuilder.Build` inside
  `TurnPlanner.Choose`, heuristic evaluation inside `Choose`, and leaf scoring.
- **Record** both tables in `docs/BOT_VERSIONS.md` (new; the running record of every version's evidence) under a
  "Reference costs" heading, with the box and date.

### S0.2 Fast ladder mode

- In `FullMatchRunner.RunAsync` (`src/Netrunner.Match/FullMatchRunner.cs`) put the **per-action state round trip**
  (the save-and-reload check before every `Apply`) and the end-of-game replay verification behind one `verify` flag
  passed from `RatingLadder.PlayAsync`. The round trip is probably the largest cost after the agent itself; S0.1 says.
- `ai-ladder --verify-replays-every N` (default 8, as `fuzz` uses) plays every Nth finished game with `verify` on and
  the rest with it off. `--verify-replays-every 1` is today's behaviour. The fuzz and every verified game keep both
  checks: they are what proves a state survives a save and reload.
- Remove the next largest cost found in S0.1 if it is not the agent itself (candidates: observation building for a seat
  whose decision is forced, repeated catalog lookups).
- **Gate:** a 240-deal box ladder in both modes gives identical results game by game (same seeds, same winners, same
  action counts), and the fast mode's wall time is recorded. Target: 1,000 deals on the box pool in under 10 minutes on
  12 workers. If the target is missed, record the gap and continue; the deals per ladder in later steps scale to what
  10 minutes buys.
- **Record:** the timing table and the game-by-game comparison in `docs/BOT_VERSIONS.md`.

### S0.3 Budget-scaling entrants

- Add ladder entrants `standard-x1`, `standard-x2`, `standard-x4`, `standard-x8` and `standard-x16` in
  `RatingLadder.CreateAgent` (`src/Netrunner.Match/RatingLadder.cs`): the current Hard planner settings at that multiple
  of `plan-samples`, on the current leaf (set 4). They are diagnostics, not shipping profiles.
- Size the run from S0.1's rollout cost. The default is ×1, ×4 and ×16 against each other on the box pool, 480 deals a
  pairing, both seats; if that exceeds about two hours on 12 workers, use ×1, ×2, ×4 and ×8 with 240 deals a pairing and
  record the choice. ×16 of Hard's samples is 48 to 64 samples a decision, which may be hours by itself.
- **Gate (a reading, not a pass):** record whether strength is monotone in budget per seat. The plan expects it is not
  for the Corp and weakly so for the Runner; that confirms the leaf is the target of S4. If it *is* monotone with lower
  bounds above zero in both seats, S4 still runs, but S5 may start from a higher budget sooner.
- **Record:** the table in `docs/BOT_VERSIONS.md`.

### S0.4 Rollout cost, cut where the trace says

**Independent of S0.2 and S0.3; needs S0.1's planner profile.** Revised on 4 October after S0.1's split and a CPU trace
of 60 Hard decisions and 32 whole-game rollouts (`dotnet-trace`, `dotnet-sampled-thread-time`). The plan's first rollout
policy (the heuristic at a reduced work budget with a cached `HeuristicContext`) can only reach the heuristic, which is
about a tenth of a rollout step; the step is simulated `Apply` (about half), then the observation build and read (about
a sixth each). So S0.4 takes exact cost out where the trace finds it, and the rollout policy stays today's: the
planner's own per-step `Choose`, which S1.2's teacher therefore shares with the shipped planner.

- **Done:** the observation read builds its JSON options once (a tenth of a read); the behaviour catalog computes each
  definition's fingerprint once instead of for every issued ability action (about a sixth of planner time in the
  trace, `BehaviorCatalogFingerprint.ComputeDefinition`).
- **Shelved:** an exact direct observation-to-features mapper (about a seventh of planner time, but a hand-written copy
  of the serializer's semantics over some forty record types), and a thin state-to-features view (the same plus a
  second copy of the information boundary). Revisit only if S5's budgets need them after S5.0.
- **Gate:** exact changes only: a 240-deal box ladder identical game by game to S0.2's fast-mode run (same final state
  hashes), and S0.1's costs re-measured.
- **Record:** the trace's top costs, the costs before and after, and the ladder, in `docs/BOT_VERSIONS.md`.

## S1. The regret suite (M)

**Independent of S0.3; needs S0.1's planner profile and S0.4's costs to size the teacher.**

### S1.1 Decision roots

- A root record: deal (content ID, game seed), action sequence number, acting side, the agent's checkpoint digest, the
  legal candidates (action IDs with their descriptions), and the category tags. Store as JSON lines under
  `data/ai/regret/<pool>/roots.jsonl`. Roots are replayed from the ladder's saved games (`LadderGameArchive`), so they
  carry no engine state of their own.
- `ai-regret roots <games-dir> --output <suite-dir> --per-game N --seed S`: samples N turn-action roots per game with at
  least two meaningful candidates (after the filter in S5.1; until then, at least two candidates whose heuristic
  priorities differ by less than a band or whose scores are within 20%), plus every run-choice root with a real option.
  Start with 960 box games → about 8,000 roots; hold out one game in five by game, never by root.
- Add every saved decision from `BotLoop1Tests`, `BotLoop2Tests` and `BotStrategy1Tests` as roots with their
  categories.

### S1.2 Teacher labels

- `ai-regret label <suite-dir> --samples 32 --horizon game|<turns> --evaluation 4 --workers 12`: for each root,
  rebuilds the observation and checkpoint at the sequence, deals 32 shared samples from the tracker and pool as
  `TurnPlanner.Pins` does, rolls out every candidate on every sample **past the leaf's horizon** (to the game's end
  with the planner's rollout choices, or `<turns>` turns and then the leaf; start with `game` and fall back to 4 turns
  if S0.4's whole-game rollout cost makes 8,000 roots exceed about four hours on 12 workers), and writes per-candidate
  mean win probability with its standard error over the samples, the root value (the best candidate's) and the horizon
  used. Deterministic given the suite's seed. The point of the depth is that labels come from play, not from the leaf
  section 1.3 of the plan calls noise.
- **Noise floor:** `ai-regret label --repeat <n> --repeat-seed S` labels a random `n` roots (start 200) a second time
  with independent sample seeds and writes how often the two labellings pick different best candidates, and the median
  standard error. The suite carries it, and `score` reports agreement against it (the plan's 5.1).
- Move the saved-decision replay into `Netrunner.Match` as `SavedDecision.Replay` and put **every** test copy of
  `ChooseAtAsync` on it. There are 15 copies, and they differ in shape, so `SavedDecision.Replay` must cover:
  - finding the decision by sequence number, or by a predicate over the state and pending decision
    (`CorpLintV19Tests`);
  - returning the replayed state with the choice, as most copies do (`BotLoop1Tests.ChooseWithStateAtAsync`,
    `RunnerCodexV14Tests.ChooseInAsync`), and the match definition (`CorpAgendaHidingV16Tests`);
  - either side acting (`PlaytestWave5Tests` and `BotLoop1Tests` take it);
  - both fixture formats: `LadderGameArchive` saved games, and raw `Fixtures/*-replay.json` replays whose content and
    seed the test names (`CorpPlaytestV13Tests`, `CorpPlaytestV14Tests`, `SelfPlayReviewTests`).

  The copies: `BotLoop1Tests` (shared with `BotLoop2Tests` and `BotStrategy1Tests`), `CorpAgendaHidingV16Tests`,
  `CorpCodexV20Tests`, `CorpLintV19Tests`, `CorpPlaytestV11Tests`, `CorpPlaytestV13Tests`, `CorpPlaytestV14Tests`,
  `CorpPlaytestV15Tests`, `CorpPlaytestV17Tests`, `CorpPlaytestV18Tests`, `PlaytestWave5Tests`, `RunnerCodexV14Tests`,
  `RunnerInferenceV21Tests`, `RunnerPlaytestV13Tests` and `SelfPlayReviewTests`. One replay path, so a root in the suite
  and a root in a test are the same thing; every moved test still passes unchanged in what it asserts.
- If reducing `--per-game` is still needed, record it.

### S1.3 Scoring and the blunder list

- `ai-regret score <suite-dir> [--version V] [--profile P] [--json]`: asks the named policy version and profile to
  choose at every root (the production path, not the planner's rollout path) and reports regret, agreement, per side,
  per pool, per category, with a 200-draw bootstrap interval over roots; `--blunders N` lists the N highest-regret
  roots with every candidate's value and the version's reason string.
- **Gate:** the suite separates what the ladder could not: Runner v24, v25 and v26 scored on the same suite in under
  five minutes each, with intervals that order at least two of them. If no pair separates, first check the teacher on
  the noise-floor roots:
  - if the deep labelling disagrees with *itself* (the noise floor) on more than about a third of best candidates, the
    labels are too noisy to rank: raise `--samples` on those roots or move to the 4-turn horizon (less noise, a little
    of the leaf's bias), and re-label;
  - otherwise compare the deep labels with turn's-end labels on the same roots; disagreement **above the noise floor**
    by more than a quarter means the short horizon is the problem and labels must stay deep. Disagreement at or below
    the floor says nothing about the horizon.

  Only then treat the roots as too easy: raise `--per-game` on roots where the heuristic's top two scores are close and
  re-label.
- **Record:** the three versions' table, the labelling time, and the first blunder list in `docs/BOT_VERSIONS.md`.

## S2. Puzzle suite v2 (M)

**Independent of S1 after S1.1's root format exists.**

### S2.1 Manifest and runner

- `data/ai/puzzles/<pool>/puzzles.json`: each puzzle names a root (S1.1 format), a kind (`decision`, `line`,
  `nested`), a category, a split (`tuning` or `held-out`), a title, and an acceptance:
  - `decision`: the accepted action kinds, or a card definition ID to play, install or run;
  - `line`: a predicate over the end-of-turn state from a fixed vocabulary (card X installed; counters on card X ≥ n;
    a run on server Y succeeded; credits ≥ n; card X played before any install; the Runner's grip ≥ n at the turn's
    end);
  - `nested`: the accepted answer at a nested decision reached by replaying the root's own line.
- `ai-puzzles <manifest> --version V --profile P [--json]` (in `src/Netrunner.Cli/CliCommands.Puzzles.cs`) plays each
  puzzle with the production agent and prints per-category pass counts and the failures with the agent's reason.
- The M5 tactical suite (`ai-tactics`) stays frozen as a regression check; it is not extended.

### S2.2 Content

- Convert every `BotLoop1Tests`, `BotLoop2Tests` and `BotStrategy1Tests` case into a puzzle. Keep the tests until the
  manifest is wired into `make bot-check`, then replace them with one test that runs the manifest and asserts the
  critical puzzles.
- Write line puzzles for every item in the loops' and strategy's **deferred** lists: Petty Cash before installs,
  Mercia's free install, Synapse Global's tagged gain, Touch-ups before advancing, Spin Doctor before the install that
  trashes it, Ritual before Fermenter, GAMEDRAGON™ Pro, a second Jailbreak on unchanged R&D, drawing before an advanced
  remote, holding back a run that arms a punisher. Each category gets at least two negative controls.
- Target: 60 puzzles across the ten categories in the plan's 5.3, a third held out.
- **Gate:** the current defaults (Corp v30, Runner v26) score the suite; the per-category table is the baseline. At least
  one category where the defaults score under half, so the suite has room to move.
- **Record:** the baseline table in `docs/BOT_VERSIONS.md`.

### S2.3 Replace the verification tiers

- Wire `ai-regret score` (target pool, the suite's held-out split), `ai-puzzles`, and `ai-lint` counts on the latest
  saved run into `scripts/bot-check.sh` behind `BOT_CHECK_SUITES=1`, with a summary line each.
- Update `AGENTS.md`'s "Testing Guidelines" bot paragraph to the plan's section 6 tiers, and
  [BOT_LOOP_WORKFLOW.md](BOT_LOOP_WORKFLOW.md) section 7 to point there.

## S3. One live policy (M)

**Independent of S1 and S2. Do it before S4 changes the heuristic's features.**

- Copy `CorpHeuristicPolicy*.cs` and `RunnerHeuristicPolicy*.cs` to `src/Netrunner.AI/Legacy/` as `LegacyCorpPolicy`
  and `LegacyRunnerPolicy` serving every version through v30 and v26. `OfflinePolicies.Create` routes those versions
  there. `CorpPolicyProfiles` and `RunnerPolicyProfiles` keep constructing their profiles.
- In the live files: set `Generation` to the latest value everywhere, resolve every `Generation >=`, `version is` and
  `Plans`/`Repairs` branch to the latest behaviour, delete the dead branches, and remove the `Generation` property. One
  version string each: `corp-heuristic-v31`, `runner-heuristic-v27`, chosen by behaviour identical to v30 and v26.
- Add `standard-prev` to the ladder: an entrant that runs the previous live version from a frozen build under
  `.tools/ladder-anchors/<version>/` (`scripts/ai-ladder.sh` builds it from a recorded commit when missing). Until S5,
  `standard-prev` is Corp v30 and Runner v26 from the legacy files; afterwards it is the frozen build.
- **Gate** (needs nothing from S1 or S2):
  - a 240-deal box ladder and a 240-deal Gateway ladder of v31 against v30 and v27 against v26, with `--save-games`,
    where every game comes out identical to its seat-swapped pair on the same deal: the same winner, action count and
    final state hash. That is a stronger check than any suite, since it covers every decision in 960 games. It is valid
    because an agent's random stream is seeded from the match seed and its side only
    (`AgentLifecycleHost.DerivePolicySeed`, called from `OfflineMatchFactory`), never from the policy version or the
    configuration digest; a test asserts that, so a later change that seeds from the version fails loudly instead of
    voiding this gate;
  - `PolicyVersionTests` extended: every version through v30 and v26 constructs, and the legacy files differ from the
    policy files at the commit the step branched from only in the rename lines (class names and namespace), checked by
    a normalized diff in the test;
  - every frozen benchmark digest, saved-game fixture and twin test unchanged;
  - `make gates` passes.
  - When S1 exists, 100% agreement on the regret suite is added as a cheap repeat of the same check.
- **Record:** the file sizes before and after, and the agreement check, in `docs/BOT_VERSIONS.md`.

## S4. Evaluation set 5 (L)

**Needs S1 (labels) and S3 (a live policy to add features to).**

### S4.1 Features

- Implement the plan's 4.2 terms in `EvaluationFeatures` as feature set 5, each from the observing side's view, using
  `RunModel` for break costs and `CardTracker` for densities and the punisher prior (`RunnerInference` already computes
  the latter for v26). Names in `NamesFor(5)`.
- Unit tests per term on constructed observations (`tests/Netrunner.AI.Tests/EvaluationFeaturesTests.cs`), including
  a twin test: two states with the same observation give the same features.

### S4.2 Fitting to the teacher

- `ai-fit-evaluation --target teacher --suite <suite-dir> --feature-set 5 --outcome-weight 0.2 --output <doc>`: fits
  one model per side to the teacher's root value (primary) and the game's outcome (auxiliary), on the suite's training
  games, reports held-out log loss and mean absolute error against the teacher's value, and compares to set 4 scored the
  same way. The model is `LogisticSideModel` or, if the held-out error asks for it, a two-layer additive model over the
  same named features; either is stored in `data/ai/evaluation-v5.json` with provenance.
- Fit **three** models, so the gain is attributed: set 4 with its shipped weights (set 2's, never fitted to set 4's own
  features), set 4 refitted to the teacher (new target, old features), and set 5 fitted to the teacher (new target, new
  features).
- **Gate:** set 5 beats both set 4 arms on held-out teacher values in both seats, and the planner (Hard settings)
  scoring with set 5 beats the same planner on each set 4 arm on the regret suite's held-out roots. If the refitted set
  4 captures most of the gain, the features are not the lever and S4.1's terms are reconsidered before S4.3.
- **Record:** the three fit reports and the regret comparison.

### S4.3 Budget scaling on set 5

- Re-run S0.3's entrants with `plan-evaluation 5`.
- **Gate:** monotone in both seats with each step's lower bound above zero. If not, the leaf still lacks a term the
  rollouts need; use the blunder list to find it, add it, refit, and repeat. S5 does not start until this holds.

## S5. The planner becomes Standard (L)

### S5.0 Engine cost of simulation

**Before S5.1; can run any time after S0.** S0.1 says Standard cannot plan 8 samples × width 4 inside 50 ms p95 unless a
rollout step gets cheaper, and S0.4's trace says the step is mostly simulated `Apply`. Take exact cost out of
`GameEngine.Apply` in `ApplyMode.Simulation`, in the order the trace ranks it, re-tracing after each:

- `DeterministicHash.ForState` (about 8% of `ApplyCore`) and `StateInvariantValidator.Validate` (about 7%): whether a
  simulated state needs its hash and its invariants checked at every step, or only where a rollout's result is used. A
  change here is a decision that simulated states are trusted, so it is taken with the user, not by a step.
- `TurnFactsState` copies (about 5% of all time), `BehaviorStaticModifierProjection.Sum` (about 6%), and building the
  working state (`WorkingState.Build` and its constructor, about 8% of `ApplyCore`).
- **Gate:** each change is exact: a 240-deal box ladder identical game by game to the run before it, the per-slice
  fuzz on every pool (an engine change), and the costs re-measured into `docs/BOT_VERSIONS.md`.

### S5.1 Pipeline changes in `TurnPlanner`

- **Meaningful-candidate filter:** drop candidates the heuristic marks as bookkeeping (pass, end turn with clicks left
  while an affordable positive action exists) and exact duplicates (several copies of one card); keep up to
  `plan-width` by heuristic order.
- **Probability-space values:** each rollout's leaf as a win probability; candidate value = mean(leaf − root) +
  `static-mix` × (leaf after the first action − root). Remove the raw log-odds sum and the heuristic score from the value;
  the heuristic orders candidates and breaks ties within `plan-tie` (start 0.005).
- **Rollout policy:** today's per-step `Choose` (S0.4). Profile against S0.1 after S5.0 and, if a Standard decision
  still cannot fit 50 ms p95 at 8 samples × width 4, implement the plan's 4.3 options (the heuristic at a reduced work
  budget, then the distilled table policy) and record each number. If the rollout policy changes here, re-label the
  regret suite with it, so the teacher and the planner still share one.
- **Work budget:** `rollout-budget` (simulated decisions per root) in the profile; the planner stops sampling when it is
  spent and scores what it has. No wall-clock checks inside a decision.
- Everything behind profile parameters so Hard's current behaviour stays reproducible until the switch.

### S5.2 Profiles and tiers

- Standard: planner at 8 samples × width 4, `static-mix` 0.25, set 5, pool prior, full tracker. Hard: 32 × 6, decklist.
  Easy: heuristic, forgetful tracker (unchanged). Personalities keep shifting named terms of set 5.
- `PolicyPerformanceTests` on the reference box: Standard within 50 ms / 200 ms p95; Hard within the same, or with a
  documented larger budget for turn-start decisions only.
- Both sides' versions bump (`corp-heuristic-v32`, `runner-heuristic-v28`) because the default profile's decider changed.

### S5.3 Gates

- Regret suite: Standard's regret below the heuristic's (v31/v27) with the interval clear.
- Puzzle suite: no category worse; the sequencing and exposure categories better.
- Ladders on the box pool, 1,000 deals, `--stop-when-clear`: new Standard beats `standard-prev` in both seats with the
  lower bound above zero; loses to no anchor; Gateway pool level or better; tiers ordered Easy < Standard < Hard in both
  seats.
- Lint counts per 1,000 games not worse on any lint.
- Belief and run-model validations in `make bot-check` pass.
- **Record:** all of it in `docs/BOT_VERSIONS.md`; the human playtest expectation (Standard feels like it plans) noted
  for the user, not scheduled.

## S6. Expert iteration rounds (M each, repeating)

A round is one version. It runs when the previous round's gates are met.

1. Play 960 box games of the current Standard against itself with `--save-games`; add roots from them (S1.1), holding
   out by game.
2. Re-label the whole suite with the teacher at the current Standard's leaf and 4× its budget. Report teacher drift:
   the share of roots whose best candidate changed. Over 15% stops the round for inspection of the top changes.
3. Refit set 5's weights (a new document version, `evaluation-v5.<round>.json`).
4. Score the candidate (Standard with the new weights) by S5.3's gates, with 1,000-deal ladders only when the regret
   suite and puzzles improved.
5. Work the blunder list: the top 20 roots by regret. Each is a leaf term, a filter bug, a rollout-policy weakness or an
   engine bug; fix what is general, add a puzzle for each, and never a card-named rule.
6. Record the round in `docs/BOT_VERSIONS.md` and promote or discard.

Stop condition for a batch of rounds: two consecutive rounds that pass the regret suite but not the ladder, which
means the suite has stopped tracking strength and needs new roots from a stronger or different opponent (section 7 of
the plan is the way to get them, on the user's ask).

## Timeline and order

```text
S0 measure ──> S1 regret suite ──> S4 set 5 ──> S5 planner Standard ──> S6 rounds
           ──> S2 puzzles ───────┘        (S2 independent after S1.1)
           ──> S3 one live policy ────────┘  (S3 independent; before S4)
(S5.0, the engine cost of simulation, can run any time after S0 and must land before S5.1)
```

Elapsed, one developer with an agent: S0 one week including the ladder speedup; S1, S2 and S3 two to three weeks in
parallel; S4 and S5 three to four weeks; the first S6 round a week. About two months before Standard plans on both sides
with evidence. The first visible gain is earlier: S1's regret suite and S2's puzzles replace the 75-minute per-change
run as soon as they exist.
