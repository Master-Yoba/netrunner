# M4 completion gate

Run the provisioned gate from the repository root:

```bash
./scripts/bootstrap.sh
./scripts/update-card-data.sh --commit 236140cf84491c23995b028bc791281837520b97 --retrieval-date 2026-08-28
make m4-gate
```

`make m4-gate` fails before building if the authenticated source snapshot is
missing. The build validates the snapshot and embeds runtime metadata; the
client and CLI subsequently load their assembly resources without source-file
or network access. Source card data remains gitignored.

The gate includes the complete repository regression, formatting verification,
M1 win proofs, pinned M2 replay proof, card-data diagnostics, the 77-card gallery,
all earlier client smokes, and the responsiveness check. It regenerates coverage
and requires exactly 77 registered, tested and full-text-complete designs, zero
partial designs and zero diagnostics. See [coverage](generated/gateway-coverage.md)
and [preset provenance](M4_PRESET_REVALIDATION.md).

The final integration matrix is:

| Proof | Content | Required evidence |
|---|---|---|
| CLI official basic and expanded | Authenticated embedded metadata | Deterministic seed, real setup, every-yield save/load and verified replay to an outcome |
| `M4_FULL_SMOKE`, human Corp, basic and expanded | Official preset pair | Scripted human submissions through client controls against Runner contract agent |
| `M4_FULL_SMOKE`, human Runner, basic and expanded | Official preset pair | Scripted human submissions through client controls against Corp contract agent |
| Client reload | Each official full smoke | Terminal presentation, activate New match, fresh selectable setup |
| `M4_SLICE48_UX` | Real Gateway metadata on provisioned lane | Setup, five-card hand, dense board/rig, choices, discard, run and outcome at 1920×1080 and 2560×1440; keyboard, reduced motion, state cues, scrolling and readable text |

The scripted human chooses only issued actions. Core owns authoritative state;
Match publishes filtered presentation, accepted-action count and public setup
ownership. The client gate uses seed 47042, an accepted-action cap and bounded
waits. A failed simulation preserves a diagnostic snapshot and replay; CLI
`--replay-out` writes that replay even when a cap is reached. Client failures
report filtered presentation diagnostics.

`make verify` supports a clean synthetic CI checkout. It runs the non-content
checks and explicitly reports that the official M4 gate was not run. The CI
matrix runs `make verify` for synthetic content and `make m4-gate` for its
provisioned official lane. A synthetic pass alone is not M4 completion.

The final accepted results, native screenshots and any deviations are recorded
in [M4 progress](M4_PROGRESS.md). M5 agent difficulty tuning, user-facing
save/resume, LLM play and release packaging remain outside this gate.
