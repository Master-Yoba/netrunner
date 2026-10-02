# Elevation progress — the second card set

Plan: [Elevation implementation plan](ELEVATION_PLAN.md). Audit: [ELEVATION_PRIMITIVES.md](ELEVATION_PRIMITIVES.md).
Registry: `data/elevation/batch-registry.json`. Evidence is from the Linux x86_64 development box.

## Branches

| Slice | Branch | Status |
|---|---|---|
| EL.1 preparation: card data, coverage, formats, deck rules | `feat/el1-prep` | complete |
| EL.2 primitives A: values, conditions, counters, timing, identity state | `feat/el2-primitives-a` | complete |
| EL.3 Batch 1: 43 cards | `feat/el3-batch1` | complete |
| EL.4 primitives B: card sources, zones and information | `feat/el4-primitives-b` | complete |
| EL.5 Batch 2: 16 cards | `feat/el5-batch2` | complete |
| EL.6 primitives C: runs and ICE | `feat/el6-primitives-c` | complete |
| EL.7 Batch 3: 23 cards | `feat/el7-batch3` | complete |

## E6 baseline

`scripts/core-growth.py` over Gateway's slices, from git history (reproduced on 2 October 2026 with both registries
loaded, `make core-growth CORE_GROWTH_ARGS='--registry data/m4/batch-registry.json --registry
data/elevation/batch-registry.json --range …'`):

| Gateway slice | Core lines added | New vocabulary | Cards needing no Core change |
|---|---|---|---|
| 4.2 | +19,932 | 208 | 0 of 12 |
| 4.3 | +12,514 | 91 | 0 of 14 |
| 4.4 | +5,524 | 24 | 0 of 7 |
| 4.5 | +5,814 | 46 | 0 of 7 |
| 4.6 | +1,077 | 21 | 3 of 12 |
| 4.7 | +934 | 16 | 9 of 20 |
| **All** | | | **12 of 72 (17%)** |

## EL.1 — preparation

### Card data (D-EL-2)

- `scripts/update-card-data.sh` extracts `sg` and `elev` from the pinned commit
  `236140cf84491c23995b028bc791281837520b97` into two local-only snapshots, `data/vendor/system-gateway` and
  `data/vendor/elevation`, each with its own manifest, count (77, 82) and checksum. Both stay gitignored.
- `card-data normalize|validate --pack sg|elev`; the official checks (manifest identity, count, sentinels) run per pack
  from one table, `OfficialCardPacks`.
- `scripts/build-card-data.py` authenticates and embeds both packs in an official build. A missing Elevation pack fails
  with a DATA-400 diagnostic, checked in `verify.sh`. A synthetic build embeds the fixture only.
- `CardDataResource` exposes the packs (`EmbeddedPackCodes`, `LoadEmbeddedPack`) and the pool (`LoadEmbeddedPool`);
  `CardDataSet.Packs` keeps each pack's manifest, and `PackCodeOf` gives a card's pack.
- **The Gateway payload is byte-identical** (cards `a190e6e6…`, manifest `e7642ae4…`), so the frozen M5 pins, the
  saved-game fixtures and the Gateway behavior and runtime fingerprints are unchanged. The Elevation pack's cards hash is
  `217e03f5…`.
- Two upstream facts the old single-pack checks rejected:
  - Elevation's gray and black ops have trash costs (CR 3.5.3). Normalized data and runtime specs now allow trash costs
    on ice and operations.
  - IP Enforcement's play cost is X (null upstream). Its runtime play cost is 0 and its behavior charges X.

### Card pools and formats (C2, D-EL-3)

- **Card pools** (`CardPools`): `gateway` (System Gateway, `GatewayBehaviorCatalog`, unchanged) and
  `gateway-elevation` (`ElevationBehaviorCatalog`, which composes the Gateway catalog with the Elevation batches under its
  own version, `gateway-elevation-el.1`). A saved game's behavior version names its pool.
- **Format snapshots** in `data/formats/` (schema `format.schema.json`), embedded and parsed strictly by
  `CardFormatCatalog` (FORMAT-401):

  | Format | Pool | Ban list | High-value agendas | Playable |
  |---|---|---|---|---|
  | System Gateway | `gateway` | none | no limit | yes |
  | System Gateway + Elevation | `gateway-elevation` | Startup Balance Update 26.05, on the cards we have | at most 3 worth 3+ | yes |
  | Startup 2025 (`startup_8`) | `gateway-elevation` | Balance Update 25.11 | no limit | no: Liberation missing |
  | Startup 2026 (`startup_10`) | `gateway-elevation` | Balance Update 26.05 | at most 3 worth 3+ | no: Vantage Point missing |

- `FormatDeckValidator.Validate(deck, format, pool)` runs the identity's rules, then the format's legal packs, ban list,
  copy limit and high-value-agenda limit. The Gateway sample decks validate under the Gateway format; an illegal deck
  fails each rule (`CardFormatTests`).

### Coverage (CARD-401)

- `tools/Netrunner.CardCoverage` reads both registries and both rulings files, the Gateway catalog for System Gateway and
  the Gateway + Elevation catalog (less the Gateway codes) for Elevation. Elevation tests are marked
  `[Trait("ElevationCard", "350xx")]`.
- One report, `docs/generated/card-coverage.{md,json}`, replaces `gateway-coverage.*`: **159 cards, Gateway's 77
  complete, Elevation's 82 planned.** It's the same with or without the local card data. `verify.sh` diffs it and checks
  both sets.

### The audit review

Every row of the registry now carries its CR citations (`data/elevation/rulings.json`, 47 rulings), an observation, a
hidden-information note and a serialization note. Corrections to the planning audit:

| Card | Correction |
|---|---|
| Rising Tide (35009) | Its strength counts fracters in the heap: `EL-AMOUNT`, not existing vocabulary. |
| Lie Low (35015) | Removing tags as an effect is new: `EL-REMOVE-TAGS`. |
| Synapse Global (35058), Bigger Picture (35065) | Also `EL-REMOVE-TAGS` (a cost and an effect). |
| IP Enforcement (35066) | X play cost with an X-tag additional cost (`EL-REMOVE-TAGS`) and a trash cost (`EL-OPERATION-TRASH-COST`). |
| Measured Response (35078) | Has a trash cost: `EL-OPERATION-TRASH-COST`. |

With them, 21 of 82 cards (26%) use only existing vocabulary. Both new primitives belong to EL.2's families.

### E6 tooling

`card-vocabulary` dumps the widest embedded pool (159 cards in an official build), and `core-growth.py` takes
`--registry` more than once.

### Gate

- Data loads and validates in both modes: synthetic (fixture only) and official (both packs); DATA-400 tests cover a
  corrupt, mismatched or missing pack (`ElevationCardDataTests`).
- The coverage report lists 159 cards with no diagnostics.
- The formats validate the Gateway decks and reject an illegal deck per rule (`CardFormatTests`).
- `verify.sh`: see the PR.

## EL.2 — primitives A

Core only, on `feat/el2-primitives-a` (`bdc3d6f..2ad657c`): **+3,329/−223 lines in 37 Core files.** Every addition is
gated so a Gateway game keeps its bytes (the frozen M5 pins, the saved-game fixtures and the Gateway catalog
fingerprint are unchanged): new contract fields serialize only when set, and turn-event tracking, deferred occurrences
and last-known facts are recorded only by a catalog that asks for them (`TrackTurnEvents`, the Elevation pool).

| Family | What landed |
|---|---|
| Values and conditions | Threat level; tag, zone, memory, turn-event ("the first time each turn") and played-this-turn conditions; typed amounts (source counters and advancement, excess advancement, clicks, a chosen card's install cost, recorded amounts) with offsets; trigger requirements; option conditions. |
| Counters, scoring and credits | Dividends (a scored agenda keeps its advancement counters), agenda and power counters added and removed, abilities on identities and in either score area, forfeiting, recurring credits and declared-use hosted credits (trash costs, job and connection installs, rezzing in a server), the persistent trash-cost increase, the region limit (already enforced), extra allotted clicks next turn, a per-card ban on scoring this turn. |
| Timing occurrences | Action phase ended, run began, agenda forfeited, and first-time occurrences (a tag removed, credits gained through an agenda or operation, an operation played) deferred to the end of the current action (CR 9.1). A card trashed while rezzed keeps its own "when trashed" ability (last-known information). |
| Identity state | Double-faced identities: a face on each trigger and a flip instruction. |
| Also | Multi-click actions, the generic ICE subroutine, behavior installs from a zone, Sabotage, deck-top placement, additional breach access, a deck shuffle, the Runner-cannot-trash flag. |

Contract, serialization and fingerprint tests: `ElevationPrimitiveContractTests`. The behaviors are exercised through
EL.3's cards and through probe definitions in the Elevation test catalog (`ElevationHarness.Probe`: "trash an installed
ice", "forfeit an agenda"), because no printed card in the pool exercises those yet. **Deviation:** EL.2's behavior
tests ship with EL.3 (a stacked PR) rather than as card-free fixtures.

## EL.3 — Batch 1

43 cards on `feat/el3-batch1`: Gourmand, Hantu, Rising Tide, Cacophony, Rent Rioters, Clean Getaway, Lie Low, Transfer
of Wealth, Sang Kancil, Open Market, Dewi Subrotoputri, Illumination, Ritual, Azimat, Devadatta Drone, Principia,
"Knickknack" O'Brian, Side Hustle, Aggressive Trendsetting, Otto Campaign, Nanomanagement, Top-Down Solutions, PT
Untaian, Sericulture Expansion, Phật Gioan Baotixita, Nebula Talent Management, Synapse Global, Embedded Reporting, Next
Big Thing, Idiosyncresis, Public Access Plaza, Doomscroll, Bigger Picture, The Zwicky Group, Greenmail, Off the Books,
Anthill Excavation Contract, Kessleroid, Syailendra, Key Performance Indicators, Measured Response, Lamplighter and
Mahkota Langit Grid.

- **Specs:** `src/Netrunner.Cards/Elevation/` (economy, Runner, Corp and ICE files), composed by
  `ElevationBehaviorCatalog` with the hand-copied breaker, connection and job subtypes.
- **Tests:** 82 Elevation tests (`tests/Netrunner.Cards.Tests/Elevation/`), at least one positive test per card plus
  negative and interaction cases (first-time-each-turn limits, threat thresholds, unrezzed Kessleroid, the Mahkota
  increase persisting after Mahkota is trashed, an empty Open Market trashing itself). They run in the synthetic lane on a
  text-free runtime catalog (`ElevationTestMetadata`).
- **Registry and coverage:** EL.3 complete; the report shows Elevation 43 of 82 registered, tested and complete.
- **Humanoid Resources moves to EL.5:** its "you may play 1 operation from HQ" is nested play, an EL.4 primitive
  ("card sources and zones").

### E6

`make core-growth CORE_GROWTH_ARGS='--registry data/m4/batch-registry.json --registry data/elevation/batch-registry.json
--range slice-EL.3=2ad657c..fa8ee4b'`:

| Batch | Core lines | New vocabulary | Cards needing no Core change |
|---|---|---|---|
| slice-EL.3 | +0/−0 | 0 | **43 of 43 (100%)** |

### Simulation decks and the batch proof

- `data/decks/elevation-simulation-decks.json`: three Corp decks (Nebula, The Zwicky Group, PT Untaian) and two Runner
  decks (Dewi, Zahya), ours, legal under System Gateway + Elevation (checked by `CardFormatTests`). They load through
  `OfficialPresetCatalog.ElevationSimulation()`, apart from the embedded catalog, so the Gateway preset pins and the menu
  are unchanged.
- The fuzzer and the ladder take `--content elevation` (`scripts/fuzz.sh elevation`).
- The first fuzz runs found five defects, fixed before the batch closed: an empty optional advance, two choices sharing
  the `chosen` binding (now named), an empty shuffle missing its source zone, the bots rejecting card-effect install
  choices, and the contract agent rejecting multi-click abilities.
- **Evidence:** `scripts/fuzz.sh elevation --games 60 --workers 12`: 60 of 60 terminal. `ai-ladder --content elevation
  --entrants random,contract,easy,standard --seeds-per-pairing 3`: 288 games, 0 errors, 0 capped; both sides order
  random < contract < easy < standard.

### Bots

The heuristic policies value every new instruction (counters, flips, tag removal, forfeits, extra clicks, Sabotage,
additional access, recurring credits, deck-top placement, card-effect installs) and the typed amounts. These paths are
reached only by Elevation cards, so Gateway decisions are unchanged and the policy versions stay as they are. The
contract agent answers card-effect install choices and multi-click abilities.

### Rulings and simplifications

| Card | Note |
|---|---|
| Azimat, Open Market, Mahkota Langit Grid | Declared-use hosted credits are spent automatically, before the wallet: they pay for nothing else, so the order never costs a choice that matters. |
| Cacophony (Sabotage) | The Corp chooses up to 3 HQ cards and the rest come from the top of R&D. If R&D is short, fewer cards are trashed instead of the Corp having to take more from HQ (rare: R&D nearly empty). |
| Off the Books | The Corp chooses "install" (an R&D card, ignoring all costs) or "HQ" (searched and added) before looking; the card installed this way isn't revealed. |
| Synapse Global | The installed card isn't revealed (EL.4 brings reveals). |
| Devadatta Drone | "Whenever you breach R&D" triggers on a successful run on R&D, which is the only way the pool breaches R&D. |

### Gate

- Synthetic suite green: Core 829, Cards 434, AI 130, CLI 171, Match 582 (+23 skipped).
- `make gates`: see the PR.

## EL.4 — Primitives B: card sources, zones and information

On `feat/el4-primitives-b`, all in Core, before any Batch 2 card:

| Family | Primitives |
|---|---|
| Card sources | Nested play from an ability, Archives or a console (`PlayCard`, a nested `CardPlayFrame`); paid abilities usable from Archives; installs from Archives, the heap, the score area (the agenda's points leave with it) and the top N cards of a deck (`top:N` targets); a keyed install binds the card it installed. |
| Zones | Cards hosted faceup on a console: two new zones (`RunnerHostedFaceUp`, `CorpHostedFaceUp`), the `HostFaceUp` instruction, `hosted-on-source` targets; a host whose cards play and install as if in the grip (`HostedCardsPlayAsGrip`); installs that spent no credits (`FreeInstallIds`, `SubjectInstalledWithoutCredits`). Faceup installed agendas (`TurnFaceUp`), still advanceable and scorable. |
| Information | Hand-size discards (`HandSizeDiscardIds`, `hand-size-discards` targets); played-from, source-zone, zone-count and chose-cards conditions; damage-done, HQ-trash and hand-size occurrences; access-time abilities on the card being accessed; rez requirements; an operation's trash cost when accessed (Measured Response's, an EL.2 gap). |

The validator knows every new frame, zone and binding; the observation shows hosted faceup cards to both sides.
**Deviation:** four EL.4-family primitives move to EL.6, because only EL.7 cards use them: moving an upgrade (Mercia
B4LL4RD), random HQ trash (Shred), "a copy of a card accessed" (Charm Offensive), and Detente's any-player ability with
its access outside a run.
Contract, serialization and fingerprint tests: `ElevationPrimitiveContractTests`. **Deviation:** as in EL.2, the
behavior tests ship with the cards (EL.5).

The EL.5 batch fuzz found four Core defects; their fixes are on this branch (`fix: EL.4 …`), so the batch itself needs
no Core change:
- a zone install's discount ("paying 1 less") must come before the quoted first-program-install discount, or the
  frozen quote and the install cost disagree (Illumination with DZMZ Optimizer, an EL.3 card pair);
- a paid-ability zone install inside a live run (Madani mid-run) was outside the run-stack topology;
- a keyed install's binding (IP Enforcement's agenda) failed the binding-reachability check once suspended;
- "trash all hosted cards" with none hosted.

## EL.5 — Batch 2

16 cards on `feat/el5-batch2`: Petty Cash, Humanoid Resources, Project Ingatan, Scrounge, Poétrï Luxury Brands,
MuslihaT, Byte!, Magdalene Keino-Chemutai, BANGUN, AU Co., Peer Review, Touch-ups, IP Enforcement, Plutus, Bling and
Madani.

- **Specs:** `src/Netrunner.Cards/Elevation/ElevationBatch2Specs.cs`.
- **Tests:** 29 Batch 2 tests (`ElevationBatch2Tests`), 123 Elevation tests in all, at least one per card, with
  observation checks for the faceup host (the Corp sees Bling's card) and fuzz regressions (Bling with nothing hosted,
  Madani installing mid-run, IP Enforcement over an occupied root, Illumination with DZMZ).
- **Registry and coverage:** EL.5 complete; Elevation 59 of 82 registered, tested and complete.
- **Detente moves to EL.7:** its "access 1 card in HQ at random" happens outside a run, which needs EL.6's run and
  access machinery (EL-ACCESS-OUTSIDE-RUN), and its ability is usable by either player (EL-ANY-PLAYER-ABILITY).

### E6

`make core-growth CORE_GROWTH_ARGS='--registry data/m4/batch-registry.json --registry data/elevation/batch-registry.json
--range slice-EL.5=4862fc8..140fcc7'`:

| Batch | Core lines | New vocabulary | Cards needing no Core change |
|---|---|---|---|
| slice-EL.5 | +0/−0 | 0 | **16 of 16 (100%)** |

### Simulation decks and the batch proof

- Five more simulation decks, for the Batch 2 identities and consoles: AU Co., BANGUN and Poétrï (Corp), Magdalene and
  MuslihaT (Runner); 6 Corp and 4 Runner decks in all, legal under System Gateway + Elevation. The fuzz content is
  `elevation-simulation-v2`.
- **Evidence:** `scripts/fuzz.sh elevation --games 120 --seed 5 --workers 8`: first 109 of 120 terminal (the four
  defects above); after the fixes, 120 of 120.
- `ai-ladder --content elevation --entrants random,contract,easy,standard --seeds-per-pairing 2`: the first run, 768
  games, found 18 errors from four defects the random fuzz had missed (Aggressive Trendsetting's extra click failing the
  turn-facts validator, a nested operation's deck-out, an exhausted R&D top for AU Co., IP Enforcement with no agenda of
  value X); all fixed with regression tests (`fix: EL.4 …`, `fix: EL.5 …`). The rerun: 768 games, 0 errors, 0 capped.
  The Corp order random < contract < easy < standard separates; on the Runner side contract and random do not separate
  (contract − random = −8, 95% [−65, 48]), as the contract agent plays legal but aimless Runner turns.

### Bots

The heuristic policies value nested plays (+250), faceup hosting (+100) and faceup agendas (+40); card-effect installs
from the hosted cards reuse the install valuation. As in EL.3 these paths are reached only by Elevation cards, so the
policy versions stay as they are.

### Client

The Match view model carries the hosted faceup cards (`HostedFaceUp`, each naming its host) and both removed-from-game
piles. The table draws hosted faceup cards on their console like hosted programs, tooltip "Hosted faceup on … · not
installed", and names the removed-from-game pile on the heap and Archives tooltips. The M4.9 UI smoke renders these
states on the real table (`ELEVATION_TABLE PASS hosted_faceup=true threat=true removed=true`, required by
`verify.sh`). **Deviation:** the removed-from-game piles have no table pile of their own yet; EL.8 (the client) gives
them one.
Screenshot (synthetic cards, 1920×1080): `docs/evidence/elevation/el5-hosted-faceup-runner-1920.png`, a card hosted
faceup on the first rig program.

### Rulings and simplifications

| Card | Note |
|---|---|
| BANGUN | "Install an agenda faceup" is the Corp choosing, as the agenda is installed, to turn it faceup; it stays inactive and can still be advanced and scored. |
| Plutus | The additional rez cost is paid by a "when rezzed" ability; the rez is offered only when the Corp can pay one of the alternatives. |
| IP Enforcement | X is chosen as an option (1 to 3, one per printed agenda value), with "remove X tags" as its cost. |
| Madani | "Install 1 hosted program" counts the console's hosted cards through the hosted zone, which holds one console's cards at a time (Limit 1 console). |
| Bling | "Without spending credits" is an install whose final cost was 0, recurring and declared-use credits included. |

### Gate

- Synthetic suite green after the batch; `make gates` on the EL.5 head: see the PR.

## EL.6 — Primitives C: runs and ICE

On `feat/el6-primitives-c`, all in Core before any Batch 3 card, with the four EL.4-family primitives EL.4 handed over
(moving an upgrade, random HQ trash, a copy of an accessed card, Detente's access outside a run):

| Family | Primitives |
|---|---|
| Runs | A Runner move to another server's outermost position (`MoveRunnerToServer`, applied when the run cursor resumes); a run event's approach redirect and extra click (`RedirectApproachTo`, `AdditionalClicks`); Archives run events; a run event reacting to its own run's end; the first end of a run prevented unless the Corp trashes X random HQ cards (`PreventFirstEndRun`); bypass (`BypassEncounteredIce`); derez; ICE approached as an occurrence; swapping the approached ICE with ICE from HQ or Archives. |
| ICE | Credit-cost break abilities only the Runner uses; breaker-subtype break restrictions (`BreakableOnlyBySubtype`); hosts gaining every ICE subtype (`HostGainsEveryIceType`); strength while the only ICE on a server; another ICE's subroutines resolved through options the catalog expands from the pool; rez paying less; forfeit-to-rez (`RezForfeitDiscount`, a `RezCardParameters.ForfeitAgendaId`); a global ICE rez-cost increase from an installed card. |
| Cards and abilities | Mods hosted on non-AI icebreakers with run-long pumps (`HostIcebreakerStrength`); moving an upgrade; ending the run from a paid ability; rezzed cards in the attacked server as targets; random choices (`ChooseAtRandom`); a steal outside a run, with the trash offered at the card's cost. |

The validator knows every new field and frame shape; contract, serialization and fingerprint tests are in
`ElevationPrimitiveContractTests`, and the behavior tests ship with EL.7's cards. The EL.7 fuzz found two Core defects,
fixed on this branch (`fix: EL.6 …`): forfeit-rez variants of the same ICE looked like duplicate actions, and a card-effect
install mid-run (Mycoweb) could trash the encountered ICE and lose the run's ICE cursor. The gates found that the
trigger-automation audit had no verdict for `ChooseAtRandom` and `StealAgenda` (both now the player's decision), as EL.4's
`HostFaceUp` had none.

## EL.7 — Batch 3

23 cards on `feat/el7-batch3`: Ryō "Phoenix" Ōno, Topan, Charm Offensive, Shred, Barry "Baz" Wong, Maintenance Access,
Detente, Maglectric Rapid, Fransofia Ward, GAMEDRAGON™ Pro, Chromatophores, LEO Construction, Bumi 1.0, Scatter Field,
Mercia B4LL4RD, Proprionegation, Empiricist, Mycoweb, Semak-samun, Mitra Aman, N-Pot, Biawak and Flyswatter.

- **Specs:** `src/Netrunner.Cards/Elevation/ElevationBatch3Specs.cs`; Mycoweb's options are expanded from the composed
  pool when the catalog is built.
- **Tests:** 43 Batch 3 tests (`ElevationBatch3Tests`), at least one per card, with the fuzz regressions.
- **Registry and coverage:** EL.7 complete; **159 of 159** cards registered, tested and complete.

### E6

`make core-growth CORE_GROWTH_ARGS='--registry data/m4/batch-registry.json --registry data/elevation/batch-registry.json
--range slice-EL.7=6a7483e..2b52122'`:

| Batch | Core lines | New vocabulary | Cards needing no Core change |
|---|---|---|---|
| slice-EL.7 | +37/−8 | 1 | **23 of 23 (100%)** |

The Core lines are the ladder fix below: the access-trash turn events recorded before the trigger check, and one trigger
condition (`TrashedSubjectWasInstalled`) for Aggressive Trendsetting, an EL.3 card. No EL.7 card uses them.

### Simulation decks and the batch proof

- Four more simulation decks: LEO Construction and Synapse Global (Corp), Ryō and Barry (Runner); 8 Corp and 6 Runner
  decks in all. The fuzz content is `elevation-simulation-v3`.
- **Evidence:** `scripts/fuzz.sh elevation --games 192 --seed 7 --workers 12`: first 190 of 192 terminal (the two
  defects above); after the fixes, 192 of 192.
- `ai-ladder --content elevation --entrants random,contract,easy,standard --seeds-per-pairing 1`: the first run, 768
  games, had 6 errors; the rerun on the fixed head, 768 games, 0 errors, 0 capped. The Corp order random < contract <
  easy < standard separates (+110, +178, +154). On the Runner side, contract doesn't beat random (−35, 95% [−92, 14]),
  as in EL.5, and standard − easy is +48 (95% [−24, 107]): one seed per pairing leaves it inside the noise.

The first run's errors came from three defects, fixed on this branch with regression tests (`ElevationLadderRegressionTests`,
`ElevationBatch1CorpTests`):

- The contract agent rejected N-Pot's credit-cost break as malformed parameters (it expected a lost click).
- When a trash during access was the first installed trash of the turn, Aggressive Trendsetting's trigger became active
  only after the engine had yielded the next breach decision, and its occurrence frame was stranded beneath it
  (`resolution.pending-frame-mismatch`). The trash's turn events are now recorded before the trigger check.
- Aggressive Trendsetting fired again on a later trash of a card that wasn't installed (from HQ): it now needs the trashed
  card to have been installed.

### Bots

The policies value bypasses (+300), derezzes (+200), steals outside a run (+400), mods (+80), Runner moves (+200), ICE
swaps (+150), cheaper rezzes (credits) and purges (+60). Paths reached only by Elevation cards; policy versions unchanged.

### Client

Mods hosted on breakers and Corp cards hosted on Detente use the hosted-card rendering of EL.5; the M4.9 smoke's
Elevation check now includes a Corp card hosted faceup on a console. The encounter panel's "can interface" uses the
full check (gained subtypes, break restrictions).

### Rulings and simplifications

| Card | Note |
|---|---|
| Shred | The Corp pays (trashes X random HQ cards) whenever it can; it is not offered the choice to let the run go on. |
| Detente | Only the Runner uses the click ability ("any player can use" is not modelled). The access outside a run steals an agenda or offers the trash at the card's cost; "when accessed" abilities and steal reactions do not fire. "The first time each turn" is once per turn when used. |
| Mycoweb | "Resolve 1 subroutine" offers each subroutine of each sentry or code gate in the pool while a rezzed copy is installed; subroutines with no instruction equivalent (run restrictions, damage with a printed follow-up, inward installs) are not offered, and a "resolve nothing" option stays available. |
| LEO Construction | The rezzed bioroid is trashed as the ability's first effect rather than as its cost; the ability is offered only when such a bioroid is in the attacked server. |
| Charm Offensive | After a successful Archives run every card there was accessed, so "a copy of a card you accessed in Archives" is a copy of a faceup card in Archives. |
| GAMEDRAGON™ Pro | Pumped strength lasts the run for fixed pumps; a dynamic pump (counted from installed cards) still lasts the encounter. |
| Chromatophores | The gained subtypes count for breaker interfaces; subtype filters elsewhere (Mycoweb's "rezzed sentry") read the printed subtypes. |
| Ryō "Phoenix" Ōno | The Corp chooses the HQ card to trash. |
| Mercia B4LL4RD | Banned in System Gateway + Elevation; implemented for completeness. |

### Gate

- Synthetic suite green after the batch. `make gates` at `c589092` passed m5-gate, tactics and the AI and CLI suites; verify
  and the Match suite failed only on the trigger-automation audit (verdicts now in EL.4 and EL.6) and a stale coverage
  report, both fixed. The full gates for the stack run on the EL.8 head.

