# M4 DATA-401B preset revalidation

Revalidated: 2026-09-05

Authenticated metadata: `236140cf84491c23995b028bc791281837520b97`

Revalidation artifact: [`data/m4/preset-revalidation.json`](../data/m4/preset-revalidation.json)

Artifact SHA-256: `8b134eb3e828f9388eab1228576e4c1ba282a151606636b73e979bfaf11656bf`

The four official System Gateway – Remastered Edition lists were checked
against the published [System Gateway card lists](https://nullsignal.games/products/system-gateway/)
and the two [Runner Learn to Play](https://nullsignal.games/players/learn-to-play/learn-to-play-runner/)
and [Corp Learn to Play](https://nullsignal.games/players/learn-to-play/learn-to-play-corp/)
guides. The product page's Card Lists section identifies the starter cards and
the 10-card advanced booster for each side. The Learn to Play guides state the
30-card Runner and 34-card Corp starter sizes and the 40-card Runner and
44-card Corp sizes after adding the booster cards. The published material does
not define an official 50-card teaching preset.

The code-only runtime payload remains [`data/m4/presets.json`](../data/m4/presets.json).
The revalidation artifact is the reviewable name-to-code bridge: names and
quantities are transcribed from the published lists, while each stable code is
resolved against the authenticated 77-card snapshot. Runtime card text and
artwork are intentionally absent from both preset payloads.

| Preset | Tutorial identity | Published composition | Exact quantity | Distinct codes |
|---|---|---|---:|---:|
| `system-gateway-basic-runner` | `30076` The Catalyst | starter cards | 30 | 16 |
| `system-gateway-expanded-runner` | `30076` The Catalyst | starter + 10-card Runner booster | 40 | 21 |
| `system-gateway-basic-corp` | `30077` The Syndicate | starter cards | 34 | 16 |
| `system-gateway-expanded-corp` | `30077` The Syndicate | starter + 10-card Corp booster | 44 | 22 |

Each artifact entry records the complete ordered `code`, published `name`, and
`quantity` tuple. The DATA-401B tests compare every tuple, in order, against
both the checked-in JSON and the embedded `OfficialPresetCatalog`; totals are
asserted as an additional guard. In an official content-enabled build,
`RevalidationCardsResolveAgainstAuthenticatedRuntimeCatalog` also validates
the preset catalog against the authenticated `CardDataSet`, checks each
published name against the normalized metadata title, and verifies every code
is present in `GatewayRuntimeCardCatalog`. The synthetic clean-CI lane runs the
exact artifact/file/catalog comparison and leaves the authenticated title and
runtime-ID check to the official content lane.

The Cards coverage checklist was reviewed with the generated artifact on this
date. `docs/generated/gateway-coverage.json` reports 77 total, 77 registered,
77 tested, 77 full-text complete, zero partial, zero unregistered, and an empty
diagnostics array. Every card has non-empty positive-test metadata, CR/ruling
citations, observation, hidden-information, and serialization fields. The
generated Markdown summary now agrees with those counts, including the
Offworld Office row, so the DATA-401B review found no outstanding coverage
diagnostics.
