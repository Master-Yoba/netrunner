# Correctness slice

Baseline: `2e9257b`; branch: `corrective`.

This slice repairs existing execution paths from the repository audit. It is
not a declaration that every audit finding is closed or Slice 4.6 is ready.

- [x] Zone- and viewer-aware deck/access visibility, including regressions.
- [x] Dual-catalog production replay validation and execution.
- [x] Coordinator command completion after worker failure.
- [x] Installed-card rez/advancement/typed-counter cleanup; hidden-zone face normalization.
- [x] Negative maximum-hand-size flatline before discard.
- [x] CI uses the full verification script in synthetic and official-content lanes.
- [x] Bounded responsiveness smoke is part of the verification script.
- [x] Reconcile README and milestone status with actual delivered paths.
- [x] Full local verification.
- [ ] PR into dev.

Existing M4 card batches remain the supported content boundary. This slice
does not implement the cards assigned to 4.6 or 4.7.

## Remaining readiness blockers

- Empty-remote deletion is still limited to scoring. A complete repair must
  cover every checkpoint, protect a server during installation (CR 8.5.9),
  and end an affected run after its open paid window closes (CR 4.6.1e).
  Removing servers directly in `MoveCard` would invalidate active run and
  install-frame references; that shortcut is intentionally not included.
- Setup/mulligan, identity placement, and shared setup-to-win execution
  through Match, CLI, Godot, and simulations remain undelivered. This needs
  a serialized setup contract and explicit developer decks while official
  preset identities still lack complete behavior coverage. The M2 client
  factory and simulation document validation must not be called full matches.

These are still required follow-up work before declaring the audit closed.
Large reducer/validator files remain an architectural maintenance concern;
extract bounded domains alongside those changes rather than rewriting them
as part of unrelated bug fixes.

## Compatibility and verification

No serialized fields or discriminators change. Private `deck`/`stack` keys
remain as empty arrays for presentation compatibility; public counts remain.
Affected production replay hashes can change because corrected state no
longer retains advancement counters after scoring/stealing. Replay verifies
both catalog fingerprints and expected final hashes; it never silently
accepts mismatched history. The legacy toy scoring adapter is unchanged.

Regression coverage includes hidden deck order, Runner-only R&D access,
runtime-catalog mismatch, replay JSON round-trip, worker failure, negative
hand size, and uninstall-to-discard/hand/deck state normalization.
No visible Godot scene changes are included, so screenshots are not applicable.

Local `make verify` passed on 2026-09-05: 1,162 tests (Core 762, Cards 214,
AI 53, CLI 55, Match 78), formatting, M1/M2 deterministic proofs, generated
coverage diffs, data failure contracts, official 77-card gallery, Godot
import and Slice 4.3–4.5 smokes, M3 responsiveness and full-run smokes, and
secret scanning. The new Ubuntu CI matrix still requires a remote run.
