# NETRUNNER SOLO

**Product Requirements Document**
Desktop-first solo Netrunner with deterministic rules, offline AI, and optional LLM opponents

| | |
|---|---|
| **Status** | Working draft for implementation planning |
| **Version** | 0.2 |
| **Date** | 27 August 2026 |
| **Initial content scope** | System Gateway – Remastered Edition |
| **Primary mode** | Single-player, local desktop game |

> **Purpose of this PRD**
> Define the product scope, observable behavior, quality bar, architecture constraints, and acceptance criteria tightly enough that a separate implementation plan can decompose the work into milestones, components, issues, and test plans without revisiting core product decisions.

> **Revision notes (v0.1 → v0.2)**
> Incorporated review feedback prior to implementation planning:
> - Resolved OD-02 (card data source) to a concrete source rather than leaving it open.
> - Tightened the OD-04 Godot/.NET spike to require an iteration-speed benchmark, not just structural validation.
> - Elevated LLM-006 (local/OpenAI-compatible endpoint support) from Should to Must, and added local-inference reliability and GPU-contention considerations (LLM-008, new risk row).
> - Added explicit decision-granularity scoping for hybrid mode (new OD-11) and reweighted the milestone plan toward hybrid mode as the primary vehicle for the bluffing/mind-games thesis.
> - Added rulings/FAQ and jnet cross-referencing as explicit inputs for card-behavior correctness (CARD-004, section 18).
> - Added a forward note on mixed-strategy (simultaneous secret bid) decisions for future set expansion (section 8.6).

---

# 1. Executive summary

Netrunner Solo is a desktop-first implementation of modern Null Signal Games Netrunner focused on a polished single-player experience. The player competes against a computer-controlled Corp or Runner. The game must be fully playable without network access using a deterministic offline bot; an optional tool-capable LLM agent may replace or augment that bot when a model endpoint is configured.

The first product increment is deliberately narrow: the complete System Gateway – Remastered Edition card pool and the rules needed to play it correctly. The product is not a browser client, does not include online multiplayer, and does not attempt to support the entire historical card pool. The long-term architecture must, however, make future set expansion and stronger AI practical rather than requiring a rewrite.

> **Product thesis**
> The differentiator is not merely "Netrunner on a computer." It is a responsive, tactile solo game whose rules engine is authoritative and deterministic while opponent strategy is replaceable: human input, a conventional bot, a search agent, an LLM, or a hybrid can all drive the same legal-action interface.

# 2. Product vision and principles

| Principle | Implication |
|---|---|
| Rules are authoritative | The rules engine—not the UI or AI—owns state transitions, legality, timing, randomness, and victory conditions. |
| Offline is the baseline | A complete game, including AI decisions, must work without internet connectivity or external services. |
| Hidden information is a security boundary | Each player/agent receives a filtered observation. No AI implementation receives omniscient state by convenience. |
| Presentation is decoupled from simulation | Animation may be rich and asynchronous, but it never changes game semantics or blocks engine execution. |
| Every action is reproducible | Seeded randomness, snapshots, and an event/action log must make bugs and AI decisions reproducible. |
| Expand by adding content, not exceptions | New sets should primarily add card implementations and data, not fork the engine or AI contract. |

# 3. Goals, non-goals, and success criteria

## 3.1 Goals

- Provide a complete, enjoyable solo match as either Corp or Runner using only System Gateway – Remastered Edition.
- Deliver a native-feeling, highly responsive card-game interface with fast hover, drag, target highlighting, server focus, and concise action feedback.
- Implement a deterministic, headless-capable Netrunner rules engine suitable for automated testing, replay, simulation, and multiple AI types.
- Provide a competent heuristic/offline opponent that can finish games legally and make tactically sensible choices.
- Provide an optional LLM agent integration that reasons over the same filtered observations and legal actions as any other agent.
- Create a content architecture that supports adding future Null Signal sets without redesigning the engine.

## 3.2 Non-goals for the initial release

- Online multiplayer, matchmaking, accounts, chat, spectators, or any server backend.
- Compatibility with the entire historical Android: Netrunner / NISEI / Null Signal card pool.
- A tournament-grade deckbuilder or NetrunnerDB synchronization. Curated local deck presets are sufficient for the first release.
- Rules execution by natural-language interpretation of card text. Card behavior is implemented explicitly in code using reusable engine primitives.
- An AI that is immediately competitive with expert human players. Correctness, completeness, and extensibility take priority over peak strength.
- A mandatory cloud service, telemetry pipeline, launcher, or auto-update infrastructure.

## 3.3 Release success criteria

| Area | Release bar |
|---|---|
| Correctness | All System Gateway card designs are implemented and test-covered; no known rules blocker prevents completion of a normal match. |
| Completeness | A human can start, play, save/resume, and finish a game as Corp or Runner against the offline bot. |
| Determinism | Given the same initial decks, seed, and accepted actions, the engine reproduces the same state/event sequence. |
| Information safety | Automated tests verify that player/agent observations do not expose hidden opponent cards or privileged engine state. |
| Responsiveness | Human interactions feel immediate; local rules/UI feedback is not gated on AI or network round trips. |
| AI legality | The offline bot completes stress-test games without issuing illegal state mutations or bypassing the legal-action API. |
| Extensibility | Adding a new card does not require changing UI or agent interfaces unless that card introduces a genuinely new rules primitive. |

# 4. Target users and core use cases

## 4.1 Primary user

A Netrunner player who wants to play alone on a desktop, prefers a smoother game-like interface to browser implementations, and may want either a fast local AI match or a more personality-rich LLM-driven opponent.

## 4.2 Secondary user

The developer/tester of the project, who needs deterministic board-state construction, snapshots, replays, headless simulation, and visibility into AI decisions to implement and debug rules interactions efficiently.

## 4.3 Core use cases

- Start a quick game using a curated Corp and Runner deck preset.
- Choose to play either side and select an offline bot or configured LLM/hybrid opponent.
- Play an entire match using contextual card/server interactions without needing rules-engine knowledge.
- Save a game and resume it later on the same machine.
- Review a concise action log or replay a saved deterministic game for debugging.
- Run headless bot-vs-bot simulations during development to identify crashes, deadlocks, illegal states, or poor heuristics.

# 5. Scope and release boundaries

## 5.1 Initial card and rules baseline

The implementation baseline is System Gateway – Remastered Edition. Null Signal Games currently describes the product as 77 card designs (73 original Standard-legal cards, 2 Standard-legal reprints, and 2 tutorial-only identities), totaling 205 playable physical cards plus player aids. The engine should implement card designs and quantities relevant to digital deck construction, not model physical packaging.

Rules behavior must track the current Null Signal Games Comprehensive Rules at implementation time. As of this PRD, the current published rules are Comprehensive Rules v26.03, effective 2 March 2026. Rules-version compatibility should be recorded in build metadata and save/debug snapshots.

## 5.2 MVP / post-MVP split

| Capability | Initial release | Later |
|---|---|---|
| Game content | Complete System Gateway – Remastered rules/cards | Additional Null Signal sets; rotation-aware pools |
| Match setup | Curated deck presets; side and opponent selection | Full deckbuilder; deck import/export; format legality |
| Opponent | Heuristic offline bot | Search/ISMCTS; hybrid planning; personalities |
| LLM | Optional provider abstraction + legal-action tool loop, **with hybrid intent-level play as a first-release target (see 8.4, 17.2)** | Richer opponent modeling; provider presets; local-model tuning |
| Tutorial | Contextual hints and usable first-game flow | Scripted tutorial campaign / scenario lessons |
| Replay/debug | Save/load, snapshots, action/event log | Full replay browser, branching analysis, scenario editor |
| Platforms | Desktop-native; no browser dependency | Additional desktop packaging as resources allow |
| Networking | None | Still no PVP requirement unless product direction changes |

# 6. Core product flows

## 6.1 New game

1. Open the application and select New Game.
2. Select side: Corp or Runner.
3. Select one of the available curated deck presets for each side.
4. Select opponent mode: Offline Bot, LLM, or Hybrid (only modes that are configured/available are enabled).
5. Optionally select difficulty/personality settings supported by that agent.
6. Create the match from a generated or explicitly shown RNG seed and enter mulligan/setup.

## 6.2 Human turn

The UI queries the engine for legal actions and visually exposes only currently legal or contextually relevant interactions. When the player commits an action, the UI submits a typed command/action identifier. The engine validates it, resolves resulting effects/prompts, and emits events. The UI animates those events and presents the next decision point.

## 6.3 AI turn or response window

The active agent receives a filtered observation plus the legal action set. A conventional agent selects directly. An LLM agent may call read-only analysis tools, but the only state-changing operation is selecting an engine-issued legal action (and, where required, subsequent engine-issued choices). The UI remains responsive while a model thinks and presents a clear "opponent thinking" state.

## 6.4 Run

Starting a run visually focuses the attacked server. Approach, paid-ability windows, encounters, subroutine resolution/breaking, movement, success, breach/access, steal/trash choices, and run end are represented as explicit engine states/prompts. The UI may compress trivial transitions but may not skip a decision point that is legal under the rules.

## 6.5 Save/resume

A save contains sufficient engine state, rules/card data version identifiers, RNG state, and agent configuration metadata to resume the game deterministically. Secrets such as external API keys are never embedded in saves.

# 7. Functional requirements

Requirement IDs are intended to carry directly into the implementation plan, issue tracker, and test plan. "Must" requirements define the initial release unless explicitly marked later.

| ID | Area | Pri. | Requirement |
|---|---|---|---|
| ENG-001 | Engine | Must | Maintain the complete authoritative game state independently of UI and agent implementations. |
| ENG-002 | Engine | Must | Expose typed legal actions/choices for the current decision point; callers may not mutate state directly. |
| ENG-003 | Engine | Must | Validate every submitted action and reject invalid, stale, or out-of-turn actions without corrupting state. |
| ENG-004 | Engine | Must | Support deterministic seeded randomness and serialize/restore RNG state. |
| ENG-005 | Engine | Must | Emit structured events describing resolved state changes for presentation, logs, and replay. |
| ENG-006 | Engine | Must | Support nested effect resolution and decision prompts without relying on UI callbacks or global mutable phase variables. |
| ENG-007 | Engine | Must | Run headlessly for automated tests and simulations without initializing the graphical client. |
| ENG-008 | Engine | Must | Expose stable object/action identifiers suitable for saves, replays, bot logic, and LLM tool calls. |
| RULE-001 | Rules | Must | Implement turn structure, clicks, credits, draw, install, play, advance, rez, score, trash, damage, tags, memory, hand size, and victory conditions required by the initial pool. |
| RULE-002 | Rules | Must | Implement run timing including initiation, approach, encounter, paid-ability windows, breaking/firing subroutines, movement, success, breach/access, and run end. |
| RULE-003 | Rules | Must | Implement triggered, optional, conditional, prevention/replacement, and simultaneous effects required by System Gateway. |
| RULE-004 | Rules | Must | Record the Comprehensive Rules baseline/version used by the build. |
| CARD-001 | Cards | Must | Implement all System Gateway – Remastered card designs needed for normal and tutorial/preset play. |
| CARD-002 | Cards | Must | Separate static card metadata from executable card behavior. |
| CARD-003 | Cards | Must | Prefer reusable effect/rules primitives over card-specific state mutation. |
| CARD-004 | Cards | Must | Provide automated behavior tests for every implemented card design, including important interaction edge cases **identified from Null Signal Games rulings/FAQ sources and cross-referenced against existing open-source implementations (see section 18) — not derived from Comprehensive Rules text alone.** |
| INFO-001 | Hidden info | Must | Build player-specific observations from authoritative state rather than exposing GameState directly. |
| INFO-002 | Hidden info | Must | Hide unrezzed/unknown Corp cards and any other rule-defined private information from the Runner observation. |
| INFO-003 | Hidden info | Must | Hide Runner hand/stack information from the Corp except where revealed/known by rules. |
| INFO-004 | Hidden info | Must | Use the same observation boundary for heuristic, search, and LLM agents unless a debug cheat mode is explicitly enabled. |
| MATCH-001 | Match | Must | Start a game from selected side, curated deck presets, opponent type, and RNG seed. |
| MATCH-002 | Match | Must | Support mulligans and all setup steps needed by the selected presets. |
| MATCH-003 | Match | Must | Determine and display normal win/loss outcomes and reason. |
| MATCH-004 | Match | Must | Save and resume an in-progress local game. |
| UI-001 | UI | Must | Render Corp centrals/remotes, ICE, roots, Runner rig, hands, credits, clicks, tags/damage, agenda points, and required counters/tokens clearly. |
| UI-002 | UI | Must | Provide immediate hover/selection feedback and contextual highlighting for legal cards, servers, targets, and choices. |
| UI-003 | UI | Must | Support click-first interactions; drag-and-drop may be provided where it improves feel but must not be the only way to perform an action. |
| UI-004 | UI | Must | Animate engine events without changing engine timing or legality. |
| UI-005 | UI | Must | Make the current phase/decision owner and mandatory prompt understandable without requiring the action log. |
| UI-006 | UI | Must | Provide card zoom/details and readable rules text on demand. |
| UI-007 | UI | Should | Allow animation speed reduction/increase and skip/compress non-decision AI animation sequences. |
| AI-001 | Offline AI | Must | Implement both Corp and Runner agents capable of completing legal System Gateway games. |
| AI-002 | Offline AI | Must | Use only observation + legal-action APIs available to non-cheating agents. |
| AI-003 | Offline AI | Must | Use utility/heuristic evaluation for baseline tactical and strategic choices; deterministic tie-breaking must be seedable. |
| AI-004 | Offline AI | Should | Support difficulty through heuristic parameters, search depth, or controlled stochasticity rather than illegal information advantages. |
| AI-005 | Offline AI | Must | Provide debug decision logs that can be enabled locally without exposing them during normal play. |
| LLM-001 | LLM | Must | Treat LLM support as optional; absence/failure of a model endpoint must not prevent offline play. |
| LLM-002 | LLM | Must | Implement an agent/provider abstraction so the game is not coupled to one vendor or model. |
| LLM-003 | LLM | Must | Expose only filtered observations, legal actions, and read-only game-analysis tools to the model. |
| LLM-004 | LLM | Must | Permit state change only by selecting an engine-issued legal action/choice identifier. |
| LLM-005 | LLM | Must | Handle timeout, malformed output, unavailable endpoint, and invalid/stale action responses with retry/fallback behavior that cannot corrupt the match. |
| LLM-006 | LLM | **Must** *(elevated from Should)* | Support OpenAI-compatible endpoints to enable local model servers as well as remote providers where configured. |
| LLM-007 | LLM | Should | Allow hybrid mode in which the LLM supplies strategic intent while deterministic logic handles routine/tactical choices. |
| LLM-008 *(new)* | LLM | Should | Where the local inference server supports it, use schema/grammar-constrained decoding to bias model output toward legal action identifiers, reducing reliance on retry-only handling of malformed/illegal output for lower-capability local models. |
| DEV-001 | Developer tools | Must | Create/save/load arbitrary test snapshots without graphical setup steps. |
| DEV-002 | Developer tools | Must | Persist an action/event log sufficient to reproduce a reported state from a known build and seed. |
| DEV-003 | Developer tools | Must | Provide a headless simulation entry point for bot-vs-bot regression/stress runs. |
| DEV-004 | Developer tools | Should | Provide developer-only controls for step, fast-forward, reveal hidden information, grant resources, and jump to common test states. |
| DATA-001 | Data/assets | Must | Keep the engine independent from card-art availability and support a fallback text/card renderer. |
| DATA-002 | Data/assets | Must | Store card metadata using stable identifiers and explicit set/version fields. **Source: `Null-Signal-Games/netrunner-cards-json` (the canonical data source also consumed by NetrunnerDB), vendored locally; NetrunnerDB's v3 API (`api-preview.netrunnerdb.com`) may be used as a live-lookup/validation aid during development (see OD-02).** |
| DATA-003 | Data/assets | Must | Do not redistribute Null Signal card artwork, frames, or card backs without permission/licensing appropriate to the distribution model. |

# 8. AI and LLM behavior model

## 8.1 Common agent contract

All non-human opponents implement the same conceptual contract. The engine supplies a player-filtered observation and a finite set of legal actions. The agent returns one legal action identifier. Follow-up targets, reorderings, trace bids, access decisions, or optional triggers are represented as subsequent legal-choice requests rather than hidden side channels.

```
ChooseAction(Observation observation, IReadOnlyList<LegalAction> legalActions) -> ActionId
```

## 8.2 Offline bot

The first bot should prioritize robust completion over sophisticated search. Utility scoring should cover economy, tempo, server protection, agenda exposure, rig state, likely run cost, damage/tag pressure, and winning lines. Controlled randomness may prevent overly mechanical play, but difficulty must not be implemented by granting hidden information or illegal resources.

## 8.3 LLM agent

The LLM is a strategic decision-maker, not a rules engine. Prompt/tool output should be compact, structured, and based on stable IDs. Useful read-only tools may include card inspection, public/known server inspection, remaining known resources, legal-action explanation, and engine-computed tactical estimates such as known break cost. The model never receives raw internal objects, RNG state, hidden opponent cards, or a generic "execute command" capability. **Where the configured endpoint supports it, output should be constrained to the legal-action schema (see LLM-008) rather than relying solely on post-hoc validation and retry.**

## 8.4 Hybrid mode

Hybrid mode is the preferred long-term path for strong and interesting play, **and is the primary mechanism by which the product thesis (bluffing/mind-games opponent behavior) is actually realized** — a heuristic executing a chosen intent (for example "stabilize economy," "pressure R&D," "represent a scoring remote," or "preserve credits for the next run") produces observably different, more human-plausible board states than an agent purely optimizing per-decision EV, without requiring a full model call for every tactical choice. This reduces latency and model cost while keeping bluffing/opponent modeling at the strategic layer.

**Decision granularity must be scoped explicitly rather than left implicit.** Netrunner has many response-window decisions per turn (whether to rez a piece of ICE, whether to jack out mid-run, whether to fire an optional ability) where the entire strategic value is the bluff or read, not the tactical computation. Routing every such decision through a full LLM call risks both breaking the non-blocking UX target (NFR-002) and diluting the model's few calls per turn across low-signal choices. Which decision points warrant a fresh LLM intent call versus falling under the current standing intent is an explicit open decision (see OD-11) that should be resolved before hybrid mode implementation begins, not discovered during it.

## 8.5 Failure handling

- If the LLM endpoint is unavailable before game start, the mode is unavailable or the player may choose the offline bot.
- If a configured LLM fails during a match, the game preserves state and offers retry or local-bot takeover.
- Malformed or non-legal model output is never executed; the agent layer retries with the current legal action set or falls back according to configuration.
- No model/provider error may alter the deterministic game state until a valid legal action has been accepted by the engine.

## 8.6 Forward note: mixed-strategy decisions in future sets

System Gateway does not currently require simultaneous secret-bid ("Psi game") style decisions, but later Null Signal sets do include this mechanic. Correct play in a mixed-strategy setting requires sampling from an equilibrium distribution rather than selecting a single "best" answer — a mode of play LLMs are structurally weak at, since they tend toward a modal response rather than genuine randomization even when instructed to randomize. This is a distinct failure mode from the malformed-output handling in 8.5 (the output would be legal, just strategically exploitable) and should be flagged for explicit design review — likely resolved by having the engine itself inject the randomization (e.g., the LLM selects a distribution/bias rather than a single bid, or a validated-random fallback is used for this decision class) — when set expansion reaches a set that introduces it. No action required for the current release.

# 9. UX and interaction requirements

## 9.1 Interaction feel

The visual target is "game-like responsiveness" rather than literal imitation of Hearthstone. Hover/selection feedback should be immediate, dragged objects should track the pointer without perceptible lag, valid targets should become obvious before commitment, and accepted actions should animate clearly to their resolved destination.

## 9.2 Board layout

The default board should preserve Netrunner's spatial model: Corp central servers and remotes with ICE ordered outward from each server; Runner rig organized by card type/host relationships; both hands and scoring areas accessible without modal navigation. During a run, the attacked server and current ICE/encounter may be visually emphasized or temporarily enlarged while spatial identity remains clear.

## 9.3 Decision presentation

| Situation | UX requirement |
|---|---|
| Normal action | Cards/servers with legal actions highlight; unavailable actions are not presented as active. |
| Target choice | Candidate targets glow/highlight; current selection and cancel path remain visible. |
| Paid-ability window | Show which player has priority and a clear Continue/Pass action. |
| Encounter | Center/focus current ICE; show strength, active subroutines, breaker options, and continue/jack-out choices. |
| Access | Present accessed card(s) and legal steal/trash/no-action choices with costs. |
| AI thinking | Show non-blocking status; keep menus/card inspection responsive; avoid fake deterministic timing. |
| Error | Illegal/stale actions produce concise feedback and no state mutation. |

## 9.4 Accessibility and usability

- All essential actions must be available without drag-and-drop.
- Rules text and counters must remain legible at common desktop resolutions; card zoom/details must be available on demand.
- Important state must not rely on color alone; use labels/icons/position in addition to color highlights.
- Animation speed should be adjustable; non-essential motion should be reducible where practical.
- Keyboard shortcuts may be added later but should not be required for basic play.

# 10. Non-functional requirements

| ID / area | Requirement |
|---|---|
| NFR-001 Performance | Local UI feedback for selection/hover should normally appear within one rendered frame; routine engine commands should resolve fast enough that animation, not rules computation, is the perceived pacing. |
| NFR-002 Frame rate | Target a stable 60 FPS on a typical contemporary desktop during normal board states; AI/model computation must not block rendering/input. |
| NFR-003 Reliability | No recoverable invalid user/agent action may crash the game or leave partially mutated state. |
| NFR-004 Determinism | All randomness affecting game state must flow through a serializable seeded RNG controlled by the engine. |
| NFR-005 Offline operation | Core game, card data, saves, and heuristic AI require no network connection. Remote LLM use is explicitly opt-in. |
| NFR-006 Privacy | No telemetry by default. External LLM requests contain only the configured agent observation/tools; provider credentials remain local and are excluded from saves/logs. |
| NFR-007 Maintainability | Core engine and card tests run without launching the game client. Rules/card modules should have no dependency on rendering APIs. |
| NFR-008 Compatibility | Save/debug artifacts include schema/build/rules/card-data versions and fail gracefully when incompatible rather than silently misloading. |
| NFR-009 Diagnostics | Errors and debug logs identify current decision state, action ID, relevant object IDs, seed, and build version without requiring hidden state in normal user logs. |

# 11. Architecture constraints and preferred technical direction

This section intentionally captures constraints that materially affect the implementation plan. Exact libraries may change, but violating these boundaries requires an explicit product/architecture decision rather than incidental implementation convenience.

## 11.1 Preferred stack

> **Current preference**
> Godot 4 for the desktop game/presentation layer, C#/.NET for both the Godot client and a UI-independent Netrunner.Core library. The implementation plan must validate this choice with a small technical spike before full production work (see OD-04) — in particular, the C#/Godot edit-compile-observe iteration loop, since Godot's C# hot-reload support remains limited relative to GDScript and this loop will be exercised once per card design, roughly 77+ times, during initial development.

## 11.2 Dependency direction

```
Netrunner.Godot -> Netrunner.Core <- Netrunner.Cards
                        ^
                        |
              Netrunner.AI / Netrunner.LLM
```

Tests + CLI/headless tools depend on Core/Cards, never on the graphical client.

## 11.3 State mutation boundary

UI and agent modules never set credits, move cards, rez cards, alter counters, or advance phases directly. They submit typed actions/choices. The engine validates and resolves them, then emits events. This boundary is mandatory even when a direct mutation would appear simpler for a particular card or animation.

## 11.4 Resolution model

The engine should model game flow using explicit state/resolution objects (for example a resolution stack or equivalent continuation model) rather than relying on renderer callbacks or a large set of process-global phase flags. Nested prompts, optional abilities, simultaneous triggers, prevention/replacement, and run timing must remain headless and serializable.

## 11.5 Card implementation model

Static card data is declarative; executable behavior is code composed from reusable primitives. The product does not require a natural-language card parser or a general scripting language for the first release. A future declarative effect DSL is acceptable only if it preserves compile/test/debug quality and cannot bypass engine legality/state ownership.

# 12. Data, assets, and legal/distribution constraints

## 12.1 Card data

Each card design needs a stable code, title, side, faction, type/subtypes, cost/strength/agenda values as applicable, rules text for display, set information, deck quantity, and implementation binding. **The authoritative machine-readable source is `Null-Signal-Games/netrunner-cards-json`, vendored for offline use (see OD-02 and DATA-002).**

## 12.2 Artwork

The engine and UI must work without official card art. A text/template fallback is therefore a product requirement, not merely a developer convenience. Null Signal's public visual-assets terms explicitly exclude general public use of card artwork, card frames, and card backs. If the project is distributed, official card imagery should not be bundled unless appropriate permission has been obtained. A local user-supplied/imported asset pack may be considered separately, subject to applicable rights and terms.

## 12.3 Branding

Project naming, logos, card backs, and presentation should avoid implying official Null Signal, Fantasy Flight Games, or Wizards of the Coast endorsement. Attribution/licensing notices for any permitted assets should be kept in a dedicated credits/licenses view and distribution package.

# 13. Save, replay, and developer diagnostics

## 13.1 Save state

- Complete authoritative engine state required to resume.
- Serialized RNG state / seed and current decision/resolution state.
- Deck definitions and stable card instance identifiers.
- Rules/card-data/build/schema versions.
- Human-visible opponent mode/configuration metadata, excluding credentials/secrets.

## 13.2 Action/event log

The canonical reproduction artifact should record accepted player/agent actions and deterministic events with identifiers and ordering. Because LLM output is inherently non-deterministic, **replay must always mean replaying the logged accepted actions, never re-invoking the model** — the action/event log, not the model, is the source of truth for reproduction. A debug export should be able to include hidden information when explicitly requested by the developer, but normal user-facing logs must preserve information boundaries during live play.

## 13.3 Test scenarios

The implementation plan should include a lightweight scenario/snapshot format for constructing exact states such as "Runner approaching the second ICE on R&D with 6 credits" or "Corp has an agenda in a remote with two advancements." This replaces manual UI setup and should be usable by unit/integration tests as well as a developer menu.

# 14. Quality strategy and acceptance tests

## 14.1 Automated test layers

| Layer | Minimum coverage |
|---|---|
| Rules unit tests | Core primitives: costs, movement, timing, prevention/replacement, damage, tags, access, scoring, victory. |
| Card tests | At least one positive behavior test per card design plus interactions/edge cases proportional to complexity, drawn from rulings/FAQ sources per CARD-004. |
| Observation tests | Assert exact visibility/known-state behavior for Corp and Runner at representative hidden-information states. |
| Determinism tests | Replay action sequences from a fixed seed and compare resulting state/event hashes. |
| Agent contract tests | Feed legal-action sets to each bot path; assert selected IDs are legal and no privileged state is accessed. |
| Simulation tests | Run many headless games to detect crashes, stuck prompts, impossible states, and pathological loops. |
| Save/load tests | Round-trip representative states, including nested prompts and in-progress runs. |
| UI smoke tests | Start game, perform representative actions/run/access, save/resume, and finish match in packaged desktop build. |

## 14.2 Initial release exit criteria

- No P0/P1 rules defect is known in the supported System Gateway pool.
- All supported card designs have automated tests and are reachable through normal play or test scenarios.
- Headless bot-vs-bot stress runs complete without crashes, unresolved prompts, or illegal action acceptance at an agreed sample size.
- Human-vs-bot games can be completed from both sides using packaged builds.
- Save/resume works during normal turns and during representative run/decision states.
- Observation leakage tests pass for Corp and Runner.
- LLM mode, when configured, cannot mutate state except through legal actions and can fail over without corrupting a saved match.
- The game remains fully playable with no LLM configuration and no network access.

# 15. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Rules/timing complexity | High | Build engine primitives and timing model before broad card implementation; use rules-versioned tests and exact snapshot scenarios. |
| Card-specific special cases | High | Prefer reusable primitives; require tests before considering a card complete; treat engine changes triggered by cards as reviewed architecture changes. |
| Hidden-information leaks to AI | High | Dedicated ObservationBuilder boundary plus negative tests; forbid agent dependency on internal GameState. |
| Weak or predictable bot | Medium | Ship heuristic competence first; add controlled randomness and simulation/search after correctness baseline. |
| LLM latency/cost/unreliability | Medium | Make LLM optional; keep UI asynchronous; use legal IDs; add retry and local-bot takeover; reserve LLM for meaningful choices in hybrid mode. |
| **Local LLM / GPU resource contention** *(new)* | **Medium** | **Document expected VRAM/compute footprint for supported local model sizes; keep the rendering budget and local-inference budget independently tunable; treat a remote endpoint as an escape valve when local contention on shared hardware is unacceptable.** |
| UI animation creates rule bugs | High | Presentation consumes immutable events; it cannot advance phases or mutate engine state. |
| Artwork/distribution rights | High | Do not make official art a runtime dependency; ship fallback renderer; seek permission before bundling restricted assets. |
| Scope expansion into historical pool | Medium | Gate new sets behind "System Gateway complete + regression suite green"; maintain per-set modules and versioned card data. |
| Save schema churn | Medium | Version serialized formats early; provide explicit migration or incompatibility errors rather than silent load. |

# 16. Open decisions for the implementation plan

These items do not block this PRD but should be resolved explicitly during implementation planning or the first technical spikes.

| ID | Decision | Planning output |
|---|---|---|
| OD-01 | Desktop packaging target | Which OS(es) are required for the first distributable build? The architecture should remain cross-platform, but CI/packaging scope affects effort. |
| OD-02 | Card data source | **Resolved:** `Null-Signal-Games/netrunner-cards-json` (canonical source consumed by NetrunnerDB) as the vendored offline data source; NetrunnerDB's v3 API (`api-preview.netrunnerdb.com`, currently in preview) as an optional live-lookup/validation aid during development. Remaining planning output is the vendoring/update workflow only. |
| OD-03 | Rules representation | Choose the concrete resolution-stack/continuation/state-machine design and serialization strategy. |
| OD-04 | Godot/.NET validation | Spike Godot 4 + C# project structure, headless Core tests, packaging, and animation/event bridge. **Exit criterion must include a measured edit-compile-observe cycle time for a representative card-test change (not structural validation alone), given the C#/Godot hot-reload loop will be exercised for every card design.** |
| OD-05 | Initial deck presets | Define the curated Corp/Runner presets included in first playtest builds and whether tutorial identities are separately supported. |
| OD-06 | LLM provider set | Decide which provider adapters are first-party at launch; OpenAI-compatible local/remote endpoint support is preferred as a portability baseline (see LLM-006, now Must). |
| OD-07 | LLM privacy UX | Define exactly what observation data is shown in a pre-flight preview/consent screen before using a remote endpoint. |
| OD-08 | Artwork workflow | Decide between text-only release, user-imported local artwork, permission request, or another legally appropriate asset strategy. |
| OD-09 | Bot strength target | Define "competent enough" with a small benchmark scenario suite rather than subjective play impressions alone. |
| OD-10 | Replay product surface | Decide whether first release exposes only debug export or a user-facing replay viewer. |
| OD-11 *(new)* | LLM decision granularity | Enumerate the response-window/tactical decision points that warrant a fresh hybrid-mode LLM intent call versus falling under the currently standing intent (see 8.4), before hybrid mode implementation begins. |

# 17. Implementation-plan handoff

The next document should convert this PRD into a dependency-ordered engineering plan. It should not simply restate features. Each workstream should identify deliverables, interfaces, test gates, and the smallest playable vertical slices.

## 17.1 Expected planning workstreams

- Foundation: repository, solution/project boundaries, build/test/CI, formatting and logging.
- Core domain model: cards, zones, players, servers, runs, counters, stable IDs, seeded RNG.
- Resolution/timing engine: commands, legal actions, prompts, effect stack/continuations, event emission.
- Rules primitives and System Gateway card implementation with test fixtures.
- Observation and hidden-information layer.
- Headless CLI/simulation/snapshot/replay tooling.
- Godot presentation shell and event-driven board/interaction UI.
- Human input adapter and match orchestration.
- Offline Corp/Runner heuristic agents and benchmark scenarios.
- LLM provider/tool adapter, fallback behavior, and hybrid policy layer.
- Packaging, settings, save files, credits/licenses, and release QA.

## 17.2 Preferred milestone shape

| Milestone | Outcome | Exit signal |
|---|---|---|
| Vertical slice A | Headless toy game | Two tiny decks can reach a win condition through typed actions; deterministic replay works. |
| Vertical slice B | Core Netrunner run | Human/CLI can execute a representative run with ICE encounter, breaking, success, and access. |
| Vertical slice C | Godot playable shell | Same engine runs through native UI with one curated micro-deck per side. |
| Vertical slice D | Gateway complete | All System Gateway cards/rules implemented and regression-tested. |
| Vertical slice E | Offline solo | Both sides playable against baseline bot; save/resume and developer diagnostics complete. |
| Vertical slice F | LLM/hybrid | Configured model can play via filtered tools/legal actions with safe fallback. **Hybrid intent-level play (8.4) is the priority within this slice, since it is the primary vehicle for the product thesis; pure per-decision LLM calls are a secondary/fallback mode.** |
| Release candidate | Polish/QA | Performance, UX, packaging, rights-safe assets, stress tests, and known-issue review complete. |

> **Planning rule**
> A milestone is complete only when its engine behavior is testable headlessly and its product behavior is demonstrable through the current client. Avoid a long "engine first, UI later" branch after the first core primitives; build thin vertical slices early while preserving the strict dependency boundary.

# 18. Reference baseline

The following sources define the external baseline used by this PRD. They should be rechecked when the implementation plan begins and whenever rules/card data are updated.

- **Null Signal Games — System Gateway product page:** <https://nullsignal.games/products/system-gateway/>
- **Null Signal Games Shop — System Gateway – Remastered Edition:** <https://shop.nullsignal.games/products/system-gateway-remastered-edition>
- **Null Signal Games — Comprehensive Rules Hub:** <https://nullsignal.games/rules/comp-rules/>
- **Netrunner Comprehensive Rules (web edition):** <https://rules.nullsignal.games/>
- **Null Signal Games — Visual Assets guidelines and terms:** <https://nullsignal.games/about/nsg-visual-assets/>
- **Chiriboga solo-mode fork (reference implementation):** <https://github.com/drbo6/chiriboga>
- **`Null-Signal-Games/netrunner-cards-json` — canonical card data source *(new, resolves OD-02)*:** <https://github.com/Null-Signal-Games/netrunner-cards-json>
- **NetrunnerDB v3 API (preview) — optional live-lookup during development *(new)*:** <https://api-preview.netrunnerdb.com/api/docs>
- **`mtgred/netrunner` (jnet, the jinteki.net engine; MIT-licensed) — reference only, for cross-checking card-interaction edge cases and rulings against CARD-004; not a source for code, language, or rules-representation reuse *(new)*:** <https://github.com/mtgred/netrunner>

**Document note:** This PRD is a product/engineering planning artifact, not legal advice. Any distributed build should independently verify rights and license obligations for names, card data, artwork, frames, backs, symbols, and other assets.
