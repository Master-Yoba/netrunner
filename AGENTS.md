# Repository Guidelines

## Project Structure & Module Organization

`Netrunner.sln` groups the .NET 8 projects. Put deterministic rules and state in `src/Netrunner.Core`, card metadata and behaviors in `src/Netrunner.Cards`, agents in `src/Netrunner.AI`, provider-neutral contracts in `src/Netrunner.LLM`, orchestration and presentation models in `src/Netrunner.Match`, commands in `src/Netrunner.Cli`, and Godot UI code/scenes in `src/Netrunner.Godot`. Mirror these areas under `tests/<Project>.Tests`. Repository fixtures live in `data/`, design records in `docs/`, maintenance commands in `scripts/`, and developer utilities in `tools/`.

Keep the engine UI-independent: only the engine mutates authoritative state, agents and UI submit engine-issued action IDs, and game-affecting randomness must use serialized `DeterministicRandom`.

## Build, Test, and Development Commands

Run `./scripts/bootstrap.sh` once, then `source scripts/env.sh` in each shell.

- `make restore` restores solution dependencies.
- `make build` compiles the solution without restoring.
- `make test` runs all xUnit projects.
- `make watch-core` or `make watch-cards` provides a fast test loop.
- `make format` applies repository formatting rules.
- `make cli` runs the deterministic CLI self-test.
- `make godot-import` validates the Godot project headlessly.
- `make verify` runs the complete pre-merge gate, including formatting, proofs, smoke tests, generated coverage checks, and secret scanning.

## Coding Style & Naming Conventions

Follow `.editorconfig`: UTF-8, LF endings, final newlines, and no trailing whitespace. C# uses four spaces, file-scoped namespaces, sorted `System` directives, nullable references, and warnings as errors. Godot C# under `src/Netrunner.Godot` uses tabs. JSON, YAML, Markdown, and Godot resources use two spaces. Use PascalCase for types and public members, camelCase for locals and parameters, and descriptive filenames matching their primary type. Run `dotnet format Netrunner.sln --verify-no-changes` before submitting.

## Testing Guidelines

Tests use xUnit (`[Fact]` and `[Theory]`) and follow `*Tests.cs`/`*Tests` naming. Add focused regression tests in the corresponding test project; Core, Cards, AI, and CLI tests must remain headless. Preserve deterministic seeds and assert serialized/timing contracts when changing engine behavior. No numeric coverage threshold is configured, but generated Gateway coverage reports must remain current.

For bot and Match changes, run `make bot-check` per change (formatting, the quick M5 cases, and the official-data AI, Match and CLI suites in parallel; about 15 minutes) plus the ladder a new policy version needs. Run the full gates (`make verify`, `make m5-gate`, `make ai-tactics-verify TACTICAL_ARGS='--workers 12'`) once per batch of changes, before pushing; `make gates REF=<commit>` runs them all in a separate worktree (`.tools/gate-worktree`), so work can continue in this checkout meanwhile.

## Commit & Pull Request Guidelines

Recent history favors short, imperative subjects, commonly Conventional Commit prefixes such as `feat:`, `fix:`, `test:`, and `docs:`. Keep each commit scoped. Checkpoints should be locally commited while the work on a slice or milestone progresses.
Work on a slice or a milestone should happen on it's own feature branch. When finished - push to GH and make a PR into dev branch. Pull requests should explain behavior and architecture impact, link relevant issues or milestone docs, list verification performed, and include screenshots for visible Godot changes. Never commit `.env`, credentials, `.tools/`, Godot cache files, or local official card data. The generic NSG card backs under `src/Netrunner.Godot/card-backs` are committed for CI parity (see its `SOURCE.md`), but card art and the `.tools/` copies remain local-only.

GitHub Actions minutes are limited, so run the gates locally before pushing and treat CI as a backstop. Ready-for-review PRs run only the fast synthetic tier (`make verify`); pushes to `dev`/`main` and PRs labelled `full-ci` run the full official tier. Drafts and documentation-only changes run nothing, and a newer push cancels a superseded run. Merge a stack of PRs so that one push to `dev` carries it, and use `[skip ci]` in both the commit and the merge subject for changes CI can't meaningfully test, such as CI configuration. The full M5 benchmark workflow is manual only; run `make m5-gate` locally instead.
