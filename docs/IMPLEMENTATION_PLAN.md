# Netrunner Solo implementation plan

Status: executable plan for PRD v0.2  
Baseline date: 27 August 2026  
Rules baseline: Null Signal Comprehensive Rules v26.03  
Initial pool: System Gateway – Remastered Edition  
Assumed staffing for estimates: one full-time engineer, with rules/playtest help

## 1. Outcome and delivery strategy

Build the game as seven thin, dependency-ordered vertical slices. Every slice
ends with a headless proof and a client demonstration. Correct rules,
reproducibility, and information safety are release constraints; visual polish
and AI strength grow only on top of those constraints.

The realistic solo-engineer range is roughly 38–54 weeks to release candidate.
That is not a commitment: card timing discoveries and playtest defect rates are
the dominant variables. Milestone gates, rather than calendar dates, control
scope progression.

| Slice | Outcome | Solo estimate | Hard exit gate |
|---|---|---:|---|
| M0 | Foundation and stack spike | 1–2 weeks | Toolchain, CI, architecture spike, measured edit loop |
| M1 | Deterministic headless toy game | 3–5 weeks | Typed actions can finish/replay a two-deck toy match |
| M2 | Representative Netrunner run | 5–7 weeks | Run/ICE/break/access path passes timing scenarios |
| M3 | Native playable micro-decks | 4–6 weeks | The same M2 match is playable through Godot |
| M4 | Complete Gateway rules/cards | 12–18 weeks | All designs implemented, reviewed, and card-tested |
| M4.9 | Card-table UI and understandable history | See [UI plan](M4_9_UI_PLAN.md) | Native layout, history/visibility and manual usability gate before M5 |
| M5 | Complete offline solo mode | 6–8 weeks | Both bots finish stress runs; save/resume works |
| M6 | Safe LLM/hybrid mode | 4–6 weeks | Intent play, constrained actions, timeout/takeover pass |
| RC | Polish, packaging, QA | 3–5 weeks | Release matrix and all PRD exit criteria pass |

M3 UX work can continue in parallel with M4 card batches once the event bridge
is stable. M5 benchmark design can start during M4, but bot tuning must not
become a substitute for missing rules behavior.

## 2. Decisions made for implementation

These resolve the PRD's open decisions unless later evidence forces an ADR.

| PRD decision | Implementation decision |
|---|---|
| OD-01 Packaging | Linux x86_64 is the first dev and distributable lane. Preserve Windows/macOS portability; activate those CI/package jobs before RC, subject to available runners/signing. |
| OD-02 Card data | Vendor a pinned commit of `Null-Signal-Games/netrunner-cards-json`. Keep source license/commit/checksums and generate a normalized Gateway-only runtime file. Updates are explicit pull requests, never runtime fetches. |
| OD-03 Resolution | Use a serializable resolution stack of typed frames plus atomic action transactions. Details are in section 3. |
| OD-04 Stack validation | Godot 4.7.2 .NET, .NET 10 LTS SDK, shared libraries targeting `net8.0`. M0 records clean/incremental build and representative test-to-observe p50/p95 over ten runs. |
| OD-05 Presets | First micro-decks are test-only. First user presets are the official System Gateway teaching/starter Corp and Runner configurations derived from the vendored data and published learn-to-play material; tutorial identities are selectable only in the teaching preset group. Add one expanded preset per side after Gateway completion. |
| OD-06 Providers | Ship one OpenAI-compatible adapter supporting local and remote base URLs. Do not add vendor-specific SDKs in the first release. |
| OD-07 Privacy | Before a remote endpoint is enabled, show endpoint host, model, exact observation categories, absence of hidden opponent state, and logging policy; require explicit confirmation. Provide a JSON preview with secrets redacted. Localhost does not require remote-data consent but uses the same preview. |
| OD-08 Art | Release text/template cards. Allow a later local user asset directory, but do not download or redistribute official art. |
| OD-09 Bot bar | Use a versioned benchmark suite: 100% legal choices; 1,000 seeded self-play games without crash/stall; at least 99% conclude or hit an explicitly diagnosed turn cap; at least 85% correct across tactical must-find scenarios; no hidden-state access. |
| OD-10 Replay UI | First release exposes save/load plus a developer action-log export and CLI replay. A graphical replay browser is post-release. |
| OD-11 Hybrid calls | A standing intent normally lasts through a turn. Fresh calls occur at turn start, after score/steal or major hidden-information reveal, and before high-impact commitments when no valid intent covers them. See section 8 for the exact policy. |

Version policy: follow Godot 4.7 patch releases only after the verification suite
passes; evaluate minor upgrades separately. Stay on an even-numbered .NET LTS
SDK. Save artifacts record actual build, rules, data, and schema versions rather
than assuming compatibility from package versions.

## 3. Target architecture

Dependency direction remains:

```text
Netrunner.Godot --> Netrunner.Core <-- Netrunner.Cards
                         ^
                         |
               Netrunner.AI / Netrunner.LLM
                         ^
                         |
                    Netrunner.Cli
```

### 3.1 Authoritative state and transactions

`GameState` is a data-only, versioned aggregate containing players, zones,
servers, card instances, turn/run state, the resolution stack, pending decision,
known-information records, counters, RNG state, and outcome. It contains stable
IDs and no Godot objects, delegates, tasks, service references, or callbacks.

The only mutation entry point is conceptually:

```csharp
ApplyResult Apply(GameState state, DecisionId decision, ActionId action)
```

The engine validates decision ownership, decision revision, and action ID. It
then resolves against a working copy, buffers events, and commits state + RNG +
events together. An exception or rejected/stale action discards the working
copy. Debug builds additionally validate invariants before commit.

### 3.2 Resolution model

Use a LIFO stack of JSON-serializable discriminated `ResolutionFrame` records.
Frames perform one bounded step, then complete, push child frames, or yield a
typed `Decision`. Examples include `PayCost`, `ChooseTargets`, `ResolveAbility`,
`OrderTriggers`, `PreventDamage`, `ApproachIce`, `EncounterIce`,
`ResolveSubroutine`, `BreachServer`, and `AccessCard`.

Do not serialize code pointers or lambdas. Frames identify a registered rule or
card behavior by stable behavior ID and store only its serializable arguments.
Unknown frame/behavior versions fail load with a useful incompatibility error.

Legal actions are materialized only for the current decision and carry a
decision revision. Multi-step input (target, amount, ordering) becomes a series
of decisions; clients never smuggle arbitrary parameters into a generic action.

### 3.3 Events, replay, and presentation

Accepted actions are the canonical replay input. Structured events are the
canonical presentation stream and diagnostic output, but are not reapplied to
reconstruct rules state. A snapshot plus subsequent accepted actions reproduces
the game. Event sequence numbers, action IDs, object IDs, seed/RNG state hash,
and state hash make divergence locatable.

Godot owns animation scheduling. It consumes immutable event batches, may
compress them, then asks the match coordinator for the latest view model. It
does not advance the engine when an animation ends.

### 3.4 Information boundary

Only `ObservationBuilder.Build(GameState, viewer)` can create agent/client
observations. The observation is a purpose-built DTO, never a serialized
`GameState`. Known-but-no-longer-visible cards are modeled explicitly so that
memory is rules-correct without leaking current hidden state.

Tests compare complete serialized observation shapes at representative states,
including unrezzed Corp cards, HQ/R&D, Runner grip/stack, facedown installs,
reveals, accesses, hosted cards, and debug-cheat mode separation.

### 3.5 Card behavior

Static card records come from normalized vendored JSON. `CardBehaviorRegistry`
maps a stable card code to compiled behavior built from engine primitives.
Common primitives—gain/lose, draw, move, install, trash, damage, tags, counters,
strength modifiers, costs, triggers, prevention, replacement, run/access
changes—are tested independently. A card may introduce a new primitive only
with engine tests and an architecture review.

## 4. Work breakdown and gates

### M0 — Foundation and technical spike

Deliverables:

- FND-001: solution/project boundaries, analyzers, nullable warnings-as-errors,
  deterministic builds, formatting, local bootstrap, and Linux CI.
- FND-002: pin Godot/.NET versions; verify Core tests do not load Godot.
- FND-003: Godot shell references Core and displays build/rules metadata.
- FND-004: event-bridge spike: Core emits a five-event batch; Godot animates it
  while input and card inspection remain responsive.
- FND-005: packaging spike for an unsigned Linux x64 build.
- FND-006: benchmark clean build, incremental Core test change, card-test
  change, and Godot observe loop. Record ten samples, p50/p95, hardware, and
  commands in `docs/spikes/godot-dotnet-loop.md`.
- FND-007: CI secret scan and license inventory; no telemetry.

Targets for the spike are p50 ≤ 3 seconds for a focused Core/card test cycle and
p50 ≤ 10 seconds from a representative client C# edit to visible restarted
scene. Exceeding a target triggers a decision: keep card behavior in fast
headless projects, reduce Godot rebuild scope, or revisit the presentation
scripting split before M3. It does not silently waive OD-04.

Gate: bootstrap on a clean Linux x64 environment, `scripts/verify.sh` green,
headless tests green, packaged shell launches, benchmark documented.

### M1 — Deterministic headless toy game

Deliverables:

- ENG-101: stable typed IDs, card instances, zones, players, Corp servers, and
  immutable public snapshots.
- ENG-102: `GameState`, invariant validator, deterministic shuffle, and RNG
  state serialization.
- ENG-103: decision revision + legal-action builder + atomic `Apply` result.
- ENG-104: serializable resolution frames and nested prompt support.
- ENG-105: structured action/event log, state hashing, replay verifier.
- DEV-101: JSON scenario builder and CLI `scenario`, `play`, `replay` commands.
- RULE-101: toy setup, turns, clicks, credits, draw, install, agenda scoring,
  agenda stealing, and deck-out victory.
- DATA-101: vendor/normalize Gateway metadata with provenance manifest.
- SLICE-101: two tiny deterministic fixture decks, explicitly not user presets.

Tests: stale/out-of-turn rejection, transaction rollback, fixed shuffle golden
sequence, nested prompt round trip, save during prompt, replay hash equality,
and malformed snapshot error.

Gate: CLI agents play the toy decks to all implemented win conditions; the same
seed/actions produce byte-equivalent canonical state and event hashes on repeat.

### M2 — Representative run and timing engine

Deliverables:

- RULE-201: run initiation, attacked-server identity, approach/movement,
  encounter, paid-ability priority, jack out, success, breach/access, run end.
- RULE-202: ICE rez, strength, ordered subroutines, break/fire, and end encounter.
- RULE-203: costs, conditional/optional triggers, simultaneous trigger ordering,
  prevention/replacement, lingering effects, checkpoints.
- RULE-204: trash/steal/no-action access decisions and central/remote access.
- RULE-205: damage, tags, memory, hand size, purge/virus counters, and victory
  primitives required by the selected representative cards.
- SCN-201: scenario fixtures for every run phase and response window.
- SLICE-201: a curated interaction using one barrier, one code gate, breakers,
  an agenda, an asset/upgrade, and a run event.

Rules review procedure for every timing scenario: cite CR rule number, current
card text/ruling where relevant, and jnet behavior as a cross-check—not copied
code or an authority replacing the CR/ruling.

Gate: the representative run completes headlessly from both scripted human and
contract agent inputs; save/load succeeds at every yielded decision; a timing
trace review finds no missing legal window.

### M3 — Godot playable micro-decks

Deliverables:

- UI-301: match coordinator on a worker/task boundary; main thread never waits
  synchronously for AI or I/O.
- UI-302: view models for centrals/remotes/ICE/root, Runner rig, hands, scoring,
  resources, phase, decision owner, and run focus.
- UI-303: reusable text-card control with zoom/details and counter overlays.
- UI-304: click-first legal action and target selection, cancel/back path, stale
  action feedback; drag is optional enhancement only.
- UI-305: event animation queue, speed controls, reduced motion, and batch skip.
- UI-306: access/encounter/paid-window focused presentations.
- UI-307: accessibility pass for non-color cues and 1280×720 through 2560×1440.
- SLICE-301: match setup screen launches the M2 micro-decks and reaches outcome.

Gate: packaged Linux build plays the M2 slice without CLI; hover/selection is
one-frame local feedback at 60 FPS; artificial 10-second AI latency does not
block menus, inspection, or input unrelated to the pending decision.

### M4 — System Gateway complete

Implement cards in primitive-driven batches, not pack-number order:

1. vanilla economy, draw, basic install/play, simple stats;
2. paid abilities and recurring credits;
3. conditional/static modifiers and additional costs;
4. run events, encounter/breaker interactions, access changes;
5. damage/tag/prevention/replacement and trash/disable interactions;
6. identities, agendas, upgrades, complex triggers, hosting, and remaining
   unique timing cases.

For each batch:

- import metadata and bind all codes;
- create the missing generic primitives first;
- add at least one positive card test per design;
- add negative, interaction, and ruling-derived tests proportional to complexity;
- run observation-leak tests for any reveal/hidden-zone behavior;
- add the cards to simulation decks only after tests pass;
- record implementation status, source rulings, and test links in a generated
  Gateway coverage report.

Additional deliverables:

- DATA-401: official teaching presets plus one expanded preset per side.
- MATCH-401: complete setup and mulligan rules.
- RULE-401: all remaining Gateway-reachable rules and win reasons.
- CARD-401: coverage validator fails CI when a Gateway design has no behavior or
  test registration.

Gate: 100% metadata/behavior/test registration for supported designs, card
review checklist complete, full headless regression green, and both sides can
finish human-vs-contract-agent games through the client.

### M5 — Complete offline solo experience

Deliverables:

- AI-501: feature extractor based only on filtered observation and legal actions.
- AI-502: Corp heuristic scoring for economy, agenda risk, server value,
  protection, scoring windows, damage/tag pressure, and bluff-compatible
  stochastic choices.
- AI-503: Runner heuristic scoring for economy, rig, known/estimated run cost,
  central pressure, remote challenge, damage/tag risk, and winning accesses.
- AI-504: deterministic seeded tie breaking, parameterized difficulty, and
  opt-in decision diagnostics.
- AI-505: benchmark scenarios, self-play runner, stall detector, and summary.
- SAVE-501: atomic local saves, resume browser, compatibility validation, and
  bot configuration without secrets.
- DEV-501: arbitrary scenario save/load and developer step/fast-forward/reveal
  controls excluded from release UI.
- MATCH-501: new game side/preset/opponent/seed selection and full outcome UI.

The bot begins as one-ply utility choice with tactical action filters. Add bounded
lookahead only after profiling shows it improves named benchmark failures. Any
belief about hidden cards is derived from observed/public information and an
explicit belief model, never authoritative state.

Gate: the OD-09 benchmark bar passes for both sides, fixed seeds reproduce
decisions, save/resume passes during normal action and representative nested/run
prompts, and an offline clean-room test completes without network access.

### M6 — Safe LLM and hybrid intent play

Deliverables:

- LLM-601: OpenAI-compatible HTTP transport with endpoint/model/API-key settings,
  cancellation, timeout, bounded retry, redacted structured logs, and no SDK
  vendor lock-in.
- LLM-602: compact observation schema, legal-action schema, read-only analysis
  tools, token/size budgets, and prompt versioning.
- LLM-603: schema/grammar response mode where supported, strict post-validation
  everywhere, and stale-action refresh.
- LLM-604: failure UX: retry, preserve state, and local-bot takeover.
- LLM-605: remote privacy preview/consent and local credential storage; no keys
  in saves/logs.
- HYB-601: intent taxonomy and deterministic intent-to-heuristic policy.
- HYB-602: standing-intent lifecycle and high-impact refresh policy below.
- HYB-603: local-inference profile: configurable think budget, concurrency one,
  frame-time monitoring, and remote escape valve documentation.

Gate: a mock endpoint proves malformed/timeout/unavailable/stale responses cannot
mutate state; recorded accepted actions replay without the model; local takeover
can finish immediately from the preserved decision; observations pass the same
leak tests as offline agents.

### RC — Productization and release QA

- Linux x64 export, reproducible build metadata, licenses/credits, save location,
  settings, crash-safe writes, and clean uninstall behavior.
- Activate Windows/macOS build smoke jobs if runners are available; do not claim
  supported packaging until manual install/play/save/resume checks pass.
- Profile representative maximal board states at 60 FPS; test low-motion and all
  animation speeds.
- Run full card/rules review, 1,000+ simulation gate, soak tests, save migration
  failures, corrupted files, endpoint failures, and offline validation.
- Verify distribution contains no restricted Null Signal art/frame/back assets
  and does not imply endorsement.
- Triage all defects: no known P0/P1 rules blockers; publish lower-severity known
  issues with affected cards/interactions.

## 5. Data vendoring workflow

Create `scripts/update-card-data.sh` in M1 with this behavior:

1. require an explicit upstream commit SHA;
2. fetch into a temporary directory;
3. validate upstream JSON using its published schema/tests where practical;
4. extract the System Gateway Remastered records needed at runtime;
5. normalize symbol markup without interpreting card rules;
6. write deterministic, title-sorted JSON plus `manifest.json` containing source
   URL, SHA, retrieval date, schema version, output SHA-256, and license path;
7. run schema, uniqueness, pack-count, stable-code, and behavior-binding tests;
8. show the diff for human card-text review.

The game reads only the checked-in normalized snapshot. Live NetrunnerDB lookup
is a developer validation aid and never a runtime or build dependency.

## 6. Save, scenario, and replay formats

Use JSON initially for inspectability, with canonical property ordering for
hashing and compressed files only after profiling. All artifacts begin with:

- artifact kind and schema version;
- build commit/version;
- rules version (`26.03` initially);
- card-data manifest SHA;
- behavior registry version;
- creation timestamp (metadata only, excluded from deterministic state hash).

Save writes use temp-file + flush + atomic rename and retain one recovery backup.
Scenario files may omit irrelevant state and are expanded/validated by a
versioned builder. Replay files contain initial snapshot/decks/seed plus accepted
actions and optional expected event/state hashes. LLM prose is optional
diagnostic metadata and never required for replay.

## 7. Test and quality gates

Every pull request runs format/analyzers, Core/Card/AI tests, observation negative
tests, fixed replay goldens, save round trips, card coverage validation, and a
short simulation suite. Nightly runs add long simulations, randomized scenario
property tests, determinism repeats, and performance trend capture. Packaging
smoke tests run on release branches.

Important invariants checked after every committed action in tests/debug builds:

- each card instance exists in exactly one legal zone/host location;
- credits/clicks/counters and agenda points remain valid;
- installed topology and ICE order are coherent;
- only one current decision owner/revision exists;
- stack and decision agree; terminal games expose no gameplay action;
- known information never upgrades to privileged current information;
- event and action sequences are monotonic;
- RNG changes only through the engine random service.

Defect severity: P0 corrupts state/save or leaks hidden information; P1 blocks a
normal match or materially violates a supported card/rule; P2 has a legal
workaround or meaningful UX fault; P3 is cosmetic/diagnostic.

## 8. Hybrid LLM decision granularity

The hybrid agent receives a fresh intent call for:

- the start of each active turn after mandatory start-of-turn effects;
- immediately after either side scores/steals an agenda;
- a material reveal that invalidates the standing intent;
- choosing among genuinely different high-impact plans (start a costly run,
  commit a scoring remote, install a likely bluff, or take a tag/damage risk)
  when the standing intent does not already decide it;
- an explicit user-requested personality/strategy reconsideration in debug mode.

The deterministic policy normally handles clicks inside a standing intent,
mandatory choices, obvious economy actions, target/order choices with a clearly
dominant tactical score, breaker math, routine access/steal, and pass/continue
windows with no meaningful strategic alternative.

Rez, jack-out, optional damage/tag ability, and trap-like response windows are
classified `bluff-sensitive`. They first consult the standing intent plus its
reserved-credit/risk parameters. A fresh model call is permitted only when the
engine's impact classifier exceeds a configured threshold and the response
budget can meet UX latency; otherwise a seeded mixed heuristic decides. There is
never an unconditional model call at every paid-ability window.

Intent is structured, not prose-only:

```text
goal, target server, horizon, credit reserve, acceptable risk,
information posture, bluff posture, expiration conditions
```

Tests use a scripted fake model to prove call counts and exact refresh points.
The expected common case is one to three calls per turn, not one per action.

## 9. Requirement traceability

| PRD group | Primary delivery | Verification |
|---|---|---|
| ENG-001–008 | M1–M2 | transaction, serialization, replay, headless tests |
| RULE-001–004 | M1–M4 | primitive/timing scenarios and rules metadata |
| CARD-001–004 | M1 data + M4 | generated coverage and ruling-linked card tests |
| INFO-001–004 | M1 boundary, M2/M4 cases | exact observation negative tests |
| MATCH-001–004 | M4–M5 | setup/mulligan/outcome/save client flows |
| UI-001–007 | M3–RC | packaged smoke, accessibility, frame-time checks |
| AI-001–005 | M5 | contract, benchmark, self-play, deterministic logs |
| LLM-001–008 | M6 | mock endpoints, schemas, privacy, fallback/takeover |
| DEV-001–004 | M1–M5 | CLI scenarios/replay/simulation and dev menu |
| DATA-001–003 | M1, M3, RC | provenance, text renderer, distribution audit |
| NFR-001–009 | all; final RC gate | perf, soak, offline, privacy, compatibility |

## 10. First executable backlog

Work in this order; each item should be independently reviewable:

1. Finish M0 CI and record the Godot/.NET iteration benchmark.
2. Add repository version stamping and a diagnostic `doctor` CLI command.
3. Define all stable ID value types and canonical JSON rules.
4. Implement zone/card/player/server state plus invariant validation.
5. Finalize `ApplyResult`, rejection types, decision revisions, and event envelope.
6. Implement transaction copy/commit and rollback tests.
7. Implement serializable resolution-frame registry and nested choice fixture.
8. Implement canonical state/event hashes and action-log envelopes.
9. Add replay CLI and determinism golden test.
10. Implement versioned scenario builder and three exact-state fixtures.
11. Add pinned card-data updater, normalized schema, and provenance manifest.
12. Implement deck creation and deterministic shuffle.
13. Implement toy setup, draw, click/credit actions, install, score/steal, outcome.
14. Add contract agents and finish M1 bot-vs-bot slice.
15. Review the M1 architecture and serialization before beginning run timing.

Do not start broad Gateway card implementation before item 15. Discovering that
resolution frames cannot serialize midway through 77 card designs is the costliest
plausible rework in the project.

## 11. Definition of done

A rules primitive or card is done only when implementation, headless positive
and negative tests, relevant timing/ruling citations, observation implications,
serialization during its prompts/effects, and coverage registration are all
complete. A UI feature is done only with click-first behavior, non-blocking
operation, reduced-motion behavior where relevant, and packaged-build smoke.
An agent feature is done only through filtered observations and legal IDs, with
fixed-seed reproducibility and local diagnostics.

No milestone is complete on a demo alone. Its automated gate must be green and
the current packaged/client surface must demonstrate the promised product slice.

## 12. Verified external baseline

Checked on 27 August 2026:

- Null Signal's rules hub still names Comprehensive Rules v26.03 as current.
- NetrunnerDB is the official source of current card text; the canonical JSON
  repository remains the chosen machine-readable vendoring source.
- Godot 4.7.2 stable was released 18 August 2026 and provides Linux .NET builds.
- Godot C# requires a separate .NET SDK and .NET-enabled editor; desktop export
  is supported, while web export is not—which is consistent with this PRD.
- .NET 10 is the current LTS line; shared projects target `net8.0` to retain the
  Godot-supported floor while building with the newer LTS SDK.

