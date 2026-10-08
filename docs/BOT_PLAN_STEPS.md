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

  Runner v28 shipped on its own gates (#107: regret +0.0179 [+0.0113, +0.0245] over 398 discordant roots, +44 Elo
  [+11, +81] on the box, level on Gateway, self-flatlines within noise). The relabel with the hard-model teacher runs
  on `feat/bot-plan-relabel-v28` into `labels-model`; its re-read of the 272 discordant Corp roots decides the Corp's
  gate. A 30-minute probe at 128 samples moved the 119 agenda roots from −0.0021 to +0.0122 [−0.0160, +0.0401]: the right
  direction, not clear of zero at 119 roots. That is the case for S1.6.

  **The relabel (6 October; teacher v2, the record is in `docs/BOT_VERSIONS.md`).** 7 h 48 min; the S1.5 gate repeated
  for the new teacher: noise floor 61%; drift at the floor (the Corp's values fall by 0.126, the live Runner punishing
  what v7 did not); 14 of the 26 certified puzzles hold and 10 move to `teacher-v2`; the Runner's ordering is
  unchanged. **The Corp separates**: v30 over v28 on every root, and v30 over v29 over the 272 discordant roots
  (+0.0142 [0.0010, 0.0284]). The 119 agenda roots read +0.0185 [−0.0099, 0.0463]: the right way, inside zero, so the
  agenda rule's own reading waits for S1.6. Labels: `labels.jsonl.gz` is teacher v2, `labels-v1-lines` the S1.5 one.

**The horizon's leaf is a confound (S4.2c, 7 October).** Teacher v2 plays four turns and scores the horizon with set
4, which for the Gateway + Elevation pool is the pool's own fit: the same leaf the shipped planner and one of S4's arms
use. So its values partly reward agreeing with that leaf four turns on (S4.2c measured it: the pool's fit is the one
Corp arm the 4-turn reference lifts). Regret under teacher v2 is still comparable within its labelling, but it favours
a policy whose choices that leaf likes. Until a teacher v3 that plays to the game's end replaces it, the ladder, which
has no leaf in its result, is the unconfounded arbiter of planner gates. Teacher v3 replaces v2 only at a milestone,
and only if S4.2c's game's-end run shows it affordable and its reference reliable.

### S1.6 Targeted roots (S)

**After the hard-model relabel's re-read; before S4.2's gate. Independent of S4.1.** A paired comparison has power only
at the roots where the two versions choose differently. The suite's roots were drawn where the *heuristic's* top two
are close, which is the right place to find the heuristic's own mistakes but not where two particular versions differ:
v29 and v30 differ at 272 of 2,552 Corp roots, and the 119 that hold an agenda cannot clear zero whatever the teacher.
S4.2 and S5.3 compare planners whose choices diverge from the heuristic's far more often, and they need the same thing.

- `ai-regret diverge <games-dir> --versions A,B --output <suite-dir>`: replays every saved game, asks both versions
  (same profile) to choose at every turn-action and run-choice decision of their side, and keeps every decision where
  the chosen groups differ as a root in the suite's format, with `source: diverge A,B` and the two choices recorded.
  Roots are deduplicated by position and held out by deal, as the suite's are. The command labels nothing itself.
- The new roots are labelled with the suite's current teacher, settings and seed, a sample of them repeated for the
  noise floor, and merged with their provenance (`ai-regret merge`). A root that already exists keeps its labels.
- **Pruning, measured here.** S1.7 found that adaptive samples by early stopping do not pay on the suite's roots (they
  are drawn where the top two are close, so the rule almost always extends). S1.6 measures a pruning rule instead:
  `ai-regret label --decide 8 --record 24 --prune 3` gives every group 8 deciding samples, then 24 recording samples to
  the best three and to any group a scored version chose; a pruned group keeps its 8-sample value, marked, and is never
  a root's best. It is checked on a sample of the targeted roots against their flat 32-sample labels (cost, and the
  values and the pair's regret difference within noise). If it does not pay on targeted roots either, adaptive labelling
  leaves the plan.
- `--discordant` (S1.5's view) is the report for every version comparison from now on, beside the all-roots table.
- **First run:** the 960 box games for Corp v29 against v30 and v28 against v29, so the Corp's gate is read at every
  root where it can be. Expected: a few hundred more Corp roots, mostly agenda installs and holds.
- **Gate:**
  - Corp v30 against v29 and v28 separates over the discordant roots with the interval clear of zero and the right
    sign, on the hard-model labels; if it does not, the Corp's gate stays open and the record says what the agenda roots
    read at the new size;
  - the Runner comparisons of S1.3 still separate;
  - the labelling time and the roots added, per side and category, are recorded.
- **Record:** the counts, the time, and the Corp table before and after, in `docs/BOT_VERSIONS.md`.

**Outcome (7 October; the record is in `docs/BOT_VERSIONS.md`).** `diverge` found 2,073 roots for Corp v29 against v30
(turn decisions) and 499 for v28 against v29 (run choices); 2,393 new roots were labelled by teacher v2 with pruning in
2 h 59 min and merged (8,905 roots). Pruning pays on targeted roots (48% of flat's samples, the pair's comparison within
the intervals) and stays for them; it biases absolute regret low, which is quoted on the core only. **The Corp
separates** over the discordant roots for every pair (v29 − v30 +0.0180 [0.0120, 0.0243] over 2,198), and the agenda
roots on their own (+0.0241 [0.0141, 0.0340] over 1,201): S1.3's gate is closed. The Runner's comparisons hold.

**Later uses.** S4.2 runs `diverge` for the planner on set 5 against the planner on set 4 before its regret gate;
S5.3 for the new Standard against `standard-prev`; S6 for each round's candidate against the current Standard. In each
case the gate is read over the discordant roots, and the all-roots table is reported beside it.

### S1.7 Labelling cost (S)

**After the hard-model relabel; before S1.6's labelling run.** The plan's 5.1 sizes the suite so a relabel is an hour or
two: a versioned teacher, a core plus targeted roots, adaptive samples, and relabels scheduled overnight. The pieces:

- **Teacher version.** The hard-model labels (`labels-model`) are teacher v2: the line search (beam 5, one-turn screen on
  a quarter of the samples, two finalists), the live Standard opponent (`plan-model 1`), a 4-turn horizon, 32 samples.
  `ai-regret label` writes `teacher: v2` into every label; `score` refuses to compare two values at one root from
  different teachers, and prints the teacher beside every table. Nothing about teacher v2 changes before S4 fits.
- **The core.** `ai-regret core <suite-dir> --roots 2000 --seed S` marks a random subset of the suite's roots, drawn by
  deal so a held-out deal is wholly in or out, keeping the held-out share. The core's labels are the ones it already has:
  no run. Absolute regret, the noise floor and the drift are reported on the core; comparisons use every root both
  versions are labelled at, with `--discordant` beside them.
- **Adaptive samples.** `ai-regret label --decide 8 --record 8 --extend-to 32`: every group gets 8 *deciding* samples,
  used only to choose how many *recording* samples the root gets: 8 when the deciding samples put the best two groups
  more than two standard errors apart, otherwise steps of 8 until they separate on the recording samples or 32 is
  reached. The recorded values, standard errors and gaps come from the recording samples alone; the deciding samples
  are discarded. That is what keeps the labels free of optional-stopping bias: a root that stopped because its first
  samples happened to show a large gap would otherwise carry that inflated gap into absolute regret and the S4 and S6
  fitting targets. The label records both counts. Expected: 16 samples at a clearly decided root against 32 flat, so
  about a third to a half off the suite's cost, more on targeted roots, where the two choices usually differ clearly.
- **Schedule.** A core relabel (about 2,000 roots) or a targeted labelling runs overnight or while no ladder is due; the
  record names the date and the hours. A step that needs a relabel schedules it the evening before its gate.
- **Gate:** the core's S1.3 table (Runner v24 against v25 and v26; the Corp pairs over the discordant roots) reads the
  same as the whole suite's within the intervals. Adaptive labelling of 200 roots against their flat 32-sample labels,
  on the values and not only the chosen group: the mean signed difference of every group's value is within its
  standard error of zero (no lift either way), the mean absolute difference is reported, and the paired regret
  difference of one version pair computed on each labelling agrees within the intervals; the best group agrees within
  the noise floor. The costs recorded.
- **Record:** the core's size and seed, the adaptive labelling's cost and agreement, in `docs/BOT_VERSIONS.md`.

**Outcome (7 October; the record is in `docs/BOT_VERSIONS.md`).** Every label names its teacher (v1, v1-lines, v2; the
committed files are tagged) and `score` prints it and refuses a root mixing two. The core is 2,050 roots (seed 1) and
reads as the whole suite does within the intervals. Adaptive samples are unbiased on the values (mean signed difference
0.0000 over 1,695 groups) but **cost more than flat on the suite's roots** (1,480 s against about 860 s for 200 roots:
181 of them extend to 32, since a root's best two of about nine groups are rarely apart after 8 samples), so a core
relabel runs flat at 32 and adaptive samples wait for S1.6's targeted roots, where their saving is measured first. One
finding for the freeze: the teacher plays the live Hard policies, so a version name is pinned to them (v2 is Corp v31
and Runner v28); a policy bump makes new labels a new version by name.


### S1.8 Teacher v3: the game's end (S, plus relabels)

**From 8 October; before any gate is read again.** Teacher v3 is teacher v2's search played to the game's end and
scored by the result: the line search (beam 5, one-turn screen on a quarter of the samples, two finalists), the live
Standard opponent, 32 samples, no leaf. The reason is in plan 5.1: two gates where the v2 suite and the ladder
disagreed, both from v2's leaf at its horizon.

- **The core** already has v3 labels (S4.2c's relabel, 5 hours); they become `labels.jsonl.gz` for the core, with the
  v2 labels kept as `labels-v2*`. `bot-check`'s regret line reads the core's v3 labels and names them.
- **Targeted roots** are relabelled at v3 when a comparison needs them (about 1.8 times a v2 labelling: the S1.6
  Corp roots first, since the Corp's separation gate was read on v2). Until then they keep their v2 labels, marked;
  `score` never mixes teachers at a root, and a comparison is read on the roots labelled by one teacher.
- **The 26 certified puzzles** are re-certified at v3 (128 samples, the game's end); failures move to a `teacher-v2`
  split kept out of the gate, and the stable set (converted tests and loop findings) is unchanged.
- **The noise floor** is measured again on the core at v3 (the S1 repeat's roots), with the flip cost.
- **Gate:** the S1.3 table on v3 labels: the Runner's ordering holds (v24 against v25 and v26, v28 over v27), the
  Corp's holds where its targeted roots have been relabelled, and the Corp's 3×3 planner reads no worse than the
  heuristic on v3, as the ladder has it; if it still reads worse, the disagreement is recorded and the ladder stays
  the arbiter for that comparison.
- **Record:** the label files, the puzzle recertification, the noise floor, and the table, under S1.5's confound note,
  which closes with this step.

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
  [BOT_LOOP_WORKFLOW.md](history/BOT_LOOP_WORKFLOW.md) section 7 to point there.

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

**Outcome (7 October; the record is in `docs/BOT_VERSIONS.md`).** Set 5 is set 4 and 12 terms, each from the observer's
view with its knowledge (`Extract(…, 5, knowledge)`): exposed agenda points, HQ and R&D agenda density, advancements
left and whether that server is open, punisher exposure and grip margin, the central tax (HQ and R&D), money after
reserve and income cards, and the score race's interaction (its other terms are set 1's). Unit and twin tests pass, and
the terms vary on real positions (the punisher term is rare in self-play). No fit yet.

### S4.2 Fitting to the teacher

- `ai-fit-evaluation --target teacher --suite <suite-dir> --feature-set 5 --outcome-weight 0.2 --output <doc>`: fits
  one model per side to the teacher's root value (primary) and the game's outcome (auxiliary), on the core's training
  deals (one teacher throughout, S1.7), reports held-out log loss and mean absolute error against the teacher's value,
  and compares to set 4 scored the same way. The model is `LogisticSideModel` or, if the held-out error asks for it, a two-layer additive model over the
  same named features; either is stored in `data/ai/evaluation-v5.json` with provenance.
- Fit **three** models, so the gain is attributed: set 4 with its shipped weights (set 2's, never fitted to set 4's own
  features), set 4 refitted to the teacher (new target, old features), and set 5 fitted to the teacher (new target, new
  features).
- **Gate:** set 5 beats both set 4 arms on held-out teacher values in both seats, and the planner (Hard settings)
  scoring with set 5 beats the same planner on each set 4 arm on the regret suite: over the discordant roots of each
  pair (`diverge`, labelled adaptively, S1.6 and S1.7) with the core's held-out roots reported beside. If the refitted
  set 4 captures most of the gain, the features are not the lever and S4.1's terms are reconsidered before S4.3.
- **Record:** the three fit reports and the regret comparison.

**Outcome (7 October; the record is in `docs/BOT_VERSIONS.md`).** Not met on the root-value fit. Set 5 beats both set 4
arms on held-out teacher values in both seats, but the planner at Hard's settings does not separate from either set 4
arm on the discordant roots or the core, and the box ladder is level. The measurement has two confounds, so this is not
a verdict on the features and S4.1 is not revisited: the planner's value adds the rollout's log-odds to the heuristic's
score (so the leaf moves only part of a choice), and the leaf was fitted at roots while the planner reads it where the
turn ends. S4.2b removes both.

### S4.2b Fitting where the planner reads the leaf

- **S5.1's value scoring, brought forward** behind `plan-value 1`: a candidate's value is the mean over the samples of
  (the leaf as a win probability − the sample's root value); the heuristic's score is not part of it, and the
  heuristic's order breaks ties within `plan-tie` (thousandths, default 5). `diverge` and the discordant regret
  comparison for set 5 against set 4 (shipped) with `plan-value 1` on both, recorded.
- **End-of-line rows:** `ai-regret label --end-lines FILE` records, for every line played to the horizon, the
  end-of-turn state's sets 4 and 5 (from the root side's view, with its knowledge) and the play-out's value from it. The
  core is relabelled flat with recording on; the labels must equal the core's.
- `ai-fit-evaluation --target end-lines` refits the three arms on those rows, held out by deal (intervals resample
  roots), and the gate of S4.2 is read again under `plan-value 1`.

### S4.3 Budget scaling on set 5

**With `plan-value 1`** (S4.2b): ×1, ×4 and ×8 on the box, about 400 deals a pairing. Monotone and clear starts S5;
flat sends the plan back to the end-of-line fit.

- Re-run S0.3's entrants with `plan-evaluation 5`.
- **Gate:** monotone in both seats with each step's lower bound above zero. If not, the leaf still lacks a term the
  rollouts need; use the blunder list to find it, add it, refit, and repeat. S5 does not start until this holds.

**Outcome of S4.2b and S4.3 (7 October; the record is in `docs/BOT_VERSIONS.md`).** The core's end-of-line rows fit
set 5 better than both set 4 arms for the Corp, and better than the refit but not the shipped weights for the Runner.
Under `plan-value 1` neither the teacher-fitted nor the end-of-line set 5 separates from set 4 on the discordant roots,
and S4.3 on the end-of-line set 5 is flat in both seats (no step clear). S5 does not start. The reason is measurable on
the recorded rows: the leaves barely rank end states of one root (S4.2c below), so neither the planner's width nor its
samples could have moved anything. The plan goes to S4.2c, the leaf fitted to rank.

### S4.2c The leaf fitted to rank

**Before S4.3 is re-run; no feature or planner change.** On the held-out end-of-line rows of the core, pairs of distinct
end states of one root whose play-out values differ by more than 0.1 (up to 50 a root; the pairs span samples, since
the rows carry no sample id, so part of each gap is sample noise):

| Seat | Pairs | Set 5, end-of-line fit | Set 4, refit | Set 4, shipped |
|---|---:|---:|---:|---:|
| Corp | 4,166 | 60.4% | 56.4% | 55.3% |
| Runner | 11,711 | 64.0% | 63.5% | 62.9% |

Chance is 50%. S4.2's log-loss gains came from the level across roots, not from the order within one, and the planner
only ever compares within one. The fit's target and its gate change accordingly (plan 4.2):

- **Recording:** the end-of-line rows carry `sample` and `line` ids, so pairs are formed on common random numbers and
  a line's value over all samples can be recovered. No extra play-outs are needed for the gate: the **reference order**
  of two lines at a root is the sign of the difference of their mean values over every sample, and its reliability is
  measured from the same samples by split halves (how often the order on 16 samples matches the order on the other
  16). A second independent play-out, where one was recorded, is reported as repeatability only: the agreement of two
  noisy reports is not a ceiling on a model's accuracy against the latent order (two reports each right 80% of the
  time agree 68% of the time).
- **The fit:** `ai-fit-evaluation --target pairs`. Examples are within-root pairs of distinct end states on the same
  sample; the target is the sign of the value difference, weighted by its size; the model is linear on the feature
  difference with no bias (`P(A better) = sigmoid(w · (f_A − f_B))`), then calibrated with a one-dimensional fit of
  bias and slope on the play-out values so the planner's delta from the root keeps a win-probability scale. Fitted on
  the core's rows, held out by deal; the three arms as before, plus the current value-fitted set 5 as a fourth.
- **Fit gate:** within-root pairwise accuracy on held-out pairs against the reference order, counted where the
  reference difference exceeds twice its standard error, gap-weighted, per seat: above the table's baseline (re-read
  against the same reference) with the interval clear. Split-half repeatability of the reference and log loss are
  reported beside it, not gated.
- **If the linear ranker stays under about 70%** while the reference is repeatable well above it, the trade-offs are
  not linear in the differences. Then one small nonlinear model, and it must still be a **scalar scorer** the planner
  calls one state at a time: boosted scalar trees `g(features)` (depth 3, about 200 trees) trained with the pairwise
  gradient (RankNet-style: the loss is on `g(f_A) − g(f_B)`, the model is on one state), deterministic, exported to
  JSON, its inference cost recorded against set 5's 93 µs. A tree over the difference vector is not a leaf: it cannot
  be split into per-state scores and can answer a reversed pair inconsistently. No other model family.
- **Then** the planner gate of S4.2 under `plan-value 1`, and S4.3 again. If the ranking accuracy moves and the
  planner's choices still do not, the next suspect is the planner's own continuation (`plan-lines`, S1.5's machinery),
  not the leaf.
- **Fallback to keep in view, not to build now:** Standard as the teacher's screen, one opponent turn played out
  before the leaf, so the leaf scores a state where exposure has been punished or not. About four times a one-step
  rollout: affordable for Hard, probably not for Standard before S5.0.
- **Record:** the reference's repeatability, the four arms' pairwise accuracy with intervals, the calibration, and
  the planner gate.

**First reading and the confound (7 October; the record is in `docs/BOT_VERSIONS.md`).** Against the 4-turn reference
the linear rank fit is not above the baseline in either seat, and the pool's fit (the shipped set 4, and the teacher's
horizon leaf) is the one Corp arm the reference lifts. So, before any trees:

- **A second reference with no leaf in it:** the core relabelled to the game's end (`--horizon game`, 32 samples,
  end-of-line recording). Its split-half repeatability and the pairs it counts are recorded beside the 4-turn
  reference's; fewer pairs is the price of no leaf.
- **Every arm read against both references** (the general fit, the pool's fit, sets 4 and 5 value-fitted, the rank
  fit), by the mean leaf over the samples (the planner's view, the gate) and on per-sample state pairs (beside it);
  `game` rows in the line means and out of the counted pairs. The difference between the two readings, arm by arm, is
  the confound's size. If the pool's Corp lead vanishes on the game's-end reference, the 4-turn reference rewarded
  agreement with itself; if it holds, the pool's fit is better on this pool.
- **Gate:** the planner's view against the game's-end reference, above the best value arm with the interval clear.
- **Then:** if the Corp's linear arms all sit near the mid-60s on the clean reference too, the Corp leaf is
  feature-limited in linear form: the boosted scalar trees, fitted on the game's-end rows. The Runner's rank fit goes
  forward.
- **Calibration:** a ranker ships only calibrated by a monotone one-dimensional fit over all rows whose log loss is no
  worse than the value fit's; failing that, the planner keeps its tie threshold and static mix on the value fit's scale.

**Outcome (8 October; the record is in `docs/BOT_VERSIONS.md`).** The game's-end relabel took 5 hours (1.8 times the
4-turn run) and no play-out reached the cap; its reference counts fewer pairs (3,566 Corp, 3,097 Runner) as repeatably
(98% split halves). It measures the confound: the pool's fit loses its lead without its own horizon (the Runner's
reverses, −0.065 against the general fit), and every fit to the 4-turn values falls with it. Against it no arm passes
the gate. The Corp's arms, linear or boosted, value- or rank-fitted, sit between 0.59 and 0.65 with nothing separated;
the Runner's rank fit is clear of the value fits but level with the general fit (0.731 against 0.726). The boosted
trees (2.2 µs a state) overfit a weak per-sample signal and read no better. No ranker's monotone calibration matches the
value fit's log loss, so none reaches the planner. The limit is what the end-of-turn state says about the game; the
next lever is the fallback kept in view above (one opponent turn before the leaf), a decision for the plan. A teacher
v3 to the game's end is affordable (5 hours for the core) and its reference reliable where it counts.

**Decision (8 October).** S4's premise is falsified at the turn boundary (plan 4.2's outcome note). No further leaf
fit. The plan splits: Hard becomes the teacher at a budget it can afford (plan 4.6), Standard takes an apprentice
ranker at the root if it beats the heuristic on the suite's own metric (plan 4.7). Both are measured before S5 commits
to either: S4.4 and S4.5 below. The user accepted Hard's turn-action budget of about one second (plan 4.5).

### S4.4 Horizon × samples: what ranks, at what cost (S)

**From data on disk; one extra 45-minute run. Decides Hard's cell before S5.** Both labellings of the core exist with
every line on every sample: 4 turns plus the leaf, and the game's end. So the planner's-view ranking accuracy of
"k samples of horizon h", against the game's-end reference built from all 32 samples, is computable by subsampling the
recorded rows, for k in 4, 8, 16, 32 (several draws each, averaged) and h in:

- turn end plus the leaf (the arms already read: the baseline row);
- one opponent turn plus the leaf (one new recording run on the core, `--horizon 1`, about 45 minutes; the horizon the
  teacher's screen used when it found the Petty Cash line);
- 4 turns plus the leaf;
- the game's end (k samples of the result itself; at k = 32 this is the reference's own split-half figure).

Beside each cell, its cost from S0.1 and S0.4: steps per play-out at that horizon times the per-step cost, times k,
times the lines a width-5 beam plays at a turn-action root (the teacher's count), on the reference box.

- **Gate (a reading, not a pass):** the table, per seat, with the cheapest cell above 80% in the planner's view marked,
  and its wall time per turn-action decision. A cell's k samples are drawn from one half of the recorded samples and
  its reference is built from the other half, so no cell shares samples with what it is scored against; the design
  that reuses all 32 for both reads higher and is reported only as such. If no cell under one second reaches 80%,
  mark the best that fits and record its accuracy; S5.0's expected cuts are applied to the costs as a second column.
- **Record:** the table and the chosen cell in `docs/BOT_VERSIONS.md`; the S4.3 flat result re-read against it (Hard
  at 3 to 4 samples of the turn-end leaf is the table's weakest cell, which is why more of it changed nothing).

**Outcome (8 October; the record is in `docs/BOT_VERSIONS.md`).** Against the game's-end reference, the Corp's
accuracy is set by the horizon, not the samples: the turn's end reads about 0.60 at any k, one opponent turn about
0.67, and 4 turns or the game's end pass 80% (4 turns at k = 16, or the game's end at k = 8, with k drawn from one half
of the samples and the reference from the other; 4 turns at k = 4 when both use all 32). The Runner's candidates are
mostly noise to every horizon (0.63 to 0.74 on the unbiased design, the game's end's own split-half 0.711). No cell
fits one second at the teacher's count (13 Corp and 10 Runner candidate groups, 5 lines): one sample of one candidate
costs about 78 ms at 4 turns. Hard's cell as read: the Corp plays out 4 turns with as many samples as S5.1's beam
leaves (about 3 at width 4 before S5.0); the Runner stops at the turn's end, since no horizon ranks better. The
opponent's horizon was run as `--horizon opponent` (the next turn of the root side), since `--horizon 1` counts rounds.

### S4.5 The apprentice at the root (M)

**From the regret suite as it stands; independent of S4.4. Decides Standard's decider before S5.** A pairwise ranker
over the meaningful candidate groups of a root, trained on the teacher's values (plan 4.7).

- **Rows:** every labelled root of the suite (core and targeted; `game` and pruned groups included at their values,
  marked), one example per pair of groups at a root, weighted by the value gap; features per plan 4.7. Held out by
  deal, with the core's held-out split kept as the primary read.
- **Model:** a scalar scorer `g(root, candidate)`, linear first, then boosted scalar trees with the pairwise gradient
  if the linear one is clearly under the teacher's order on held-out pairs; exported to JSON; inference cost recorded
  (target: under 50 µs a root, every candidate included).
- **Use:** `apprentice 1` as a profile parameter on the live heuristic: within the heuristic's top priority band, the
  candidates are re-ordered by `g`; priorities and vetoes are untouched. Off by default until the gate.
- **Gate:** `ai-regret score` on held-out roots, the heuristic with the apprentice against the heuristic alone, paired,
  per seat: regret lower with the interval clear. Then `diverge` on the pair and the discordant-root reading; then a
  400-deal box ladder against `standard-prev` and 240 on Gateway; puzzles and lints not worse; `PolicyPerformanceTests`
  within Standard's budgets. The budget-scaling ladder does not apply (no search).
- **If it lands level with the heuristic** in a seat, Standard stays the heuristic in that seat and the record says
  where the apprentice's order differed from the teacher's most (its own blunder list), which feeds S6's heuristic
  fixes instead.
- **Record:** held-out pairwise accuracy against the teacher's order, the regret table, the ladders, the cost.

**Outcome (8 October; the record is in `docs/BOT_VERSIONS.md`).** Not met; Standard stays the heuristic in both seats.
Boosted trees order the teacher's choices about 6.5 points better than the heuristic on held-out pairs in both seats,
and regret is lower on every held-out root with the interval clear (Corp −0.0077, Runner −0.0034; the core's held-out
roots lean the same way without clearing). In play the Runner's apprentice banks credits and loses (−248 Elo on the
box, −371 on Gateway against the heuristic, 8% of games won); the Corp's is level (+24 and −12). The teacher's 4-turn
play-out ending on the pool's fit rates a banked credit level with a draw, a run or an install, and the apprentice
learned that order. Standard with its Corp planner off is below live Standard on both pools, so the 3×3 planner stays.

**Decision (8 October).** The apprentice's failure and the Corp planner's reading are the same fault: teacher v2's
leaf at its horizon. Teacher v3 (S1.8) replaces it; the apprentice is refitted on v3 (S4.5b); the Runner's run choices
are measured before Runner Hard spends anything on them (S4.6).

### S4.5b The apprentice on teacher v3 (S)

**After S1.8's core is in place; a 15-minute fit and two ladders.** The same model, rows and use as S4.5, fitted on the
core's game's-end labels only (the targeted roots are relabelled at v3 as S1.8 reaches them, and the fit is redone on
the larger set then).

- **Diagnostic first:** at the core's roots, the share where teacher v2 ranks "gain a credit" best against the share
  where teacher v3 does, per seat. This is the bias the Runner's apprentice learned, stated as a number.
- **Gate:** as S4.5's, read on v3 labels: regret below the heuristic alone on the core's held-out roots with the
  interval clear in a seat, then the three-entrant ladders (`standard-heuristic`, `standard-apprentice`, `standard`),
  400 deals on the box and 240 on Gateway, puzzles and lints not worse, the performance tests within budget. A seat
  passes only with the ladder; the suite alone promotes nothing (plan section 9).
- **If the Runner's apprentice still banks credits** on v3 labels, the fault is in the rows, not the teacher: the
  candidate features cannot express tempo, and the record says which terms were tried before the apprentice is set
  aside for that seat.
- **Record:** the diagnostic, the regret table, the ladders, the cost.

### S4.6 Run choices: the Runner's own curve (S)

**Before Runner Hard spends on anything; one recording run on the core's run-choice roots, about an hour.** S4.4 says
no horizon ranks the Runner's turn lines above the turn-end fit. The Runner's decisions that matter are inside runs:
continue or jack out, trash or not, and which server to run is a turn action whose value is the run's. The same
measurement as S4.4 on the suite's run-choice roots: play-outs from each choice to the run's end plus `h` turns, or
the game's end, at k samples, scored in the planner's view against a game's-end reference; costs beside.

- **Gate (a reading):** the cheapest cell above 80% inside 200 ms (a run choice is a nested decision, not a turn
  action); if none fits, the best that does, with its accuracy. If no cell beats the heuristic's own choice at these
  roots (the heuristic's regret on them under v3), Runner Hard keeps Standard's run choices too, and the record says so.
- **Record:** the table and the cell in `docs/BOT_VERSIONS.md`; S5.2's Runner Hard profile follows it.

## S5. Hard plays out; Standard takes the apprentice (L)

**Rewritten 8 October after S4.2c, S4.4 and S4.5.** S5 no longer makes a leaf-scored planner the Standard. Corp Hard
becomes the play-out planner over turn lines at the cell S4.4 read (plan 4.6), inside the one-second turn-action
budget; Runner Hard keeps Standard's decider and adds run-choice play-outs only where S4.6 shows they rank. Standard
becomes the heuristic with the apprentice ranker where S4.5b's gate passes (plan 4.7), otherwise the heuristic, with
the Corp's 3×3 planner kept on the ladder's evidence. S5.0 and S5.1 serve Corp Hard; S5.2 and S5.3 cover every tier.
Nothing in S5 is read on teacher v2 labels: S1.8 comes first.

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
- **Probability-space values:** each play-out's value as a win probability (the result when the horizon is the game's
  end, otherwise the general fit at S4.4's horizon); candidate value = the mean over samples of (value − root value),
  as `plan-value 1` already does. No static mix at the turn's end: S4.2c showed it carries no ranking signal. The
  heuristic orders candidates and breaks ties within `plan-tie` (start 0.005).
- **Horizon:** `plan-horizon` turns past the root side's turn, from S4.4's cell; the rollout policy plays both sides
  through it. The lines' play-outs share samples across every line of the root.
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

- Standard: the live heuristic with `apprentice 1` where S4.5b's gate passed, pool prior, full tracker; the Corp keeps
  its 3×3 planner. Corp Hard: the play-out planner over turn lines at S4.4's cell (`plan-lines 1`, `plan-value 1`,
  `plan-horizon 4`, `plan-samples k` as S5.0 allows), decklist, full tracker, modelling the opponent by the live
  Standard. Runner Hard: Standard's decider with the decklist, plus run-choice play-outs at S4.6's cell if one ranks.
  Easy: heuristic, forgetful tracker (unchanged). Personalities shift the heuristic's named terms as today.
- `PolicyPerformanceTests` on the reference box: Standard within 50 ms / 200 ms p95; Hard's turn-action decisions
  within about 1 s p95 (the budget accepted on 8 October, recorded in the test), every other decision within 200 ms.
- Both sides' versions bump (`corp-heuristic-v32`, `runner-heuristic-v29`; v28 is the remote valuation of the S1.5
  follow-up) because the default profile's decider changed.
- **Hard's opponent model is an open decision.** Hard today models the opponent by the pinned versions (Runner v7, Corp
  v8); Wave 5 found that modelling the live Runner gained Hard nothing (`hard-model` level on the ladder), but that was
  with the one-step planner. S5 re-tests it with the line search, and Standard and Hard model the live Standard
  heuristic unless the ladder says otherwise, rather than carrying the pinned versions forward by default.

### S5.3 Gates

- Regret suite: Standard (with the apprentice) below the heuristic alone, and Hard below Standard, each with the
  interval clear, read on the core's held-out roots and over the discordant roots of each pair.
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

1. Play 960 box games of the current Standard against itself with `--save-games`; refresh a fifth of the core from
   them (S1.1, held out by deal) and add the candidate's discordant roots against the current Standard (`diverge`).
2. Re-label the **core and the discordant roots** with the teacher at the current Standard's leaf (a new teacher
   version, S1.7), adaptively, overnight: about two hours, not seven. Report teacher drift on the core: the share of
   roots whose best candidate changed, against the noise floor. Over 15% above the floor stops the round for inspection
   of the top changes. The rest of the suite keeps its older labels; a whole-suite relabel is a milestone event.
3. Refit the apprentice (S4.5) on the round's labels, a new document version; the Runner's general fit is refitted
   only if a round's blunder list points at the horizon's leaf.
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
S0 measure ──> S1 regret suite ──> S1.4 all groups, S1.5 line teacher, S1.6 targeted roots ──> S4.1–S4.2c (the leaf: falsified)
           ──> S2 puzzles ──────────────────────────────────────────────────────────────────┘        │
           ──> S3 one live policy (alongside S1.4 to S1.6; before S4) ───────────────────────┘        ▼
                                       S4.4 horizon × samples (Corp Hard's cell: 4 turns) ─┐
                                       S4.5 apprentice (not met on teacher v2) ──> S1.8 teacher v3 ──> S4.5b apprentice on v3 ──┼──> S5 ──> S6 rounds
                                       S4.6 run choices (Runner Hard) ───────────────────────────────────────────────────────┘
(S5.0, the engine cost of simulation, can run any time after S0 and must land before S5.1; S1.5 raises its stakes)
```

Elapsed, one developer with an agent: S0 one week including the ladder speedup; S1, S2 and S3 two to three weeks in
parallel; S4 and S5 three to four weeks; the first S6 round a week. About two months before Standard plans on both sides
with evidence. The first visible gain is earlier: S1's regret suite and S2's puzzles replace the 75-minute per-change
run as soon as they exist.
