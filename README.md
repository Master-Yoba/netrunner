# Netrunner Solo

An offline-first, desktop solo implementation of current Null Signal Games
Netrunner. The rules engine is deterministic and UI-independent; Godot is a
presentation client, and every human or AI player acts through the same legal
action boundary.

The product definition is in [the PRD](docs/netrunner_solo_prd_v0.2.md). The
dependency-ordered engineering roadmap is in
[the implementation plan](docs/IMPLEMENTATION_PLAN.md).

## Current state

M1–M3 and M4 Slices 4.1–4.5 are merged. Gateway behavior coverage is tracked in
[the generated report](docs/generated/gateway-coverage.md). The desktop client
starts a developer-only setup-to-outcome match as either side, including mulligan
and identity display. Reduced decks use synthetic no-ability identities;
official presets remain locked behind their behavior-coverage gate.
See [the correctness slice](docs/CORRECTNESS_SLICE.md) for audit repairs and
verification status. This is not yet the complete Netrunner ruleset:

- `Netrunner.Core`: typed state, deterministic RNG, atomic legal actions,
  serializable resolution, canonical hashes, scenarios, and replay.
- `Netrunner.Cards`: static metadata, fixture behavior, and strict local-only
  System Gateway normalization/validation.
- `Netrunner.AI`: offline-agent boundary, toy/run agents, and full-turn contract policy.
- `Netrunner.LLM`: provider-neutral LLM transport contracts.
- `Netrunner.Cli`: working `full-match`, `simulate`, `scenario`, `play`, `replay`, `m2-proof`, and
  `card-data` commands.
- `Netrunner.Match`: asynchronous authoritative-state ownership and filtered presentation.
- `Netrunner.Godot`: Godot 4.7.2 .NET client with card and decision presentation.
- headless Core, Cards, AI, CLI, and Match test projects.

The verified M2 proof covers deterministic save/load and replay, public run
event lifecycle, Archives disclosure, per-activation breaker limits,
wallet/hosted-credit payment choices, and exact breach-access timing traces.

See [the M1 completion record](docs/M1_COMPLETION.md) for the reviewed contracts
and gate evidence, and [the M2 progress record](docs/M2_PROGRESS.md) for the
M2 contract, the curated proof, and the independent-review rework log.

## Linux development setup

The bootstrap is local to the repository: it does not require root and does not
modify the system installation. It installs the current .NET 10 LTS SDK, the
side-by-side .NET 8 runtime used by the Godot-targeted projects, and Godot 4.7.2
.NET under `.tools/`.

```bash
./scripts/bootstrap.sh
source scripts/env.sh
./scripts/verify.sh
```

After bootstrap, the common commands are:

```bash
make build
make test
make watch-core
make watch-cards
make cli
make godot-import
make verify
make doctor
make event-smoke
```

The toy agents can prove each M1 win path directly:

```bash
dotnet run --project src/Netrunner.Cli -- play --seed 42 --goal corp-score --json
dotnet run --project src/Netrunner.Cli -- play --seed 42 --goal runner-steal --json
dotnet run --project src/Netrunner.Cli -- play --seed 42 --goal deck-out --json
```

Official System Gateway metadata is optional, local-only, and ignored by Git.
Generate the pinned 77-card snapshot explicitly with:

```bash
./scripts/update-card-data.sh \
  --commit 236140cf84491c23995b028bc791281837520b97 \
  --retrieval-date 2026-08-28
```

`scripts/verify.sh` validates the snapshot when all three generated files are
present, skips it without network access in a clean clone, and rejects a partial
snapshot. No official card artwork is downloaded.

To open the editor:

```bash
source scripts/env.sh
"$GODOT4" --editor --path src/Netrunner.Godot
```

To install the pinned Godot .NET export templates and create the M0 Linux
package:

```bash
make templates
make package-linux
```

The one-shot test host has noticeable startup cost on the M0 reference laptop.
Use `make watch-core` or `make watch-cards` while iterating so the compiler/test
process stays warm. See [the measured M0 spike](docs/spikes/godot-dotnet-loop.md).

The bootstrap currently supports Linux x86_64. The projects themselves are
cross-platform; the macOS development setup is documented in
[docs/DEVELOPMENT_MACOS.md](docs/DEVELOPMENT_MACOS.md), and Windows/macOS
bootstrap and packaging scripts are planned when those release lanes become
active.

## Optional LLM configuration

Offline play will never require an endpoint. When LLM work begins, copy
`.env.example` to `.env` and configure an OpenAI-compatible local or remote
endpoint. `.env` and credentials are ignored by version control and must never
be written into saves or action logs.

## Project rules

- Core and card tests must run without Godot.
- Only the engine mutates authoritative game state.
- Game-affecting randomness uses `DeterministicRandom` and is serialized.
- UI and agents submit engine-issued action IDs; they never receive mutation
  handles.
- Official card art, frames, and backs are not runtime dependencies and are not
  bundled without appropriate permission.
