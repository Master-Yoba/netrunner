# M5 benchmark inputs

`contract-baseline-v1.json` is executable by the M5.0 capture utility. It pins
rules/state schema, normalized metadata checksum, behavior/runtime fingerprints,
embedded preset checksum and contract-agent/policy versions. Its 20 games are
five seeds across four Corp/Runner basic/expanded pairings, with two independent
executions per game and a 4,000-action bound. There is no policy RNG in v6.

`tactical-v1.json` specifies 80 independent tactical positions: 40 per
side, eight categories per side, 24 tuning and 16 held-out positions per side.
Each includes permitted decision families, relevant design IDs, preconditions,
an acceptance predicate and criticality. It contains no official rules text.
`tactical-contract-v1.json` is the executable M5.2 baseline: Core scenario
builders freeze root/twin hashes and semantic assertions, and contract v6 has
a measured score. See `docs/M5_BENCHMARKS.md` for the evidence contract.

`corp-{standard,easy}-{tactical,smoke}-v2.json` pin the final M5.3 Corp profiles.
`runner-{standard,easy}-{tactical,smoke}-v1.json` pin both M5.4 Runner profiles
against the same 40 Runner tactical roots and 20 smoke seeds. Final M5.4
allowlisted results are in `docs/evidence/m5-4/`. These manifests contain only
pins, seeds, metadata and configurations; private
snapshots and official card content remain under ignored artifacts.

Run the content-independent specification/evidence check:

```sh
make m5-spec
```

Capture the contract baseline with locally provisioned official card data:

```sh
./scripts/capture-m5-baseline.sh --describe
./scripts/capture-m5-baseline.sh \
  data/m5/benchmarks/contract-baseline-v1.json \
  artifacts/m5-0/my-capture
```

The destination must not already exist. Full replays and failed-cap snapshots
contain private engine state and remain in ignored `artifacts/`; only the
aggregate/per-game report is suitable for the committed evidence directory.
The script builds with official content explicitly and never fetches it.
Toolchain/package provisioning is separate from offline game execution.

The utility wraps the unchanged contract agent with counters, calls the existing
`FullMatchRunner`, verifies each replay and compares replay bytes plus counters
between repetitions. It records environment and assembly hashes. It is
deliberately sequential and uses the existing runner's action cap/cooperative
timeout. The newer `ai-benchmark` and `ai-tactics` commands provide batch
scheduling, stall classification and tactical scoring. The 1,000-game M5 gate
has not run; do not present this 20-game capture as that gate.

The captured baseline is historical evidence: preserve it when introducing the
new heuristic. Revise manifests/report versions explicitly when changing pinned
content, policies, seeds or exact tactical positions; do not overwrite expected
results until a desired score appears.
