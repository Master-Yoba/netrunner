# M5 integration gate

Automated evidence date: 23 September 2026. User acceptance: 25 September 2026.
Branch: `feat/m5-8-final-gate`. The user authorized GitHub publication.
Implementation and automated verification are complete: headless, repository,
offline-package and performance gates pass. General human acceptance is recorded below.
The [completion audit](M5_COMPLETION_AUDIT.md) maps each planned deliverable to
its implementation and evidence, and records the remaining human criteria.

## Frozen inputs and reproducibility

`make m5-quick` runs the 20-game Standard smoke and both 40-position tactical
suites, each with four workers and one worker. `make m5-gate` additionally runs
1,000 Standard games, 100 Easy games, two 40-game paired comparisons, and both
Easy tactical suites. Official metadata must already be provisioned. The gate
does not fetch metadata or artwork.

The [freeze](../data/m5/benchmarks/frozen-profiles-v1.json) binds the existing
Corp v2 and Runner v1 Standard/Easy profiles and the earlier held-out seed
corpus. Each full self-play suite balances all four Basic/Expanded deck pairings.
Comparisons use 20 paired seed/deck cases with the candidate in each seat.
The previous-policy reference is `de4e1cf`. M5.8 changes no strategic weights or
policy versions, so the previous-policy comparison is a regression reference,
not evidence of a new strength improvement. All policies use the repaired rules.

The launcher copies compiled binaries and manifests into a new private evidence
directory before execution. It rejects reused output directories. Validation
checks the exact row denominator, seed/deck/profile bindings, actual outcomes,
replays, aggregate counts, critical tactics, per-seat scores and recomputed
deterministic digests. Repeats must match both build and results with different
worker counts. Fixture-oracle execution cannot count as policy evaluation.
Any cap requires explicit review; the shipped gate currently accepts zero caps.
Failed-attempt summaries are retained alongside passing reports.

Eight validation tests cover accepted reports, stale/missing/duplicate rows, hidden failures,
false terminal claims, replay failure, caps, changed worker results and oracle
misclassification. A fixed Core serialization vector covers HTML/Unicode
escaping in the cross-language digest checker.

## Benchmarks and tactics

Standard completes 1,000/1,000 actual terminal games with zero caps or failures,
all accepted-action replays verified, and 250 games in each preset pairing.
Corp wins 743 and Runner wins 257. The completed quick portion has 20/20 terminal
games at each worker count and the following Standard tactical results.
Every tactical replay and declared
hidden-state twin verifies, with identical results across worker counts.

| Standard Corp / Runner presets | Games | Corp wins | Runner wins | Mean complete turn pairs |
|---|---:|---:|---:|---:|
| Basic / Basic | 250 | 183 | 67 | 13.012 |
| Basic / Expanded | 250 | 175 | 75 | 13.412 |
| Expanded / Basic | 250 | 184 | 66 | 14.116 |
| Expanded / Expanded | 250 | 201 | 49 | 14.304 |

The separate Easy suite completes 100/100 terminal games (70 Corp wins,
30 Runner wins), with 25 per preset pairing, zero caps/failures and all replays
verified. These self-play win counts do not estimate strength against people or
establish that Easy feels easier. Standard records zero fallback decisions and
zero wasted pumps across its 1,000 games.

| Seat | All | Critical | Tuning | Held-out |
|---|---:|---:|---:|---:|
| Corp | 39/40 (97.5%) | 11/11 | 24/24 | 15/16 |
| Runner | 38/40 (95%) | 17/17 | 24/24 | 14/16 |

Corp categories are 5/5 for choices, information, opening, pressure, replacement,
rez and scoring; development is 4/5. Runner categories are 5/5 for choices,
defense, encounter, opening, payments and targets; access and rig are each 4/5.
The noncritical misses remain `corp-development-04`, `runner-access-04` and
`runner-rig-04`. Their denominators and classifications have not changed.
Earlier correctness repairs used held-out feedback in M5.3/5.4; these retained
cases are regression measurements, not a newly unseen evaluation population.

Easy scores 38/40 in each seat, with Corp 11/11 and Runner 17/17 critical.
Corp scores 23/24 tuning and 15/16 held-out; Runner scores 24/24 and 14/16.
Every Easy tactical position and declared hidden twin completes and replays.
The complete category and position denominators are retained in the
[Corp](evidence/m5-8/corp-easy-tactics-summary.json) and
[Runner](evidence/m5-8/runner-easy-tactics-summary.json) summaries.

Both paired comparison suites complete 40/40 terminal games with verified
replays and no caps or failures. Each cell below is five paired seed/deck cases;
candidate wins use that seat's perspective.

| Corp / Runner presets | Candidate Corp vs contract | Candidate Runner vs contract | Candidate Corp vs previous | Candidate Runner vs previous |
|---|---:|---:|---:|---:|
| Basic / Basic | 4/5 | 4/5 | 2/5 | 3/5 |
| Basic / Expanded | 5/5 | 4/5 | 5/5 | 0/5 |
| Expanded / Basic | 4/5 | 5/5 | 4/5 | 1/5 |
| Expanded / Expanded | 5/5 | 4/5 | 3/5 | 2/5 |
| Total | 18/20 | 17/20 | 14/20 | 6/20 |

The previous-profile comparison's 40 replay hashes match the corresponding
unchanged-profile smoke games exactly. This is expected with no policy tuning.
The [contract](evidence/m5-8/compare-contract-summary.json) and
[previous](evidence/m5-8/compare-previous-summary.json) summaries retain per-seat
and per-preset game lengths, scores, economy, installs/replacements, runs,
successful runs, pumps and fallback counts. [Comparison timings](evidence/m5-8/comparison-timing.json)
retain explicit per-game percentile summaries; they include benchmark scheduling
and diagnostic overhead and are separate from the direct-policy budget below.

All 12 headless cases pass. The [gate summary](evidence/m5-8/gate-summary.json)
binds every result to exact manifest, compiled-assembly and report hashes, plus
worker counts and deterministic digests. The full Standard manifest hash is
`108a14a94f2bc0ed80c6f377012f75cf65a98641726e25a429cd97ee48f25cd8`, its report
SHA-256 is `a42f53544415825c19d086aa8f2c6d77e1157d91c36a8709a34cd7c1d4c007da`,
and its deterministic digest is
`202291b968fca327595919f7d8a6bff74812f0e994471aeef10c1aa783c43bd2`.

## Preserved failures and repairs

The [exploratory Standard preflight](evidence/m5-8/failed-preflight-summary.json)
completed 999/1,000 games, with zero caps and one resolution failure. This is a
failed attempt, not a passing milestone result. Its report SHA-256 is
`3c1b842b0b3a98e1e9be8cc5221665d87142d51e1f19a411686f2dfd5980562d`.

`expanded-expanded-0020`, seed `6989852`, failed at accepted action 617 when
Retribution trashed DZMZ Optimizer and reduced available memory below installed
usage. Core had no mandatory cleanup boundary before resuming card behavior.
Commit `90457ea` opens the existing Runner memory cleanup through an empty
effect frame, then resumes the interrupted Corp behavior. It only applies when
a card actually leaves the Runner rig. The invariant validator accepts the
specific controller/frame relationship and rejects a forged controller.

Minimal regressions cover DZMZ Optimizer and Pantograph, serialization and full
replay. The preserved real position now accepts the exact failing action and
the cleanup choice at boundaries 618 and 619 with verified replays. In the new
full suite the same seed reaches a terminal outcome after 1,031 actions, with
its full replay verified. All 2,043 official solution tests pass after the repair
and final instrumentation. The complete `make m4-gate` passes, including updated
77/77 generated coverage, existing board/interaction/artwork checks, and four
completed/reloaded official full games. Formatting and secret scanning pass.

The first quick-gate checker also rejected two otherwise complete Corp tactical
reports because Python did not escape `&` like .NET's canonical JSON writer.
The checker was repaired and cross-checked against Core; no manifest, position,
policy or accepted result was changed to address that failure. The final gate
runs into a new evidence directory rather than overwriting the attempt.

## Saves, client and offline package

M5.5 verifies repeated restart with exact engine/policy/replay agreement, nested
and opponent-owned choices, terminal saves, legacy trace recovery, and actual
writer-process termination. M5.6's [five-case client matrix](evidence/m5-6/client-matrix.json)
covers both seats/preset families plus Easy, ten process phases, paused resume,
write failures, corrupt entries, verified backups, stale callbacks, Escape,
ordered playback, terminal reload and New game. M5.7 verifies all 80 developer
snapshot roots and absence of developer controls from exported releases.

The M5.8 package includes the committed generic card backs in both export
presets. Official card faces remain exclusively in the local cache/archive.
The macOS offline proof copies the built release outside the checkout, denies
all network operations and access to the checkout and old application profiles,
then exercises save/restart/outcome in four seat/preset/artwork combinations.
Pack, unpack and validation of all 77 cached images also run with networking
denied. Resume copies saves to a different folder and denies access to the
previous process's saves and diagnostics. A loopback socket probe verifies the
network denial. No existing diagnostic history is available to the new process.

Reproduce after local provisioning and an official-content release build:

```sh
source scripts/env.sh
NetrunnerContentEnabled=true bash scripts/package-macos.sh
make m5-offline-proof ART_CACHE="/path/to/existing/card-art"
# The previous command prints a private directory outside the checkout:
python3 scripts/m5-offline-proof.py --render-proof /path/to/completed-proof
```

The renderer supplement captures actual 1080p missing-art and 1440p restored-art
windows from the same verified bundle. All eight restart phases and eight actual
renderer captures pass; logs contain no errors or warnings. The
[offline summary](evidence/m5-8/offline-summary.json) records bundle/archive/log
hashes and exact moved-save engine/policy/integrity hashes. The
[1080p no-art capture](evidence/m5-8/offline-no-art-1920.png) shows the packaged
backs and paused continuation. Screenshots with official faces remain outside
the repository. This is an offline runtime proof, not offline toolchain bootstrap
or human playtesting.

| Artwork | Human seat / presets | Saved action | Terminal action |
|---|---|---:|---:|
| Missing | Corp / Basic | 53 | 129 |
| Missing | Runner / Expanded | 32 | 1,760 |
| Restored | Corp / Expanded | 15 | 1,347 |
| Restored | Runner / Basic | 32 | 659 |

Every case uses seed `56142` and Standard. All restart points are nonterminal.
The release bundle has embedded official metadata, but no official card faces;
it uses neither the source checkout nor the previous profile. Developer controls
remain absent even with `--developer` supplied to every release proof process.

## Performance

`ai-performance` wraps the actual offline policies and runs the frozen smoke
serially, without diagnostic capture, matching the client. Timing covers direct
`ChooseActionAsync`, including feature preparation/scoring, and excludes thread
scheduling, observation construction, rules, storage and rendering. All recorded
samples count; no warm-up samples are removed. Ordinary setup/turn choices have
a 50 ms p95 budget; all run/nested/cleanup choices have a 200 ms budget. Passing
requires both categories in both seats, terminal games and verified replays.

```sh
dotnet /path/to/gate/runner/Netrunner.Cli.dll ai-performance \
  /path/to/gate/manifests/standard-smoke-v1.json \
  --output /new/private/performance-directory \
  --hardware "Apple M5, 24 GiB RAM, macOS 27.0 (26A428)"
```

Run this after concurrent benchmarks and native clients finish. The report
records CPU description, OS/runtime, manifest and assembly hashes, every sample,
and per-seat/category action-set sizes and latency percentiles. A regression
test verifies that timing instrumentation preserves actual replay hashes.

The isolated run passes on Apple M5, 24 GiB RAM, macOS 27.0 (26A428), Arm64,
.NET 8.0.30. No other benchmark, build or native-client job ran concurrently.
All 13,828 decisions from the frozen 20-game Standard workload are recorded;
all twenty terminal replay hashes match the normal benchmark exactly.

| Seat / category | Decisions | Legal actions min / p95 / max | Median ms | p95 ms | Max ms | p95 budget ms |
|---|---:|---:|---:|---:|---:|---:|
| Corp ordinary | 5,166 | 1 / 26 / 59 | 0.1784 | 0.7832 | 5.4272 | 50 |
| Corp tactical | 1,170 | 1 / 4 / 16 | 0.1873 | 0.5497 | 4.5993 | 200 |
| Runner ordinary | 5,278 | 1 / 18 / 27 | 0.1428 | 0.4764 | 3.6114 | 50 |
| Runner tactical | 2,214 | 1 / 6 / 16 | 0.1604 | 0.5067 | 2.9629 | 200 |

The [performance summary](evidence/m5-8/performance-summary.json) retains exact
build/manifest/report hashes. These figures cover this frozen workload on the
recorded desktop. The full local report retains every raw sample.

The real Apple M5 renderer is measured separately, after the policy run, using
the exported offline bundle and existing dense-board/history/motion checks.
All four seat/resolution combinations pass. Across 120 idle frames, median
frame time is 2.91 ms and p95 is 3.05 ms, within the 16.67 ms budget for 60 FPS.
Idle allocation remains 410 bytes/frame. Existing cancellation, stale-result
and background-save tests pass; policy and save-file work remain off the Godot
render thread. Log bindings are in the [validation record](evidence/m5-8/validation.json).

## Completion audit: private policy diagnostics

The final requirement audit added explicit opt-in per-decision JSONL to
`ai-benchmark --policy-diagnostics` (`66098d7`), followed by a cancellation
recheck after log writes (`c808438`). See the
[command guide](M5_BENCHMARKS.md#private-policy-decision-logs). This closes §4.5's
diagnostic export requirement without changing strategic weights, gameplay,
save formats or UI behavior.

All **2,050 official solution tests** pass: Core 819, Cards 331, AI 95, CLI 142,
Match 663. The final diagnostic check completes twenty official games with
logging enabled at four workers and again without logging at one worker.
All **13,828 records** match the issued action, replay sequence, configuration
and manifest/build pins; every replay and the deterministic report digest are
identical. Normal execution emits no decision logs, and public reports contain
no candidate scores. [Diagnostic evidence](evidence/m5-8/audit/diagnostics-summary.json)
retains report/build/log hashes without any private candidate records.
`workUsed` measures deterministic budget units, not processor instructions;
69 records legitimately use zero budget units on direct encounter estimates.

The repeated full M5 gate passes all twelve cases, including 1,000/1,000
Standard and 100/100 Easy terminal games. Every case's manifest and deterministic
digest matches the preserved passing baseline. The complete provisioned M4 gate
also passes again, including all four native save/reload games, 77/77 coverage,
existing interactions, formatting and secret scanning. The new
[gate summary](evidence/m5-8/audit/gate-summary.json) and
[validation bindings](evidence/m5-8/audit/validation.json) preserve this attempt
separately. The long gate uses the immutable `66098d7` build with logging off;
the final `c808438` logging/cancellation path is covered by the separate official
logged/unlogged comparison and solution tests. No policy or rules changes
separate those builds.

The original release/offline/renderer measurements above remain tied to their
recorded bundle. This benchmark-only addition does not retroactively change
that provenance or claim a newly exported package.

## Human acceptance

On 25 September 2026, in direct response to the request for M5 playtest results,
the user reported: “it seems to work fine, can push to GH”. This is recorded as
general acceptance of M5 and authorization to publish the completed work.
The [acceptance record](evidence/m5-8/acceptance.json) binds the feedback to the
reviewed implementation at `bc2d596`.

The user did not supply individual seeds, seat/preset results, save boundaries,
or resolution/motion observations. Those details are not inferred from the
general acceptance. The planned manual matrix remains a useful regression
checklist in [the audit](M5_COMPLETION_AUDIT.md#human-acceptance), while the
automated matrices above remain explicitly scripted evidence. The publication
blocker is resolved by the user's acceptance; no additional confirmation is
required. Historical evidence files retain their original acceptance status
as recorded on 23 September.
