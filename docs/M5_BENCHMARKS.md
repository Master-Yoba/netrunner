# M5 benchmarks

M5.2 is complete. Batch execution and all 80 executable tactical positions are implemented,
with 40 positions per side and frozen roots, resource bounds and policy seeds.
The checked-in manifests currently run the unchanged v6 contract agent. They
do not establish Standard/Easy strength or completion of the M5 milestone.

## Run offline

After bootstrap/restore, with official metadata already provisioned locally:

```sh
make ai-benchmark-smoke
make ai-benchmark BENCHMARK_ARGS='--workers 4 --output artifacts/m5/full-check'
make ai-benchmark-developer
make ai-tactics-verify
make ai-tactics
```

The first two targets build with official content, then execute offline. The
developer target uses the reduced synthetic fixture and is not official Gateway
evidence. No command downloads card metadata or artwork. Output directories
must be new; existing evidence is never overwritten.

The CLI accepts a fully pinned manifest:

```sh
dotnet src/Netrunner.Cli/bin/Debug/net8.0/Netrunner.Cli.dll ai-benchmark \
  data/m5/benchmarks/contract-smoke-v1.json \
  --output artifacts/m5/smoke-check --workers 4 --json
```

`--json` writes one versioned report to stdout. Exit 0 means every game reached
a terminal outcome with verified replay. Exit 1 means execution produced a cap,
cancellation, stall or failure. Exit 2 means invalid arguments, incompatible
pins, unavailable policy/content or an I/O/command failure. A turn cap remains
nonzero until reviewed; it is not silently presented as a successful game.

`ai-benchmark init smoke|full|developer --output <new manifest> [--json]` captures
the installed catalogs and explicit deck hashes using the fixed seed schedule.
Manifest regeneration is an intentional content/configuration change. Never
regenerate held-out seeds to remove an unfavorable result.

`ai-tactics init --output <new manifest>` captures all 80 tactical roots.
`ai-tactics <manifest> --output <new directory> [--workers 1..64] [--json]`
measures the declared policies. Its exit 0 requires 85% correctness per tested
side, all critical cases passing, and no execution/replay failures; exit 1
retains a below-target report. Exit 2 rejects incompatible inputs or command
errors. `--verify-fixtures` executes harness oracles and explicitly labels the
report `fixture-validation`; it requires all positions to pass and makes no
claim about policy strength. `make ai-tactics` currently measures contract v6
and is expected to exit 1. `make ai-tactics-verify` is the passing harness gate.

## Inputs and comparisons

The official smoke has four preset pairings times five historical seeds. The
full schedule has 250 fixed held-out seeds for each of those four pairings,
1,000 rows total. Each row pins both seat policies and domain-separated policy
seeds, the game seed, preset IDs and deck hashes. Manifest-level pins bind rules,
state schema, metadata, behavior, runtime, descriptors and preset definitions.
Unknown policy versions are rejected before execution, with no fallback bot.

`BenchmarkSuites.Compare` creates paired rows with the candidate in each seat.
It retains game/policy seeds, decks and split; callers supply exact candidate
and baseline configurations. The future policy slices will add their own
manifests against contract v6 and any supported frozen previous policy.
`FullMatchRunner` and its existing exact-outcome fixtures retain their API.

## Reports and artifacts

Every declared row stays in the denominator, including canceled rows. Games own
independent state, policy instances and artifacts. The harness rejects factories
that reuse an agent across seats or games. Reports sort rows by fixture ID.
Build provenance includes executable assembly hashes as well as version/revision.
The deterministic digest covers row inputs and results, excluding latency,
worker count, build/OS provenance, paths and exception wording. Running with a
different worker count must preserve that digest and all replay hashes.

`report.json` includes per-seat metrics and totals grouped by comparison group
and preset pairing. Counts include decisions/action kinds, gross credit changes,
installs/replacements, runs/successes, pumps, final credits and agenda points.
"Wasted pumps" counts pumps after the last break with that breaker when the
encounter ends; it is a diagnostic approximation, not a proof of optimal cost.
An encounter still open at a cap has not yet contributed to that count.
Contract v6 has no fallback diagnostic, so fallback frequency is `null`, not zero.
Timing provides decision median, p95 and maximum separately from deterministic
results. Divide comparison wins by all attempts, retaining failures/caps rather
than removing them from the comparison.

Every game writes an accepted-action replay. Nonterminal/failing games also
write a snapshot, exact configuration, recent attempted decisions and progress
diagnostics. `executionTermination` preserves the execution classification even
if replay verification subsequently fails. Replays are verified for terminal,
capped and failed prefixes; invalid initial scenarios cannot claim a verified
replay. No failed attempt is retried or dropped automatically.

Artifacts contain hidden cards and private policy state. Keep them in ignored
`artifacts/` storage. There is no automatic upload of private replays/failures or
opponent access to harness snapshots. The progress fingerprint is harness-only
inspection. CI runs the corresponding 20-game smoke in each content lane; a
scheduled/manual 90-minute job runs the 1,000-game contract suite. It uploads
only the allowlisted summary produced by `scripts/summarize-ai-benchmark.py`.

## Bounds and failure classification

Default limits are 100 complete turn pairs, 20,000 accepted actions, eight
repetitions in a 256-observation progress window, and separate 180-second game
and replay watchdogs. These are explicit manifest inputs. Turn cap, action cap,
repeated progress, missing decision, illegal action, invariant failure,
resolution failure, agent exception, replay mismatch, timeout and cancellation
are distinct outcomes.

The progress detector retains resources, cards, zones, turn/run/resolution facts
and semantic choices while ignoring allocation, event and decision-ID churn.
It cannot feed authoritative information back into the agent. A real unlimited
no-op paid ability fixture proves that newly allocated frames/actions do not
hide a cycle.

Agent creation, choice and lifecycle callbacks are bounded by the game watchdog.
Timed-out instances are retired and never applied or reused. An in-process
watchdog cannot forcibly stop arbitrary managed code that ignores cancellation;
it isolates its late result. CI also needs an outer job/process timeout.

The final Standard 1,000-game completion threshold, per-side tactical targets,
Easy suite and save/UI gates remain the M5.8 acceptance criteria. These harness
commands alone do not constitute `make m5-gate`.

## Tactical fixture construction

`TacticalBoard` creates explicit test positions using official metadata and
compiled Gateway behavior. Physical card IDs come from Core's allocator; the
builder's descriptive roles never enter policy observations. Core generates
all legal choices. `TacticalExecution` applies only those issued IDs, validates
state and verifies the resulting replay. A prepared position begins a new
policy observation history at cursor zero while retaining actual engine replay
sequences. Its initial state is an explicit scenario root, not a claimed prefix
of a standard deck match.

Fixture oracles are harness scripts that prove the intended outcome is possible.
Contrasting legal choices prove the predicates can fail. A compulsory agenda
access continuation has no legal alternative; its negative control withholds
the required action instead. Neither counts as a
policy tactical score. Multi-step installs, advancement/scoring, target
selection, private searches and simultaneous triggers assert their completed
outcome. Hidden-state twins compare normalized observations, candidate scores
when supplied, issued choices and committed policy checkpoints, retaining both
different authoritative replays. They never give either state to the policy.

Two M5.0 intentions were refined before policy tuning: `corp-information-03`
uses Longevity Serum's real Corp Archives-to-R&D shuffle instead of inventing a
Runner heap-to-stack ability; `corp-information-05` uses Brân's optional inward
install when removing HQ's non-agenda decoy would make the current winning
access certain and Corp cannot rez the installed ICE. The intent, case IDs and
tuning/held-out classification remain explicit in the specification.

Two Runner intentions were also corrected against actual Gateway behavior:
`runner-choices-03` uses Carnivore's issued target/additional-cost bundle and
subsequent access continuation, because the generic mode/target/payment lab
example is not a Gateway card; Karuna's `runner-defense-05` exit is free.
`runner-payments-03` pins Marjanah's existing encounter boosts as well as its
successful-run discount. These refinements precede the frozen executable
manifest and policy tuning.

Tactical reports retain per-side, category and tuning/held-out scores, critical
counts and execution failures. Every row retains a replay; hidden-state twins
retain both. Failures include the final snapshot, recent accepted choices,
available candidate evaluations and committed checkpoint. No factory may
reuse an agent across cases or twins. Agent creation, execution and replay
verification have separate 30-second watchdogs. Cancellation retains every row.
The report's deterministic digest excludes build/worker provenance but includes
the ordered tactical results. CI verifies all 80 fixtures in the official lane;
synthetic tests explicitly skip official tactical evidence.

Initial contract v6 measurement: Corp 22/40 (10/11 critical), Runner 21/40
(10/17 critical). All 80 replay prefixes verify. Each side has one incomplete
chain; these remain failures in the denominator. This is a diagnostic baseline,
not a passing Standard policy. Fixture validation passes 80/80 and produces
identical replay hashes at one and four workers (digest
`778295a750f5f403b030b0b10d33ba0e202b3c27f0006e9105c995fbe6165f76`).

## Corp policy candidates

`ai-benchmark init smoke|full --corp-profile standard|easy --output <new.json>`
keeps the baseline game seeds, decks and Runner contract policy, replacing only
the Corp with its complete offline configuration. `ai-tactics init
--corp-profile standard|easy --output <new.json>` retains all 40 Corp positions,
including frozen tuning/held-out membership, root/twin hashes and policy seeds.
Run either manifest with the existing command and a new output directory.
Initialization and execution need preprovisioned official content and never
fetch it. An unsupported profile is an error, not a baseline substitution.

The initial Corp policy is `corp-heuristic-v1`; profiles share its typed utility
families. Standard uses 20,000 evaluation work units and exact ties; Easy uses
12,000 units, a 20-point near-tie range, 90% credit and 80% protection weighting.
Both retain exact scoring and survival priorities. These are strategic
estimates rather than a second rules engine. The policy only submits issued
IDs and recomputes after every accepted action. It never receives a deck list,
full state, engine RNG, UI labels, or prompt prose.

Benchmark fallback counts are nullable for agents without diagnostics. For an
offline agent they count decisions whose selected utility explicitly used a
conservative fallback; an unselected fallback candidate does not increment the
count. Approximate economic or defensive valuation alone is not a fallback.
Malformed/unsupported action contracts are checked before the work budget, so
exhaustion cannot hide a broken contract.
