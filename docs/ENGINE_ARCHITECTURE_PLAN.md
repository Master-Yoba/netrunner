# Engine and architecture improvement plan

Status: proposed, not started
Baseline: `feat/m5-8-final-gate` at `1b5a58f` (M5 complete, PR #30 open)
Companion plans: [AI opponent](AI_OPPONENT_PLAN.md) and
[UI/UX redesign](UI_UX_REDESIGN_PLAN.md)
Order: see the [improvement roadmap](IMPROVEMENT_ROADMAP.md)

## 1. Summary

The engine's foundations are sound and should be kept:

- **Atomic reducer:** each action resolves on a working copy and is committed
  only if it succeeds.
- **Serializable resolution:** frames are plain data, so any state can be saved
  and reloaded.
- **Single information boundary:** `ObservationBuilder` is the only way to see
  the game.
- **Declarative cards:** behavior specs live in `Netrunner.Cards`.
- **Verified replays:** games replay exactly and are checked by hash.

This plan does not change those contracts. It fixes the following:

1. Every `Apply` pays for full invariant validation and SHA-256 state hashing.
   That's fine for authoritative play, but it rules out search-based AI.
2. The 13.5K-line `StateInvariantValidator` is turning into a second rules
   engine that has to learn about specific cards.
3. Timing defects are found late, by heuristic self-play that only explores
   narrow lines.
4. Core writes prose that the player sees ("CR 6.9.3b: encounter paid
   window.") and emits events without enough attribution for the history and
   animation layers.
5. Several files are named after milestones, and Core's growth per card needs
   measuring before the card pool grows past System Gateway.

## 2. Measured baseline (25 September 2026)

All numbers come from the official `standard-smoke-v1` manifest: 20 games,
Release build, one worker, on the development Linux box.

| Measurement | Value |
|---|---|
| Wall time, 20 games | 172 s (about 8.6 s per game) |
| Accepted actions per game | about 930 (sample game: 933) |
| Share of decisions that are `PassPriority` | about 70% (sample: 625 of 933) |
| Same run with invariant validation skipped | 127 s (−26%), deterministic digest identical |

This `dotnet-trace` profile of the same run shows each component's share of the
benchmark's busy time. Shares overlap, so they don't sum to 100%.

| Component | Share |
|---|---:|
| `GameEngine.ApplyCore` (total, including validation) | ~29% |
| `CanonicalJson.Canonicalize` (hashing and fingerprints) | ~26% |
| `StateInvariantValidator.Collect` | ~24% |
| `OfflineAgent.Observe` and `ObservationReader.Read` | ~15% |
| `ReplayVerifier.Verify` | ~14% |
| `BenchmarkProgressDetector.Fingerprint` | ~11% |
| `EngineJson.DeserializeState` (serialization round-trip per action in the harness) | ~11% |
| `OfflineAgent.ChooseActionAsync` (all policy evaluation) | ~10% |

Serialization and hashing, not rules logic, dominate. The per-action fixed
costs are visible in `GameEngine.cs`:

- input validation at `ApplyCore` (`TryValidate`, around line 123)
- output validation after `working.Build` (around line 202)
- `DeterministicHash.ForState(next)` and `ForEvents(events)` for every accepted
  action (around line 210)
- the benchmark harness round-trips the state through JSON before each action
  (`AiBenchmarkRunner.cs:195`)

To reproduce, run `dotnet build src/Netrunner.Cli -c Release`, then run
`ai-benchmark data/m5/benchmarks/standard-smoke-v1.json --workers 1` with the
official environment variables used by `scripts/ai-benchmark.sh`. For the
comparison, short-circuit `StateInvariantValidator.Collect` locally; don't
commit that change.

## 3. Principles

- Authoritative play, saves, replays and gates keep full validation and
  hashing. The cheaper modes are opt-in and never reachable from the
  authoritative `MatchCoordinator` path.
- A cheaper mode must be proven equivalent by differential tests, not by
  reasoning.
- Information safety beats performance. A sampled or simulated state never
  leaves the agent that created it.
- Name code after the domain concept, not the milestone that introduced it.

## 4. Work breakdown

The phases can proceed in parallel except where a dependency is listed. The
sizes are relative: S is a few days, M is one to two weeks, L is several weeks.

### E1. Tiered `Apply` for simulation (M) — prerequisite for AI phases A3 and A4

Introduce an explicit reduction mode:

```csharp
public enum ApplyMode { Authoritative, Simulation }

public static ApplyResult Apply(GameState state, ActionSubmission submission,
    IBehaviorCatalog behavior, IRuntimeCardCatalog runtime, ApplyMode mode);
```

- `Authoritative` is today's behavior and stays the default for every existing
  overload.
- `Simulation` skips input and output invariant validation and does not compute
  `AcceptedActionRecord` hashes. Make the hashes lazy, or leave them null with
  an explicit `IsSimulated` flag so a simulated result can't be written to a
  replay or save.
- Add a guard: `EngineJson.Serialize`, `ReplayRecorder` and
  `SoloSaveStore` reject states produced in `Simulation` mode. Carry a
  non-serialized `Provenance` bit on the in-memory `GameState`.
- Profile `WorkingState.From` and `Build` next. Copying every list per action
  is the remaining cost once hashing and validation are gone. Consider
  structural sharing (immutable collections with builders) for zones and cards
  that an action doesn't touch.
- Harness: make the per-action serialization round-trip in
  `AiBenchmarkRunner` a configurable checkpoint interval. Keep the default at
  every action for gates, and use every N actions for exploratory runs.

Tests and gate:

- A differential test replays a sample of stored benchmark replays in both
  modes and asserts identical canonical states, events and decisions at every
  step. It runs on synthetic fixtures in CI and on the official corpus in
  `make m5-gate`.
- A micro-benchmark in `tools/` reports `Apply` throughput for a set of frozen
  mid-game positions in both modes.
- Target: Simulation mode is at least 5× faster than Authoritative for
  representative turn actions. Record p50 and p95 in the plan's progress doc.

### E2. Split the invariant validator (M)

The validator currently mixes three kinds of checks:

1. **Structural:** IDs are unique, every card is in exactly one zone, counters
   are non-negative, and there is one pending decision owner. These are cheap
   and catch corruption.
2. **Rules consistency:** run-stack topology, encounter cursors, subroutine
   ledgers, and memory limits against installed programs. They're expensive and
   duplicate engine logic.
3. **Card- or frame-specific exceptions.** The M5.8 DZMZ fix added an allowance
   for one controller/frame relationship. These make the validator a second
   rules engine.

Plan:

- Categorize every `Validate*` method into these three tiers and record the
  inventory in a table in this document.
- Run the structural tier on every authoritative `Apply` and the rules tier at
  checkpoints: save, load, the end of each turn, and in all tests, benchmarks
  and fuzzing. Keep a `NETRUNNER_FULL_INVARIANTS=1` switch that forces the
  rules tier on every action for debugging.
- For each card- or frame-specific exception, prefer changing the engine type
  so the illegal state can't be represented. Examples: typed frame-controller
  relationships, or a memory cleanup frame that owns its own precondition. If
  that isn't possible, move the check next to the behavior that creates the
  state, so it's reviewed with it.
- Gate: validator share under 5% of benchmark busy time, with no reduction in
  what fuzzing (E3) detects on a seeded corpus of known-bad states. Build that
  corpus from the existing regression tests that assert violations.

### E3. Random-legal rules fuzzing (S → M)

The DZMZ Optimizer and Retribution failure surfaced at game 999 of 1,000
because heuristic bots take narrow lines. Random legal play reaches odd
timing combinations much sooner.

- `RandomLegalAgent`: seeded, it picks uniformly among issued actions, with a
  configurable bias against `PassPriority` and `EndTurn` so games actually
  exercise cards. It lives in `Netrunner.AI` next to `FirstLegalAgent`.
- Mixed pairings: random vs random, random vs heuristic, and heuristic vs
  random, across all four official preset pairings.
- Every action runs in Authoritative mode with the full rules tier (E2), plus
  replay verification at game end.
- On failure, write the state immediately before the failing action plus the
  action as a scenario fixture under `artifacts/fuzz/`. Then run an automatic
  shrink pass that replays from earlier checkpoints with the same RNG stream,
  to find the shortest reproducing prefix.
- CI runs 50 games on synthetic content, and a nightly or local target
  (`make fuzz`) runs 5,000 or more games on official content.
- Gate: zero failures across 10,000 official fuzz games before any new card
  set is started.

### E4. Presentation-ready events and prompts (S + M) — E4a unblocks U1, E4b unblocks U3

Observed in play on 25 September 2026:

- The decision dock showed "CR 6.9.3b: encounter paid window." That string is
  written in Core (`GameEngine.Run.cs:737`, `YieldRunPriorityDecision`).
- History tags entries with the side whose action produced the events.
  `MatchHistoryProjector` receives an `actor`, so "Corp turn 2 begins" is
  tagged RUNNER because the Runner's end-of-turn action caused it.
- Generic history lines appear, such as "Corp: advance card." and "Corp: use
  trigger."

The work splits into two parts. E4a is small and unblocks the UI quick fixes
(U1). E4b is needed only when the event timeline (U3) starts.

**E4a. Prompt keys and subject side (S)**

- Replace free-text decision prompts with a typed `PromptKey` plus structured
  arguments (for example `RunPriority { Step, ServerId, IceId }`). Keep the rules
  citation as separate metadata for a developer overlay. `Netrunner.Match`
  owns the player-facing wording. Keep the existing text as a fallback during
  migration.
- Add a `SubjectSide` (or subject player) to every event where a side is
  meaningful. The history and animation layers group by subject, not by actor.

**E4b. Event completeness for animation (M)**

- Audit event completeness for animation. Every visible change should have its
  own event with enough payload to animate it, including:
  - credit gains and losses, with their source
  - click spends
  - advancement counters placed
  - rezzes, including cost payment
  - approach, encounter and pass for each piece of ICE
  - each subroutine broken or fired
  - accesses and their outcomes
  - damage per card
  - run success or failure

  Produce a table of event kind → present/missing → payload gaps. The typed
  event catalog in `TypedEvents.cs` is already large, so this is mostly gap
  filling.
- Keep viewer filtering in `Netrunner.Match` (`RunnerEventVisibility` and
  `CorpEventVisibility`). Add a test that every new event kind has an explicit
  visibility rule, so a new event can't leak a hidden identity by default.

### E5. Determinization API (M) — prerequisite for AI phases A3 and A4

Search needs to sample full game states that are consistent with what an agent
has observed. Only Core can build `GameState`, so the sampler belongs there:

```csharp
public static GameState ObservationSampler.Sample(
    Observation observation, SamplerKnowledge knowledge,
    OpponentCardPool pool, DeterministicRandom rng);
```

- Hidden zones (HQ, R&D, grip, stack, face-down installs and face-down
  Archives cards) are filled from `pool` minus every card the viewer has seen
  or can infer.
- `OpponentCardPool` is an explicit policy input:
  - `GatewayPool`: any card legal for the opponent's faction
  - `PublicDecklist`: the opponent's preset list
  - `Empty`: unknown cards are placeholders

  This keeps the fairness decision visible and testable.
- Key invariant, tested with property tests: `ObservationBuilder.Build(sample,
  viewer)` equals the input observation, apart from fields that are defined as
  sampled. That single check proves the sampler is consistent and doesn't leak
  anything.
- Samples are marked `Simulation` provenance (E1) and can't be serialized or
  published.
- The existing hidden-state twin tests in the tactical suite extend naturally.
  Two twins with identical observations must produce identically distributed
  samples for the same seed.

### E6. Structure, naming and growth tracking (S, ongoing)

- Rename milestone-named production files to domain names. Examples:
  `Slice45Presentation.cs`, `Slice46Presentation.cs`, `Slice47Presentation.cs`
  and `M2ScenarioSpec.cs` in `Netrunner.Match`. (The Godot `Main.Slice4x.cs`
  files are covered in the UI plan.)
- Track Core size per card. Add a generated report that counts, per card
  batch, the Core files and lines touched, the new `BehaviorInstructionKind`
  values and new frame types. Before starting a second card set, set a budget
  such as "at least 80% of the next pool needs no Core change". Treat misses
  as design reviews of the primitive vocabulary.
- Keep a short primitives reference (`docs/M4_PRIMITIVES.md` exists) generated
  or checked against `BehaviorCatalog`, so card authors can see what's
  available without reading Core.

## 5. Sequencing

```text
E1 tiered Apply ─────────┬─> AI A3 turn planner ──> AI A4 search
E5 determinization ──────┘
E3 fuzzing ──────────────┬─> E2 validator split (uses the E3 corpus)
                         └─> gate before next card set
E4a prompts/subject ─────> UI U1 quick fixes
E4b event completeness ──> UI U3 event timeline
E6 naming/growth ────────> continuous
```

Suggested order: E4a first (it unblocks visible UI fixes), then E1 together
with E3, then E5 and E4b, then E2. The cross-plan order is in the
[improvement roadmap](IMPROVEMENT_ROADMAP.md).

## 6. Verification for every phase

- `make verify` passes, and the official `make m5-gate` passes where content is
  touched.
- Deterministic digests of the frozen benchmark manifests are unchanged by
  E1, E2 and E6. E4a and E4b may change event payloads, so re-freeze the
  affected fixtures intentionally in a dedicated commit.
- Report performance numbers against the section 2 baseline with the same
  command line and hardware description.
