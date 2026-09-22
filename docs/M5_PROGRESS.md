# M5 implementation progress

Plan: [M5 implementation plan](M5_PLAN.md).
Branches: `feat/m5-0-baseline`, `feat/m5-1-agent-foundation`.
Record opened 22 September 2026.

## Scope authorization and acceptance reconciliation

The user requested the detailed M5 plan and then explicitly directed work to
begin with slice 5.0. That authorizes the baseline, inventories, case specification
and evidence in this slice. It does not authorize silently expanding into M5.1–8.
Work remains locally committed; no push or PR is part of this session.

The subsequent implementation request explicitly authorizes M5.1 followed by
M5.2. Those slices preserve the frozen M5.0 evidence and continue on separate
feature branches, with local commits only.

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

## Slice M5.1 — agent foundation

Status: complete, 22 September 2026.

Delivered typed immutable observations, a public definition descriptor adapter,
versioned policy configuration/checkpoints, a deterministic transactional policy
driver and optional lifecycle integration in the match runner/coordinator.
The narrowly scoped private-search projection now exposes eligible definitions
to the search owner without deck order. See
[architecture and host protocol](M5_AGENT_FOUNDATION.md).

Verification:

- Final solution tests: 1,589 pass (Core 816, Cards 328, AI 79, CLI 104,
  Match 262). Real Core-issued private-search/access twins cover both seats.
- Retry, rejected/stale commit, cancellation, observation cursor, objective
  invalidation and serialized checkpoint tests pass. The final post-commit
  observation guard was additionally checked with the complete AI test project.
- Six lifecycle integration tests pass. A complete stateful-driver game with
  checkpoint restoration and discarded evaluation before every choice matches
  uninterrupted replay and both final policy checkpoints exactly.
- Full provisioned `make m4-gate` passes: official descriptor metadata,
  replay/rules proofs, 77/77 card coverage, Godot UI/artwork/editor/interaction
  checks, completed/reloaded games in both seats and preset families, and secret
  scanning. Three earlier owner-search assertions were updated to check the
  new private field while retaining public/opponent non-disclosure assertions.
- `dotnet format Netrunner.sln --no-restore --verify-no-changes` and whitespace
  checks pass. The existing contract policy, authoritative state schema and
  gameplay rules remain unchanged.

The final audit confirms the M5.1 observation, descriptor, information-boundary,
deterministic selection and lifecycle requirements. M5.3–4 policy strength,
M5.5 durable saves/trace checkpoints and M5.6 player-facing opponent selection
are explicitly outside this completed foundation slice.

## Slice M5.2 — benchmark harness

Status: complete, 22 September 2026.

The generic batch runner and CLI now retain every replay and failed prefix,
classify termination/failure causes, enforce independent agent instances and
produce deterministic sorted reports with separate timing and comparison/preset
totals. Frozen contract manifests cover the official 20-game smoke and balanced
1,000-game schedule; a separate 20-game developer suite remains synthetic.
See [commands, report semantics and limitations](M5_BENCHMARKS.md).

Initial verification: all 281 Match and 112 CLI tests pass, including 19 Match
benchmark tests and eight CLI tests. The official 20-game smoke completes with
20 terminal games and verified replays at both one and four workers; all ordered
results/replay hashes match (digest
`ef38f82bf5021e0e7e059b9086f8ad4ad6a18a37c6696a05414105b8c6b53e77`).
They cover worker-count equivalence to the legacy runner, illegal IDs, agent and
lifecycle exceptions/hangs, distinct caps, canceled-row retention, shared-agent
rejection, no-decision/invalid-root/replay classification and a real Core-issued
unlimited paid-ability cycle.

All 80 tactical builders are implemented (40 per side, 24 tuning/16 held-out),
with frozen initial/twin hashes, policy seeds, resource bounds and assertions.
`ai-tactics` measures declared policies; `--verify-fixtures` separately validates
oracles. Both serial and parallel fixture validation pass all 80, and contract
v6 scores 22/40 Corp and 21/40 Runner with all replay prefixes verified. These
baseline policy failures are retained, including one incomplete chain per side.
Reports include category/split/critical scores, twin replay verification and
private failure snapshots/checkpoints. Shareable summaries are checked in under
`docs/evidence/m5-2/`; hidden snapshots/replays remain local.

The fixtures exposed a real Mutual Favor install-option bug: cards in temporary
search custody were excluded before installation. Eligibility now probes the
same restoration used by the reducer on an isolated copy, and snapshot
validation agrees. Card-level regressions cover both empty and nonempty remaining
Stacks. Official testing also corrected older assertions about hidden Archives,
private search transfers and terminal operation-trash turn facts.

Final validation passes: all 1,794 official-content solution tests, including
172 tactical checks; full `make m4-gate`, formatting, specification validation
and secret scanning. The official smoke again completes 20/20 with
verified replays at one and four workers and retains the original digest above.
The gate now explicitly executes official tactical, CLI and Mutual Favor tests;
synthetic CI continues to label official tactical checks as skipped.

## M5.3 — Corp policy (in progress)

The observation-only Corp evaluator covers economy, server development,
replacement loss, scoring, rez reserves, damage/tag pressure, purge and nested
choices. Easy and Standard configurations persist exact integer weights, work
limits and policy seeds. Both use the same information boundary. Public typed
context for offered triggers is projected only to their owner; feature schema
`agent-features-v2` records this additive change. Unsupported action shapes fail
even when the evaluation budget is exhausted. Selected conservative fallbacks
are measured separately from approximate strategic scores.

Before held-out evaluation, Standard passes 24/24 frozen tuning positions.
The initial complete-game smoke finished 20/20 with verified replays (19 Corp
wins); it predates the final tuning adjustment and is exploratory evidence.
Final held-out, reproducibility and gate results are pending. M5.4 follows only
when this slice meets its exit criteria. Keep separate branches and local commits.
