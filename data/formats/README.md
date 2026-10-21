# Format snapshots

Each file is one versioned format (C2, [Elevation plan](../../docs/history/ELEVATION_PLAN.md) D-EL-3): the packs whose cards
are legal, the ban list, the deck rules and an effective date. `format.schema.json` is the schema;
`Netrunner.Cards.CardFormatCatalog` embeds and validates every file (FORMAT-401), and `DeckValidator` takes a format.

| File | Format | Pool | Playable |
|---|---|---|---|
| `gateway.json` | System Gateway | `sg` | yes |
| `gateway-elevation.json` | System Gateway + Elevation, no ban list (the Core Battle Box's format, D-EL-10) | `sg`, `elev` | yes |
| `nsg-core-sets.json` | NSG Core Sets (System Gateway + Elevation), no ban list | `sg`, `elev` | yes |
| `startup-2025.json` | Startup 2025 (`startup_8`) | `sg`, `elev`; Liberation missing | no, incomplete |
| `startup-2026.json` | Startup 2026 (`startup_10`), Balance Update 26.09 | `sg`, `elev`; Vantage Point missing until VP.8 | no, incomplete |

Ban lists are card codes from the pinned upstream `mwl.json` at commit `236140cf84491c23995b028bc791281837520b97`. A
ban on a card from a missing pack is kept as data; such a card can't appear in a deck this build validates.
Startup 2026's list is the exception: Balance Update 26.09 (effective 21 September 2026) is newer than that commit, so it
is taken from [Null Signal's announcement](https://nullsignal.games/blog/startup-balance-update-26-09/) (Vantage Point
plan, D-VP-5).
