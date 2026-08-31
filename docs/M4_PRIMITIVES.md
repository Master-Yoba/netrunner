# M4 primitive audit — System Gateway

Status: Slice 4.1 audit baseline, 31 August 2026.  This is an implementation
inventory, not executable rules text.  Card rules remain inert metadata until
a compiled behavior registration and tests land.

The rules authority is [Null Signal Comprehensive Rules
26.03](https://rules.nullsignal.games/).  The card-text baseline is the
authenticated, local-only System Gateway snapshot described by
`data/vendor/system-gateway/manifest.json`.  The audit deliberately uses
`data/m4/rulings.json` for the CR citation and jnet cross-check attached to
each row.

## Audit contract

Each primitive has an owning slice.  A card batch cannot close until every
primitive it uses has a positive and negative test, an observation test when
hidden information is involved, and a serializable/replayable frame wherever
the primitive can yield.  `planned` tests in this document identify the test
contract to add; they are not claimed as completed evidence.  The checked-in
coverage report currently records 7 registered cards, 5 full-text-complete
cards, 2 partial registrations (Nico Campaign and Offworld Office), and 70
unregistered cards.

## Primitive inventory

| Primitive | CR 26.03 authority | Owning slice | Current gap at M4 start | Hidden-information impact | Serialization/replay impact | Representative tests |
|---|---|---|---|---|---|---|
| `CATALOG-401` immutable behavior catalog | D-401/D-415/D-417; 9.1 | 4.1 | Core has a process-wide mutable compatibility catalog and type-specific registration only. Build an immutable, versioned catalog with duplicate/conflict validation and a compatibility adapter. | Catalog lookup is not an observation channel; an opponent must never receive a definition ID from an observation. | Persist catalog version/fingerprint only; never serialize delegates or registry objects. | `CatalogBuilderTests.DuplicateDefinitionsFail`; `CatalogFingerprintTests.RoundTrips` (planned) |
| `DATA-401` distinct metadata and behavior versions | D-401; 1.8, 8.2 | 4.1 | State currently compares/records M2 versions without a capability fingerprint for additive behavior. | No hidden data; metadata resolver must be read-only. | One deliberate schema/hash migration; record old/new pins and a semantic trace diff. | `VersionMigrationTests.RepinsM2Trace`; `AdditiveRegistrationDoesNotChangeM2Semantics` (planned) |
| `SCHEMA-401` state/frame schema policy | D-416; 8.2, 9.2.8 | 4.1 | New identities, zones, hosts, counters, turn facts, and decisions need explicit fields and canonical ordering. | Observation filtering must happen after canonical state, with no hidden IDs in opponent views. | Deep-copy, invariant, canonical-JSON, replay, and per-yield round-trip tests for every new frame. | `StateSchemaTests.DeepCopyPreservesHosts`; `FrameRoundTripTests.EveryYield` (planned) |
| `TURN-401` turn timing | 5.1.1–5.1.4, 5.3, 5.4, 5.5 | 4.2 | M1 has a toy loop; implement Corp draw/action/discard and Runner action/discard phases, formal begin/end occurrences, forced action-phase end, leftover-click loss, and deck-out. | Drawn HQ/grip cards and deck order remain viewer-private. | Turn/phase, click allotment, first/last-turn facts, and discard decisions are canonical. | `SCN-400.TurnOrder`; `TurnTests.MandatoryCorpDraw`; `TurnTests.NoVoluntaryEndTurn` (planned) |
| `ACTION-401` basic actions | 5.2.3–5.2.7, 10.1.2, 10.5 | 4.2 | Add gain credit, draw, install/play, advance, tagged resource trash, purge, run, and remove-tag with phase/cost legality. | Choices from grip/HQ are owner-private; action affordances expose only legal targets. | Typed action costs and target IDs are included in action frames; derived legal lists are not state. | `SCN-400.BasicActionMatrix`; `ActionLegalityTests.PhaseAndCost` (planned) |
| `SETUP-401` setup and mulligan | 1.6.5–1.6.7 | 4.2 | No Core setup request/state exists. Add faceup identities, five credits, shuffle, five-card hands, Corp-then-Runner one-time mulligan, mandatory second hand, and Corp-first turn. | Each player sees only their own hand and mulligan contents; identity is public. | Setup choices are engine-issued, serializable, replayable, and seed-pinned. | `MatchSetupTests.CorpThenRunnerMulligan`; `ObservationTests.SetupDoesNotLeakHands` (planned) |
| `DECK-401` deck legality | 1.8, 2.2, 3.x, 5.2 | 4.2 | Validate side/identity, minimum size, influence-by-copy, max-three/name, deck limits, Corp agenda range, and exact tutorial preset restrictions. | Validation errors must not disclose the other side's deck contents. | Store deck request and authenticated catalog fingerprint, not a mutable file path. | `DeckValidationTests.RejectsEveryInvalidClass`; `PresetTests.OfficialCounts` (planned) |
| `INSTALL-401` install legality and costs | 8.5.1–8.5.16; 4.6.6–4.6.9 | 4.2 | Implement Runner grip installs and Corp HQ installs, roots/ICE topology, like-card trash, remote create/destroy, active/inactive/facedown status, and ICE count cost. | Hidden Corp cards are not revealed for ordinary type/server verification; reveal only under 8.5.13 ability-specific requirements. | Install step frames carry destination, cost snapshot, like-card choices, status, host, and resulting server topology. | `SCN-400.InstallSteps`; `InstallTests.BrânStyleNoReveal`; `ObservationTests.HiddenInstall` (planned) |
| `PLAY-401` event/operation play | 8.6.1–8.6.7 | 4.2 | Add play cost, active resolution, terminal action-phase ending, play area, and post-resolution trash. | Played card is visible to both players at the prescribed play step; hidden grip choice is not leaked before then. | Play frames preserve cost, effect yields, lingering effects, and cleanup. | `SCN-400.PlaySteps`; `PlayTests.EventOperationCleanup` (planned) |
| `REZ-401` rez and inactive Corp cards | 1.8.3, 3.3, 3.6, 8.1, 8.5 | 4.2/4.5 | Add paid rez windows, printed rez costs, active/inactive state, and re-rez after derez. | Unrezzed identity remains hidden; rez reveals only the card being rezzed. | Persist rezzed state, paid cost, and run timing frame; no hidden definition in opponent observation. | `SCN-401.RezDuringRun`; `RezTests.DerezIsReversible` (planned) |
| `ADVANCE-401` advanceability | 1.18, 5.2.6f | 4.2/4.6 | Only agendas and cards explicitly permitting advancement may be advanced; retain pre-advance counter snapshots. | Installed Corp targets are already visible to the Corp; Runner only learns what normal visibility permits. | Persist advancement counters and printed-cost snapshots used by triggers. | `AdvanceTests.RejectsNonAdvanceable`; `ClearinghouseTests.CostSnapshot` (planned) |
| `SCORE-401` score/steal windows and victory | 1.17.1–1.17.3, 10.3.5c, 7.1.6 | 4.2/4.7 | Scoring is a `(S)`-window option, not a click action; implement advancement requirement, steal, score/steal triggers, and the seven-point checkpoint win. | Accessed agenda becomes visible before mandatory steal; scored areas are public. | Score/steal source and trigger context must be frame data; win is a checkpoint result. | `SCN-400.ScoreAndSteal`; `RuleTests.SevenPointVictoryAtCheckpoint` (planned) |
| `DRAW-401` draw and deck-out | 5.3.2–5.3.3, 8.4.1–8.4.5 | 4.2 | Generic ordered draw, required-draw deck-out, multi-draw effects, and no Runner draw phase are missing. | Drawn cards enter owner-private HQ/grip; only counts are public. | Persist draw count, order, deck-out outcome, and replacement effects. | `DrawTests.MandatoryDrawDeckOut`; `ObservationTests.DrawDoesNotLeak` (planned) |
| `SEARCH-401` search/reveal/shuffle | 8.7.1–8.7.4 | 4.2/4.3/4.7 | Add filters, hidden-zone temporary access, explicit reveal, add-to-zone, and shuffle procedures. | Search contents stay private unless the effect says reveal; Archives visibility follows the rules. | Search, reveal, selected card stable ID, and shuffle event must replay without exposing hidden choices. | `SCN-400.SearchAndShuffle`; `SearchTests.HiddenChoiceFiltering` (planned) |
| `CHOICE-401` cardinality/chooser | 9.11, 9.12; 5.2.4–5.2.5 | 4.2 | Typed exactly/up-to/any-number, alternatives, conditional branches, and another-player chooser are absent. | Observation issues only acting player's prompt, legal targets, and permitted public consequences. | Choices are explicit decision frames with owner, revision, cardinality, and branch; stale decisions reject. | `ChoiceTests.CardinalityAndChooser`; `ObservationTests.OpponentPromptHidden` (planned) |
| `ACCESS-401` access and breach modifiers | 7.1–7.5, 6.7 | 4.4/4.6 | Add additional accesses, exact access occurrence count, access trash triggers, and effect-scoped breach modifications. | Accessed card is temporarily visible; R&D/HQ order and unaccessed cards remain hidden. | Persist access index/count, breach modifier, accessed-card trigger, and run context. | `SCN-402.AccessCount`; `AccessTests.OneMidAccessAbility` (planned) |
| `RUN-401` run duration facts | 6.1–6.9, 9.10 | 4.4/4.5/4.6 | Extend existing M2 timing with installed/successful-this-turn, current/last-turn, run-end delayed effects, and stable ICE position. | Do not expose unrezzed ICE during topology effects; show only observation-safe legal choices. | Run context, server, stable ICE/card IDs, modifiers, and delayed effects round-trip at every yield. | `SCN-401.PositionAfterInstall`; `RunTests.MayflyOnlyAfterUse` (planned) |
| `STRENGTH-401` generic modifiers | 2.3, 6.4–6.5 | 4.4/4.6 | Add composable static/dynamic strength and cost modifiers, with reasons for UI; link remains metadata-only. | Strength reasons cannot reveal hidden cards or hidden state to the wrong viewer. | Persist modifier sources/dependencies and canonical effective values only where needed. | `ModifierTests.EchelonCountsBreakers`; `ObservationTests.StrengthReasons` (planned) |
| `HOST-401` host/hosted relationships | 1.13.1–1.13.14, 8.5.1a | 4.7 | Add host IDs, installed-vs-hosted distinction, Trojan restrictions, host uninstall cleanup, and hosted counters. | Host identity and facedown status are filtered by normal visibility; hidden host choices remain owner-private. | Host graph is explicit, non-transitive, invariant-checked, deeply copied, and replayed. | `SCN-401.TrojanHostRemoval`; `HostTests.NoTransitiveHost` (planned) |
| `UNIQUE-401` uniqueness and consoles | 2.2, 3.8.5, 10.1.1, 10.3.5d | 4.2/4.7 | Enforce active newest-wins cleanup at checkpoint; never turn deck construction into a uniqueness/console limit. | Active/inactive state determines what is visible; no hidden deck inspection. | Persist activation sequence and cleanup set; deterministic tie handling. | `RuleTests.UniqueNewestWins`; `RuleTests.ConsoleNewestWins` (planned) |
| `MEMORY-401` memory legality | 3.9.3, 10.3.5e | 4.2/4.4 | Add MU base/modifiers, program install over-limit choices, and checkpoint cleanup. | Installed program identities are Runner-visible; no opponent hidden-zone leak. | Persist memory modifiers and installed program costs; derive legal trash sets deterministically. | `MemoryTests.OverLimitRequiresTrash`; `ObservationTests.MemoryChoices` (planned) |
| `HOSTED-CREDITS-401` load/empty/turn transfers | 10.9, 10.1.3 | 4.3 | Add ordinary hosted credit counters for assets/resources/hardware/events; no recurring-credit subsystem. | Hosted pools are public on visible installed cards; hidden card identities remain filtered. | Counters and empty triggers are typed, ordered, and serialized; hosted credits never enter player pools implicitly. | `CounterTests.LoadAndEmpty`; `CounterTests.TurnBeginTransfer` (planned) |
| `DAMAGE-401` damage/checkpoint/flatline | 1.5, 9.2.8, 10.3 | 4.6 | Route net/meat/brain damage from subroutines, access, operations, and turn triggers through interrupt/checkpoint/flatline; Karunā jack-out follows first damage only. | Damage can discard private grip cards; only source/amount/result are exposed. | Damage instruction, prevention/choice frame, discard order, and flatline result are replayable. | `SCN-402.KarunaDamageThenJackOut`; `DamageTests.Flatline` (planned) |
| `TAG-401` tag state and unless payment | 10.5, 9.10 | 4.6 | Add persistent tags, tag-or-pay, tag-triggered actions, rez condition, and tag removal. | Runner tag count is public; payment choice may involve private credits but not hidden cards. | Persist tag state, condition snapshots, unless branch, and turn-first-tag facts. | `SCN-402.TagOrPay`; `TagTests.RemoveTagCosts` (planned) |
| `IDENTITY-401` identity contracts | 1.8, 9.1.8, 10.3 | 4.7 | Add maximum hand, first access-trash, discard-end, first-tag, advance-from-zero, score/steal, and run-end identity triggers; starter identities stay ability-free. | Identity is faceup; private choices from HQ/grip remain filtered. | Persist identity definition, turn/run facts, and triggered decisions, not executable delegates. | `IdentityTests.AllGatewayIdentities`; `ObservationTests.IdentityChoices` (planned) |
| `AGENDA-401` agenda trigger contracts | 1.17, 9.1, 9.10 | 4.7 | Add score/steal triggers, clicks/draw, targeted trash, shuffle/search, conditional tag/damage, ignore-cost rez, max hand, and cannot-score remainder. | Score areas are public; HQ/R&D search/draw choices remain Corp-private. | Persist trigger context, branch decisions, and duration facts. | `AgendaTests.ScoreAndStealTriggers`; `AgendaTests.LuminalRestriction` (planned) |
| `ICE-TOPOLOGY-401` inward install and swap | 6.2, 8.5.10, 8.5.14, 8.8 | 4.5/4.7 | Add normal/ignore-cost ICE install from hidden zones, inward placement, uninstall while approached/encountered, and stable-ID swaps. | Ordinary type verification does not reveal Brân/Ansel installs; explicit ability requirements may. | Persist server/position by stable card ID and update active run, host, and subroutine context atomically. | `SCN-401.InwardInstall`; `SCN-401.SwapDuringRun` (planned) |
| `OBS-401` observation boundary | 1.21, 4.3–4.4, 8.5.13, D-407 | 4.1 onward | New choices and metadata must not leak definition IDs, hidden card order, hidden prompt ownership, or private search contents. | This is the primary hidden-information gate; owner-only data is explicitly marked in every observation. | Observation is derived from canonical state; serialized observations omit hidden IDs and resolver internals. | `ObservationTests.NoOpponentDefinitionIds`; golden/negative leak suite (planned) |
| `REPLAY-401` deterministic frame/replay | 9.2.8, D-411/D-416 | 4.1 onward | Existing replay must cover setup, new phases, choices, card movement, and every yielded frame. | Replay verifier consumes events, never hidden opponent observations. | Canonical frame/state hashes include schema and catalog fingerprints; migrations have documented semantic diffs. | `ReplayTests.RoundTripEveryYield`; `ReplayTests.HiddenObservationStable` (planned) |

## Gateway-reachable base rules checklist

These are required even when no single card in the current batch appears to
need them.  They are the engine substrate on which all 77 designs depend.

| Area | Required behavior and correction | Authority / evidence |
|---|---|---|
| Turn structure | Corp: formal turn begin, mandatory draw (not a click), action phase, discard phase. Runner: formal turn begin, action phase, discard phase, no mandatory draw. Corp receives 3 clicks; Runner 4. Action phases continue until zero clicks or a direct effect ends them; there is no voluntary “end turn” action and unused clicks are lost. | `CR-5.1-turns`, `CR-5.3-draw`, `CR-5.4-action-phase`, `CR-1.11-clicks`; `SCN-400` |
| Basic actions | Corp: gain credit, draw, install from HQ, play operation, advance (click+credit), tagged Runner resource trash (click+2 credits), purge (3 clicks). Runner: gain credit, draw, install from grip, play event, run (1 click), remove tag (click+2 credits). | `CR-5.2-basic-actions`; `CR-10.5-tags`; `SCN-400` |
| Run cost/link | A basic run costs exactly 1 click. Gateway identities have base link metadata but no card changes the basic run cost; do not add dead link mechanics. | `CR-10.7-link`; D-410; `ENG-403` |
| Install/play/rez | Events/operations are played and then trashed; identities are never installed. Runner cards install faceup/active; Corp cards install facedown/unrezzed/inactive. Corp roots and ICE positions follow server rules. ICE install is one credit per existing ICE; Corp agenda/asset/upgrade install cost is zero; Runner uses printed install cost. Rez is a paid-window transition to active. | `CR-8.5-install`, `CR-8.5.11-costs`, `CR-8.6-play`, `CR-4.6.6-servers` |
| Hidden install/reveal | Do not reveal a hidden card merely to verify ordinary type/server legality. Reveal only when an ability imposes an additional requirement or install-and-rez requires it. Archives remains faceup knowledge; HQ/R&D remain hidden. | `CR-8.5.13-reveal`; Ansel/Brân audit rows; `OBS-401` |
| Advance/score/steal | Advance is a basic Corp action and only legal for advanceable installed cards. Scoring happens in `(S)` windows, not as a click action. Agenda access forces steal after the one mid-access opportunity. Checkpoint victory threshold is 7 agenda points. | `CR-1.18-advance`, `CR-1.17-victory`, `CR-7.1-access` |
| Hosting | Hosting is distinct from installed; only eligible play-area/score-area cards host. Hosted counters do not count as player-owned pools. Host movement/uninstall trashes hosted objects at checkpoint unless an explicit rule says otherwise. | `CR-1.13-hosting`; `HOST-401` |
| Uniqueness/console | Unique and console restrictions are active-play/checkpoint rules, not deck-construction limits. Newest active copy survives; all other illegal copies are trashed at checkpoint. | `CR-2.2-unique`, `CR-3.8-console`, `CR-10.3-illegal-state` |
| Setup/presets | Faceup identities, 5 credits, shuffle, draw five, Corp mulligan then Runner mulligan at most once, second hand mandatory, Corp first turn. Official teaching presets are code/quantity-only Runner 30/40 and Corp 34/44; incomplete behavior cannot be user-playable. | `CR-1.6-setup`; D-405; `MATCH-401`; `DATA-401A` |
| Choices/search | Exactly/up-to/any-number cardinality, acting-player/other-player chooser, hidden search, reveal and shuffle are explicit typed decisions and frames. | `CR-8.7-search`, `CHOICE-401`, `OBS-401` |
| Damage/tags | Existing interrupt/checkpoint/flatline machinery is reused. No Gateway card invents damage prevention. Tag state is persistent and tag-or-pay branches are legal choices. | `CR-9.2.8-checkpoint`, `CR-10.5-tags`; `SCN-402` |

## Card-text audit — 70 unregistered designs

The following rows are the complete set of designs not registered at the
4.1 baseline.  “Gap” is the smallest missing behavior contract, not a
promise that the printed text can be parsed.  Hidden-info and serialization
notes are acceptance criteria for the owning slice.  The rows marked
`planned` intentionally do not claim a test has already landed.

| Code | Design | Slice | CR/ruling citation | Required primitive / current gap | Hidden-information and serialization impact | Representative test (planned) |
|---|---|---:|---|---|---|---|
| 30001 | René “Loup” Arcemont: Party Animal | 4.7 | CR-9.1-triggered | Identity first access-trash trigger; no identity contract yet. | Access is visible; persist first-trash-per-turn fact and pending gain/draw. | `IdentityTests.ReneFirstAccessTrash` |
| 30002 | Wildcat Strike | 4.2 | CR-8.6-play | Corp chooses gain 6 or draw 4; needs chooser/branch. | Runner grip remains private; persist Corp-owned branch. | `CardTests.WildcatStrikeCorpChoice` |
| 30003 | Carnivore | 4.3 | CR-3.8-console, CR-7.1-access | Access once/turn trash-two-grip ability and console/MU metadata. | Grip-trash target is Runner-private; persist access-use fact and MU. | `CardTests.CarnivoreMidAccess` |
| 30004 | Botulus | 4.7 | CR-1.13-hosting, CR-8.5-install | Trojan install-only-on-ICE, hosted virus counter, host-removal cleanup. | Host and facedown state are observation-filtered; persist host graph/counters. | `SCN-401.BotulusHostLifecycle` |
| 30007 | Fermenter | 4.3 | CR-10.1-counters, CR-5.1-turns | Install/turn-begin virus load and click-trash gain. | Hosted counters public; persist turn-begin trigger and load/empty. | `CardTests.FermenterLoadAndCashOut` |
| 30008 | Leech | 4.4 | CR-6.7-success, CR-10.1-counters | Successful-central-run counter and encounter strength reduction. | Encounter ICE is public; persist run/encounter modifier duration. | `CardTests.LeechSuccessfulCentralRun` |
| 30009 | Cookbook | 4.3 | CR-9.1-triggered, CR-10.1-counters | Optional virus placement after virus-program install. | Installed program is public; persist optional trigger choice. | `CardTests.CookbookOptionalCounter` |
| 30010 | Zahya Sadeghi: Versatile Smuggler | 4.7 | CR-6.7-success, CR-7.1-access | Exact per-run HQ/R&D access count and run-end credit trigger. | Access count public, card identities filtered; persist count/run-end choice. | `SCN-402.ZahyaAccessCount` |
| 30011 | Mutual Favor | 4.2 | CR-8.7-search | Hidden icebreaker search, reveal, optional install. | Search private until reveal; persist selected ID, reveal, shuffle, branch. | `SCN-400.MutualFavorSearch` |
| 30012 | Tread Lightly | 4.4 | CR-6.7-success, CR-9.10-lingering | Run-scoped +3 rez-cost modifier. | Unrezzed ICE stays hidden; persist modifier through run. | `CardTests.TreadLightlyRezModifier` |
| 30013 | Docklands Pass | 4.4 | CR-7.3-breach | First HQ breach +1 access. | Additional HQ accesses retain hidden random order; persist once-per-turn fact. | `SCN-402.DocklandsFirstBreach` |
| 30014 | Pennyshaver | 4.3 | CR-3.8-console, CR-10.1-counters | Successful-run hosted-credit trigger and click cash-out. | Hosted credits public; persist pool and trigger. | `CardTests.PennyshaverHostedCredits` |
| 30015 | Carmen | 4.3 | CR-8.5-install, CR-6.7-success | Successful-run install discount plus sentry breaker. | Grip choice private until install; persist effective cost/turn fact. | `CardTests.CarmenInstallDiscount` |
| 30016 | Marjanah | 4.3 | CR-6.2-run-position, CR-6.7-success | Successful-run-this-turn conditional barrier break cost. | ICE is public at interface; persist turn fact/cost. | `CardTests.MarjanahConditionalBreak` |
| 30017 | Tranquilizer | 4.7 | CR-1.13-hosting, CR-10.1-counters | Trojan counter threshold derez host. | Derez can hide ICE again; persist host state and counters. | `SCN-401.TranquilizerDerezThreshold` |
| 30018 | Red Team | 4.3 | CR-10.9-load-empty, CR-6.7-success | Once-per-turn central run and loaded-credit payout. | Hosted pool public; persist central-run set/load. | `CardTests.RedTeamCentralRun` |
| 30019 | Tāo Salonga: Telepresence Magician | 4.7 | CR-8.8-swap, CR-9.1-triggered | Score/steal optional stable-ID ICE swap. | Opponent unrezzed ICE identities remain hidden; persist simultaneous swap/hosts. | `SCN-401.TaoSwapDuringRun` |
| 30020 | Creative Commission | 4.2 | CR-8.6-play, CR-5.4-action-phase | Gain 5 then conditional click loss. | No hidden choice; persist conditional click instruction. | `CardTests.CreativeCommissionClickLoss` |
| 30021 | VRcation | 4.2 | CR-8.4-draw, CR-5.4-action-phase | Draw 4 then conditional click loss. | Four cards remain Runner-private; persist ordered draw/deck-out. | `CardTests.VRcationDrawAndLoss` |
| 30022 | DZMZ Optimizer | 4.4 | CR-3.8-console, CR-8.5-install | +1 MU and first-program install discount. | Grip choice private; persist first-program fact/modifier. | `CardTests.DzmzFirstProgramDiscount` |
| 30023 | Pantograph | 4.3 | CR-3.8-console, CR-9.1-triggered | +1 MU and score/steal credit plus optional grip install. | Grip selection private; persist trigger and install choice. | `CardTests.PantographScoreSteal` |
| 30024 | Conduit | 4.3 | CR-6.7-success, CR-10.1-counters | Successful R&D counter and X additional accesses. | Extra R&D access filtered; persist counter/X. | `SCN-402.ConduitAdditionalAccess` |
| 30025 | Echelon | 4.4 | CR-6.2-run-position | Strength +1 per installed icebreaker, including itself. | Only visible installed breakers count; persist modifier dependencies. | `CardTests.EchelonStrengthBreakdown` |
| 30026 | Unity | 4.4 | CR-6.2-run-position | Dynamic X strength by breaker count and code-gate interface. | Installed breaker count public to Runner; persist dependency. | `CardTests.UnityDynamicStrength` |
| 30027 | Telework Contract | 4.3 | CR-10.9-load-empty, CR-5.1-turns | Loaded pool and once-per-turn click transfer. | Pool public; persist load, transfer, used fact. | `CardTests.TeleworkOncePerTurn` |
| 30028 | Jailbreak | 4.5 | CR-6.7-success, CR-7.3-breach | HQ/R&D run, success draw, +1 breach access. | Server choice public; hidden cards remain filtered; persist run effects. | `SCN-401.JailbreakSuccessEffects` |
| 30030 | Sure Gamble | 4.2 | CR-8.6-play | Ordinary event play/gain 9. | Grip choice private until play; persist cost/gain. | `CardTests.SureGamble` |
| 30031 | T400 Memory Diamond | 4.2 | CR-3.8-console, CR-8.5-install | +1 MU and maximum hand. | No new leak; persist static modifiers. | `CardTests.T400Modifiers` |
| 30032 | Mayfly | 4.5 | CR-9.10-lingering, CR-6.2-run-position | Break ability arms run-end trash only when used. | No hidden search; persist run-scoped armed flag. | `SCN-401.MayflyUsedVersusUnused` |
| 30033 | Smartware Distributor | 4.3 | CR-10.9-load-empty, CR-5.1-turns | Click load and turn-begin transfer. | Pool public; persist load/transfer. | `CardTests.SmartwareTurnBegin` |
| 30034 | Verbal Plasticity | 4.4 | CR-5.2-basic-actions, CR-8.4-draw | First basic draw replacement with draw two. | Drawn cards private; persist replacement fact/order. | `CardTests.VerbalPlasticityBasicDraw` |
| 30035 | Haas-Bioroid: Precision Design | 4.7 | CR-9.1-triggered, CR-8.7-search | Max hand and optional Archives-to-HQ score trigger. | Corp Archives choice private as appropriate; persist search/add. | `IdentityTests.HaasArchivesChoice` |
| 30036 | Luminal Transubstantiation | 4.7 | CR-1.17-victory, CR-5.4-action-phase | Score gain 3 clicks and remainder-turn cannot-score. | Score public; persist gained clicks/duration. | `AgendaTests.LuminalRestriction` |
| 30038 | Ansel 1.0 | 4.5 | CR-8.5.13-reveal, CR-6.2-run-position | Bioroid repeated break, Runner-card trash, normal install, run restriction. | No ordinary hidden install reveal; persist target, trigger, restriction/topology. | `SCN-401.AnselNormalInstall` |
| 30039 | Brân 1.0 | 4.5 | CR-8.5.11-costs, CR-8.5.13-reveal | Bioroid break and inward ignore-cost ICE install. | No ordinary type reveal; persist inward stable position and run context. | `SCN-401.BranInwardInstall` |
| 30040 | Seamless Launch | 4.2 | CR-1.18-advance, CR-8.5-install | Place two counters on card not installed this turn. | Installed state public; persist install-turn marker/choice. | `CardTests.SeamlessLaunchEligibility` |
| 30041 | Sprint | 4.2 | CR-8.4-draw, CR-8.3-arrange | Draw 3 then choose 2 HQ to shuffle into R&D. | HQ choice private; persist choice and deterministic shuffle. | `SCN-400.SprintHiddenChoice` |
| 30042 | Manegarm Skunkworks | 4.5 | CR-6.2-run-position, CR-9.2.8-checkpoint | Approach end-run unless spend 2 clicks or pay 5. | No hidden reveal; persist approach alternatives. | `SCN-401.ManegarmUnlessChoice` |
| 30043 | Jinteki: Restoring Humanity | 4.7 | CR-5.1-turns, CR-4.3-hidden | Discard-phase-end facedown Archives check. | Do not reveal satisfying card; persist predicate/phase occurrence. | `IdentityTests.JintekiDiscardEnd` |
| 30044 | Longevity Serum | 4.7 | CR-8.3-arrange, CR-8.7-search | Score HQ any-number trash and up-to-three Archives shuffle. | HQ/R&D order hidden; persist cardinality, IDs, shuffle. | `AgendaTests.LongevityChoices` |
| 30045 | Urtica Cipher | 4.6 | CR-7.1-access, CR-1.18-advance | Access damage 2 plus one per advancement counter. | Access reveals asset normally; persist counter snapshot/damage. | `SCN-402.UrticaAccessDamage` |
| 30046 | Diviner | 4.6 | CR-7.1-access, CR-9.2.8-checkpoint | Net damage then odd printed-cost end-run condition. | Trash/access card cost revealed only at required timing; persist cost snapshot. | `SCN-402.DivinerOddCost` |
| 30047 | Karunā | 4.6 | CR-9.2.8-checkpoint, CR-6.2-run-position | Two net damage then jack-out choice, then two net damage. | Grip remains private except damage discards; persist instruction order. | `SCN-402.KarunaDamageThenJackOut` |
| 30048 | Hansei Review | 4.2 | CR-8.6-play, CR-4.3-hidden | Gain 10, then mandatorily trash one HQ card when HQ is non-empty. | Corp HQ target is private; persist the conditional target-selection instruction. | `CardTests.HanseiReview` |
| 30049 | Neurospike | 4.6 | CR-1.17-victory, CR-9.1-triggered | Net damage X from printed points scored this turn. | Scored agendas public; persist turn-scoped printed-point sum. | `SCN-402.NeurospikeX` |
| 30050 | Anoetic Void | 4.5 | CR-6.2-run-position, CR-4.3-hidden | Approach pay 2 and trash two HQ to end run. | HQ choices private; persist exact two-card choice before end run. | `SCN-401.AnoeticApproach` |
| 30051 | NBN: Reality Plus | 4.7 | CR-10.5-tags, CR-9.1-triggered | First tag each turn gain 2 or draw 2. | Drawn HQ private; persist first-tag fact/choice. | `IdentityTests.NbnFirstTag` |
| 30052 | Tomorrowʼs Headline | 4.7 | CR-1.17-victory, CR-10.5-tags | Score/steal gives one tag. | Public tag result; persist trigger context. | `AgendaTests.TomorrowsHeadline` |
| 30053 | Spin Doctor | 4.3 | CR-8.7-search, CR-8.8-swap | Rez draw 2; remove-self and Archives-to-R&D shuffle. | Archives visible, resulting R&D order hidden; persist movement/shuffle. | `CardTests.SpinDoctorRemoveAndShuffle` |
| 30054 | Funhouse | 4.5 | CR-6.2-run-position, CR-10.5-tags | Encounter end-run unless tag; subroutine tag unless pay 4. | ICE visible at encounter; persist two distinct choice windows. | `SCN-401.FunhouseUnlessWindows` |
| 30055 | Ping | 4.6 | CR-6.2-run-position, CR-10.5-tags | Rez during run against server gives tag. | Rez reveals only target ICE; persist run-server condition/first-tag. | `SCN-402.PingRezTag` |
| 30056 | Predictive Planogram | 4.2 | CR-8.6-play, CR-8.4-draw | One branch, or both if tagged, gain 3/draw 3. | Drawn HQ private; persist branch and ordered draw. | `CardTests.PredictivePlanogram` |
| 30057 | Public Trail | 4.6 | CR-5.1-turns, CR-10.5-tags | Last-turn successful-run legality and tag unless pay 8. | Last-turn fact public; persist across boundary and branch. | `SCN-402.PublicTrailTagOrPay` |
| 30058 | AMAZE Amusements | 4.6 | CR-6.7-success, CR-9.10-lingering | Run-end two tags for stolen agenda, even after access trash. | Accessed upgrade visible; persist source-independent run linger. | `SCN-402.AmazePersistsAfterTrash` |
| 30059 | Weyland Consortium: Built to Last | 4.7 | CR-1.18-advance, CR-9.1-triggered | Advance-from-zero trigger gains 2. | Installed target public; persist pre-advance snapshot. | `IdentityTests.WeylandAdvanceFromZero` |
| 30060 | Above the Law | 4.7 | CR-9.1-triggered, CR-7.1-access | Optional installed-resource trash on score. | Target installed resource is public; persist optional score choice. | `AgendaTests.AboveTheLawChoice` |
| 30061 | Clearinghouse | 4.6 | CR-1.18-advance, CR-5.1-turns | Advanceable asset, turn-begin self-trash, counter-based meat damage. | Installed counters public; persist turn-begin choice and damage. | `SCN-402.ClearinghouseTurnBegin` |
| 30062 | Ballista | 4.6 | CR-6.2-run-position, CR-7.1-access | Corp chooses installed program trash or end-run alternative. | Installed targets public; persist Corp branch/target. | `SCN-402.BallistaCorpChoice` |
| 30063 | Pharos | 4.6 | CR-1.18-advance, CR-6.2-run-position | Advanceable ICE, +5 strength at three counters. | ICE/counters public; persist counters/derived modifier. | `CardTests.PharosThreshold` |
| 30064 | Government Subsidy | 4.2 | CR-8.6-play | Ordinary operation play/gain 15. | HQ choice private until play; persist cost/gain. | `CardTests.GovernmentSubsidy` |
| 30065 | Retribution | 4.6 | CR-8.6-play, CR-10.5-tags | Tagged-only operation chooses installed program/hardware. | Installed target public; persist tag legality/target. | `CardTests.RetributionTargetFilter` |
| 30066 | Malapert Data Vault | 4.7 | CR-8.7-search, CR-1.17-victory | Score-root snapshot, non-agenda R&D search/reveal/add HQ. | Search private until explicit reveal; persist snapshot/search/reveal/shuffle. | `SCN-402.MalapertReveal` |
| 30068 | Orbital Superiority | 4.7 | CR-10.5-tags, CR-9.1-triggered | Score branch: tagged → 4 meat, otherwise one tag. | Public tag state/result; persist condition snapshot/branch. | `AgendaTests.OrbitalBranch` |
| 30069 | Send a Message | 4.7 | CR-9.1-triggered, CR-8.5.11-costs | Score/steal optional ignore-cost rez installed ICE. | Target rez reveals target ICE only; persist trigger/target. | `AgendaTests.SendAMessageRez` |
| 30070 | Superconducting Hub | 4.7 | CR-1.17-victory, CR-8.4-draw | +2 max hand and optional score draw 2. | Drawn HQ private; persist static modifier/choice. | `AgendaTests.SuperconductingHub` |
| 30071 | Regolith Mining License | 4.3 | CR-10.9-load-empty | Rez load 15 and click take 3/empty trash. | Pool public; persist load/empty/transfer. | `CardTests.RegolithLoadAndTake` |
| 30073 | Tithe | 4.6 | CR-9.2.8-checkpoint, CR-6.2-run-position | Ordered net damage then credit subroutines. | Damage may discard private grip; persist order/checkpoint. | `SCN-402.TitheOrder` |
| 30075 | Hedge Fund | 4.2 | CR-8.6-play | Ordinary operation play/gain 9. | HQ choice private until play; persist cost/gain. | `CardTests.HedgeFund` |
| 30076 | The Catalyst: Convention Breaker | 4.7 | CR-1.6-setup, CR-4.3-hidden | Starter-only identity/preset legality; no gameplay ability. | Identity public; deck validation must not reveal contents. | `PresetTests.CatalystStarterOnly` |
| 30077 | The Syndicate: Profit over Principle | 4.7 | CR-1.6-setup, CR-4.3-hidden | Starter-only identity/preset legality; no gameplay ability. | Identity public; deck validation must not reveal contents. | `PresetTests.SyndicateStarterOnly` |

## M2 partials to close in later owning batches

The 70-row table intentionally excludes the two cards that already have a
registered but partial M2 adapter.  They are not allowed to satisfy the final
coverage gate until their printed text is complete:

* **30037 Nico Campaign (4.3):** add load 9, empty-trash/draw, and turn-begin
  take-three behavior to the existing asset trash-cost adapter.  Keep hosted
  credits distinct from a player pool (`CR-10.9-load-empty`).
* **30067 Offworld Office (4.7):** add the seven-credit on-score trigger to
  the existing agenda-point adapter and preserve the M2 capability fingerprint
  (`CR-1.17-victory`).

The five already-complete M2 designs are Palisade (30072), Whitespace
(30074), Cleaver (30006), Buzzsaw (30005), and Overclock (30029).  Their
metadata/behavior/test/review rows remain pinned in the generated coverage
report; additive registrations must not churn their M2 semantic trace.
