# Elevation progress — the second card set

Plan: [Elevation implementation plan](ELEVATION_PLAN.md). Audit: [ELEVATION_PRIMITIVES.md](ELEVATION_PRIMITIVES.md).
Registry: `data/elevation/batch-registry.json`. Evidence is from the Linux x86_64 development box.

## Branches

| Slice | Branch | Status |
|---|---|---|
| EL.1 preparation: card data, coverage, formats, deck rules | `feat/el1-prep` | complete |
| EL.2 primitives A: values, conditions, counters, timing, identity state | `feat/el2-primitives-a` | complete |
| EL.3 Batch 1: 43 cards | `feat/el3-batch1` | complete |

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

