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

**Outcome (5 October; the record is in `docs/BOT_VERSIONS.md`).** The box suite is `data/ai/regret/gateway-elevation`:
960 games stored as choice indices, 6,512 roots after deduplication (a deal's games share positions, so a position is
kept once and a deal is held out whole), and 23 findings. The teacher labels at **32 samples and a 4-turn horizon, then
the leaf**: the game's-end horizon was mostly ties at 8 samples and past four hours at 32. The noise floor is 51% best
disagreement but a mean flip cost of 0.018, so regret, paired on the same labels, is the number; agreement is not. The
gate holds: Runner v24 separates from v25 and v26, Corp v30 from v28 and v29, about 2 minutes a version. Because the
labels end in set 4's leaf, S4.2's set 4 control and S6's relabelling matter more, and a deeper horizon is worth trying
once S5.0 makes rollouts cheaper. Two gaps found after the gate are S1.4 and S1.5 below: the labels cover only the
heuristic's best six groups, and the teacher values a candidate with the heuristic's continuation of the turn.

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

### S1.4 Label every meaningful group (S)

**During S3; before S4.2.** The first labelling cut each root to the heuristic's best six groups, and a version's
choice outside them is unlabelled and drops out of its regret. That flatters older versions (the record says so) and
would hide the planner's choices, which are expected to fall outside the heuristic's top groups.

- `ai-regret roots` keeps every meaningful group (after the filter S5.1 defines; until then, every group whose
  priority is within one band of the best), with the heuristic's order recorded.
- `ai-regret score --label-missing` labels an unlabelled choice on demand with the suite's teacher settings and seed,
  and writes it back, so the suite grows where versions disagree with the heuristic.
- **Gate:** the unlabelled count is zero for every version in the S1.3 table; the table is re-reported.
- **Record:** the counts before and after in `docs/BOT_VERSIONS.md`.

**Outcome (5 October; the record is in `docs/BOT_VERSIONS.md`).** The box suite's roots have every meaningful group
(32,821 groups became 57,109) and the choices outside the old six were labelled on demand: no version in the S1.3 table
has an unlabelled choice. Counting them raised every older version's regret, as the S1.3 record warned; the Runner's
separation held and the Corp's intervals widened past zero.

### S1.5 The turn-line teacher (M)

**During S3 (it edits `TurnPlanner`, S3 edits the heuristic files); its relabel runs between S3's gate ladders; before
S4.** S2 found that the teacher, and so the shipped planner, values a candidate followed by the heuristic's play of the
rest of the turn, which undervalues a first action whose worth depends on its follow-up (plan 1.3 and section 9). The
horizon does not fix it. The fix is the turn-line search of plan 4.1, built once for both:

- In `TurnPlanner`, behind `plan-lines` (off by default, so Hard and every frozen digest are unchanged): at a turn-action
  root, enumerate lines of the root side's turn actions with a beam of `plan-width` over the heuristic's candidates at
  each of its decisions, to the turn's end (clicks spent or the turn passed). Nested choices inside an action, the
  opponent and later turns stay with today's per-step `Choose`. Score each line on the shared samples; a candidate's
  value is the best line beginning with it. Sequential halving: every line gets a quarter of the samples, the best half
  the rest.
- `ai-regret label --lines` uses it; labels record `lines=true`. Cost estimate from S0.1: about three times the
  one-step labelling (about six hours for the box suite on this box); if more, lower the beam to 3 and record it.
- **Gate:**
  - the twin tests pass with `plan-lines` on;
  - on the 23 Petty Cash roots and the worst root (`…leo-glacier~…topan-00202#358`), the line teacher's value of
    Petty Cash first is within noise of the best full line (the artifact is gone);
  - the whole box suite relabelled; the drift (share of roots whose best group changed, and the mean change of the best
    value) recorded as S6 would record it;
  - the 26 certified puzzles re-certified at 128 samples; those that fail are moved to a `teacher-v1` split and kept
    out of the gate;
  - S1.3's separation gate passed again on the new labels (Runner v24 against v25 and v26; Corp v30 against v28 and
    v29).
- **Record:** the cost, the Petty Cash check, the drift, the recertification and the new S1.3 table in
  `docs/BOT_VERSIONS.md`.

**Outcome (6 October; the record is in `docs/BOT_VERSIONS.md`).** The box suite is relabelled with the line teacher
(7 h 9 min; the S1.2 labels are kept as `labels-v1`). The settings depart from the spec on evidence: a beam of 3 cannot
find the Petty Cash line (it takes the heuristic's 4th and 5th choices), and screening every line by a full play-out
cost 5 to 6 times a one-step labelling, so the teacher searches a **beam of 5**, screens lines with a **one-turn
play-out on a quarter of the samples**, and plays the heuristic's line and the best other one to the horizon (2.75
times one-step). Gate:
  - the twin tests pass with `plan-lines` on (the live Hard bot; the live Runner's Standard does not plan);
  - Petty Cash first is level with the best at 18 of 24 roots (13 one-step), the worst root's gap 0.230 → 0.003;
  - drift recorded with the lift the labels carry; the best-group change (22.6%) is below the noise floor (57%);
  - 24 of the 26 certified puzzles hold; 2 moved to `teacher-v1`;
  - S1.3's separation: the Runner passes (v24 against v25 and v26); **the Corp does not**, as at S1.4. The discordant
    view (`ai-regret score --discordant`) and the exposure count (`ai-regret exposure`) say why: v30 is ahead where no
    agenda is involved and level on holding an agenda, at 4 turns and at 8, because neither the ladder's Runner nor
    the teacher's (its rollouts model the Runner as v7) punishes a remote it can get into. The Corp's gate stays open
    until a Runner v28 values that remote and the suite is relabelled by a teacher that models the live Runner.

## S2. Puzzle suite v2 (M)

**Independent of S1 after S1.1's root format exists.**

**Outcome (5 October; the record is in `docs/BOT_VERSIONS.md`).** `data/ai/puzzles/gateway-elevation` (49 puzzles) and
`nsg-core-sets` (5): the 28 converted tests and 26 teacher-certified decisions, 8 categories, 25 negative controls. The
defaults score 34/49 and 5/5; agenda exposure is 0/4, which meets the gate. Three deferred findings (Petty Cash first,
Ritual first, Spin Doctor before its trash) are not made puzzles: the one-step teacher values a first action with the
heuristic's continuation, which undervalued Petty Cash first by 0.23 at one root where the full lines are level (plan
section 9, the teacher's continuation, fixed before S4);
the target of 60 puzzles, ICE placement and inference, a third held out, and an NSG Core suite are still open. The old
saved-decision tests stay until S3 freezes the versions before v31 and v27: their halves that pin the older versions'
choices guard code that is still live until then, and `PuzzleSuiteTests` already asserts that the defaults pass every
converted puzzle.

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

**Outcome (5 October; the record is in `docs/BOT_VERSIONS.md`).** Corp v31 and Runner v27 are live, without a version
check of their own (135 `Generation` references to 4); `Legacy/` holds the 94a7404 policy files, pinned by hash. The
gate's ladders (box and Gateway, 240 deals each) are identical game by game. The shared helpers (`HeuristicContext`,
`RunnerValues`, `RunModel`) keep their generation switches, since freezing them would split shared types; a helper change
in S4 or S5 must leave the older generations alone or move the helper into `Legacy/` first. `standard-prev` is v30 and
v26 from the legacy files until S5; the frozen-build anchor under `.tools/ladder-anchors/` is built when S5 first needs a
previous live version that is not legacy code.

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
- **S1.5 raised the stakes.** The heuristic orders a turn's later clicks poorly: the Petty Cash line needed its 4th and
  5th choices, so the teacher searches a beam of 5 and a beam of 3 misses the line. Standard cannot afford a beam of 5
  per click inside 50 ms at today's cost, so either simulated `Apply` gets much cheaper here or the shipped planner
  searches a narrower beam than the teacher; S5.1's record says which, and what the narrower beam costs on the regret
  suite against the teacher's.

### S5.1 Pipeline changes in `TurnPlanner`

- **Meaningful-candidate filter:** drop candidates the heuristic marks as bookkeeping (pass, end turn with clicks left
  while an affordable positive action exists) and exact duplicates (several copies of one card); keep up to
  `plan-width` by heuristic order.
- **Probability-space values:** each rollout's leaf as a win probability; candidate value = mean(leaf − root) +
  `static-mix` × (leaf after the first action − root). Remove the raw log-odds sum and the heuristic score from the value;
  the heuristic orders candidates and breaks ties within `plan-tie` (start 0.005).
- **Turn-line search on** (`plan-lines`, S1.5): Standard and Hard value a candidate at its best line of the turn. The
  budget below is lines × samples, so S5.0's gains decide the beam Standard can afford inside 50 ms; if a beam of 4 does
  not fit after S5.0, Standard plans a beam of 3 or fewer samples and the record says which. Without the teacher's
  play-out, `plan-lines` takes the best of every screened line, more lines than the teacher's two finalists, so its
  values are more optimistic, and a candidate whose heuristic line ends the turn early has fewer lines and less of that
  lift. The values only rank candidates, so S5.1's gates check this on the regret suite (a planner preferring
  long-turn candidates for this reason shows as regret on short-turn roots).
- **Rollout policy:** today's per-step `Choose` (S0.4) for nested choices, the opponent and later turns. Profile against
  S0.1 after S5.0 and, if a Standard decision still cannot fit 50 ms p95, implement the plan's 4.3 options (the heuristic
  at a reduced work budget, then the distilled table policy) and record each number. If the rollout policy changes here,
  re-label the regret suite with it, so the teacher and the planner still share one.
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
- Ladders on the box pool, about 400 deals (what 10 minutes buys, S0.2), `--stop-when-clear`: new Standard beats
  `standard-prev` in both seats with the lower bound above zero; loses to no anchor; Gateway pool level or better; tiers
  ordered Easy < Standard < Hard in both seats.
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
4. Score the candidate (Standard with the new weights) by S5.3's gates, with box ladders (about 400 deals) only when
   the regret suite and puzzles improved.
5. Work the blunder list: the top 20 roots by regret. Each is a leaf term, a filter bug, a rollout-policy weakness or an
   engine bug; fix what is general, add a puzzle for each, and never a card-named rule.
6. Record the round in `docs/BOT_VERSIONS.md` and promote or discard.

Stop condition for a batch of rounds: two consecutive rounds that pass the regret suite but not the ladder, which
means the suite has stopped tracking strength and needs new roots from a stronger or different opponent (section 7 of
the plan is the way to get them, on the user's ask).

## Timeline and order

```text
S0 measure ──> S1 regret suite ──> S1.4 all groups, S1.5 line teacher ──> S4 set 5 ──> S5 planner Standard ──> S6 rounds
           ──> S2 puzzles ──────────────────────────────────────────────┘
           ──> S3 one live policy (alongside S1.4 and S1.5; before S4) ──┘
(S5.0, the engine cost of simulation, can run any time after S0 and must land before S5.1; S1.5 raises its stakes)
```

Elapsed, one developer with an agent: S0 one week including the ladder speedup; S1, S2 and S3 two to three weeks in
parallel; S4 and S5 three to four weeks; the first S6 round a week. About two months before Standard plans on both sides
with evidence. The first visible gain is earlier: S1's regret suite and S2's puzzles replace the 75-minute per-change
run as soon as they exist.
