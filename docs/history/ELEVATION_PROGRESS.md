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
| EL.8 integration: decks, formats, bots, client and the milestone gate | `feat/el8-integration` | complete; its decks retired by EL.10 |
| EL.9 the NSG Core Sets format: NSG's 21 decks, no ban list (D-EL-9) | `feat/el9-nsg-core-sets` | complete |
| EL.10 the Core Battle Box replaces our decks, no ban list (D-EL-10) | `feat/el10-core-battle-box` | complete |

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

## EL.8 — Integration: decks, formats, bots, client and the milestone gate

On `feat/el8-integration`.

<a id="el8-decks"></a>

### Decks (D-EL-4)

Fourteen decks of our own in `data/decks/gateway-elevation-decks.json`, one per Elevation identity, so every faction has a
Corp or Runner deck for each of its two Elevation identities. Each has a one-line plan and declared archetypes. All are
legal under the `gateway-elevation` format snapshot: 45 cards; influence within the identity's limit; 20 agenda points
for the Corp decks, with at most 3 agendas of 3 or more points; no banned card (Cleaver, Seamless Launch, Offworld
Office, Mercia B4LL4RD); and Limit 1 per deck respected. `CardFormatTests` validates them against the format, and every
card is bound. Provenance: ours, built on 3 October 2026.

| Deck | Identity | Plan |
|---|---|---|
| Bioroid Wall | LEO Construction | A wall of bioroids that ends runs, trashed by the identity when a run must stop. |
| Trendsetters | Poétrï Luxury Brands | Score fast with extra clicks, installing from R&D on every score. |
| Clone Vats | AU Co. | Net damage and HQ trashes charge the identity, which sculpts R&D. |
| Silk Road | PT Untaian | Keep HQ small and seed advancement counters on unrezzed cards. |
| Headliners | Nebula Talent Management | An operation every turn flips the identity for extra clicks. |
| Fast News | Synapse Global | Tags in, tags out, and a free install from HQ whenever one is removed. |
| Open House | BANGUN | Agendas installed faceup as bait; taking them costs meat damage and tags. |
| Shell Companies | The Zwicky Group | Every credit from an agenda or operation draws a card. |
| Phoenix Fire | Ryō "Phoenix" Ōno | Take a subroutine, finish the run, and burn a card from HQ. |
| Street Gang | Topan | Cheap installs every turn, paid for in meat damage, for a big anarch rig. |
| Tri-Maf | Barry "Baz" Wong | Every rezzed ICE installs hardware or a resource, and Fransofia Ward taxes the rez. |
| Market Runs | MuslihaT | Breakers and run events off the top of the stack, then run every turn. |
| Shadow Play | Dewi Subrotoputri | Fill memory, run, and flip between credits and cards. |
| Cryptarchitect | Magdalene Keino-Chemutai | Overdraw, then install programs and hardware from the discards. |

**For the user to review.** The menu doesn't offer them until `PlayFormats.ElevationDecksReviewed` is set. Until then,
developer builds and `--elevation-preview` show them.

### The format choice (D-EL-8)

- **Menu:** New game offers a **Gateway + Elevation** mode next to Gateway decks and Tutorial. It is shown once the decks are reviewed (`PlayFormats.ElevationDecksReviewed`), or now in developer builds and with `--elevation-preview`. The mode lists the format's 14 decks as tiles (identity cards from the whole pool), with Hard and the personalities, and Random draws from the chosen format's decks.
- **Games:** `PlayFormats` (Match) bundles each playable format's card data, behavior and runtime catalogs, metadata and presets. A pairing that includes an Elevation deck plays on the Gateway + Elevation pool; Gateway games keep the embedded Gateway payloads, so their pins are unchanged. The setup form lists the Elevation decks as `ELEVATION · …`.
- **Saves:** a save records its format (`SoloSaveMetadata.FormatId`; absent in older saves, which load as Gateway), and the save service validates and loads each save with its own format's content. A build that can't play the format lists the save as incompatible.
- **Client fixes found on the way:**
  - A card effect's install choice (Synapse Global, Peer Review) had no controls or readable labels; it now groups with install choices.
  - History names a Runner moved to another server, and the EL.6 instructions have effect labels.
  - A faceup installed agenda reads `FACEUP · INACTIVE`, not `FACE-DOWN`.
  - Removed-from-game cards are browsable with their side's discard pile (EL.5's deviation).

### E6 over the milestone

Each card batch, measured over its own range past its primitive slice (the gate):

| Batch | Range | Core lines | New vocabulary | Cards needing no Core change |
|---|---|---|---|---|
| slice-EL.3 | `2ad657c..fa8ee4b` | +0/−0 | 0 | **43 of 43 (100%)** |
| slice-EL.5 | `4862fc8..140fcc7` | +0/−0 | 0 | **16 of 16 (100%)** |
| slice-EL.7 | `6a7483e..2b52122` | +37/−8 | 1 | **23 of 23 (100%)** |

Each batch with its primitive slice (`--range slice-EL.3=bdc3d6f..142900b --range slice-EL.5=142900b..c7c89d4 --range
slice-EL.7=c7c89d4..2b52122`), the whole-set report:

| Batch | Core files | Core lines | New vocabulary | Cards needing no Core change |
|---|---|---|---|---|
| EL.2 + EL.3 | 39 | +3,394/−223 | 70 | 9 of 43 (21%) |
| EL.4 + EL.5 | 30 | +1,030/−87 | 17 | 4 of 16 (25%) |
| EL.6 + EL.7 | 29 | +1,217/−91 | 21 | 10 of 23 (44%) |
| **All** | | **+5,641/−401** | **108** | **23 of 82 (28%)** |

The plan expected about 28%. Against Gateway's 12 of 72 (17%) and +45,795 Core lines, Elevation's 82 cards needed
+5,641: the primitive vocabulary carried most of the set. EL.8's integration added a little Core: an observation field
for faceup inactive agendas, and the fixes the deck-pool ladder and the 10,000-game fuzz found (seven, below).

### Bots

- **Deck-pool ladder (A1b, A7):** `ai-ladder --content gateway-elevation --entrants easy,standard --deals 240` (pool
  `gateway-elevation-v1`, the 48 pairings of the format's decks; 960 games).
  - The first run had 17 errors from four engine defects, all fixed with regression tests (`fix: EL.8 …`):
    - Proprionegation moving the Runner from the server itself;
    - breach access modifiers left on the old attacked server after a move;
    - a run event hosted faceup on Bling;
    - Aggressive Trendsetting's trash trigger under Gourmand's access ability.
  - The rerun: **960 games, 0 errors, 0 capped, no deck warning**. Standard beats Easy on both seats: Corp +119 Elo (95% [77, 161]), Runner +71 ([28, 117]).
  - The pool favours the Corp: Standard's Corp scores 75% and its Runner 39%.
- **Tracker (A9):** the bots' board reads both hosted faceup zones; a card hosted from the hand (Madani, Detente) leaves it, and a face-down slot the hand can't account for is the card that left faceup Archives (Mycoweb). Faceup installed agendas and removed-from-game cards were already public (visible cards and Remaining counts).
- **`ai-belief`** on the saved games of the second ladder run (not used in any fit):

  | Tier | Slot log loss (prior) | Slot top-1 (prior) | HQ agenda error (prior) | Tracked-HQ precision | Confident guesses right |
  |---|---|---|---|---|---|
  | Easy | 3.088 (3.282) | 11% (3%) | 0.786 (0.875) | 75% | 90% |
  | Standard | 2.997 (3.282) | 15% (3%) | 0.765 (0.875) | 69% | 87% |
  | Hard | 1.819 (1.941) | 27% (19%) | 0.756 (0.825) | 69% | 87% |

  Every tier beats its prior on every measure, and Easy's forgetting shows.
  - **The HQ agenda rate per pool (A7, A9):** before it, the tracker's HQ agenda error was worse than the prior on this pool (Standard 0.901 against 0.870).
    - Why: the Corp bots hold agendas in HQ, so at a Runner turn start HQ had 1.40 agendas against the unseen rate's 0.80. The Runner almost never tracks an agenda in HQ (accessed ones are stolen), and knowing about 0.46 non-agenda HQ cards lowered its estimate further.
    - The fix, `TrackerPrior.HqAgendaFactor`, scales the untracked HQ cards' agenda rate for this pool by 1.866, capped by the untracked cards and the unseen agendas. It was fitted on 480 saved games of the first ladder run, and checked on the second run's games above.
    - Runner v21's HQ valuation and the measure use it. The Gateway pool keeps 1, so its play and the `BeliefAccuracyTests` fixtures are unchanged.
    - The deck-pool ladder with it: 960 games, 0 errors, no deck warning; Standard − Easy is Corp +100 (95% [59, 141]) and Runner +88 ([44, 130]), inside the previous intervals.
- **Per-format fit (A7):** `ai-fit-evaluation --content gateway-elevation --games 400 --holdout-games 120 --feature-version 2 --compare data/ai/evaluation-v2.json`. On 2,574 held-out positions per side, the pool's own fit beats the general fit:

  | Side | Pool fit | General fit | Hand evaluation | Base rate |
  |---|---|---|---|---|
  | Corp | **0.528** | 0.558 | 0.606 | 0.674 |
  | Runner | **0.535** | 0.561 | 0.591 | 0.674 |

  So Gateway + Elevation games plan with `data/ai/evaluation-gateway-elevation-v2.json`, which `FittedEvaluation` selects by the game's behavior version. Gateway games keep the general fit, so the M5 pins are unchanged. Policy versions are unchanged too: the change reaches only Standard's and Hard's planning in Elevation games, which no saved game had before this slice.
  The deck-pool ladder rerun with the pool's fit: 960 games, 0 errors, 0 capped, no deck warning. Standard against Easy
  moved to Corp +111 (95% [72, 150], from +119) and Runner +90 ([45, 132], from +71), inside each other's intervals.
  Easy doesn't plan, so this measures Standard.

### Hardening

- `scripts/fuzz.sh gateway-elevation --games 10000 --seed 8 --workers 12` (random-legal and heuristic seats over the 48 pairings). The first run had 9,975 of 10,000 terminal: 25 defects in three classes, fixed with `ElevationFuzzRegressionTests` (`fix: EL.8 …`):
  - **21:** a card-effect install (Topan, Magdalene, Synapse) offered Botulus or Chromatophores for the rig, which install only on ICE;
  - **3:** Poétrï installing from HQ after a steal left the breach more random accesses than cards, and the breach prompt had no action;
  - **1:** an ICE subroutine trashing a breaker under GAMEDRAGON™ Pro left its run-long pumps behind.

  The rerun on the fixed head: **10,000 of 10,000 terminal** (mean 389 actions; `status=ok`).
- The deck-pool ladder's own defects are under Bots.

### Rulings and simplifications

| Card | Note |
|---|---|
| Topan, Synapse Global, Magdalene, Poétrï | A card-effect install doesn't choose a host, so it doesn't offer a program that installs only on ICE (Botulus, Tranquilizer, Chromatophores). |
| GAMEDRAGON™ Pro | A pump made while it is hosted lasts the run even if the mod leaves; it ends when the breaker leaves play. |
| Jailbreak (and other breach modifiers) | "When you breach the attacked server" follows the attacked server when the Runner is moved (Proprionegation). |

### Client

- **Every new state presented** (the plan's section 8):
  - agenda counters, the threat level, flipped identities, recurring credits (EL.2, EL.3);
  - faceup installed agendas, marked `FACEUP · INACTIVE`;
  - hosted faceup cards on consoles, Runner's and Corp's;
  - removed-from-game cards, named on the discard tooltips and browsable with each discard pile;
  - for bypass, derez, a moved Runner and a changed server: the run HUD reads the attacked server, and history names a move to another server and each derez;
  - card-effect install choices, with controls and labels.
- **Accessibility re-check:** the M4.9 smoke checks the Elevation states at 1920×1080 and 2560×1440 on both seats. Hosted cards and the pile browser must stay inside the viewport, and the removed-from-game pile must take keyboard focus (`ELEVATION_TABLE PASS … side=… size=…`, four lines, required by `verify.sh`). Screenshots, synthetic cards:
  - `docs/evidence/elevation/el8-elevation-states-{runner,corp}-{1920,2560}.png`;
  - `docs/evidence/elevation/el8-removed-pile-{runner,corp}-{1920,2560}.png`.
- **Smoke matrices:**
  - the M4 full-match matrix and the M5 client save/resume matrix include a Gateway + Elevation pairing (Fast News against Cryptarchitect; `--preset elevation`);
  - the main-menu smoke previews the Gateway + Elevation area at both resolutions (`elevation=true`).
- **Every deck through the client:** `scripts/elevation-deck-smoke.sh` plays each of the 14 decks on its side as the human seat (the contract agent clicking through the presented actions) against the bot, to a finished game and a reload. **14 of 14 PASS.**
- **Not done:** with a real display (Xvfb), the main-menu smoke fails at its first New game check, in this checkout and without the Elevation preview too; `make verify` runs it headless, where it passes. Menu and in-game captures with real decks show official card text and art, so they stay local.

### Review fixes and CI

The PR reviews (#84–#91) found 18 items; each slice's branch carries its own fix commit, and the stack was rebased onto
them:

- **EL.1:** the main-menu smoke and the onboarding smoke needed official card data, so the synthetic fast CI tier
  failed. Without content, the menu smoke now checks that New game and Load game are disabled and the data notice shows,
  and `verify.sh` skips the onboarding smoke.
- **EL.2:** a deferred occurrence opens when a run resumes; install finalization counts declared hosted credits;
  "played this turn" reads only the controller's own turn.
- **EL.3:** Lie Low may remove 1 tag; Open Market pays for a unique connection that replaces its copy.
- **EL.4:** a run event hosted on a console (Bling) plays from there; a hosted card set aside by a behavior keeps its
  host.
- **EL.5:** MuslihaT takes the first matching card and knows the top card; Synapse Global and Off the Books reveal what
  they install; public reveals reach the other player (history names the card by title, not by instance).
- **EL.6:** a steal outside a run raises the scored-or-stolen occurrence (Détente); rez effects pay with hosted rez
  credits (Biawak's forfeit rez, Mycoweb); the credit-cost ICE ability event keeps its cost (N-Pot).
- **EL.7:** Mycoweb's copy of a trash subroutine needs a target, or else ends the run.
- **EL.8:** the hosted faceup board features are left out when absent, so Gateway feature digests and saves are
  unchanged.

The fixes changed Core in the primitive slices (EL.2 +17/−3, EL.4 +16/−4, EL.6 +42/−13) and once in a batch: EL.5's
reveal fix let private choice candidates include cards in the opponent's hand (`ObservationBuilder.Cards`, +6/−2,
no new vocabulary). The slice-EL.5 E6 row above predates that fix: counted strictly, Batch 2 is no longer free of Core
changes, though it added no vocabulary.
Three items stand as built and are answered on their PRs: Syailendra may target its own server, as the card text
allows; the EL.1 snapshot marks the format complete because the behaviors ship in the stack and the menu stays behind the
review flag; and Off the Books keeps its destination choice before the search, which offers the same outcomes.

## EL.9 — The NSG Core Sets format (D-EL-9)

Null Signal Games publishes 21 System Gateway + Elevation sample decks, the "NSG Core Sets" decks, on its
[getting-started page](https://nullsignal.games/players/getting-started-sample-decklists/), each with a NetrunnerDB
entry. They are built without the Startup ban list: 9 of the 12 Corp decks use Offworld Office, Seamless Launch or
Mercia B4LL4RD, and Fine Print is NBN: Reality Plus, all banned in 26.05. So they get a format of their own beside
Gateway + Elevation, which keeps its ban list and our 14 decks.

### Format and decks

- `data/formats/nsg-core-sets.json`: the Gateway + Elevation pool and deck rules, no restriction, effective 24 April 2025.
- `data/decks/nsg-core-sets-decks.json`: the 21 lists (12 Corp, 9 Runner, one per identity), each with its NetrunnerDB
  URL. The names are NSG's; the one-line summaries and archetypes are ours.
- `CardFormatTests`: every deck is bound, valid in the format, and 9 Corp decks fail under the 26.05 bans.
- Every card in the lists was already implemented: the format needed no card work.

### Plumbing

- `PlayFormats` lists the format. Both Elevation-pool formats share one behavior catalog, so `FormatOf` tells them
  apart by the match's decks, and a save records and reloads the right one (`PlayFormatsTests`).
- The menu offers "NSG Core Sets" wherever the pool is installed: NSG's published lists need no review gate. Random
  opponents come from its decks (`main-menu-smoke`: 12 Corp decks, 9 Runner decks plus Random).
  Evidence: `docs/evidence/elevation/el9-menu-nsg-core-corp-{1920,2560}.png`.
- `--content nsg-core-sets` (108 pairings, pool `nsg-core-sets-v1`) for the fuzzer, the ladder and `ai-fit-evaluation`;
  `elevation-deck-smoke.sh --format nsg-core-sets`; verify's full-match matrix adds an NSG pairing.

### Defects found

- **Client deck smoke** (21 decks, human against bot on its side): 19 of 21 passed at `--jobs 6`.
  - Gimbatul against Enthusiasm: Madani's once-per-turn install, used on the Runner's turn and again on the Corp's, was
    in both players' turn facts, and the view mapper stopped the client on the "duplicate". Fixed (shown once, as
    first-X uses are), with `AbilityUsePresentationTests`. Our Magdalene deck has Madani too; no smoke had hit it.
  - Fashion Lab against Flow and Ebb hit the smoke's 180-second cap at turn 30 (2,259 actions, no repeated state)
    with six games in parallel; it passes alone, as does the Madani pairing after the fix.
- **10,000-game fuzz** (`scripts/fuzz.sh nsg-core-sets --games 10000 --seed 9 --workers 12`): 9,924 terminal, 76 defects
  in three classes, all from cards and combinations our decks didn't have; each replayed by seed in
  `NsgCoreSetsFuzzRegressionTests`:
  - The Zwicky Group (61): Greenmail forfeited to rez Biawak mid-run gains credits through an agenda; the deferred
    first-card-credit occurrence failed the run's stack topology when the run resumed.
  - Plutus (7): it plays Peer Review from Archives, whose install replaces Plutus itself.
  - Byte! (8): accessed on top of R&D, its tag made NBN: Reality Plus draw 2, which drew Byte! before its net damage.
    The engine now records a begun trigger whose source another effect moved
    (`CatalogTriggerSourceMovedAfterStart`, omitted when false, so saves and hashes are unchanged), and a forged flag is
    rejected (`CatalogTriggerRuntimeTests`).

- **The fuzz rerun** on the fixed head, same seed: **10,000 of 10,000 terminal**.

### Bots on NSG's decks

NSG Core Sets shares the Gateway + Elevation card pool, so its games use that pool's fitted evaluation and HQ agenda
factor (1.866), both measured on our 14 decks. The slice checked both on NSG's decks (the user's decision: one bot for
every format, with per-pool numbers, and Gateway as a regression floor).

- **Deck-pool ladder:** `ai-ladder --content nsg-core-sets --entrants easy,standard --deals 240`, two runs (seeds 91 and
  92; 960 games each): **0 errors, 0 capped**. Standard beats Easy on both seats in both runs: Corp +140 (95% [96, 179])
  and +103 ([55, 154]), Runner +81 ([32, 127]) and +86 ([36, 134]). The pool favours the Corp more than ours: Standard's
  Runner scores 31–32% (39% on our decks).
  - Six pairing warnings over both runs (2–3 deals each, none repeated). Pooled per deck, Standard scores at least as
    well as Easy with every deck on both seats, so they are small-sample noise. It adds least with Glyph of Warding
    (Corp, +0.01) and with Enthusiasm and Prick Thyself (Runner, +0.01 to +0.02): tuning leads, not defects.
- **Fitted evaluation:** `ai-fit-evaluation --content nsg-core-sets --games 400 --holdout-games 120 --feature-version 2
  --compare data/ai/evaluation-gateway-elevation-v2.json`. On 2,309 held-out NSG positions per side, the shared
  Gateway + Elevation fit is best:

  | Side | Shared fit | NSG's own fit | Hand evaluation | Base rate |
  |---|---|---|---|---|
  | Corp | **0.665** | 0.709 | 0.707 | 0.732 |
  | Runner | **0.672** | 0.712 | 0.693 | 0.732 |

- **`ai-belief`** on both runs' saved games, with a sweep of HQ agenda factors in the same pass. Every tier beats its prior
  on slot log loss and top-1 in both runs; the HQ agenda error:

  | Factor | Easy (run A / B) | Standard | Hard |
  |---|---|---|---|
  | prior | 0.899 / 0.845 | 0.899 / 0.845 | 0.899 / 0.845 |
  | **1.866 (the shared factor)** | 0.860 / 0.847 | 0.838 / 0.822 | 0.801 / 0.780 |
  | 1.6 (NSG's best) | 0.841 / 0.813 | 0.836 / 0.808 | 0.814 / 0.782 |

  NSG's decks would prefer about 1.6 for Easy and Standard, and the shared factor for Hard. The gain is at most 0.03
  agendas and Hard loses, so NSG keeps the shared factor, and no number is keyed by format. One gap remains: with it,
  Easy (the forgetful tier) misses its prior on run B by 0.002.
- **Cost:** the measurement took about 3 hours (fuzz rerun 66 minutes, ladders 15 minutes each, `ai-belief` 6–12 minutes
  per pass, the fit 9 minutes). `ai-belief` now loads and scores games in parallel and sweeps factors in one pass; it
  still scales poorly with workers, which the next step (profiling the verification runs) looks into.

### Gate

- `elevation-deck-smoke.sh --format nsg-core-sets --jobs 3`: **21 of 21** decks play human against bot on their side.
- `make gates REF=e2cc3f4` PASS: verify (with the NSG menu area and pairing), m5-gate, tactics and bot-check (AI 133,
  Match 1,011, CLI 171). The synthetic verify, the CI fast tier, passes too.

## EL.10 — The Core Battle Box (D-EL-10)

Real players' lists replace the 14 decks we built in EL.8. NetrunnerDB's popularity sort for System Gateway + Elevation
lists is led by lunari's **Core Battle Box** (featured on
[CardGamer](https://cardgamer.com/features/introducing-the-netrunner-core-battle-box/)): seven decks, one per faction,
"designed to be played against the other decks", using only System Gateway and Elevation.

- `data/decks/elevation-core-battle-box-decks.json`: Nebula Fast Advance, PT Untaian Shell Game, Zwicky Supermodernism and
  LEO Glacier against Topan, MuslihaT and Magdalene, each with its NetrunnerDB URL (lunari's names; our summaries and
  archetypes).
- Zwicky Supermodernism (Offworld Office) and LEO Glacier (Seamless Launch, Offworld Office, Mercia B4LL4RD) use cards
  banned in 26.05, so the format drops its ban list until the full Startup format is in. Its rules are now NSG Core
  Sets'; the two differ in their decks.
- Our 14 decks are retired: still in the catalog under their IDs (`RetiredElevationDecks`), so older saves load and EL.8's
  seed replays play, but out of the menu and the deck pool (`gateway-elevation-v2`, 12 pairings).
- The menu's **Core Battle Box** area needs no review gate (`--elevation-preview` is gone). Evidence:
  `docs/evidence/elevation/el10-menu-core-battle-box-corp-{1920,2560}.png`.

### Evidence

- `scripts/fuzz.sh gateway-elevation --games 10000 --seed 12` (a new pool, so the milestone size, every replay
  verified): **10,000 of 10,000 terminal** in 25 minutes.
- `elevation-deck-smoke.sh --jobs 3`: **7 of 7** decks play human against bot through the client.
- Ladder (`--deals 240 --batch-deals 48 --stop-when-clear`): 960 games, 0 errors, 0 capped, no deck warning. Standard
  beats Easy on both seats, Corp +83 (95% [31, 130]) and Runner +47 ([1, 88]). The bots find this pool very Corp-sided:
  Standard's Corp scores 86% and its Runner 21% (31% on NSG's decks). The box is built for human play, so the Runner
  bots' play with these decks is a tuning lead.
- Fitted evaluation (`ai-fit-evaluation --content gateway-elevation --compare
  data/ai/evaluation-gateway-elevation-v2.json`): the shared fit matches one trained on the box (Corp 0.457 against
  0.457, Runner 0.460 against 0.457), so the pool keeps it.
- `ai-belief` with the sweep: every tier beats its prior on log loss, top-1 and HQ agenda error. As on NSG's decks, a
  factor of 1.6 edges out the shared 1.866 (Standard 0.863 against 0.872), now measured on both deck pools the format
  family offers, while 1.866 was fitted on the retired decks. Retuning it is a bot change for its own measured slice.

## Milestone gate (section 7)

- [x] **Coverage:** 159 of 159 bound, tested and checklist-complete; no drift (`verify.sh`); CARD-401 green.
- [x] **E6:** each card batch at 100% with no Core change past its primitive slice; the whole-set report above (28%). The
  EL.5 review fix later added a 6-line observation change to Batch 2, with no new vocabulary (see Review fixes).
- [x] **Formats:** the snapshots load and validate; Gateway + Elevation is playable with its decks (since EL.10 the Core Battle Box, offered without a review gate).
- [x] **Decks:** each new deck validates (`CardFormatTests`), loads and plays human against bot through the client on its side (`elevation-deck-smoke.sh`, 14 of 14). EL.10 replaced them with the Core Battle Box (D-EL-10).
- [x] **Hardening:** 10,000 clean official fuzz games over the pool; the full `make m5-gate`, tactics and every suite:
  `make gates REF=f9acda7` PASS (verify, m5-gate, tactics, bot-check: AI 132, Match 984, CLI 171).
  After the review fixes, on `129c5cc`: `make gates` passed m5-gate and tactics. Its verify (card-art smoke) and one
  Match coordinator timeout failed while a synthetic verify ran beside it, and both passed rerun alone (verify; bot-check:
  AI 133, Match 985, CLI 171). The synthetic verify, the CI fast tier, passes too.
- [x] **Bots:**
  - every Elevation card played without errors, with a non-default valuation (EL.3, EL.5, EL.7 ladders and proofs: 0 errors);
  - the deck-pool ladder has no deck warning;
  - `ai-belief` beats its prior on the new pool on every measure and tier, with the pool's HQ agenda rate.
- [x] **Client:** every new state presented; accessibility re-checked; the smoke matrices include a new deck.
- [x] `docs/ELEVATION_PROGRESS.md` complete, with evidence and deviations.
