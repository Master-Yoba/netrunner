# Netrunner Solo

An offline-first, desktop solo implementation of current Null Signal Games
Netrunner. The rules engine is deterministic and UI-independent; Godot is a
presentation client, and every human or AI player acts through the same legal
action boundary.

The product definition is in [the PRD](docs/netrunner_solo_prd_v0.2.md). The
dependency-ordered engineering roadmap is in
[the implementation plan](docs/IMPLEMENTATION_PLAN.md).
M5 (offline solo mode: heuristic opponents, benchmarks, save/resume and
developer tooling) is complete. Current work follows the
[improvement roadmap](docs/history/IMPROVEMENT_ROADMAP.md) (engine, AI opponent and
UI/UX waves); Wave 0 is recorded in [its progress record](docs/history/WAVE_0_PROGRESS.md).
[The docs index](docs/README.md) lists current documents; milestone history is
in `docs/history/`.

## Current state

All 77 System Gateway designs have complete behavior and registered tests in
[the generated report](docs/generated/card-coverage.md). With authenticated
content provisioned, the desktop client plays official basic and expanded
teaching presets as either side against the offline Standard or Easy bot,
including setup, mulligan, seed selection and a new-match flow. See
[the M4 progress record](docs/history/M4_PROGRESS.md) for the final gate evidence and
[preset revalidation](docs/history/M4_PRESET_REVALIDATION.md) for exact deck provenance.
Support is scoped to System Gateway:

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

See [the M1 completion record](docs/history/M1_COMPLETION.md) for the reviewed contracts
and gate evidence, and [the M2 progress record](docs/history/M2_PROGRESS.md) for the
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
make m4-gate
make doctor
make event-smoke
```

`make m4-gate` requires the authenticated official snapshot and runs the full
repository gate plus official games through the client in both seats and the
two-resolution accessibility checks. `make verify` also supports synthetic CI
without that snapshot and explicitly reports that the official M4 gate was not
run.

Smoke and gate wall-time limits are tuned to the reference hardware. On a
slower machine, set `NETRUNNER_TIMEOUT_SCALE` (1 to 20, default 1) to stretch
every "give up after" limit, for example `NETRUNNER_TIMEOUT_SCALE=4 make
m4-gate`. It never changes game behavior or performance budgets.

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

To play the card-table client with the provisioned official metadata:

```bash
make play
```

Select your side, decks and seed on the setup screen. Click a card or server
for contextual actions; the right-edge history rail expands into the retained
match log. Hover/focus inspects a card, and the Inspect button opens full text.
Escape closes the topmost inspection or pile panel, cancels local staging, or
clears the selected card/server when back on the board.
“Skip animation” preserves history. Use “Copy log” for a filtered playtest report.
Official full card faces can be provisioned explicitly with `make card-art-fetch`.
Board resources, hover rules, and full card inspection use bundled
[Null Signal game symbols](src/Netrunner.Godot/icons/SOURCE.md), including clicks,
credits, memory, link, recurring credits, and subroutines. They work offline in
both the game and interaction lab, independently of the artwork cache.
The game reads a local cache only; missing images use the generated card frames.
Use `make card-art-pack ART_ARGS='--output artifacts/gateway-art.zip'` to carry
the cache to an offline machine. See [artwork caches](docs/CARD_ARTWORK.md) for
unpacking, optional metadata bundles, cache locations, and exported builds.
Legacy `user://card-art/<card-code>.png` illustrations remain supported.
The developer view remains available through
`./scripts/play.sh --diagnostic-ui`.

See [the M4.9 UI plan](docs/history/M4_9_UI_PLAN.md) and
[implementation evidence](docs/history/M4_9_UI_PROGRESS.md).

After `make m4-gate` has built the official content, run a preset directly:

```bash
dotnet run --project src/Netrunner.Cli --no-build -- full-match --preset expanded --seed 47042 --json
dotnet run --project src/Netrunner.Cli --no-build -- full-match --preset expanded --seed 47042 --json
```

These runs validate setup, save/load at every decision, and the resulting
replay. Omitting `--preset` retains the synthetic developer match.

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
process stays warm. See [the measured M0 spike](docs/history/spikes/godot-dotnet-loop.md).

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

### Practice board interactions

Use `make interaction-lab` for resettable drag/play/install UI scenarios without
playing a match. The setup screen also has **Practice board interactions**.
Choose **Runner** or **Corp**, then an interaction. **Hand & board basics**
combines playing events/operations, drawing, credits, and pile browsing.
Fixtures are synthetic and exercise the production UI; game-rule correctness
is covered separately by engine tests. Known practice cards use the same local
artwork cache as `make play`, including cards drawn during a scenario. Run
`make card-art-fetch` to populate it, or set `NETRUNNER_CARD_ART_DIRECTORY` to an
unpacked cache. The lab does not require official metadata or internet access;
without artwork it retains the generated card layout.
