# Local card data

System Gateway metadata is a local-only developer artifact. The official output
paths under `data/vendor/system-gateway/` are intentionally ignored by Git, so a
clean clone contains no redistributed official card records. The updater and
local validation command use the pinned upstream source only when a developer
explicitly requests them; runtime/build code never fetches the network. The
current source is commit
`236140cf84491c23995b028bc791281837520b97`, retrieved for the local snapshot
on `2026-08-28` from `pack/sg.json` (77 records, upstream schema v1).

The checked-in normalized schema is
[`schema/normalized-card-schema.json`](schema/normalized-card-schema.json).
It is version 1 and contains only the fields needed for card identity, display,
deck validation, and future rules work:

- `code` and `title` are retained as stable display identifiers;
- `side_code`, `faction_code`, and `type_code` become `side`, `faction`, and
  `type`;
- upstream `keywords` becomes the deterministic `subtypes` array split on
  `" - "` (empty/missing keywords become an empty array);
- `text` is retained as metadata, with lexical `[symbol]` tokens normalized to
  `<symbol:name>` (no rules are inferred; an explicitly empty upstream string
  remains empty);
- `pack_code`, `quantity`, `deck_limit`, and `uniqueness` are retained as
  `setCode`, `quantity`, `deckLimit`, and `uniqueness`;
- `cost`, `strength`, `memory_cost`, `trash_cost`, `agenda_points`, and
  `advancement_cost` become the corresponding camel-case nullable numeric
  fields, with `faction_cost` deliberately mapped to `influenceCost`;
- `minimum_deck_size`, `influence_limit`, and `base_link` are retained for
  identity records only.

The normalized file deliberately excludes `stripped_text`, narrative/flavor
text, artwork, frames, backs, illustrator names, source positions, and all
executable card behavior. Official card records are not registered in the M1
fixture behavior catalog. Missing nullable upstream values are emitted as
explicit JSON `null`; required normalized fields and type-specific constraints
are validated strictly.

Regenerate the local snapshot with the explicit, reproducible command:

```bash
./scripts/update-card-data.sh \
  --commit 236140cf84491c23995b028bc791281837520b97 \
  --retrieval-date 2026-08-28
```

The updater downloads only source JSON/schema/license files over HTTPS, stages
and validates all output before installation, and never downloads artwork. The
source license identity is recorded as `NOASSERTION`: the upstream
`COPYRIGHT.md` is copied for local review, but it does not grant this project a
redistribution license. Do not add the ignored official output to Git or ship
it in a release.

The generated `cards.json`, `manifest.json`, and `SOURCE_COPYRIGHT.md` remain
ignored local files. A clean clone intentionally has no official card records;
the checked-in synthetic fixtures and normalized schema are sufficient for
offline tests.
