# M0 completion and M1 readiness

Status: **M0 complete — ready to begin M1**  
Reviewed: 27 August 2026

## Exit-gate evidence

| M0 requirement | Result | Evidence |
|---|---|---|
| Repository boundaries and reproducible local setup | Pass | Root solution, pinned SDK/editor, local bootstrap, shared build settings |
| Headless Core/Card/AI tests without Godot | Pass | Six tests pass; test projects reference headless libraries only |
| Build quality | Pass | Full solution builds with zero warnings and zero errors; format check passes |
| Build/rules diagnostics | Pass | `Netrunner.Cli doctor --json`, version `0.1.0-dev`, rules `26.03`, schema versions |
| Godot/Core event bridge | Pass | Five immutable ordered events animate from Core; headless assertion reports all five |
| UI remains live during animation | Pass | smoke run counts 8–10 processed frames during the short animation batch; card-inspection button is independent of the queue |
| Linux packaging spike | Pass | Godot .NET release export succeeds; 147 MiB artifact launches headlessly and repeats the five-event PASS |
| Iteration benchmark | Pass with recorded mitigation | Ten-sample p50/p95 report and OD-04 decision in `docs/spikes/godot-dotnet-loop.md` |
| Secrets and third-party inventory | Pass | repository scanner passes; `THIRD_PARTY_NOTICES.md` records direct/transitive M0 dependencies |
| Linux CI definition | Pass locally | GitHub Actions workflow mirrors restore/build/test/format/secret gates; first hosted run awaits the next push |
| Offline baseline | Pass | event bridge, CLI, tests, and packaged app require no network after bootstrap/templates are installed |

## Defects found and closed during M0

1. Godot inferred the display title `Netrunner Solo` as the managed assembly
   name, while the project produces `Netrunner.Godot.dll`. This allowed builds
   but prevented C# scene instantiation. `project.godot` now pins the correct
   assembly name and the runtime smoke test covers it.
2. The Godot .NET exporter requires a solution beside the Godot project even
   though the repository has a root solution. A client-local solution now
   includes the Godot project and its Core/Card references; real export is part
   of the packaging script.
3. Targeting `net8.0` while building with .NET 10 requires the side-by-side .NET
   8 runtime for testhost. Bootstrap installs and verifies it.
4. The original verification process used single-node MSBuild because of the
   automation sandbox. The benchmark now uses the normal developer process and
   records the cold and warm results honestly.

## Scope boundary at M1 start

M0 deliberately contains no authoritative `GameState`, command reducer, card
rules, save format, or simulation. The five-event batch is a deterministic
presentation fixture and is labeled as such in code. This avoids turning spike
code into an accidental engine.

M1 may now begin with the first executable backlog item after foundation:

1. define stable ID value types and canonical JSON rules;
2. implement zone/card/player/server state and invariant validation;
3. define decision revisions, legal actions, atomic apply results, and rejection
   behavior;
4. add serializable resolution frames, action logs, state hashing, replay, and
   scenarios;
5. vendor pinned Gateway metadata; then build the two-deck toy game.

## Operational follow-up

The next push should confirm the first hosted GitHub Actions run. A hosted-run
failure blocks merging M1 work but does not invalidate the locally proven M0
architecture. The package and raw benchmark artifacts remain ignored and can be
regenerated with `make package-linux` and `make benchmark-m0`.

