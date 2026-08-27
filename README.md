# Netrunner Solo

An offline-first, desktop solo implementation of current Null Signal Games
Netrunner. The rules engine is deterministic and UI-independent; Godot is a
presentation client, and every human or AI player acts through the same legal
action boundary.

The product definition is in [the PRD](netrunner_solo_prd_v0.2.md). The
dependency-ordered engineering roadmap is in
[the implementation plan](docs/IMPLEMENTATION_PLAN.md).

## Current state

This repository contains the foundation scaffold, not a playable Netrunner
match yet:

- `Netrunner.Core`: UI-independent domain and deterministic RNG primitives.
- `Netrunner.Cards`: static card definition and behavior boundary.
- `Netrunner.AI`: offline-agent boundary and a contract-test agent.
- `Netrunner.LLM`: provider-neutral LLM transport contracts.
- `Netrunner.Cli`: the future simulation/replay/scenario entry point.
- `Netrunner.Godot`: a minimal Godot 4.7.2 .NET desktop shell.
- headless Core, Cards, and AI test projects.

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
make cli
make godot-import
make verify
```

To open the editor:

```bash
source scripts/env.sh
"$GODOT4" --editor --path src/Netrunner.Godot
```

The bootstrap currently supports Linux x86_64. The projects themselves are
cross-platform; Windows and macOS bootstrap/packaging scripts are planned when
those release lanes become active.

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
