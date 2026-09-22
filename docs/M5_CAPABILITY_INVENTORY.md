# M5.0 capability and decision inventory

Baseline: `8f27bac` production code, full-game contract policy v6.
This is an evaluation-work inventory, not a claim of missing card rules.
All 77 designs already have full behavior and registered rules tests in
[the generated coverage report](generated/gateway-coverage.md).

## Observation and metadata seams for M5.1

| Feature | Available input today | Gap / implementation owner |
|---|---|---|
| Decision owner/kind/revision | `Observation.Decision`, viewer-owned only; absent from serialized JSON | AI typed reader; explicit fixture envelope or rebuild through Core |
| Credits/clicks/agenda points | `PublicState.players`; typed issued cost payloads | AI utility/features, not new authoritative state |
| Tags, hand size, memory | Player rule projections; some keys conditional on state | Reader distinguishes absent/not applicable from zero |
| Own grip/HQ | `PrivateState.grip` / `hand`; owned identities/counters | Known-card metadata join; no private-opponent input |
| Decks and searches | Counts and empty private deck/stack arrays; currently permitted search actions | Never join the full selected opponent deck or inspect draw order; exact candidate facts need a viewer-safe audit |
| Installed/server state | Public servers, ICE order, rig, discard/score; owned face-down information | Typed topology reader; track concealed slots without reconstructing identity |
| Card values/classes/subtypes | `IRuntimeCardCatalog` / `CardRuntimeSpec` | Reuse printed metadata; does not contain a complete effect/ICE/breaker evaluator |
| Costs and replacements | Action parameters, including `CostQuote` and selected replacement IDs | Current quote wins over printed cost; compute lost investment from permitted known cards |
| Counter eligibility | Typed counter count/purpose/spend restriction and legal ability costs | Distinguish card counters from expiring run-credit pools |
| Run/encounter | Public phase/step/ICE, broken/resolved subroutines, effective strength quotes, run credits | Bounded encounter solver plus public definition-level effect descriptors |
| Trigger/ability semantics | Typed legal parameters and compiled behavior definitions | Cards supplies detached static descriptors through Core interfaces; no prose parser |
| Recent conditions | Filtered turn facts, first-use and run facts; fresh legal actions | Typed condition features; do not feed UI-only `HandConditions` into agents |
| Knowledge/history | Only current observation reaches `ChooseActionAsync` | Optional per-side observation lifecycle and checkpoint in M5.1; no authoritative events/traces |
| Difficulty/randomness | Current policy is deterministic, stateless and not difficulty-parameterized | New policy configuration and serialized separate RNG; retain v6 unchanged |
| Saveable policy state | Current contract agent has none; session manifest records agent identity/version | Versioned policy checkpoint/lifecycle in M5.1 and durable session integration in M5.5 |

Sources: [Core observations](../src/Netrunner.Core/ObservationBuilder.cs),
[agent contract](../src/Netrunner.Core/Domain.cs),
[runtime metadata](../src/Netrunner.Core/RuntimeCardCatalog.cs),
[typed actions](../src/Netrunner.Core/ActionContracts.cs),
[contract policy](../src/Netrunner.AI/FullGameContractAgent.cs).

Do not extend the observation merely to copy all authoritative state. First
identify the missing value and test paired hidden-state observations. Printed
ICE effects/strength and breaker capabilities need a static descriptor adapter;
dynamic discounts, memory, restrictions and permissions stay Core-owned.

M5.1 implementation: [typed features and lifecycle](M5_AGENT_FOUNDATION.md) now
cover the projection/descriptor seams above. All 77 compiled designs have
descriptor coverage with explicit approximate evaluation. Owner-only private
search definitions are projected through existing visibility permission;
hidden deck order remains unavailable. Strategic utility, encounter planning
and policy tuning remain M5.3–4 work. The baseline inventory below is retained
as historical evidence.

## Complete decision inventory

Counts below refer to the first attempt of each of the 20 baseline games, not
the duplicate determinism runs. “Not sampled” means the small deck/seed matrix
did not reach it; it is not evidence that a handler or rule is absent.

| DecisionKind | Contract v6 selection | M5 work | Baseline sampled |
|---|---|---|---|
| `Setup` | Always selects typed keep value 0 | Opening-hand quality and legal mulligan sequence | Corp 20; Runner 20 |
| `TurnTiming` | Score, rez, bounded ability, then pass | Reserves, score value and useful paid windows | Corp 3,977; Runner 3,870 |
| `TurnAction` | Fixed class order; small current install-cost adjustment | Economy, tempo, server/rig value, risk and future costs | Corp 902; Runner 1,130 |
| `InstallChoice` | Finalize if possible; otherwise semantic ordering | Replacement loss, target/destination consistency | Corp 239; Runner 6 |
| `TurnDiscard` | Semantic card-ID ordering | Keep useful coverage/economy and avoid unnecessary agenda exposure | Runner 8 |
| `RunTiming` | Fixed run action priorities | Target/risk, relevant rez, pumps/breaks, payments and access value | Corp 1,023; Runner 3,098 |
| `MemoryCleanup` | First legal trash-program action | Remove redundant/exhausted programs before unique answers | Not sampled |
| `TriggerChoice` | Use whenever available | Compare optional benefit/cost, including lethal optional draws | Corp 21; Runner 23 |
| `TriggerOrdering` | First issued ordering action | Resolve dependencies and finish every required trigger once | Not sampled |
| `InterruptChoice` | Maximum prevention; prefer replacement use | Generic engine regression retained; audit actual Gateway reachability before scoring tactical coverage | Not sampled |
| `BehaviorChoice` | Fixed mode/semantic preference; selected Corp public-target scoring | Search, additional costs, mode value and coherent compound choices | Corp 49; Runner 111 |
| `IceSubroutineChoice` | Fixed branch ordering; selected public target and wallet-cost scoring | Survival, program value, typed payment/exit branches | Runner 111 |
| `ChooseNumber` | Lowest typed number | Generic resolver contract; actual strategic use must be identified by fixture | Not sampled independently of Setup |
| `ChooseCard` | Semantic card-ID ordering | Generic resolver contract; prefer typed contextual value where reachable | Not sampled independently of TurnDiscard |
| `ToyTurn` | Unsupported by full-game agent | Deliberately outside Gateway; retain toy-agent tests separately | Not applicable |

There are 15 Core decision kinds; nine occurred in this baseline. Setup actions
have `ActionKind.ChooseNumber`, and discards use `ActionKind.ChooseCard`, but these
do not establish coverage of `DecisionKind.ChooseNumber` or `ChooseCard`.
Future metrics must preserve this distinction.

## Evaluation map for all Gateway designs

Each design is listed exactly once. Entries describe what the heuristic needs
to value; they do not duplicate rules text or authorize actions. Existing
metadata/test links remain in the generated coverage report. M5.1 should add
descriptor capability tests against this list and mark approximate evaluation
explicitly rather than pretending to compute exact hidden-state value.

| Code | Design | Evaluation work for M5 |
|---|---|---|
| 30001 | René “Loup” Arcemont: Party Animal | access-trash reward; first-use tracking |
| 30002 | Wildcat Strike | modal economy/draw; opponent choice |
| 30003 | Carnivore | access replacement; grip additional costs |
| 30004 | Botulus | host legality; virus-counter break planning |
| 30005 | Buzzsaw | decoder coverage; pump/break cost |
| 30006 | Cleaver | fracter coverage; multi-subroutine break |
| 30007 | Fermenter | counter growth versus cash-out timing |
| 30008 | Leech | virus strength reduction; encounter costs |
| 30009 | Cookbook | virus-install trigger; counter value |
| 30010 | Zahya Sadeghi: Versatile Smuggler | run-access reward; optional-use timing |
| 30011 | Mutual Favor | private search; conditional install |
| 30012 | Tread Lightly | run event; Corp rez surcharge |
| 30013 | Docklands Pass | first-HQ-access modifier |
| 30014 | Pennyshaver | console replacement; run-funded economy |
| 30015 | Carmen | killer coverage; successful-run install discount |
| 30016 | Marjanah | fracter coverage; conditional break discount |
| 30017 | Tranquilizer | hosted virus counters; derez timing |
| 30018 | Red Team | run action; server restriction; hosted rewards |
| 30019 | Tāo Salonga: Telepresence Magician | ICE swap topology; optional trigger |
| 30020 | Creative Commission | economy; conditional click consequences |
| 30021 | VRcation | draw value; click budget |
| 30022 | DZMZ Optimizer | memory; first-program install discount |
| 30023 | Pantograph | agenda trigger; optional install affordability |
| 30024 | Conduit | R&D access growth; run ability |
| 30025 | Echelon | killer coverage; rig-dependent strength |
| 30026 | Unity | decoder coverage; rig-dependent strength |
| 30027 | Telework Contract | finite click economy; remaining payouts |
| 30028 | Jailbreak | run plus access modifier |
| 30029 | Overclock | expiring run-credit pool |
| 30030 | Sure Gamble | upfront economy investment |
| 30031 | T400 Memory Diamond | memory; hand-size value; console slot |
| 30032 | Mayfly | all-type coverage; disposable breaker cost |
| 30033 | Smartware Distributor | deferred economy; hosted-credit timing |
| 30034 | Verbal Plasticity | first basic-draw replacement |
| 30035 | Haas-Bioroid: Precision Design | optional recovery; hand size |
| 30036 | Luminal Transubstantiation | score clicks; scoring restriction |
| 30037 | Nico Campaign | payout horizon; replacement loss |
| 30038 | Ansel 1.0 | bioroid clicks; install/trash branches |
| 30039 | Brân 1.0 | bioroid clicks; optional ICE install |
| 30040 | Seamless Launch | advancement target and scoring line |
| 30041 | Sprint | ordered draw/filter/deck choices |
| 30042 | Manegarm Skunkworks | defensive choice; payment opportunity cost |
| 30043 | Jinteki: Restoring Humanity | discard visibility condition; income |
| 30044 | Longevity Serum | score filtering; shuffle and knowledge invalidation |
| 30045 | Urtica Cipher | advanced ambush risk; damage |
| 30046 | Diviner | damage; subroutine priorities |
| 30047 | Karunā | damage; paid exit choice |
| 30048 | Hansei Review | economy with required discard |
| 30049 | Neurospike | conditional damage line; grip risk |
| 30050 | Anoetic Void | run defense; hand-card additional costs |
| 30051 | NBN: Reality Plus | first-tag reward; modal benefit |
| 30052 | Tomorrowʼs Headline | score/steal tag consequences |
| 30053 | Spin Doctor | draw versus recovery; self-trash timing |
| 30054 | Funhouse | tag/payment branches; taxing ICE |
| 30055 | Ping | encounter tag; end-run protection |
| 30056 | Predictive Planogram | modal economy/draw |
| 30057 | Public Trail | run-history condition; tag payment |
| 30058 | AMAZE Amusements | access-history tag consequence |
| 30059 | Weyland Consortium: Built to Last | first advancement reward; scoring budget |
| 30060 | Above the Law | optional scored resource trash target |
| 30061 | Clearinghouse | advancement economy versus damage timing |
| 30062 | Ballista | program-trash branch; breaker threat |
| 30063 | Pharos | high-strength barrier; break planning |
| 30064 | Government Subsidy | upfront economy threshold |
| 30065 | Retribution | tagged installed-card target value |
| 30066 | Malapert Data Vault | private search; optional trigger |
| 30067 | Offworld Office | score economy; agenda value |
| 30068 | Orbital Superiority | score tag/damage branch |
| 30069 | Send a Message | score/steal free rez target |
| 30070 | Superconducting Hub | optional score draw; deck-out avoidance; hand size |
| 30071 | Regolith Mining License | finite economy payouts; remaining value |
| 30072 | Palisade | barrier coverage; end-run defense |
| 30073 | Tithe | credit gain and damage subroutines |
| 30074 | Whitespace | resource-dependent run end |
| 30075 | Hedge Fund | upfront economy investment |
| 30076 | The Catalyst: Convention Breaker | no ability; opening/hand/rig policy |
| 30077 | The Syndicate: Profit over Principle | no ability; opening/server/scoring policy |
