# Correctness slice

Baseline: `2e9257b`; branch: `corrective`; [PR #10 into dev](https://github.com/Master-Yoba/netrunner/pull/10).

Readiness candidate: implementation and local gate complete; final remote gates pending.
This closes prerequisites for starting Slice 4.6, not the cards or rules assigned
to Slices 4.6 and 4.7. Official presets remain coverage-gated.

## Repairs

- Zone/viewer-aware deck and access visibility; only the Runner sees R&D access.
- Dual-catalog production replay validation and execution.
- Coordinator command completion after worker failure and configured-human ownership.
- Installed-card rez/advancement/typed-counter cleanup and hidden-zone face normalization.
- Negative maximum-hand-size flatline before discard.
- Empty-remote lifecycle at checkpoints, installation protection, and run termination
  after the current paid window closes. Tombstones retain source-zone/ICE provenance
  while effects unwind, but cannot be targeted by new installs or runs. Final cleanup
  removes the server and its zones. Legacy scenario placeholders remain supported;
  setup-created matches reject active empty remotes outside installation.
- Serialized Core setup: deterministic shuffle/deal, public identity placement,
  Corp-then-Runner keep/mulligan, one redraw each, and entry into the formal turn shell.
- Shared validated Match composition for CLI, simulations, and either human client seat.
  All agent choices use observations and engine-issued IDs. Simulations enforce action
  caps, declared outcomes, save/load at every yield, and final replay verification.
- Developer deck code/quantity data in `data/m4/developer/decks.json`; Gateway metadata
  comes from the selected generated payload. The two no-ability identities are explicitly
  synthetic. Standard deck validation cannot accept these reduced decks.
- CLI `full-match [--seed N] [--json] [--replay-out file]` and
  `simulate <versioned-fixture-file> [--json] [--replay-out file]`.
- Godot side selection, own-hand display, identity display, setup controls, and restart.
- Full synthetic/official CI gates; Linux Godot discovery and fail-fast bootstrap;
  environment-discovery regressions; both-seat full-match client smokes.

## Architecture and compatibility

Core remains authoritative. Cards composes metadata/behavior and validates decks;
Match owns full-match composition, asynchronous coordination, and presentation filtering;
Godot consumes Match projections. Setup and server lifecycle reducers/validators are
separate partial files rather than further enlarging the main domain files.
Large legacy reducer/validator files remain a maintenance concern, not a reason to
rewrite unrelated domains before the next slice.

Engine state schema is **15** (previously 14): setup frame/decision, identity zones,
and required server lifecycle state are explicit serialized contracts. Old snapshots
are rejected rather than silently upgraded. Both catalog fingerprints remain enforced.
Developer metadata is content-addressed and requires matching supplied catalogs.

Canonical fixture migration (full SHA-256 values remain pinned in fixtures/tests):

| Fixture | Schema 14 state hash prefix | Schema 15 state hash prefix |
| --- | --- | --- |
| base | `e30e410b8504` | `efb43b283440` |
| nested-inner | `81d2026a8e17` | `1cfe53d67a01` |
| nested-outer | `8a694ebac314` | `e4fb4744e1e2` |
| M2 initial | `308cadf42d6f` | `452467004c66` |
| M2 final | `4603c78adb06` | `ef71cd24f83e` |

M2's 32 accepted actions/checkpoints and event-stream hash are unchanged:
`33af2b049fbad324ed5ff0ee130740c3d65c03f2cb1196100afbdc73e9f0e67c`.
Both representative policies still produce identical final states and event streams.

## Evidence

Focused tests cover all keep/mulligan combinations, seeded setup determinism,
save/load at every yield, replay to deck-out, both human seats, simulation caps,
developer/standard separation, CLI replay, last-card bounce/discard, and self-trash
in initiation/approach/encounter/movement paid windows. Existing install replacement
tests retain the destination when its last previous contents are trashed.

Native client captures: [Corp setup](evidence/correctness/corp-setup.png) and
[Runner setup](evidence/correctness/runner-setup.png). Both native and headless smoke
paths prove setup, mulligan, identity display, a turn pair, and terminal outcome.

Bootstrap-fix CI: [33958195017](https://github.com/Master-Yoba/netrunner/actions/runs/33958195017)
passed both synthetic and official lanes. Local `make verify` passed on 2026-09-05:
1,184 tests (Core 774, Cards 214, AI 53, CLI 59, Match 84), formatting, deterministic
proofs, generated coverage checks, official 77-card gallery, all client smokes,
and secret scanning. Remote verification of the final implementation is pending;
the earlier green run does not certify the later rules/client changes.
