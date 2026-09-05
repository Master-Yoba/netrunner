# M4 implementation plan — System Gateway complete

Status: architect-reviewed execution plan for the M4 slice of `docs/IMPLEMENTATION_PLAN.md`
Baseline date: 31 August 2026
Rules baseline: Null Signal Comprehensive Rules v26.03
Card baseline: System Gateway – Remastered Edition, vendored commit `236140cf…`, 77 designs
Companion docs: `docs/IMPLEMENTATION_PLAN.md` (M4 section),
`docs/M2_ARCHITECTURE.md`, `docs/M2_PROGRESS.md`, `docs/M3_PROGRESS.md`,
`data/vendor/README.md`

## 1. Scope and gate

M4 implements every System Gateway design with the plan's primitive-driven batch
order, the setup/mulligan rules, the official teaching presets, the CARD-401
coverage validator, and a client that plays a full game with a human in either
role against a contract agent. The rules authority is the
[Null Signal Comprehensive Rules 26.03](https://rules.nullsignal.games/); the
[official System Gateway card lists](https://nullsignal.games/products/system-gateway/)
are the preset authority.

Hard exit gate (from the plan):

- 100% metadata/behavior/test registration for supported designs (all 77).
- Card review checklist complete (per-card text-diff check, ruling citations,
  test links — see §6).
- Full headless regression green (all suites, M2 proof, M3 smoke, card-data
  validation).
- Both sides can finish human-vs-contract-agent games through the client.
- DATA-400 proves the client build consumes the authenticated, gitignored card
  snapshot through an explicit bootstrap/build step and contains the generated
  runtime metadata needed to run offline.

Out of scope for M4: benchmark bots and difficulty tuning (M5), user-facing
save/resume (M5), LLM/hybrid mode (M6), graphical replay browser
(post-release), any official art (OD-08 — the client stays deliberately
text-only), and new distribution/package lanes (RC). Existing headless/editor
and native development smokes remain required.

## 2. Baseline (31 August 2026)

Engine (`src/Netrunner.Core/`):

- Full CR 6.9 run timing machine, paid-window priority, encounter/subroutine
  resolution, break/pump, breach/access (CR 7.x), run events with hosted run
  pools, checkpoint/trigger/interrupt system (CR 9.2.8), damage + flatline,
  tags, memory, virus purge, lingering effects, observation builder with leak
  tests, replay verifier, invariant validator. 356 tests green at the M3 gate.
- Five behavior-spec families in `CardBehaviorSpecCatalog`
  (version `gateway-m2-3`): ICE, icebreaker, run event, agenda, asset
  (trash-cost only). 7 of 77 Gateway cards are registered (9%), but only 5
  encode their full text; Nico Campaign and Offworld Office are partial.
- Gaps for a full Gateway game: no real turn structure (M1 toy turns only),
  no advance/score beyond the toy, no install/play for full card types, no
  draw effect instruction, no search/reveal/shuffle, no cost modification,
  no hosted card credit pools, no generic strength/cost modifier system,
  no identity/upgrade/operation/resource/hardware spec families, no
  mulligan/setup, no coverage validator, no presets.

Match/client (`src/Netrunner.Match/`, `src/Netrunner.Godot/`):

- `MatchCoordinator` (worker-boundary, UI-301) and full M2 view models are
  stable; the event bridge is the thing the plan says M3 UX work can run in
  parallel with card batches.
- The whole UI is one 1,868-line `Main.cs` god class, 100% code-built layout,
  per-snapshot full rebuild, no Theme resource, text-only by design.
- `CardPresentationMetadata.For(definitionId)` in
  `src/Netrunner.Match/PresentationViewModels.cs:715-739` is a hardcoded
  7-card switch — the single biggest UI blocker for the full card set.
- Match "setup" is one button that launches the embedded M2 scenario. No deck
  or preset selection, no mulligan, no restart path.

Data: 77 normalized cards under `data/vendor/system-gateway/` (git-ignored,
SHA-authenticated by `manifest.json`), normalizer + validator in
`src/Netrunner.Cards/CardData.cs`, updater `scripts/update-card-data.sh`.
`data/vendor/README.md` explicitly prohibits checking those source artifacts
into Git; a clean clone and CI do not have them. No presets exist in the repo
yet. DATA-400 therefore has to make acquisition, validation, build inclusion,
and missing-data behavior explicit instead of relying on a developer's working
directory by accident. Public redistribution approval remains an RC concern.

## 3. Decisions for M4

These stand until evidence forces an ADR.

| ID | Decision |
|---|---|
| D-401 | Separate version meanings before adding cards. `CardDataVersion` identifies the authenticated metadata snapshot (commit/hash); `BehaviorRegistryVersion` identifies compiled behavior. The current M2 document incorrectly compares `cardDataVersion` to the global behavior-catalog version. Replace that comparison with an explicit required-capability/fingerprint check for its seven-card slice. A one-time schema/version migration will change state hashes and must be re-pinned with a documented diff; later additive registrations must not churn M2 pins. Bump the behavior version once per completed batch. |
| D-402 | The five non-M2-gap cards (Palisade, Whitespace, Cleaver, Buzzsaw, Overclock) are audited in 4.1 and closed as complete; no M4 work. Nico Campaign and Offworld Office have unimplemented printed text and are completed in 4.3 and 4.7 respectively. |
| D-403 | CARD-401 is build tooling, not a production CLI dependency. Add a small `tools/Netrunner.CardCoverage` executable and keep the test marker in test/build-tool contracts (or use xUnit traits); do not put a test-only attribute in `Netrunner.Cards`, and do not make `Netrunner.Cli` reference or discover test assemblies. The tool accepts explicit catalog/test-assembly paths, reads metadata without executing tests, and cross-checks checked-in `data/m4/batch-registry.json`. |
| D-404 | Coverage reports are generated to `docs/generated/gateway-coverage.md` and `.json` and checked in. Inputs are the compiled catalog, test metadata, `data/m4/batch-registry.json`, and manual `data/m4/rulings.json`. `scripts/verify.sh` regenerates in a temporary directory and diffs. A completed batch fails if a card lacks full behavior registration, at least one registered positive test, review status, or required citations. |
| D-405 | Simulation inputs use a new versioned match/deck fixture, not the M2 run-scenario schema. `data/m4/sim/*.json` names identities, decklists, seed, and optional deterministic policy overrides; it goes through the same shuffle/setup/mulligan/turn path as a client game. An explicit developer-only validation profile may waive minimum deck size and Corp agenda ranges so early completed-card pools are runnable; it may not waive side, known-code, implemented-behavior, or runtime legality checks, and it is rejected by user-facing setup. Narrow board-state scenarios remain appropriate for timing fixtures only. |
| D-406 | Contract agent: keep `RunContractAgent` untouched as the M2 timing anchor; add `FullGameContractAgent` (deterministic, observation-only) that grows one action class per slice. The M4 gate requires it to finish a full teaching-preset game on either side. |
| D-407 | UI strategy: stay text-only with non-color cues (OD-08, M3 design). Introduce a Theme resource and extract region controls (board, actions, event feed, focus, zoom, setup) incrementally, with `Main` retaining composition/coordinator duties. Keep per-snapshot region rebuilds while the latency smoke passes. Card presentation uses an injected read-only metadata resolver; hidden observations never receive a definition ID, and the resolver never becomes a hidden-information side channel. Normalized HTML/symbol markup is converted by one tested, allow-listed formatter rather than displayed or stripped ad hoc. |
| D-408 | Engine organization: `GameEngine.Run.cs` is already 1,922 lines. 4.1 adds per-domain partials (`GameEngine.Turn.cs`, `.Install.cs`, `.Advance.cs`, `.Damage.cs`, `.Identities.cs`, …) and moves logic out of `GameEngine.cs` as it belongs. No behavior change in 4.1. |
| D-409 | No Gateway card uses recurring credits. Hosted credits and turn-begin transfers are ordinary card counters/effects, not recurring credits. Do not add a recurring-credit subsystem in M4 unless the primitive audit finds a reachable rule requirement. |
| D-410 | Link does not modify the basic run action. All four Runner identities have base link 0 and no Gateway card uses or changes link, so retain metadata but add no dead link mechanics. A basic run always costs 1 click (CR 5.2.7f). |
| D-411 | New timing structures get scenario fixtures and trace validation like M2: SCN-400 (turn/action/discard and install/play/score), SCN-401 (approach/encounter), and SCN-402 (damage/tag/card-trigger interactions), with CR 26.03 citations and jnet cross-checks. Every newly serializable frame/decision must round-trip at each yield. |
| D-412 | Preserve dependency direction: Core owns generic rules state, setup/mulligan state transitions, typed effects/frames, legality and behavior contracts; Cards owns Gateway metadata, compiled registrations and metadata-driven deck validation; Match owns preset/side selection, composition and coordinator; AI sees only `Observation`; Godot sees Match view models. Core and AI must not reference `Netrunner.Cards`, and Match receives catalogs/decks through composition rather than process-global file reads. |
| D-413 | Keep the authenticated source snapshot gitignored, but make it an explicit client build input. DATA-400 validates it and an MSBuild target generates a deterministic resource under `obj/` for `Netrunner.Cards`; downstream CLI/Match/Godot outputs receive it through the Cards assembly, so a built client works offline without the source directory. Missing input fails a content-enabled build with the exact bootstrap command; synthetic-data builds remain available to clean CI. Do not add a runtime network fetch or hand-copy metadata into switches. Public redistribution/license approval is recorded for RC packaging, not made an M4 engine blocker. |
| D-414 | Implement the actual CR turn/action/install model, not an expanded toy loop: Corp 3 clicks and mandatory draw, Runner 4 clicks and no mandatory draw; action phases end only at 0 clicks or by an effect; purge is a 3-click Corp basic action; scoring occurs in `(S)` windows and is not a click action; the normal agenda victory threshold is 7. |
| D-415 | Do not add one ad-hoc engine branch per card or an ever-growing family per printed type. In 4.1 define a composable compiled behavior contract around typed costs, conditions, targets, instructions, static modifiers, triggers, and narrow extension handlers for genuinely unique timing. Preserve the five existing M2 spec APIs as adapters until migrated. Rules text remains inert. |
| D-416 | M4 state expansion is schema work. New zones (identities, removed-from-game, transient play/set-aside as required), hosts, counters, turn history, and frames must be explicit, deeply copied, invariant-checked, canonicalized, replayed, and observation-filtered. Schema bumps happen deliberately at slice boundaries; backward compatibility is not inferred from default-valued fields. |
| D-417 | Replace process-wide mutable behavior registration as the production path. A builder validates duplicate/conflicting specs and produces an immutable versioned Core catalog passed to an engine session/match at composition time; state/replay stores only its version/fingerprint, never delegates. Keep the current static `CardBehaviorSpecCatalog` only as a compatibility adapter for M2 tests until callers migrate. This makes parallel matches/tests and future set catalogs deterministic. |

## 4. Slices

Slice numbering: 4.1 is preparation; 4.2–4.7 are the plan's six card batches
in order; 4.8 is integration reserve. Every card-batch slice ends with
`scripts/verify.sh` green, a headless proof (the batch sim deck driven by the
contract agent to a defined outcome), and a client demonstration (a
smoke-flagged scene driving the new content). Slices 4.1 and 4.8 use their own
explicit exit criteria.

### Slice 4.1 — Preparation, architecture, audit, coverage gate (≈1–2 weeks)

Purpose: make the next six slices mechanical — every new card binds, registers,
renders, and reports without one-off work.

| Sub-unit | Content |
|---|---|
| ARCH-401 | Implement D-401/D-412/D-415/D-416/D-417 contracts: distinct data/behavior versions; immutable composable behavior catalogs injected into engine sessions; state/frame schema policy; dependency tests that reject Core→Cards, Core→Match, and AI→Cards references. Preserve the M2 APIs through adapters. |
| ENG-400 | Engine organization per D-408: per-domain partial files and documented `StateInvariantValidator`, JSON-polymorphism, observation, and event extension checklists. Move code mechanically only where the diff is reviewable; behavior-neutral, full suite green. |
| PRIME-AUDIT | Walk all 70 incomplete card texts and the Gateway-reachable base rules; list every required primitive with CR 26.03 citation, owning slice, current gap, hidden-information impact, serialization impact, and representative tests. Output: `docs/M4_PRIMITIVES.md`. It must explicitly cover the full turn timing, all basic actions, install/play/rez/score legality, hosting, uniqueness/console rules, 7-point victory, setup, and the card-specific corrections captured in this plan. |
| CARD-401 | Build-tool implementation per D-403, `data/m4/batch-registry.json` (the 7 M2 codes in a legacy pseudo-batch), report generator, `verify.sh` wiring, and `data/m4/rulings.json` seed. The expected 77-code set is checked in; when local vendor data exists, the tool also asserts exact equality with it. |
| COV-401 | Generate and check in the first coverage report with distinct states: 7/77 registered/tested, of which 5/77 are full-text complete; Nico and Offworld are explicitly partial; 70 are unregistered. A partial registration never satisfies the final gate. |
| VER-401 | Perform the one-time version/schema migration from D-401. Record old/new state and event hashes and a semantic trace diff; hashes are expected to change because version fields are part of canonical state. Prove later additive registrations do not invalidate the M2 fixture. |
| DATA-400 | Add the explicit local-data build pipeline from D-413: authenticate commit/hash, generate the deterministic `Netrunner.Cards` resource under `obj/`, include it transitively in content-enabled CLI/Match/Godot outputs, and test missing/corrupt/stale input diagnostics plus offline loading after the source directory is absent. Keep source/generated content out of Git; clean CI uses synthetic fixtures, while the 77-card content smoke runs on a provisioned lane. |
| UI-401 | Replace the 7-card switch with an injected catalog-backed metadata resolver (title, type, faction, subtypes, cost, strength, memory, trash cost, agenda points, advancement requirement, rules text). Add one allow-listed formatter for `<symbol:...>`, `<strong>`, `<em>`, `<ul>`, `<li>`, and line breaks, with escaping/failure tests for unknown markup. A dev-only 77-card gallery on the provisioned content lane proves metadata coverage. |
| UI-402 | Theme foundation and incremental god-class split per D-407: a Theme resource (palette, font sizes, focus states, panel/card/button/label styles) and independently testable board/actions/feed/focus/zoom regions. Preserve keyboard focus, non-color cues, and M3 smoke/latency behavior after each extraction. |

Exit: CI green; report at 7 registered / 5 complete of 77; the version
migration and M2 semantic diff are accounted for; a content-enabled client
build is deterministic and self-contained; all 77 cards render correctly in
the provisioned gallery; clean CI reports how to bootstrap content rather than
failing mysteriously; both M3 smokes remain green.

### Slice 4.2 — Batch 1: real-game shell, basic install/play, choices, simple stats (≈3–4 weeks)

The foundation batch replaces the toy loop with a complete Gateway-reachable
game shell. It deliberately binds only cards that do not require installed-card
paid abilities or dynamic strength.

Primitives (engine first, then cards):

| Sub-unit | Primitive | Representative cards |
|---|---|---|
| ENG-401 | Serializable CR 5 turn frames and SCN-400: Corp gains 3 allotted clicks, has draw/action/discard phases and performs the mandatory draw; Runner gains 4 clicks, has action/discard phases and no mandatory draw. Implement all required `(P)/(R)/(S)` windows, formal turn-begin/end occurrences, forced discard, loss of leftover clicks, and `this turn`/`last turn` facts. There is no voluntary end-turn action while clicks remain. | (all) |
| ENG-402 | All Gateway-reachable basic actions: gain 1 credit; basic draw; Corp install and play (1 click plus the applicable card/install cost), advance (1 click + 1 credit), trash a resource while tagged (1 click + 2 credits), and purge (3 clicks); Runner install/play (1 click plus card cost), run (1 click), and remove a tag (1 click + 2 credits). Score eligible agendas in `(S)` windows without spending a click; win at 7 agenda points. | Seamless Launch, Verbal Plasticity, tag/virus cards |
| ENG-403 | Base run initiation costs exactly 1 click against any existing server and nests the M2 run timing frame inside the action. Link is irrelevant. | (all runs) |
| ENG-404 | Generic draw/set-aside/checkpoint procedure and deck-out; hidden-zone search with filter, reveal and shuffle; choose/trash from HQ; add from Archives; choose/shuffle HQ or Archives cards into R&D. Choice cardinality (`exactly`, `up to`, `any number`) is explicit. | Sprint, Hansei Review, Mutual Favor |
| ENG-405 | Full CR 8.5/8.6 install/play/rez model. Runner installs programs/hardware/resources from grip, paying printed install cost and enforcing memory. Corp's basic install is from HQ only: agendas/assets to remote roots, upgrades to central/remote roots, ICE outermost; ICE install cost is the number already protecting that server, while agendas/assets/upgrades have no install cost. Implement like-card trash choices, remote create/destroy, active/inactive/facedown state, rez costs/windows, event/operation play areas and post-resolution trash. Archives installs exist only when a card explicitly allows them. | (all) |
| ENG-406 | Reusable mode/chooser instructions and conditional branches, including another player choosing; conditional click loss; installed-this-turn and successful-run-this-turn facts. | Wildcat Strike, Predictive Planogram, Creative Commission, VRcation, Seamless Launch, Mutual Favor |
| MATCH-401A | Versioned deck/preset/match request plus Core-owned setup frames: faceup identities, 5 credits each, shuffle, draw 5, Corp mulligan then Runner mulligan, at most once each and second hand mandatory, then Corp first turn. Every yielded setup choice is engine-issued, observation-filtered, serializable and replayable. | (all games) |
| MATCH-401B | Deck validation: correct side/no identities in deck; identity minimum size and influence by copy; max 3 copies by name plus printed deck limits; Corp agenda-point range; tutorial identities accepted only with exact official starter/expanded presets. Uniqueness and console limits are in-game active/install rules, not deck-construction limits. | (all games) |
| DATA-401A | Check in code/quantity-only official presets with provenance: basic Runner 30 / Corp 34 and expanded starter+booster Runner 40 / Corp 44. They may be parsed and validated now but become user-playable only as their cards reach complete batches. | — |

Cards (12):

| Side | Cards |
|---|---|
| Corp operation | Government Subsidy (30064), Hedge Fund (30075), Sprint (30041), Hansei Review (30048), Predictive Planogram (30056), Seamless Launch (30040) |
| Runner event | Sure Gamble (30030), Wildcat Strike (30002), Creative Commission (30020), VRcation (30021), Mutual Favor (30011) |
| Runner hardware | T400 Memory Diamond (30031) |

Supporting work:

| Sub-unit | Content |
|---|---|
| AGT-401 | `FullGameContractAgent` v1: setup/mulligan, action loop, advance/install/play/basic economy, discard, tag removal/resource trash/purge, scoring and basic runs; observation-only. It does not submit an illegal end-turn action. |
| SIM-401 | First versioned match fixtures in `data/m4/sim/` (developer-curated, batch-1 cards), executed through normal setup rather than prebuilt state, using only the explicit D-405 reduced-deck validation profile. |
| UI-403 | Setup/mulligan flow for enabled sim presets; turn/phase/priority panel; all basic actions; install destinations and like-card trash choices; advance tokens; score reveal/points; base-run initiation; discard-to-limit prompt; hand/max-hand display. Use progressive disclosure so the normal action panel does not show irrelevant basic actions as a wall of buttons. |
| OBS-401 | Observation tests: search reveals (opponent sees searched card), HQ trash choice visibility, Archives add visibility. |

Exit: 12 cards bound/tested/registered; report at 19 registered / 17 complete
of 77; SCN-400 validates
turn/action/install timing; a seeded match starts through real setup and reaches
a defined 7-point, flatline, or deck-out outcome within an action-count cap with
no illegal action/stall; the client smoke completes setup and at least one full
Corp/Runner turn pair against the agent.

### Slice 4.3 — Batch 2: paid abilities, hosted credits, counters and triggers (≈2–3 weeks)

| Sub-unit | Primitive | Representative cards |
|---|---|---|
| ENG-407 | Paid-ability activation with typed costs: click-prefixed actions versus `lose click` paid abilities, credit/counter/self-trash/additional costs, affordability before materialization, once-per-turn limits, targeting, and optional/mandatory choices. Enforce one active console per player and unique-card checkpoint rules in game state. | Fermenter, Pennyshaver, Carnivore |
| ENG-407A | Generalize M2 breaker/ICE subtype matching beyond the current Barrier/Code Gate enum: support Sentry and abilities that interface with any encountered ICE; keep break limits and strength requirements in typed legality rather than title-specific branches. | Carmen, Echelon (4.4), Mayfly (4.5) |
| ENG-408 | Generic hosted counters and hosted credit pools: load/place/take/spend, spend restrictions, public counts, and empty-pool conditions. `load` is not recurring credit. | Nico Campaign, Red Team, Regolith, Smartware, Telework |
| ENG-409 | Active-source trigger bindings for install, rez, turn begin, successful run, run end, agenda score/steal and access; source snapshots survive movement where the CR requires. | Cookbook, Fermenter, Nico, Pantograph, Pennyshaver |
| ENG-410 | Removed-from-game zone and self-removal as a paid-ability cost; shuffle up to N chosen Archives cards into R&D. | Spin Doctor |
| ENG-411 | Click-cost run actions with server/history constraints, run-source context, run-end success tests, and per-run breach-access modifiers. | Conduit, Red Team |
| ENG-412 | Conditional install and ability-cost modification from explicit turn facts, with base/effective cost and reasons exposed to observations. | Carmen, Marjanah |
| ENG-413 | Per-turn fact/counter framework: successful runs, servers/centrals run, cards accessed/trashed, first-X consumption, agenda changes, tag changes, and reset at the formal turn boundary. | DZMZ (4.4), Loup/NBN (4.7) |

Cards (13 new + 1 completion):

| Side | Cards |
|---|---|
| Runner program | Fermenter (30007), Carmen (30015), Marjanah (30016), Conduit (30024) |
| Runner hardware | Pennyshaver (30014) |
| Runner resource | Red Team (30018), Telework Contract (30027), Smartware Distributor (30033), Cookbook (30009) |
| Runner console | Carnivore (30003), Pantograph (30023) |
| Corp asset | Spin Doctor (30053), Regolith Mining License (30071) |
| M2 completion | Nico Campaign (30037) — full printed text (rez load 9, turn-begin take 3, empty → trash + draw); verify M2 capability compatibility per D-401 |

Supporting work:

| Sub-unit | Content |
|---|---|
| AGT-401 | Agent v2: uses paid abilities (click spend, once-per-turn limits). |
| SIM-401 | Sim decks extended with batch-2 cards; headless proof includes an ability-driven turn (e.g. Carnivore access trash). |
| UI-404 | Paid-ability affordances: cards whose ability is currently usable get a non-color "ABILITY READY" cue plus the issued legal action; once-per-turn abilities show "USED" state; hosted counter/credit chips distinguish token type; console/unique conflicts explain the required trash/checkpoint result; removed-from-game movement gets a feed line and inspectable history. |
| OBS-401 | Observation tests: Carnivore grip trash (Runner's own zone), hosted-pool visibility to both sides, Spin Doctor removal. |

Exit: 13 previously unbound cards plus Nico completion are tested/reviewed
(report at 32 registered / 31 complete of 77); Nico Campaign is fully
bound with ruling note; headless proof + client demo as per the standard.

### Slice 4.4 — Batch 3: conditional/static modifiers and replacements (≈2 weeks)

The "systems" batch: generalizes the ad-hoc M2 strength/cost handling into
data-driven modifier systems with displayable reasons.

| Sub-unit | Primitive | Representative cards |
|---|---|---|
| ENG-414 | General value-modifier system, first exercised for strength: static, per-installed-X, variable pump, counter-threshold and encounter-scoped modifiers compose with printed values; ordered calculation terms and localization keys are exposed, not preformatted UI strings. Adapt/replace M2 `EncounterStrength` special cases. | Echelon, Unity, Leech |
| ENG-415 | Rez-cost modification as a run-scoped lingering effect. | Tread Lightly (30012) |
| ENG-416 | Access-count modification: per-server, per-run, first-this-turn additional breach access. | Docklands Pass (30013) |
| ENG-417 | Replacement on basic actions (reuse the existing replacement interrupt against a new `basic draw` occurrence). | Verbal Plasticity (30034) |
| ENG-418 | First-X-each-turn consumption (completes ENG-413's counter framework): a card's "first time each turn" ability fires, then consumes. | DZMZ Optimizer (30022) |

Cards (7): Echelon (30025), Unity (30026), Leech (30008), Tread Lightly
(30012), DZMZ Optimizer (30022), Docklands Pass (30013), Verbal Plasticity
(30034).

Supporting work: AGT-401 v3 (agent respects cost modifiers and once-per-turn
consumption in its utility scores); SIM-401 extended; UI-405:

| Sub-unit | Content |
|---|---|
| UI-405 | Effective-strength display with reasons ("STR 3 +2 (per installed icebreaker: 2)"); cost lines show modifiers ("install 2 → 0 (successful run this turn)"); "used this turn" chips on first-X cards; breach presentation notes extra access ("+1 first HQ breach this turn"). |

Exit: 7 cards bound/tested/registered (report at 39 registered / 38 complete
of 77); the headless proof includes a run where Tread Lightly's rez-cost
modifier and Leech's
encounter-scoped strength both fire in the same encounter (timing fixture).

### Slice 4.5 — Batch 4: run events and encounter/access interactions (≈2–3 weeks)

| Sub-unit | Primitive | Representative cards |
|---|---|---|
| ENG-419 | Card-triggered approach/encounter reactions integrated with the existing paid windows: optional Corp trigger at approach with compound costs (Anoetic), mandatory "unless" choice at approach (Manegarm), and encounter trigger (Funhouse). These are reactions/checkpoints, not replacement paid windows. | Manegarm, Anoetic Void, Funhouse |
| ENG-420 | Structured alternatives with distinct acting players and semantics: pay credits, spend clicks, take a tag, or accept end-the-run. Paying/spending/taking/losing are not interchangeable effect verbs. | Funhouse, Manegarm |
| ENG-421 | Runner-useable ability printed on encountered bioroid ICE: `lose 1 click` as a paid-ability cost to break one subroutine. It is not an action and has no once-per-encounter limit; it may be used repeatedly while clicks and unbroken subroutines remain. | Ansel 1.0, Brân 1.0 |
| ENG-422 | Card-effect Corp installs from HQ/Archives with hidden-zone-safe choices. Ansel performs a normal legal install and pays install costs; Brân installs one piece of ICE directly inward from itself and ignores all costs. Reveal only when CR 8.5.13 requires verification, preserve Archives faceup/facedown knowledge, and update active-run position/card identity correctly when ICE topology changes mid-run. | Ansel 1.0, Brân 1.0 |
| ENG-423 | Run-scoped restrictions: "cannot steal or trash Corp cards for the remainder of this run" (legal-action filtering + state). | Ansel 1.0 |
| ENG-424 | Ability-created delayed run-end effect scoped to the run during which Mayfly's interface ability was used. Merely having Mayfly installed must not trash it at run end. Multiple uses still create the correct single cleanup result. | Mayfly (30032) |
| ENG-425 | Run-event variants: choose HQ or R&D, draw-on-success, breach +1 (reuses ENG-411). | Jailbreak (30028) |
| ENG-425A | Corp-selected trash targets for installed Runner cards, including unrestricted installed-card targeting and program-only targeting reused in 4.6. | Ansel 1.0, Ballista (4.6) |
| SCN-401 | Scenario fixtures for every new trigger/window (approach optional/mandatory, encounter "unless", repeated bioroid ability use, normal versus ignore-cost install, Mayfly used/not used) with CR citations; `RunTimingTraceValidator` checks order without inventing new priority windows. | — |

Cards (7): Jailbreak (30028), Mayfly (30032), Funhouse (30054), Manegarm
Skunkworks (30042), Anoetic Void (30050), Ansel 1.0 (30038), Brân 1.0 (30039).

Supporting work: AGT-401 v4 (agent makes approach/encounter choices, uses ICE
abilities, responds to "unless" windows); SIM-401 extended; UI-406:

| Sub-unit | Content |
|---|---|
| UI-406 | Approach/encounter decision presentation: prominent focus panel lists each issued option, acting player, cost, and fallback; do not reveal a hidden installed card unless the observation says it was revealed. Show the run restriction for its duration, bioroid ability while legally usable, and Mayfly's armed run-end effect after (and only after) its interface ability is used. |

Exit: 7 cards bound/tested/registered (report at 46 registered / 45 complete
of 77); SCN-401 fixtures pass the timing trace validator; headless proof: full
run hitting Manegarm's
"end unless" trigger, Ansel's normal-cost install, Brân's inward ignore-cost
install, and Funhouse's tag-or-pay trigger, driven by both scripted-human and
contract-agent policies (M2 gate pattern).

### Slice 4.6 — Batch 5: damage/tag, conditional values and persistent interactions (≈2–3 weeks)

| Sub-unit | Primitive | Representative cards |
|---|---|---|
| ENG-426 | Damage instructions from subroutines/access/operations/turn triggers, using the existing interrupt/checkpoint/flatline machinery. Karunā's first subroutine resolves 2 net damage and then offers the Runner a jack-out instruction; this is a card-specific sequence, not a generic mid-subroutine jack-out window or damage prevention. | Diviner, Karunā, Tithe, Urtica, Clearinghouse, Neurospike |
| ENG-427 | Tag instructions and persistent tag state; "unless pay N" via ENG-420; rez-during-run-against-this-server and per-turn first-tag facts. Complete the tag-related basic actions from ENG-402. | Ping, Funhouse (4.5), Public Trail |
| ENG-428 | Conditional/variable subroutine values: effect depends on the trashed card's printed cost (Diviner parity), on turn-scoped state (Neurospike X = agendas scored this turn), with the computation exposed for display. | Diviner, Neurospike |
| ENG-429 | Corp trash targeting with type filters and alternatives: Ballista resolves "trash 1 installed program or end the run" as a Corp choice (with only legal alternatives offered); Retribution chooses an installed program or hardware. | Ballista, Retribution |
| ENG-430 | Play-condition legality: cards playable only when a `last turn`/current fact holds (legal-action builder consults ENG-413 state). | Public Trail, Retribution |
| ENG-431 | Advanceability and advancement-counter-conditioned effects: only agendas and cards whose text permits it can be advanced; Urtica triggers when accessed while installed; Clearinghouse may trash itself at turn begin and counts counters from its cost snapshot; Pharos uses ENG-414. | Urtica Cipher, Clearinghouse, Pharos |
| ENG-432 | Per-run context plus persistent ability semantics: track agendas stolen during this run, and keep AMAZE's run-end ability applicable for that run even if AMAZE is trashed while accessed. Do not keep unrelated trashed-card abilities active. | AMAZE Amusements |
| SCN-402 | Scenario fixtures for damage/checkpoint/flatline, Karunā damage-then-jack-out ordering, tag-or-pay, Ping's rez condition, persistent AMAZE, and conditional X, with CR citations and trace-validator coverage. Existing generic interrupt tests remain green; no Gateway card invents a damage-prevention ability. | — |

Cards (12): Diviner (30046), Karunā (30047), Ballista (30062), Tithe (30073),
Ping (30055), Pharos (30063), Urtica Cipher (30045), Clearinghouse (30061),
AMAZE Amusements (30058), Neurospike (30049), Public Trail (30057),
Retribution (30065).

Supporting work: AGT-401 v5 (agent responds to damage-follow-up and tag
choices and chooses trash targets by score); SIM-401 extended; UI-407:

| Sub-unit | Content |
|---|---|
| UI-407 | Damage presentation: amount, type, source, and flatline risk; show Karunā's jack-out choice only after its first damage instruction resolves. Add tag chips/"TAGGED" state, tag gain/removal feed, "unless pay" choices, and conditional-value calculation terms from the observation (for example Neurospike's scored-agenda point sum), without recomputing rules in Godot. |

Exit: 12 cards bound/tested/registered (report at 58 registered / 57 complete
of 77); SCN-402 fixtures pass; headless proof: a run that resolves Karunā's
damage followed by its
jack-out choice, then a separate Urtica access with per-counter damage, under
both policies.

### Slice 4.7 — Batch 6: identities, agendas, upgrades, hosting and unique timing (≈3–4 weeks)

| Sub-unit | Primitive | Representative cards |
|---|---|---|
| ENG-433 | Identity behavior registrations using the generic contracts: maximum-hand modifier; first access-trash; formal discard-phase-end check; first tag each turn; advance-from-zero; score/steal ICE swap; and run-end credit per access. Tutorial identities enforce starter-preset legality but have no gameplay ability. | all 9 identities |
| ENG-434 | Agenda registrations: on-score and on-score-or-steal triggers, gain clicks, draw, targeted trash, HQ/Archives shuffle choices, conditional tag/damage, ignore-cost rez, scored-area maximum-hand modifier, and Luminal's remainder-of-turn score restriction. Scoring stays in `(S)` windows. | 7 agendas + Offworld Office (complete) |
| ENG-435 | Hosting/Trojan rules: install only hosted on ICE, host relationship in state/observation, hosted counter as a break cost, host uninstall causing program trash, and derez (inactive/facedown but installed and re-rezzable). Tranquilizer checks its threshold after each placement. | Botulus, Tranquilizer |
| ENG-436 | Exact per-run access occurrence count for Zahya and score/steal trigger context for Tāo/Pantograph/agenda abilities. | Zahya Sadeghi |
| ENG-437 | Malapert's score-location snapshot and non-agenda R&D search/set-aside/shuffle/reveal/add-to-HQ sequence (CR 8.7.2–8.7.4); Tāo swaps two installed ICE positions without reinstalling or changing host relationships. ICE swaps during an agenda steal must update an active run by stable ICE/position semantics rather than stale list indexes. | Malapert Data Vault, Tāo Salonga |
| RULE-401 | Close the primitive audit with zero unowned gaps; re-run normal-game proofs for 7 agenda points, flatline (including negative max hand at discard), and required-draw deck-out. Verify active/inactive identity, scored/stolen agenda, unique, console, host, derez, and cannot-score interactions. | — |
| AGT-402 | `FullGameContractAgent` complete: setup/mulligan policy (keep), full turns, all run windows, endgame. Deterministic on fixed seed. | — |

Cards (19 new + 1 completion):

| Side | Cards |
|---|---|
| Runner identity | René "Loup" Arcemont (30001), Zahya Sadeghi (30010), Tāo Salonga (30019), The Catalyst (30076, starter-only, no ability) |
| Corp identity | Haas-Bioroid (30035), Jinteki (30043), NBN (30051), Weyland (30059), The Syndicate (30077, starter-only, no ability) |
| Corp agenda | Luminal Transubstantiation (30036), Longevity Serum (30044), Tomorrow's Headline (30052), Above the Law (30060), Orbital Superiority (30068), Send a Message (30069), Superconducting Hub (30070) |
| Corp upgrade | Malapert Data Vault (30066) |
| Runner program (Trojan) | Botulus (30004), Tranquilizer (30017) |
| M2 completion | Offworld Office (30067) — on-score gain 7 credits; verify the M2 capability fingerprint remains compatible per D-401 |

Supporting work:

| Sub-unit | Content |
|---|---|
| UI-408 | Complete identity panel and card-specific choices: ICE swap previews both resulting positions; optional Haas/agenda choices identify controller; NBN shows gain 2/draw 2; Zahya shows the engine-provided access count; derez changes state non-color; Trojan cards render as children of host ICE with inspect/focus paths. Extend the setup screen from UI-403 to official basic/expanded presets and add the full outcome/new-match flow. |

Exit: all remaining cards are bound/tested/registered; coverage report reaches
77/77 and the official presets are content-complete. The final integration gate
is intentionally separate so batch review is not hidden inside stabilization.

### Slice 4.8 — Full-game integration and M4 gate (≈1–2 weeks)

| Sub-unit | Content |
|---|---|
| DATA-401B | Revalidate all four official code/quantity presets against the authenticated runtime catalog and published lists. Basic presets are Catalyst Runner 30 and Syndicate Corp 34; expanded starter+booster presets are Runner 40 and Corp 44. Record provenance and do not invent a 50-card "official" deck. |
| MATCH-402 | Enable side/preset selection, one mulligan per player in Corp-then-Runner order, restart/new match, and deterministic seed injection for the client and CLI. The client never starts a deck with incomplete behavior coverage. |
| GATE-401 | `M4_FULL_SMOKE`: teaching preset to a real outcome with scripted human policy versus contract agent, with the agent in each role in separate runs. Full regression includes M1/M2/M3, schema/replay round-trips, card-data/content build validation, 77/77 coverage/checklist, preset validation, and an action-count stall cap with diagnostic snapshot. |
| UX-401 | Accessibility/layout re-check at 1280×720 and 2560×1440 for setup, five-card hand, dense server/rig, nested choice, discard prompt, run focus and outcome. Verify keyboard focus order, reduced motion, non-color state cues, scroll containment, and readable metadata markup. |

Exit: the M4 gate (§7) passes. The forecast is gate-driven; the original
12–18 week milestone should be revised if the primitive audit or content-build
lane shows that the integration reserve is insufficient.

## 5. Complete card assignment

All 77 designs, each assigned to exactly one slice. "M2" = bound since M2
(5 complete, 2 completed in M4).

Batch 1 (slice 4.2): 30040, 30041, 30048, 30056, 30064, 30075 (corp ops);
30002, 30011, 30020, 30021, 30030 (runner events); 30031 (runner hardware).

Batch 2 (slice 4.3): 30007, 30015, 30016, 30024 (runner programs); 30014
(runner hardware); 30009, 30018, 30027, 30033 (runner resources); 30003,
30023 (runner consoles); 30053, 30071 (corp assets); 30037 (M2 completion).

Batch 3 (slice 4.4): 30008, 30012, 30013, 30022, 30025, 30026, 30034.

Batch 4 (slice 4.5): 30028, 30032 (runner); 30038, 30039, 30042, 30050, 30054
(corp).

Batch 5 (slice 4.6): 30045, 30046, 30047, 30049, 30055, 30057, 30058, 30061,
30062, 30063, 30065, 30073.

Batch 6 (slice 4.7): 30001, 30010, 30019, 30076 (runner identities); 30035,
30043, 30051, 30059, 30077 (corp identities); 30036, 30044, 30052, 30060,
30068, 30069, 30070 (corp agendas); 30066 (corp upgrade); 30004, 30017
(runner Trojans); 30067 (M2 completion).

Already complete (audited in 4.1): 30005, 30006, 30029, 30072, 30074.

## 6. Standard per-batch process

Each slice follows the plan's per-batch process, concretized:

1. Primitives first: every new primitive lands with its own engine tests
   (positive + negative) before any card uses it. New timing windows also get
   a scenario fixture with CR 26.03 citations and a jnet behavior cross-check
   (not a code copy).
2. Bind or complete all batch card codes: compiled specs in `Netrunner.Cards`,
   registered in the catalog; spec-vs-text diff check — a reviewer confirms the
   spec
   encodes the full printed text (this caught the Nico/Offworld gaps that
   M2's partial specs had).
3. Tests: ≥1 positive test per design (tagged with the D-403 test metadata), plus
   negative, interaction, and ruling-derived tests proportional to complexity.
4. Observation tests for any reveal/hidden-zone behavior (search, hidden
   install, Archives, facedown zones) using the existing golden/negative
   pattern.
5. Mark the batch complete in `data/m4/batch-registry.json`; regenerate the
   coverage report; `verify.sh` must show no drift.
6. Extend sim decks (D-405) and the contract agent; run the slice headless
   proof (agent-driven, seed-pinned).
7. Client demonstration: the slice's UI sub-unit is exercised in a
   smoke-flagged scene; M3 smoke + latency smoke stay green.
8. Record the slice in `docs/M4_PROGRESS.md` (status, deviations, evidence,
   pins) in the M2/M3 doc style.

Card review checklist row (in the coverage report, per card): spec-vs-text
check (done/by), CR/ruling citations from `data/m4/rulings.json` (jnet
cross-check noted where behavior is non-obvious), fully-qualified test IDs
auto-collected from test metadata, and observation note or explicit `N/A`.
The reviewer field is evidence of review, not a security control. A card in a
completed batch with any required field empty fails CARD-401.

## 7. M4 gate checklist

- [ ] Coverage report: 77/77 bound, tested, checklist complete; no drift.
- [ ] CARD-401 validator green in CI and `verify.sh`.
- [ ] Full headless regression green: all suites, M1 win-path proofs, M2 proof
      (final pins), M3 smoke, card-data validation.
- [ ] DATA-400: authenticated gitignored metadata is transformed into the
      content-enabled client build; missing/corrupt inputs have tested diagnostics.
- [ ] M4_FULL_SMOKE: human vs contract agent finishes a teaching-preset game
      through the client, with the agent on each side in separate runs.
- [ ] MATCH-401: setup/mulligan headless tests (Corp then Runner, zero or one
      mulligan each, mandatory keep of the second hand, deck validation rejects
      each invalid class, 5-credit/5-card start, Corp first turn).
- [ ] RULE-401: all three win paths reachable in real games and tested.
- [ ] DATA-401: official basic 30-Runner/34-Corp and expanded 40-Runner/44-Corp
      teaching presets have recorded provenance and load, validate, and play.
- [ ] UI-408: setup screen, identity panel, outcome screen, all choice
      windows click-first; accessibility re-check at 1280×720 and 2560×1440
      with full-game content.
- [ ] `docs/M4_PROGRESS.md` complete with evidence and any deviations.

## 8. UI/UX work index

| Sub-unit | Slice | Content | PRD |
|---|---|---|---|
| UI-401 | 4.1 | Injected catalog metadata, safe text formatter, provisioned dev gallery | UI-001, UI-004, UI-006 |
| UI-402 | 4.1 | Theme resource; incremental region extraction with focus preservation | UI-002 |
| UI-403 | 4.2 | Setup/mulligan, turn/priority, all basic actions, install/score/run/discard UI | UI-001, UI-003, UI-005 |
| UI-404 | 4.3 | Paid-ability affordances, once-per-turn state, removal presentation | UI-001, UI-002 |
| UI-405 | 4.4 | Strength breakdown with reasons, modified cost display, per-turn chips, access-modifier notes | UI-001, UI-002, UI-005 |
| UI-406 | 4.5 | Approach/encounter triggers, observation-safe installs, run restriction, ICE/Mayfly ability cues | UI-002, UI-005 |
| UI-407 | 4.6 | Damage/flatline presentation, tag UI, "unless pay" windows, conditional-value breakdown | UI-001, UI-005 |
| UI-408 | 4.7 | Identity/preset completion, outcome screen, swap/choice windows, derez/host display | UI-001, UI-003, UI-005 |
| UX-401 | 4.8 | Full-content keyboard/accessibility/layout/reduced-motion gate | UI-001–UI-007 |

Carried over from M3 (no re-work, only extension as content grows): event
animation queue + speed + reduced motion + batch skip (UI-305), click-first
actions + target staging + cancel/back + stale feedback (UI-304), card zoom/
details (UI-303), coordinator worker boundary (UI-301).

## 9. Risks

- **Engine concentration.** `GameEngine.Run.cs` (1,922 lines) plus the new
  domain partials will keep growing. Mitigation: D-408 split happens in 4.1,
  each primitive lands in its own partial, and every slice keeps the full
  suite green.
- **Version/pin risk.** The M2 proof pins and M3 smoke depend on the seven-card
  scenario, and version strings are part of canonical state. Mitigation:
  D-401 performs one explicit migration with semantic trace diff, separates
  data and behavior versions, and checks M2 capabilities rather than global
  catalog equality so additive batches do not cause repeated pin churn.
- **Card-text ambiguity.** Some Gateway texts need ruling-level
  interpretation (e.g. "end the run unless …" ordering and normal versus
  ignore-cost installs from Archives). Mitigation: the review checklist
  requires CR 26.03 citations plus a jnet cross-check; a genuine conflict stops
  the slice and becomes an ADR, not a silent choice.
- **Scope.** 70 incomplete cards + full rules/setup/presets in a 12–18 week
  target is aggressive. Mitigation:
  the per-slice gate is the scope controller; any batch may be split
  horizontally (e.g. 4.6's damage subroutines from its trash-targeting work)
  without breaking the dependency order, since primitives are the ordering
  unit, not cards.
- **UI refactor risk.** Splitting the 1,868-line `Main.cs` can regress the
  M3 smokes. Mitigation: UI-402 is behavior-neutral and gated on both M3
  smokes passing unchanged; region extraction happens before any new content
  lands.
- **Observation leaks.** New hidden-zone operations (search, hidden install,
  Archives manipulation) are leak surface. Mitigation: OBS-401 tests are part
  of every affected slice's exit criteria, and the existing observation
  goldens must stay byte-stable.
- **Ignored content input.** A developer can have valid card data while a clean
  CI/package lane does not. Mitigation: DATA-400 makes the content-enabled build
  explicit, hash-validates its input, embeds/copies its output, and provides a
  separate synthetic clean-CI lane. Public redistribution approval remains an
  RC checklist item.
- **Generic-framework overreach.** A universal card DSL could consume the
  milestone, while card-specific engine switches would make later sets brittle.
  Mitigation: D-415 supports only constructs found by `M4_PRIMITIVES.md`, keeps
  narrow typed extension handlers, and requires a second real card before a
  speculative abstraction is generalized.

## 10. Progress tracking

`docs/M4_PROGRESS.md` is created when 4.1 starts and updated at every slice
boundary, following the `docs/M2_PROGRESS.md` / `docs/M3_PROGRESS.md`
convention: status line, date, rules/data baseline, architecture record,
evidence (test counts, pins, smoke output), and documented deviations.
