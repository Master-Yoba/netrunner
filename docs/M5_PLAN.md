# M5 implementation plan — complete offline solo mode

Status: M5.0–5.8 implemented and [audited](M5_COMPLETION_AUDIT.md); human
acceptance remains open. Results are tracked in [progress and evidence](M5_PROGRESS.md)
and [the gate report](M5_GATE.md).

Baseline: 22 September 2026, repository commit `8f27bac`.

Scope: System Gateway, the repository's pinned rules/content versions, both seats.

Estimate: 32–41 engineering days, approximately 6–9 weeks for one engineer,
plus player review and any newly discovered rules defects.

This expands [M5 in the roadmap](IMPLEMENTATION_PLAN.md#m5--complete-offline-solo-experience).
Requirements come from [PRD v0.2](netrunner_solo_prd_v0.2.md). The implementation
must preserve the completed board interactions, condition/cost feedback, local
artwork support, and paced opponent playback.

## 1. Intended result and scope

A player can start a Gateway game as either side, choose an offline difficulty,
play against an opponent that makes understandable tactical choices, save at
any stable decision, close the application, and resume the same game without
internet access. A developer can reproduce an agent decision, run a pinned
benchmark, and turn a reported failure into a regression fixture.

Deliver these roadmap items:

| Item | M5 implementation |
|---|---|
| AI-501 | Typed observation reader, features, public card evaluation metadata, and information-boundary tests |
| AI-502 | Corp utility policy, including economy, protection, scoring and replacement discipline |
| AI-503 | Runner utility policy, including rig development, run risk, encounters and access |
| AI-504 | Reproducible policy randomness, Easy/Standard profiles and opt-in diagnostics |
| AI-505 | Tactical fixtures, batch simulation, stall classification and versioned reports |
| SAVE-501 | Versioned session saves, atomic storage, compatibility checks and resume browser |
| DEV-501 | General snapshot loading and bounded developer step/fast-forward/reveal controls |
| MATCH-501 | Offline opponent selection, difficulty, seed, resume and complete outcome flow |

M5 does not add LLM/hybrid transport, broad game-tree search, a deckbuilder,
additional sets, a graphical replay browser, cloud saves, or release installers.
It does not replace the interaction lab or redesign the board again. M6 and RC
retain their existing scope. Small UI changes needed for saving, difficulty and
clear opponent feedback belong here.

### Entry conditions

- Keep the M4 rules/content gate and current UI regression suite green.
- Reconcile the older manual-acceptance entries in
  [M4.9 progress](M4_9_UI_PROGRESS.md#required-player-acceptance-before-m5) and
  [M4.10 progress](M4_10_UI_PROGRESS.md#outstanding-acceptance) with subsequent
  player feedback. Record what was actually accepted and any remaining defects;
  automated smoke tests do not establish manual acceptance.
- Planning and benchmark specification can proceed now. Before implementation
  is declared ready, explicitly record the status of those historical gates;
  do not invent a completed two-seat playtest from general positive feedback.
- Use the currently pinned rules and card data. Updating those pins is a
  separate rules/content change, not a prerequisite to this milestone.

## 2. What already exists

| Area | Current implementation | Work remaining |
|---|---|---|
| Rules and observations | Core owns state, issued actions, serialized resolution, RNG, observations and replay. `GameSnapshot` is schema 17. | Add only narrowly justified, viewer-safe evaluation facts; preserve existing serialization contracts unless explicitly versioned. |
| Offline agent | `FullGameContractAgent` v6 covers Gateway decision classes with typed priorities and limited utility adjustments. | Add a separate strategic heuristic agent. Keep the contract agent stable for existing proofs and baseline comparisons. |
| Agent boundary | `IGameAgent.ChooseActionAsync(Observation, CancellationToken)` returns an issued `ActionId`. AI references Core only. | Typed features, optional observation/checkpoint lifecycle, deterministic configuration and diagnostics. |
| Full games | `FullMatchRunner` accepts injected agents, validates state after each action, and verifies replay. Existing fixtures specify exact outcomes. | Batch scheduling, tactical assertions, progress metrics, aggregate reports and reproducible failures. |
| Session recovery | `SessionTrace` records durable intents/results; CLI and Godot can reopen a verified trace paused. | Product saves and browsing, policy-state restoration, backup recovery, and saving while an agent is active. |
| Match worker | `MatchCoordinator` owns state and drives agents asynchronously. Its inner agent loop can run across multiple decisions before reading another command. | A bounded decision pump so pause/save/stop commands can intervene between accepted actions. |
| New game | Both official presets/seats, explicit seed, setup/mulligan and outcome/new-match flows exist. Factory constructs a contract agent. | Choose the new offline policy/profile without changing preset validation or setup rules. |
| Presentation | Filtered history, board actions, local art, cost cues and ordered animation playback exist. | Save/resume lifecycle must reset transient UI safely and retain readable opponent playback. |
| Developer tooling | Scenario/replay CLI, session recovery, synthetic interaction lab and full-match smokes exist. | Load arbitrary validated Gateway snapshots and control real engine progression in a developer session. |

Primary integration points:

- [Agent contract](../src/Netrunner.Core/Domain.cs),
  [observation projection](../src/Netrunner.Core/ObservationBuilder.cs),
  [typed actions](../src/Netrunner.Core/ActionContracts.cs).
- [Contract agent](../src/Netrunner.AI/FullGameContractAgent.cs),
  [runtime card metadata](../src/Netrunner.Core/RuntimeCardCatalog.cs).
- [Coordinator](../src/Netrunner.Match/MatchCoordinator.cs),
  [factory](../src/Netrunner.Match/FullMatchFactory.cs),
  [match runner](../src/Netrunner.Match/FullMatchRunner.cs),
  [session trace](../src/Netrunner.Match/SessionTrace.cs).
- [Simulation fixtures](../src/Netrunner.Match/SimulationFixtureContracts.cs),
  [CLI full matches](../src/Netrunner.Cli/CliCommands.FullMatch.cs),
  [recovery workflow](SESSION_DIAGNOSTICS.md).
- [Setup scene](../src/Netrunner.Godot/scenes/components/MatchSetup.tscn),
  [settings scene](../src/Netrunner.Godot/scenes/components/SettingsMenu.tscn),
  [client playback](../src/Netrunner.Godot/scenes/Main.CardTable.cs).

Existing full-match serialization proves that Core can resume nested decisions.
It does not prove that a user save includes the correct bot memory/configuration,
survives an interrupted write, or restores the client without stale controls.
Similarly, completing contract-agent games is not evidence of useful AI strength.

For artwork, follow the implemented [local-cache workflow](CARD_ARTWORK.md),
which supersedes the original roadmap's text-only assumption. M5 preserves
optional cache fetch/pack/unpack as explicit tools; starting or resuming a game
never downloads anything.

## 3. Architecture and non-negotiable boundaries

### 3.1 Project ownership

| Project | New responsibilities |
|---|---|
| Core | Observation-safe contract additions when needed; typed public card descriptors; authoritative validation and unchanged action application |
| Cards | Construct immutable evaluation descriptors from compiled Gateway behavior and metadata; no agent access to live cards or decks |
| AI | Typed observation decoding, feature extraction, tactics, Corp/Runner scoring, profile validation, policy state and explanations |
| Match | Agent composition/lifecycle, filtered observation delivery, committed checkpoints, saves, benchmark orchestration and developer session control |
| CLI | Benchmark, snapshot and save-validation commands; machine-readable reports and exit codes |
| Godot | Opponent/profile controls, save/resume browser, progress/error feedback and developer-only UI |

Do not add a Match or Godot dependency to AI. Extract a small provider-neutral
contract into Core when it is genuinely shared; do not move presentation models
into the agent. Static card definitions are public knowledge; actual opponent
deck composition, order, hidden instances and privileged state are not inputs.

### 3.2 Agent input and typed features

Keep `IGameAgent` as the action-selection contract. Introduce an internal AI
`ObservationReader` that validates the dictionary-shaped observation once and
produces immutable typed values. Handle native values and serialized JSON values
consistently. Missing optional data means unknown/not applicable, not zero.

Features cover:

- Own hand and permitted private cards, resources, effective hand size, memory,
  tags, agenda points, installed cards/counters and used abilities.
- Public opponent resources, board layout, ICE ordering, visible card identities,
  concealed slot counts, public scores/discards and observed turn facts.
- Current run/encounter/access facts, effective strengths, broken/resolved
  subroutines, run credits and live legal payment/target alternatives.
- Typed decision and action parameters, including engine-frozen cost quotes,
  install replacement choices, targets and additional costs.

Never parse action descriptions, prompt wording, artwork, localized UI labels,
or card rules prose to determine behavior. Do not use `GameState`,
`MatchPresentationContext`, session traces, or a full-state simulation as an AI
shortcut. Preserve the current observation JSON shape where possible;
`Observation.Decision` is currently excluded from JSON, so offline decision
fixtures must rebuild it through Core or supply an explicitly versioned,
viewer-owned decision envelope.

Audit each desired feature against existing observations in M5.1. If, for
example, evaluating a known ICE requires printed subroutine semantics absent
from `CardRuntimeSpec`, add an immutable definition-level descriptor through a
Core interface implemented by Cards. Include the relevant printed effects,
strength, breaker compatibility and cost/benefit categories. Dynamic costs and
permissions still come from issued actions or viewer-safe Core projections.
The descriptor is not a second rules engine. A capability inventory must cover
all 77 designs and explicitly mark approximate evaluation; unknown features get
a conservative utility fallback, not an invented exact result.

### 3.3 Knowledge and information safety

Start with a small explicit knowledge model: remembered reveals, observed
install/advance/rez/run history, and broad risk estimates for concealed slots.
Use only information delivered in that side's observations. Do not infer a
hidden identity from instance IDs, allocator gaps, a selected opponent preset,
the game seed, save contents or authoritative event payloads.

Match should deliver an optional, read-only observation update to each agent
after each accepted action, including when it does not own a decision. This
allows a bot to remember a reveal that occurs between its turns. Those updates
carry no opponent legal actions and never invoke action selection. Keep existing
agents compatible through an optional lifecycle interface. Serialize memory in
the policy checkpoint; no external caches or process-global memory.

Invalidate precise location beliefs on shuffle/randomization and loss of
trackable identity. Remembering that a card was seen does not imply knowing its
current position. Derive priors from the supported card pool and observed facts,
not the opponent's actual hidden deck list. M5 need not implement full Bayesian
inference.

Paired hidden-state tests must change hidden cards/deck order while preserving
the same observation/history and issued options. Assert identical features,
scores, chosen semantic action and next policy state. Repeat for concealed
Archives, unrezzed ICE, private searches, access candidates and both seats.

### 3.4 Deterministic policy and difficulty

Add versioned `OfflineAgentConfiguration` and `OfflineAgentCheckpoint` contracts.
Configuration includes side, agent/policy/feature versions, profile ID,
parameter-set hash, explicit policy seed and deterministic evaluation budget.
Checkpoint includes the separate policy RNG state, committed decision count,
knowledge state and any current multi-step tactical objective.

Use serialized `DeterministicRandom` for policy stochasticity. Keep its stream
separate from the engine's shuffle/damage/access stream; never give the agent
`GameState.RandomState` or access to engine random draws. Match establishes and
records policy seeds. A displayed match seed can deterministically derive
domain-separated Corp/Runner policy seeds in Match; only the derived policy
seed reaches that agent. No `Random.Shared`, wall-clock seeding or hash-code
randomization.

Evaluate against a copy of the committed policy checkpoint. Commit the next
checkpoint only when the selected issued action is accepted. Cancellation,
rejection, retry and stale responses must not consume policy randomness or
change memory twice. Observation updates also need an accepted-action sequence
cursor in the persisted lifecycle so resume cannot apply them twice.

Use integer/fixed-point utility components and explicit ordinal tie ordering.
Only sample within a bounded set of tactically acceptable candidates. Sorting,
diagnostic logging, worker count and animation speed must not change decisions.
Bound tactical work by a deterministic operation count; wall-clock watchdogs
report failure rather than selecting a hardware-dependent alternative.

Ship two profiles initially:

| Profile | Behavior |
|---|---|
| Easy | Lower planning ambition, wider near-equal choice variation and simpler reserves; still obeys legality and critical survival/win tactics |
| Standard | Full M5 feature set, stronger economy/run/scoring evaluation and narrower variation |

Keep the contract agent developer-only. Do not advertise Hard until a separate
measured improvement exists. Difficulty never grants resources or hidden facts.
Persist exact profile parameters, not only a label that may change next build.
Retain an execution seam for M6 strategic intent without implementing intents now.

## 4. Decision policy

### 4.1 Common selection pipeline

1. Validate viewer, owned decision, nonempty legal actions and supported shapes.
2. Decode features once; match only permitted known card IDs to static metadata.
3. Group semantically equivalent actions that differ only in payment allocation.
   Preserve every materially different target, replacement and additional cost.
4. Apply exact tactical priorities where supported: a known immediate win,
   avoiding a provable immediate loss, mandatory resolution, or preserving the
   only affordable answer to a visible threat. Approximate beliefs remain scores.
5. Score remaining actions with named components and policy parameters.
6. Choose using deterministic ordering/seeded near-tie variation; return an
   existing `ActionId`. Recompute from the next observation after acceptance.
7. Optionally record candidate scores/reasons and the decision/checkpoint hashes.

No policy rule can authorize a missing action. A legal fallback for an
unmodelled strategic feature must be recorded and measured. An unsupported or
malformed action contract is a diagnostic failure, not a silent production
switch to a different bot. Keep policy families in separate files rather than
extending the existing 1,700-line contract agent.

### 4.2 Corp policy

| Decision | Expected evaluation and first regression cases |
|---|---|
| Mulligan | Credit generation, affordable ICE and usable opening composition; avoid treating every hand as an automatic keep |
| Economy | Immediate net credits, clicks spent, recurring payout timing, remaining hosted counters and rez reserves |
| Install/destination | Central exposure, agenda density in own HQ, scoring remote value, ICE install surcharge and usable protection |
| Asset/agenda replacement | Deduct the lost installed card's remaining value; keep an active Nico Campaign when another destination is preferable; replace an exhausted/low-value asset when justified |
| Advancement/scoring | Effective requirement, credits/clicks needed, public Runner threat and scoring-window viability; take a known legal winning score |
| ICE/asset/upgrade rez | Current run relevance, likely tax/end-run benefit, future reserves and whether an asset will repay its cost; avoid gratuitous rez spending |
| Damage/tags | Typed immediate payoff, public grip size, prevention uncertainty, tagged-resource targets and follow-up affordability |
| Purge/abilities | Virus impact versus full-turn opportunity cost; bounded useful activations and conditional timing |
| Discard/choices | Keep scoring/economy/protection balance; minimize visible replacement/additional-cost loss; sensible ordering of beneficial triggers |

Use broad strategic weights rather than an install-before-economy priority list.
Support seeded variation between similarly valuable remotes/rez choices, without
calling uncertainty a guaranteed bluff payoff. Avoid loops of installing and
replacing cards solely because those actions are legal.

### 4.3 Runner policy

| Decision | Expected evaluation and first regression cases |
|---|---|
| Mulligan/economy | Early economy, draw needs, useful breaker coverage and installation budget |
| Rig development | Coverage of known ICE types, effective install discounts, memory/replacement costs, unique/console constraints and near-term usefulness |
| Run target | Public agenda threat, central pressure, access modifiers, successful-run rewards, visible protection and reserve for access/trash |
| Unknown ICE | Explicit uncertainty/risk penalty; do not treat hidden protection as free or silently recover its identity |
| Encounter | Matching breakers only; effective strength, pumps, break quantities, selected subroutines, bioroid clicks and supported bypass/virus alternatives |
| Continue/jack out | Remaining visible costs and consequences, grip/tag/damage exposure and win potential; avoid needless expenditure on a run that cannot proceed |
| Access | Take available winning steals, evaluate accessed trash against future value and reserves, order eligible accesses using permitted knowledge |
| Tags/abilities/choices | Remove dangerous tags, use useful paid/triggered effects, manage hosted counters and choose least-cost forced losses |
| Discard/memory cleanup | Retain useful coverage/economy; remove redundant or exhausted programs/resources before unique answers |

Re-evaluate discounted installs from fresh issued cost quotes. Carmen's discount
and first-program discounts must expire in the policy when they expire in Core.
Do not derive an action's actual cost from its printed cost alone.

### 4.4 Encounter and payment planning

One-ply strategic selection still needs a bounded local cost solver for pumping
and breaking. Compare feasible plans over the currently known encounter using
typed strengths, compatible breakers, break quantities, subroutine effects and
legal payment sources. This is a tactical resource calculation, not a search
over authoritative hidden game states.

Model clicks, credits, expiring run credits, per-activation modifiers, virus
counters, persistent/encounter boosts and disposable breakers separately. A plan
must reserve the cost of its subsequent breaks; pumping until broke is not a
valid success. Plan only where metadata permits a supported estimate. Return one
currently issued action, retain at most an objective, and rebuild after every
accepted action. Invalidate on decision/encounter changes, altered strength,
targets leaving play, new costs, or unavailable sources.

Prefer expiring run credits, then eligible hosted sources, then the personal
pool when otherwise equivalent. In current contracts `HostedCredits` on run
payments represents the run-credit pool, including Overclock; ordinary card
counters are available only through their specific effects. Never treat all
hosted counters as generic money. Preserve materially different consequences
and source restrictions. Share a pure typed payment-preference helper if useful;
AI must not depend on Match's `LegalActionViewModel` or rewrite Core payments.

Compare plans deterministically by survival/win requirements, then opportunity
cost and stable tie keys. Test mixed ICE types, multiple breakers/subroutines,
insufficient funds, bioroid clicks, discounts, temporary credits, Mayfly and
hosted virus effects. If the solver reaches its deterministic budget, use a
documented conservative estimate and mark it in diagnostics.

### 4.5 Follow-up prompts and diagnostics

Cover every reachable `DecisionKind`: setup, turn timing/action/discard,
install choices, memory cleanup, run timing, trigger selection/ordering,
interrupts, behavior choices, ICE subroutine choices, and number/card choices.
Compound choices need the same objective through select/confirm/cancel stages;
do not independently choose the first legal target and then cancel forever.

Opt-in decision records contain versions/profile, side, decision kind/ID,
observation digest, policy checkpoint digest, candidate utilities/reasons,
selected action and evaluation work/time. Full records may contain that bot's
private hand and strategic reasoning, so keep them in developer diagnostics,
outside normal history/tooltips and off by default. Public last-action feedback
continues to describe accepted actions, not private strategy. A local failure
bundle can contain authoritative recovery state and must be clearly identified
as a private debug artifact, with no automatic upload.

## 5. Benchmark and reproducibility design

### 5.1 Tactical suite

Create a versioned manifest under `data/m5/benchmarks/`. Begin with at least
80 independent positions, at least 40 per side, spanning economy, development,
run/scoring decisions, encounters, access, loss prevention, nested choices and
replacement discipline. Seed variants do not count as independent positions.

Each fixture declares ID/category/side, content and behavior pins, snapshot or
deterministic scenario builder, policy profile/seed, expected semantic action
set or bounded outcome predicate, rationale, and whether it is critical.
Generate issued actions through Core; avoid brittle expectations on allocated
action numbers. Accept multiple equally good choices where appropriate.

Use several-decision assertions for pump/break, install confirmation, additional
costs and trigger chains. Assert outcomes such as preserving a breaker and
completing the encounter within a resource bound, not just choosing one pump.
Maintain tuning and held-out groups; freeze the evaluation manifest before
final tuning. Every player-reported AI defect adds a named regression.

Critical fixtures include immediate visible wins, affordable prevention of a
known immediate loss, matching ICE/breaker restrictions, payment-source legality
and completion of compulsory prompts. These require 100% success. Standard
requires at least 85% of all tactical positions **for each side independently**.
Report category scores and Easy results separately; never conceal a weak side
behind an aggregate average.

### 5.2 Batch games and comparisons

Extend the orchestration behind `FullMatchRunner`; retain its current exact
fixture/result API and replay proofs. Add a separate batch configuration/result
contract for runs where the winner is not predeclared. Do not turn existing
`SimulationFixtureDocument.ExpectedOutcome` into a tuning-dependent assertion.

Pin a 1,000-game Standard-vs-Standard suite, balanced across the four supported
basic/expanded Corp/Runner preset pairings. Validate pairings through the current
preset composer; unsupported combinations are reported, never silently skipped.
Every row records game seed, both policy seeds/configurations, deck IDs and all
content/version hashes. Every game exercises both new policies.

Also run paired-seed candidate-vs-contract and candidate-vs-frozen-previous-policy
comparisons with the candidate in each seat. Report seat/preset-specific wins,
game length, scores, economy, installs/replacements, runs/successes, wasted pumps,
fallback frequency and decision latency. Win rate is supporting evidence, not a
substitute for legality, tactics or good play. Keep held-out seeds unchanged.

Parallel batch workers own independent match/policy state. Running the same
manifest with one or multiple workers must yield the same ordered results and
replay hashes. Sort aggregation by fixture ID; timing measurements are excluded
from deterministic comparisons. Never share a mutable agent across games.

### 5.3 Termination, failures and evidence

Distinguish normal terminal outcome, declared turn cap, action-cap failure,
repeated-progress failure, no-decision stall, illegal action, invariant failure,
agent exception, replay mismatch and watchdog timeout. Record each explicitly.

Use a progress fingerprint that ignores allocator/event/decision sequence
churn. Include public phase/run progress plus relevant state/resource changes;
watch several decisions before classifying a repeated cycle. Monotonic IDs alone
do not prove progress. Keep progress inspection in the harness, never the agent.
Initial standard limits: 100 complete Corp/Runner turn pairs and 20,000 accepted
actions, both configurable and frozen in the benchmark manifest. A global
wall-clock watchdog catches hangs but does not alter chosen actions.

OD-09 requires 1,000 games without crash/stall, all choices legal, at least 99%
terminal or explicitly diagnosed caps, and at least 85% tactical correctness.
For M5, use the stronger completion target of **at least 990 actual terminal
games out of 1,000**, at most 10 reviewed turn caps, and zero unexplained stalls,
illegal choices, invariant failures or replay mismatches. This prevents a suite
of diagnosed caps from being presented as a playable opponent. Treat an action
cap as a failure requiring investigation, not a normal match outcome.

All games retain summary, hashes and replay artifacts in ignored output storage;
failures additionally retain the last committed snapshot, configuration, recent
decisions and progress diagnostics. Full gate verifies every game's accepted
action replay, including capped prefixes. Do not silently retry failures out of
the denominator. A repaired rerun is a new report with its own build hash.

### 5.4 Commands and CI

The following are proposed commands/targets, not existing interfaces:

```sh
make ai-benchmark-smoke
make ai-benchmark
make m5-gate

# Through the existing CLI launcher/environment:
dotnet run --project src/Netrunner.Cli -- ai-benchmark data/m5/benchmarks/standard.json --output artifacts/m5 --json
dotnet run --project src/Netrunner.Cli -- save-validate /path/to/game.save.json --json
```

Define stable JSON reports and nonzero failure exit codes. Quick CI runs all
critical tactics, observation/determinism/save tests, and a fixed 20-game smoke.
An official-content scheduled/manual job runs the complete tactical and
1,000-game suite; the milestone gate requires that result. Preserve synthetic
CI without claiming it passed official Gateway benchmarks. Provision content
before testing; benchmark execution itself must be offline. Add explicit time
budgets and artifacts to CI instead of forcing the long suite into every PR.

## 6. Save/resume and coordinator lifecycle

### 6.1 Product save contract

Introduce a Match-owned `SoloSaveDocument` around the existing Core snapshot.
It must contain:

- Artifact kind/schema, save ID/name, created/updated UTC timestamps and build
  provenance. Timestamps are metadata, excluded from deterministic state hashes.
- Rules/card-data pins, behavior and runtime catalog versions/fingerprints,
  validated `MatchRequest`, human seat and seed.
- Full `GameSnapshot`, accepted-action sequence and canonical state hash,
  including pending decision, resolution frames, RNG, allocators and run state.
- Exact offline agent configuration/checkpoint for each automated seat.
- A self-contained replay prefix from the match's initial snapshot, with hashes,
  sufficient to reconstruct viewer-filtered history and reproduce the save.
  Do not depend on a diagnostics directory that may be deleted or moved.
- Integrity hashes covering the session payload, with engine-state and
  policy-state hashes separately identified. Integrity is corruption detection,
  not protection against a local user editing their own save.

Exclude Godot nodes/tweens, selected/hovered cards, animation queues, tasks,
absolute artwork paths, artwork files, credentials and remote-provider secrets.
Saves necessarily contain hidden game state. Only Match/storage reads that
payload; browser previews use allowlisted metadata and the human-filtered view.

Validate replay/checkpoint endpoint agreement and sequence cursors on load.
An ordinary save resumes from its checkpoint without replaying bot thinking;
accepted-action replay remains independent of the AI implementation. Full
prefix verification can run off the UI thread with progress feedback.

### 6.2 Capture a consistent decision

Refactor the coordinator into a one-decision-at-a-time pump. It must service
pause/save/stop between committed actions instead of waiting for the entire
opponent turn. Keep one authoritative writer and at most one in-flight agent
evaluation. Agent results carry a session generation, decision ID/revision and
starting checkpoint identity. Stale completions never reach `GameEngine.Apply`.

A save request pauses advancement at the next committed boundary. If thinking
is in flight, cancel/await its bounded evaluation or invalidate its result;
discard tentative policy changes. If an action has already committed, include
both its resulting engine state and committed policy state. Never snapshot
half an Apply, replay append, knowledge update or policy RNG transition.

Capture an immutable document on the worker, then perform serialization/I/O
off the render thread. A normal Save can continue play after capture; Save and
quit waits for a successful durable write before closing. A failed write leaves
the match usable and the previous save intact. New game/load disposes or
invalidates the previous session so delayed results cannot enter the new one.

The rendered board can lag the engine during paced playback. Pause and catch
presentation up to the captured action boundary before showing a manual save
confirmation. Label the saved turn/phase clearly. Do not save a stale displayed
snapshot or serialize queued animations. Auto-saves may capture ahead of display
without interrupting it; resume always starts at a settled board boundary.

Extend `SessionTrace` with versioned policy checkpoints/accepted updates so
crash recovery remains deterministic for the new stateful agent. Its durable
record must describe the same engine/policy commit before publication. Preserve
v1 trace recovery for existing contract-agent sessions; unknown old policy
state cannot silently become a new heuristic configuration.

### 6.3 Atomic storage and compatibility

Store saves in one application-owned location shared by direct Godot launches
and `make play`, outside ephemeral launch diagnostics and the repository. Match
receives an injected storage directory for headless tests. A proposed default
is `LocalApplicationData/NetrunnerSolo/saves`, consistent with the artwork
workflow; the browser provides Open save folder.

Write to a unique temporary file in the destination directory, flush to disk,
validate/checksum, then publish using a platform-supported atomic replacement.
Retain one previously valid backup. Failed temporary/backup/replace operations
must not destroy the current save. Test first write, overwrite and recovery on
supported development OSes; do not claim arbitrary hardware-failure durability.
Reuse/extract the repository's existing atomic-write patterns rather than
maintaining three slightly different implementations.

A rebuildable browser index may cache metadata; save files remain the authority.
Reject malformed/oversized JSON, unsupported schemas, mismatched content/catalog
fingerprints, invalid state and missing policy versions before replacing the
active match. A build-commit difference alone is informational when all semantic
compatibility pins match. Initial schema support may be exact-version only;
future migrations must be explicit, tested and preserve the original file.

One corrupt save must not break browsing the rest. Offer the verified backup
when the primary fails, showing its saved time. Never silently substitute the
newest policy or download missing content. Missing artwork uses existing card
fallbacks and is not a save incompatibility.

### 6.4 Player flow

- Setup exposes Offline bot and Easy/Standard, with Standard the default. Keep
  existing Corp/Runner preset selectors and validated seed entry. Generate and
  show a seed when requested; record it before setup.
- Add Resume to the startup/setup flow. List save name, human side, presets,
  difficulty, turn, phase, timestamp and compatibility status; no hidden cards.
- Put Save game, Load game and Save and quit in the existing cog menu. Do not
  add a second toolbar/action dock or obscure server targets. Escape closes the
  topmost dialog and returns focus predictably.
- Loading restores the board and pending prompt paused. Continue explicitly
  resumes an opponent-owned decision, consistent with recovered sessions. Clear
  old tooltips, selection, contextual panels and publication queues before render.
- Keep a rolling auto-save at setup completion, turn boundaries, and successful
  manual saves. Coalesce writes; retain a previous generation. Crash recovery
  continues to provide more granular diagnostics between auto-save points.
- Outcome shows winner/reason and provides New game and return to setup/resume.
  Terminal saves load as terminal; no agent resumes after the game is over.

### 6.5 Required save matrix

| Boundary | Required proof |
|---|---|
| Setup/mulligan, both seats | Same remaining choices, decks and RNG; no second shuffle or repeated setup trigger |
| Ordinary turn, both seats | Same state hash, actions, policy checkpoint and subsequent choices |
| Agent thinking / animation backlog | Exactly one committed boundary; no duplicate action or lost policy RNG update |
| Install replacement, memory cleanup, forced discard | Selected costs/targets and remaining mandatory choices survive |
| Trigger ordering, prevention/replacement, paid targeted ability | Resolution stack and partial selections resume without repeating effects |
| Approach, encounter pump/break, access/trash | Effective strength, broken subs, current access and eligible payment pools survive |
| Run credits, discounts, hosted virus counters | Pools, expiry, conditional costs and counter effects remain identical |
| Terminal outcome | Winner/reason retained; no further action or animation replay |
| Corrupt/incompatible/interrupted write | Clear error or verified backup; current game and previous save preserved |

For each meaningful engine boundary, compare uninterrupted and save/load
continuations over the same human submissions: engine/event hashes, accepted
actions, policy checkpoints and filtered history must match. Include repeated
save/load, cancellation during evaluation, background write failure, and process
termination during overwrite. Prove resumption without the original trace folder.

## 7. Developer tools

Keep three concepts separate: interaction-lab teaching fixtures, real engine
snapshots, and reproducible full-match benchmarks. A synthetic lab state is not
a valid saved game or evidence of rules/AI behavior.

Extend CLI scenario tooling to export/load a full `GameSnapshot` with matching
catalogs and validated invariants. Permit arbitrary legal nested/run states;
do not force snapshots back through new-game setup. Integrate a developer
session with pause, one accepted-action step, and bounded fast-forward to the
next human decision, chosen turn, outcome or cap.

Reveal is a developer presentation mode only. It must never swap an AI's
observation builder for an omniscient one. Resource grants/state edits, if
included, construct a validated new scenario branch through Core tooling while
paused; they are not production actions. Mark edited sessions and start a new
replay root rather than claiming the edited state follows the original actions.

Developer controls require an explicit development build/launch mode and are
absent from release UI. Add a release-mode smoke that verifies they cannot be
enabled by a saved setting alone. Exported diagnostics remain local-only.

## 8. Delivery slices and dependencies

Each slice ends in a scoped local checkpoint commit, focused tests and a progress
entry. Follow the active repository workflow for branches/reviews; do not bundle
AI tuning, a new save format and unrelated UI cleanup into one implementation.
The names below are proposed files/components, not claims that they exist.

| Slice | Estimate | Deliverables and likely files | Depends on | Exit evidence |
|---|---:|---|---|---|
| M5.0 — Baseline and cases | 2 days | `docs/M5_PROGRESS.md`, initial benchmark manifest, capability/decision inventory, acceptance reconciliation | Current M4/UI baseline | Contract-agent baseline report, named failures, pinned content/seeds and approved scope recorded |
| M5.1 — Agent foundation | 3–4 days | AI `ObservationReader`, `AgentFeatures`, configuration/checkpoint contracts, optional lifecycle; Core/Cards descriptor adapter | M5.0 | Typed observations and hidden-state twins pass; seeded selection/retry/cancel restore exactly; existing agents unchanged |
| M5.2 — Benchmark harness | 3–4 days | Match benchmark runner/results; CLI `ai-benchmark`; tactical fixture builders, progress detector, report schema | M5.1 | Same manifest produces identical results at different worker counts; intentional illegal/stall/cap fixtures classify correctly |
| M5.3 — Corp policy | 5–6 days | `CorpHeuristicPolicy`, economy/server/scoring/choice evaluators; Corp fixtures | M5.1–2 | Corp tactical target met, critical fixtures all pass, Nico replacement regression fixed, legal full games |
| M5.4 — Runner policy | 6–7 days | `RunnerHeuristicPolicy`, run/encounter cost solver, payment preferences; Runner fixtures | M5.1–2 | Runner tactical target met, pump/break/payment chains complete, no mismatched breaker or stale discount decisions |
| M5.5 — Session persistence | 4–5 days | Coordinator decision pump, save/checkpoint contracts, storage, trace extension, CLI validation | M5.1; integrate M5.3–4 checkpoints | Save matrix passes headlessly, writes recover safely, bot decisions match uninterrupted continuation |
| M5.6 — Offline client flow | 3–4 days | Agent factory selection, setup controls, reusable save-browser scene, cog actions, resume/outcome integration | M5.3–5 | Native games in both seats save/restart/resume; prompts, history and animations remain clear at both resolutions |
| M5.7 — Developer workflow | 2–3 days | Snapshot import/export, step/fast-forward/reveal controls and release guard | M5.2, M5.5 | A reported fixture can be reopened, stepped, exported and replayed; release UI excludes developer controls |
| M5.8 — Tuning and gate | 4–6 days | Freeze profiles, held-out suite, scheduled CI, offline evidence, final progress/gate docs | All prior slices | Complete exit checklist below, documented evidence and player acceptance |

For a single engineer, run these in table order; capture persistence contracts
in M5.1 so policies do not acquire unsaveable state. M5.5 can be developed after
M5.2 if saving is the next product priority. Corp/Runner implementation and
persistence have separable code seams, but no staffing overlap is assumed in
the estimate. Reserve additional time for genuine rules defects found by the
new bots, with a minimal Core/Cards reproducer before changing heuristics.

## 9. Verification and milestone exit

### Automated checks

- Existing `make verify` and provisioned M4 gate remain green; format/analyzers,
  replay goldens and 77-design coverage remain current.
- AI unit tests cover all decision families, utility features, deterministic
  tie handling, cancellation and known/unknown distinctions. Core tests prove
  any new observation field is filtered correctly. No agent tests need Godot.
- Hidden-state twins pass with identical policy state and outputs; player-facing
  diagnostics, save previews and public history do not leak opponent private
  information. Opt-in private developer artifacts remain separate.
- Standard passes every critical tactic and at least 85% of tactics in each
  seat; final held-out scores are reported separately from tuning results.
- The 1,000-game gate meets section 5's legality/completion thresholds, with
  deterministic reruns and replay verification. Easy also completes a pinned
  100-game legality/stall suite; strength is not inferred from its name.
- Save/resume and crash-write matrix passes with matching engine/policy state,
  including opponent-owned and nested decisions. Legacy trace tests still pass.
- Godot smoke covers new-game selection, save/browser errors, paused resume,
  terminal load, focus/Escape, stale async results and ordered playback. Reuse
  current board-interaction smoke checks rather than replacing them.

### Performance and offline proof

Measure agent time separately from playback delays, storage flushes and renderer
time. Initial target on a recorded development desktop: ordinary decisions
p95 at most 50 ms, tactical decisions p95 at most 200 ms, responsive cancellation,
and no main-thread policy or save-file work. Record CPU/OS/build and action-set
sizes. If profiling invalidates these budgets, document a measured revision
before the gate rather than silently dropping slow decisions. Preserve the
60-FPS UI target and the existing board-motion/idle-allocation checks.

Perform a clean-profile, network-disabled run from already-built official
content, outside the source checkout and without diagnostics history. Complete
both human-seat flows through setup, play, save, process restart, resume and
outcome. Repeat missing-art rendering, and a cache restored through the existing
portable artwork archive workflow. Neither path may fetch art or metadata.
Toolchain provisioning may happen beforehand; do not claim offline bootstrap.
User saves must work after being moved with compatible installed game content.

### Manual acceptance and evidence

Play a complete game in each human seat against Standard, exercise both preset
families, and sample Easy. Record seed, decks, profile, build, save/resume point
and feedback. Specifically review Corp economy/replacement behavior, Runner
pressure/breaker use, waiting/animation clarity, save discoverability, and resume
of a run or nested choice. Test 1080p/1440p and normal/reduced motion.

Publish a concise `docs/M5_GATE.md` and progress record containing benchmark
manifest/report hashes, actual denominators/category scores, failures and their
disposition, save-matrix results, latency measurements, offline steps and manual
acceptance. Keep full private snapshots/logs and official art outside source
control; use synthetic/no-art screenshots for committed UI evidence. Automated
opponents completing games must not be labelled human playtesting.

M5 is complete only when both bots meet the measured gate, deterministic
save/resume works through the client, the offline proof passes, and no known
critical correctness, information-leak, save-loss or interaction blocker remains.

## 10. Risks and deliberate limits

| Risk | Mitigation / decision |
|---|---|
| Printed descriptors drift from rules | Derive typed facts from compiled behavior where possible; test descriptor coverage and named cost/strength cases; exact legality remains Core-owned. |
| One-ply policy misses multi-action value | Add narrowly scoped encounter/scoring objectives and diagnostic fixtures first; add broader lookahead only for a measured named failure after profiling. |
| Bots exploit IDs or hidden metadata | No actual deck/seed/state inputs, no parsing IDs for identity, paired hidden-state tests, static-card lookup only for known definitions. |
| Apparent progress hides loops | Progress fingerprints exclude sequence churn; caps and failures remain visible in the report and replay. |
| Save captures inconsistent AI state | Tentative policy state, one authoritative commit boundary, generation-tagged results, durable checkpoint tests. |
| Playback and engine checkpoint disagree | Capture authoritative boundary, synchronize manual-save feedback, discard transient playback on load. |
| New heuristics break old goldens | Leave contract-agent proofs/version intact; opt into heuristic agents explicitly; version new benchmark expectations. |
| Tuning overfits a seed or one seat | Held-out positions/seeds, paired comparisons and independent per-seat/category results. |
| Persistence expands into migration/cloud work | Ship a local schema with explicit compatibility rejection and one verified backup; add migration only when a real prior schema needs it. |

The implementation may adjust internal type/file names. Changes to information
access, save compatibility, scope, or benchmark thresholds require a recorded
plan/progress update with the reason and replacement evidence.
