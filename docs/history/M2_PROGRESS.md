# M2 implementation progress

Last updated: 2026-08-30

Status: M2 first-pass exit gate and second-pass M3-readiness corrections are
implemented and verified in the working tree.
The second-pass section at the end is the current implementation record; the
first-pass and U1–U9 entries below are retained as historical review history.

Re-work boundary: the U1–U9 checkpoint commits `43a68b5` (U1) through
`9613bf0` (U9) on `qwen_cont` (per-unit log below); the closing checkpoint
commit finalizes this boundary

Branch: `qwen_cont`

## Milestone goal

M2 delivers a deterministic, headless representative Netrunner run using the
Null Signal Comprehensive Rules 26.03 timing model. The complete acceptance
contract is in [M2_ARCHITECTURE.md](M2_ARCHITECTURE.md).

## Completed and accepted

### Architecture and scope

- Defined the four dependency-ordered M2 implementation slices.
- Recorded the authoritative run timing, effect/checkpoint, access,
  observation, persistence, and exit-gate contracts.
- Selected the representative System Gateway cards: Palisade, Whitespace,
  Cleaver, Buzzsaw, Overclock, Offworld Office, and Nico Campaign.
- Kept official card data as local ignored input; it is not redistributed by
  this repository.

### Slice 1: run timing skeleton

- Added optional, serializable `RunState` and its invariants.
- Added the CR 6.9 run phases and timing steps.
- Added alternating paid-window priority and consecutive-pass handling.
- Added approached-ICE rez opportunity without a separate decline-rez action.
- Added approach, encounter boundary, movement, jack-out, success, placeholder
  breach boundary, run-end cleanup, and typed timing events.
- Preserved save/load at each run prompt and M1 no-run serialization behavior.
- The focused run timing suite passed before moving to Slice 2.

### Slice 2A: costs and reusable rule effects

- Added atomic credit/click costs and ordered effect bundles.
- Added deterministic net, meat, and core damage plus flatline victory.
- Added tags, memory state, installed program memory costs, mandatory memory
  cleanup, card movement, named counters, and virus purge.
- Added encounter/run/turn lingering-effect expiry.
- Added typed events and deep state/frame invariants for these primitives.
- The focused rule-effect suite passed before moving to Slice 2B.

### Slice 2B: checkpoint vertical

- Added serializable imminent/resolved occurrence snapshots.
- Added typed trigger and interrupt registrations and materialized invocations.
- Added effect checkpoints, persisted instruction cursors, and nested child
  effect resolution.
- Added mandatory and optional trigger handling.
- Added same-controller trigger-order choices.
- Added partial/full damage prevention and replacement effects.
- Added source definition/zone snapshots and trigger/interrupt recursion
  suppression fields.
- Added exact checkpoint legal-action validation.
- Changed cross-controller trigger materialization to start with the active
  player, following CR 9.2.8b, instead of sorting priority by identifier text.
- Completed the adversarial checkpoint test vertical: 17 `CheckpointTests`
  covering simultaneous same-controller ordering, active-player precedence,
  replacement-instead-of-original, source-snapshot survival across a source
  move, nested save/load at every yielded decision, identical
  state/event-trace/hashes after mid-scenario reload, tampered/stale/forged
  action rejection with byte-identical state, self-trigger recursion
  suppression, and the no-registration fast path.
- Fixed per-occurrence suppression (CR 9.2.8a, 9.2.8e, 9.6.2, 9.6.11): a
  declined trigger or interrupt is a per-occurrence window decision, not a
  global waiver. Child effect frames now inherit only the parent effect
  frame's `SuppressedTriggers`/`SuppressedInterrupts` plus the resolving
  registration id, so a new occurrence re-offers previously declined
  registrations. Two regression tests fail without the fix.

### Slice 3 sub-slice 3.1: card-behavior specs and real ICE encounter data

- Added typed, compiled, versioned card-behavior specifications to Core
  (`CardBehaviorSpecs.cs`): `IceBehaviorSpec` (base strength, printed rez cost,
  printed ordered subroutines, strength modifiers), subroutine spec records
  (`EndRunSubroutineSpec`, `LoseCreditsSubroutineSpec`,
  `EndRunIfCreditsAtMostSubroutineSpec`), `IceStrengthModifierSpec` with the
  `ProtectingRemoteServer` condition, and the fail-closed
  `CardBehaviorSpecCatalog` (version `gateway-m2-1`; idempotent structural
  registration, conflicting duplicates rejected; `TryGetIce`/`GetIce`).
- Populated the specs from the Cards layer (`GatewayCardBehaviorSpecs.cs`):
  typed `GatewayCardCodes` for all seven representative cards and compiled
  registrations for Palisade (30072: strength 2, rez 3, [end the run], +2
  strength while protecting a remote server) and Whitespace (30074: strength 0,
  rez 2, [Runner loses 3 credits; end the run if the Runner has 6 or less]).
  No rules text is parsed or executed at runtime.
- Encounter initialization (CR 6.9.3a) materializes ordered
  `RunSubroutineState` instances from the spec: deterministic
  `subroutine:{encounterId}:{index}` keys, ordinals in printed order,
  unbroken/unresolved. Unregistered ICE fails closed with a descriptive
  `ResolutionFailureException`; `EncounterInitialized` now carries the real
  subroutine count. Subroutine effects are not yet resolved (sub-slice 3.2).
- Approach-window rez (CR 6.9.2b) charges the Corp the printed rez cost inside
  the atomic transaction via the existing credit primitive (`CreditsAdjusted`
  emitted before `IceRezzed`). `RezIce` is materialized only when the Corp can
  afford the printed cost; the validator's expected-run-action logic mirrors
  the same condition; an unrezzed ICE that is not rezzed is passed without an
  encounter.
- Core.Tests fixture ICE specs (`fixture:ice-plain`,
  `fixture:ice-two-subroutines`, `fixture:ice-rez-cost`) are registered at test
  assembly load via a module initializer; run-timing fixtures no longer use
  unregistered ICE definitions.
- Thirteen focused tests added (6 Cards spec-binding, 4 encounter-subroutine,
  3 rez-cost), each reviewed and accepted.

### Slice 3 sub-slice 3.2: strength, break, subroutine resolution

- W1: typed `IcebreakerBehaviorSpec` (base strength, interface `IceType`,
  break cost, break limit, pump cost, pump strength) in Core plus
  fail-closed catalog lookup (`TryGetBreaker`/`GetBreaker`); Cards-layer
  registrations for Cleaver (30006) and Buzzsaw (30005). The W1 enum
  `IcebreakerInterfaceType` was renamed `IceType` during W3 so the ICE spec
  and breaker specs share one type.
- W2: effective strength model (`EncounterStrength.cs`): ICE base strength +
  conditional modifiers (Palisade +2 while protecting the run's remote
  server), breaker base strength + encounter-scoped pumps. `ServerState`
  gained the optional `AttachedBreakerId` field following the M1
  byte-compatibility rules (`[JsonInclude]` + `JsonIgnore(WhenWritingNull)`,
  no `[JsonRequired]`). `AttachBreaker` action at the encounter paid window
  (`BreakerAttached` event) charges the interface cost and is offered only
  when the interface matches and the Runner can pay; the validator mirrors
  the same condition.
- W3a: `ActionKind.BreakSubroutines` + `RunBreakParameters`
  (discriminator "run-break") and the `SubroutinesBroken` event. Breaking is
  gated on interface match, effective strength at least the ICE strength,
  and credits at least break cost times quantity, with quantity bounded by
  min(break limit, unbroken count); the first n unbroken subroutines in
  ordinal order are marked `Broken`. Legal quantities are materialized one
  action each.
- W3b: `ActionKind.PumpBreaker` (reuses `RunBreakerParameters`) and the
  `BreakerPumped` event. Pumping is encounter-scoped lingering strength
  (`RunLingeringEffect` with `IcebreakerPumpBehavior`, `Encounter` expiry)
  with a deterministic effect id; it is offered whenever the attached
  breaker is registered and the Runner can pay, not gated on strength or
  interface match, so a weak or mismatched-ICE breaker can pump toward
  strength.
- W4: encounter subroutine resolution (CR 6.5/9.8). At encounter close every
  unbroken subroutine resolves in printed order (credit loss clamped to the
  target's available credits; end-the-run stops the remaining sequence and
  ends the run with `RunEndReason.EndedByRule`); broken subroutines are
  skipped and stay unresolved; executed subroutines are marked `Resolved`;
  encounter-scoped lingering effects expire at encounter completion; run-end
  cleanup detaches every attached breaker (after jack out or an end-the-run
  subroutine).
- 42 new focused tests (36 Core: encounter strength, breaker attachment,
  break, pump, subroutine resolution; 6 Cards: breaker spec binding).

### Slice 3 sub-slice 3.3: breach and access

- W1: additive run-access data layer. New `ActionKind`s `ChooseBreachCandidate`
  (`BreachCandidateParameters`: a specific candidate card id or a hidden-zone
  source, cross-validated), `TrashAccessedCard`, and `NoAccessAction`; run
  timing steps `BreachCandidateSelection` and `BreachAccessAbility` replace
  the placeholder breach boundary; typed events `BreachStarted`,
  `BreachCandidateChosen`, `CardAccessed`, `AccessedCardTrashed`; `RunState`
  gained the `InProgress` access state, `BreachHiddenCandidateSource`, and the
  chosen/accessed card lists. Core catalog gained `AssetBehaviorSpec` (trash
  cost) and `AgendaBehaviorSpec` (agenda points), bumping the catalog version
  to `gateway-m2-2`; Cards-layer registrations for Nico Campaign (30037: trash
  cost 2) and Offworld Office (30067: 2 agenda points).
- W2: breach engine (CR 7.5/7.2). Candidate selection: remote servers offer
  every card in the remote root; HQ offers installed root cards plus hidden
  hand candidates; R&D offers installed cards plus the top deck card (hidden);
  Archives flips the discard faceup and makes every card a candidate.
  Sequential access: the Runner chooses candidates in order (specific card or
  a random hidden token consumed from the hidden zone via the working RNG,
  bounded by the random access limit); the chosen card is revealed faceup and
  access is recorded at choice time (chosen + accessed + `CardAccessed`), then
  the access step yields the mid-access ability decision; when no candidates
  remain the run ends with `RunEndReason.BreachCompleted`. The validator
  mirrors every offer condition and adds cross-list invariants (accessed
  subset of chosen, candidates disjoint from chosen).
- W3: pay-trash access path (CR 7.1.5/7.1.5b). The basic trash is offered only
  for a registered asset that is not already in the Corp discard and whose
  trash cost the Runner can pay; consuming it is atomic (`CreditsAdjusted` →
  `CardMoved` → `AccessedCardTrashed`), preserving faceup state.
- W4: mandatory agenda-steal access path (CR 7.1.6/7.2.3). Accessing a
  registered agenda offers exactly one action — the steal (new
  `ActionKind.StealAccessedAgenda`; the existing `StealAgenda` kind is claimed
  by the toy game's `ConsumeAction` routing and was not reused). The steal
  moves the agenda faceup to the Runner score, awards the spec's agenda
  points, and emits `AgendaStolen`; reaching the agenda-point win threshold
  completes the run normally (RunEnded, CR 9.8 breaker detach) and then ends
  the game (`GameWon`, AgendaPoints). A forged no-action during a mandatory
  steal is rejected.
- 23 new focused tests (13 Core: 6 breach candidate-selection/access, 3
  pay-trash, 4 mandatory steal; 10 Cards: asset/agenda spec binding).

### Slice 3 sub-slice 3.4 W1: Overclock run-scoped hosted credits (first-pass record)

- Added `RunEventBehaviorSpec` (definition, printed cost, hosted credits) to the
  Core catalog with fail-closed registration semantics matching the other spec
  families; catalog version bumped to `gateway-m2-3`. Cards layer registered
  Overclock (30029: cost 1, 5 hosted credits) in
  `GatewayRunEventBehaviorSpecs.cs`.
- `BeginRun` pays a registered run event atomically at initiation: the wallet
  pays the printed cost only, the hosted amount seeds `RunState.RunCredits`
  (a run-scoped pool, `[JsonRequired]`, round-trips through `EngineJson`), and
  `RunEventPlayed` is emitted. Unregistered event sources start plain runs,
  unchanged. A run started with a registered event that the Runner cannot
  afford fails closed before any mutation.
- While the run is live, pump, break, and breach-trash costs offer each legal
  wallet/hosted-credit division as an engine-issued action; the chosen split
  is applied atomically. Credit-loss subroutines remain wallet-only, and
  `EndRunIfCreditsAtMostSubroutineSpec` reads only the target wallet under CR
  1.13.3. Hosted Overclock credits are a separate spend source and do not
  satisfy that threshold.
- Unspent run credits expire at run end in `CompleteRun` (no refund to the
  wallet), emitting `RunCreditsAdjusted` before `RunEnded`. Offer-ability checks
  in the engine and the `StateInvariantValidator` mirror both count wallet +
  run pool; Corp rez stays wallet-only in both.
- New typed events `run-event-played` and `run-credits-adjusted` with JSON
  polymorphism, `Copy()`, validator cases, and round-trip coverage.
- 18 Core tests (`RunEventTests`) plus 7 Cards tests
  (`GatewayRunEventBehaviorSpecTests`) covering: atomic initiation payment,
  fail-closed affordability, unregistered-source no-op, run-first spend for
  break/pump/loss, **split payments spanning pool + wallet** (pump, break, and
  accessed-card trash), `EndRunIfCreditsAtMost` wallet-only boundaries,
  expiry without refund, save/load determinism with live run credits, and
  catalog semantics.
- Reviewed and accepted by the reviewer agent (verdict: ACCEPT with notes).
  The second-pass corrections add the Spark fixture (cost 1, 1 hosted) and
  direct split-action tests for pump, break, and accessed-card trash.
- Documented notes (accepted, Slice 4 must honor them):
  - Events emitted inside `BeginRun` (the initiating `credits-adjusted` and
    `run-event-played`) consume event-sequence numbers but are not delivered in
    any `ApplyResult` batch (pre-existing setup-boundary pattern); the
    observable event stream of an Overclock run starts three sequences after
    the pre-run `EventSequence`. Scenario/CLI fixtures must key off
    `state.EventSequence`.
  - `RunState.RunCredits` is `[JsonRequired]` while `RunState.Version` and the
    game schema stay at 1; pre-change live-run saves are not forward-loadable.
    M1 no-run goldens are unaffected (a null run is omitted from JSON).

## Documented M2 deviation (accepted)

- Trigger/interrupt recursion suppression: a self-triggering registration
  resolves exactly once per occurrence. Literal CR 9.6.4a would permit
  self-recursion and CR 10.1.6a (mandatory infinite loops, player picks the
  count) is not implemented in M2. None of the representative Gateway cards
  requires infinite-loop handling, so the suppression is accepted for M2 and
  must be revisited if a supported card needs it.
- Process deviations (sub-slice 3.2, accepted): the reviewer agent
  repeatedly exhausted its step budget, so W3a, W3b, and W4 were
  orchestrator self-reviews against M2_ARCHITECTURE.md after the full suite
  passed (W1 and W2 received proper reviewer passes). The W3a implementer
  worker also exhausted its step budget with the unit unfinished (broken
  build, no tests); the orchestrator completed the remainder (validator
  mirrors, the test file, and the build fixes) before self-review.
- Sub-slice 3.3 rule assumptions (accepted): the mandatory-steal win check
  reuses `ToyRules.AgendaPointsToWin` (3) — M2 has no separate agenda-win
  rule, and the representative set wins at 3 points. The random access limit
  is fixed at the default 1 when the breach begins (CR 7.3.5a); no supported
  card increases it (CR 7.3.5b out of scope for M2).
- Process deviation (sub-slice 3.3, accepted): the implementer subagent
  repeatedly exhausted its step budget mid-unit (W1 finished all code but died
  before reporting; the first W2 dispatch spent its budget reading with no
  edits; W3 exited after one dispatch case, leaving a broken build; W4
  finished all production code with no tests). The orchestrator resumed the
  same session each time with a strict edit-only packet and performed
  orchestrator self-reviews of all four units against M2_ARCHITECTURE.md after
  an independent build and full-suite run.

## Validation at the Slice 2B checkpoint

- `dotnet build Netrunner.sln --no-restore --nologo -m:1`: passed, 0 warnings,
  0 errors.
- Full solution suite: 180 passed, 0 failed (Core 131, Cards 14, AI 9, CLI 26),
  including all 17 `CheckpointTests` and the M1 no-run golden hashes.
- `git diff --check`: clean at the checkpoint commit.
- Slice 2B was reviewed against M2_ARCHITECTURE.md; the reviewer found and
  fixed the per-occurrence suppression defect before acceptance.

## Validation at the Slice 3 sub-slice 3.1 boundary

- `dotnet build Netrunner.sln --no-restore --nologo -m:1`: passed, 0 warnings,
  0 errors.
- Full solution suite: 193 passed, 0 failed (Core 138, Cards 20, AI 9, CLI 26),
  including all 13 new sub-slice tests and the M1 no-run golden hashes.
- `git diff --check`: clean.
- Each of the three work units (spec catalog + Cards registration, encounter
  subroutine initialization, real rez costs) was reviewed against
  M2_ARCHITECTURE.md and accepted. Sub-slice changes are uncommitted at the
  time of writing; the Slice 2B checkpoint commit remains `53c2e7c`.

## Validation at the Slice 3 sub-slice 3.2 boundary

- `dotnet build Netrunner.sln --no-restore --nologo -m:1`: passed, 0 warnings,
  0 errors.
- Full solution suite: 235 passed, 0 failed (Core 174, Cards 26, AI 9, CLI
  26), including all 42 new sub-slice tests and the M1 no-run golden hashes.
- `git diff --check`: clean.
- W1 and W2 were reviewed by the reviewer agent; W3a, W3b, and W4 were
  orchestrator self-reviews (documented process deviation above).
  Sub-slice 3.2 changes are uncommitted at the time of writing; the Slice 2B
  checkpoint commit remains `53c2e7c`.

## Validation at the Slice 3 sub-slice 3.3 boundary

- `dotnet build Netrunner.sln --no-restore --nologo -m:1`: passed, 0 warnings,
  0 errors.
- Full solution suite: 258 passed, 0 failed (Core 187, Cards 36, AI 9, CLI
  26), including all 23 new sub-slice tests and the M1 no-run golden hashes.
- `git diff --check`: clean.
- All four work units (W1 data layer, W2 breach engine, W3 pay-trash, W4
  mandatory steal) were orchestrator self-reviews (documented process
  deviation above), each verified by an independent build and full-suite run
  before the next unit began. Sub-slice changes are uncommitted at the time of
  writing; the Slice 2B checkpoint commit remains `53c2e7c`.

## Validation at the Slice 3 sub-slice 3.4 W1 boundary

- `dotnet build Netrunner.sln --no-restore --nologo -m:1`: passed, 0 warnings,
  0 errors.
- Full solution suite: 279 passed, 0 failed (Core 201, Cards 43, AI 9, CLI
  26), including all 21 new sub-slice tests and the M1 no-run golden hashes.
- `git diff --check`: clean.
- Reviewed by the reviewer agent; ACCEPT with notes, both actionable notes
  fixed before the boundary run (see above).

### Slice 3 sub-slice 3.4 W2: observation boundary

- Added `ObservationBuilder.Build(state, viewer)` (Core) as the sole
  production path from authoritative `GameState` to the M0 `Observation`
  agent/client DTO. The M1 toy match runner now consumes the builder too
  (its empty-view helper is gone); the toy agents read only legal actions,
  so every M1 match result and pinned hash golden is unchanged.
- Exposure policy: a card is visible when it is the viewer's own card or
  faceup (the engine turns cards faceup only on public reveal). Public
  state carries turn/players/outcome, per-server ice and root (face-down
  ICE keeps its position as a `null` slot for the non-owner), public zone
  contents (corp discard, runner heap, runner rig, both score areas), and
  hidden-zone counts only. Private state carries the viewer's own hand/deck
  plus face-down ICE (Corp) or grip/stack (Runner). Public run/encounter
  data is projected per the run view; hidden breach pools are anonymous
  (`candidates` empty, `hiddenCandidateSource` metadata only) until a card
  is drawn, after which the revealed card is public to both viewers.
- Legal actions appear only in the owner's observation (the other viewer
  gets the decision id with an empty action list); engine internals (RNG,
  allocator, sequences, revisions, schema, debug data) are never projected.
- Nine `ObservationTests`: six exact-string goldens pinning the full
  serialized observation for both viewers before a run, during breach
  candidate selection (run section, both viewers byte-identical), and after
  a stolen-agenda run; negative assertions prove neither viewer sees the
  opponent's hidden cards, face-down ICE identity, or engine internals;
  `AllFixturesValidateAsProducibleEngineStates` pins that every fixture is a
  state the `StateInvariantValidator` accepts (run timing frames, decision
  revisions, legal-action sets matching the engine's own offers).
- Reviewed by the reviewer agent: ACCEPT, no blockers, no majors. Both
  actionable P2 notes were fixed before the boundary run: fixtures now model
  producible engine states (matching `RunTimingFrame`, the
  `BreachAccessAbility` chosen+accessed shape, a registered-agenda stolen
  score via `FixtureAgendaSpecs`, `decisionRevision` alignment), and
  `run.initiatingSource` is routed through the same visibility gate as
  access candidates. The remaining note (byte goldens rely on dictionary
  insertion order) was accepted as the same mechanism the repository already
  uses for pinned state/event hash goldens.
- Documented note (accepted): the byte-pinned goldens depend on .NET
  dictionary enumeration order and string hashing stability; this matches
  the existing pinned-hash golden mechanism and the goldens fail loudly if
  the runtime ever changes ordering.

## Validation at the Slice 3 sub-slice 3.4 W2 boundary

- `dotnet build Netrunner.sln --no-restore --nologo -m:1`: passed, 0 warnings,
  0 errors.
- Full solution suite: 288 passed, 0 failed (Core 210, Cards 43, AI 9, CLI
  26), including all 9 `ObservationTests`, the W1 run-event suite, and the
  M1 no-run golden hashes (unchanged after the toy runner switched to the
  observation builder).
- `git diff --check`: clean.
- Reviewed by the reviewer agent; ACCEPT with notes, both actionable notes
  fixed before the boundary run (see above).

## Slice 4: milestone proof and exit gate

Started after the W2 boundary; the full solution suite was green (288 tests)
before this slice began. Implementation units completed so far, each verified
against the full solution suite (currently 299 tests including a temporary
proof smoke test that is being replaced by production tests):

### Scenario model and curated representative run

- `M2ScenarioRunSpec` (`server`, `source`, `breaker`, `maxPumps`) with strict
  path validation: the breaker must be Runner-owned, placed in the runner rig,
  and a registered icebreaker definition; `maxPumps` must be zero or
  positive.
- `data/m2/representative-run.json` is pinned with
  `expectedInitialStateHash`; `M2ScenarioSetup` places remote ICE (Palisade
  outer, Whitespace inner), root agendas (Offworld Office, Nico Campaign),
  the face-up rig (Cleaver, Buzzsaw), and the grip (Overclock).
- `M2ScenarioSetupTests` covers strict path errors for every run field.

### Headless coordinators

- `ScriptedHumanRunPolicy` (CLI): an explicit ordered human plan (context
  predicate + action selector per step; the plan head is consulted, a
  mismatching step is not consumed; default is continue-run, else pass).
- `RunContractAgent` + `RunPlayContract` (AI): fixed-priority rule agent
  (rez → attach contracted breaker → break → pump within budget → breach
  candidate → steal → trash → continue → pass). Both consume only
  `ObservationBuilder.Build(state, viewer)` output and issued legal action IDs.

### M2ProofRunner

- Builds the scenario, begins the contracted run, and at every yielded
  decision serializes the canonical state, restores it, and requires the
  restored state hash to match (any divergence throws `M2ProofException`).
- Drives the policy through filtered observations, rejects unoffered action
  IDs, records `ReplayAction`s, then verifies the replay artifact with
  `ReplayVerifier` and requires `RunTimingTraceValidator` to report no
  problems.

### Timing-trace validation and engine fixes discovered while wiring it

- `RunTimingTraceValidator` + `RunTimingCitations`: every `RunTimingStep` and
  every run event kind has a CR 26.03 citation; the validator enforces the
  ordered window sequence (initiation → per-ICE approach/encounter/movement
  windows → success → breach windows → run end) and the run-credits ledger
  (never negative, zero at run end).
- Engine fixes made while the validator exposed real gaps: `BeginRun` now
  emits the `run-initiated` event first (CR 26.03 6.9.1); breach-flow window
  events now carry the correct step labels (BreachBoundary →
  BreachCandidateSelection → BreachAccessAbility); the validator treats the
  6.9.5b boundary as the context of the first 7.5.4 selection and accepts it
  as a post-success window; `ReplayVerifier` accepts the M2 card-data and
  behavior-registry versions in addition to the M1 ones.
- `CardBehaviorSpecCatalog` is now thread-safe (a single gate around every
  read and write) after a parallel xUnit registration race surfaced an
  intermittent `ArgumentException` in the CLI test assembly.

### Timing-scenario evidence index

- `docs/M2_TIMING_SCENARIOS.md` maps all thirteen required timing scenario
  categories to checked-in tests/scenarios with CR 26.03 citations and
  Jinteki.net behavioral cross-check notes.

### Production proof tests, validator unit tests, and the CLI proof command

- `M2ProofTests` (CLI, 4 facts): both policies byte-identical (pinned final
  state hash `edb61c2df27e0daefc5e4bdeff3d2dc3530c6089d4a381da602ee3ef47ba5cd7`
  and event stream hash
  `ac1e0b6f673c4ddda4d1d1beaf6a1792f615be8121bb47e367a2232f05be5b96`), the
  curated exit-gate ordering with the run-credits pool trajectory (5 → 3 → 2 →
  0, asserted as the adjusted-values sequence 3, 2, 0), save/load at every
  yielded decision (31 checkpoints, replay verified), and unoffered-action
  rejection.
- `RunTimingTraceTests` (Core, 5 facts): citation coverage for every
  `RunTimingStep`, a real engine-driven stream passes validation, and
  missing/misordered window detection on mutated copies.
- `m2-proof [scenarioPath] [--json]` CLI command: loads the strict scenario,
  runs both policies, requires byte-identical hashes, and reports the cited
  trace, checkpoint ledger, and replay verification (exit 0 on success;
  structured failures for load/contract errors and proof failures).
- `scripts/verify.sh` gained the `m2-proof --json` gate after the M1 replay
  block.
- Trace rendering now fills every run event detail (initiation, run event,
  encounter completion, movement, steal, trash).
- Six pre-existing unnecessary using directives were removed so the format
  gate passes (approved during the implementation dispatch; no behavior
  change).

## Validation at the Slice 4 boundary

- `dotnet build Netrunner.sln --no-restore --nologo -m:1`: passed, 0 warnings,
  0 errors.
- Full solution suite: 309 passed, 0 failed (Core 215, Cards 43, AI 9, CLI
  42); the CLI test assembly ran 3 consecutive times green (no ordering
  flakes).
- `dotnet format Netrunner.sln --no-restore --verify-no-changes`: passed.
- `m2-proof`: exit 0; both policies byte-identical (31 checkpoints, replay
  verified, no trace problems).
- `scripts/verify.sh`: full run, exit 0, including the M2 proof gate and the
  Godot headless smoke.
- Reviewed by the reviewer agent; ACCEPT, no blockers. The one latent major
  (the validator's candidate-count check miscounted hidden-source HQ/R&D
  breach choices) and four minors (two doc corrections, the `m2-proof`
  catch filter missing `InvalidOperationException`-family failures, and the
  verify.sh CWD-relative scenario path) were all fixed at this boundary and
  re-verified.

## M2 first-pass completion snapshot (historical; superseded)

At the first-pass boundary, all planned M2 work in
`docs/M2_ARCHITECTURE.md` and the implementation plan was reported done:
Slices 1, 2A, 2B, 3.1–3.3, 3.4 W1/W2, and Slice 4. The milestone exit gate is
satisfied: the curated run finishes headlessly from both a scripted-human
policy and a contract agent using only filtered observations and issued legal
action IDs; Palisade and Whitespace are legally rezzed, encountered, broken or
fired in order; the run reaches the remote breach/access of Offworld Office
and Nico Campaign; the Overclock run-scoped credits are spendable only during
that run and expire at run end; save/load succeeds at every yielded decision;
and the cited trace validator finds every CR-required window present and in
order.

## Resume commands

```bash
git switch qwen_cont
git log -2 --oneline
source scripts/env.sh
dotnet build Netrunner.sln --no-restore --nologo -m:1
dotnet test Netrunner.sln --no-restore --nologo -m:1
```

Do not modify `src/Netrunner.Godot/addons/godot_ai`; it is user-owned and
outside the M2 work.

## M2 rework — independent review fixes (historical, qwen_cont @ 67c954f)

`independent_review_m2.md` (untracked, user-provided) rejected the M2
completion claim: the automated gate was green but the representative proof
was deterministic rather than rules-correct. The entries below preserve that
first-pass fix-unit history. Their claims describe the state at that boundary;
the second-pass section at the end supersedes any detail that it explicitly
corrects.

- **U1 (accepted)** — rez turns ICE faceup (CR 26.03 8.1.1).
  `ConsumeRunRez` now emits `FaceUp: true`; new fail-closed invariant
  `cards.rezzed-facedown` (Rezzed && !FaceUp is a violation); FaceUp
  assertions in `RunRezCostTests`; the shared `CreateRunState` fixture no
  longer encodes rezzed-facedown ICE; end-to-end
  `ObservationTests.RezApproachedIceTurnsFaceupForBothViewers`. Re-pinned M2
  final state hash to `edb61c2df27e0daefc5e4bdeff3d2dc3530c6089d4a381da602ee3ef47ba5cd7`
  (event stream `ac1e0b6f...` and initial `4733c8f8...` unchanged). Reviewer
  verdict: ACCEPT, no blockers/majors/minors.
- **U2 (implemented)** — breach access visibility is temporary, not a
  permanent faceup (CR 26.03 7.3.1a; review P0 hidden-information leak).
  `ConsumeRunChooseBreachCandidate` no longer flips the chosen card faceup
  and `StepBreachBoundary` no longer flips facedown Archives discard cards;
  a card trashed through access is revealed when it enters the discard.
  `ObservationBuilder` now computes an access-visible set from the run state
  (all chosen access cards, plus the whole Corp discard while the attacked
  server is Archives) and threads it through every visibility decision, so
  the chosen card and the Archives discard are visible to both viewers only
  for the remainder of the breach. Tests rewritten to the
  temporary-visibility expectation: `HqBreachOffersSingleHiddenHandTokenAndDrawsDeterministically`,
  `RndBreachAccessesTopCardOfDeck`,
  `ArchivesBreachTurnsDiscardFaceupAndAccessesEveryCard` →
  `ArchivesBreachKeepsDiscardFacedownAndAccessesEveryCard`,
  `AssetInArchivesIsAccessedButNotTrashed`,
  `ArchivesAgendaFlippedFaceupStillForcesSteal` →
  `ArchivesAgendaBreachAccessStillForcesSteal`, plus facedown-at-selection in
  `RemoteBreachOffersTrashForPayableAssetAndTrashesIt`. New observation
  tests: `RemoteBreachFacedownAssetVisibleToBothViewersOnlyDuringTheBreach`,
  `HqBreachFacedownHandCardVisibleToBothViewersOnlyDuringTheBreach`,
  `RndBreachFacedownTopCardVisibleToBothViewersOnlyDuringTheBreach`,
  `ArchivesBreachFacedownDiscardVisibleToBothViewersOnlyDuringTheBreach`
  (314 tests green). Curated proof unchanged: final state
  `edb61c2df27e0daefc5e4bdeff3d2dc3530c6089d4a381da602ee3ef47ba5cd7`, event
  stream `ac1e0b6f...`, initial `4733c8f8...` — both policies
  byte-identical, replay verified. Reviewer verdict: ACCEPT; three
  non-blocking P2 minors (owned-discard under-reveal to the Corp, breach
  window spans the run, pre-existing doc-comment inaccuracy).
- **U3 (implemented; first-pass record, superseded below)** — run-event payment, loss path, and lifecycle (CR 26.03
  1.10.3b/1.10.3c/1.10.4, 8.6.7; review P1s). This historical entry records
  the first-pass lifecycle and payment policy; its grip/cleanup visibility,
  pool-first spend, and combined-credit threshold details are superseded by
  the second-pass section below. Model verified against the
  published CR text: (a) the run event's printed cost is paid from the
  Runner's credit pool (wallet) only — 1.10.3c ("To spend or pay credits, a
  player puts the specified number of credits back into the bank. By default,
  the credits must come from the player's credit pool."); (b) the event's
  hosted credits are not paid — they stay on the event and are spendable
  during the run as if in the pool — 1.10.4b/c; (c) losing credits (e.g.
  Whitespace "lose 3") always comes from the wallet, clamped to the wallet's
  balance, never from hosted credits — 1.10.3b ("If a player is required to
  lose credits, that player is forced to move the specified number of credits
  from their credit pool to the bank. Players cannot lose credits from
  cards."); (d) "fewer than N credits" checks count wallet + hosted run pool
  (1.10.4b). Implemented: `BeginRunWithEvents` pays cost only and seeds the
  run-scoped pool with the hosted credits; `BeginRunWithEvents` rejects
  non-Runner-owned sources, and new `ValidateRunEventSource` rejects any
  source that is not a grip card (faceup or facedown) with a registered
  run-event spec (or the already-active event). At this first-pass boundary,
  the first-pass event lifecycle kept the source in its original Runner zone
  and used a then-current cleanup visibility policy; the credit-loss subroutine uses a wallet-only lose
  path while `PayRunnerRunCredits` used a deterministic spend path (pool
  first, then wallet) for pump/break/trash costs; `ObservationBuilder`
  renders `run.initiatingSource` as the full candidate view so both viewers
  see the faceup event (id + definition) during the run. Tests: `RunEventTests`
  rewritten to the new ledger plus new source-validation, run-end trash,
  clamped-loss, and both-viewers observation tests (318 tests green).
  Re-pinned curated proof: final state
  `edb61c2df27e0daefc5e4bdeff3d2dc3530c6089d4a381da602ee3ef47ba5cd7` →
  `c80749ff2ab554456a78e5328d52942ec7d5029caf391c81153441ba86ddcd6b`, event
  stream `ac1e0b6f673c4ddda4d1d1beaf6a1792f615be8121bb47e367a2232f05be5b96` →
  `011de577387024314e9811354e39b90ffef4c0f6e4e2a2eaff865a8ca9136065`,
  initial `4733c8f8...` unchanged; run-credits-adjusted ledger `[3, 2, 0]`
  unchanged; wallet ledger 15 → 14 (Overclock cost 1) → 11 (Whitespace loss
  3) → 11 (Nico trash 2 paid from the pool), pool 5 → 3 → 2 → 0; both
  policies byte-identical, replay verified.
- **U4 (implemented)** — HQ/R&D breaches include their root cards as
  candidates (CR 26.03 7.4.1; review P1 "central-server roots are omitted
  from breaches"). Verified against the published CR text: 7.4.1 — "The
  candidates at the beginning of a breach are as follows: a. Each card in
  the root of the breached server is a candidate. b. If the breached server
  is HQ, each card in the Corp's hand is a candidate. c. If the breached
  server is R&D, the top card of the Corp's deck is a candidate. d. If the
  breached server is Archives, each card in the Corp's discard pile is a
  candidate." — so root cards are candidates for every server kind, listed
  first, then the central-zone access. `StepBreachBoundary`
  (`src/Netrunner.Core/GameEngine.Run.cs`) now seeds the explicit candidate
  list with the HQ/R&D root zone's cards (root-first ordering); the
  hidden-source mechanism (HQ hand / R&D deck top, randomAccessLimit 1) is
  unchanged, and remote/Archives paths are unchanged (Archives covers the
  discard per 7.4.1d, noted in the comment; the Archives ROOT was added in
  U9 per 7.4.1a — see the U9 entry). `BreachStarted.CandidateCount`
  keeps its explicit-pool semantics: the new root cards are explicit
  candidates and are chosen with `Source == null`, so the trace validator's
  candidate-count check in `src/Netrunner.Core/RunTimingTrace.cs` stays
  consistent. Observation: facedown HQ/R&D root cards render as anonymous
  `null` candidates to the Runner before selection; on access they are
  visible to both viewers for the remainder of the breach (U2 access-visible
  set); faceup root cards stay visible via the normal visibility rules. No
  engine changes were needed in the observation layer. Tests (321 green,
  was 318):
  `BreachAccessTests.HqBreachOffersRootCardsFirstThenHiddenHandAccess`
  (root-first ordering, CandidateCount 2 with root cards, facedown asset
  anonymous before selection / revealed to both viewers on access, random
  access limit untouched by root picks, full breach to run end),
  `BreachAccessTests.HqBreachWithRootCardsPassesRunTimingTraceValidation`
  (full HQ root-breach event stream passes the trace validator), and
  `BreachAccessTests.RndBreachOffersRootCardFirstThenHiddenDeckAccess`
  (same pattern with the top-of-deck hidden access); existing remote/
  Archives breach tests unchanged and green. `CreateBreachState` fixture
  extended with `hqRoot`/`hqRootDefinitions`/`rndRoot`/`rndRootDefinitions`.
  Curated proof unchanged (remote-only scenario, empty HQ/R&D roots):
  final state `c80749ff2ab554456a78e5328d52942ec7d5029caf391c81153441ba86ddcd6b`,
  event stream `011de577387024314e9811354e39b90ffef4c0f6e4e2a2eaff865a8ca9136065`,
  initial `4733c8f8...` — both policies byte-identical, replay verified.
  Files: `src/Netrunner.Core/GameEngine.Run.cs`,
  `tests/Netrunner.Core.Tests/BreachAccessTests.cs`.
- **U5 (implemented; historical behavior superseded by the second-pass
  correction below)** — encounter-scoped rig breakers replace the
  fictional server-level breaker attachment (CR 26.03 6.5/9.8/3.9.5g/3.9.5h;
  review P0 "attaching a breaker to a server is not a rules concept"). This
  historical entry describes the retired per-encounter accounting; the
  second-pass correction below applies BreakLimit per interface activation.
  Verified against the published CR text: 3.9.5g (a breaker's interface
  requires the breaker's current strength to cover the ICE's strength),
  3.9.5h (a named-subtype interface ability applies only to that subtype;
  untyped abilities to any ICE), 6.5.x (an ICE's subroutines are unbroken
  again when a new encounter begins; breaking happens in the 6.9.3b
  encounter paid window), 9.8 (breaker interface costs). Removed:
  `ServerState.AttachedBreakerId` (+ `WithAttachedBreaker`),
  `ActionKind.AttachBreaker`, the `BreakerAttached` event kind/payload, the
  attach reducer, the run-end detach loop, the `attachedBreaker` observation
  field, and the `RunTimingTrace` citation. `RunBreakParameters` was then
  `(breakerId, quantity)` and the break/pump events and trace details carried
  the acting breaker. The first-pass encounter state also had a per-encounter
  usage ledger (now retired): the breaker's printed break limit applied per
  encounter and reset when the next encounter started. The encounter paid
  window then materialized, for every registered rig-installed breaker in
  rig-zone order, break quantities under that first-pass accounting model —
  offered only while the breaker's subtype matched the ICE
  and its current strength (base + active encounter pumps) covers the ICE —
  plus a pump (never subtype- or strength-gated) whenever the pump cost is
  affordable; the window is therefore switchable between breakers and
  breakers always stay in the rig. The scenario's `run.breaker` is now a
  PREFERENCE for both policies (`RunContractAgent`, `ScriptedHumanRunPolicy`):
  use it when eligible, otherwise the first offered breaker. Validator:
  encounter version 2 required, plus new breaker-usage checks
  (`run.breaker-usage-unsupported-version`, `run.unknown-card` on the usage
  breaker, `run.duplicate-breaker-usage`, `run.invalid-breaker-usage`) and an
  updated expected-offer mirror (break actions keyed by
  (breaker, quantity)). Tests (323 green, was 321): `BreakerAttachmentTests`
  rewritten as `EncounterBreakerTests` (8 tests: every eligible rig breaker
  offered — a subtype-mismatched breaker still gets its pump; both rig
  breakers act in one run when the ICE type changes; the historical
  printed-limit/usage and forged-action checks are superseded by the
  per-activation contract below; an unregistered rig program yields no
  offers; save/load restores the encounter window identically). New fixture
  ICE: `ThreeSubroutines`, `CodeGateLoseCredits`.
  Re-pinned M2 proof: final state
  `73ae535ab4f7c1758434b23e51569b697291dd4bfa546edef6c6f8e5ee37e5d9`, event
  stream `14627034b25694c92aab5224945367468da5e54fa8e737204adac9412ff05f57`,
  initial `4733c8f8...` UNCHANGED; both policies byte-identical, replay
  verified, 32 checkpoints. Ledger deviation from the task estimate: the
  curated run breaks every offered subroutine, so Whitespace's two
  subroutines are both broken by Buzzsaw (card:6) and its printed credit
  loss never fires — run-credits ledger `[3, 2, 1, 0]` (pump 2, break 1,
  break 1, break 1; pool 5 → 0), wallet 15 → 14 (Overclock cost) → 12
  (Nico trash 2, pool already exhausted). The estimated final wallet 5 /
  ledger `[3, 2, 0]` assumed the Whitespace loss fires, which is
  incompatible with breaking both of its subroutines (a broken subroutine
  cannot also fire). Files: `src/Netrunner.Core/{GameState,RunState,
  ActionContracts,GameEngine,GameEngine.Run,ObservationBuilder,TypedEvents,
  RunTimingTrace,StateInvariantValidator}.cs`,
  `src/Netrunner.AI/RunContractAgent.cs`,
  `src/Netrunner.Cli/{ScriptedHumanRunPolicy,M2ScenarioSpec}.cs`,
  `tests/Netrunner.Core.Tests/{EncounterBreakerTests,RunBreakTests,
  RunPumpTests,EncounterResolutionTests,RunEventTests,FixtureIceBehaviorSpecs,
  ObservationTests}.cs`, `tests/Netrunner.Cli.Tests/{M2ProofTests,
  M2ProofCommandTests}.cs`, `data/m2/representative-run.json` (citation),
  `docs/M2_TIMING_SCENARIOS.md` (stale test names).
- **U6 (implemented)** — agenda access yields the 7.2.2 mid-access window
  and steals automatically (CR 26.03 7.2/7.1.4/7.1.5/7.1.6; review P1
  "Agenda access skips the mid-access window"). Verified against the
  published CR text: 7.1.4 — "During each access, the Runner has 1
  opportunity to use a mid-access ability. This occurs after the reaction
  window at the beginning of the access, and before the Runner steals an
  accessed agenda."; 7.1.5 — "The Runner always has the ability 'Access →
  Pay the trash cost of the accessed card: Trash it.' This is the basic
  trash ability." (7.1.5a: "If a card does not have a trash cost, the
  Runner cannot pay its trash cost, and therefore the Runner cannot use
  the basic trash ability during that access."); 7.1.6 — "If, after
  resolving mid-access abilities, the Runner is still accessing an agenda,
  they must steal it." (1.16.10a: a forced effect with an additional cost
  may be declined — no M2 card adds a steal cost); 7.2 — "7.2.1. The card
  is accessed. Conditions related to accessing this card are met. 7.2.2.
  The Runner may use a single mid-access ability, such as the basic trash
  ability. 7.2.3. If the accessed card is an agenda, the Runner must steal
  it. 7.2.4. The access is complete. Conditions related to an access
  ending are met." Implemented sequence per accessed card (step names):
  7.2.1 `CardAccessed` event (emitted at candidate choice, unchanged) →
  7.2.2 yielded mid-access decision at `RunTimingStep.BreachAccessAbility`
  — an accessed agenda offers the pass only (`NoAccessAction`, since
  agendas have no trash cost and no other mid-access ability is registered
  in the M2 card set), a non-agenda card offers the basic trash
  (`TrashAccessedCard`, only when payable and not already in the discard)
  or the pass, both unchanged → 7.2.3 progression past the window
  (pass/no trash) with a registered agenda still being accessed
  (not already in the Runner score) resolves the engine-internal
  `StealAccessedAgendaInternal` — the same move/faceup/points/`AgendaStolen`
  event and win check as the removed player-steal path → 7.2.4
  `AdvanceBreachAfterAccess` to the next candidate or `RunEnd`. Removed:
  the player-facing `ActionKind.StealAccessedAgenda` (enum value, both
  dispatch cases, the yielded steal action), replaced by the private
  engine path above; the `StealAccessedAgenda` expectation in the
  pending-decision mirror (now: an accessed agenda offers `NoAccessAction`
  only) and its action-kind checks. Trace validator: the swapped citations
  are fixed — `AgendaStolen` now cites 7.2.3 and `AccessedCardTrashed` now
  cites 7.2.2 (the basic trash ability is a mid-access ability); the
  existing strict check that a 7.2.2 `BreachAccessAbility` window precedes
  every `AgendaStolen`/`AccessedCardTrashed` effect is unchanged, so a
  trace missing the mid-access window for an agenda access remains a
  problem. Policies: `ScriptedHumanRunPolicy` step 8 now passes the
  agenda's mid-access window; `RunContractAgent` drops the steal branch
  and passes the window (trash still wins when offered). Tests (324 green,
  was 323): `RemoteBreachAccessOfAgendaForcesStealAndScoresIt`,
  `StealingWinningAgendaEndsTheGame`, `ArchivesAgendaBreachAccessStillForcesSteal`
  rewritten to the pass-only mid-access window + automatic steal (same
  `AgendaStolen` event); `NoActionIsRejectedWhileAgendaStealIsMandatory` →
  `PassingTheMidAccessWindowCannotSkipTheAgendaSteal` (forged action still
  rejected `UnknownAction`; passing is the only legal action and does not
  skip the steal); new
  `SaveLoadAcrossAgendaMidAccessProducesIdenticalContinuation` (serialize
  at the yielded mid-access decision, restore, continue identically);
  `RemoteBreachAccessesRootCardsInChosenOrderAndEndsRun` strengthened with
  a no-`AgendaStolen` assertion for the non-agenda path (mechanics
  unchanged, per 7.2.2/7.1.5). M2 proof pins UNCHANGED: final state
  `73ae535ab4f7c1758434b23e51569b697291dd4bfa546edef6c6f8e5ee37e5d9`,
  event stream
  `14627034b25694c92aab5224945367468da5e54fa8e737204adac9412ff05f57`,
  initial `4733c8f8...`, 32 checkpoints — the automatic steal emits the
  same event stream from the same 32 decisions, so only the checkpoint
  state hash at the agenda mid-access decision differs from U5 (not
  pinned). Both policies byte-identical, replay verified, trace problems
  empty. Files: `src/Netrunner.Core/{GameEngine,GameEngine.Run,
  ActionContracts,RunState,RunTimingTrace,StateInvariantValidator}.cs`,
  `src/Netrunner.AI/RunContractAgent.cs`,
  `src/Netrunner.Cli/ScriptedHumanRunPolicy.cs`,
  `tests/Netrunner.Core.Tests/BreachAccessTests.cs`,
  `tests/Netrunner.Cli.Tests/{M2ProofTests,M2ProofCommandTests}.cs`
  (pin comments).
- **U7 (implemented)** — the proof resumes from the restored state, and the
  run-timing trace validator is adversarial (review P1 findings A and B).
  Finding A (verbatim): "'Save/load at every decision' does not prove
  resumption. The proof serializes and deserializes each state and compares
  hashes, but then continues from current, not restored:
  src/Netrunner.Cli/M2ProofRunner.cs:143, src/Netrunner.Cli/M2ProofRunner.cs:182.
  Therefore, it proves round-trip equality but not that the loaded state can
  resume and produce the same next decisions/events/hashes, as required by
  docs/M2_ARCHITECTURE.md:170. Set current = restored before building the
  observation and applying the action, or apply each selected action to both
  live and restored states and compare results." Finding B (verbatim): "The
  timing validator can accept a trace with no timing windows or breach. The
  first-window check is conditional on windows.Count > 0:
  src/Netrunner.Core/RunTimingTrace.cs:241. Success/breach checks are
  likewise mostly conditional on those events already existing:
  src/Netrunner.Core/RunTimingTrace.cs:350. As written, a synthetic
  successful stream containing run-initiated, run-successful, and
  run-ended, but no timing windows or breach, can evade the intended
  completeness checks. Add adversarial tests for: zero timing windows,
  missing breach after success, missing success/breach windows, ..." Fix A:
  in the proof loop (`src/Netrunner.Cli/M2ProofRunner.cs`), after the
  serialize/deserialize round trip and its hash-equality assert, the loop
  re-reads the pending decision from the RESTORED state (asserting it is
  non-null and that its Id/Revision/Owner match the live one — the round
  trip is byte-equal, so they must), builds the observation from the
  restored state, validates the agent's choice against the restored
  decision's legal actions, and calls `GameEngine.Apply` on the restored
  state, assigning the result back to `current`; the live object never again
  drives the decision, observation, or apply of an iteration, satisfying
  M2_ARCHITECTURE.md:170. `DeterministicHash.ForState` is the SHA256 of the
  full canonical JSON, so a byte-equal round trip implies a byte-identical
  downstream stream — verified empirically, the curated proof output is
  unchanged. Fix B (`src/Netrunner.Core/RunTimingTrace.cs`): the validator
  is now unconditional for any stream containing a run, and out of scope
  (validates empty) for streams with no run events at all (M1 no-run
  paths): (1) a run trace with zero timing windows is a problem, and the
  first-window check (InitiationPaidWindow, after initiation) applies to
  every run trace; (2) a run that ended successful must have exactly one
  breach-started event (previously a successful run with no breach passed);
  (3) new run-id consistency check: every run event carrying a RunId (all
  run-related kinds except agenda-stolen, which carries none) must carry the
  trace's single run ID, so interleaved events from two runs are rejected;
  (4) new priority-alternation state machine (CR 26.03 9.2): every paid
  window opens with the Runner holding priority (GameEngine.CreatePriority);
  a pass is legal only while a paid window is open, only by the current
  holder, only with the next consecutive-pass count, and hands priority to
  the other side; a Corp rez (8.1) is legal only while the Corp holds
  priority in the approach window and restarts the counter at zero with
  priority kept by the Corp; a window closes only at the second consecutive
  pass, so no other window may open and the run may not end while one is
  still open; re-emitted window events (the engine emits one per re-yield
  but none on a window's first yield) must carry the current holder. The
  existing checks for the streams they pass are unchanged. Tests (333 green,
  was 324): new adversarial tests in
  `tests/Netrunner.Core.Tests/RunTimingTraceTests.cs` —
  `SuccessfulRunWithZeroTimingWindowsIsRejected` (the exact evasion:
  run-initiated + run-successful + run-ended, no windows, no breach),
  `MissingBreachAfterSuccessIsRejected`,
  `ChosenCandidateWithoutSelectionWindowIsRejected`,
  `AccessEffectWithoutMidAccessWindowIsRejected`,
  `MixedRunIdsAreRejected` (two full runs interleaved, renumbered),
  `PassByWrongHolderIsRejected` (Corp passes first),
  `ConsecutivePassesBySameHolderAreRejected` (Runner twice in a row),
  `MinimalWellFormedSuccessfulStreamPasses` (positive control: a hand-built
  minimal successful stream — initiation window, success, one-candidate
  breach, selection window, access, run end — validates with zero
  problems), and
  `StreamWithoutRunEventsStaysOutOfScope` (M1 no-run control: turn/click
  events only, zero problems, zero entries). M2 proof pins UNCHANGED
  (verified, not re-pinned): final state
  `73ae535ab4f7c1758434b23e51569b697291dd4bfa546edef6c6f8e5ee37e5d9`,
  event stream
  `14627034b25694c92aab5224945367468da5e54fa8e737204adac9412ff05f57`,
  initial `4733c8f8...`, 32 checkpoints — the `m2-proof --json` output is
  byte-identical before and after the change, both policies
  byte-identical, replay verified, trace problems empty. Files:
  `src/Netrunner.Cli/M2ProofRunner.cs`,
  `src/Netrunner.Core/RunTimingTrace.cs`,
  `tests/Netrunner.Core.Tests/RunTimingTraceTests.cs`.
- **U8 (implemented; historical citation/visibility record)** — docs,
  citations, and the Corp's facedown Archives
  observation. (1) `README.md` "Current state" now reads M1 and M2 complete
  (keeping the "not yet the complete Netrunner ruleset" framing) and lists
  the `m2-proof` command; this file's header and the rework intro reflect
  the completed rework. (2) Every wrong CR 26.03 citation in the
  run-timing renderer/validator (`RunTimingTrace.cs`) is fixed against the
  fetched CR 26.03 text (rules.nullsignal.games; sections verified in
  full): the first-pass credit text had incorrectly described hosted run
  credits as a separate generic alternate fund and used an incorrect
  section reference; the corrected implementation and citations use
  1.10.3b/1.10.3c/1.10.4 for wallet loss, player-chosen hosted-credit
  spending, and event-hosted credits. Run-event cleanup is 6.9.6b. The
  priority citations 9.3/9.4 (the interpreting-card-text and
  static-abilities sections) are 9.2 (Timing and Priority; 9.2.7 paid-window
  passing); the 6.9.1f initiation-phase-complete citation for the run-event
  payment is 6.9.1e (the initiation paid window).
  `docs/M2_TIMING_SCENARIOS.md` lines 24/35/44/127/157/268/301 now cite
  1.10.3b/1.10.3c/1.10.4, 6.9.6b/10.6.3b, 3.9.5 (icebreakers, replacing
  "12.x"), 9.6/9.2.6b/9.6.15 (trigger windows, replacing "4.x"), 9.9
  (interrupts and replacement), 10.4/10.4.4 (damage/flatline), 10.5 (tags
  — a correct tags citation), and 2.10/3.9.3c at the 10.3.1e checkpoint
  (memory cleanup); a duplicated credit-ledger comment in `M2ProofTests.cs`
  is consolidated with the corrected citations. (3) The citation tests
  assert the EXACT citation strings — mapping level for the corrected
  kinds and the curated window sequence, plus an end-to-end validated-stream
  check with negative scans for the stale 10.5 / 9.3/9.4 patterns —
  instead of "CR 26.03" substring checks. (4) The Corp's OWN facedown
  Archives (discard) cards are now in the Corp-private observation
  (`faceDownArchives`, the private card view, same pattern as
  `faceDownIce`; faceup trash stays public via `corpDiscard` and never
  appears in the private list) per CR 26.03 4.4.6c, with no
  double-exposure against the U2 access-visible logic: during an Archives
  breach the facedown discard card is public to BOTH viewers through the
  breach candidate list (7.3.1a) and additionally in the Corp's private
  view, and a card flipped faceup by trash is public via `corpDiscard` and
  drops out of the private listing. New tests:
  `ObservationTests.FacedownCorpArchivesCardIsVisibleToTheCorpOnly`,
  `ObservationTests.FacedownArchivesCardIsNotDoubleExposed`,
  `RunTimingTraceTests.EventCitationsMatchTheVerifiedCrSections`,
  `RunTimingTraceTests.WindowCitationsForTheCuratedSequenceMatchTheVerifiedCrSections`,
  `RunTimingTraceTests.ValidatedStreamEntriesCarryTheVerifiedCitations`;
  the two U2 tests asserting the facedown discard card is invisible to the
  Corp are updated to the new Corp-private visibility (Runner-side
  invisibility unchanged). Verification (boundary): build 0 warnings / 0
  errors; all 338 tests pass (was 333); `dotnet format` clean;
  `m2-proof --json` exit 0 with all pins UNCHANGED (verified, not
  re-pinned): initial `4733c8f8...`, final state
  `73ae535ab4f7c1758434b23e51569b697291dd4bfa546edef6c6f8e5ee37e5d9`,
  event stream
  `14627034b25694c92aab5224945367468da5e54fa8e737204adac9412ff05f57`,
  32 checkpoints, both policies byte-identical, replay verified, trace
  problems empty — the observation change is Corp-private and the
  citation strings live in the renderer/validator, not in engine payloads.
  Files: `README.md`, `docs/M2_PROGRESS.md`,
  `docs/M2_TIMING_SCENARIOS.md`,
  `src/Netrunner.Core/ObservationBuilder.cs`,
  `src/Netrunner.Core/RunTimingTrace.cs`,
  `tests/Netrunner.Cli.Tests/M2ProofTests.cs`,
  `tests/Netrunner.Core.Tests/BreachAccessTests.cs`,
  `tests/Netrunner.Core.Tests/ObservationTests.cs`,
  `tests/Netrunner.Core.Tests/RunTimingTraceTests.cs`.
  Review (fresh, diff-first, CR ground truth re-fetched): first pass
  REJECTED on four text-only points in `docs/M2_TIMING_SCENARIOS.md` —
  (a) the new reaction-window citation pointed at 9.2.6 (the ACTION
  window section) instead of 9.2.8/9.2.8b; (b) the category-10
  parenthetical still attributed the zero run pool to a Whitespace credit
  loss that never fired in the pinned run (both subroutines are broken);
  (c) the "Pinned representative run" paragraph still described the pre-U5
  run (ledger 5 → 3 → 2 → 0, "0 (Whitespace credit loss)"); (d) the
  category-5 bullet still claimed the lose-3 subroutine fired unbroken and
  the 8 > 6 end-the-run check ran. Fixed text-only: (a) 9.2.8/9.2.8b;
  (b)–(d) the pinned-run descriptions now match the accepted U5 facts
  (ledger (3, 2, 1, 0), both Whitespace subroutines broken, wallet 15 →
  12, both-breaks pool trajectory 5 → 3 → 2 → 1 → 0). Along with them: the
  two loose citations the reviewer noted ("7.5.3 (breach start, candidate
  pool per server kind)" → 7.4.1/7.5.1/7.5.3-as-HQ-R&D-limit; "7.2
  accessed-card visibility" → 7.1.2, plus 7.3.1a/4.4.6c) and SEVEN stale
  test names the doc still referenced after the U3/U5/U6 renames
  (`…ChargesCostOnlyAndSeedsRunCredits`,
  `RunEventTests.UnregisteredEventSourceIsRejected`,
  `…QuantitiesAndPassForRigBreaker`,
  `FullEncounterFlowBreaksAndEndsTheRunWithCleanup`,
  `ArchivesBreachKeepsDiscardFacedownAndAccessesEveryCard`,
  `ArchivesAgendaBreachAccessStillForcesSteal`,
  `PassingTheMidAccessWindowCannotSkipTheAgendaSteal`). One test comment
  in `BreachAccessTests` now records honestly that the 7.5.2
  Archives-discard faceup flip at breach start is NOT modeled: facedown
  discard cards are reachable only through scenario seeding because the
  only engine trash path enters the discard faceup (a documented residual,
  outside the review's scope). No engine change, no re-pinning; after the
  fix: build 0 warnings / 0 errors, 338 tests green, `dotnet format`
  clean, `m2-proof --json` exit 0 with all pins unchanged.
- **U9 (implemented)** — Archives breaches include their root-zone cards
  as candidates (CR 26.03 7.4.1a), closing the last third of the review P1
  "central-server roots are omitted from breaches" (HQ/R&D were done in
  U4; the review's required mixed tests were "HQ root plus hand, R&D root
  plus top deck card, Archives root plus discard"). Verified against the
  published CR text: 7.4.1a makes every card in the root of the breached
  server a candidate for EVERY server kind, listed before the central-zone
  candidates (7.4.1d: the Archives discard), and 3.6.1 allows upgrades in
  the root of any central server, so an Archives root can hold cards. The
  U4 code comment claimed "the root cards are candidates for every server
  kind" while the `ServerKind.Archives` branch built the candidate list
  from the discard alone; `StepBreachBoundary`
  (`src/Netrunner.Core/GameEngine.Run.cs`) now builds the Archives
  candidates as [root-zone card ids..., Corp discard card ids...] (root
  first), and the comment now matches the implementation for all four
  server kinds (Remote's pool IS its root zone — not double-added; HQ/R&D
  and the hidden-source `randomAccessLimit` mechanism are untouched;
  `GetBreachHiddenZone` returns null for Archives, so all Archives
  candidates stay explicit with `Source == null`). No observation change
  needed: facedown root candidates render anonymously to the Runner before
  selection and are revealed to both viewers on selection through the U2
  access-visible set, exactly as U4's HQ/R&D roots. `BreachStarted.CandidateCount`
  keeps its explicit-pool semantics (the root card counts). Tests (340
  green, was 338): `BreachAccessTests.ArchivesBreachOffersRootCardsFirstThenDiscardAccess`
  (candidate order [root, discardFacedown, discardFaceup], CandidateCount
  3 / RandomAccessLimit 0, facedown root asset anonymous to the Runner /
  visible to the Corp before selection, visible to BOTH viewers on
  selection while staying facedown in the authoritative state (7.3.1a),
  discard visibility semantics unchanged, full breach to run end, root
  card back in the Archives root facedown, facedown discard card
  Corp-private via faceDownArchives at terminal per 4.4.6c) and
  `BreachAccessTests.ArchivesBreachWithRootCardsPassesRunTimingTraceValidation`
  (full Archives root-breach event stream, including a mid-breach trash,
  passes the run-timing trace validator); the `CreateBreachState` fixture
  gained `archivesRoot`/`archivesRootDefinitions` parameters mirroring the
  U4 `hqRoot`/`rndRoot` pattern. The existing
  `ArchivesBreachKeepsDiscardFacedownAndAccessesEveryCard` and
  `ArchivesBreachFacedownDiscardVisibleToBothViewersOnlyDuringTheBreach`
  are unchanged and green (their fixtures have empty Archives roots). Curated
  proof unchanged (remote-only breach, empty central roots): initial
  `4733c8f84f1f054c6c1277e07d6450173cfb0d4741d11466cf8997cfd5ea4ac6`,
  final state
  `73ae535ab4f7c1758434b23e51569b697291dd4bfa546edef6c6f8e5ee37e5d9`,
  event stream
  `14627034b25694c92aab5224945367468da5e54fa8e737204adac9412ff05f57`,
  32 checkpoints, both policies byte-identical, replay verified, trace
  problems empty. Files: `src/Netrunner.Core/GameEngine.Run.cs`,
  `tests/Netrunner.Core.Tests/BreachAccessTests.cs`,
  `docs/M2_PROGRESS.md`, `docs/M2_TIMING_SCENARIOS.md`.

## Second-pass M3-readiness corrections (implemented and verified)

The first-pass U1-U9 entries above are retained as historical review records.
The following corrections supersede any earlier statement that described an
implementation detail now fixed:

- Archives breach start (CR 7.5.2) turns every CorpDiscard card faceup in
  authoritative state permanently. Archives root cards remain unrevealed until
  selected; discard visibility is no longer temporary observation state.
- BreakLimit is applied independently to each icebreaker interface-ability
  activation. The retired breaker-usage ledger and type are absent from
  authoritative and serialized encounter state.
- EndRunIfCreditsAtMost reads only the target player's wallet/credit pool.
  Hosted Overclock credits are a separate spend source and never satisfy the
  CR 1.13.3 threshold; credit-loss effects remain wallet-only.
- A played run event is moved faceup from RunnerGrip to a lazily created,
  public RunnerPlayArea for the nested run, then moved faceup to RunnerHeap at
  cleanup. Runner heap entry normalization covers damage and other card moves.
  Event placement/payment and RunEventPlayed precede the nested RunInitiated
  trace event.
- Every CardAccessed segment now requires exactly one BreachAccessAbility
  window, including no-action accesses; access effects are bounded to and
  checked against that segment's CardInstanceId.
- Pump, break, and accessed-card trash actions materialize each legal
  wallet/hosted-credit division under CR 1.10.3c in serialized parameters.
  Reducer and invariant validation apply the chosen split atomically, keeping
  save/load, replay, and deterministic policy behavior stable.
- M2 scenario validation now requires a run source to be a registered
  RunnerGrip run event. Compact pre-run scenarios cannot populate the active
  RunnerPlayArea.

Verification on 2026-08-30: `./scripts/verify.sh` passed the full solution
build and 347 tests (Core 251, Cards 43, AI 9, CLI 44), the M1 proof paths,
the Godot smoke check, and the curated M2 proof. The curated proof remains
deterministic across both policies with save/load and replay verified:
initial state `4733c8f84f1f054c6c1277e07d6450173cfb0d4741d11466cf8997cfd5ea4ac6`,
final state `c822e9790dcad48ccdb52f42539be678943fc109820ea2c7e20e5f8fa03805ac`,
and event stream
`d20cdef94e97ac928ca088e14952b65d2116fecda00485fff5c3b05e1b6e9e20`.
The M1 default topology remains unchanged because RunnerPlayArea is lazy.
