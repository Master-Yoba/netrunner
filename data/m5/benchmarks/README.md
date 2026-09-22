# M5 benchmark inputs

`contract-baseline-v1.json` is executable by the M5.0 capture utility. It pins
rules/state schema, normalized metadata checksum, behavior/runtime fingerprints,
embedded preset checksum and contract-agent/policy versions. Its 20 games are
five seeds across four Corp/Runner basic/expanded pairings, with two independent
executions per game and a 4,000-action bound. There is no policy RNG in v6.

`tactical-v1.json` specifies 80 independent future tactical positions: 40 per
side, eight categories per side, 24 tuning and 16 held-out positions per side.
Each includes permitted decision families, relevant design IDs, preconditions,
an acceptance predicate and criticality. It contains no official rules text.
These positions are **not executable fixtures yet** and have no measured score.
M5.2 supplies real Core scenario builders, exact resource counts/decision bounds,
and semantic action assertions. Validate reachability before freezing inputs;
do not invent a Gateway prevention card to exercise a generic engine feature.

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
timeout. Full M5.2 stall classification, arbitrary-agent batch scheduling,
tactical scoring, checkpoint diagnostics and the 1,000-game gate do not exist
yet. Do not present this 20-game capture as that gate.

The captured baseline is historical evidence: preserve it when introducing the
new heuristic. Revise manifests/report versions explicitly when changing pinned
content, policies, seeds or exact tactical positions; do not overwrite expected
results until a desired score appears.
