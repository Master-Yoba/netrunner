# M4 progress — System Gateway complete

Status: slice 4.1 in progress

Date started: 31 August 2026

Rules/data baseline: Null Signal Comprehensive Rules 26.03 / System Gateway
vendored commit `236140cf84491c23995b028bc791281837520b97` (77 designs)

## Scope

Current implementation scope is limited to Slice 4.1 of `docs/M4_PLAN.md`:
architecture/version preparation, behavior-neutral engine organization, the
Gateway primitive audit, CARD-401/COV-401, the explicit gitignored-metadata
content build, catalog-driven presentation, and the first Theme/region split.
No Slice 4.2 rules or card behavior is in scope yet.

## Baseline

- M3 completion evidence records 356 tests green, deterministic M1/M2 proofs,
  M3 smoke and latency smoke green.
- Seven Gateway designs are registered in `gateway-m2-3`; Palisade,
  Whitespace, Cleaver, Buzzsaw, and Overclock are full-text complete, while
  Nico Campaign and Offworld Office are partial.
- The local authenticated 77-card source snapshot is present but gitignored.
- `docs/M4_PLAN.md` is the architect-reviewed execution contract.

## Slice 4.1 work log

### 2026-08-31 — started

- Started three bounded Luna Max implementation workstreams:
  architecture/version/catalog foundation; CARD-401 plus primitive audit; and
  metadata-build plus UI foundation.
- The primary agent owns integration, architectural review, full verification,
  and this progress record.
- A direct `dotnet test` before loading `scripts/env.sh` could not find the SDK;
  all actual verification commands will use the repository environment script.

### 2026-08-31 — implementation streams landed; architect review in progress

- ARCH-401/VER-401 landed an immutable, injected behavior catalog and engine
  session boundary, distinct metadata/behavior pins, state schema 2, and M2
  hash migration coverage. The architect review rejected empty ENG-400 marker
  partials as insufficient and returned that stream for a bounded mechanical
  extraction plus cross-family catalog conflict validation.
- PRIME-AUDIT/CARD-401/COV-401 landed the 77-code registry and rulings seed,
  metadata-only test discovery, deterministic checked-in reports, and the
  primitive audit. The reviewed report is currently 7 registered and tested,
  5 full-text complete, 2 partial, and 70 unregistered, with zero diagnostics.
- DATA-400/UI-401/UI-402 landed the generated Cards resource, injected metadata
  resolver, allow-listed formatter, provisioned gallery, Theme foundation,
  and first focus-region extraction. The architect review returned the stream
  to make content selection explicitly opt-in and to complete honest,
  testable region seams beyond the focus partial.
- First combined gate: build succeeded with zero warnings/errors and all 385
  tests passed (Core 259, Cards 54, AI 9, CLI 47, Match 16). The gate then
  stopped on four import/format diagnostics; these were mechanically fixed.
  Proof and Godot evidence remain pending the post-review rerun.

## Architecture record

The Slice 4.1 architecture decisions are D-401 through D-417 in
`docs/M4_PLAN.md`. Implementation evidence and any deviations will be added as
each workstream is reviewed; a workstream report alone is not completion.
Architect review is currently enforcing explicit content-build selection,
real mechanical region and reducer seams, catalog conflict validation, and
schema-version consistency.

## Evidence

- Combined pre-review test evidence: 385/385 passed.
- Coverage report: 77 total / 7 registered and tested / 5 complete / 2 partial
  / 70 unregistered; zero diagnostics.
- Final format, proof, content-gallery, and M3 smoke evidence is pending.

## Deviations and blockers

None currently. The official metadata source remains gitignored by design; the
content-enabled client build will consume a validated generated resource rather
than requiring runtime network access.

## Slice 4.1 exit checklist

- [ ] ARCH-401 / ENG-400 / VER-401 implemented and reviewed.
- [ ] PRIME-AUDIT complete in `docs/M4_PRIMITIVES.md`.
- [ ] CARD-401 / COV-401 generated report distinguishes registered and complete.
- [ ] DATA-400 content-enabled build is deterministic and offline after build.
- [ ] UI-401 renders all 77 provisioned cards through an injected resolver.
- [ ] UI-402 Theme/region extraction preserves M3 smoke and latency behavior.
- [ ] Full `scripts/verify.sh` green with final test/smoke evidence recorded.
