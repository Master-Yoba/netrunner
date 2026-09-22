# M5 implementation progress

Plan: [M5 implementation plan](M5_PLAN.md).
Branch: `feat/m5-0-baseline`. Record opened 22 September 2026.

## Scope authorization and acceptance reconciliation

The user requested the detailed M5 plan and then explicitly directed work to
begin with slice 5.0. That authorizes the baseline, inventories, case specification
and evidence in this slice. It does not authorize silently expanding into M5.1–8.
Work remains locally committed; no push or PR is part of this session.

Historical M4 acceptance records need the following distinction:

| Item | Evidence available in this session | Recorded status |
|---|---|---|
| Board space/layout follow-up | User said “it's good” after the compact-board work, then requested three concrete follow-ups | Positive feedback on the board changes; not a blanket completion of every older checklist |
| Hand-condition/cost cue, Run popup and opponent playback | Implemented in `8f27bac`; unit, native/headless UI and full repository gate passed in that change | Implemented and automatically verified; no separate user acceptance of each follow-up recorded |
| M4.9 full manual usability matrix | Old progress doc still requires completed human games in both seats and recorded criteria; later feedback does not enumerate those games/criteria | Formal manual record remains outstanding; do not claim it passed |
| M4.10 editor persistence exercise | No explicit report of independently editing, saving, reopening and verifying glow settings | Formal manual record remains outstanding |
| M5.0 baseline/specification work | Explicit user request to begin this slice | Authorized; the outstanding manual records do not prevent this baseline work |

No rules/content versions are being updated. The manual gaps are recorded rather
than replaced by scripted games. Resolve them explicitly as implementation
progresses and retain M5's own manual acceptance gate. This record supersedes
any reading of the older progress prose that all subsequent work must remain
blocked despite the user's explicit slice-5.0 instruction.

## Slice M5.0 — baseline and cases

Status: complete, 22 September 2026. Main checkpoint: `9045fc0`.

Delivered:

- [Pinned 20-game manifest](../data/m5/benchmarks/contract-baseline-v1.json), all
  four basic/expanded deck pairings and five seeds, each run twice.
- [Measured baseline and named findings](M5_BASELINE.md), with the
  [per-attempt JSON evidence](evidence/m5-0/contract-baseline-v1.json).
- [Capability inventory](M5_CAPABILITY_INVENTORY.md): all 77 designs, all 15
  decision kinds, current observation/metadata seams and M5 ownership of gaps.
- [80 independent tactical case specifications](../data/m5/benchmarks/tactical-v1.json),
  40 per side; 24 tuning / 16 held-out per side; criticality, preconditions and
  semantic acceptance recorded. These are not executable or scored yet.
- [Reproducible capture utility](../tools/Netrunner.M5Baseline/Program.cs) and
  [launcher](../scripts/capture-m5-baseline.sh), reusing `FullMatchRunner` and
  the unchanged contract policy. Full private replay files stay ignored.
- `make m5-spec` plus the normal verification integration, checking case IDs,
  deck/seed matrix, report binding, all card IDs and complete decision inventory
  without requiring official metadata.

Measured results: 20/20 terminal games; all 40 replay verifications pass; all
20 duplicate runs match complete replay hashes and counters. Corp won 6 games,
Runner 14. No cap, exception or illegal-choice error occurred in this sample.
The full M5 1,000-game gate and 85% tactical targets have not been run.

Verification log:

- Official-content capture built successfully, with no production code changes.
- Final capture agrees with the exploratory capture's complete replay hashes.
- `make m5-spec` and shell syntax checks pass.
- Specification negative checks pass: missing game, unknown card, false tactical
  execution claim, stale report binding, missing repeat and unequal replay hashes
  are rejected.
- Capture negative checks pass: synthetic content, wrong catalog pin, duplicate
  IDs, empty suite and existing output are rejected. A deliberately capped run
  returns failure, preserves both snapshots, and verifies both repeated prefix
  replays.
- Full provisioned `make m4-gate` passes: all 1,559 solution tests, additional
  official-content checks, formatting, replay/rules proofs, Godot UI and complete
  games in both seats/preset families, secret scanning and 77/77 coverage.
- Documentation links and staged whitespace checks pass. No production engine,
  card, agent or Godot behavior changed in this slice.

The small capture utility is deliberately not the M5.2 generic benchmark
command. It has no policy evaluator, parallel scheduler, cycle detector, new
agent checkpoint or product-save implementation. This keeps the baseline frozen
and leaves the planned dependencies intact.

## Next slice

M5.1 starts from the typed observation/capability inventory. Its first concrete
tasks are the AI observation reader and feature DTOs, the smallest needed static
card descriptor contract, and versioned configuration/checkpoint lifecycle.
Preserve v6 baseline behavior and existing proofs; introduce the heuristic as a
separate agent. Do not tune weights or mark tactical cases passed until real
Core fixture builders and assertions exist.
