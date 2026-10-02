# Elevation progress — the second card set

Plan: [Elevation implementation plan](ELEVATION_PLAN.md). Audit: [ELEVATION_PRIMITIVES.md](ELEVATION_PRIMITIVES.md).
Registry: `data/elevation/batch-registry.json`. Evidence is from the Linux x86_64 development box.

## Branches

| Slice | Branch | Status |
|---|---|---|
| EL.1 preparation: card data, coverage, formats, deck rules | `feat/el1-prep` | complete |

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
