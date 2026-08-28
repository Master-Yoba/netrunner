# M1 completion and M2 readiness

Status: **M1 complete — ready to begin M2**  
Reviewed: 28 August 2026

## Exit-gate evidence

| M1 requirement | Result | Evidence |
|---|---|---|
| Typed state and deterministic serialization | Pass | Stable typed IDs, detached snapshots, strict invariant validation, serialized RNG/allocators, canonical JSON and SHA-256 hashes |
| Atomic legal-action boundary | Pass | Decision identity/revision and action membership checks; rejection and injected-resolution failures leave state, RNG, logs, sequences, and hashes unchanged |
| Serializable resolution | Pass | Versioned LIFO frames, nested decisions, and save/load during a pending prompt round-trip exactly |
| Evidence, scenarios, and replay | Pass | Accepted action/event logs, strict scenario builder, deterministic replay, and first-divergence diagnostics |
| Toy game and agents | Pass | Fixture decks and legal-action-only agents finish by Corp agenda points, Runner agenda points, and deck-out |
| Local card-data workflow | Pass | Pinned official System Gateway source normalizes 77 metadata-only records deterministically; strict provenance, schema, checksum, code, sentinel, and unbound-behavior checks pass |
| Offline/distribution boundary | Pass | Generated official data and copied notice are Git-ignored; clean verification does not fetch; no artwork, frames, or backs are included |
| Automated gate | Pass | Full solution builds with zero warnings/errors; 139 tests pass; format, secret scan, headless Godot import, runtime smoke, all toy win paths, and replay verification pass |

## Deterministic proof values

For seed `42`, the final reviewed CLI results are:

| Goal | Winner / reason | Actions | Final state SHA-256 | Event stream SHA-256 |
|---|---|---:|---|---|
| `corp-score` | Corp / agenda points | 7 | `0fd4d2782b566c008c10ca89efab5eba9b3d44f6b4950dc719f971f7f528273c` | `359afdbb666cec9b4b32a2ce7c0ce8933bf10d9264d8d866a8e6178dfab87b05` |
| `runner-steal` | Runner / agenda points | 3 | `533694e5cc1addb803a886ffc60003211acc0a46fba018aa0554a019deba62db` | `063923e474547c4c16ced9ab9f534c0d313cca5cfc54e03c4c6a5193c044aaf0` |
| `deck-out` | Runner / deck-out | 8 | `f5979a2232e443688ec85d3005451d0f2baf2ff247919e277727da9055d570ec` | `bd16fec9e42732817d721060e444933c248b0ca874a38dabc9e62f044133766b` |

The local System Gateway snapshot is pinned to upstream commit
`236140cf84491c23995b028bc791281837520b97`. Two updater runs produced no card
diff and the same hashes:

- normalized cards: `a190e6e63a6d58734d915258ebb6b5659ffef0e253fc4c5128776256ee3d699b`;
- manifest: `e7642ae499de9f78e7b7763705fd4170689ab02c70b45ab69e3fd1dfc541b2eb`;
- copied upstream copyright notice: `f0b267636c0b20850e627010038a8ec1e412b5c0335ddad47a2f677f88133a6b`.

These three generated files remain local and must not be committed or shipped.
The implementation, normalized schema, and synthetic fixtures are checked in.

## M2 boundary

M2 may build the representative run and timing engine on the accepted M1
contracts. It must continue to route all human and AI choices through issued
legal action IDs, keep official card text inert until explicitly bound to
reviewed behavior, and preserve deterministic replay compatibility.
