# M4 progress — System Gateway complete

Status: Slice 4.7 implemented and verified on `m4_7`; ready for review into `dev`.

Date started: 31 August 2026

Rules/data baseline: Null Signal Comprehensive Rules 26.03 / System Gateway
vendored commit `236140cf84491c23995b028bc791281837520b97` (77 designs)

## Scope

Slices 4.1 through 4.6 of `docs/M4_PLAN.md` and the correctness prerequisite
are merged. Slice 4.7 implements ENG-433–437, RULE-401, AGT-402, UI-408,
and the final Gateway batch, bringing behavior coverage to 77/77.

Match, CLI, and both client seats share Core setup and terminal execution with
official basic/expanded presets as well as developer decks. See
[the correctness follow-up](CORRECTNESS_SLICE.md) for
the preceding setup and server-lifecycle compatibility evidence.

## Baseline

- M3 completion evidence records 356 tests green, deterministic M1/M2 proofs,
  M3 smoke and latency smoke green.
- Seven Gateway designs are registered in `gateway-m2-3`; Palisade,
  Whitespace, Cleaver, Buzzsaw, and Overclock are full-text complete, while
  Nico Campaign and Offworld Office are partial.
- The local authenticated 77-card source snapshot is present but gitignored.
- `docs/M4_PLAN.md` is the architect-reviewed execution contract.

## Slice 4.1 work log

### 2026-08-31 — started

- Started three bounded Luna Max implementation workstreams:
  architecture/version/catalog foundation; CARD-401 plus primitive audit; and
  metadata-build plus UI foundation.
- The primary agent owns integration, architectural review, full verification,
  and this progress record.
- A direct `dotnet test` before loading `scripts/env.sh` could not find the SDK;
  all actual verification commands will use the repository environment script.

### 2026-08-31 — implementation streams landed; architect review in progress

- ARCH-401/VER-401 landed an immutable, injected behavior catalog and engine
  session boundary, distinct metadata/behavior pins, state schema 2, and M2
  hash migration coverage. The architect review rejected empty ENG-400 marker
  partials as insufficient and returned that stream for a bounded mechanical
  extraction plus cross-family catalog conflict validation.
- PRIME-AUDIT/CARD-401/COV-401 landed the 77-code registry and rulings seed,
  metadata-only test discovery, deterministic checked-in reports, and the
  primitive audit. The reviewed report is currently 7 registered and tested,
  5 full-text complete, 2 partial, and 70 unregistered, with zero diagnostics.
- DATA-400/UI-401/UI-402 landed the generated Cards resource, injected metadata
  resolver, allow-listed formatter, provisioned gallery, Theme foundation,
  and first focus-region extraction. The architect review returned the stream
  to make content selection explicitly opt-in and to complete honest,
  testable region seams beyond the focus partial.
- First combined gate: build succeeded with zero warnings/errors and all 385
  tests passed (Core 259, Cards 54, AI 9, CLI 47, Match 16). The gate then
  stopped on four import/format diagnostics; these were mechanically fixed.
  Proof and Godot evidence remain pending the post-review rerun.

### 2026-08-31 — implementation resumed after interruption

- Reconstructed the Slice 4.1 state from commit `bf87766` and this progress
  record; the worktree was clean at resume time.
- Resumed the architect-review phase. Bounded Luna Max follow-up streams will
  address the rejected ENG-400/catalog seam and the DATA-400/UI-402 seam,
  while a separate verification audit checks the complete Slice 4.1 contract.
- No Slice 4.2 implementation is authorized until the Slice 4.1 exit evidence
  is green and recorded here.
- Final handoff now includes committing the reviewed integration, pushing the
  current branch, and opening a GitHub pull request into `dev`; the resulting
  commit and PR will be recorded in this file.

### 2026-08-31 — ENG-400/catalog/version follow-up

- Replaced the empty `GameEngine.Damage.cs` and `GameEngine.Identities.cs`
  markers with a bounded mechanical split: shared working-state/card movement
  and event helpers now live in `GameEngine.State.cs`, catalog compatibility
  checks in `GameEngine.Catalog.cs`, and generic resolution-frame decision
  helpers in `GameEngine.Resolution.cs`. Existing turn/install/advance/run
  reducers retain their bodies and behavior; the Core build is warning-free.
- `BehaviorCatalog` now validates duplicate typed-family IDs again at the
  immutable construction boundary, and the legacy static adapter rejects
  cross-family registration before mutating its process-wide compatibility
  state. Tests cover every pair of the five typed families, same-family
  structural/conflicting duplicates, the generic overlay contract, and the
  static-adapter fail-fast path.
- Closed the D-401 legacy pin loophole in `M2ScenarioDocument`: only the
  authenticated Gateway metadata commit is accepted, missing behavior and
  fingerprint fields are never inferred from metadata, and the M2 artifact
  schema check uses `EngineCompatibility.ArtifactSchemaVersion`. Build save
  metadata now derives its state schema pin from `GameState.CurrentSchemaVersion`.
- VER-401 migration evidence is precise: pre-migration schema-1/legacy-pin
  representative M2 initial/final state hashes were
  `4733c8f84f1f054c6c1277e07d6450173cfb0d4741d11466cf8997cfd5ea4ac6` /
  `c822e9790dcad48ccdb52f42539be678943fc109820ea2c7e20e5f8fa03805ac`;
  schema-2/separate-pin hashes are
  `a61f20c71efa735d6b893b2de5bd75da96f6b20481e5ab8c0a1c0a7ac40a2c8b` /
  `950d1b0d44da26d5ba9a688b83b7ac8202850d8337a8c0609eaf47269b1387ed`.
  The event stream remains
  `d20cdef94e97ac928ca088e14952b65d2116fecda00485fff5c3b05e1b6e9e20`;
  no semantic event ordering or payload diff was observed.
- Verification: `source scripts/env.sh && dotnet test tests/Netrunner.Core.Tests/Netrunner.Core.Tests.csproj --no-restore -m:1`
  (275/275), the equivalent full CLI test command (48/48), the focused M2
  proof filter (4/4), and Core/CLI `dotnet format --verify-no-changes` all pass.
  Direct `dotnet run --project src/Netrunner.Cli --no-build --no-restore --
  m2-proof data/m2/representative-run.json --json` also passes with 32
  checkpoints, replay verified, and the exact migrated hashes above.
  The solution build/format gate remains blocked by unrelated concurrent UI
  changes: CA1512 at `src/Netrunner.Match/UiRegionPresenters.cs:149`, plus
  existing import/unused-using diagnostics in the UI project and its tests.

### 2026-08-31 — independent Slice 4.1 audit

- Audit started against `docs/M4_PLAN.md` and the worktree at the current
  HEAD; no Slice 4.2 work or code changes were made by the audit.
- Exact checks run: `source scripts/env.sh && dotnet build Netrunner.sln
  --no-restore --nologo -m:1` (warning/error-free after the follow-up edits);
  `source scripts/env.sh && dotnet restore Netrunner.sln -m:1
  -p:NuGetAudit=false` (pass; the default restore was blocked by unavailable
  NuGet audit network access); the focused CardCoverage invocation with the
  compiled Cards/tests assemblies and the local 77-card source (pass:
  `total=77 registered=7 tested=7 complete=5 partial=2 unregistered=70`,
  zero diagnostics); and an official Cards content build with
  `NetrunnerContentEnabled=true NetrunnerCardDataMode=official` (started but
  was stopped when the parent requested the audit finish).
- `dotnet test Netrunner.sln --no-restore -m:1` and
  `dotnet format Netrunner.sln --no-restore --verify-no-changes` could not
  provide evidence in this sandbox: VSTest/named-pipe communication failed
  with `SocketException (13) Permission denied`. These are not recorded as
  passing checks; the authoritative full gate remains with the primary agent.
- Findings requiring bounded follow-up before the Slice 4.1 exit gate (4 and
  5 are closed in the follow-up entry below):
  (1) **high** — `scripts/verify.sh:133-137` launches the gallery smoke with
  no `--quit-after`, while `Main.Gallery.cs:14-112` only prints PASS/FAIL and
  never calls `GetTree().Quit`; a headless gallery invocation therefore hangs
  (and a FAIL does not become a process failure). Make the gallery smoke
  terminate with pass/fail status, and retain a timeout defense in the script.
  (2) **high** — the content lane explicitly builds only
  `Netrunner.Cards.csproj` official at `scripts/verify.sh:38-44`, then invokes
  Godot's own build at `scripts/verify.sh:125-136`; there is no assertion that
  CLI/Match/Godot outputs contain the official generated resource. A manual
  inspection found the official Cards output alongside smaller stale
  downstream copies, and the Godot editor build regenerated synthetic card
  data before the gallery reported `found 7 in synthetic mode`. Make the
  downstream content-enabled build properties explicit and assert the gallery
  sees the authenticated 77-card payload.
  (3) **high** — clean local Godot discovery in `scripts/env.sh:25-27` searches
  only two directory levels, so the provisioned
  `.tools/godot/Godot.app/Contents/MacOS/Godot` executable is not found after
  `GODOT4` is unset. The verification script then exits as unavailable unless
  callers pre-export a path. Search the app bundle depth (or use an explicit
  bundle path) and add a clean-provisioning check.
  (4) **medium** — `ObservationViewMapper.CalculateEffectiveStrength` and
  `CalculateBreakerStrengths` still consult the process-wide
  `CardBehaviorSpecCatalog`/Gateway static lists at
  `src/Netrunner.Match/PresentationViewModels.cs:431-495`, despite D-417's
  injected-catalog production-path requirement. Thread the immutable catalog
  through the Match view projection (or carry the Core-derived values) while
  retaining the no-argument M2 adapter only for compatibility callers.
  (5) **medium/design** — `tools/Netrunner.CardCoverage/Program.cs:556-646`
  discovers registrations by exported `*BehaviorSpecs` lists and the legacy
  static `DefinitionIds`, and invokes `RegisterAll`; it does not inspect the
  immutable `GatewayBehaviorCatalog.Create()`/generic catalog. Current 7-card
  counts pass, but a future generic-only registration could be falsely
  reported unregistered. Have CARD-401 read a deterministic compiled catalog
  surface (or expose one from `GatewayBehaviorCatalog`) without executing
  tests.

### 2026-08-31 — catalog injection/coverage discovery follow-up

- Closed audit finding (4): `ObservationViewMapper` now has a canonical
  `(observation, metadataResolver, behaviorCatalog)` composition overload;
  `MatchCoordinator.Publish` supplies its immutable session catalog, and
  effective ICE/icebreaker strengths use that snapshot exclusively. The
  no-catalog overloads remain explicitly documented M2 compatibility adapters
  that construct the immutable Gateway catalog. A regression test supplies a
  deliberately altered catalog and verifies both projected strength values,
  proving the process-wide compatibility registry is not consulted.
- Closed audit finding (5): `Netrunner.CardCoverage` now invokes the compiled
  `GatewayBehaviorCatalog.Create()` surface and reads immutable
  `AllDefinitionIds`; exported spec lists, `CardBehaviorSpecCatalog`, and
  `RegisterAll` are no longer discovery inputs. The generic-only ID contract
  is covered by Core catalog tests, and Gateway coverage marker tests now query
  the immutable catalog. This keeps future generic-only registrations visible
  to CARD-401 without executing tests or mutating process-wide registration.
- Exact focused verification: `source scripts/env.sh && dotnet test
  tests/Netrunner.Match.Tests/Netrunner.Match.Tests.csproj --no-restore -m:1
  --filter FullyQualifiedName~MatchCoordinatorTests` (9/9), `source
  scripts/env.sh && dotnet test
  tests/Netrunner.Core.Tests/Netrunner.Core.Tests.csproj --no-restore -m:1
  --filter FullyQualifiedName~BehaviorCatalogTests` (21/21), and `source
  scripts/env.sh && dotnet test
  tests/Netrunner.Cards.Tests/Netrunner.Cards.Tests.csproj --no-restore -m:1
  --filter FullyQualifiedName~GatewayCoverageMetadataTests` (8/8) pass. The
  tool build `source scripts/env.sh && dotnet build
  tools/Netrunner.CardCoverage/Netrunner.CardCoverage.csproj --no-restore
  --nologo -m:1` is warning/error-free. The immutable-reader invocation
  `source scripts/env.sh && dotnet run --project tools/Netrunner.CardCoverage
  --no-build --no-restore -- --root "$PWD" --catalog
  "$PWD/src/Netrunner.Cards/bin/Debug/net8.0/Netrunner.Cards.dll" --tests
  "$PWD/tests/Netrunner.Cards.Tests/bin/Debug/net8.0/Netrunner.Cards.Tests.dll"
  --cards "$PWD/data/vendor/system-gateway/cards.json" --markdown
  /tmp/gateway-coverage-followup.md --json /tmp/gateway-coverage-followup.json`
  reports `total=77 registered=7 tested=7 complete=5 partial=2
  unregistered=70` with zero diagnostics. Core, Cards-tests, and Match-tests
  formatting checks pass for touched projects, and the targeted Match-file
  check `source scripts/env.sh && dotnet format
  src/Netrunner.Match/Netrunner.Match.csproj --no-restore
  --verify-no-changes --include
  src/Netrunner.Match/PresentationViewModels.cs
  src/Netrunner.Match/MatchCoordinator.cs` passes. Project-wide Match
  formatting still reports the unrelated concurrent `UiRegionPresenters.cs`
  import diagnostic and
  Match-tests formatting reports the unrelated concurrent
  `UiRegionPresenterTests.cs` unused using.

### 2026-08-31 — DATA-400/UI-401/UI-402 follow-up and audit closure

- DATA-400 now requires explicit `NetrunnerContentEnabled=true` before the
  official mode can be selected. The clean/default build remains synthetic
  even when the ignored vendor snapshot is present; missing inputs identify
  the exact path, pinned bootstrap command, and required rebuild properties.
  The generator also uses a Python-compatible LF writer, so the default and
  official builds are deterministic on the provisioned lane.
- UI-401/UI-402 now have immutable, Match-layer seams for board, actions,
  event feed, focus, and card zoom. Main consumes all five through its region
  partials; focused tests cover collection ownership, action target
  projections, every focus mode, feed retention/description, zoom formatting,
  and the behavior-preserving `RunCredits > 0` summary condition. Theme and
  M3 behavior remain unchanged.
- Closed the gallery audit: `--m4-card-gallery-smoke` now reports a real
  success/failure exit through typed deferred shutdown, and `verify.sh`
  bounds the process, captures its log, and requires the exact
  `M4_CARD_GALLERY PASS cards=77 mode=official` marker. When the local source
  is complete, verification explicitly builds Cards, Match, CLI, and Godot
  with `NetrunnerContentEnabled=true`, `NetrunnerCardDataMode=official`, and
  the validated input root before launching the gallery, preventing a stale
  synthetic downstream output from satisfying the content proof. `env.sh`
  now discovers both direct binaries and the provisioned
  `.tools/godot/Godot.app/Contents/MacOS/Godot` app-bundle executable.
- Evidence: `bash -n scripts/verify.sh` (pass); clean
  `env -u GODOT4 bash -lc 'source scripts/env.sh; ...'` discovery check
  (pass, app-bundle path); default Godot build (0 warnings, 0 errors);
  explicit official Cards/Match/CLI/Godot builds (each 0 warnings, 0
  errors), generated mode `official`, and the official editor/gallery launch
  (exit 0, `M4_CARD_GALLERY PASS cards=77 mode=official`); focused Match
  tests 22/22 and Cards tests 54/54. The DATA-400 missing-input and
  official-without-opt-in contracts also fail with their asserted diagnostics.
- No substream blockers and no Slice 4.2 work. The primary agent still owns
  the final full `scripts/verify.sh` gate and integration handoff.

### 2026-08-31 — UI-401/D-412 resolver-gallery follow-up

- Closed the final client dependency gap by adding the Match-owned immutable
  `CardMetadataSnapshot` composition boundary (resolver, mode, authenticated
  commit, count, and diagnostic). `CardPresentationMetadata` now retains the
  source side as presentation data, while Main explicitly holds the snapshot
  resolver and supplies that same resolver in normal, fast-smoke, and latency
  `MatchCoordinatorOptions`.
- The gallery now iterates only `_metadataResolver.Cards` and renders
  `CardPresentationMetadata` fields through Match's shared markup formatter;
  its official proof retains mode/count and emits the authenticated commit in
  the PASS marker. Godot no longer has a direct Cards project reference or
  Cards-layer code dependency; Cards remains transitively owned by Match.
- Focused evidence: `env -u GODOT4 bash -lc 'source scripts/env.sh &&
  dotnet build src/Netrunner.Match/Netrunner.Match.csproj --no-restore
  --nologo -m:1 --verbosity:minimal && dotnet build
  src/Netrunner.Godot/Netrunner.Godot.csproj --no-restore --nologo -m:1
  --verbosity:minimal'` passed with 0 warnings/errors; the elevated
  `env -u GODOT4 bash -lc 'source scripts/env.sh && dotnet test
  tests/Netrunner.Match.Tests/Netrunner.Match.Tests.csproj --no-restore -m:1
  --verbosity:minimal'` passed 24/24. The non-elevated test-host attempt was
  blocked by sandbox `SocketException (13)` and is not counted as evidence.
  Focused `dotnet format src/Netrunner.Match/Netrunner.Match.csproj
  --no-restore --verify-no-changes --include CardMetadataResolver.cs` did not
  complete before the tool window was interrupted; no format pass is claimed.
- No Slice 4.2 work. The primary agent owns the official-content gallery
  rerun and final full verification gate.

### 2026-08-31 — primary integration gate started

- The first authoritative `./scripts/verify.sh` attempt stopped before build
  because the inherited `GODOT4` value pointed at an old workspace. The app
  bundle was present at the newly discovered local path, but `env.sh` only
  replaced an empty variable. The integration review tightened discovery to
  replace both empty and non-executable inherited paths; the full gate will be
  rerun from the corrected environment.
- The corrected gate then completed a zero-warning build and passed all 411
  tests (Core 276, Cards 55, AI 9, CLI 48, Match 23) before the format stage
  reported three mechanical import-order/unused-using diagnostics in the new
  UI seam files. Those imports were corrected; no behavioral change was made,
  and the complete gate will be rerun rather than treating the partial pass as
  exit evidence.
- The next escalated rerun passed build, format, all 411 tests, tool/doctor
  checks, official metadata validation, and explicit official builds through
  Godot before the DATA-400 negative check found a macOS-only doubled `/` in
  its exact temporary-path assertion (`TMPDIR` already ends in `/`). The
  verification script now strips the trailing separator before `mktemp`; the
  negative build itself failed with the correct bootstrap guidance, so this
  was a verifier portability defect rather than a content-build defect.
- After normalizing the temp root, the authoritative gate completed green:
  zero-warning/error build, all 411 tests, format, doctor/self-test, DATA-400
  negative diagnostics, deterministic coverage, M1 goals/replay, the 32-step
  M2 proof, official editor import, `M4_CARD_GALLERY PASS cards=77
  mode=official`, M3 smoke, and secret scan all passed. Final contract review
  nevertheless returned one UI-401/D-412 issue before acceptance: the gallery
  still rendered Cards-layer records directly instead of the injected Match
  metadata resolver. A final bounded Luna follow-up is removing that direct
  presentation dependency; the full gate will be rerun after review.

### 2026-08-31 — Slice 4.1 accepted

- The final resolver-based dependency graph passed the complete authoritative
  `./scripts/verify.sh` gate: zero warnings/errors, format clean, and 412/412
  tests (Core 276, Cards 55, AI 9, CLI 48, Match 24).
- DATA-400 proved both negative contracts, rebuilt Cards/Match/CLI/Godot with
  the authenticated official snapshot, and the resolver-backed gallery exited
  successfully with `M4_CARD_GALLERY PASS cards=77 mode=official`.
- CARD-401/COV-401 regenerated without a diff and reported 77 total, 7
  registered/tested, 5 complete, 2 partial, 70 unregistered, and zero
  diagnostics. M1 goal/replay proofs and the 32-action M2 proof passed; the M2
  initial/final/event hashes match the VER-401 pins recorded above.
- Godot editor import, the fast M3 smoke, and the separate real-delay latency
  smoke passed. Latency evidence: `held_ms=10253`, 1,486 thinking frames,
  144.9 average FPS, one-frame inspection/control/hover feedback, zoom in
  viewport, and both 1280x720 and 2560x1440 layouts green.
- The architect reviewed every Luna stream and the independent audit. All five
  audit findings and the final UI-401/D-412 dependency issue are closed; no
  Slice 4.2 behavior entered this change set.

### 2026-08-31 — pull-request handoff

- Committed the reviewed implementation as `f6f77e9` (`Complete M4 Slice 4.1
  architecture and content foundation`) and pushed `M4-horizon` to `origin`.
- Opened [PR #5](https://github.com/Master-Yoba/netrunner/pull/5) from
  `M4-horizon` into `dev`, with the completed Slice 4.1 scope and authoritative
  verification evidence in the PR description.
- GitHub Actions `build-and-test` passed on the pushed handoff head in 2m9s;
  the follow-up that records this result is documentation-only.

## Architecture record

The Slice 4.1 foundation decisions D-401 through D-417 in `docs/M4_PLAN.md`
are accepted. Production composition now uses immutable injected behavior and
metadata catalogs; rules-only convenience overloads use the immutable empty
catalog and M2 composition uses an explicit scenario slice, with no
process-wide behavior registry remaining. Engine extraction is mechanical,
schema/version migration is pinned, and Godot consumes Match presentation
boundaries rather than Cards records.

## Evidence

- Authoritative command: `./scripts/verify.sh` — PASS.
- Build/format/tests: 0 warnings, 0 errors, format clean, 412/412 tests.
- Coverage: 77 total / 7 registered and tested / 5 complete / 2 partial / 70
  unregistered; zero diagnostics and checked-in reports current.
- M2: initial `a61f20c…`, final `950d1b0d…`, event stream `d20cdef9…`, 32
  actions/checkpoints, scripted and contract-agent policies identical, replay
  verified.
- Content/client: missing-input and opt-in diagnostics PASS; official
  Cards/Match/CLI/Godot builds PASS; resolver gallery 77/official PASS; editor
  import, fast M3 smoke, ten-second latency smoke, and secret scan PASS.

## Deviations and blockers

None. The authenticated official source remains gitignored by design; builds
embed a validated generated resource and perform no runtime network fetch.

### 2026-08-31 — PR #5 feedback follow-up started

- GitHub review at `f6f77e9` raised one P1 concern: the checked M2
  representative-run document pins the fingerprint of the complete Gateway
  catalog rather than only the seven behavior definitions the scenario uses.
- Primary architectural assessment: valid. `CollectProblems()`, scenario
  setup, and proof execution all currently compare or persist the full catalog
  fingerprint, so an unrelated additive Gateway registration would reject the
  document and churn its pinned state hashes.
- Accepted fix boundary: derive an immutable behavior-catalog slice from the
  scenario's referenced definition IDs, use it consistently for validation,
  setup, proof execution, and match coordination, and prove with a regression
  test that an unrelated additive registration does not change the scenario
  slice fingerprint or built state. Luna Max implemented the bounded change;
  the primary architect reviewed and accepted its final API, runtime wiring,
  compatibility overload, diagnostics, and regression coverage.
- Implemented `BehaviorCatalog.CreateSlice` plus
  `M2ScenarioDocument.CreateBehaviorCatalog`/injected validation. The slice
  retains the supplied version and copies every typed and generic contract for
  the document's distinct, valid card definition IDs; missing or changed
  required contracts still fail with the document's exact card/fingerprint
  paths.
- `M2ScenarioSetup`, `M2ProofRunner`, and `MatchCoordinator` now carry the
  validated scenario slice into state construction, save/load/replay, engine
  actions, and presentation. The no-argument Gateway adapters remain for M2
  compatibility callers.
- Added regression coverage for an unrelated injected behavior (stable slice
  fingerprint and state hash), plus missing and structurally changed required
  behavior. The current representative document already references all seven
  Gateway registrations, so its slice fingerprint and existing initial/final
  state pins remain unchanged; no JSON pin edit was necessary.
- Focused evidence: CLI M2 scenario setup tests `19/19`, CLI M2 proof tests
  `4/4`, Match tests `24/24`, Core behavior-catalog tests `22/22`, and the CLI
  project build all pass with zero warnings/errors.
- Primary authoritative `./scripts/verify.sh` evidence: zero warnings/errors,
  format and `git diff --check` clean, and `416/416` tests passed (Core 277,
  Cards 55, AI 9, CLI 51, Match 24). Coverage remained 77 total / 7
  registered and tested / 5 complete / 2 partial / 70 unregistered; the M1
  proofs, 32-action M2 proof and replay passed with unchanged hashes; official
  resolver gallery reported 77/official; Godot editor import, M3 smoke, and
  secret scan passed.
- Committed the reviewed fix as `d4d5a70` (`Pin M2 proof to scenario catalog
  slice`) and pushed it to PR #5. GitHub Actions `build-and-test` passed on that
  implementation head in 2m15s; this final progress update is documentation
  only.

### 2026-08-31 — second PR #5 feedback follow-up started

- A fresh review of `f91811d` found one additional P1 path in
  `CliCommands.RunM2Proof`: after both policy runs verify their replay against
  the scenario slice, the command re-verifies the returned artifact against a
  newly created full Gateway catalog.
- Primary architectural assessment: valid. A future unrelated additive
  Gateway registration would leave the document-derived replay pin stable but
  make this separate command-level verification report `replayVerified:
  false` because the full-catalog fingerprint differs.
- Implemented the bounded fix in `CliCommands.RunM2Proof`: the command now
  composes one catalog, derives one document scenario slice, and passes that
  same immutable slice to both policy runs and the final `ReplayVerifier`
  call. The production overloads retain their existing default behavior; an
  internal `RunWithBehaviorCatalog` seam keeps composition injection scoped to
  command-level coverage.
- Added `M2ProofCommandTests.M2ProofCommandReplaysAgainstTheScenarioSliceOfAnExpandedCatalog`,
  which adds an unrelated asset registration to an otherwise identical
  Gateway composition and asserts `m2-proof --json` remains successful with
  `replayVerified: true` and the pinned final-state/event-stream hashes.
- Focused evidence: `dotnet test tests/Netrunner.Cli.Tests/Netrunner.Cli.Tests.csproj
  --no-restore --filter FullyQualifiedName~M2ProofCommandTests --verbosity
  minimal -m:1` passed (3/3). Primary full verification/CI is intentionally
  not claimed in this follow-up.
- The primary architect reviewed and accepted the internal-only composition
  seam, the single validated-slice ownership in the command, and the expanded
  catalog regression; public CLI behavior and output remain unchanged.
- Primary authoritative `./scripts/verify.sh` evidence: zero warnings/errors,
  format and diff checks clean, and `417/417` tests passed (Core 277, Cards 55,
  AI 9, CLI 52, Match 24). Coverage remained 77 total / 7 registered and
  tested / 5 complete / 2 partial / 70 unregistered. M1 and the 32-action M2
  proof/replay passed with unchanged hashes and `replayVerified: true`; the
  official 77-card gallery, Godot editor/M3 smoke, and secret scan passed.
- Committed the reviewed fix as `ddb4448` (`Verify M2 replay with scenario
  catalog`) and pushed it to PR #5. GitHub Actions `build-and-test` passed on
  that implementation head in 2m0s; this final progress update is
  documentation only.

## Slice 4.1 exit checklist

- [x] ARCH-401 / ENG-400 / VER-401 implemented and reviewed.
- [x] PRIME-AUDIT complete in `docs/M4_PRIMITIVES.md`.
- [x] CARD-401 / COV-401 generated report distinguishes registered and complete.
- [x] DATA-400 content-enabled build is deterministic and offline after build.
- [x] UI-401 renders all 77 provisioned cards through an injected resolver.
- [x] UI-402 Theme/region extraction preserves M3 smoke and latency behavior.
- [x] Full `scripts/verify.sh` green with final test/smoke evidence recorded.

## Slice 4.2 work log

### 2026-09-01 — Slice 4.2 started

- Started from clean branch `m4_2` at merged `dev` commit `0a46ca6`; Slice 4.1
  and both PR-review follow-ups are present in the baseline.
- Scope is the complete Slice 4.2 contract from `docs/M4_PLAN.md`: ENG-401–406,
  MATCH-401A/B, DATA-401A, all 12 Batch 1 cards, AGT-401, SIM-401, UI-403, and
  OBS-401. Later batches remain excluded.
- Execution is dependency-phased to protect shared state/serialization seams:
  (1) turn/timing shell in Core alongside independent deck/preset contracts;
  (2) basic actions, run nesting, generic choices and install/play/rez;
  (3) 12 card registrations and observation coverage; (4) setup/sim/agent and
  client integration; (5) coverage regeneration and authoritative exit gates.
- The primary agent owns architecture, stream boundaries, cross-stream review,
  integration, the progress ledger, and final verification. Bounded Luna Max
  agents implement each stream; no agent commits or pushes independently.

### 2026-09-01 — ENG-404/ENG-406 declarative Core contract foundation

- Added immutable Core behavior contracts for explicit chooser ownership
  (`acting player`, controller, opponent, Corp, Runner), mode/target/order
  choices, and exactly/up-to/any-number cardinality. Exact and up-to choices
  require positive bounds; declared mode options are unique and frozen.
- Added typed condition, zone, card-filter, target, instruction, branch, and
  static-modifier vocabulary for tagged/not-tagged, remaining clicks,
  installed-this-turn, successful-run-this-turn, hidden Deck/HQ/Archives
  selection, reveal, trash, add-to-hand, shuffle-into-deck, optional install,
  installed-card advancement, memory, and maximum-hand-size modifiers.
- `CardBehaviorDefinition` freezes the instruction tree, whose nested choices
  and branches are the sole reusable payload source. Catalog duplicate checks
  and generic fingerprints traverse every nested contract in order, including
  null-vs-value distinctions; no delegates, raw rules-text interpretation,
  title branches, or runtime reducer hooks were added.
- Focused evidence: `dotnet build
  tests/Netrunner.Core.Tests/Netrunner.Core.Tests.csproj --no-restore --nologo
  -m:1` passed with zero warnings/errors; the focused
  `dotnet test tests/Netrunner.Core.Tests/Netrunner.Core.Tests.csproj
  --no-restore --filter
  "FullyQualifiedName~BehaviorContractsTests|FullyQualifiedName~BehaviorCatalogTests"
  --verbosity minimal -m:1` run passed 27/27; and `dotnet format
  tests/Netrunner.Core.Tests/Netrunner.Core.Tests.csproj --no-restore
  --verify-no-changes` plus `git diff --check` passed. Runtime
  execution/state/replay/card registration remains pending; no full
  verification or CI result is claimed here.

### 2026-09-01 — ENG-404/ENG-406 architect review corrections

- The instruction tree is now the sole source of truth for reusable choices
  and branches; duplicate top-level `CardBehaviorDefinition` collections were
  removed. `Choose`/`Branch` require their matching typed payload and reject
  payloads attached to any other instruction kind.
- Search now requires a typed source zone, filter, and explicit zone-card
  candidate descriptor. Corp Deck/HQ/Archives and Runner Stack are supported;
  add-to-hand requires a destination (HQ or Grip), shuffle-into-deck requires
  Deck or Stack, and install requires Rig, server-root/ICE, or play-area
  destinations.
- New executable numeric instructions (`GainCredits`, `Draw`, `LoseClicks`,
  and `AdvanceCard`) require positive amounts. Mode choices require exactly
  one selection; optional choices require up to one. Dynamic target choices
  remain otherwise unconstrained.
- Focused evidence was rerun after these corrections: Core build passed with
  zero warnings/errors, contract/catalog tests passed 27/27, format
  verification passed, and `git diff --check` passed. Runtime execution,
  state/replay integration, and final Slice 4.2 verification remain pending.

### 2026-09-01 — ENG-404/ENG-406 Search and tag invariant correction

- Search now requires the candidate target's zone to equal its typed source
  zone and its filter to be structurally equal to the instruction filter;
  contradictory hidden-zone programs are rejected before cataloging.
- `Tagged` and `NotTagged` are parameterless standard-tag conditions. Named
  tag keys/fact payloads are not part of the Gateway contract.
- Added mismatch and parameter-rejection regressions while retaining positive
  Corp Deck/HQ/Archives and Runner Stack search/add-to-Grip coverage. Focused
  Core build passed with zero warnings/errors, contract/catalog tests passed
  27/27, format verification passed, and `git diff --check` passed. Runtime
  execution and full Slice 4.2 verification remain pending.

### 2026-09-01 — MATCH-401B / DATA-401A bounded deck and preset contracts

- Added Core-owned, metadata-free `DeckRequest`/`DeckCardEntry` schema v1
  contracts and Match-owned `MatchRequest` schema v1 plus strict deterministic
  JSON parsing/writing. These contracts only describe composition inputs; they
  do not create cards, shuffle, deal, or execute a setup transition.
- Cards owns `DeckValidator` and the exact-path diagnostics. It checks side,
  known code, identity exclusion, identity minimum size and influence by copy,
  positive quantities, duplicate entries, three copies by printed name,
  printed deck limits, Corp agenda ranges (40–44: 18–19; 45–49: 20–21;
  50–54: 22–23; above 54: CR 1.4.6 increments), and authenticated metadata
  versions. Uniqueness and console traits are intentionally not construction
  limits. Tutorial identities `30076`/`30077` require an exact official
  starter or expanded card/quantity list.
- `DeveloperReduced` is an explicit developer-only profile. It can waive only
  minimum size and Corp agenda-range checks; side, known code, identity
  exclusion, behavior coverage, copy and printed limits remain enforced.
  Match user-facing composition rejects the profile at the deck path and also
  rejects composition when no behavior catalog is supplied. The behavior
  catalog check accepts registrations from every typed or generic family and
  reports missing compiled behavior independently of profile.
- Checked in `data/m4/presets.json` as code/quantity-only DATA-401A data: exact
  System Gateway basic Runner 30 / Corp 34 and expanded starter+booster Runner
  40 / Corp 44. All four remain `playable: false` until complete behavior
  coverage. Provenance pins the authenticated local metadata commit
  `236140cf84491c23995b028bc791281837520b97`, the published product list at
  `https://nullsignal.games/products/system-gateway/`, and agenda rules at
  `https://rules.nullsignal.games/`; no card text or vendored metadata is
  included. The parser checks schema, provenance, exact IDs/sides/identities,
  quantities, and expanded-superset relationships before metadata validation.
- Added focused invalid-class, exact-preset, reduced-profile, typed/generic
  behavior-gate, parser path, and user-playability tests. Default synthetic
  execution passed with the shared Core stream clean: Cards suite 64/64 and
  Match suite 29/29; the authenticated-content Cards build and suite also
  passed 64/64 with zero warnings/errors. No primary/full verification or CI
  result is claimed.

### 2026-09-01 — ENG-401 serializable CR 5 turn shell

- Added `TurnFrame`, `TurnDiscardFrame`, `TurnFactsState`, and
  `TurnHistoryState` in `src/Netrunner.Core/TurnContracts.cs`. The reducer in
  `GameEngine.Turn.cs` now models Corp allotment (3 clicks), mandatory draw,
  action/discard phases and Runner allotment (4 clicks), action/discard phases
  without a mandatory draw. Alternating `(P)/(R)/(S)` capability windows,
  formal `TurnStarted`/`TurnEnded` occurrences, forced hand-size discard,
  leftover-click loss, and explicit side-specific current/last-turn facts are
  persisted. `EndTurn` is never materialized while clicks remain.
- Extended `GameState.Turn` additively with `Step`/`History`; legacy M1/M2
  states continue to omit those default/null fields. This is a deliberate
  no-bump decision: state schema remains 2 and frame/event payload versions
  remain 1. Real turn frames require the explicit non-default cursor/history,
  so a missing real-turn field cannot silently become legacy behavior.
- Added typed turn timing/discard events and cloning/validation in
  `TypedEvents.cs`, typed SCN-400 schema/validation in `TurnScenario.cs`,
  stack/pending-decision invariants in `StateInvariantValidator.cs` and
  `Resolution.cs`, turn dispatch/session adapters in `GameEngine.cs`,
  `GameEngine.Advance.cs`, and `GameEngineSession.cs`, and observation-safe
  step/facts projection in `ObservationBuilder.cs`. `TurnTimingTrace.cs`
  provides CR citations and ordered evidence independent of card behavior;
  `EngineJson.cs` validates the fixture fail-closed.
- Added `data/m4/scenarios/scn-400-turn.json` (SCN-400, schema 1, CR 26.03
  citations, Jinteki.net cross-check note, ENG-401-only exclusions) and
  `TurnShellTests` covering positive/negative turn timing, mandatory/no-draw
  boundaries, forced discard and click loss, save/load/hash and observation
  filtering. Focused turn tests passed 9/9 before unrelated concurrent shared
  tree edits; no primary/full verification or CI result is claimed.
- Unresolved dependencies: ENG-402 must replace the minimal gain/draw action
  set with the full basic-action catalog and connect direct-effect action-phase
  termination; MATCH/setup must enter this turn shell after mulligan. The
  shared tree currently also has unrelated in-progress validator/test syntax
  and analyzer errors, so integration/full verification remains pending.

Architect follow-up corrections (ENG-401): startup now exposes
`GameEngine.TurnStartResult`/`BeginTurnWithEvents` plus injected-catalog session
adapters, and the turn tests consume the actual startup event batch rather than
reconstructing `LastBeginEvents` from state. Formal `TurnBegin`/`TurnEnd`
boundaries are serializable `OccurrenceKind` values routed through the existing
effect checkpoint/trigger machinery; a boundary checkpoint is tied by state
invariants to its matching turn frame, side, and exact turn number, while
free-standing public boundary resolutions are rejected. Effect-imminent and
effect-resolved wildcard conditions no longer match turn-boundary occurrences.
The trace validator now requires click allotment, ordered paid windows and
passes, formal phase boundaries, Corp's mandatory draw (and no Runner
mandatory draw), discard/click-loss, and formal turn end; SCN-400 evidence runs
a real Corp+Runner pair and round-trips every yielded state/hash. Click-loss
facts intentionally allow loss above the baseline allotment for granted-click
forced boundaries. Focused `TurnShellTests` passed 15/15 with zero warnings or
errors; runtime action expansion and full verification remain pending.

### 2026-09-01 — Slice 4.2 Batch-1 Cards behavior registrations

- Added the focused `GatewayBatch1BehaviorSpecs` Cards module and composed its
  ten complete generic declarations through `GatewayBehaviorCatalog.Create`.
  The declarations cover the ordinary economy/draw cards, Corp-owned Wildcat
  and Planogram choices (including the tagged both/order branch), Sprint and
  Hansei hidden HQ choices, conditional click loss, and T400's two static
  modifiers. The module carries all twelve assignment IDs and an explicit
  blocked-ID list; it does not alter legacy `RegisterAll` registrations.
- Two assigned designs remain intentionally outside `AllDefinitionIds`, the
  authoritative behavior gate: Mutual Favor (30011) needs a serializable
  hidden search set-aside/reveal/shuffle boundary before its install-or-grip
  branch can be executable, and Seamless Launch (30040) needs a typed target
  predicate excluding cards installed during the current turn. No title branch,
  invented fact, or approximate target was added for either gap.
- Provenance is the authenticated local System Gateway snapshot identified by
  `data/vendor/system-gateway/manifest.json`, with CR-26.03 citations recorded
  in the module comments and corresponding audit rows in `docs/M4_PRIMITIVES.md`.
  No card text or vendored metadata was copied into the registrations.
- Focused evidence: Cards build passed with zero warnings/errors; the Cards
  suite passed 69/69; the Core behavior-contract/catalog filter passed 27/27;
  formatting verification and `git diff --check` passed. No primary/full
  verification or CI result is claimed. The two blocked designs remain the
  next Core contract/runtime dependency before the Batch-1 behavior gate can
  report all twelve as executable.

### 2026-09-01 — Foundation-wave architect integration gate

- Primary review accepted ENG-401 after two correction rounds. Startup events
  are returned rather than reconstructed, SCN-400 drives an actual full
  Corp/Runner turn pair with a save/load/hash check at every yield, formal
  begin/end occurrences use the existing checkpoint registry, and boundary
  effects are fail-closed to their exact parent turn cursor. Generic effect
  wildcard conditions cannot observe turn-boundary occurrences.
- Primary review accepted MATCH-401B/DATA-401A after correcting typed-catalog
  coverage, Corp influence enforcement, duplicate diagnostics, and the
  user-facing null-catalog gate. The four code/quantity preset lists were also
  compared with Null Signal Games' published System Gateway starter/booster
  lists; their 30/34/40/44 totals and quantities match the published source.
- Primary review accepted the declarative ENG-404/ENG-406 contract foundation
  and ten representable Batch-1 declarations. Mutual Favor and Seamless Launch
  remain deliberately outside `AllDefinitionIds` until the next runtime wave
  supplies the serialized set-aside boundary and installed-not-this-turn
  target predicate; they are not counted as registered or complete.
- Independent integrated evidence after all corrections: `dotnet build
  Netrunner.sln --no-restore -m:1` passed with zero warnings/errors; the full
  solution test run passed 456/456 (Core 297, Cards 69, AI 9, CLI 52, Match
  29); `dotnet format Netrunner.sln --no-restore --verify-no-changes` and
  `git diff --check` passed. This is a foundation-wave gate, not final Slice
  4.2 verification or CI evidence.

### 2026-09-01 — SIM-401 developer fixture contract/data foundation

- Added the Match-owned `SimulationFixtureDocument`, strict JSON parser/writer,
  explicit-path provider, and duplicate-detecting `SimulationFixtureCatalog`.
  The version-1 contract carries rules/card-data/behavior registry pins,
  SHA-256 behavior fingerprint, deterministic seed, bounded action cap,
  optional per-side action ordering, and an ongoing/won outcome expectation.
  Parsing and structural validation never construct `GameState`, shuffle,
  deal, or enter setup; MATCH-401A/AGT-401 remain the future consumer.
- Checked in `data/m4/sim/basic-system-gateway.json` and
  `data/m4/sim/expanded-system-gateway.json`. Each is code/quantity-only,
  uses the explicit `DeveloperReduced` profile for both sides, and contains a
  bespoke reduced deck over the seven M2 baseline registrations plus all
  twelve Slice 4.2 Batch-1 assignment codes. They intentionally omit
  `presetId`: neither reduced list is an exact official 30/34/40/44 preset.
  The fixtures pin rules `26.03`, metadata commit
  `236140cf84491c23995b028bc791281837520b97`, behavior registry
  `gateway-m2-3`, and the current catalog fingerprint
  `609d83418823cd629d6b763cd3a2b9ab401cb39cadfa5a23bb237bd800d3a063`.
  That fingerprint is provisional for the ten complete declarations and must
  be refreshed only when Mutual Favor and Seamless Launch complete their
  required Core predicates. Provenance cites D-405/CR-26.03 and records the
  no-headless-outcome status; no card text or vendored metadata is checked in.
- Validation is fail-closed for schema/version/profile/pin/side/deck/preset,
  duplicate fixture/card/action IDs, invalid JSON shape, action-cap bounds,
  and outcome fields. The execution-bound overload requires both an
  authenticated `CardDataSet` and behavior catalog before any future runner
  can proceed; it does not waive unknown cards or missing behavior for
  `DeveloperReduced`.
- Focused evidence: owned Match source and test projects build cleanly with
  zero warnings/errors; the complete Match test project passed 35/35 and the
  SIM-401 filter passed 6/6 after covering allowed-code reduced decks and stable
  serialization, parser unknown/missing/duplicate/side paths, all pins,
  reduced-profile and deck/preset failures, duplicate fixtures, action caps,
  outcome shape, and fail-closed metadata/catalog validation. Full Match
  verification remains pending the concurrent Core ENG-404/406 compile lane
  (the normal dependency build currently stops on Core `CA1512` in
  `BehaviorRuntimeContracts.cs`, outside this stream);
  the future execution gate also needs MATCH-401A/deck-validation's explicit
  no-gameplay-behavior capability for tutorial identities (the current blanket
  identity behavior check rejects these intentionally ability-less identities)
  and the Mutual Favor/Seamless Launch Core work. No primary/full Slice 4.2
  verification or headless exit proof is claimed.

- Architect correction recorded: the fixtures use tutorial identities `30077`
  and `30076` without `presetId` because the bespoke lists are not official
  presets; active Slice 4.7 identities would make this Slice 4.2 fixture
  knowingly depend on later identity abilities. The behavior fingerprint
  remains provisional and must be regenerated only after all twelve Batch-1
  declarations are executable.

### 2026-09-01 — AGT-401 observation-only v1 takeover

- Took over the interrupted `FullGameContractAgent` foundation. The owned
  implementation is limited to `FullGameContractAgent` v1 and its immutable,
  versioned `FullGameContractPolicy`; `RunContractAgent` remains untouched as
  the M2 timing anchor. The public agent API accepts only the Core
  `Observation` boundary (viewer-owned `ObservationDecision` metadata plus
  visible `LegalAction` values) and returns an existing engine-issued
  `ActionId`. It has no `GameState`, catalog, RNG, or card-data dependency.
- The implemented v1 dispatch subset is exactly the currently materialized
  Slice-4.2 surface: `TurnTiming` (`PassPriority`), `TurnAction`
  (`GainCredit`/`DrawCard`), `TurnDiscard` (`ChooseCard`), `BehaviorChoice`
  (`ChooseBehavior` option/target payloads), and M2 `RunTiming`
  (`PassPriority`, `RezIce`, `BreakSubroutines`, `PumpBreaker`,
  `ChooseBreachCandidate`, `TrashAccessedCard`, `NoAccessAction`,
  `ContinueRun`, `JackOut`). Empty, malformed, unsupported, wrong-viewer, and
  mismatched-decision metadata fail closed. `EndTurn` is never selected (and
  is not a v1 policy priority); ENG-401's zero-click reducer remains the
  authority for action-phase timing.
- Selection uses typed payloads only. Gain is the deterministic economy
  preference when a behavior mode offers `gain`; card/target/payment payloads
  are ordered by semantic typed keys and then stable `ActionId`, independent
  of legal-action list order. Action descriptions/prompts and observation
  public/private dictionaries are never consulted; hidden parameters are only
  consumed when Core has materialized them in the viewer's legal-action
  surface. The decision-kind switch leaves explicit fail-closed extension
  points for future setup/ENG-402/403/405 action classes without claiming that
  those classes are implemented here.
- Added 23 AI contract tests covering schema/policy immutability, controlled
  owner and decision metadata, cancellation, pass/timing and no-`EndTurn`,
  gain/draw preference, deterministic visible discard and behavior targets,
  every currently materialized M2 run action, typed payload validation,
  unsupported/empty/future decisions, description neutrality, hidden-state
  non-access, legal-action reorder stability, stable-ID ties, and typed
  legal-action serialization/replay neutrality.
- Evidence using `source scripts/env.sh`: AI and AI-test projects build cleanly
  with zero warnings/errors against the already-built Core binary via
  `-p:BuildProjectReferences=false`; the AI test project passes 23/23; both
  AI-owned `dotnet format ... --no-restore --verify-no-changes` checks and
  `git diff --check` pass. The normal dependency build is currently blocked by
  concurrent out-of-scope Core edits (`GameEngine.Behavior.cs` and
  `Resolution.cs`, 18 compile errors); no full-solution or primary
  verification result is claimed.
- Blockers and explicit non-claims: v1 does not implement setup/mulligan,
  full-turn action execution, run initiation, install/play/rez, advance/score,
  tag removal/resource trash/purge, endgame policy, or a headless full-match
  proof. Those require the pending ENG-402/403/405 and MATCH-401A/setup work;
  AGT-401 is not complete and Slice-4.2 completion is not claimed.

### 2026-09-01 — UI-403 bounded observation/presentation foundation

- Added a Match-owned UI-403 projection over the existing filtered
  `Observation`: explicit public turn (`activeSide`, `phase`, `turnNumber`,
  optional `step`), active player clicks/credits through the existing player
  view, viewer-safe hand counts, visible installed-card advancement counters,
  score-area cards/agenda points, and a forced-discard view when Core issues
  `DecisionKind.TurnDiscard`. Existing metadata resolver injection, hidden-card
  nulls, and typed action parameters remain unchanged.
- Maximum hand size is intentionally nullable. Core's current observation
  contract does not project `RulesState.MaximumHandSize`; the Match model emits
  the exact dependency diagnostic and accepts an optional injected
  `HandSizeProjection` for a future viewer-safe Core/Match projection. No
  default hand size or card-text/rules inference is used.
- Added `TurnRegionPresenter` for text turn/phase/timing/priority status with
  unknown/malformed enum values rendered unavailable, and progressive
  `ActionsRegionPresenter` groups keyed by active `DecisionKind`. The current
  display subset is `TurnTiming` pass priority, `TurnAction`
  gain/draw/issued behavior choice, `TurnDiscard` card choices,
  `BehaviorChoice`, generic card/number compatibility choices, and the
  currently materialized M2 `RunTiming` action classes. Godot renders only
  these Core-issued actions, keeps target staging ID-only, and shows forced
  discard text cues; it creates no setup/mulligan/install/run-initiation
  actions and does not parse descriptions or card text.
- Added Match tests for turn/hand/score/counter mappings, injected hand-size
  values and unavailable diagnostics, forced-discard presentation, decision
  progressive disclosure, hidden-state-safe visible counters, focus behavior,
  and unknown turn/decision/run enum fail-closed handling. Godot uses the
  existing M3 scene and smoke paths; no UI-403 full-game smoke claim is made.
- Evidence using `source scripts/env.sh`: owned Match build with
  `-p:BuildProjectReferences=false` (0 warnings/errors), Match tests 40/40,
  Match/Godot targeted format verification and `git diff --check` pass;
  Godot build with `-p:BuildProjectReferences=false` (0 warnings/errors);
  existing `--smoke-test` reports `M3_SMOKE PASS`, and
  `--m3-responsiveness-smoke` reports `M3_LATENCY_SMOKE PASS` with 10-second
  agent delay, one-frame interaction checks, and both viewport layouts.
  Dependency builds remain blocked by concurrent out-of-scope Core analyzer
  edits; no primary/full UI-403 or Slice-4.2 claim is made.

### 2026-09-01 — ENG-404/ENG-406 behavior runtime takeover and architect corrections

- Took over the interrupted Core runtime/classification stream after restoring
  compile integrity. `BehaviorCatalog` now treats `TryGetCardType` metadata as
  an adjunct only: classification keys are excluded from `AllDefinitionIds`,
  and `Build` fails closed when a classification has no corresponding typed or
  generic executable definition. Equivalent catalog construction, slices,
  fingerprints, and classification-only regression tests prove deterministic
  behavior coverage cannot be satisfied by metadata alone.
- Generic behavior invocation is catalog-driven and engine-owned. Public
  `BeginBehavior` session/static entry points were removed; Core keeps only the
  internal engine composition seam plus an explicitly friend-visible,
  non-production fixture seam. The private `WorkingState` child push/resume
  path binds controller, acting player, source card/zone, immediate parent ID,
  and exact serialized parent-frame identity. Deck, heap, discard, score, and
  arbitrary installed/hand activation are rejected at the boundary as
  applicable; future ENG-405 play/install reducers remain the production
  authorization owner.
- Persisted frames contain catalog/definition fingerprints, cursor and a
  catalog-derived continuation path, bindings, choice materialization, and
  physical transient set-aside metadata. Executable instruction lists remain
  non-serialized projections. Validation replays the injected catalog and
  rejects mismatched catalogs, forged continuations/bindings, stale physical
  candidates, parent identity changes, and invalid source/set-aside locations
  before mutation. Nested mode/target/branch choices preserve chooser
  ownership; exact/up-to/any-number semantics use a bounded incremental
  protocol rather than exponential `LegalAction` enumeration, including
  mandatory-empty completion.
- ENG-404 search physically moves selected cards to `BehaviorSetAside` before
  reveal, emits typed reveal evidence without changing hidden cards to
  `FaceUp`, excludes selected cards from the deterministic shuffle, then
  reinserts them while retaining the `chosen` binding for a later grip/install
  disposition. HQ/Archives add/trash and HQ/Archives-to-deck shuffle paths
  include opponent-observation leak coverage. Corp empty required draw ends in
  Runner deck-out; Runner empty draw consumes no card and permits later effects
  to resolve. Static memory/hand-size modifiers are projected from active
  installed cards and disappear after trash/deactivation instead of mutating
  base values or stacking on repeated activation.
- Turn facts now mark actual installs and successful runs, reset at formal turn
  boundaries, and are observation-filtered. `InstalledCardNotThisTurn` is a
  typed target predicate feeding `ChosenCard`, not an implicit effect over all
  installed cards. Generic advancement emits the honest
  `BehaviorCardAdvancedEventPayload` (card, amount, resulting counter); the
  legacy `AgendaAdvanced` payload remains reserved for rules that know the
  printed advancement requirement.
- Focused Core evidence after the takeover: Core build is warning/error-free;
  the behavior runtime filter passes 51/51, including provenance/zone/parent
  nesting, catalog mismatch and forged/stale continuation/candidate rejection,
  bounded choice round-trips, set-aside/reveal/shuffle/binding and observation
  checks, draw/deck-out, static lifecycle, turn-fact transitions, typed
  advancement, and JSON/hash yields. Formatting verification and
  `git diff --check` are run on the owned Core scope. This is focused evidence,
  not a primary/full Slice-4.2 or CI claim.
- Remaining dependency: ENG-405 must own full CR 8.5/8.6 install/play/rez
  legality, printed costs, uniqueness/console/memory checks, destination and
  play-area lifecycle, and the authorized production handoff into the private
  behavior child seam. Generic `InstallCard` in the runtime tests is an
  explicitly provisional binding primitive and does not register or expose a
  zero-cost public install path; Cards registrations, Match/SIM, AI, and UI
  remain outside this takeover.

### 2026-09-01 — primary integration gate after ENG-404/406 and UI/agent foundations

- The primary architect reviewed the finalized behavior-runtime persistence,
  invocation, choice, set-aside, static-projection, turn-fact, and typed-event
  seams and accepted the bounded ENG-404/406 gate. The full solution dependency
  build passes with zero warnings/errors; the complete Core suite passes
  319/319, including the 51-test behavior filter.
- The previously provisional observation-only agent and UI foundations were
  rechecked against the integrated Core output: AI passes 23/23 and Match
  passes 40/40. The primary full-solution no-build regression gate passes
  503/503 (Core 319, Cards 69, AI 23, CLI 52, Match 40). These counts validate
  the currently materialized surfaces only;
  AGT-401/UI-403 remain incomplete until ENG-402/403/405 and MATCH-401A expose
  setup, install/play/rez, scoring, and base-run actions.
- Started the Luna Max Core ENG-402/403/405 stream under primary architectural
  review. Its contract requires typed fingerprinted runtime-card metadata that
  cannot inflate behavior coverage, engine-issued/replayable basic actions and
  install destinations, atomic printed/click costs, full install/play/rez
  lifecycle, M2 run nesting, score/win resolution, viewer-safe maximum-hand
  projection, and focused compatibility evidence. Cards/Match/AI/UI registration
  and presentation changes remain outside that stream.

### 2026-09-01 — authenticated Batch-1 metadata/declaration audit

- A read-only Luna Max audit checked all twelve Batch-1 designs and the seven
  existing registrations against the authenticated local System Gateway
  snapshot at commit `236140cf84491c23995b028bc791281837520b97` / output SHA
  `a190e6e63a6d58734d915258ebb6b5659ffef0e253fc4c5128776256ee3d699b`.
  No files were changed by the audit. Exact printed costs are: Corp operations
  30064=10, 30075=5, 30041=0, 30048=5, 30056=0, 30040=1; Runner events
  30030=5, 30002=2, 30020=1, 30021=1, 30011=0; hardware 30031 install=2.
  T400 is Chip, non-unique/non-console, with +1 MU/+1 maximum hand size.
  Baseline runtime metadata is 30005 program 4/MU1, 30006 program 3/MU1,
  30029 event 1, 30037 asset rez2/trash2, 30067 agenda 4/2, 30072 ICE rez3,
  and 30074 ICE rez2.
- The audit confirmed ten current generic declarations structurally match
  their printed effects except for two required runtime corrections: Predictive
  Planogram's tagged condition must explicitly inspect the Runner rather than
  the Corp controller, and Hansei Review's mandatory HQ trash must resolve
  without a prompt when HQ is empty. Creative Commission/VRcation must evaluate
  their conditional click loss after the play click has been paid.
- Mutual Favor and Seamless Launch remain correctly blocked. Mutual needs typed
  Icebreaker filtering, retained search/reveal/shuffle binding, Runner-scoped
  successful-run-this-turn, optional legal install, and Grip fallback through
  ENG-405. Seamless needs an owner-filtered installed-Corp-card-not-this-turn
  target and two advancement counters. Printed play/install costs must have one
  authoritative owner: the action pipeline, never duplicated by behavior costs;
  T400 static modifiers likewise never constitute an activation cost/effect.
- Coverage expectations are now explicit: the worktree catalog currently has
  17 definitions but checked-in coverage still reports 7 registered/tested,
  5 complete, 2 partial. The completed batch must reach 19 registered/tested,
  17 complete, 2 partial, 58 unregistered. The provisional behavior fingerprint
  `609d83418823cd629d6b763cd3a2b9ab401cb39cadfa5a23bb237bd800d3a063`
  must be recomputed once after 30011/30040 land and both SIM pins refreshed;
  existing M2 definition fingerprints/traces remain pinned.

### 2026-09-01 — Core ENG-402/403/405 Phase-A metadata and observation checkpoint

- The stream was narrowed by primary review to Phase A. Core now has one
  canonical immutable `CardRuntimeSpec` type and `RuntimeCardCatalog` adjunct,
  keyed only by `CardDefinitionId`, with deterministic sorted fingerprinting.
  Runtime metadata contains typed class/side/cost/MU/trash/agenda/subtype and
  uniqueness/console fields, performs class-shape validation, and never enters
  `BehaviorCatalog.AllDefinitionIds`; no raw-ID or title/text inference path was
  added. Identity is valid for either side; Corp agenda/asset/upgrade/ICE
  install cost is the rules-owned zero-cost path (no printed install field),
  Runner program/hardware/resource require printed install cost, play costs are
  limited to operation/event, rez costs to asset/upgrade/ICE, and printed trash
  cost is limited to Corp asset/upgrade.
- Runtime catalog version/fingerprint are optional state/snapshot metadata, so
  M1/M2 states retain their prior JSON/hash shape. The injected catalog has
  explicit state compatibility validation and round-trip/hash coverage;
  runtime IDs remain independent of executable behavior coverage.
- `ObservationBuilder.Build(state, viewer, behaviorCatalog)` now projects an
  effective viewer-safe `maximumHandSize` only when the optional rules
  aggregate exists. The value derives from base hand size/core damage plus
  active `BehaviorStaticModifierProjection`; base state is never mutated and
  the legacy two-argument observation adapter/goldens remain unchanged.
- Architect review rejected speculative public transaction shells and unused
  action DTOs in this metadata-only checkpoint. Install/play/rez reducers,
  serializable paid handoff, no-double-pay behavior invocation, typed action
  enumeration/consumption, and nested base-run resume remain explicitly
  deferred to the next reviewed Phase-B ENG-402/403/405 stream. In particular,
  Cards registration must continue to pass printed costs through one future
  authoritative Core action boundary; current generic behavior costs were not
  silently changed.
- Focused evidence: `dotnet build
  src/Netrunner.Core/Netrunner.Core.csproj --no-restore -m:1` passed with zero
  warnings/errors; `dotnet test
  tests/Netrunner.Core.Tests/Netrunner.Core.Tests.csproj --no-restore
  --filter FullyQualifiedName~RuntimeCardCatalogTests -m:1` passed 9/9 (the
  first sandbox testhost attempt was permission-denied before rerunning with
  the approved testhost socket); the complete Core suite then passed 328/328.
  Core formatter verification passed for the touched source/test files and
  `git diff --check` passed. Full/primary solution claims are not made by this
  Phase-A checkpoint.

### 2026-09-01 — Phase-A independent review corrections assigned

- Primary and independent Luna Max review accepted the typed metadata and
  observation model but rejected four persistence/composition gaps before
  Phase B: runtime version/fingerprint were not enforced both-or-neither;
  session BeginResolution/BeginTurn/BeginRun boundaries did not thread or check
  their bound runtime catalog; `Scenario.AddCards` and `ToyRules.Expand` could
  drop runtime pins while reconstructing state; and EngineJson had no atomic
  behavior+runtime-catalog deserialize boundary. Subtype input order also needs
  ordinal canonicalization before fingerprinting.
- A bounded correction pass is in progress with required wrong-catalog checks
  at every session boundary, asymmetric-pin rejection, clone/save-load
  regressions, equivalent-subtype-order fingerprint coverage, full Core
  regression/format evidence, and no ENG-402/403/405 Phase-B reducer work.

### 2026-09-01 — Phase-A independent review correction pass completed

- Runtime-card state identity is now symmetric: version and fingerprint are
  accepted only as a pair, or both omitted for legacy M1/M2 states. The
  invariant validator reports the same incomplete-pin violation for either
  asymmetry; both directions are covered by `RuntimeCardCatalogTests`.
- `GameEngineSession` and its `EngineSession` compatibility alias retain one
  optional immutable runtime catalog and pass it through Apply,
  BeginResolution, BeginTurn/BeginTurnWithEvents, and
  BeginRun/BeginRunWithEvents. Each injected setup boundary checks the runtime
  pin before terminal/pending/turn/server/source legality or working-state
  construction, so a wrong pinned catalog cannot mutate or advance state.
  Existing behavior-only and legacy static overloads remain available.
- All owned Core reconstruction paths preserve both runtime pins, including
  `ScenarioBuilder.AddCards`, `ToyGameSetup` expansion, engine working-state
  builds, and snapshot conversion. Explicit regression tests exercise the
  Scenario and Toy helpers through internal `ForTesting` seams and verify
  version, fingerprint, serialized equality, and state-hash equality after
  reconstruction.
- `EngineJson` now exposes dual behavior/runtime catalog validation for generic
  string and UTF-8 deserialization plus `DeserializeState` and
  `DeserializeSnapshot`. Validation is performed at the atomic restore
  boundary and wrong runtime catalogs fail closed; no runtime-only nullable
  overload was added, avoiding ambiguity with the legacy behavior overload.
- `CardRuntimeSpec` canonicalizes subtype values by ordinal distinctness and
  ordinal sort before catalog fingerprinting. A reorder/duplicate semantic
  equivalence regression test proves the fingerprint is stable.
- Evidence from this correction pass: Core build completed with 0 warnings and
  0 errors; focused `RuntimeCardCatalogTests` passed 16/16; the complete Core
  suite passed 335/335; Core and Core-test `dotnet format --verify-no-changes`
  passed; and `git diff --check` passed.
- Nonclaims: ENG-402/403/405 Phase B remains deferred. This pass adds no
  install/play/rez reducers, transaction/action shells, or broader turn/run
  lifecycle work, and makes no full-solution, CI, Cards, Match, AI, or Godot
  verification claim. Existing unrelated worktree changes remain outside this
  correction scope.

### 2026-09-01 — Phase-B architecture audit and B1 install stream

- A read-only Luna Max audit mapped the safe Phase-B integration around the
  real `TurnFrame`, runtime catalog, typed pending actions, serialized child
  frames, and existing M2 run reducer. It explicitly rejects routing real
  actions through toy reducers, caller-authored costs/targets, public arbitrary
  card reducers, executable serialized instructions, or title/text parsing.
  The recommended sequence is install, immediate/scoring/rez actions, play
  lifecycle with prepaid behavior provenance, then nested M2 base runs.
- Started a bounded Luna Max B1 stream for authoritative basic installs only.
  Primary review corrected the initial flattened-choice contract against CR
  26.03 rules 3.9.3b and 8.5.6: an over-memory Program install remains possible
  by first choosing installed Programs to trash; Corp root/ICE installs may
  first trash any number of like-location cards, with mandatory agenda/asset
  and Region constraints; ICE install cost counts protectors remaining after
  selected trash. B1 therefore requires a serialized, bounded incremental
  install frame/selection protocol with save/load/hash yields, rather than
  exponential action materialization or simply hiding over-memory installs.
- Play/rez/run/score/tag/purge remain outside B1. The primary architect will
  review B1 state transitions, costs, hidden visibility, remote topology,
  invariants, and compatibility before authorizing the next phase.

### 2026-09-01 — Phase-B B1a takeover and compile audit

- The first B1 implementer exhausted its Luna Max run after leaving an
  incomplete mixed Runner/Corp install draft. Primary review narrowed the
  recovery to Runner Grip-to-Rig installs only; Corp server destinations,
  replacement topology, remote creation, and ICE cost calculation remain
  unclaimed and deferred to a separate B1b stream.
- A first recovery assignment was stopped after it produced no edits or status.
  The work was reassigned as smaller Luna Max checkpoints, beginning with
  compile repair and exact TurnAction projection before the mandatory bounded
  Program-memory selection pass.
- Primary compile evidence currently fails with four known inherited errors:
  missing `IsInstallFrameTop`, one nullable-value diagnostic, and two CA1859
  return-type diagnostics. Primary architecture review also found that
  `YieldTurnActionDecision` does not yet issue install actions and
  `ValidateTurnActions` still rejects every typed install action because it
  assumes all turn actions use `NoActionParameters`. B1a therefore remains in
  progress and no install behavior or test-pass claim is made at this point.

### 2026-09-01 — Phase-B B1a compile/projection checkpoint

- Repaired the inherited Runner-only install path: `IsInstallFrameTop` is now
  available to action dispatch, nullable server lookup and CA1859 array
  diagnostics are resolved, and `BuildInstallPlan` looks up the physical card
  directly in Runner Grip. `YieldTurnActionDecision` and `AddInstallActions`
  both gate issuance on positive clicks; Corp install issuance remains disabled
  in this slice. `ConsumeTurnInstallAction` now requires a real AwaitingAction
  Runner frame and checks the card owner/source before constructing the frozen
  install frame.
- Kept one canonical `InstallCardParameters` constructor vocabulary and wired
  exact materializer/validator parity: Runner TurnAction installs must match
  the current state, injected runtime catalog, destination, printed cost, and
  frozen replacement fields; arbitrary typed install payloads are rejected.
- Added `RunnerInstallActionTests.cs`, including a legal non-program hardware
  install that round-trips through JSON, spends exactly one click and its
  printed two-credit cost, moves Grip to face-up Rig, marks the turn fact, and
  resumes the action-priority window, plus an unaffordable resource
  non-issuance check. Evidence using `source scripts/env.sh`: Core build passes
  with 0 warnings/errors; the focused Runner install filter passes 2/2; the
  combined Runner-install/turn-shell focus passes 17/17; primary review's full
  Core regression passes 337/337; and `git diff --check` passes.
- B1a remains IN PROGRESS. The bounded memory-over-limit selection protocol
  (including incremental `InstallFrame` replacement choices), its dedicated
  tests, and broader save/load/hash negative coverage remain to be completed;
  Corp destinations, play/rez, and any wider ENG-405 behavior are out of scope.

### 2026-09-01 — Phase-B B1a Program-memory stream assigned

- Started the second bounded Luna Max implementation checkpoint on top of the
  accepted non-Program projection: runtime-MU registration, incremental
  installed-Program trash selection, effective-memory static modifiers,
  owner-only observation, forged/stale rejection, and pending-frame
  save/load/hash coverage. A separate Luna Max agent is independently reviewing
  the compile/projection checkpoint while this implementation proceeds.
- Acceptance requires focused and full Core regression evidence plus formatter
  and diff hygiene. Corp installs remain disabled and are not part of this
  assignment.
- The first broad Program-memory implementation and broad independent-review
  assignments were stopped after extended runs produced neither edits nor a
  status response. They made no worktree changes. The stream was split again:
  one Luna Max checkpoint now covers only a Program that fits under MU plus
  safe RulesState initialization, and a separate fast read-only Luna Max review
  covers the accepted non-Program action boundary. Over-MU selection remains a
  subsequent checkpoint.

### 2026-09-01 — Phase-B B1a Program-under-MU checkpoint in progress

- Added the bounded Runner Program install path on top of the accepted
  Grip-to-Rig action boundary: printed install cost and one-click payment,
  face-up Rig movement, runtime memory-unit registration, `UsedMemory`
  recalculation, typed memory event emission, and return to action priority.
  Existing `RulesState` aggregates remain authoritative; when the aggregate is
  absent, initialization reconstructs all runtime-known Runner Programs already
  in the Rig before registering the incoming Program, preventing undercount.
- Focused verification passes the five `RunnerInstallActionTests` (including
  the Program-under-MU, pre-existing-Rig/null-`RulesState`, and unknown-Rig
  fail-closed regressions), and the Core build passes with 0 warnings/errors.
  Primary review's full Core regression passes 340/340. This remains an
  in-progress bounded result; no formatter claim is made here.
- Remaining in B1a is the explicit incremental over-MU selection protocol and
  its save/load/hash and forged/stale-action coverage. Corp destinations,
  play/rez, and broader ENG-405 behavior remain out of scope.

### 2026-09-01 — Phase-B B1a install-frame invariant review correction

- Closed the review gap where a structurally self-consistent Corp parent,
  Corp-owned pending InstallChoice, and Corp install frame could pass
  `StateInvariantValidator` even though the reducer was Runner-only. Install
  frames now require Runner ownership and parent side, the canonical Runner
  Grip source, Runner Rig destination with no server/new-remote marker, and a
  Runner Program/Hardware/Resource class when the runtime catalog is injected.
- The materialized Runner frame now captures `DestinationZoneId`; state/frame
  cloning preserves it, and invariants reject a missing or stale Rig identity.
  `RunnerInstallActionTests` covers the forged Corp frame/pending decision with
  actionable Runner-only rejection plus snapshot clone preservation.
- Focused review-correction evidence passes 6/6 Runner install tests, Core
  build with 0 warnings/errors, and `git diff --check`.

### 2026-09-01 — Phase-B B1a over-MU selection checkpoint assigned

- Started a final bounded Luna Max B1a implementation pass for incremental
  over-memory Program replacement selection, finalization-only payment,
  save/load/hash and owner-only observation, stale/forged rejection, linear
  action cardinality, and incoming/lost static MU modifiers. Corp installs
  remain disabled. Full-suite, formatter, and final independent-review claims
  wait until this checkpoint is complete.
- Focused evidence: `RunnerInstallActionTests` passes 6/6; the Core project
  builds with zero warnings/errors; and `git diff --check` passes.

### 2026-09-01 — Phase-B B1a over-MU selection checkpoint completed

- Completed the bounded Runner over-memory Program protocol. A TurnAction
  opens a serialized `InstallFrame`/`InstallChoice` without movement or
  payment; each continuation appends exactly one remaining frozen candidate,
  with at most one finalize action once the selected set fits. Finalization
  re-resolves the destination after replacement moves, trashes selected Rig
  Programs to Heap, unregisters them, installs the incoming Grip card face-up,
  pays its printed runtime cost and one click once, recalculates runtime MU,
  emits typed movement/payment/memory/install events, and resumes priority.
  Runtime MU registrations are now checked against the injected metadata
  catalog, and missing installed Program registrations fail closed.
- Added focused coverage for linear cardinality, pending-frame dual-catalog
  JSON/hash preservation, Runner-only observations, stale decisions and
  destination/resource rejection, incoming active static +1 MU, and loss of
  an active +MU Program's modifier when selected for trash. Corp installs
  remain unreachable.
- Evidence: `RunnerInstallActionTests` passed 10/10; the combined
  Runner-install/TurnShell focus passed 25/25; the Core project built with
  zero warnings/errors; and `git diff --check` passed. Full-suite execution,
  formatter verification, and final independent review remain pending; Corp
  destinations, play/rez, and broader ENG-405 behavior remain out of scope.

### 2026-09-01 — Phase-B B1a unknown-Rig metadata fail-closed correction

- Root finding: `BuildInstallPlan` previously rejected an incoming Runner
  Program when an existing Rig card lacked runtime metadata only while
  `RulesState` was null. With a non-null internally consistent aggregate, the
  unknown card was skipped by Program registration and `CurrentUsedMemory`,
  allowing a Program install to undercount memory if that card was actually a
  Program. Runtime metadata is authoritative, so this boundary must fail
  closed independently of `RulesState` presence.
- Corrected the shared install planner so every incoming Runner Program checks
  that all current Rig cards resolve in `RuntimeCardCatalog`; Hardware and
  Resource installation behavior is unchanged. Initial issuance, materialized
  turn-action parity, and install-frame revalidation all use this same planner
  check.
- Added the focused non-null-`RulesState` regression with a valid default
  aggregate, an unknown face-up Rig card omitted from registrations, and a
  known incoming Program; the Runner turn still opens but no `InstallCard`
  action is offered. The existing null-`RulesState` regression remains.

### 2026-09-01 — Phase-B B1a exit review and unknown-Rig correction

- Primary exit verification passes the complete Core suite 345/345, Core and
  Core-test formatter verification, and `git diff --check`. A broad final Luna
  Max review was stopped after producing no output; a narrowed independent
  Luna Max review found no defect in the registration/commit path and confirmed
  that injected runtime metadata now enforces exact MU plus one registration
  for every runtime-known installed Program.
- Primary follow-up found a narrower fail-closed gap outside that review: an
  unknown Rig card blocked Program installation only when `RulesState` was
  null. With a non-null aggregate, the card could not be classified and might
  be skipped by memory accounting. The shared `BuildInstallPlan` boundary now
  blocks every incoming Runner Program whenever any Rig card lacks runtime
  metadata, regardless of aggregate presence; Hardware and Resource behavior
  is unchanged, and frame revalidation reuses the same check.
- Added the non-null-`RulesState` regression with a valid default aggregate,
  an unknown face-up Rig card omitted from registrations, and a known incoming
  Program. The Runner turn opens but no `InstallCard` action is offered; the
  existing null-`RulesState` regression remains. Focused evidence passes
  `RunnerInstallActionTests` 11/11, the Core build with zero warnings/errors,
  and `git diff --check`.

### 2026-09-01 — Phase-B B1a Runner install accepted

- Primary architecture review accepts the Runner Grip-to-Rig install boundary:
  engine-issued typed actions, printed runtime cost and MU, Program memory
  registration, bounded incremental replacement selection, frozen physical
  Rig identity, owner-only observation, dual-catalog save/load/hash,
  finalization-only payment, active static-MU handling, and stale/forged
  rejection. The stale destination object defect and both independent/root
  fail-closed findings were corrected before acceptance.
- Final evidence after the unknown-Rig correction: focused
  `RunnerInstallActionTests` passes 11/11; combined Runner-install/TurnShell
  focus passes 26/26; complete Core regression passes 346/346; Core and
  Core-test `dotnet format --verify-no-changes` passes; Core build has zero
  warnings/errors; and `git diff --check` passes.
- ENG-405 is not complete. Corp root installs and new remotes move to B1b;
  Corp ICE installs/dynamic protector cost remain a separate checkpoint.
  Play/rez/run/score/tag/purge remain outside B1.

### 2026-09-02 — Phase-B B1b Corp-root stream assigned

- Started a bounded Luna Max implementation stream for Corp Agenda, Asset, and
  Upgrade installs from HQ into explicit existing roots or a newly created
  remote. The same serialized incremental replacement protocol must cover
  optional root trash, mandatory Agenda/Asset coexistence removal, and Region
  replacement without exposing facedown identities to the Runner. ICE is
  explicitly excluded from this checkpoint.

### 2026-09-02 — Phase-B B1b Corp empty-root checkpoint completed

- Completed the first bounded Corp-root slice: engine-issued `InstallCard`
  actions now cover zero-credit, one-click Agenda/Asset installs into empty
  existing Remote roots and Upgrade installs into empty existing server roots,
  plus explicit new-Remote choices for all three runtime classes. Corp cards
  move from HQ facedown and unrezzed, receive `MarkInstalledThisTurn`, emit
  typed movement/install/click events, and return the real TurnFrame to action
  priority. Agenda and Asset are never issued for central roots, and occupied
  roots are omitted until the replacement/Region checkpoint.
- Install planning is pure: a new-Remote action retains a null physical zone
  until final commit, while an existing destination freezes and revalidates its
  physical root zone. Commit allocates exactly one Remote server and its ICE
  and root zones; rejected or detached validation cannot mutate topology.
  Corp action materialization and state invariants require exact typed parity
  with the engine-issued action set without weakening the Runner boundary.
- Corp-private observations retain HQ definitions and legal target actions;
  Runner observations expose neither Corp action IDs/parameters nor the
  identity of the resulting facedown card. Legacy Runner install behavior is
  unchanged.
- Evidence: sourced `scripts/env.sh`; the focused Corp+Runner install filter
  passes 21/21; the Core project builds with zero warnings/errors; and
  `git diff --check` passes. Added coverage includes empty Remote Agenda/Asset,
  central-root Upgrade, exact payment/status/event/resume behavior, one-remote
  topology commits, illegal/occupied target nonissuance, owner-only
  observation, and stale/forged target rejection.
- Nonclaims: occupied-root replacement and Region selection (B1b2), Corp ICE
  installs/protector costs, Corp play/rez/score/advance behavior, and broader
  ENG-405 remain outside this checkpoint. Full-suite and formatter verification
  remain intentionally deferred.

### 2026-09-02 — Phase-B InstallChoice continuation parity correction

- Finding: `StateInvariantValidator` checked each serialized Runner
  `InstallCard` payload against its frozen `InstallFrame`, but did not compare
  the whole `InstallChoice` continuation set. A loaded decision could omit a
  remaining-candidate or finalization action, or duplicate one under a fresh
  `ActionId`, without violating the individual payload checks.
- Correction: extracted the exact read-only `InstallCardParameters` projection
  from `YieldInstallChoice`; the engine wraps that projection in allocator IDs,
  while validation re-materializes it from a detached working copy without
  consuming allocator state or mutating the validated `GameState`. Validator
  parity now requires exact count and semantic payload-set equality independent
  of `ActionId`, with stable missing/extra/duplicate continuation violations.
- Evidence: focused Runner+Corp install tests pass 25/25; Core.Tests build passes
  with zero warnings/errors; and the pre-existing unaffordable stale-choice
  regression now correctly expects `InvalidInputState` with the stable
  `decision.install-materialization-failed` diagnostic and unchanged hash.
- Nonclaims: this correction does not expand install rules, alter action ID or
  JSON/hash semantics, change owner visibility, or claim full Core/solution,
  formatter, CI, Corp ICE, B1b2 replacement/Region, or broader ENG-405 gates.

### 2026-09-02 — Phase-B B1b1 Corp empty-root install accepted

- Independent Luna Max review accepted pure new-Remote allocation, exact
  existing-root targeting, source/destination revalidation, Corp
  facedown/unrezzed state, click and typed-event ordering, TurnFrame resume,
  owner-only action visibility, and the explicit occupied-root/ICE omissions.
  Its one medium finding was the pre-existing whole-set validation gap for a
  serialized `InstallChoice`; that finding was corrected before acceptance.
- Root architecture review accepts the shared, allocator-free continuation
  projection: issuance alone assigns action IDs, while detached invariant
  validation requires the exact semantic payload multiset and rejects missing,
  extra, or duplicate continuations. This closes the loaded-state boundary
  needed by both Runner memory replacement and the next Corp root-selection
  checkpoint.
- Final evidence: combined Runner/Corp install tests pass 25/25; the complete
  Core suite passes 360/360; Core and Core-test formatter verification passes;
  the Core.Tests build is warning/error-free; and `git diff --check` passes.
- B1b1 is complete. Occupied-root optional/mandatory replacement and Region
  rules start in B1b2; Corp ICE installation and dynamic protector cost remain
  B1b3. Play/rez/score and the rest of ENG-402 through ENG-406 remain open.

### 2026-09-02 — Phase-B B1b2 Corp occupied-root replacement implemented

- Extended the existing serialized `InstallFrame`/`InstallChoice` protocol to
  Corp Agenda, Asset, and Upgrade installs into occupied existing ServerRoots.
  A Corp turn action now freezes the exact Corp/HQ source, server/root
  destination, stable ordered physical-root candidates, required coexistence
  or Region subset, and candidate definition identities without moving a card,
  spending a click, changing credits, allocating topology, or emitting events.
  Empty roots and explicit new-Remote choices retain the direct B1b1 path.
- Agenda and Asset incoming cards require every existing Agenda or Asset in
  the root (combined classes); Region Upgrade replacement uses case-insensitive
  `Region` subtype matching; ordinary Upgrade replacement may finalize with no
  selection. Occupied roots with unknown runtime metadata fail closed. Generic
  finalization trashes selected root cards once to CorpDiscard, preserves their
  faceup/facedown knowledge, installs the incoming Corp card facedown/unrezzed
  into the same physical root, spends exactly one click and zero credits,
  emits deterministic typed movement/click/install events, marks the install,
  and resumes real TurnFrame action priority.
- Generalized state/frame checks now enforce the Corp owner, HQ source,
  existing ServerRoot and exact physical zone, Corp install class/cost,
  candidate/required/selected sets, frozen candidate definitions, current root
  membership, and stale topology/resource rejection while retaining Runner
  invariants. Corp InstallChoice actions remain owner-only; Runner observations
  receive neither Corp actions nor facedown candidate identities.
- Added `CorpInstallReplacementTests` for cross-class and multiple mandatory
  replacement, Region/non-Region behavior, linear bounded action counts,
  atomic resources/events/final state, unknown metadata, stale movement and
  classification, forged frame fields, continuation omission/duplication,
  owner-only observation, and dual-catalog JSON/hash roundtrip. Updated the
  occupied-root regression in `CorpInstallActionTests` to assert frame entry.
- Evidence: the new Corp replacement focus passes 9/9; combined
  `CorpInstallReplacementTests|CorpInstallActionTests|RunnerInstallActionTests`
  passes 34/34; Core build passes with zero warnings/errors; Core formatter
  verification passes; and `git diff --check` passes. Root architecture
  verification reports the integrated Core suite at 370/370.
- Nonclaims: Corp ICE/protector costs, play/rez/score/advance, remote
  destruction between trash/install, unique-install gating, broader ENG-405,
  solution-wide formatter/CI, and commits/pushes remain outside this slice.

### 2026-09-02 — Phase-B B1b2 Corp occupied-root replacement coverage added

- Coverage-only test additions exercise mixed Agenda/Asset mandatory
  replacement, optional and Region Upgrade selection, atomic final commit
  events/state, unknown-root metadata fail-closed behavior, dual-catalog
  pending-frame JSON/hash preservation, allocator-free invariant/materialized
  projections, Corp-only observation, and stale/forged loaded-state and
  continuation rejection with unchanged hashes.
- Evidence: focused `CorpInstallReplacementTests` passes 10/10; the combined
  `CorpInstallReplacementTests` + `CorpInstallActionTests` +
  `RunnerInstallActionTests` filter passes 35/35. The test build used the
  existing in-progress CA1859 analyzer suppression; production acceptance and
  the final B1b2 gate remain with the primary agent.

### 2026-09-02 — Phase-B B1b2 Corp occupied-root replacement accepted

- Independent Luna Max review found no concrete high- or medium-severity
  defects in the combined production and test implementation. Root review
  accepts the single bounded transaction: stable physical-root candidates and
  their definition identities are frozen in a side-specific Corp frame;
  exact allocator-free continuation parity governs incremental selection; and
  trash, zero-credit install, click payment, events, and turn resume occur only
  at finalization.
- Root verification passes the replacement/Corp-action/Runner-action focus
  35/35, the complete Core suite 370/370, Core and Core-test formatter
  verification, a warning/error-free Core build, and `git diff --check`.
- Four non-blocking coverage follow-ups remain recorded for later regression
  hardening: Runner observation after every intermediate Corp selection;
  occupied central-root Upgrade selection with multiple optional candidates;
  legacy Runner-frame roundtrip with omitted candidate-definition identities;
  and CorpDiscard observation containing both facedown and previously faceup
  replacements. None changes the accepted B1b2 rules boundary.
- B1b2 is complete. The next install checkpoint is B1b3 Corp ICE installation,
  outermost topology, optional protector trash, and dynamic remaining-ICE
  cost. Per user request, the accumulated Slice 4.2 work through B1b2 will be
  captured in an interim commit after the repository pre-commit gate.

### 2026-09-02 — B1b2 interim-commit gate

- `./scripts/verify.sh` built the complete solution with zero warnings/errors
  and passed all 554 tests: Core 370, Cards 69, AI 23, CLI 52, and Match 40.
  Runtime/version, authenticated 77-card source, content-build, and missing or
  non-opted-in card-data diagnostics also passed.
- The script stopped at the expected Slice 4.2 coverage gate because ten
  already compiled Batch-1 declarations still have `unregistered` batch
  registry status and lack final `GatewayCard` test metadata. The report is
  therefore 17 compiled registrations but only the prior 7 tested registry
  entries (5 complete, 2 partial). This is an honest open-slice gate, not a
  B1b2 regression; declaration binding/tests/report completion remain later
  Slice 4.2 work.
- The interim commit is authorized at the completed B1b2 checkpoint despite
  that expected final-slice coverage failure. No push or PR is part of this
  checkpoint commit.

### 2026-09-02 — Phase-B B1b3 Corp ICE stream assigned

- Started the bounded Corp basic-ICE install checkpoint on top of the accepted
  B1b2 root replacement protocol. This stream covers HQ-to-existing-server
  and explicit-new-Remote ICE choices, outermost placement, optional linear
  protector trash, dynamic remaining-protector cost, atomic final payment and
  events, and allocator-free materialization/invariant checks.
- Root installs, Runner installs, play/rez/score/advance, card-effect and
  Archives installs, inward insertion, active-run topology changes, and remote
  destruction remain outside this checkpoint.

### 2026-09-02 — Phase-B B1b3 Corp ICE install accepted

- Completed basic Corp HQ-to-ICE installs for every existing central/remote
  server and one explicit new-Remote choice. Existing ICE zones preserve their
  physical innermost-to-outermost order; accepted installs append the incoming
  facedown/unrezzed ICE outermost. A new-Remote commit allocates exactly one
  server and its ICE/root zones, places the incoming card in the ICE zone, and
  leaves the root empty; detached planning and validation allocate nothing.
- An occupied ICE zone opens the shared serialized Corp `InstallFrame` with
  every physical protector in order, no mandatory candidates, frozen
  definition identities, and the exact physical destination. Incremental
  selection may trash any number; the dynamic credit cost is the number of
  protectors remaining after the selected set. Unaffordable finalize actions
  are omitted while cost-reducing candidate actions remain, and zero-cost
  commits emit no spurious credit event.
- Generalized Corp frame/action invariants to the ICE destination/class and
  authoritative remaining-protector cost. Unknown or non-ICE runtime metadata,
  rebuilt destinations, moved candidates, forged costs, and inexact
  continuations fail closed without mutation. Runner and Corp-root install
  invariants remain unchanged.
- Added `CorpIceInstallTests` covering destination enumeration, empty and new
  servers, physical ordering, dynamic nonzero/zero cost, affordability-driven
  finalize visibility, linear selection, exact events/resources, owner-only
  observation, pending-frame JSON/hash, allocator purity, metadata fail-closed
  behavior, and stale/forged rejection.
- Both Luna Max implementation streams reached the environment's usage limit
  after their production and test edits had landed, so they could not provide
  normal final handoffs. Root inspected the complete diff, corrected only
  formatter/test-fixture issues, and found no high- or medium-severity defect.
- Final evidence: focused Corp-ICE/Corp-root/Runner-install tests pass 45/45;
  the complete Core suite passes 380/380; Core and Core-test formatter
  verification passes; Core builds with zero warnings/errors; and
  `git diff --check` passes.
- B1b3 is complete. Basic Runner and Corp install topology/cost/replacement is
  now accepted. Card-effect/Archives/inward installs remain later-slice work;
  Slice 4.2 next continues ENG-402/403/405 with play, rez, basic run/action,
  scoring, tag/resource, and purge behavior.

### 2026-09-02 — Phase-B Slice 4.2 ordinary card-play seam assigned

- `/root/slice42_b2_play_core` owns only the production Core seam for typed
  ordinary Runner Event and Corp Operation play: legal-action projection,
  serializable play frames, atomic card movement/costs, generic behavior
  continuation, completion events, play-area topology/observation, and whole-
  state stale/forged validation. Tests, UI, Match, run-event/install semantics,
  and card registrations remain outside this assignment.

### 2026-09-02 — Architect contract for ordinary card play

- The primary agent stopped the exploratory Luna pass before production edits
  and owns the hard design. Luna implementation is constrained to the contract
  below; it does not choose the state machine, trust boundary, cost source, or
  run-event interaction.
- `PlayCardParameters` is the complete turn-action payload: physical card ID
  plus the frozen nonnegative printed credit cost. The allocator-free play
  projection walks the active side's physical HQ/Grip order and emits an
  action only when all of these facts agree: one or more clicks remain; the
  runtime catalog identifies the card as the same-side Corp Operation or
  Runner Event and supplies `PlayCost`; the generic behavior catalog contains
  the same definition with that side as controller; every up-front behavior
  cost is `Credits`; their checked sum exactly equals `PlayCost`; and current
  wallet credits cover it. A legacy `RunEventBehaviorSpec` without a generic
  definition is therefore not an ordinary play action.
- Accepted play uses stack `[TurnFrame(AwaitingAction), CardPlayFrame,
  BehaviorResolutionFrame]`. `CardPlayFrame` is version 1 with behavior ID
  `rules.card-play-26.03` and freezes player/side, card/definition/class,
  canonical source, play-area and discard zone IDs, printed cost, behavior
  catalog fingerprint, and definition fingerprint. It is immutable and has a
  single resolving state; the generic child binds its exact parent through
  existing `ParentBehaviorId` plus `BehaviorFrameIdentity.Compute(parent)`.
- Before the child is pushed, the reducer re-materializes and exactly matches
  the issued action, creates or reuses an empty canonical side play area,
  spends exactly one action click, moves the card there faceup/unrezzed, and
  emits typed play-start evidence. The existing generic `PayCosts` stage is
  the only executor of the printed credit cost. Zero-credit behavior costs
  must not emit a zero `CreditsAdjusted` event. All work occurs on the normal
  `WorkingState`, so any later failure rejects without mutating the supplied
  state.
- A yielded generic choice serializes all three frames. The played card remains
  the only card in its public side play area, and the existing choice owner is
  the only viewer receiving legal actions. Once the generic child pops,
  `CardPlayFrame` revalidates its frozen topology/catalog facts, moves the card
  faceup from the play area to the canonical Corp discard or Runner heap,
  emits typed completion evidence, pops itself, and replaces the underlying
  turn frame with `ActionPriority` for the active side. This remains true when
  the play leaves zero clicks; the normal two-pass action-priority path owns
  transition to forced action-phase end.
- Add lazy `CorpPlayArea`; retain lazy `RunnerPlayArea`. Both are public,
  side-owned, serverless, unique-at-most-one zones and are forbidden as
  prepopulated compact-scenario inputs. Lazy creation preserves existing state
  topology/hashes. A populated Runner play area is legal either for this exact
  ordinary-play frame/child topology or for the existing exact live run-event
  lifecycle, never both. Overclock/run initiation, payment, active-event
  identity, and run-end trash remain unchanged.
- Whole-state validation requires the play frame immediately above the matching
  active-side `TurnFrame(AwaitingAction)` and immediately below its matching
  generic child. The physical card must match the frozen definition/owner,
  occupy the frozen correct-side play area faceup/unrezzed, and be its sole
  member; source/discard IDs must be the canonical side zones. Injected runtime
  metadata, generic definition/controller, fingerprints, supported credit-only
  cost sum, printed-cost equality, child source location, and exact parent
  identity are all rechecked. Turn-action validation compares the serialized
  play actions bijectively with the same allocator-free projection. Unknown,
  stale, forged, mismatched, or unsupported data fails closed.
- Typed event order is fixed for review: `ClickSpent`, hand-to-play-area
  `CardMoved`, `CardPlayStarted`, nonzero `CreditsAdjusted`, behavior effects
  and choices, play-area-to-discard `CardMoved`, then `CardPlayCompleted`.
  Choice-bearing plays split this evidence across accepted submissions without
  changing the global sequence. This checkpoint does not bind cards, add
  play UI, implement run actions/rez/scoring, or generalize play to any class
  beyond ordinary Event/Operation.

### 2026-09-02 — Ordinary card-play checkpoint A contract landed

- Added the typed `PlayCardParameters` action payload, lazy `CorpPlayArea`
  topology kind, and immutable version-1 `CardPlayFrame` JSON contract with
  strict frame validation and cloning. Reducers, state invariants, events,
  observations, scenarios, and tests remain for the follow-up implementation
  checkpoint.

### 2026-09-02 — Ordinary card-play checkpoint B reducer landed

- Luna implemented the fixed reducer contract without changing its design:
  allocator-free runtime/behavior-cost-parity projection, typed turn-action
  dispatch, lazy side play-area creation, one-click/card-move/start ordering,
  private generic child push, zero-credit event suppression, completion trash
  and action-priority resume, plus typed start/completion events and their
  immutable validation/clone surfaces.
- Root reviewed the reducer line by line. It re-materializes before mutation,
  leaves generic `PayCosts` as the sole printed-credit executor, binds the
  child to the exact play parent, and does not touch the run-event reducer.
  The Luna turn was interrupted only after its patches had landed because its
  verification phase did not return; root's direct Core build passes with zero
  warnings/errors and `git diff --check` passes.
- This checkpoint is not yet behaviorally accepted: whole-state play-frame,
  play-area/run exclusivity, and TurnAction-bijection validation; public
  observation/scenario wiring; and focused/full tests remain pending.

### 2026-09-02 — Ordinary card-play checkpoint C invariant/observation surfaces landed

- Added whole-state `CardPlayFrame` checks for canonical side-owned source,
  discard, and lazy play-area zones; active-side/current-turn parent placement;
  required generic behavior child; physical card definition/owner/location,
  faceup/unrezzed, and sole play-area membership; injected runtime metadata;
  generic definition/controller, catalog/definition fingerprints, checked
  credit-only up-front cost parity; and child card/controller/acting-player,
  source location, parent behavior ID, and serialized parent-frame identity.
  Malformed cost sums and parent identity serialization are reported as
  violations instead of escaping validation.
- Added typed `PlayCard` decision validation and allocator-free
  `TurnAction` play-action bijection checks by physical card ID plus frozen
  credit cost, including missing, extra, duplicate, stale-cost, and
  materialization-failure diagnostics. `TurnAction` action-kind messages now
  include ordinary play.
- `CorpPlayArea` now has Corp zone semantics and an at-most-one constraint
  parallel to `RunnerPlayArea`. Populated play areas require their exact
  active card-play frame; Runner play areas retain the live-run lifecycle and
  reject ordinary Runner play frames while `RunState` exists. Existing exact
  active-run-event checks remain in force.
- Added conditional public `corpPlayArea` CardView exposure for both viewers,
  while preserving lazy topology and existing conditional Runner exposure.
  Compact scenarios now recognize Corp play-area ownership but reject either
  play-area kind as active card-play/run zones.
- Evidence: `bash -c 'source scripts/env.sh && dotnet build
  src/Netrunner.Core/Netrunner.Core.csproj --no-restore --nologo'` passed with
  0 warnings and 0 errors; targeted `dotnet format` completed successfully;
  `git diff --check` passed.
- Nonclaims: no reducers, contracts, events, or run reducer were changed in
  checkpoint C; no focused/full test suite was run in this checkpoint, and
  card registrations/UI/Match behavior remain outside its scope.

### 2026-09-02 — Ordinary card-play architect review before tests

- Root reviewed checkpoints A–C against the recorded contract. Production
  projection, mutation order, generic cost ownership, frame/catalog binding,
  public-zone ownership, TurnAction bijection, and run-event separation match
  the design. The warning-free Core build and all pre-existing Core tests pass
  380/380; `git diff --check` remains clean.
- Test hardening must prove the persisted play stack is exactly Turn/Play/
  Behavior, not only locally adjacent; reject extra nested frames, forged
  parent identity/cost/source/class/catalog facts, and orphaned or run-
  conflicting play areas. No ordinary card-play checkpoint is accepted until
  those adversarial cases and both side lifecycles pass.

### 2026-09-02 — Ordinary card-play lifecycle tests accepted

- Luna added `OrdinaryCardPlayTests` for physical-order Runner/Corp action
  projection; no-click, affordability, class, missing-definition, catalog-cost,
  unsupported-cost, and legacy-run-event exclusions; exact Runner paid/effect/
  trash event order; zero-cost Corp play without a zero credit event; and a
  Corp-owned mode choice that persists the public Runner play area and exact
  three-frame stack across JSON/hash round trip before completing to heap and
  Runner action priority.
- Root review corrected two test-fixture defects before acceptance: no-click
  and unaffordable rows now isolate those predicates instead of also failing
  catalog cost parity, and the Corp chooser test correctly credits the Runner
  controller rather than the player who chose the mode. No production behavior
  changed.
- Evidence passes: ordinary-play focus 12/12, existing `RunEventTests` 19/19,
  complete Core 392/392, Core/test formatting, and `git diff --check`.
  Adversarial forged-state/atomicity coverage remains the final gate for this
  ordinary-play checkpoint.

### 2026-09-02 — Ordinary card-play adversarial gate accepted

- Luna implemented only the adversarial coverage and the one production
  hardening authorized by the architect contract. `CardPlayFrame` now requires
  its bound generic behavior child to be the final/top resolution frame and
  reports `resolution.card-play-child-not-top` for injected nesting.
- The added tests prove play-action decision bijection diagnostics for altered,
  duplicate, missing, and extra actions; atomic rejection when an issued play
  becomes unaffordable; no-throw, specific diagnostics for forged frozen cost,
  catalog/definition fingerprints and identities, runtime class, source/play/
  discard topology, card definition/owner/visibility/rez state, child parent
  identity, and play-area membership; rejection of populated orphan Corp and
  Runner play areas; and exclusion of an ordinary Runner play frame during a
  live run.
- Root reviewed the production and test changes against the recorded contract.
  The hardening is local to the ordinary-play stack invariant, preserves the
  separate legacy run-event lifecycle, and all invalid-input applications
  checked by the suite return the supplied state with no events.
- Evidence: `OrdinaryCardPlayTests` passes 19/19; the complete Core suite passes
  399/399; Core builds with zero warnings/errors; Core/test formatter
  verification passes; and `git diff --check` passes. The ordinary Event/
  Operation play primitive is accepted; Batch-1 card bindings remain later
  Slice 4.2 work.

### 2026-09-02 — Architect contract for basic run actions inside real turns

- Root owns this state-machine and compatibility design. Luna is assigned only
  its mechanical implementation and tests; it must not choose a second run
  lifecycle, alter M2 timing, or broaden card-run semantics.
- Add typed `StartRun` plus immutable `RunServerParameters(ServerId)`. During a
  Runner `TurnFrame(AwaitingAction)` with at least one click, no live run, and
  no occupied transient play area, allocator-free projection emits exactly one
  basic-run action for every existing server in physical `GameState.Servers`
  order. Corp never receives it. Link and credits are deliberately absent: a
  basic run costs exactly one click and nothing else.
- Consumption re-materializes the complete current run set and exactly matches
  the issued server ID before mutation. It spends one Runner click, then uses a
  single shared internal M2 run-start routine to create the same `RunState`,
  `RunTimingFrame`, first priority decision, and `RunInitiated` evidence as the
  existing public `BeginRunWithEvents` boundary. Do not duplicate run setup.
  Event order begins `ClickSpent`, then `RunInitiated`; no synthetic credit or
  link event is emitted.
- The persisted action lifecycle is exactly `[TurnFrame(AwaitingAction),
  RunTimingFrame]` while the ordinary basic run is active. The run frame is the
  top frame and retains all existing M2 timing/choice semantics. The no-source
  basic action must not populate `RunnerPlayArea`, `InitiatingSource`, or
  `ActiveRunEventId`. Standalone public `BeginRun*` remains exactly the legacy
  one-frame boundary and registered run-event behavior remains unchanged.
- `CompleteRun` detects the matching real-turn parent after popping its run
  child and replaces that parent with Runner `ActionPriority`, even when the
  run spent the final click. The normal two-pass turn window then decides
  whether another action is available or the phase ends. Standalone M2 runs
  still finish with an empty stack and no new turn decision.
- A real-turn run uses the CR victory threshold of seven agenda points. An
  automatic steal during such a run completes `RunEnded` first and then uses
  the real-turn terminal cleanup. Existing standalone M2 fixtures retain their
  three-point compatibility threshold and event ordering; this compatibility
  rule is contextual to presence of the exact real-turn parent, not inferred
  from mutable catalog or caller flags.
- Whole-state validation accepts only the two explicit run topologies: one
  standalone matching `RunTimingFrame`, or the exact Runner Turn/Run nesting
  above. A nested basic run requires current active Runner/turn number,
  `AwaitingAction`, null source/event, and matching top run frame; extra,
  reordered, Corp, stale, or card-sourced nesting fails closed. TurnAction
  validation compares run actions bijectively with the same allocator-free
  projection and emits run-specific missing/extra/duplicate diagnostics.
- Tests must cover both-side action projection, all physical servers/order,
  exact click/event/stack initiation, mid-run JSON/hash roundtrip, action-
  priority resumption with and without remaining clicks, forged and stale
  decision atomic rejection, invalid nested topology without validator throws,
  real-turn seven-point win/non-win boundaries, and unchanged standalone M2
  run/run-event tests. No rez redesign, run-event turn action, card binding,
  UI, Match, scoring action, tags, purge, or broader run primitive belongs to
  this checkpoint.

### 2026-09-02 — Basic-run action verification

- Added focused `BasicRunActionTests` for both-side projection in persisted
  server order, transient play-area exclusion, exact click/event/stack
  initiation, mid-run JSON/hash continuation, action-priority resumption with
  remaining and final clicks, forged/missing/extra/duplicate/wrong/stale
  decisions with atomic rejection, malformed nested topology diagnostics, and
  real-turn seven-point automatic-steal boundaries. The nested-source case
  explicitly verifies that non-null initiating source/event data is diagnosed
  without a validator throw and rejects atomically. Standalone BeginRun smoke
  coverage remains in the same focused suite; existing RunEvent coverage was
  retained.
- Removed the unused `MaterializeStartRunParameters` alias. No other
  production behavior was changed during this verification pass; one
  whitespace-only indentation defect exposed by formatter verification was
  corrected.
- Evidence: `BasicRunActionTests` 12/12, `RunTimingTests` 11/11,
  `RunEventTests` 19/19, `BreachAccessTests` 24/24, and the complete Core
  suite 411/411. Core builds with zero warnings/errors; Core and focused-test
  formatter verification pass; `git diff --check` passes.
- Nonclaims: this checkpoint does not add rez redesign, run-event turn
  actions, card bindings, UI/Match changes, scoring actions, tags, purge, or
  broader run primitives.

### 2026-09-02 — Basic-run action architect acceptance

- Root reviewed the shared start routine, typed action projection/dispatch,
  exact nested topology checks, completion continuation, contextual victory
  branch, and all focused tests against the preceding architect contract. The
  implementation has one run lifecycle: the new turn action and legacy public
  boundary converge before `RunState` creation and diverge only at the
  explicitly validated parent/continuation boundary.
- Root corrected two test-contract mistakes during review: a one-point agenda
  added to six points must win, and removing a Turn parent produces the valid
  standalone topology rather than a distinguishable malformed state. An
  explicit non-null source/event nesting forgery now covers the invalid case.
- Independent evidence: combined BasicRun/RunTiming/RunEvent/BreachAccess
  coverage passes 66/66; complete Core passes 411/411; Core builds with zero
  warnings/errors; formatter verification and `git diff --check` pass. The
  basic one-click run action inside a real Runner turn is accepted.

### 2026-09-02 — Architect contract for real advance and score actions

- Root owns the hard timing and trust-boundary design below. Luna will receive
  these decisions as an implementation contract and must not choose alternate
  payloads, put scoring in the click-action prompt, reuse toy reducers, or add
  score/steal triggers.
- Add `RealGameRules.AgendaPointsToWin = 7` and replace the basic-run literal
  with that single authority. Preserve `ToyRules.AgendaPointsToWin = 3` only
  for standalone M1/M2 compatibility. Add immutable typed
  `AdvanceCardParameters(CardId, CreditCost)` and
  `ScoreAgendaParameters(CardId, AgendaPoints, AdvancementRequirement)`;
  `CreditCost` is exactly one for this basic action. Keep legacy
  `AdvanceAgenda`/card-choice and `ScoreAgenda`/card-choice toy actions intact;
  use new `AdvanceCard` for the real click action and the existing
  `ScoreAgenda` kind with its new typed score payload in real turn timing.
- Runtime metadata is the sole printed-data authority in this checkpoint.
  Allocator-free advance projection exists only in a Corp
  `TurnFrame(AwaitingAction)` with a click and credit, and walks physical
  `GameState.Servers` order then each physical root order. It emits installed,
  Corp-owned, facedown/unrezzed runtime Agendas in any remote root, including
  agendas already at or above their requirement: overadvancement is legal.
  This checkpoint deliberately does not make non-agenda cards advanceable;
  explicit advanceable-card metadata arrives with the owning later batch.
- Accepted advance re-materializes the full current set, spends exactly one
  click and one credit, checked-increments the counter, emits the existing
  typed `AgendaAdvanced` evidence using the authoritative requirement, and
  resumes Corp `ActionPriority`. Exact evidence order is `ClickSpent`,
  `CreditsAdjusted`, `AgendaAdvanced`; the supplied state remains untouched on
  any stale/overflow/resource failure.
- Scoring is never a TurnAction. It is materialized alongside Pass only for
  the Corp holder of the Corp `ActionPriority` `PaidRezAndScore` window. It
  walks the same physical server/root order and offers only installed Corp
  runtime Agendas whose counters meet or exceed their positive requirement.
  Score payload points/requirement must exactly match the injected runtime
  catalog; no behavior registration is required merely to score printed
  agenda metadata.
- Accepted scoring re-materializes the current score set, spends no click or
  credit, moves the agenda faceup/unrezzed from its server root to canonical
  `CorpScore`, adds its positive printed points, and emits `CardMoved` then
  `AgendaScored`. If the total is at least seven, real-turn terminal cleanup
  emits `GameWon` last. Otherwise it restarts the Corp action-priority window
  with Corp as holder and zero passes. If movement leaves a remote with both
  root and ICE empty, remove that remote and its two empty zones atomically;
  retain the server when either zone still contains a card.
- Pending TurnAction validation compares real advance actions bijectively with
  the advance projection. Pending TurnTiming validation retains exactly one
  Pass and compares score actions bijectively with the score projection.
  Missing, extra, duplicate, wrong-payload, forged cost/points/requirement,
  unknown target, stale counters/topology/resources, catalog mismatch, and
  invalid timing owner/window all fail closed before mutation with specific
  advance/score diagnostics. Projections consume no allocator state.
- Tests must prove physical ordering and side/phase/window/resource/class/
  topology exclusions; overadvancement; exact advance and score event/resource
  sequences; pending-score JSON/hash roundtrip; nonterminal window restart;
  six-to-seven and below-seven outcomes; empty-versus-protected/occupied remote
  cleanup; forged/stale decision atomicity and no-throw validation; and full
  toy, turn, install, play, and basic-run regression safety. Rez, non-agenda
  advanceability, score/steal triggers, Runner steals, card bindings, UI, and
  Match remain outside this checkpoint.

### 2026-09-02 — Real advance/score implementation and adversarial evidence

- Added the fixed real-turn advance and score payloads, runtime-catalog-backed
  allocator-free projections, Corp turn-action/timing dispatch, exact resource
  and typed-event reducers, nonterminal priority-window restart, seven-point
  real-turn victory branch, and empty-remote cleanup. Legacy toy
  `AdvanceAgenda`/`ScoreAgenda` card-choice reducers remain separate.
- Focused `RealAdvanceScoreTests` now cover two-remote physical ordering with a
  deliberately reversed root-card list, overadvancement, all requested context,
  resource, topology, owner, class, face-up, rezzed, and requirement
  exclusions, exact event/resource/zone outcomes, score JSON/hash roundtrip,
  terminal and nonterminal scoring, separate ICE-only and root-card-only remote
  retention, and forged missing/extra/duplicate/wrong/stale/cost/points/
  requirement decisions. Invalid pending states are checked for specific
  diagnostics, no validator throw, same-state rejection, and empty events.
- Evidence: the final `RealAdvanceScoreTests` run passed 15/15; the focused
  Turn/Toy/Install/Play/BasicRun selection passed 124/124; before the final
  overflow-only test was appended, the complete Core suite passed 425/425,
  Core build with warnings as errors passed with 0 warnings and 0 errors, Core
  and Core-test `dotnet format --verify-no-changes` passed, and
  `git diff --check` passed.
- Nonclaims: this checkpoint does not add rez redesign, non-agenda
  advanceability, score/steal triggers, Runner steals, card bindings, UI,
  Match, or broader run primitives. Full solution/Godot/card-data verification
  is not claimed here.

### 2026-09-02 — Real advance/score architect acceptance

- Root reviewed the final reducer, projection, dispatch, invariant, and test
  diff against the preceding contract. The implementation preserves the toy
  agenda path, trusts only injected runtime printed values, keeps scoring out
  of the click-action prompt, restarts nonterminal Corp priority correctly,
  and removes a remote only when both of its physical zones are empty.
- Root required and reviewed additional adversarial rows for face-up/rezzed
  cards, wrong timing holder/window, physical order across multiple remotes,
  separate ICE/root remote-retention cases, missing/extra/duplicate and frozen
  cost/points/requirement forgeries, stale metadata/resources/topology, the
  complete post-action event tails, and checked counter overflow. Invalid
  inputs reject with the supplied state instance, no events, and no validator
  exception.
- Independent final evidence after the overflow row landed:
  `RealAdvanceScoreTests` passes 15/15; the complete Core suite passes 426/426;
  Core builds with zero warnings/errors; Core/test formatter verification and
  `git diff --check` pass. The real agenda advance/score checkpoint is
  accepted.

### 2026-09-02 — Architect contract for runtime-catalog rez actions

- Root owns the hard rez timing, compatibility, printed-cost, and event design
  below. Luna is limited to implementing this fixed contract and its tests; it
  must not replace M2 run timing, infer card classes or costs from behavior
  text, expose hidden Corp identities, or introduce derez/trigger behavior.
- Add immutable `RezCardParameters(CardId, CreditCost)` and a new
  `ActionKind.RezCard`. The frozen nonnegative cost must equal the injected
  `CardRuntimeSpec.RezCost` at validation and consumption. Retain the existing
  `RezIce`/`RunIceParameters` path only for runtime-catalog-free standalone M2
  compatibility; a runtime-catalog-backed decision uses `RezCard` and never
  emits a second legacy rez action for the same ICE.
- A turn-timing rez is materialized alongside Pass (and any legal Score) when
  the priority holder is Corp in either `PaidRezAndScore` or `PaidAndRez`.
  This is independent of whose turn it is. It walks physical server order and
  each physical root order, offering installed Corp-owned, unrezzed runtime
  Assets and Upgrades whose printed rez cost is present and affordable. It
  accepts both facedown newly installed cards and faceup derezzed cards, and
  rejects agendas, ICE outside an approach, wrong/missing zones or metadata,
  Runner-owned cards, and already-rezzed cards.
- A runtime-catalog-backed run decision uses the persisted capability flags.
  When `CorpMayRezApproachedIce` is true, only the exact current approached
  ICE may be offered, and only if it is the Corp-owned unrezzed runtime ICE at
  the current index of the attacked server's ICE zone. When
  `CorpMayRezNonIce` is true, the same physical Asset/Upgrade root projection
  is offered. Projection order is physical server order, and within a server
  the eligible approached ICE precedes that server's physical root cards.
  No other ICE can be rezzed. Corp must hold priority and afford each card.
- Consumption re-materializes the complete current rez set before mutation.
  It pays exactly the frozen printed credit cost, suppressing a zero-credit
  adjustment event, then replaces the installed card with the same physical
  identity/location/owner/counters but `FaceUp=true` and `Rezzed=true`, and
  emits a new typed `CardRezzed(CardId, CreditCost)` event. No behavior child,
  activation effect, trigger, click, or topology mutation belongs here.
- A successful rez resets consecutive passes to zero, retains Corp priority in
  the same turn/run window, disables only the consumed approached-ICE rez
  capability for that ICE, and yields a fresh decision. Exact evidence is an
  optional nonzero `CreditsAdjusted`, then `CardRezzed`, then the existing
  matching timing-window-opened event. Other eligible rez and score actions
  are freshly reprojected. A later external derez represented by
  `FaceUp=true/Rezzed=false` is eligible to be re-rezzed.
- TurnTiming and RunTiming invariant validation compare typed runtime rez
  actions bijectively with the same allocator-free projections. They diagnose
  missing, extra, duplicate, wrong-payload, unknown target, frozen-cost
  mismatch, stale credits/rez state/topology/capability, and catalog failures
  without throwing. Observation continues to expose legal actions only to the
  Corp decision owner; the opponent learns a formerly hidden card definition
  only after accepted rez makes it faceup.
- Tests must cover Corp priority during both sides' turns; both turn window
  kinds; physical multi-server/root order; zero/nonzero payment and exact
  event tails; same-window/same-holder continuation; faceup derezzed re-rez;
  all class/owner/zone/metadata/affordability exclusions; run approached-ICE
  and non-ICE flags/order; a non-current ICE exclusion; JSON/hash roundtrip;
  allocator purity; stale/forged decision atomicity/no-throw diagnostics; and
  unchanged standalone M2 run/trace/hash behavior. Derez actions, rez effects,
  paid abilities, triggers, uniqueness cleanup, card bindings, UI, and Match
  remain outside this checkpoint.

### 2026-09-02 — Runtime-catalog rez implementation/evidence

- Implemented the fixed runtime rez contract in `ActionContracts`, typed event
  serialization/validation, `GameEngine.Rez`, turn/run dispatch, and invariant
  projection. Runtime-backed decisions use `RezCard` with frozen printed costs;
  runtime-null standalone M2 `RezIce` behavior remains available.
- Added focused coverage for both turn windows and physical ordering, payment
  and event tails, same-window continuation, face-up derezzed re-rez, run ICE
  and non-ICE capabilities, secrecy/observation, exclusions, round trips,
  forged actions, stale topology, and catalog compatibility. The focused
  `RuntimeRezActionTests` filter passes 16/16, and the combined rez/run timing
  regression filter passes 103/103.
- Evidence also includes a zero-warning/error Core build, solution formatter
  verification, and `git diff --check`. This checkpoint does not claim derez,
  rez effects/triggers, paid abilities, UI, Match integration, or full
  Godot/card-data verification.

### 2026-09-02 — Runtime-catalog rez architect acceptance

- Root reviewed the action/event schema, turn and run projections, reducer,
  runtime-versus-legacy dispatch, decision bijections, secrecy boundary, and
  final tests. During review, root corrected the topology design explicitly:
  Assets are rezzable only from Remote roots, while Upgrades may be rezzed
  from central or Remote roots. Luna applied that fixed rule and added the
  central-Asset exclusion; no test relies on an illegal central Asset as a
  positive fixture.
- The runtime path freezes and rechecks printed cost, preserves physical
  order, retains Corp priority with reset passes, exposes a formerly hidden
  definition only after the accepted faceup transition, and never co-issues
  legacy `RezIce`. Runtime-catalog-free standalone M2 retains its original ICE
  cost/action/event path. Stale topology, catalog, credits and rez state plus
  malformed/missing/extra/duplicate/cost-forged turn and run sets reject
  without validator exceptions, mutation, or events.
- Independent final evidence: `RuntimeRezActionTests` passes 16/16; the
  combined runtime-rez, `RunRezCost`, `RunTiming`, `RunTimingTrace`, and
  `RunEvent` gate passes 69/69; complete Core passes 442/442; Core builds with
  zero warnings/errors; full-solution formatter verification and
  `git diff --check` pass. The bounded runtime-catalog rez checkpoint is
  accepted.

### 2026-09-02 — Architect contract for remaining tag/resource/purge basic actions

- Root owns the action-cost, state, counter, observation, and trust-boundary
  design below. Luna is assigned only its mechanical implementation and tests;
  it must not infer resource class from card text/behavior, reinterpret losing
  clicks as spending clicks, add tag avoidance, or broaden counter cleanup.
- Add immutable action-specific payloads:
  `RemoveTagParameters(ClickCost, CreditCost)`,
  `TrashTaggedResourceParameters(CardId, ClickCost, CreditCost)`, and
  `PurgeVirusCountersParameters(ClickCost)`, with new matching action kinds.
  Authoritative constants are remove-tag `1 click + 2 credits`, tagged-resource
  trash `1 click + 2 credits`, and purge `3 clicks`. Pending validation and
  consumption require the frozen values to equal those constants exactly;
  callers still submit only an issued action ID.
- Remove-tag is a Runner `TurnFrame(AwaitingAction)` option only when the
  Runner has at least one standard tag in `RulesState`, at least one click,
  and at least two credits. Missing `RulesState` means zero tags, not an
  implicit tagged state. Accepted removal re-materializes, spends one click,
  pays two credits, removes exactly one tag through the existing checked tag
  primitive, then resumes Runner ActionPriority. Exact evidence is
  `ClickSpent`, `CreditsAdjusted`, `TagsAdjusted(-1)`, and the fresh
  `TurnTimingWindowOpened` tail.
- Tagged-resource trash is a Corp `TurnFrame(AwaitingAction)` option only when
  the Runner has at least one standard tag and Corp has one click plus two
  credits. Runtime metadata is the sole class authority. Projection walks the
  canonical RunnerRig physical card order and offers only cards that agree in
  every layer: physical membership/location, Runner ownership, faceup and
  unrezzed installed state, and an injected Runner `Resource` runtime spec.
  Accepted trash re-materializes the whole target set, spends one Corp click,
  pays two credits, moves the selected card to canonical RunnerHeap (whose
  existing move boundary preserves public faceup state), and resumes Corp
  ActionPriority. Exact evidence is `ClickSpent`, `CreditsAdjusted`,
  `CardMoved`, then `TurnTimingWindowOpened`. Resource abilities, hosted state,
  and card-counter cleanup are later owning primitives.
- Purge is a Corp `TurnFrame(AwaitingAction)` option whenever Corp has at least
  three clicks, even when no virus counter exists and whether or not optional
  `RulesState` has previously been materialized. Accepted purge re-materializes
  and spends exactly three clicks through the ordinary spend-click boundary,
  producing three ordered `ClickSpent` events with each remaining-click value.
  It then ensures default RulesState exists, removes every physical
  `CardCounterState` whose case-sensitive name is `virus`, preserves all other
  counter records/order, and emits one `VirusPurged` payload whose
  `CountersRemoved` is the checked sum of removed token counts (not the number
  of records). It resumes Corp ActionPriority, so the timing-window event is
  last. A total that cannot fit the existing integer payload rejects atomically.
- All three projections require the exact active real-turn action topology,
  no live run, and no occupied transient play area. They allocate nothing.
  TurnAction issuance has deterministic existing base/install/play/run/advance
  ordering followed by the applicable new action(s); tagged-resource targets
  retain rig order. TurnAction invariants compare each new typed family
  bijectively with the same projection and diagnose missing, extra, duplicate,
  wrong payload/cost/target, stale tags/clicks/credits/card location/metadata,
  and materialization failures without throwing.
- Standard tag count is public information. When `RulesState` exists,
  `ObservationBuilder.PlayerView` adds each player's nonnegative `tags` value;
  legacy observations without RulesState retain their prior byte shape.
  Decision actions remain visible only to their owner; no hidden hand/deck
  identity is exposed.
- Tests must cover both-side projection and action ordering; exact thresholds
  and one-below exclusions; multi-tag single removal; physical resource order
  and all owner/class/visibility/rez/location/catalog exclusions; exact
  resources/zones/events; purge with zero and multiple virus records plus
  preserved non-virus counters and corrected token total; null-RulesState
  purge; JSON/hash and allocator purity; public tag observation; all stale and
  forged decision families with same-state/no-events/no-throw rejection; and
  turn/install/play/run/effect compatibility. Tag gain/avoidance, hosted-card
  cleanup, resource abilities, UI, Match, agent policy, and card bindings are
  outside this checkpoint.

### 2026-09-02 — Tag/resource/purge implementation and architect acceptance

- Luna implemented the three fixed action families without changing the root
  design: typed/frozen payloads and constants, allocator-free projections,
  deterministic TurnAction issuance, atomic reducers, public standard-tag
  observation, and exact decision-family validation. The purge reducer reuses
  the shared effect primitive; that primitive now reports the checked sum of
  removed virus tokens rather than the number of counter records and preserves
  every non-virus counter in order.
- Root reviewed the production paths and required a second adversarial pass.
  That pass added both-side thresholds and action ordering, physical resource
  order plus owner/visibility/rez/location/runtime-class exclusions, allocator
  purity, all three pending-payload JSON/hash round trips, public tags for both
  viewers with the null-RulesState legacy shape retained, wrong payload/cost and
  missing/extra/duplicate decision families, stale wallet/tag/card/catalog
  rejection, and checked purge-total overflow. Root caught and returned three
  transient test merge artifacts, one analyzer return-type issue, and one
  two-resource selector mistake; no production correction was required.
- Independent final evidence: the combined `RealBasicActionTests` and
  `RulesEffectsTests` gate passes 22/22; complete Core passes 451/451; the Core
  test project builds with zero warnings/errors; solution formatter
  verification and `git diff --check` pass. The bounded remaining-basic-action
  checkpoint is accepted. Tag avoidance/gain, hosted cleanup, resource
  abilities, UI, Match, agent policy, and card bindings remain outside it.

### 2026-09-02 — Architect contract for authenticated runtime composition and ten-card lifecycle gate

- Root owns this composition and evidence design. Luna may implement only the
  fixed mappings and tests below; it must not interpret card text, couple
  runtime metadata to behavior coverage, silently admit incomplete metadata,
  or claim Mutual Favor/Seamless Launch complete in this checkpoint.
- Add a Cards-owned static `GatewayRuntimeCardCatalog` composition root whose
  methods return Core's concrete immutable `RuntimeCardCatalog`; the Cards
  layer must not wrap the catalog or retain/expose the source dataset or rules
  text through the engine dependency.
  `Create(CardDataSet)` revalidates the supplied normalized dataset and builds
  exactly one `CardRuntimeSpec` for every supplied definition, independent of
  whether executable behavior exists. Its stable version is
  `gateway-runtime-v1-<manifest commitSha>` and its existing Core fingerprint
  remains the content identity. `CreateEmbedded()` is only a convenience over
  the already authenticated offline `CardDataResource`; neither path performs
  I/O or network access after the dataset has been loaded.
- Mapping is exhaustive and ordinal over normalized tokens: corp/runner map to
  their exact sides; identity, agenda, asset, upgrade, ice, operation, event,
  program, hardware, and resource map to the matching runtime classes. Cost is
  required and becomes PlayCost for Operation/Event, RezCost for
  Asset/Upgrade/ICE, and InstallCost for Program/Hardware/Resource. Agenda
  points and advancement requirement are both required only for Agenda;
  program memory cost is required only for Program; printed trash cost is
  required only for Asset/Upgrade. Applicable missing/negative or forbidden
  metadata fails with a Cards-layer validation exception naming the card code.
  Identities carry none of those fields. Subtype keys come only from normalized
  metadata through a lexical, non-semantic key conversion: invariant lowercase,
  runs of spaces become one hyphen, existing ASCII letters/digits/hyphens are
  retained, and every other character, empty result, or post-conversion
  duplicate fails closed. Thus `Code Gate`, `G-mod`, and `AI` become
  `code-gate`, `g-mod`, and `ai`. A console is exactly Runner Hardware with the
  resulting `console` subtype and receives the single stable `runner-console` key.
  Uniqueness comes only from normalized `Uniqueness`; its whitespace-safe key
  is `unique-title-sha256:<lowercase SHA-256 of the exact normalized UTF-8
  title>`, so equal normalized names converge without putting titles in engine
  identifiers. No title, code, or rules text determines class/cost/effects.
- The existing typed declarative condition gains an optional explicit subject
  side for standard `Tagged`/`NotTagged` only. Omitted subject preserves the
  controller-relative behavior and its existing definition fingerprint bytes;
  an explicit subject is serialized, compared, fingerprinted, and evaluated
  against that side's player/rules state. Predictive Planogram must use
  `Tagged(PlayerSide.Runner)`, proving Corp tags cannot unlock `both` while one
  Runner tag does. This additive correction is the only Core behavior-contract
  change in this checkpoint.
- Add real action-lifecycle evidence for the ten already executable Batch-1
  declarations, using `GatewayBehaviorCatalog` plus runtime specs compiled from
  normalized metadata. Event/Operation tests enter through real TurnAction
  `PlayCard`; T400 enters through real Runner `InstallCard`. Prove printed costs
  are paid once, play-area/heap-or-Archives cleanup, exact credit/click/draw
  results, Creative Commission/VRcation post-play conditional click loss,
  Wildcat's Corp-owned choice with Runner-owned result, Sprint's private exact
  HQ selection/shuffle, Hansei's private mandatory-if-able trash and empty-HQ
  continuation, Planogram's untagged/tagged option sets and both orders, and
  T400's active +1 MU/+1 maximum-hand projections after its paid install.
  Every yielded choice must round-trip JSON/hash and remain visible only to its
  owner; opponent observations must not reveal HQ/Grip/Deck identities.
- Runtime composition tests cover every supplied definition, deterministic
  input reordering/fingerprint equivalence, all ten type mappings, uniqueness/
  console keys, required-field failures, duplicate definitions, and the
  synthetic and official embedded modes without weakening the official opt-in
  boundary. Lifecycle tests receive positive card-test metadata only after the
  real path passes. Mutual Favor and Seamless Launch remain explicitly blocked:
  their search-to-authoritative-install and required-target play-legality
  contracts are the next root-owned design checkpoint.

### 2026-09-03 — Authenticated runtime composition and ten-card lifecycle accepted

- Luna implemented the fixed Cards composition root, explicit condition
  subject, and ten-card real-lifecycle evidence. Root reviewed the mappings,
  provenance/fingerprint boundaries, condition byte compatibility, action
  entry points, choice ownership, events, and cleanup paths. The resulting
  `GatewayRuntimeCardCatalog` returns Core's immutable runtime catalog from
  either an already authenticated dataset or the embedded authenticated
  resource; it maps all supplied definitions independently of behavior
  coverage and retains no rules text or dataset object.
- Root's adversarial review found two production gaps that the positive tests
  exposed. Multi-card behavior selection was resolving every subsequent move
  against the first card's stale transient-zone snapshot; selection now
  resolves the current set-aside zone for every card, with a Core regression.
  A mandatory choice with an authoritative empty candidate set was skipped but
  a later `ChosenCard` instruction still failed. Empty finalization now removes
  the projected `Choose`, appends an explicit empty choice continuation, and
  permits only that proven-empty selector to resolve as an empty no-op; missing
  or forged bindings still reject atomically. Root also corrected test setup
  errors around real T400 install finalization and Hansei's nonempty deck
  precondition before accepting the checkpoint.
- Predictive Planogram now explicitly tests Runner tags. That additive
  serialized subject changed the interim behavior-catalog fingerprint to
  `a74c26c41c2262d9aacfbdedb61c781045d2d344efbc61b2d9c40425b09af15d`;
  both checked-in SIM fixtures carry that provisional value and must be
  refreshed again when the two remaining declarations land.
- Independent final evidence: the focused Core behavior gate passes 29/29;
  the focused Cards runtime/lifecycle/batch gate passes 24/24; complete Cards
  passes 88/88; complete Core, AI, CLI, and Match pass 455/455, 23/23, 52/52,
  and 40/40 respectively. Full-solution formatter verification and
  `git diff --check` pass. The composition/ten-card checkpoint is accepted.

### 2026-09-03 — Architect contract for Seamless Launch and Mutual Favor

- Root owns this remaining Slice 4.2 design. Work is staged deliberately:
  Luna first implements the shared target/precondition seam and Seamless
  Launch; only after root review does Luna implement Mutual Favor on the
  root-designed authoritative install handoff. Luna must not invent free
  installs, infer subtypes from text/behavior coverage, move an installed
  target into the transient zone, or weaken stale/forged validation.
- A declaration's top-level `Targets` are conjunctive play preconditions for
  ordinary Event/Operation `PlayCard`. Every target must materialize at least
  one candidate both when actions are projected and when the submitted turn
  action is revalidated, before clicks, credits, zones, or events mutate. The
  preconditions are not re-applied after `CardPlayFrame` entry because a
  behavior may legitimately consume or move its target. No-target cards are
  absent from the issued action set; stale target availability rejects the
  formerly issued action atomically.
- Installed target contracts may carry an owner filter. Dynamic selection of
  an installed/public target freezes its identity in the behavior binding but
  does not move it to set-aside; only zone/search selections use the transient
  custody path. `AdvanceCard(ChosenCard)` is valid when the frozen chosen card
  still satisfies its original installed-target selector. Seamless Launch is
  a Corp Operation costing its printed one credit, with a top-level and
  mandatory exactly-one `InstalledCardNotThisTurn(owner: Corp)` target. Its
  chosen card remains installed, is revalidated for Corp ownership/location
  and absence from Corp's installed-this-turn facts, then gains exactly two
  advancement counters through the existing checked primitive/event. Tests
  cover projection/no-target exclusion, owner/location/installed-this-turn
  filtering, physical non-movement during choice, exact cost/counters/events,
  save-load/hash, observation ownership, and stale/forged atomic rejection.
- Runtime metadata is authoritative for behavior card filters whenever an
  injected runtime catalog exists. Exact card classes match their runtime
  class; `Icebreaker` means Runner `Program` with normalized `icebreaker`
  subtype; an optional subtype filter is ordinal against runtime subtypes.
  The existing behavior-catalog classification remains only as a catalog-free
  Core-fixture fallback. Mutual Favor searches canonical Runner Stack for one
  runtime Program/Icebreaker, reveals the selected card, shuffles it back into
  Stack deterministically, then adds it to Grip unless Runner made a successful
  run this turn. After such a run it offers Grip and, only when currently
  legal, Install. Search/disposition decisions are Runner-private and preserve
  binding, JSON/hash, and catalog provenance across save/load.
- Mutual's Install instruction never uses the provisional direct transfer.
  It opens the ordinary install pipeline with an additive persisted
  `BehaviorEffect` initiation kind and an exact serialized identity of its
  parent behavior frame. The only admitted topology is Turn, CardPlay,
  BehaviorResolution, then its one owned InstallFrame. Its frozen card must be
  the current `chosen` binding, be a runtime Program in canonical Runner Stack,
  and target Runner Rig. It spends no additional click but pays the printed
  install cost exactly once, applies ordinary uniqueness/console, MU,
  replacement, installed-this-turn, static-modifier, move, and CardInstalled
  semantics, then verifies/restores the exact parent and advances the behavior
  cursor once. Existing basic installs retain their click/source/topology and
  serialized behavior.
- Mutual's Install disposition option is materialized only if that owned
  install plan has at least one legal completion: authoritative metadata and
  source agree, printed cost is affordable, uniqueness permits it, and memory
  can be made legal through the normal replacement choice. The option is
  revalidated when consumed; forged or stale option/binding/source/catalog/
  parent state rejects atomically. If install is unavailable, Grip remains the
  sole disposition. Tests cover search filtering and privacy, reveal/shuffle,
  unsuccessful-run automatic Grip, successful-run option sets, decline,
  unaffordable/unique/MU exclusions, paid no-extra-click authoritative install,
  over-memory replacement, installed facts/modifiers, every yielded
  save-load/hash boundary, and stale/forged trust boundaries.
- Once both declarations pass root review, remove only their explicit blocked
  IDs, require all 12 Batch-1 declarations, refresh the checked-in SIM behavior
  fingerprint, run focused and complete solution gates, then regenerate the
  Slice 4.2 coverage evidence before the completion commit/PR workflow.

### 2026-09-03 — Seamless Launch target/precondition checkpoint accepted

- Luna implemented the bounded shared target seam and Seamless declaration
  from root's contract. Top-level behavior targets now gate ordinary play at
  allocator-free action projection and at the pre-mutation submitted-action
  revalidation boundary. Installed selectors accept only the explicit owner
  filter in this slice; installed choices retain physical custody and freeze
  only their card IDs. `AdvanceCard(ChosenCard)` reconstructs the originating
  catalog choice and continuation, requires an installed-target selector, and
  re-materializes it before applying counters rather than trusting the binding
  or current zone alone.
- Root interrupted the initial worker pass after production/Core changes
  stopped making observable progress, independently ran the partial gate, and
  returned a test-only correction contract. Luna then added the missing real
  Gateway lifecycle and adversarial origin evidence. Root reviewed the final
  production diff, removed acceptance dependence on a bare happy path, and
  verified that Seamless pays one click and one credit once, exposes only the
  Corp-owned target decision, never sets the installed target aside, places
  exactly two counters with `BehaviorCardAdvanced`, and cleans the operation
  to Archives. No-target, wrong-owner, moved, and installed-this-turn cases are
  excluded or reject atomically; a reachable non-installed origin cannot
  authorize chosen-card advancement.
- Seamless Launch is now the eleventh immutable Batch-1 generic declaration
  and only Mutual Favor remains explicitly blocked. Independent evidence:
  focused Core behavior/contracts/play passes 53/53; focused Cards batch and
  lifecycle passes 18/18; complete Core passes 460/460; complete Cards passes
  91/91; full-solution formatter verification and `git diff --check` pass.
  The combined behavior fingerprint remains provisional until Mutual Favor is
  registered, so the SIM pins are intentionally not refreshed at this point.

### 2026-09-03 — Mutual Favor hidden-search architecture amendment

- Root reviewed the current Search cursor against authenticated CR 8.7.2e,
  8.7.3, and 8.7.4 before delegating the final card declaration. A deck search
  with specified criteria must let its controller fail to find even when a
  matching card exists; the searched deck must still be shuffled immediately,
  and instructions referring to a card that was not found fail while the
  remaining effect continues. Mutual Favor therefore must not be gated by a
  top-level Stack target and must not force disclosure of an available
  icebreaker.
- The fixed Core contract is: a one-card Search over Deck/Stack with a
  non-`Any` filter materializes an up-to-one private selection; a zero-card
  result, whether chosen or forced by no candidates, records a reachable empty
  Search `ChoiceSelection` continuation without a binding or set-aside card.
  A following chosen-card `ShuffleIntoDeck` must shuffle its declared Deck or
  Stack and emit a zero-found shuffle event even for that proven-empty result.
  Selected cards remain facedown in transient custody until the shuffle is
  complete. Mutual declares Search without eager reveal, then Shuffle, and
  only then Reveal, preserving CR ordering.
- Add a parameterless serializable `FoundCard` behavior condition so the
  declaration can skip all found-card disposition UI after a failed search.
  Mutual branches on `FoundCard`; only its true arm reveals and then branches
  on `SuccessfulRunThisTurn`. The no-run arm adds the found program directly
  to Grip. The successful-run arm offers `install` plus `grip`, with `install`
  dynamically present only when the already-designed authoritative behavior
  install can complete. Engine and invariant projections must agree on Search
  cardinality, empty continuation reachability, deterministic shuffle state
  and event order, condition evaluation, and all JSON/hash boundaries.

### 2026-09-03 — Mutual Favor authoritative install checkpoint accepted

- Luna implemented root's bounded behavior-owned install handoff and runtime
  Icebreaker filter contract. Runtime metadata is authoritative when injected;
  catalog-free Core fixtures retain the behavior-classification fallback.
  `InstallFrame` now persists an additive `BehaviorEffect` initiation kind and
  exact parent behavior-frame identity while omitted/default fields preserve
  the legacy BasicAction JSON shape. The only accepted behavior topology is
  Turn, CardPlay, BehaviorResolution, InstallFrame for the current canonical
  `chosen` Runner Program proven by a reachable Stack selection.
- Root reviewed the first pass and required parity corrections before
  acceptance: unknown Rig metadata and non-Stack origins fail closed; candidate
  Programs and their exact definition snapshot are frozen in ordinal order;
  alternate chosen selectors are rejected; option projection catches catalog,
  static-modifier, and arithmetic failures; and BehaviorEffect frames require
  a non-null exact candidate-definition snapshot even when empty. Luna also
  added the missing real-path exclusions for unaffordable printed cost,
  impossible MU after every replacement, and active unique conflict.
- The accepted install path spends only the Event's ordinary play click, pays
  the found Program's printed install cost exactly once, reuses normal memory,
  replacement, uniqueness, installed-fact, static-modifier, movement, and
  `CardInstalled` semantics, then verifies and resumes the exact behavior
  parent once. Root independently passed the combined focused behavior,
  contract, play, install, and runtime-filter gate at 90/90, the final
  behavior-install suite at 10/10, and complete Core at 475/475. Luna's full
  Core and formatter gates plus root's `git diff --check` also pass. Mutual's
  remaining work is the accepted hidden-search amendment and card declaration/
  lifecycle integration; this checkpoint does not yet unblock the card ID.

### 2026-09-03 — Hidden-search and FoundCard checkpoint accepted

- Luna implemented root's CR 8.7 amendment as a reusable Core contract. A
  constrained one-card Deck/Stack Search is now a private mandatory up-to-one
  decision with an explicit fail-to-find action; a proven-empty result records
  an authenticated empty Search continuation, clears both the search-key and
  canonical `chosen` bindings, and still performs the declared deterministic
  shuffle with a zero-card typed event. Deck/Stack Search cannot reveal
  eagerly, so found cards remain private until a later explicit `Reveal`.
- The new parameterless `FoundCard` condition is strict, serializable, and
  catalog-fingerprinted. It resolves only from the latest reachable Search
  selection, allowing failed searches to bypass every found-card instruction.
  Runtime and invariant projections agree on Search mode, up-to-one
  cardinality, count, empty continuation provenance, and shuffle source. Forged
  cardinality, non-Search empty continuations, and stale bindings reject.
- Root reviewed the implementation while it was in progress and required both
  Deck and Stack eager-reveal rejection, an actually submitted forged-action
  regression, two consecutive empty-search stale-binding coverage, a
  nonempty-candidate forged-continuation case, and legacy declaration ordering
  updates. After those corrections, root independently passed the combined
  hidden-search/behavior/runtime gate at 54/54 and complete Core at 484/484;
  Luna's formatter and `git diff --check` gates also pass.

### 2026-09-03 — Mutual Favor declaration and lifecycle checkpoint accepted

- Luna implemented the final Cards declaration from root's fixed contract.
  Mutual Favor has no eager play target: it privately searches Runner Stack
  for an authoritative runtime Program/Icebreaker, shuffles before revealing,
  branches away all disposition work when no card was found, returns the card
  automatically to Grip without a successful run, and after a successful run
  offers `install` before `grip` only when the owned real install can complete.
  All twelve assigned Batch-1 definitions are now registered and the explicit
  blocked-definition set is empty.
- Root reviewed six official real-turn lifecycle scenarios. They cover
  subtype filtering and Corp privacy, explicit failure despite an available
  match, automatic empty search, shuffle/reveal/move ordering, an actual
  successful run followed by Grip decline with save/load/hash stability, a
  paid four-credit over-MU install through replacement with no extra click,
  exact installed facts/memory/events, and unaffordable-install suppression.
  The declaration cites CR 8.7 and preserves the zero-credit Event cost.
- Root independently passed complete Core at 484/484 and the complete official
  content-enabled Cards lane at 97/97 after review. Luna also passed the
  default synthetic Cards lane at 97/97, the Cards build without warnings or
  errors, solution-wide formatter verification, and `git diff --check`. The
  executable Slice 4.2 card work is accepted; final SIM fingerprint, generated
  coverage, and full repository verification remain before completion.

### 2026-09-03 — Final architect review amendment and integration artifacts

- Root's final adversarial review found that `FoundCard` authenticated a
  reachable latest card selection and matching canonical binding but did not
  require that selection to originate from `Search`. Root fixed the contract:
  replay the immutable continuation path, inspect the instruction at each
  selection cursor before applying it, and return true only when the latest
  card selection is a nonempty Search result matching the sole canonical
  `chosen` binding. Luna implemented that narrow correction and an executed
  ordinary-Choose false-arm regression. Root independently passed the combined
  hidden-search/runtime/install gate at 50/50; Luna passed Hidden Search at
  10/10, its adjacent gate at 40/40, complete Core at 485/485, formatter, and
  `git diff --check`.
- Root derived the completed immutable Gateway behavior fingerprint as
  `19c8800481f0b65935d4420f0bef019746b5039b5b0159217cc59ae051dc0a66`
  from the authoritative catalog/fixture mismatch, replaced both provisional
  SIM pins, and removed their stale blocked-card note without claiming a
  headless MATCH-401A/AGT-401 outcome. All six simulation-fixture contract
  tests then passed independently.
- From root's fixed metadata contract, Luna marked only Slice 4.2 and its
  twelve owned cards complete, set the batch completion gate, and regenerated
  both checked-in coverage reports. Root reviewed the exact twelve-card
  registry projection and generated counts: `total=77 registered=19 tested=19
  fullTextComplete=17 partial=2 unregistered=58`, with no diagnostics. Luna's
  focused coverage metadata tests pass 8/8, and its fresh-report comparison,
  formatter, and `git diff --check` gates pass. Full repository verification
  remains the final Slice 4.2 acceptance gate.

### 2026-09-03 — Slice 4.2 implementation gate complete

- Root ran `./scripts/verify.sh` after all architect-review corrections and
  generated artifacts were stable. Restore and the full solution build pass
  with zero warnings/errors; complete suites pass Core 485/485, Cards 97/97,
  AI 23/23, CLI 52/52, and Match 40/40. The solution formatter reports no
  changes.
- CLI self-test/doctor, three deterministic M1 outcomes, replay verification,
  and the 32-checkpoint M2 scripted-human/contract-agent proof pass. Official
  System Gateway data validates at 77 cards; every official content-enabled
  Cards/Match/CLI/Godot build passes; missing-input and opt-in diagnostics
  remain exact.
- The freshly generated coverage report matches both checked-in artifacts at
  19 registered/tested, 17 full-text-complete, two partial, and 58
  unregistered. The official 77-card Godot gallery smoke and complete M3 UI
  smoke pass, and the repository secret scan finds no high-confidence secret
  patterns. Slice 4.2 implementation is ready for its completion commit and PR
  into `dev`; remote review feedback remains part of the handoff workflow.

### 2026-09-03 — Slice 4.2 PR handoff

- Root created completion commit `402a7e6` (`feat: complete M4 slice 4.2`),
  pushed branch `m4_2`, and opened GitHub PR #6 into `dev`:
  `https://github.com/Master-Yoba/netrunner/pull/6`.
- GitHub's `build-and-test` check passed in 2m30s. A post-CI sweep found the PR
  merge state clean and found no general comments, submitted reviews, or inline
  review comments, so there is currently no remote feedback requiring a Luna
  correction. The PR remains open for maintainer review.

### 2026-09-03 — PR #6 automated review architecture

- A review submitted after the first handoff sweep raised three P2 findings;
  root inspected each against the production call graph and local M4 rules and
  accepted all three. `MatchCoordinator.Publish` owns the composed behavior
  catalog but calls the legacy two-argument observation adapter, so T400's
  static hand-size modifier is lost. The normal observation-to-view mapper
  then ignores the now-present per-player `maximumHandSize` field and replaces
  it with an unavailable projection. Production must pass the composed catalog
  to `ObservationBuilder`, and the mapper must prefer observed maximums while
  retaining the injected projection only as a legacy-observation fallback.
- The Runner install planner also rejects an active unique/console conflict at
  action materialization. This contradicts the checked-in CR 2.2/3.8/10.3
  contract: installing the newest active copy is legal and older conflicts are
  repaired at checkpoint. Root's bounded correction is to freeze every active
  matching unique/console card into the install candidate set and require its
  removal in the existing serialized install transaction. Program installs
  retain all ordinary MU replacement candidates plus the conflict union;
  non-program Runner installs expose only their exact conflicts. Basic and
  behavior-owned installs, option projection, frame invariants, save/load, and
  stale/forged validation must share the same sorted metadata-derived set.
- Root delegated only these fixed implementations and regressions to Luna Max.
  No Corp rez/activation cleanup or unrelated checkpoint subsystem is added by
  this review correction; facedown Corp installs remain inactive and their
  later activation timing stays with its owning checkpoint.

### 2026-09-03 — PR #6 automated review corrections accepted

- Luna implemented root's observation boundary contract at all three
  `MatchCoordinator` call sites. Explicit requests, Corp-agent input, and the
  published Runner snapshot now use the coordinator's composed behavior
  catalog, so installed static modifiers survive into the public observation.
  The Match mapper consumes the observed per-player `maximumHandSize` first,
  including zero and negative values, and uses the injected projection only
  for legacy observations that omit the field. A coordinator-level T400
  regression proves the composed value reaches both observation and view.
- Luna also implemented root's shared Runner conflict transaction. Active
  matching unique cards and active Consoles are derived solely from canonical
  face-up Rig state plus authoritative runtime metadata, sorted ordinally,
  frozen as required replacements, and trashed before the incoming card is
  activated. Program installs retain their ordinary MU candidates plus that
  conflict union; non-Program installs expose the exact conflict set. Basic
  and behavior-owned planning, option projection, persisted frames, and state
  invariants now derive the same candidates, required IDs, and definition
  snapshot. Unknown active Rig metadata fails closed where a non-Program
  unique/Console decision would otherwise be inferred.
- Root reviewed and accepted the implementation after requiring ordinary and
  behavior-path invariant parity, a coordinator-level observation regression,
  and real basic-action coverage. The final regressions prove behavior-owned
  unique replacement through save/load without an extra click, basic unique
  Program replacement, multi-Console Hardware replacement, and unknown active
  metadata suppression. Root independently passed complete Core at 488/488
  and complete Match at 43/43.
- Root's first repository-wide run passed the zero-warning build and every
  test suite but stopped at three formatter diagnostics in Luna's validator
  edit. Luna made only those whitespace corrections; root identified and sent
  back one additional visually misaligned diagnostic argument. The clean
  rerun of `./scripts/verify.sh` then passed end to end: Core 488/488, Cards
  97/97, AI 23/23, CLI 52/52, Match 43/43, formatter, official builds and
  77-card validation, coverage drift, deterministic CLI/replay proofs, the
  32-checkpoint M2 proof, both Godot smoke lanes, and secret scan. The review
  corrections are ready for commit and PR #6 update.

### 2026-09-03 — PR #6 feedback resolution handoff

- Root committed the accepted corrections as `a21f419` (`fix: address Slice
  4.2 review feedback`) and pushed them to `m4_2`. Each of the three accepted
  inline findings now has a reply identifying the commit and the corresponding
  production/regression correction.
- GitHub's updated `build-and-test` check passed in 2m55s. The post-CI sweep
  confirms PR #6 remains open and mergeable at `a21f419`, with no general
  comments and no new review or inline findings. The only remote feedback is
  the original three P2 findings and root's resolution replies.
- Root pushed the ledger handoff as `662750a`; its branch-head
  `build-and-test` check passed in 2m05s. A final GraphQL review-thread audit
  found exactly the same three replied-to threads and no new findings. Root
  explicitly resolved all three verified threads. PR #6 is therefore green,
  mergeable, and has no outstanding review feedback.

### 2026-09-03 — PR #6 second automated review architecture

- A maintainer-requested second Codex review of branch head `a0936ab` raised
  one new P1 finding. Root reproduced the control flow and accepts it: a Corp
  Draw instruction that exhausts R&D calls the terminal game helper while the
  ordinary Operation still occupies `CorpPlayArea` below its active behavior
  child. Clearing every frame first leaves that public transient card without
  its `CardPlayFrame`; final invariant validation rejects the prospective
  terminal state and rolls the submitted action back.
- The fixed contract is to finish the active ordinary card play before the
  terminal stack clear. At a fatal behavior-owned Corp draw, restore any
  behavior set-aside custody, close the exact behavior child, run the existing
  validated `CardPlayFrame` completion so the Operation moves faceup to
  Archives and emits `CardPlayCompleted`, then complete the real game and emit
  `GameWon`. No later behavior instruction may execute. Standalone behavior
  fixtures without an ordinary-play parent retain their compatibility terminal
  path; transient-zone invariants remain strict.
- Root delegated only that lifecycle implementation and its regressions to
  Luna Max. Required proof covers a real-turn immediate draw Operation and a
  decision-resumed Predictive Planogram draw: the action is accepted, partial
  draws remain committed, all players have zero clicks, the Operation is in
  Archives with an empty play area, pending decision and frames are empty,
  `CardPlayCompleted` precedes `GameWon`, terminal save/load/hash is stable,
  and the final state has no invariant violations.

### 2026-09-03 — PR #6 terminal draw correction accepted

- Luna implemented the bounded fatal-draw handoff using the exact immediate
  Turn/`CardPlayFrame`/behavior-child topology. The helper fails closed when a
  behavior claims CardPlay provenance without that parent, restores any
  set-aside custody, removes only the active behavior child, and reuses normal
  card-play validation/completion before `CompleteTurnGame`. The Operation is
  therefore moved faceup to Archives and `CardPlayCompleted` is emitted before
  the terminal stack clear and `GameWon`; real-turn history is preserved and
  standalone behavior fixtures retain the compatibility path.
- Root rejected Luna's first test-fixture accounting: its sole Core deck card
  was consumed by the Corp turn-start draw, and its official HQ assertion
  counted the Operation after it had entered the play area. Luna corrected the
  proofs to leave exactly one R&D card after turn start and identify that card
  as the behavior-owned partial draw before the next draw fails.
- The accepted regressions cover an immediate synthetic real-turn Operation
  and a save/loaded, decision-resumed Predictive Planogram `draw-first` branch.
  Both prove accepted terminal actions, committed partial draw, zero clicks,
  empty Corp play area, faceup Operation in Archives, empty pending/stack,
  preserved history, `CardDrawn` then `CardPlayCompleted` then final `GameWon`,
  invariant cleanliness, and stable terminal JSON/hash. Root independently
  passed complete Core at 489/489 and complete Cards at 98/98. Full repository
  verification remains before the PR correction commit.
- Root's first full verification passed the zero-warning build and all suites
  (Core 489, Cards 98, AI 23, CLI 52, Match 43), then correctly stopped on
  generated coverage drift because Predictive Planogram gained a second
  lifecycle evidence test. Luna regenerated only the Markdown and JSON
  coverage artifacts; their counts remain `77/19/19/17/2/58` with no
  diagnostics, and fresh-report comparisons are exact.
- The clean `./scripts/verify.sh` rerun then passed end to end: formatter,
  official data and content-enabled builds, coverage drift, deterministic CLI
  outcomes and replay, the 32-checkpoint M2 proof, both Godot smoke lanes, and
  secret scan in addition to all five suites. The P1 correction is ready for
  commit and PR #6 update.

### 2026-09-03 — PR #6 second feedback resolution handoff

- Root committed the accepted P1 correction and regenerated evidence as
  `177da0f` (`fix: complete card play before draw deck-out`), pushed it to
  `m4_2`, and replied to the inline review with the exact lifecycle and
  regression proof. GitHub's `build-and-test` check passed in 2m20s.
- The post-CI review-thread audit found the prior three threads still resolved
  and exactly one new thread: the replied-to P1. Root explicitly resolved that
  verified thread. No other new inline feedback was present at that audit.

## Slice 4.3 work log

### 2026-09-03 — Slice 4.3 architecture and execution checkpoints

- Started from clean branch `m4_3` at merged Slice 4.2 commit `ea6da2b`.
  The baseline contains 489 Core, 98 Cards, 23 AI, 52 CLI, and 43 Match tests;
  the last authoritative `scripts/verify.sh` run is green and coverage is
  19 registered / 17 complete / 2 partial / 58 unregistered of 77.
- Scope is the complete Slice 4.3 contract in `docs/M4_PLAN.md`: ENG-407,
  ENG-407A, ENG-408–413; cards 30003, 30007, 30009, 30014–16, 30018, 30023,
  30024, 30027, 30033, 30053, 30071; full-text completion of 30037; AGT-401
  v2, SIM-401, UI-404, OBS-401, and the standard batch proof/client gates.
  Slice 4.4 mechanics and cards remain excluded even where Slice 4.3 records
  facts that later batches consume.
- Root owns architecture, checkpoint contracts, integration review, the
  progress ledger, independent verification, and local commits. Luna Max owns
  implementation within each bounded checkpoint and does not commit.
- The slice is dependency-ordered into locally committable checkpoints:
  (A) paid-ability declaration/action/frame/cost foundation plus generalized
  encountered-ICE subtype legality; (B) card counters, hosted credit pools,
  removed-from-game movement, typed events, observation, and invariant/schema
  surfaces; (C) active-source occurrence bindings and per-turn/run facts;
  (D) click-run source context, breach modifiers, conditional cost modifiers,
  and their UI calculation projections; (E) Batch 2 declarations and
  card-level lifecycle/hidden-information tests in small related groups;
  (F) contract-agent, sim/headless proof, UI-404 client smoke, coverage/rulings
  regeneration, full verification, and exit evidence.
- Checkpoint A contract: ability activation is a distinct engine-issued action
  carrying source card, stable ability key, frozen typed costs, target snapshot,
  and usage limit. Click-prefixed abilities consume allotted clicks as an
  action; `lose click` is a paid cost and is not interchangeable. Costs are
  validated atomically before mutation, then paid in declared order with the
  existing cost checkpoints. The immutable behavior catalog fingerprints the
  complete ability declaration. A serialized ability frame is reconstructed
  only from that catalog declaration and resumes its behavior child through a
  parent identity, rejecting stale source, zone, target, affordability, or
  usage state. Existing install-time/play-time generic behavior remains
  unchanged. Sentry and `any encountered ICE` matching use normalized subtype
  metadata and the same strength/break-limit legality path as Barrier and Code
  Gate; no title switch is permitted.
- Checkpoint B contract: generic named counters remain canonical in
  `RulesState`; hosted credits are a typed public counter purpose with an
  explicit spending restriction, never recurring credits or player credits.
  Load/place/take/spend operations reject underflow and overflow atomically.
  An empty-pool follow-up runs after the instruction that emptied the pool.
  Removed-from-game is an explicit public zone and self-removal is paid before
  the ability effect; all additions participate in deep copy, canonical JSON,
  replay, invariant validation, and observation filtering.
- Checkpoint C contract: triggers bind typed occurrence keys to an active
  physical source and a controller snapshot. Install, rez, turn-begin,
  successful-run, run-end, score/steal, and access occurrences carry the
  minimum typed context required by declarations. Eligibility is collected at
  the checkpoint from active sources; an invocation retains only the source
  snapshot needed by CR timing after movement. Turn facts are explicit,
  controller-scoped, reset only at formal boundaries, and include successful
  server/central runs, accessed/trashed cards, agenda changes, tag changes,
  and consumed first/once-per-turn keys.
- Every accepted checkpoint requires focused positive/negative, forged/stale,
  save/load, invariant, observation (where applicable), catalog-fingerprint,
  and formatter checks proportional to its schema impact. Root reviews the
  diff and reruns the focused gate before updating this ledger and creating a
  local commit. A checkpoint is not accepted merely because it compiles.

### 2026-09-03 — Checkpoint A1 paid-ability schema accepted

- Luna Max implemented the bounded declaration/schema vertical after root
  rejected an initial alias-heavy API and a single-window model that could not
  represent generally usable paid abilities. The accepted public surface has
  exactly one immutable `BehaviorPaidAbility`, one typed timing vocabulary
  with an explicit any-paid-window value, one activation enum distinguishing
  click actions from paid abilities, one usage-limit/snapshot contract, one
  `UseAbility` action payload, and one serializable `AbilityFrame`.
- Ability declarations freeze costs, conditions, targets, and instructions;
  require unique stable keys; and participate completely in generic catalog
  equality and fingerprints. Click actions require exactly one ordinary Click
  cost and action-phase timing, while paid abilities reject Click and retain
  the distinct `LoseClick` verb. Credits may cost zero; counter, tag,
  lose-click, and additional costs are positive; self-trash is exactly one.
- Issued action/frame shapes retain the source definition/card/zone, ability
  key, timing/activation, exact cost/target/usage snapshots, catalog and
  definition fingerprints, and the frame lifecycle/parent identity needed by
  the next runtime checkpoint. `StateInvariantValidator` accepts only the
  exact action/payload pairing and checks decision kind, source ownership and
  zone membership, target existence, state/catalog pins, and the injected
  declaration shape. No action materialization, cost payment, state mutation,
  card registration, or ICE change landed in A1.
- Root independently passed the zero-warning solution build, focused paid
  ability tests 10/10, complete Core 499/499, Core formatter verification,
  and `git diff --check`. The focused CLI M2 proof suite passed 3/3; the direct
  deterministic proof retained initial/final/event hashes
  `a61f20c7...` / `950d1b0d...` / `d20cdef9...`, 32 checkpoints, and verified
  replay. Existing definition fingerprints are unchanged when no ability or
  additional-cost field is present.

### 2026-09-03 — Checkpoint A2a ability materialization and schema 3 accepted

- Luna Max implemented the first runtime vertical: Core now discovers paid
  abilities only on active installed sources (faceup Runner Rig cards and
  faceup/rezzed Corp server cards), matches click actions to the real action
  window and paid abilities to the current turn/run priority holder, evaluates
  typed conditions and aggregate affordability, and issues exact frozen
  `UseAbility` payloads in physical zone/card/declaration order. Declarations
  needing target selection or an unregistered additional-cost reducer remain
  deliberately fail-closed until their later checkpoint.
- Root rejected the first pass because it normalized duplicate usage records,
  checked repeated counter costs independently, made `Encountering` false in
  the encounter paid window, and did not prove a legal-action bijection. The
  accepted revision preserves forged duplicates for invariant rejection,
  aggregates all wallet/click/tag/named-counter costs with checked arithmetic,
  recognizes an initialized encounter, rejects duplicate self-trash costs,
  and requires an exact missing/extra/duplicate/payload-checked ability action
  set for turn actions, turn timing, and run timing.
- D-416 is applied at this first Slice 4.3 state expansion: engine state schema
  moved from 2 to 3 and `TurnFactsState` now serializes canonical physical
  source/ability usage keys. A new side-turn fact aggregate starts empty;
  mutation is deferred to the execution checkpoint. Replay artifact schema
  remains 1. The M2 semantic proof is unchanged but state hashes were
  deliberately re-pinned: initial `faf4e631...`, final `c882eb43...`, events
  `d20cdef9...`, 32 checkpoints, replay verified.
- Root independently passed the zero-warning solution build, Core formatting,
  `git diff --check`, focused ability schema/materialization tests 17/17,
  complete Core 506/506, and focused CLI/M2 regression tests 32/32. Ability
  consumption, cost mutation/checkpointing, target choices, and child behavior
  execution are explicitly not part of A2a and remain required before ENG-407
  is complete.

### 2026-09-03 — Checkpoint A2b paid-ability execution accepted

- Luna Max implemented the engine-owned execution vertical and root accepted
  it after two review rounds. `UseAbility` consumption now re-materializes and
  compares the exact issued payload, binds a serialized `AbilityFrame` to the
  precise turn/run parent identity, preflights the complete cost bundle before
  mutation, pays costs in declaration order, and executes only the selected
  ability's instruction tree through an exact behavior child. Generic
  install/play behavior continues to start from the definition's ordinary
  instruction root and retains its prior cost lifecycle.
- The accepted lifecycle is `PayCosts` → `CostCheckpoint` → `Execute` →
  `AwaitingChild` → `Complete`. The cost boundary round-trips through canonical
  JSON and resumes without duplicate payment. Credit, click, lose-click, tag,
  named-counter, and self-trash costs are supported; target selection and
  `Additional` costs remain fail-closed for their later checkpoint. A
  once-per-turn frame retains the exact issued zero-use snapshot while the
  post-payment use is recorded separately in current turn facts.
- Completion returns click actions to turn action priority and paid abilities
  to the same turn/run priority holder with consecutive passes reset. Runtime
  and invariant checks prove the active source/catalog/declaration, captured
  source zone (including the narrow paid self-trash exception), parent/child
  topology, usage ledger, and legal turn/run window. Only the top resolution
  frame owns a pending decision, so a prompt-shaped turn/run parent may be
  safely suspended beneath the ability lifecycle.
- Root independently passed the focused ability contract/materialization/
  runtime suite 26/26, complete Core 515/515, the zero-warning solution build,
  Core formatter verification, `git diff --check`, and focused CLI M2 proof
  tests 7/7. The direct M2 proof remains byte-stable at initial/final/event
  hashes `faf4e631...` / `c882eb43...` / `d20cdef9...`, with 32 checkpoints,
  identical scripted/agent results, and verified replay.

### 2026-09-03 — Checkpoint A3 encountered-ICE subtype legality accepted

- Luna Max completed ENG-407A's shared subtype boundary. `IceType` now carries
  concrete Barrier, Code Gate, and Sentry values plus an explicit `Any`
  icebreaker interface. Actual `IceBehaviorSpec` declarations reject the
  wildcard and undefined enum values; `IcebreakerBehaviorSpec` accepts all
  defined concrete/wildcard interfaces.
- One pure `IceInterfaceMatcher` is now the subtype authority for break-action
  materialization, submitted break consumption, and invariant reconstruction.
  Strength, affordability, hosted run-credit division, and per-activation
  break limits remain separate existing legality terms; pump actions remain
  correctly independent of subtype/strength eligibility. No card title switch,
  Carmen declaration, or Slice 4.4 strength-modifier work landed here.
- Focused coverage proves exact Sentry use, wildcard use against all three
  concrete types, concrete mismatch omission and forged-action rejection,
  wildcard rejection as an actual ICE type, invariant agreement, named JSON
  serialization, and catalog equality/fingerprint changes. Root independently
  passed the focused gate 15/15, complete Core 530/530, Core formatting, and
  `git diff --check`; the delegated whole-solution gate passed 746/746 with a
  zero-warning build. The direct M2 proof retains the schema-3 hashes and 32
  replay-verified checkpoints recorded above.

### 2026-09-03 — Checkpoint B1 typed counter/pool state accepted

- Luna Max implemented the state-level half of ENG-408. The canonical
  `RulesState.Counters` collection now distinguishes Generic, Virus, and
  HostedCredit purposes. Hosted-credit entries use the reserved `credit` key
  and carry an explicit CardEffectOnly, AbilityCost, RunCost, or AnyCost
  restriction; legacy three-argument `virus` construction normalizes to the
  typed Virus purpose, while forged purpose/name/restriction combinations and
  nonpositive stored entries fail closed.
- Root rejected the first permissive draft: new loaded pools now default to
  CardEffectOnly rather than AnyCost, generic and Virus counters cannot satisfy
  hosted-credit spending, CardEffectOnly is not a cost permission, and generic
  counter reducers/paid `Counter` costs cannot consume a HostedCredit pool by
  merely naming `credit`. The accepted pure transition service distinguishes
  load/place/take/spend, checks overflow and underflow, and returns zero for
  callers to omit at the immutable state boundary. Virus purge now selects the
  typed purpose and leaves hosted credits untouched.
- Deep copies, canonical JSON, invariant validation, and existing generic
  adjustment/payment paths retain the complete typed data. Declarative hosted
  credit instructions, typed pool events, public observation, empty-pool
  follow-up, and card bindings intentionally remain in B2/C.
- Root independently passed 25/25 focused counter/effect/ability tests,
  complete Core 535/535, Core formatting, and `git diff --check`; the delegated
  Release solution build completed with zero warnings/errors. The direct M2
  proof remains byte-stable at the schema-3 hashes above with 32 replay-verified
  checkpoints.

### 2026-09-03 — Checkpoint B2a hosted-credit declaration schema accepted

- Luna Max added the declarative half of ENG-408 on top of the typed B1 state.
  Behavior programs now distinguish load, place, take-exact, and take-all
  hosted-credit instructions. Load/place normalize an omitted restriction to
  CardEffectOnly; take instructions carry no spending permission. Their amount
  and operand matrices are constructor-validated and preserved by canonical
  JSON, structural equality, behavior-frame replay, and catalog fingerprints.
- Paid abilities now have a distinct positive `HostedCredits` cost kind. It
  cannot carry a generic counter name or additional-cost key and is retained
  by action/frame validation and cloning. Runtime affordability/payment remains
  deliberately fail-closed until B2b wires the declaration to the typed pool;
  generic `Counter` costs continue to reject hosted credits.
- Root independently passed the focused declaration/action/frame gate 17/17,
  complete Core 537/537, the zero-warning solution build, Core formatting, and
  `git diff --check`. The direct M2 proof remains byte-stable at the schema-3
  hashes above with 32 replay-verified checkpoints. No runtime pool mutation,
  events, observation, empty follow-up, or card binding is claimed here.

### 2026-09-03 — Checkpoint B2b-1 hosted-credit instruction runtime accepted

- Luna Max implemented the four declared hosted-credit instruction verbs through
  one engine-owned transition boundary. Load and place create or increment only
  a typed pool with the declaration's preserved restriction; take-exact and
  take-all debit that pool, credit the bound controller, reject insufficient or
  empty pools, and remove the counter entry when its count reaches zero. Generic
  counter adjustment remains unable to create or mutate the reserved pool.
- Root verified the mutation ordering and atomicity contract. Arithmetic and
  restriction failures abandon the working copy without changing the input;
  transfers emit the pool debit before the wallet credit. A new typed
  `HostedCreditPoolChanged` event records Load, Place, Take, TakeAll, or Spend,
  the physical source, signed delta, count after, restriction, transfer
  recipient where applicable, and the positive-to-zero `Emptied` transition.
  The payload participates in polymorphic JSON, deep cloning, and event
  invariant validation. Spend is intentionally declared in this shared event
  vocabulary but will first be emitted by B2b-2 ability-cost payment.
- Root independently passed the focused behavior-runtime gate 31/31, complete
  Core 541/541, the zero-warning solution build, Core formatting, and
  `git diff --check`. The direct M2 proof remains byte-stable at the schema-3
  hashes above with 32 replay-verified checkpoints. Hosted-credit ability
  affordability and payment, public observation, leave-play cleanup,
  empty-pool follow-up, and card bindings remain separate unclaimed checkpoints.

### 2026-09-03 — Checkpoint B2b-2 hosted-credit ability costs accepted

- Luna Max connected `BehaviorCostKind.HostedCredits` to the paid-ability
  legality and payment lifecycle. Materialization and the cost checkpoint use
  the same aggregate affordability projection over the activating physical
  card's single typed pool. AbilityCost and AnyCost pools are eligible;
  CardEffectOnly, RunCost, Generic, Virus, missing, stale, and insufficient
  pools fail closed before any cost mutates state.
- Successful payment retains declaration order and routes every hosted-credit
  cost through the B2b-1 transition boundary. Repeated costs therefore emit
  distinct ordered Spend events with null recipients, the pool's actual
  restriction, successive counts, and `Emptied` only on the final
  positive-to-zero transition. The whole bundle is preflighted first, so a
  rejected or stale action cannot partially debit either counters or wallet.
- Save/load coverage pins the paid CostCheckpoint after the pool debit and
  proves that resuming it does not spend twice. Root independently passed
  focused paid ability runtime/materialization tests 21/21, complete Core
  545/545, the zero-warning solution build, Core formatting, and
  `git diff --check`. The direct M2 proof remains byte-stable at the schema-3
  hashes above with 32 replay-verified checkpoints. Observation, leave-play
  cleanup, empty-pool follow-up, and card bindings remain unclaimed.

### 2026-09-03 — Checkpoint B2c counter observation and leave-play cleanup accepted

- Luna Max completed the viewer boundary for ENG-408. Every visible card path
  conditionally projects a deterministic counter list containing the stable
  name, Generic/Virus/HostedCredit purpose, count, and hosted spend
  restriction. Faceup/public cards expose the same pool to both viewers; a
  hidden HQ/grip/deck/stack/ICE/Archives card exposes its counters only in its
  owner's private card view. Cards without counters omit the property, keeping
  the pinned pre-4.3 observation JSON byte-identical.
- The single card-movement boundary now removes every typed counter when an
  installed card leaves Runner Rig, a server root, or a server ICE position.
  Cost-driven self-trash temporarily defers this cleanup until the complete
  declared cost loop finishes, so TrashSelf may precede generic-counter or
  hosted-credit debits without creating a partial-payment failure. The same
  lifecycle exception is invariant-checked for both ordinary behavior and
  paid-ability children; no counter survives the completed move.
- Root independently passed 75/75 focused observation/effect/behavior/ability
  tests, complete Core 550/550, the zero-warning solution build, Core
  formatting, and `git diff --check`. The direct M2 proof retains the schema-3
  initial/final/event hashes and all 32 replay-verified checkpoints. Triggered
  empty-pool follow-up and card bindings remain unclaimed.

### 2026-09-03 — Checkpoint B3a-1 removed-from-game schema accepted

- Luna Max added a distinct `RemoveSelfFromGame` cost term; it is not encoded
  as trash. Generic and paid declarations permit exactly one total
  self-movement cost, and the new kind participates in construction,
  structural action/frame validation, JSON, equality, cloning, and catalog
  fingerprints. Runtime payment deliberately remains fail-closed until B3a-2.
- Removed cards have separate lazy Corp and Runner zones. Each zone is
  side-owned, serverless, public, and unique when present; default games do not
  allocate either zone, preserving the existing topology and canonical hashes.
  `BehaviorZone.RemovedFromGame` resolves by an explicit target owner, and
  observation conditionally publishes each present zone to both viewers.
- Root rejected the first draft because a facedown card could occupy the
  nominally public zone and disappear from observation. The accepted invariant
  requires every RFG card to be faceup. Root independently passed 24/24 focused
  contract/topology tests, complete Core 555/555, the zero-warning solution
  build, Core formatting, and `git diff --check`; the direct M2 proof remains
  byte-stable at the schema-3 hashes with 32 replay-verified checkpoints.

### 2026-09-03 — Checkpoint B3a-2 self-removal runtime accepted

- Luna Max wired the distinct self-removal cost through generic behavior and
  paid-ability payment. The engine lazily creates the source owner's RFG zone,
  moves the physical source there faceup, emits the ordinary typed CardMoved
  history entry, and continues the already-authorized instruction child from
  its frozen source/catalog snapshot. Affordability recognizes the cost as a
  supported non-resource term but still requires an active source at payment.
- Runtime and state invariants now distinguish the two legal post-payment
  exceptions exactly: TrashSelf permits only the owner's discard, while
  RemoveSelfFromGame permits only the owner's RFG zone. Counter cleanup remains
  deferred through the full cost loop, so a declared removal before generic or
  hosted-credit costs pays in order and clears all residual counters only at
  the bundle boundary. Save/load at CostCheckpoint proves the move is not
  repeated on resume.
- Root required an additional forged-state test after the first report lacked
  the claimed evidence. The accepted test relocates a removal-paid source to
  heap and proves both the source-location invariant and resolution fail
  closed. Root independently passed 51/51 focused behavior/ability runtime
  tests, complete Core 560/560, the zero-warning solution build, Core
  formatting, and `git diff --check`; the direct M2 proof remains byte-stable
  at the schema-3 hashes with 32 replay-verified checkpoints.

### 2026-09-03 — Checkpoint B4a Corp unique rez cleanup accepted

- Luna Max added the missing activation checkpoint for Corp unique cards.
  After a card is successfully rezzed, that serialized action identifies it
  as the newest active copy; every other known, faceup, rezzed Corp card with
  the same non-null uniqueness key is moved from any server root or ICE zone
  to Archives in stable physical-card-ID order. Unknown and non-unique runtime
  definitions are not inferred from title text and remain unaffected.
- The checkpoint emits CardRezzed before the deterministic CardMoved cleanup
  sequence. Cleanup uses the central movement boundary, so all typed counters
  on the older copies are removed. Corp installation remains legal while the
  incoming unique card is facedown; uniqueness becomes active at rez. Runner
  installs retain their existing exact required-replacement contract for all
  active unique/Console conflicts, making the incoming install the newest.
- Root independently passed focused runtime rez tests 17/17, complete Core
  561/561, the zero-warning solution build, Core formatting, and
  `git diff --check`. The direct M2 proof retains the schema-3 hashes and 32
  replay-verified checkpoints. Catalog-aware forged active-conflict invariants
  remain B4b.

### 2026-09-03 — Checkpoint B4b active unique/Console invariant accepted

- Luna Max extracted one pure runtime-card activity projection used by both
  rez cleanup and catalog-aware state validation. A known Runner card is active
  only while faceup in the Rig; a known Corp card is active only while faceup
  and rezzed in a server root or ICE zone. Hidden/inactive cards and unknown
  definitions are never classified from title text.
- With the injected runtime catalog, state validation now rejects more than one
  active card controlled by a side for the same non-null uniqueness key and
  rejects more than one active Runner Console, regardless of the individual
  Console keys. Opposite sides do not conflict. Because install and rez frames
  serialize the single newest activation and cleanup is synchronous, no
  separate activation ledger or persistable illegal intermediate is needed.
- Root required the B4a normal-path fixture to begin from one valid older copy;
  multiple active copies now belong only to forged-state tests. Root
  independently passed 48/48 focused install/rez tests, complete Core 564/564,
  the zero-warning solution build, Core formatting, and `git diff --check`.
  The direct M2 proof retains the schema-3 hashes and 32 replay-verified
  checkpoints. ENG-407 unique/Console enforcement is complete at this boundary.
- A delayed delegated test-only correction aligned the pre-existing Runner
  Console install scenario with the same invariant: its normal path now starts
  from one active Console and still proves that replacement is mandatory before
  the incoming newest Console enters the Rig. The focused Runner install suite
  passed 18/18 before the follow-up local commit; the forged two-Console test
  remains the sole multi-active fixture.

### 2026-09-03 — Checkpoint C1 typed behavior-trigger declarations accepted

- Luna Max replaced the unused string occurrence placeholder with an immutable
  trigger declaration: stable trigger key, typed occurrence, mandatory/optional
  mode, Self/Controller/Any scope, optional typed subject-card filter, and a
  non-empty frozen behavior-instruction tree. Definitions reject duplicate
  trigger keys, and structural equality plus catalog fingerprints include the
  complete nested declaration.
- The occurrence vocabulary now names card install, card rez, successful run,
  run end, agenda score-or-steal, card access, and hosted-credit-pool empty.
  Root corrected the initial card-bearing classification: run occurrences carry
  their initiating source for Conduit and Red Team, while score-or-steal carries
  the agenda for Pantograph-era filtering. Consequently every new gameplay
  occurrence supports Self scope and subject filters; legacy turn/effect
  occurrences that lack card context reject them.
- JSON round trips preserve the typed declarations and copied collections stay
  immutable. Root independently passed the focused behavior-contract gate
  11/11, complete Core 567/567, the zero-warning solution build, Core
  formatting, and `git diff --check`. The direct M2 proof retains the schema-3
  hashes and all 32 replay-verified checkpoints. Runtime occurrence dispatch,
  trigger scheduling, and source-snapshot execution remain the next checkpoint.

### 2026-09-03 — Checkpoint C2a-1 catalog-trigger runtime seam accepted

- Luna Max added a typed, persistable gameplay-occurrence marker for the seven
  C1 occurrence kinds. It is a no-op effect that traverses the existing M2
  imminent/resolved checkpoint pipeline and retains the occurrence controller,
  optional subject card, and optional server context. A friend-visible test
  seam opens this exact path; production install/rez/run/score/access hooks are
  deliberately deferred rather than simulated here.
- Materialized trigger invocations now discriminate legacy registered effects
  from catalog behavior triggers. The catalog form stores only its stable key,
  physical source/controller/zone snapshot, occurrence, and catalog/definition
  provenance; its instruction tree is reconstructed from the injected catalog
  by a mutually exclusive `TriggerKey` behavior child. Deterministic IDs derive
  from occurrence, source, definition, and trigger key, and catalog candidates
  join the existing active-player/controller/stable-ID ordering pipeline.
- Eligibility is limited to active installed sources and applies typed
  Self/Controller/Any scope plus subject-card filters. The checkpoint seeds the
  reserved `trigger-subject` binding, and runtime/state validation ties it to
  the exact parent invocation across yielded JSON save/load. Child topology is
  discriminated: catalog triggers require their matching behavior frame while
  legacy triggers and replacements retain effect-resolution children. Stale
  child IDs are cleared when leaving the awaiting-child stage.
- Root rejected an early test assertion that read final credits before resolving
  the correctly yielded simultaneous-order decision and required strict yielded
  child provenance tests. The accepted gate passes focused catalog-trigger
  tests 6/6, complete Core 573/573, the zero-warning solution build, Core
  formatting, and `git diff --check`. The direct M2 proof remains byte-stable at
  the schema-3 hashes with 32 replay-verified checkpoints. Source movement after
  materialization, trigger-body self movement, fuller simultaneous ordering,
  and real lifecycle hooks remain explicit C2a-2/C2b work.

### 2026-09-03 — Checkpoint C2a-2 detached trigger timing accepted

- Luna Max completed the CR source-snapshot contract. Catalog invocations now
  retain source owner side and zone kind as well as card/definition/zone IDs.
  Once an active source has materialized a trigger, later physical movement no
  longer erases the pending object: child creation proves the same owned card
  and definition against the frozen snapshot and executes from that snapshot
  without retargeting to the card's current zone. Missing, changed-definition,
  changed-owner, malformed, or provenance-mismatched sources fail closed.
- A persisted detached-before-start authorization remains valid for the whole
  trigger child, including cursor progress, yielded choices, and JSON resume.
  A source that was still in place when its child began receives no blanket
  movement exception. It may continue after moving only when its reconstructed,
  reachable instruction prefix has actually passed `TrashSelf`, and then only
  from its owner's discard. The central movement boundary clears typed counters;
  forged movement before that cursor or to a wrong destination is rejected.
- Simultaneous catalog triggers retain the existing timing rules: active-player
  controller first, explicit same-controller ordering, stable deterministic IDs,
  and identical order/hash after save/load. Root independently passed the
  expanded focused catalog-trigger gate 13/13, complete Core 580/580, the
  zero-warning solution build, Core formatting, and `git diff --check`. The
  direct M2 proof remains byte-stable at the schema-3 hashes with all 32
  checkpoints replay verified. Generic trigger execution and movement timing
  are complete; production lifecycle occurrence hooks remain C2b.

### 2026-09-03 — Checkpoint C2b-1a formal turn-begin triggers accepted

- Luna Max connected catalog declarations to the already-serialized formal
  `TurnBegin` boundary. The normal active-source scan and detached snapshot
  machinery now collects Controller-scoped triggers only for the beginning
  player's sources and Any-scoped triggers across both sides; hidden/inactive
  cards remain ineligible. Self scope is rejected defensively because a turn
  boundary has no card subject.
- Cross-controller ordering still begins with the active player, and a yielded
  boundary resumes with the same queue after JSON load without collecting or
  applying a trigger twice. Root split hosted-pool empty integration out after
  identifying that an inner empty trigger can trash the source of its outer
  transfer behavior; that parent lifecycle needs an explicit narrow contract,
  not a general moved-source exception.
- Root independently passed the expanded catalog-trigger gate 16/16, focused
  TurnShell/Checkpoint regressions 32/32, complete Core 583/583, the zero-warning
  solution build, Core formatting, and `git diff --check`. The direct M2 proof
  remains byte-stable at the schema-3 hashes with 32 replay-verified checkpoints.
  Hosted-credit empty, install, and rez production hooks remain unclaimed.

### 2026-09-03 — Checkpoint C2b-1b hosted-pool empty triggers accepted

- Luna Max routed behavior-driven Take and TakeAll positive-to-zero transitions
  through `HostedCreditPoolEmptied`. A non-empty remainder raises no occurrence.
  The transfer instruction advances first, then a serialized
  `AwaitingOccurrence` behavior continuation sits below the ordinary effect /
  checkpoint pipeline, preventing save/load or resume from repeating either the
  debit or the occurrence.
- The continuation is tied to the exact behavior frame, post-instruction cursor,
  source card, controller, and typed empty marker. When the nested occurrence
  finishes, the outer source must still occupy its captured zone unless a
  reachable empty-pool trigger explicitly moved it to its owner's discard with
  no hosted-credit counter remaining. Only that completed result receives a
  narrow persisted authorization to run later outer instructions; wrong subject,
  missing continuation, wrong destination, stale owner/definition, or an
  unrelated movement fails closed.
- Event evidence fixes the order as hosted-pool debit, wallet credit, then
  nested trigger movement/effects. Mandatory self-trash completes both inner
  and outer frames and clears all counters; optional use/decline and an inner
  post-trash choice round-trip through JSON without duplication. Root passed
  the catalog/hosted focused gate 22/22, behavior/ability regressions 51/51,
  complete Core 589/589, the zero-warning solution build, Core formatting, and
  `git diff --check`. The direct M2 proof retains all schema-3 hashes and 32
  replay-verified checkpoints. Ability-cost Spend depletion and other lifecycle
  occurrence hooks remain separate work.

### 2026-09-03 — Checkpoint C2b-2a card-install triggers accepted

- Luna Max connected every production runtime-catalog install completion to
  the typed `CardInstalled` occurrence: basic Runner installs (including
  required replacement selection), Corp root/ICE installs, and installs driven
  by a behavior instruction. The install mutation and `CardInstalled` event
  commit first, the install frame is removed, and the turn or behavior parent
  is advanced before the ordinary serialized occurrence/checkpoint effect is
  pushed. Optional triggers can therefore yield and round-trip without paying,
  moving, or emitting the install a second time.
- Eligibility observes the committed board. A newly installed faceup Runner
  source can trigger itself; a facedown Corp subject remains available to an
  already-active external Corp source through its typed subject filter. On a
  required Runner Console replacement, the old source reaches Heap before
  collection and cannot contribute a controller trigger, while the incoming
  active Console can contribute its self trigger. A behavior-effect install
  resumes the already-advanced outer instruction exactly once after nested
  install triggers finish.
- Root corrected two delegated fixture/API defects and required the missing
  replacement-order evidence before acceptance. The final focused install and
  catalog-trigger gate passes 65/65, complete Core passes 594/594, the solution
  build completes with zero warnings/errors, Core formatting and
  `git diff --check` pass, and the direct M2 proof retains initial/final/event
  hashes `faf4e631...` / `c882eb43...` / `d20cdef9...` with all 32 checkpoints
  replay verified. Rez, run, agenda, and access lifecycle hooks remain separate
  C2b work.

### 2026-09-03 — Checkpoint C2b-2b card-rez triggers accepted

- Luna Max connected the production runtime-catalog rez reducer to
  `CardRezzed`. The reducer captures the installed server, pays the exact issued
  cost, commits faceup/rezzed state, emits `CardRezzed`, and completes stable
  Corp unique cleanup before restoring its parent. It then resets the existing
  Corp turn priority or run priority (including consuming the approached-ICE
  rez permission) before pushing the ordinary serialized occurrence effect.
- Trigger collection therefore sees the post-rez board. The incoming card is
  active and may trigger itself; an older rezzed unique copy has already moved
  to Archives and cannot contribute its controller trigger. Turn and approach
  run tests fix event ordering and prove the run parent retains its step,
  priority holder, zero consecutive passes, non-ICE permission, and consumed
  approached-ICE permission. An optional turn-rez trigger round-trips through
  canonical JSON and declines without repeating payment, rez state, or the
  `CardRezzed` event.
- The initial delegated run terminated before its final report; a fresh Luna
  Max pass repaired its one incomplete fixture declaration, after which root
  independently reviewed and gated the resulting patch. Focused
  rez/catalog-trigger tests pass 46/46, complete Core
  passes 598/598, the solution build has zero warnings/errors, Core formatting
  and `git diff --check` pass, and the direct M2 proof remains byte-stable at
  `faf4e631...` / `c882eb43...` / `d20cdef9...` with 32 replay-verified
  checkpoints. Run-success/end, agenda, and access hooks remain separate C2b
  work.

### 2026-09-03 — Checkpoint C2b-3a successful-run triggers accepted

- Luna Max connected `StepRunSuccess` to the catalog occurrence pipeline. The
  engine emits `RunSuccessful`, records the turn fact, and advances both live
  run state and its timing frame to `BreachBoundary` before queuing the marker.
  The occurrence carries the initiating player, optional physical run source,
  and attacked server. Trigger effects consequently finish before breach starts,
  and an optional trigger can yield/save/load without declaring success twice.
- Run-stack validation permits only the exact matching RunSuccessful occurrence
  suffix at that boundary and rejects a forged controller/source/server tuple;
  the generic effect/checkpoint validators continue to own every deeper frame.
  Focused evidence covers Controller and Any scopes, active/inactive/unknown
  sources, same-controller ordering, run-event subject filtering, self-scope
  exclusion for the inactive play-area event, canonical JSON resume, event
  order, and a clean `RunTimingTrace`.
- Root rejected the first green semantic patch because even a triggerless M2
  run allocated no-op occurrence frames and changed the pinned final state hash.
  The accepted reusable `HasActiveCatalogTrigger` gate skips only an occurrence
  for which no active installed source declares the kind; full eligibility
  remains centralized in the checkpoint collector. Focused run/catalog tests
  pass 36/36, complete Core passes 601/601, the solution build has zero
  warnings/errors, Core formatting and `git diff --check` pass, and the direct
  M2 proof is restored to `faf4e631...` / `c882eb43...` / `d20cdef9...` with
  32 replay-verified checkpoints. Run-end, agenda, and access hooks remain.

### 2026-09-03 — Checkpoint C2b-3b run-end outcome triggers accepted

- Luna Max extended a RunEnded trigger with an optional typed
  `RunOutcomeRequirement` (`Successful` or `Unsuccessful`). The field is legal
  only on RunEnded, participates in immutable equality, canonical JSON, and the
  catalog fingerprint, and is matched centrally against a required persisted
  success boolean on `GameplayOccurrenceEffect`. Other occurrence kinds reject
  that context, while a RunEnded marker without it is invalid.
- `CompleteRun` snapshots the initiating player/source, attacked server, and
  outcome, then performs the complete existing run-end transaction first:
  expire run credits, emit `RunEnded`, move the active run event, clear live run
  state, pop its timing frame, and restore the real-turn action parent. Only
  then does it conditionally queue the occurrence when an active declaration
  exists. A successful-only trigger therefore fires after cleanup, while a
  jack-out selects only an unsuccessful-only trigger. An optional real-turn
  trigger round-trips with `RunState` already null and the turn parent already
  at action priority, then declines without repeating cleanup or `RunEnded`.
- Root isolated and returned an invalid unsuccessful-run fixture that omitted
  its ICE behavior spec; the accepted Luna Max repair uses bounded timing
  progression and the registered fixture ICE. The generic state invariant now
  also proves an occurrence checkpoint retains the exact typed context of its
  parent effect frame. Focused run/catalog tests pass 40/40, complete Core
  passes 605/605, the solution build has zero warnings/errors, formatting and
  `git diff --check` pass, and the M2 proof retains `faf4e631...` /
  `c882eb43...` / `d20cdef9...` with 32 replay-verified checkpoints. Agenda and
  access hooks remain C2b work; installed-card run-source self matching remains
  coupled to ENG-411's click-run source state.

### 2026-09-03 — Checkpoint C2b-4 agenda score/steal triggers accepted

- Luna Max connected the two production agenda paths to the shared
  `AgendaScoredOrStolen` occurrence without touching the toy reducer. A
  nonterminal Corp score moves and reveals the agenda, adds points, emits
  `AgendaScored`, cleans its empty remote, and advances the turn frame to fresh
  action priority before dispatch. A nonterminal automatic Runner steal moves
  and reveals the agenda, adds points, emits `AgendaStolen`, and advances the
  breach cursor before dispatch.
- The steal path now separates breach-state advancement from immediate decision
  yielding only when a matching active declaration exists. After its nested
  trigger finishes, ordinary run advancement yields the next candidate or ends
  the run; the no-trigger path is unchanged. Typed agenda subject filtering and
  Controller/Any scope are evaluated from the winner, agenda, and attacked
  server snapshot. An optional score round-trips after move/points/remote
  cleanup and declines without duplicating any of them.
- The seven-point score/steal boundary remains terminal and does not leave a
  post-score trigger frame beneath `CompleteTurnGame`; later agenda-self source
  activity is outside this Slice 4.3 external Pantograph vertical. Root passed
  the focused score/access/catalog gate 25/25, complete Core 608/608, a
  zero-warning/error solution build, Core formatting, and `git diff --check`.
  The direct M2 proof remains `faf4e631...` / `c882eb43...` / `d20cdef9...`
  with 32 replay-verified checkpoints. Card-access dispatch is the remaining
  C2b lifecycle hook.

### 2026-09-03 — Checkpoint C2b-5 card-access triggers accepted

- Luna Max connected real breach candidate selection to `CardAccessed`. The
  reducer removes or deterministically selects the candidate, persists the
  chosen/accessed IDs, emits `BreachCandidateChosen` and `CardAccessed`, and
  advances the run state/frame to `BreachAccessAbility` before dispatch. With
  an active declaration, the normal mid-access decision is delayed until the
  nested trigger finishes; without one, the existing direct yield is unchanged.
- Mandatory evidence fixes the ordering as candidate chosen → card accessed →
  trigger effect → access window. An optional typed agenda-filter trigger
  round-trips with the exact candidate already consumed, then declines into the
  ordinary `NoAccessAction` decision without rerandomizing, reselecting, or
  emitting either access event again. Inactive same-definition and active
  unknown cards do not contribute invocations, and the completed trace through
  automatic agenda steal and run end remains valid.
- The first optional test exposed that live-run stack validation recognized
  only RunSuccessful occurrence suspension. Root required an exact generalized
  predicate: RunSuccessful at `BreachBoundary`, CardAccessed at the matching
  latest access, AgendaScoredOrStolen at its post-steal cursor, and CardRezzed
  for the matching active rezzed Corp card/server during a paid run step.
  Arbitrary effect frames or mismatched controller/subject/server/step remain
  invalid. Root passed 58/58 combined lifecycle tests, complete Core 610/610,
  the zero-warning/error solution build, formatting, and `git diff --check`;
  M2 stays at `faf4e631...` / `c882eb43...` / `d20cdef9...` with 32 verified
  checkpoints. All ENG-409 occurrence kinds now have production dispatch;
  card bindings and ENG-411 installed run-source state remain later work.

### 2026-09-03 — Checkpoint C3a ENG-413 turn-fact contracts accepted

- Luna Max implemented the state/API half of ENG-413. Side-specific turn facts
  now retain first-seen run-attempt and successful-run server sets, ordered
  access and trash occurrences (including repeated physical IDs), typed agenda
  score/steal and tag deltas, and counted first-X consumption keyed by physical
  source plus stable purpose. Immutable transitions preserve every ledger, use
  checked accumulation, and expose one canonical first-X count query.
- Root rejected an ephemeral legacy marker whose meaning changed after a JSON
  round trip and removed redundant accessor aliases. The stable compatibility
  rule accepts the existing `SuccessfulRun = true` shape with no server detail,
  while new server-bearing success facts require the success flag and a matching
  attempted server. Historical server IDs are deliberately not required to
  remain on the current board because an accessed/trash-cleared remote can be
  removed later in the same turn.
- Constructors and history copies freeze nested collections and records;
  canonical JSON retains all new facts. Invariants reject malformed IDs,
  duplicate run/first-X keys, success-without-attempt, unknown card/player
  references, Corp run facts, mismatched agenda kind/beneficiary/turn side,
  non-Runner tag targets, and invalid counts. A formal `TurnHistory.Start`
  moves the complete prior ledger to last-turn facts and creates an empty new
  current ledger without mutating the prior history.
- Root independently passed focused turn-fact contracts 7/7, complete Core
  617/617, the zero-warning/error solution build, Core formatting, and
  `git diff --check`. The direct M2 proof remains byte-stable at initial/final/
  event hashes `faf4e631...` / `c882eb43...` / `d20cdef9...`, with all 32
  checkpoints replay verified. Production fact recording and observation
  projection remain the next ENG-413 checkpoints.

### 2026-09-03 — Checkpoint C3b ENG-413 observation projection accepted

- Luna Max extended the existing viewer-scoped turn-facts projection with
  ordered attempted/successful server IDs, total access/trash occurrence
  counts, visibility-filtered occurrence IDs, public typed agenda/tag changes,
  and visibility-filtered once-per-turn/first-X source keys. The projection
  reuses the observation's live breach-visible set, so a currently accessed
  hidden Corp card is visible without making unrelated HQ/R&D cards public.
- Root fixed two privacy-fixture expectations during review: each owner may
  see its own hidden card/source IDs, while the opponent may not, and a
  detached-array test must use a card actually visible to its viewer. Repeated
  visible occurrence IDs retain order, but total public counts do not shrink
  when hidden identities are filtered. Agenda and tag records are projected as
  value dictionaries rather than leaking engine record objects.
- The new fields remain conditional on existing formal turn history, preserving
  the exact M1/M2 observation shape when history is absent. Every projected
  array/dictionary is detached, repeated builds serialize byte-identically,
  and hidden source IDs cannot leak their stable ability/first-X keys.
- Root independently passed focused observation tests 18/18, complete Core
  619/619, the zero-warning/error solution build, Core formatting, and
  `git diff --check`. The direct M2 proof remains byte-stable at
  `faf4e631...` / `c882eb43...` / `d20cdef9...`, with all 32 checkpoints
  replay verified. Production ENG-413 recording remains the next checkpoint.

### 2026-09-03 — Checkpoint C3c-1 ENG-413 lifecycle recording accepted

- Luna Max added one formal-turn fact mutation boundary and connected it to
  the shared production run initiation, run success, breach access, real agenda
  score/steal, and tag-adjustment reducers. Each helper updates only the active
  side's current formal history while preserving the turn cursor verbatim;
  legacy/toy states without formal history remain no-ops and allocate no new
  frames or events.
- Run initiation records the attacked server once, success records the same
  server as both attempted and successful, and jack-out retains only the
  attempt. Access is appended after its chosen/accessed evidence and before
  trigger dispatch, so an optional trigger JSON resume cannot duplicate it.
  Nonterminal and terminal Corp scores plus terminal Runner steals retain the
  typed physical agenda, beneficiary, kind, and printed points after the
  authoritative move/point event. Central nonzero tag deltas record both gains
  and removals against the Runner in their actual occurrence order.
- Root replaced a null-forgiving helper draft with an explicit nullable-history
  guard and removed a redundant second jack-out fixture change: the dedicated
  runtime test owns formal unsuccessful-run evidence while the established
  run-trigger test keeps its original standalone contract. Semantic trash
  recording remains deliberately separate because ordinary discard/movement
  must not be misclassified as trash.
- Root independently passed the reviewed lifecycle-focused gate 41/41,
  complete Core 622/622, the zero-warning/error solution build, Core
  formatting, and `git diff --check`. The direct M2 proof remains byte-stable
  at `faf4e631...` / `c882eb43...` / `d20cdef9...`, with all 32 checkpoints
  replay verified.

### 2026-09-03 — Checkpoint C3c-2 ENG-413 semantic trash recording accepted

- Luna Max completed the trash half of ENG-413 through one validated
  `TrashCard` boundary. A semantic trash must move a physical card to its
  owner's root Archives/Heap and then append exactly one active-turn
  occurrence. Destination alone never implies trash: ordinary discard,
  generic move effects, set-aside restoration, shuffle, score/steal,
  install/play-area entry, draw, and remove-from-game remain plain movement.
- The audited trash paths are behavior-selected/additional trash and TrashSelf
  instructions/costs, access trash, damage, memory cleanup, tagged-resource
  trash, event/operation completion, active run-event completion, Runner
  Console/unique replacement, and Corp unique cleanup on rez. Existing event
  order, visibility normalization, counter cleanup deferral, and program-memory
  cleanup remain unchanged; working-copy failure remains atomic.
- Production coverage proves ordered physical IDs from damage, one access
  trash, a self-trash cost retained exactly once across a yielded JSON resume,
  tagged-resource trash, terminal operation cleanup, and the formal-discard
  exclusion. Root reviewed every remaining raw `MoveCard` call to confirm it is
  one of the explicit non-trash movements.
- Root independently passed a broad affected-subsystem gate 218/218, complete
  Core 623/623, the zero-warning/error solution build, Core formatting, and
  `git diff --check`. The direct M2 proof remains byte-stable at
  `faf4e631...` / `c882eb43...` / `d20cdef9...`, with all 32 checkpoints
  replay verified. ENG-413's schema, observation, lifecycle, reset, and trash
  recording contracts are now complete; later cards consume the first-X API.

### 2026-09-03 — Checkpoint C4 ENG-410 Spin Doctor accepted

- Luna Max bound Spin Doctor into both the typed behavior catalog and the
  established asset lifecycle: rez draws two cards, access trash costs two,
  and an unlimited any-paid-window ability removes the rezzed source from the
  game before its Corp-owned Archives choice is exposed. The choice selects up
  to two Corp cards through an explicit binding, moves exactly those cards to
  R&D, and produces exactly one deterministic shuffle, including when zero
  cards are selected.
- Root's end-to-end review exposed that the generic shuffle continuation only
  recognized an empty searched zone, not a deliberate empty `UpTo` choice.
  The continuation proof was extended narrowly: it reconstructs the immutable
  catalog instruction and requires the exact choice key, source zone,
  permissive cardinality, card owner, and owner-correct deck destination.
  Exact-cardinality and forged-destination continuations remain atomic
  rejections, while the existing empty-search contract remains intact.
- Production coverage proves 0/1/2-card resolutions, pre-choice self-removal,
  public removed-from-game visibility for both viewers, Corp-only choice
  ownership, exact candidate privacy, JSON resume/hash stability, unique zone
  membership, and the zero-selection shuffle event.
- Root independently passed the affected Core gate 54/54, the focused Cards
  gate 17/17, complete Core 626/626, complete Cards 102/102, the zero-warning
  solution build, solution formatting, and `git diff --check`. The direct M2
  proof remains byte-stable at `faf4e631...` / `c882eb43...` /
  `d20cdef9...`, with all 32 checkpoints replay verified.

### 2026-09-03 — Checkpoint D1 ENG-411 run-action architecture opened

- Root fixes the generic contract before card binding: a `StartRun`
  instruction carries one immutable typed run declaration rather than a card
  title or free-form argument. The declaration names a fixed existing server
  kind, an optional not-run-this-turn constraint, and optional source-counter
  breach-access scaling with a stable public reason key. Red Team can expose
  one action per eligible central; Conduit names R&D directly, so no new
  server-choice frame is needed.
- An installed-card run source is run provenance, not a played run event.
  Executing the final instruction of a click ability must retire its behavior
  and ability cursors before nesting the ordinary run timing frame directly
  under the unchanged `AwaitingAction` Runner turn frame. The source remains
  active in the Rig, `InitiatingSource` identifies it, and
  `ActiveRunEventId` remains null; only the latter owns play-area movement,
  run credits, and run-end trash.
- Legality is checked both when the action is materialized and when it is
  consumed. It requires an active faceup Runner source, a unique server of the
  declared kind, current formal Runner turn facts for history constraints, and
  a final `StartRun` instruction. The existing basic-run and standalone
  run-event adapters remain byte-compatible.
- Any extra central accesses are snapshotted at initiation into immutable
  run-scoped modifier entries (source, attacked server, positive delta, stable
  reason). Breach construction adds only modifiers for that exact server;
  later source-counter mutation cannot change the active run. Empty modifiers
  use an omitted nullable state field so legacy M2 canonical JSON and proof
  hashes do not churn. Both observations expose detached ordered modifier
  reasons without revealing new hidden card identity.

### 2026-09-03 — Checkpoint D2 ENG-412 cost-quote architecture opened

- Root separates conditional cost calculation from static board-value
  modifiers. A behavior definition may declare an immutable self cost
  adjustment for Runner install or icebreaker break cost, with one typed turn
  condition, nonzero delta, and stable reason key. Slice 4.3 evaluates only
  `SuccessfulRunThisTurn`: Carmen's own definition is evaluated while the card
  is still in Grip, and Marjanah's own active breaker definition is evaluated
  during its encounter interface. Other installed cards and card titles are
  never scanned for an implicit adjustment.
- One frozen credit quote carries the printed/base value, ordered typed
  adjustments with their declared reason/source, and the nonnegative effective
  value. Runner install actions and frames and encounter break actions retain
  that exact quote across JSON boundaries; affordability, hosted/wallet split,
  invariant re-materialization, and payment all consume the effective value.
  Pump cost is deliberately outside this checkpoint.
- Missing formal current-turn facts make the condition false. Forged or stale
  base/effective/reason/source data must be rejected without mutation, checked
  arithmetic rejects overflow, and reductions clamp at zero. Unmodified
  actions omit the optional quote extension so existing canonical action/frame
  bytes remain unchanged; modified legal-action observations expose the
  detached base, effective, and ordered reason data.

### 2026-09-03 — Checkpoint E1 hosted-economy binding opened

- Luna Max is binding the already-expressible hosted-economy group without
  widening Core: Pennyshaver, Telework Contract, Smartware Distributor,
  Regolith Mining License, and Nico Campaign's missing full text. Static MU,
  install/rez loads, successful-run/turn-begin placements or withdrawals,
  click actions, once-per-turn use, and empty-pool cleanup must remain typed
  catalog declarations composed in deterministic code order.
- This checkpoint may not infer hosted credits from ordinary counters or
  player credits. Both viewers see the public hosted pool, take/place effects
  preserve exact wallet/pool deltas, and source-triggered empty cleanup occurs
  after the operation that empties the pool. If Smartware's optional turn-begin
  withdrawal from an empty pool is not safely expressible, the binding stays
  blocked and the generic gap is returned to Root rather than handled by a
  card-name branch.

### 2026-09-04 — Checkpoint E1a hosted-economy expressible subset accepted

- Luna Max bound Pennyshaver, Telework Contract, Regolith Mining License, and
  Nico Campaign's remaining printed behavior through the existing typed
  catalog. The declarations cover Pennyshaver's memory and successful-run
  credit, Telework's install load and once-per-turn cashout, Regolith's rez
  load and access trash cost, and Nico's rez load, turn-begin income, and
  ordered empty-pool trash-then-draw cleanup.
- Root corrected Batch 2 composition to definition-code order and reviewed the
  production lifecycle tests: public hosted counters, exact pool/wallet
  transfers, click and once-per-turn accounting, real install/rez/run/turn
  occurrences, empty-pool cleanup, observation visibility, and JSON stability.
  The complete Cards suite passes 110/110 and `git diff --check` is clean.
- Smartware Distributor remains deliberately registered only on the audited
  blocked surface. Its mandatory turn-begin instruction is “take up to 1,”
  while Core's existing typed instruction is exact and correctly rejects an
  empty pool. E1b will add a distinct take-up-to primitive, prove that exact
  withdrawal is unchanged, and then promote Smartware into the executable
  catalog.

### 2026-09-04 — Checkpoint D1/D2 ENG-411/ENG-412 infrastructure accepted

- Luna Max added typed final-instruction click-run declarations for fixed
  central servers, formal-turn attempt constraints, and counter-scaled breach
  access. Installed sources are validated while the action is issued and
  consumed, then retained as detached run provenance; they may leave play
  without breaking real-turn cleanup. Access modifiers snapshot at initiation,
  round-trip in run state, project detached public reasons, and are rejected
  when duplicated, forged, attached to a basic/event run, or inconsistent with
  the source declaration.
- Root's review found and closed two lifecycle defects: the first draft kept
  requiring a run source to remain active after initiation, and extended R&D
  access repeatedly selected the same physical top card. Hidden HQ/R&D
  selection now excludes already-accessed identities while preserving the
  deterministic random stream and authoritative deck order.
- Luna Max also added immutable self cost-modifier declarations and checked,
  nonnegative cost quotes for Runner installs and icebreaker break costs under
  the formal `SuccessfulRunThisTurn` fact. Issued actions and pending install
  frames retain base/effective values and ordered source/reason adjustments;
  affordability, every hosted/wallet split, and payment use the effective
  value. Re-materialization rejects stale or forged quotes atomically, while
  unmodified DTOs omit the extension and retain their prior JSON shape.
- Root passed the combined focused gate 16/16, complete Core 642/642, a
  zero-warning/error solution build, formatter checks after three mechanical
  whitespace corrections, and `git diff --check`. The M2 proof then exposed a
  pre-existing D-401 integration defect triggered by Nico's E1a completion:
  its capability slice included the new generic definition in addition to the
  legacy asset contract. A separate compatibility checkpoint is open to make
  the M2 slice depend only on the five legacy families it actually consumes.

### 2026-09-04 — Checkpoint D-401 M2 capability slicing repaired

- Root traced the failed replay gate to Nico Campaign's completed generic
  behavior being included alongside its legacy M2 asset capability. Luna Max
  moved the policy to the Match-owned M2 document boundary: its immutable
  scenario catalog now contains only the referenced ICE, icebreaker, asset,
  agenda, and run-event contracts. Core's general catalog slicing remains
  unaware of M2 policy.
- Regression coverage proves that a generic overlay on referenced asset 30037
  does not change the M2 scenario fingerprint or initial state, while changing
  Nico's required asset trash-cost contract still fails the pin. Root passed
  the focused CLI/Match gate 24/24 and the direct proof returned the unchanged
  `faf4e631...` / `c882eb43...` / `d20cdef9...` hashes with all 32 checkpoints
  replay verified.

### 2026-09-04 — Checkpoint E1b/D3 hosted withdrawal and run-cost bindings accepted

- Luna Max added a distinct typed `TakeUpToHostedCredits` operation for
  Smartware Distributor. Exact `take` still rejects underflow; `take up to`
  transfers the actual minimum, treats an absent/empty pool as a successful
  event-free no-op, emits the actual nonzero delta, and preserves atomic wallet
  overflow and ordered empty-pool trigger behavior across JSON resumes.
- Conduit and Red Team now use the typed click-run substrate. Conduit snapshots
  its virus-scaled R&D access modifier at initiation and its optional successful
  R&D run-end trigger is filtered through stable server topology. Red Team
  exposes one history-constrained action for each central, pays out only for a
  run initiated by that exact physical source, and trashes itself when its
  hosted pool empties.
- Carmen and Marjanah are registered with their Sentry/Killer and
  Barrier/Fracter interfaces and typed successful-run cost adjustments. Real
  install and encounter tests prove printed versus reduced quotes, wallet/run
  credit splits, and immutable quote reasons. Root rejected the first delegated
  test pass because it lacked these production lifecycles, then corrected three
  fixture defects before acceptance.
- Root passed the combined focused Core gate 32/32, focused Cards gate 31/31,
  complete Core 651/651, complete Cards 123/123, a zero-warning/error solution
  build, formatter verification, and `git diff --check`. The direct M2 proof
  retained the pinned `faf4e631...` / `c882eb43...` / `d20cdef9...` hashes and
  replayed all 32 checkpoints.

### 2026-09-04 — Checkpoint UI-404 paid-ability presentation accepted

- Luna Max mapped detached typed card counters and visible current-turn ability
  uses into Match view models. `ABILITY READY` is derived only from an issued
  `UseAbility` action and takes precedence over `USED`; no cue is inferred from
  card text. The cue is threaded through every visible card projection,
  including server roots/ICE, rig and score areas, private viewer zones, and
  run encounter/access/source views.
- Text cards and inspection show non-color ability cues and typed `HOSTED
  CREDIT`, `VIRUS`, `POWER`, or `GENERIC` counter chips. Install actions explain
  cleanup only from frozen required/selected replacement IDs, while malformed
  counter purpose/restriction enums and stale/duplicate use facts fail closed.
- Root kept the RFG feed requirement open until Core supplied truthful typed
  provenance. `CardMovedEventPayload` now optionally carries both endpoint zone
  kinds only for movement into or out of RFG; ordinary and legacy movements
  retain their exact JSON shape. Match reports removal only from a typed RFG
  destination and never guesses from opaque IDs or card titles.
- The focused presentation gate passes 6/6 and the focused movement/provenance
  gate passes 23/23; complete Core passes 654/654, and Core, Match, Godot, and
  the full solution build without warnings or errors. Full Match is 48/49 with
  only the known SIM-401 checked-in fixture fingerprint pending regeneration.
  Formatter and diff checks pass, and the direct M2 proof retains all pinned
  hashes with 32 replayed checkpoints.

### 2026-09-04 — Checkpoint E2 ENG-407/409 trigger-card bindings accepted

- Luna Max bound Fermenter, Cookbook, and Pantograph through generic typed
  behavior declarations. Fermenter uses a structural source-counter amount
  operand for its click-and-trash payout; its virus counters survive only the
  exact paid self-movement child required to calculate that effect and are
  cleared at ability completion. Cookbook consumes the checkpoint-authenticated
  installed-card subject and filters it as a Runner virus Program without a
  title branch.
- Pantograph contributes its typed +1 MU modifier and responds to either side's
  agenda score/steal occurrence with the ordered credit gain followed by an
  optional Grip install. Behavior-effect installs now accept Runner Program,
  Hardware, or Resource cards from Stack or Grip through the ordinary install
  planner. The private choice freezes only currently legal candidates after
  the credit gain, then revalidates printed cost, conditional quote, memory,
  uniqueness, Console replacement, exact source zone, and stable origin when
  consumed. Reducer and invariant projections share the same fail-closed
  legality boundary.
- Root rejected declaration-only and opt-in/skipped test drafts. The accepted
  production fixtures exercise real install, turn-begin, score, steal, trigger
  accept/decline, cost payment, memory replacement, hidden choice, save/load,
  and forged-source paths. Review also repaired typed static-modifier JSON so
  its enum survives round-trip while legacy string modifiers remain stable,
  and closed a pre-existing structural-equality omission for typed run
  operands.
- Root passed the combined focused Core gate 49/49, focused Cards gate 25/25,
  complete Core 659/659, complete Cards 144/144, the zero-warning/error
  solution build, solution formatter verification, and `git diff --check`.
  The direct M2 proof remains byte-stable at `faf4e631...` / `c882eb43...` /
  `d20cdef9...`, with all 32 checkpoints replay verified.

### 2026-09-04 — Checkpoint E3 ENG-407 Carnivore access ability accepted

- Luna Max bound Carnivore's +1 MU and once-per-turn breach-access ability.
  Additional card-trash costs are now a typed operation with an explicitly
  owned zone target; engine-issued actions and serialized ability frames keep
  those cost selections separate from effect targets and bind each selection
  to its declaration cost index and stable key. Empty legacy payloads retain
  their prior JSON shape.
- The access window now materializes the exact accessed card and deterministic
  Grip-card pairs, keeps those private to the Runner, pays the complete frozen
  bundle atomically, and returns to the same priority-free access cursor. The
  accessed card enters Archives faceup with a zero-credit
  `AccessedCardTrashed` event. An agenda moved there by Carnivore is not then
  stolen, while an agenda originally accessed from Archives still is.
- Root review replaced string-key dispatch with a typed additional-cost enum,
  separated access provenance from remaining trashability, and required hard
  result/exploration bounds. Focused tests exposed two further defects before
  acceptance: invariant bijection treated legal pair variants as forged, and
  a stale destination-zone snapshot orphaned the first of two trashed Grip
  cards. Both paths now use structural pair comparison and refreshed immutable
  zone snapshots.
- Contract tests cover strict operation/target combinations, fingerprint
  sensitivity, defensive copies, JSON resume, and null/empty compatibility.
  Production card tests cover deterministic pair enumeration, observation
  privacy, insufficient and oversized Grips, ordinary and agenda access,
  once-per-turn use, exact semantic trash order, and stale/forged atomic
  rejection. The Carnivore gate passes 9/9, focused Core passes 69/69, complete
  Core passes 661/661, and complete Cards passes 153/153. The solution builds
  with zero warnings/errors; formatter and diff checks pass. The direct M2
  proof remains byte-stable at `faf4e631...` / `c882eb43...` /
  `d20cdef9...`, with all 32 checkpoints replay verified.

### 2026-09-04 — Checkpoint F1 AGT-401 contract agent v2 accepted

- Luna Max implemented the version-2 observation-only policy. Issued
  `UseAbility` actions now have first priority in action-phase, turn-priority,
  and run windows when the action is provably bounded by the declared click
  cost or by an unused once-per-turn snapshot. Unlimited non-click actions and
  exhausted once-per-turn actions are ignored, so the policy cannot create a
  zero-cost activation loop; affordability and the authoritative usage ledger
  remain Core responsibilities.
- Root amended the first draft for real decision coexistence: valid install,
  play, run, rez, and score actions outside this policy's current selection
  subset are treated as opaque and never selected, rather than invalidating an
  otherwise usable decision. Policy-owned candidates still fail closed on
  malformed timing/activation pairs, IDs, fingerprints, costs, effect targets,
  usage snapshots, or typed additional-cost selections. Exact additional-cost
  cardinality and global physical-card uniqueness are checked before an action
  is considered.
- Version strings are now `full-game-contract-agent-v2` and
  `full-game-contract-policy-v2`, with policy schema 2. Canonical semantic keys
  retain the complete immutable ability payload and normalize only the order
  of cost-indexed additional selections; descriptions and observation state
  dictionaries remain outside policy input. Tests cover click and `lose click`
  forms, turn/run windows, once-per-turn availability, unbounded fallback,
  opaque action coexistence, malformed rejection, deterministic ordering, JSON
  round-trip, and configurable priority validation. Luna and Root independently
  passed the complete AI suite at 30/30.

### 2026-09-04 — Checkpoint F2 catalog, CARD-401, and M2 compatibility accepted

- The full immutable Gateway composition now has its own
  `gateway-m4-4.3` version while the M2 document keeps the independent
  `gateway-m2-3` replay pin. M2 derives only the five typed legacy
  capabilities referenced by its scenario and deliberately excludes newer
  generic overlays. Root found and closed an overload-resolution defect that
  had made parameterless validation compare the full 4.3 catalog rather than
  the derived M2 slice.
- The Slice 4.3 registry row and all 14 review rows are complete. Positive
  lifecycle metadata covers every Batch 2 code, and the regenerated CARD-401
  reports record 77 total, 32 registered, 32 tested, 31 full-text complete,
  one explicit partial (Offworld Office), and 45 unregistered, with zero
  diagnostics. Nico Campaign is now recorded as fully bound with its rez load,
  turn-begin withdrawal, and ordered empty-pool trash/draw behavior.
- Luna Max passed 28/28 focused M2/proof tests and 63/63 focused Batch 2 and
  coverage tests. Root independently passed the 24-test M2 setup/proof filter;
  the representative proof retains initial/final/event hashes
  `faf4e631...` / `c882eb43...` / `d20cdef9...` and all 32 replay-verified
  checkpoints.

### 2026-09-04 — Checkpoint F3 SIM-401 Batch 2 fixtures and ability proof accepted

- Both developer-reduced simulation fixtures now pin `gateway-m4-4.3` and
  catalog fingerprint `6e55901b...`; their combined Corp and Runner code sets
  equal all 32 currently registered definitions. Their IDs and review notes
  explicitly identify the Slice 4.3 Batch 2 boundary and the remaining single
  partial registration.
- The bounded headless proof builds a valid installed Telework Contract turn
  from each fixture's seed and production catalog. A real install occurrence
  loads nine hosted credits, then `FullGameContractAgent` v2 selects the issued
  click ability while basic economy and run actions coexist. Core accepts the
  action, spends one click, transfers three hosted credits, records the
  once-per-turn use, and omits the ability from the next decision. The prompt
  and result both round-trip with stable authoritative hashes and viewer
  observations, under each fixture's action cap.
- Luna Max and Root each passed the focused fixture/proof gate at 8/8; Root
  additionally passed the complete Match suite at 51/51. This is intentionally
  narrow ability-path evidence: the pre-existing MATCH-401A normal
  setup/mulligan transition is still absent, so the fixtures remain validated
  inputs and this checkpoint does not claim the standard full-game SIM-401
  outcome.

### 2026-09-04 — Checkpoint F4 UI-404 client smoke accepted

- Luna Max added a dedicated `--m4-slice43-ui-smoke` path, separate from the
  existing gallery and M3 lanes. It mounts the production `TextCardControl` in
  the real scene tree, verifies the non-colour `ABILITY READY` cue and typed
  hosted-credit/virus/power chips, updates the same control to `USED` without
  losing its chips, renders an engine-shaped `UseAbility` action through the
  Match action presenter, and sends typed removed-from-game provenance through
  the event-feed presenter.
- `scripts/verify.sh` runs the lane with a 30-second bound, requires the exact
  stable PASS record, rejects any FAIL record, and preserves the pre-existing
  gallery, M3 smoke, and responsiveness lanes. Root independently built the
  Godot project with zero warnings/errors and observed
  `M4_SLICE43_UI PASS card_in_scene_tree=true ready_cue=true ready_chips=true
  used_cue=true used_chips=true ability_affordance=true rfg_removed=true` with
  exit code zero.

### 2026-09-04 — Checkpoint F5 integrated Slice 4.3 verification accepted

- Root ran `./scripts/verify.sh` from the reviewed checkpoint commits. The
  solution restored and built with zero warnings/errors, formatting had no
  drift, and all 949 tests passed: Core 661, Cards 153, AI 30, CLI 54, and
  Match 51. Authenticated card-data validation returned all 77 designs and the
  coverage drift gate returned 32 registered/tested, 31 complete, one partial,
  and 45 unregistered.
- All three deterministic M1 outcome paths passed. The representative M2
  proof retained exact hashes
  `faf4e631ab5890b9a26cff3318af6e389f35cb3c10be9cf12c09f1ce2cca03c6`,
  `c882eb43f2057073cf138511ac0d5834c20822a7c4a490d8e73cd6c402c682c5`,
  and `d20cdef94e97ac928ca088e14952b65d2116fecda00485fff5c3b05e1b6e9e20`,
  with 32 actions/checkpoints and replay verification. The official 77-card
  gallery, Slice 4.3 client smoke, existing M3 full-run smoke, and repository
  secret scan all passed.
- Slice 4.3's 13 new designs plus Nico Campaign completion, OBS-401 coverage,
  AGT-401 v2, UI-404, versioned fixtures, and the bounded ability-driven
  headless proof are accepted. The M4 plan's stronger standard SIM-401 claim
  is not made: MATCH-401A still lacks the normal setup/mulligan reducer, so a
  fixture cannot yet execute from a versioned match request to its declared
  full-game outcome. That prerequisite remains tracked rather than hidden by
  a prebuilt-state test.

### 2026-09-04 — PR #7 P2 review checkpoint accepted

- Root assessed all four automated inline findings as valid and split their
  implementation across two bounded Luna Max lanes. Cards shuffled from
  faceup Archives into R&D are now normalized facedown before observation;
  generic additional-cost projection resolves existing source zones without
  creating lazy play-area or removed-from-game zones or consuming allocator
  state.
- Installed-card facts and once-per-turn ability uses now belong to the active
  formal turn, independent of the installed card or ability controller.
  Ability condition reads require facts whose side and turn number match the
  active turn, preventing retained Runner facts from satisfying
  `InstalledThisTurn` or `SuccessfulRunThisTurn` during the following Corp
  turn. Opponent-priority paid abilities remain executable and their usage is
  recorded in that active turn's ledger; click actions remain restricted to
  the active side.
- Regressions cover Spin Doctor hidden-deck visibility, Pantograph installing
  a Runner card during the Corp turn, stale cross-turn condition facts,
  opponent-turn once-per-turn consumption, and allocation-free lookup of an
  absent lazy additional-cost zone. Root passed focused Core 24/24 and Cards
  13/13, then `./scripts/verify.sh`: zero-warning build, formatting and report
  drift clean, all 952 tests (Core 664, Cards 153, AI 30, CLI 54, Match 51),
  unchanged M1/M2 deterministic proofs, both M4 client smokes, M3 smoke, and
  the secret scan.

### 2026-09-04 — Checkpoint D-417 static catalog cleanup accepted

- Migrated `M2ScenarioDocument` and `M2ProofRunner` completely to the
  injected, immutable scenario behavior slice. The M2 replay contract now
  uses the explicit `EngineCompatibility.GatewayBehaviorRegistryVersion`
  literal (`gateway-m2-3`) rather than a process registry, preserving the
  representative proof's exact state and event hashes.
- Deleted the process-mutable `CardBehaviorSpecCatalog`,
  `GatewayBehaviorCatalog.RegisterAll`, and all five typed Gateway
  `RegisterAll` methods. Core rules-only compatibility overloads use the
  immutable `BehaviorCatalog.Empty`; behavior-bearing fixtures and tests
  compose and inject a consolidated catalog explicitly.
- Retained the useful Gateway printed-text and definition-binding assertions;
  former static idempotency/conflict checks now exercise immutable catalog
  construction and fail-closed lookup. The source/test scan reports zero
  `CardBehaviorSpecCatalog` or `RegisterAll` references.
- Evidence: the solution build completed with zero warnings/errors; all 952
  tests passed (Core 664, Cards 153, AI 30, CLI 54, Match 51); solution
  formatting and `git diff --check` passed; and the direct M2 proof replayed
  32 checkpoints with `replayVerified: true` and unchanged hashes
  (`faf4e631…` / `c882eb43…` / `d20cdef9…`).

## Slice 4.4 work log

### 2026-09-04 — Slice 4.4 architecture and execution checkpoints

- Started from clean branch `m4_4` at merged Slice 4.3 commit `f2bf590`.
  The accepted baseline is 952 tests (Core 664, Cards 153, AI 30, CLI 54,
  Match 51), coverage 32 registered / 31 complete / one partial / 45
  unregistered, and a green `scripts/verify.sh` gate.
- Scope is the complete Slice 4.4 contract from `docs/M4_PLAN.md`: ENG-414
  through ENG-418; Echelon (30025), Unity (30026), Leech (30008), Tread
  Lightly (30012), DZMZ Optimizer (30022), Docklands Pass (30013), and Verbal
  Plasticity (30034); AGT-401 v3; SIM-401 extension; UI-405; CARD-401 evidence;
  and the required combined Tread Lightly/Leech encounter proof. Later M4
  mechanics and cards remain excluded.
- Root owns architecture, checkpoint contracts, integration review, the
  progress ledger, independent verification, and local commits. Luna Max owns
  implementation inside each bounded checkpoint and does not commit.
- Checkpoint A (ENG-414) replaces the M2-only strength helpers with one typed,
  immutable value-calculation contract. A quote retains the printed/base
  value, effective value, and deterministically ordered terms with stable
  reason keys and physical/definition provenance. Static, per-installed-X,
  variable pump, counter-threshold, and encounter-scoped terms compose without
  title switches. Authoritative break legality, invariant reconstruction,
  observation, and Match presentation must consume the same calculation.
- Checkpoint B (ENG-415–418) adds four orthogonal primitives: run-scoped rez
  cost quotes; first-HQ-breach-this-turn access count; a replacement interrupt
  on the real basic-draw occurrence; and source-keyed first-program-install
  consumption in the active formal turn. Issued actions/quotes and any new
  state must round-trip, validate against the immutable catalog, and remain
  observation-safe.
- Checkpoint C binds and proves the seven Batch 3 cards in two groups: the
  strength cards (Echelon, Unity, Leech), then cost/access/replacement cards
  (Tread Lightly, DZMZ, Docklands Pass, Verbal Plasticity). Positive lifecycle,
  expiry/reset, forged/stale, ordering, and hidden-information cases are
  required; catalog fingerprints must cover every new declaration.
- Checkpoint D delivers AGT-401 v3, the versioned simulation fixtures and
  combined Tread Lightly/Leech timing proof, plus UI-405 and a dedicated client
  smoke. Checkpoint E updates the batch registry, rulings/review evidence and
  generated coverage to 39 registered / 38 complete / one partial, then runs
  the full repository gate. Each accepted checkpoint is recorded here and
  committed locally before the next checkpoint begins.

### 2026-09-04 — Checkpoint A ENG-414 value calculations accepted

- Luna Max implemented the immutable strength-calculation boundary and Root
  accepted it after three architecture corrections. `ValueQuote` retains one
  canonical base/effective/ordered-term vocabulary; terms carry typed source
  and operation, stable reason keys, realized quantity/unit values, and
  visibility-filtered definition/physical-source provenance. Negative
  effective strength remains legal, preserving the previous calculation
  semantics rather than borrowing the credit-cost floor.
- Static, per-installed-card, counter-threshold, fixed pump, activation-time
  dynamic pump, and external encounter-scoped adjustments now converge on the
  same projection. Unity-style pumps declare no fake constant: activation
  counts active installed Runner icebreakers (including the source), snapshots
  quantity/unit/total into an encounter-expiring adjustment targeted at the
  breaker, and repeated pumps compose without later board changes re-deriving
  X. Leech-style adjustments retain distinct source and exact encountered-ICE
  target IDs and survive source movement until encounter cleanup.
- Break action issuance/consumption and invariant reconstruction use the same
  authoritative quote. Core observations expose only visible raw terms, and
  Match carries those terms without parsing or independently recreating rules
  arithmetic. Immutable catalog equality/fingerprints include passive value
  and dynamic-pump declarations; legacy M2 strength APIs remain adapters over
  the shared quote and existing definitions keep their old fingerprint bytes
  when the new optional fields are absent.
- D-416 applies because run state gained the typed adjustment payload: engine
  state schema moved from 3 to 4 and checked-in scenarios were re-pinned. The
  representative M2 hashes changed from `faf4e631…` / `c882eb43…` to
  `fa7421b4…` / `b658d4e1…`; its event hash remains `d20cdef9…`, with 32
  checkpoints and replay verification. The evidence scenarios were likewise
  re-pinned without semantic event changes.
- Root independently passed the complete 962-test solution gate (Core 674,
  Cards 153, AI 30, CLI 54, Match 51), the direct M2 proof, touched-project
  formatter checks, and `git diff --check`. Luna independently passed the
  zero-warning solution build, the same complete suites, solution formatting,
  and the focused 10-test value-calculation gate. ENG-415–418 and all Gateway
  Batch 3 bindings remain outside this checkpoint.

### 2026-09-04 — Checkpoint B1 ENG-415 run-scoped ICE rez costs accepted

- Luna Max implemented a typed `IceRezCreditCost` quote path whose declarations
  live on `RunEventBehaviorSpec` and are snapshotted, with physical-card and
  definition provenance, when the event starts its run. The immutable snapshot
  remains authoritative while the event moves from grip to the play area and
  then heap, applies to every ICE rezzed during that run, and disappears with
  the run. Duplicate declarations are rejected at catalog construction.
- Both rez mechanisms consume the same projection: runtime-catalog `RezCard`
  actions and the legacy approached-ICE `RezIce` action carry ordered raw terms,
  check the submitted quote exactly, test affordability against the effective
  cost, pay that cost, and expose it in the emitted event. Invariant
  reconstruction, observations, and Match presentation use the same detached
  quote; Runner observations do not receive Corp-owned rez actions or their
  explanation terms. Forged quotes fail closed on both action paths.
- D-416 applies again because `RunState` gained a run-start snapshot: engine
  state schema moved from 4 to 5 and all checked-in state fixtures were
  re-pinned. The representative M2 proof now has initial/final hashes
  `2335d1cd…` / `dfe598b4…`; its event hash remains `d20cdef9…`, with 32
  checkpoints/actions and replay verification.
- Root independently passed the focused 6-test ENG-415 suite and the complete
  969-test solution gate (Core 680, Cards 153, AI 30, CLI 54, Match 52), the
  direct M2 proof, solution formatting, and `git diff --check`. Tread Lightly
  remains intentionally unbound until the Batch 3 card-binding checkpoint;
  ENG-416–418 remain outside this commit.

### 2026-09-04 — Checkpoint B2a ENG-418 first-program install consumption accepted

- Luna Max added a typed, optional installed-source declaration for reducing
  the first Runner program installed in each formal turn. The existing install
  `CostQuote` now composes self-cost terms with every active source declaration
  in deterministic order and retains raw reason plus definition/physical-card
  provenance. Multiple sources stack on the same first program and the credit
  floor remains zero.
- The formal-turn ledger records every completed Runner program install even
  when no modifier source is active and even during the Corp turn. This global
  occurrence prevents a later-installed source from re-arming the effect;
  source-keyed First-X uses are recorded only for terms in the accepted quote,
  after the install commits. Previews, rejected or stale actions, intermediate
  replacement choices, and non-program installs do not consume it. Direct,
  replacement-selection, and behavior-effect off-turn installs share the same
  projection and completion boundary.
- Turn-fact invariants require occurrence IDs to identify Runner-owned Program
  runtime cards. Pending install frames/actions are reconstructed against the
  same projection, forged quotes and stale sources fail atomically, turn-fact
  observations expose a visibility-filtered occurrence list/count, and the
  already-shared Match cost-quote surface carries the raw terms unchanged.
  Optional declarations participate in immutable catalog equality and
  fingerprints while absent/empty declarations retain legacy JSON bytes.
- D-416 applies because `TurnFactsState` gained the occurrence ledger: engine
  state schema moved from 5 to 6 and checked-in state fixtures were re-pinned.
  The representative M2 initial/final hashes are now `23777d0f…` /
  `ab07bacd…`; its event hash remains `d20cdef9…`, with 32 actions/checkpoints
  and replay verification.
- Root independently passed the focused 12-test ENG-418 suite, the complete
  981-test solution gate (Core 692, Cards 153, AI 30, CLI 54, Match 52), the
  direct M2 proof, solution formatting, and `git diff --check`. DZMZ Optimizer
  remains unbound until the card-binding checkpoint; ENG-416 and ENG-417 remain
  outside this commit.

### 2026-09-04 — Checkpoint B2b ENG-416 first-HQ-breach access accepted

- Luna Max generalized the existing run breach-access modifier with one
  canonical typed source kind. The legacy installed-run-source value is the
  enum default and remains omitted from serialized state; first-HQ snapshots
  carry an explicit discriminator. Both paths feed the same checked random
  access calculation rather than maintaining separate access arithmetic.
- A typed optional installed-source declaration now grants positive additional
  HQ access. At the authoritative breach boundary, every actual HQ breach is
  appended to the active formal-turn occurrence ledger, even when no source is
  active and regardless of whose turn it is. On only the first occurrence,
  active faceup Runner sources are snapshotted in deterministic order and their
  source-keyed First-X uses are consumed. Multiple sources compose, the result
  is capped by the available hidden HQ pool, non-HQ breaches do not consume,
  later-installed sources cannot re-arm the effect, and the snapshot survives
  source movement until breach/run cleanup.
- Source-kind-specific invariants preserve the old installed-run-source rules
  while validating first-HQ server identity, declaration/reason/delta,
  occurrence evidence, exact First-X consumption, duplicates, and reconstructed
  random-access limits. Duplicate reason keys are rejected for both first-HQ
  and first-program declarations. Observation filtering retains hidden-hand
  secrecy while exposing raw modifier terms, and Match maps those terms without
  formatting or re-derivation.
- D-416 applies because `TurnFactsState` gained the HQ occurrence ledger:
  engine state schema moved from 6 to 7 and checked-in state fixtures were
  re-pinned. The representative M2 initial/final hashes are now `cfec7f9b…` /
  `9fbef66d…`; its event hash remains `d20cdef9…`, with 32 actions/checkpoints,
  replay verification, and no trace problems.
- Root independently passed all 994 solution tests (Core 704, Cards 153, AI 30,
  CLI 54, Match 53), the direct M2 proof, solution formatting, and
  `git diff --check`. Docklands Pass remains unbound until the card-binding
  checkpoint; ENG-417 remains outside this commit.

### 2026-09-04 — Checkpoint B2c ENG-417 basic-draw replacement accepted

- Luna Max routed the real click-for-card action through a typed
  `BasicDrawEffect` and the existing imminent-effect checkpoint/replacement
  pipeline. A typed installed-source declaration supplies the replacement draw
  count and stable reason key; catalog equality and fingerprints include that
  declaration while absent/empty declarations preserve legacy JSON.
- Every accepted real basic draw records an ordered occurrence ID in the active
  formal-turn ledger, even when no replacement source is installed. Only the
  first occurrence can materialize a replacement, so a late source cannot
  re-arm it. The mandatory interrupt spends the click exactly once, consumes
  its source-keyed First-X use only when accepted, deterministically arbitrates
  multiple active sources without stacking them, and resets with the next
  formal turn. Corp mandatory draws and generic card-effect draws remain on
  their original paths and do not touch this ledger.
- Materialized catalog interrupts retain source, definition, captured zone,
  catalog/definition fingerprints, reason, count, controller, target, and
  occurrence provenance. Runtime and load-time invariant reconstruction reject
  forged declarations, fingerprints, inactive captured zones, controllers,
  targets, first flags, duplicate/missing ledgers, and pre-consumed First-X
  uses while still allowing the physical source to move after materialization.
  Runner replacement draws stop cleanly when fewer than the configured cards
  remain, avoiding an unresolvable mandatory interrupt; Corp exhaustion keeps
  the existing deck-out rule.
- D-416 applies because turn facts and pending effect frames gained persisted
  fields: engine state schema moved from 7 to 8 and checked-in state fixtures
  were re-pinned. The representative M2 initial/final hashes are now
  `f135f1fc…` / `42d38c1c…`; its event hash remains `d20cdef9…`, with 32
  actions/checkpoints, replay verification, and no trace problems.
- Root independently passed the focused 13-test ENG-417 suite, all 1,007
  solution tests (Core 717, Cards 153, AI 30, CLI 54, Match 53), the direct M2
  proof, solution formatting, and `git diff --check`. Verbal Plasticity remains
  unbound until the Batch 3 card-binding checkpoint. ENG-414 through ENG-418
  are now accepted as reusable primitives.

### 2026-09-04 — Checkpoint C1 Echelon, Unity, and Leech bindings accepted

- Luna Max bound the three ENG-414 representative cards without title/code
  branches in Core. Echelon is a printed-strength-zero Sentry interface whose
  passive quote adds one point per installed Runner icebreaker, including
  itself. Unity is a printed-strength-one Code Gate interface whose paid pump
  snapshots the installed-icebreaker count at activation, stores the realized
  quantity/unit/total, and composes deterministically across repeated uses.
- Leech uses three typed successful-run triggers for HQ, R&D, and Archives to
  add virus counters. Its encounter paid ability spends one virus counter and
  executes the new generic `ModifyEncounteredIceStrength` instruction, creating
  a distinct-source, exact-target, encounter-expiring negative strength term.
  The realized adjustment survives source movement, stacks across activations,
  affects authoritative break legality, and expires at encounter cleanup.
- Root's adversarial review removed an initially open legacy-provenance bypass.
  Every encounter-scoped strength adjustment must now reconstruct through its
  exact catalog definition and paid-ability key; stripped/forged behavior IDs,
  source/target mismatches, declaration deltas, reasons, controller context,
  and invalid resolution parents fail closed. The former hand-authored Core
  fixture now declares the same generic catalog behavior instead of relying on
  a magic compatibility identity.
- Immutable Gateway composition is temporarily versioned
  `gateway-m4-4.4-c1` and contains 35 definitions. Its fingerprint is
  `472d22a6b13ea35fa368677435c2d04171c1e23d1bc2353e36dfb6674a0549dc`;
  the existing Slice 4.3 simulation decks remain unchanged while their catalog
  pins distinguish those 32 deck definitions from the three newly bound C1
  definitions.
- D-416 applies because the new catalog instruction/effect identity may occur
  in saved resolution/run state: engine state schema moved from 8 to 9 and all
  checked-in state fixtures were re-pinned. The representative M2 initial/final
  hashes are now `4460bc23…` / `023bd7ae…`; its event hash remains
  `d20cdef9…`, with 32 actions/checkpoints, replay verification, and no trace
  problems.
- Root independently passed the 13-test C1 card gate and 10-test focused value
  gate, all 1,020 solution tests (Core 717, Cards 166, AI 30, CLI 54, Match 53),
  the direct M2 proof, a zero-warning solution build, solution formatting, and
  `git diff --check`. The other four Batch 3 card bindings, supporting agent,
  simulation, UI, and registry work remain outside this checkpoint.

### 2026-09-04 — Checkpoint C2 remaining Batch 3 bindings accepted

- Luna Max bound Tread Lightly, DZMZ Optimizer, Docklands Pass, and Verbal
  Plasticity through the four Checkpoint B primitives, with no new Core
  branches. Tread Lightly is a cost-one run event with no hosted credits and a
  run-start snapshot that raises every ICE rez cost by three. DZMZ supplies one
  static MU and a one-credit reduction for the first completed Runner program
  install of the formal turn. Docklands supplies one additional access on the
  first HQ breach of the turn, and Verbal replaces the first real basic draw
  with a two-card draw.
- Card-owned production fixtures compile exact test-local metadata through
  `GatewayRuntimeCardCatalog` and inject the final Gateway behavior catalog.
  They prove Tread payment, arbitrary server selection, quoted/paid +3 rez
  cost, physical source provenance, and run cleanup; DZMZ's extra capacity,
  serialized discounted install, accepted First-X consumption, and second
  full-cost install; Docklands' two hidden random HQ accesses followed by a
  normal same-turn breach; and Verbal's two-then-one real basic draws plus safe
  short-stack completion. The exhaustive late-source, reset, stacking,
  stale/forged, and isolation matrices remain owned by the accepted generic
  primitive suites.
- Gateway composition now has its final Slice 4.4 version
  `gateway-m4-4.4`, all 39 expected definitions, and fingerprint
  `1d7a527112152ea16164da4def624ca0dbb83e477d06323ecfc86c2f837a2fc4`.
  Existing Slice 4.3 reduced simulation decks remain unchanged until the
  supporting simulation checkpoint, while their catalog version/fingerprint
  pins now distinguish the 32 deck definitions from all seven Batch 3 catalog
  additions.
- No persisted contract changed, so engine state schema remains 9. The direct
  M2 proof remains byte-stable at initial `4460bc23…`, final `023bd7ae…`, and
  event `d20cdef9…`, with 32 actions/checkpoints, replay verification, and no
  trace problems.
- Root independently passed the 9-test C2 binding gate and all 1,029 solution
  tests (Core 717, Cards 175, AI 30, CLI 54, Match 53), the direct M2 proof, a
  zero-warning solution build, solution formatting, and `git diff --check`.
  All seven Slice 4.4 cards are now behavior-bound; AGT-401 v3, SIM-401,
  UI-405, and registry/evidence closure remain.

### 2026-09-04 — Checkpoint D1 AGT-401 contract agent v3 accepted

- Luna Max extended the observation-only agent with typed
  `InstallCardParameters` support and bumped the policy schema plus agent and
  policy identities to version 3. Existing bounded `UseAbility` precedence,
  the no-`EndTurn` rule, and deterministic typed selection remain intact.
- Turn-action selection now has an explicit bounded utility calculation. Within
  the install category it uses only the current engine-issued effective cost
  and savings from the detached `CostQuote`; it never reads descriptions,
  reason strings, the behavior catalog, authoritative state, or observation
  state dictionaries. Consequently DZMZ's first-program benefit contributes
  only while Core includes its adjustment in the current observation, and no
  bonus is cached after the First-X use is consumed.
- Install payloads fail closed on unstable IDs, invalid destinations,
  inconsistent replacement selections, non-install adjustment kinds, malformed
  provenance, or quote arithmetic that does not reconstruct the effective
  cost. Canonical semantic keys retain the complete typed install payload and
  use the engine-issued `ActionId` as the final tie-break.
- Tests prove discounted-install ranking, a fresh unmodified observation after
  First-X consumption, order/description/JSON neutrality, malformed and forged
  rejection, and the version-3 contract while retaining the unreadable
  observation-dictionary boundary. Root independently passed the complete AI
  suite at 34/34, both AI project formatting gates, and `git diff --check`.
  SIM-401, UI-405, and registry/evidence closure remain.

### 2026-09-04 — Checkpoint D2 SIM-401 Batch 3 fixtures and timing proof accepted

- Luna Max advanced both checked-in developer-reduced simulation fixtures to
  Slice 4.4 Batch 3 IDs, added all seven new Runner codes, and updated their
  bounded review notes. The fixture union now equals the complete 39-definition
  `gateway-m4-4.4` catalog while retaining its
  `1d7a527112152ea16164da4def624ca0dbb83e477d06323ecfc86c2f837a2fc4`
  fingerprint. The earlier Slice 4.3 ability proof follows the new fixture IDs
  and remains green; these inputs still do not claim the open standard
  setup/mulligan full-game outcome.
- A new compact headless production proof begins a real Tread Lightly run
  against R&D, pays its printed event cost, and retains the run-scoped +3 ICE
  rez declaration. At the approached Palisade, the Corp receives and pays the
  checked 3 → 6 quote while the unrezzed definition remains hidden from the
  Runner observation.
- The same run and physical ICE then enter the encounter window. The exact
  engine-issued Leech action spends its real virus counter and creates a
  catalog-provenanced, encounter-expiring -1 term targeted at that ICE,
  changing authoritative strength from 2 to 1 while Tread's independent run
  snapshot remains present. Leech is deliberately selected directly because
  AGT-401 v3 refuses unlimited non-click abilities; the proof does not replace
  or synthesize its issued payload.
- Save/load at the encounter boundary before Leech and again after its payment
  preserves run, ICE, rez modifier, strength adjustment, quotes, hashes, and
  invariants. Root independently passed the nine focused fixture/proof tests,
  the complete Match suite at 54/54, Match formatting, and `git diff --check`.
  UI-405 and registry/evidence closure remain.

### 2026-09-04 — Checkpoint D3 UI-405 modifier presentation accepted

- Luna Max added a Match-owned presentation layer over Core's raw typed
  calculations. Ordered strength terms now render readable base/delta/reason
  lines such as `STR 3 +2 (per installed icebreaker: 2)` for encountered ICE
  and installed breakers. Cost quotes render base → effective lines for
  install, rez, and break actions, preserving adjustment order and explicit
  labels for DZMZ, Tread Lightly, and the existing successful-run discounts.
  Unknown reason keys remain visible rather than being guessed or discarded.
- The observation mapper now detaches viewer-filtered current-turn
  `firstXUseKeys`, rejects malformed/duplicate entries, and attaches a
  non-colour `USED THIS TURN` cue only to the matching visible physical card.
  Last-turn facts are not treated as current. Existing paid-ability readiness
  remains an independent cue.
- Typed breach-access modifiers render non-authoritative notes, including
  `+1 first HQ breach this turn`, without exposing hidden candidate identities
  or requiring source-card provenance. Strength summaries and breach notes are
  displayed in the real focus region; card controls retain strength reasons and
  First-X cues; action controls retain modified-cost descriptions.
- Root found that the first draft formatted quotes but still filtered install
  and runtime rez actions out of the actual action groups. The accepted version
  exposes `InstallCard` in turn actions and `RezCard` in run timing, retains
  their typed target IDs, and proves both grouped-control paths.
- A dedicated `--m4-slice44-ui-smoke` is wired into `scripts/verify.sh` and
  reports `M4_SLICE44_UI PASS strength_reason=true modified_cost=true
  used_first_x_chip=true breach_note=true`. Root independently passed six
  focused UI-405 tests, the complete Match suite at 60/60, a zero-warning
  Godot build, Match/Godot/test formatting, and `git diff --check`.
  Registry/evidence closure and the final repository gate remain.

### 2026-09-04 — Checkpoint E Slice 4.4 registry and exit gate accepted

- Luna Max marked `slice-4.4` complete and completion-required, then closed the
  seven Batch 3 card records as registered, full-text complete, reviewed by the
  architect, and backed by their existing CR/ruling citations plus observation,
  hidden-information, serialization, and positive lifecycle evidence. No new
  ruling record or synthetic test marker was needed.
- Fresh CARD-401 reports contain 77 expected cards, 39 registered, all 39 with
  positive compiled test metadata, 38 full-text complete, the sole explicit
  Offworld Office partial, and 38 unregistered future cards, with zero
  diagnostics. The Slice 4.4 total therefore matches the plan's exit contract.
- Root ran `./scripts/verify.sh` successfully. The solution restored and built
  with zero warnings/errors; all 1,040 tests passed (Core 717, Cards 175, AI
  34, CLI 54, Match 60); solution formatting, CLI self-test/doctor, official
  77-card validation and downstream content-enabled builds, missing/opt-in data
  negatives, fresh coverage comparison, all three M1 outcomes and replay,
  Godot editor import, official gallery, Slice 4.3 and Slice 4.4 UI smokes,
  the full M3 client smoke, and the repository secret scan passed.
- The representative M2 proof remains stable at initial
  `4460bc239b386ec53259af1f5110f0a14db000d552a0e1efbe6950771f6d5215`,
  final `023bd7ae1f9eccb0329f4b6ad0e56c2801b9ab5a56b288469809b2666a494947`,
  and event
  `d20cdef94e97ac928ca088e14952b65d2116fecda00485fff5c3b05e1b6e9e20`,
  with 32 actions/checkpoints, replay verification, and no trace problems.
  Slice 4.4 is complete; the pre-existing standard full-game setup/mulligan
  SIM-401 outcome remains explicitly outside this slice's bounded claim.

### 2026-09-04 — PR feedback correction checkpoint accepted

- Both PR #8 P1 findings were valid consumer gaps. AGT-401 v3 could select an
  initial install with replacement candidates but did not consume the ensuing
  typed `InstallChoice`; likewise, Verbal Plasticity's mandatory first-draw
  replacement yielded an `InterruptChoice` that neither the agent nor the
  action presenter exposed.
- Luna Max added bounded, observation-only install continuation handling. The
  agent prefers an engine-issued legal final selection, otherwise advances one
  canonical engine-issued continuation, including the last remaining
  candidate, with typed shape validation and stable semantic ordering. It does
  not synthesize actions or inspect authoritative state.
- The v3 agent now handles typed replacement interrupts deterministically,
  preferring `UseReplacement` and falling back to `DeclineReplacement`; it
  remains deliberately fail-closed for prevention decisions outside this
  slice's policy claim. The client action presenter exposes all four typed Core
  interrupt actions under a neutral interrupt group and exposes install-choice
  actions with their target card retained.
- Tests cover final-versus-continuation preference, fresh observation and JSON
  ordering neutrality, the last-candidate continuation, malformed install and
  replacement payloads, mandatory Verbal replacement, and UI visibility for
  install, replacement, and prevention actions. Root independently passed the
  focused AI suite at 38/38 and Match suite at 62/62, then passed the full
  `./scripts/verify.sh` gate with all 1,046 tests (Core 717, Cards 175, AI 38,
  CLI 54, Match 62), zero build warnings/errors, formatting, coverage, content,
  simulation/replay, Godot, UI smoke, and secret-scan checks green. The M2
  hashes remain unchanged.

## Slice 4.5 work log

### 2026-09-04 — architecture and execution started

- Started Slice 4.5 on its dedicated `m4_5` branch from merged Slice 4.4
  commit `2737ae4`. The complete slice contract remains ENG-419 through
  ENG-425A, SCN-401, the seven Batch 4 cards, AGT-401 v4, SIM-401, UI-406,
  registry/coverage closure, and the full repository gate.
- CR 26.03 fixes the new reaction boundaries precisely. Manegarm Skunkworks
  and Anoetic Void meet their conditions when the Runner approaches the
  attacked server at 6.9.4g, after the final after-movement paid/rez window
  and before success at 6.9.5a. Funhouse meets its condition when encounter
  initialization begins at 6.9.3a, before the encounter paid window at
  6.9.3b. These reactions will use the existing serialized occurrence and
  checkpoint machinery; they are not paid-window actions.
- The engine will add an explicit serialized approach-server timing step and
  typed approach/encounter occurrences. Installed-card reaction declarations
  must match the attacked server through stable zone/server topology, while an
  encounter-self declaration must match the exact encountered ICE. The run
  timing frame remains below any reaction child so save/load resumes at the
  already-committed next boundary without re-emitting the occurrence.
- ENG-420 will extend declarative choices with structured alternatives whose
  acting player, typed cost, effect, and fallback meaning are explicit.
  Paying credits, spending clicks, taking a tag, and accepting an end-run
  effect remain distinct operations. Illegal costs are omitted when actions
  are materialized and revalidated atomically when submitted; hidden
  additional-cost card IDs are present only in the chooser's observation.
- Execution checkpoints are: (A) timing occurrences and source-server scope;
  (B) structured alternatives and compound trigger costs; (C) approach and
  encounter card bindings; (D) bioroid, topology, restriction, delayed
  cleanup, and Jailbreak primitives/bindings; (E) agent/simulation/UI; and
  (F) registry, coverage, full verification, push, and PR into `dev`.
  Luna Max agents own bounded implementation checkpoints. The primary agent
  owns architecture, adversarial review, integration, schema/pin decisions,
  verification, and acceptance commits.

### 2026-09-04 — Checkpoint A-C timing, alternatives, Jailbreak, and AGT v4 accepted

- Added the explicit CR 6.9.4g `ApproachServer` cursor plus typed
  `ServerApproached` and `IceEncountered` occurrences. The run cursor advances
  before a reaction checkpoint is queued, so save/load cannot emit either
  occurrence twice. Catalog triggers can require their active source to be on
  the attacked server, while encounter-self triggers bind the exact ICE.
- Structured mode options now carry typed credit, click, and exact additional
  card costs plus normal/fallback semantics. Materialization filters
  unaffordable options, enumerates bounded private card combinations, and
  revalidates and pays the selected payload atomically. `EndRun` is accepted
  only from an authenticated reaction child and unwinds the live run once.
  Review corrected optional-trigger preflight so Anoetic Void is not offered
  unless its non-fallback 2-credit plus 2-HQ-card alternative is payable.
- Bound Manegarm Skunkworks, Anoetic Void, and Funhouse to the new generic
  reactions. Manegarm gives the Runner its click/credit/end-run alternatives;
  Anoetic uses the Corp-owned optional reaction and keeps HQ selections
  chooser-private; Funhouse's encounter ability offers the Runner tag or
  end-run without yet claiming its ICE subroutine binding.
- Jailbreak is a real 1-click Runner event action with distinct HQ and R&D
  targets. Its source remains in the play area for the run, its successful-run
  draw enters the shared CR 9.2.8 trigger checkpoint, and its +1 breach access
  is snapshotted with typed run-event provenance. Runtime metadata, source,
  target, click, cost, catalog fingerprint, and stale action payloads are all
  revalidated.
- AGT-401 v4 handles typed trigger choices and structured behavior alternatives
  from observation-issued payloads only, including chooser-private card-cost
  IDs. Root review removed trigger actions from the ordinary run-window policy,
  retained their dedicated `TriggerChoice` dispatch, and fixed stable semantic
  ordering without reading descriptions or authoritative state.
- Persisted approach state first moved schema 9 to 10; targeted play payloads
  and run-event breach provenance then moved it to 11. The representative M2
  proof is re-pinned at initial
  `b964e5c037e7f470c9d64cbceba371c39aad806e0f46f3518be1a2818012b423`,
  final `7da1beb7aa61527ffa4e0cb9095397cc7809d481f34d1966826991ac5d411486`,
  and event
  `33af2b049fbad324ed5ff0ee130740c3d65c03f2cb1196100afbdc73e9f0e67c`.
  The event pin changes because `server-approached` is now explicit.
- Root verification passed all 1,081 solution tests (Core 734, Cards 189, AI
  42, CLI 54, Match 62), focused reaction/Jailbreak tests, a warning-free
  build of every touched project, solution formatting, and `git diff --check`.
  Encountered-bioroid abilities, Mayfly cleanup, ICE subroutine effects and
  topology, run restrictions, SCN-401, simulation/UI, and registry closure
  remain for later checkpoints.

### 2026-09-04 — Checkpoint D1 bioroid, Mayfly, SCN-401, and UI foundation accepted

- Added a dedicated encountered-bioroid action whose authenticated payload
  names the physical ICE, exact unbroken subroutine, shared ability key, and
  typed `LoseClick` cost. Ansel 1.0 and Brân 1.0 share one immutable card-layer
  declaration; repeated uses remain legal while the Runner has clicks and
  unbroken subroutines, without passing through an icebreaker or basic-action
  path.
- Mayfly now carries its printed AI interface and a typed run-end cleanup
  declaration. A successful Mayfly break arms one catalog-provenanced,
  run-expiring effect; pumping or merely installing it does not. Multiple
  breaks deduplicate, a moved/inactive source is left alone, and an
  authenticated active source is trashed through the ordinary typed card-move
  boundary at run end. Review required an explicit cleanup-kind discriminator
  so a forged generic lingering effect cannot move a physical card.
- Checked in the first SCN-401 executable fixture cases for optional Anoetic,
  mandatory Manegarm, and Funhouse encounter reactions. They validate the
  timing trace, round-trip every yielded boundary, and prove Corp-only HQ cost
  IDs remain absent from the Runner observation. The fixture remains explicit
  about the later bioroid, install, restriction, and Mayfly cases still to add.
- UI-406 now has a Match-owned typed projector for prominent approach and
  encounter decisions. It preserves engine option order and acting-player
  ownership, renders typed costs/fallbacks/card and server targets, exposes
  the bioroid action, fails closed on malformed payloads, and keeps Godot as a
  renderer rather than a rules interpreter. Restriction and Mayfly cues remain
  nullable until Core publishes their final typed observation fields.
- New serialized bioroid action and cleanup state moved the engine schema from
  11 to 12. Canonical scenarios were re-pinned; the representative M2 proof is
  now initial `1537ca1cef72e962329e2f1340e77a25b0c22e907c6f402972506aae1dccbf41`,
  final `e04e0e01c1abe93dc18a7a29d236b2a43f78ade13a60a9f9f4553d805f334581`,
  and event `33af2b049fbad324ed5ff0ee130740c3d65c03f2cb1196100afbdc73e9f0e67c`.
  The event hash is unchanged because the representative run does not use the
  new actions.
- Root independently passed all 1,104 solution tests (Core 742, Cards 197, AI
  42, CLI 54, Match 69), the focused bioroid/Mayfly, reaction/SCN-401, UI, and
  M2 proof suites, solution formatting, and `git diff --check`. The remaining
  hard checkpoint is serialized choice-bearing ICE subroutine resolution,
  hidden-safe Corp installs/topology, Ansel's run restriction and trash target,
  followed by final SCN/simulation/UI/registry closure.

### 2026-09-04 — Checkpoint D2 typed ICE resolution and topology accepted

- Added one serialized, catalog-authenticated ICE-subroutine continuation
  instead of title branches. It executes Corp installed-card trash choices,
  Ansel's normal paid HQ/Archives install, Brân's inward cost-ignoring ICE
  install, Funhouse's pay-or-tag subroutine, and typed run restrictions. The
  payload constructors reject unsupported install-cost combinations and
  extraneous card, server, payment, and hosted-credit fields before they can
  reach a decision boundary.
- Mid-run topology follows CR 8.5.10 and 8.5.13: a removed encountered ICE
  leaves a repaired cursor, while an ICE installed immediately inward from
  Brân becomes the next approached ICE. Normal installs use the existing
  replacement/unique/install-cost machinery; inward installs have their own
  exact destination and ignore-cost contract.
- Ansel's cannot-steal-or-trash effect is run-scoped and filters only steal,
  basic trash, and declarations whose typed instruction/cost graph can trash
  a Corp card. Unrelated access-window abilities remain legal. Each
  restriction now points to an exact schema-14 resolved-unbroken-subroutine
  ledger entry, and adversarial removal of that entry fails invariants and is
  rejected atomically.
- Root review strengthened the shared bioroid payload with its typed printed
  ordinal, kept repeated click-loss uses legal, and made the contract agent's
  ordinal preference explicit. The reviewed checkpoint is committed as
  `9646928` after complete project suites passed Core 756/756, Cards 214/214,
  AI 51/51, CLI 54/54, and Match 77/77.

### 2026-09-04 — Checkpoint E-F simulation, UI, registry, and exit accepted

- SCN-401 now has 13 executable cases covering every required approach,
  encounter, bioroid, install, restriction, and Mayfly used/not-used boundary.
  Every declared yield round-trips and the timing validator proves the new
  reactions do not invent priority windows.
- The Slice 4.5 headless proof drives Manegarm, Brân, Ansel, and Funhouse in
  one full run. Scripted-human and AGT-401 v4 policies select the exact same
  action/event/state sequence; every yield passes invariants, JSON round-trip,
  and replay lockstep. Simulation fixtures now contain all 46 registered
  definitions and pin behavior catalog
  `40cf3ff37c5e014a70d43e321c63cf541dc883af74315e970df0e4e4928dd038`.
- Match receives the immutable runtime-card catalog at composition and uses it
  for setup and every reducer call, so real Ansel/Brân install choices can be
  materialized. Its Runner publication boundary omits events containing
  hidden Corp physical IDs and fails closed for future typed card-reference
  payloads while retaining public reveal, access, score, and rezzed-ICE data.
- UI-406 renders prominent ordered choices with acting player, typed costs,
  fallbacks, safe targets, active restriction, legal bioroid use, and armed
  Mayfly cleanup. The Godot smoke proves all nine cues without reading rules
  text or exposing a hidden installed card. The legacy M2 UI smoke remains on
  its explicit behavior-only adapter; normal Godot composition supplies the
  runtime catalog.
- Gateway evidence closes at 77 total / 46 registered / 46 tested / 45
  text-complete / 1 partial / 31 unregistered; Offworld Office remains the
  only registered partial. Engine schema 14 re-pins the representative M2
  proof at initial
  `308cadf42d6f32153299104661b81d1a47ce7ae80a91e966375729149e71d049`,
  final `4603c78adb067873229363c86d6b1294f92c5564009e1b8a1cbf2aa6e6dc1001`,
  and unchanged event stream
  `33af2b049fbad324ed5ff0ee130740c3d65c03f2cb1196100afbdc73e9f0e67c`.
- The final `./scripts/verify.sh` gate passes all 1,154 tests, warning-free
  builds and formatting, official card-data/coverage checks, M1/M2 and Slice
  4.5 deterministic proofs, Godot import/gallery/Slice 4.3/Slice 4.4/Slice 4.5
  and M3 smokes, and secret scanning.

### 2026-09-05 — PR review action-order boundary accepted

- PR #9 review correctly identified that ordinal sorting of unpadded issued
  IDs places `action:10` before `action:9`. Both contract agents now share one
  observation-safe ordering helper: canonical positive `action:<ulong>` IDs
  compare by invariant numeric sequence, while synthetic stable IDs retain an
  ordinal fallback.
- Regressions cover both agents across the 9/10 boundary, reversed observation
  order, and JSON round-trip. The complete AI suite passes 53/53 and the full
  1,154-test repository gate, all deterministic proofs, Godot smokes,
  formatting, coverage/content checks, and secret scan remain green.


## Slice 4.6 work log

### 2026-09-05 — Architecture and implementation checkpoint

- Started from clean `4_6` at `7b853df`, the merged correctness prerequisite.
  Root owns architecture, persistence, integration, and review; bounded ICE and
  generic damage tasks use Astra Low, and client presentation uses Luna Max.
- ENG-426–432: damage goes through existing effect checkpoints; ICE stores the
  exact damage victim so Diviner never infers it from later grip changes.
  Karunā resumes only after damage and carries a typed printed jack-out event.
  Flatline clears the live run as well as the resolution stack.
- Clearinghouse computes its advancement amount before paying its self-trash
  cost; the damage child persists the amount while live counters clear normally.
  Urtica uses an explicit installed/unrezzed access trigger declaration.
- Advanceability is explicit catalog data. Ping tests its actual run/server at
  rez; Pharos uses the shared counter-threshold strength projection. Public Trail
  uses the previous Runner turn, and Retribution filters program/hardware targets.
- CR 9.12.5 persistence retains only abilities of a rezzed source trashed while
  accessed. The original server/zone, trigger declaration, and this-run stolen
  agendas survive through the run-end checkpoint; unrelated moved abilities do
  not become active. Both agenda-before-trash and trash-before-agenda are tested.
- UI-407 receives Core-calculated damage risk, source, conditional values, and
  Diviner parity result. Match and Godot do not recompute these rules.
- Rules baseline remains local CR 26.03 and card-data commit
  `236140cf84491c23995b028bc791281837520b97`. Cross-checks use the
  [jnet ICE](https://github.com/mtgred/netrunner/blob/master/src/clj/game/cards/ice.clj),
  [assets](https://github.com/mtgred/netrunner/blob/master/src/clj/game/cards/assets.clj),
  [operations](https://github.com/mtgred/netrunner/blob/master/src/clj/game/cards/operations.clj),
  and persistent-ability semantics in the pinned CR 9.12.5.
- Focused delegated and integration tests pass so far. Full schema migration,
  SCN-402, generated coverage, client evidence, and `make verify` remain pending;
  this checkpoint is not the slice exit gate.

### 2026-09-05 — Integration and schema evidence

- Checkpoint `fb0520a` records the reviewed engine and card implementation.
- Gateway composition is `gateway-m4-4.6`; generated CARD-401 coverage reaches
  58 registered, 58 tested, 57 complete, one partial (Offworld Office), and
  19 unregistered. Official presets remain gated until their remaining cards
  are completed in Slice 4.7.
- State schema is 16. Existing artifact/replay envelope schema remains 1.
  New saved state contains ICE damage continuation/victim provenance and
  run-local persistent-ability context. Earlier state versions are rejected.
- Canonical schema-16 hashes:
  - base: `785395aea588f53b1df634cc237762b925dc12b907573c3f90d9c6d545936191`
  - nested-inner: `dc4c762aeb621f30dae5e36d56117f467c935c14b8a823c09a61b97f09ac6110`
  - nested-outer: `3e93062fdf8a44b09f6d3e8bcef22a86cfb3a51c28e9f996d08875b6003403ff`
  - M2 initial: `9155084805027b7790c15c977f41173bfbf2ae94537845db8686d2b7c53a54ff`
  - M2 final: `0f2d525c3bd95a42e3c5ed5b3ad4aef54b39768950db3484f2c641495fb23ae3`
- Reversing only the schema number reproduces the three prior baseline fixture
  hashes. M2 retains its 32 actions/checkpoints, equal policy outcomes, verified
  replay, and event hash
  `33af2b049fbad324ed5ff0ee130740c3d65c03f2cb1196100afbdc73e9f0e67c`.
- SCN-402 is `data/m4/scenarios/scn-402-damage.json`: Karunā, Urtica,
  Diviner, Tithe, Pharos, Ballista, and Ping execute under scripted and contract
  policies with serialization at every yield and replay of each action/event.
  Removing Karunā's typed jack-out evidence fails trace validation.
- Review fixed already-resolved damage being displayed as pending during an
  optional follow-up reaction. Damage presentation now consults the active
  checkpoint stage and its remaining effect, including prevention changes.
- Final global verification and native client evidence are recorded below.


### 2026-09-05 — Slice 4.6 exit gate

- `make verify` passed: 1,264 tests (Core 794, Cards 258, AI 57, CLI 59,
  Match 96), solution formatting, deterministic M1/M2 proofs, SCN-402 under
  both policies, generated coverage equality, official 77-card gallery, Godot
  import, Slice 4.3–4.6 client smokes, responsiveness, both full-match seats,
  M3 smoke, and secret scanning.
- Native UI-407 smoke passed all seven checks; root visually reviewed the
  [client screenshot](evidence/slice46/slice46-ui-smoke.png). Capture is explicit
  and runs with a native renderer; headless CI does not rewrite evidence.
- Final behavior fingerprint:
  `4cb1ffce96ebf89ecdb8769115a844e6cc7d85b48da1e321ed84ea66d5d45e1d`.
  Public Trail's tag branch carries explicit fallback presentation metadata;
  simulation pins match this final catalog.
- Independent review added post-damage presentation and two-source AMAZE
  destruction/serialization regressions. Match retains public damage after
  flatline while redacting a hidden source unless this batch already revealed
  it through access. Corp trash choices score only public installed definitions
  against injected printed metadata, with deterministic semantic ties.
- All twelve Batch 5 cards are bound, tested, reviewed, and registered. The
  required 58 registered / 57 complete exit target is met. No Slice 4.7 card
  work or official-preset coverage override is included.


### 2026-09-05 — PR handoff

- Completion commit `94c54fb` follows checkpoint `fb0520a` on dedicated branch
  `4_6`. The branch is pushed and [PR #11](https://github.com/Master-Yoba/netrunner/pull/11)
  targets `dev`, with architecture/compatibility notes, verification evidence,
  and the native screenshot. Remote synthetic and official CI checks are the
  remaining merge gate; their live results are attached to the PR.

### 2026-09-05 — PR #11 and late PR #10 review fixes

- Validated all three review findings against the current branch. Damage
  flatlines preserve formal current/last-turn history before clearing the run;
  an engine-driven Urtica regression verifies audit facts and terminal save/load.
- Restored issued run, advance, purge, rez, score, and paid ability actions to
  ordinary human decision surfaces, including paid abilities during runs.
  Full-match coordinator regressions exercise and submit actual Runner run and
  Corp rez action IDs through the presenter.
- Full-match/simulation output honors `--json` for successful results and
  failures, including invalid arguments before the flag. Human output and
  existing exit codes are preserved.
- Verification passed: all 1,272 tests, formatting, deterministic proofs,
  generated coverage, official gallery, every Godot smoke, and secret scanning
  via `make verify`. Coverage remains 58 registered/tested and 57 complete.


### 2026-09-05 — Slice 4.7 implementation and architecture review

- Dedicated feature branch `m4_7`; checkpoint `ade5111` records identities,
  agendas, access context, ICE swaps, and Trojan hosting. Astra Low implemented
  the two engine areas; Luna Max owns presentation and contract-agent updates;
  root reviewed contracts and implemented cross-domain run timing.
- Generic registrations complete the nine identities, seven new agendas,
  Malapert and both Trojans; Offworld's seven-credit score trigger is separate
  from its unchanged M2 agenda-point capability adapter.
- Physical hosted programs retain a stable host card ID while occupying the
  Runner rig. Installs from both basic actions and card effects require an
  engine-issued ICE host choice. Host uninstall trashes children, clears their
  counters and memory, while swaps preserve these relationships. Botulus
  actions target an exact subroutine; neither ICE strength nor subtype restricts
  its virus-counter payment. Tranquilizer derez preserves installation and
  allows another rez.
- Run access occurrences are counted at the actual access boundary, checked
  against the live access ledger, and copied into serialized run-end context. Zahya uses that
  context after live run cleanup. ICE score/steal swaps exchange positions
  atomically without reinstalling or following stale list indexes; hosted
  programs and counters survive. Tagging ICE resolves through the checkpoint
  pipeline so NBN can react before subsequent subroutines.
- Rules correction to ENG-437: the prose plan's reveal-before-shuffle sequence
  conflicts with [CR 26.03 8.7.2–8.7.4](https://rules.nullsignal.games/#8.7.3).
  A found card stays set aside while the remaining deck is shuffled; only then
  do reveal/add effects continue. The inspected jnet Malapert declaration also
  uses reveal/shuffle/move order; the explicit CR rule takes precedence over
  that cross-check and the original plan. Review also found the shared Corp trash
  transfer exposing private HQ cards; Longevity requires preserving their
  hidden Archives status. These are correctness fixes, not deferred exceptions.
- Schema 16 → 17 deliberately accounts for host IDs, exact access occurrence
  context and new serializable trigger/choice contracts. The three baseline
  scenario hashes change solely from the schema version:
  - base: `a2e7c0f3130df9af00eba75c72c728875186f1205f6a019664a9497e109ef696`
  - nested-inner: `3aac6ebaa02eebedeb5deccd7da62c8e1bfb76927d20d7f6262cf2a1f8e91a60`
  - nested-outer: `9e6dc5a13db8a91c26b2ea553f1728aabf2fbf47fcb7925beb40e272725f6a28`
- Rule comparisons used the authenticated local Gateway snapshot,
  [CR 26.03](https://rules.nullsignal.games/), and jnet's
  [identity](https://github.com/mtgred/netrunner/blob/master/src/clj/game/cards/identities.clj),
  [agenda](https://github.com/mtgred/netrunner/blob/master/src/clj/game/cards/agendas.clj),
  and [program](https://github.com/mtgred/netrunner/blob/master/src/clj/game/cards/programs.clj)
  declarations. No external implementation code was copied.

- M2 proof retains its 32 actions/checkpoints, equal scripted/contract policies,
  verified replay, and unchanged event hash
  `33af2b049fbad324ed5ff0ee130740c3d65c03f2cb1196100afbdc73e9f0e67c`.
  Schema-17 state pins are initial
  `9742dca2b8778a05375b0563ef3539a8d2c5248a21d989b13f9d33214de94341`
  and final
  `3242c530703869a08b82457d0399cc1a2e214d8ad07922a698cb06f53e6de969`.
  Offworld's added generic score trigger does not change the M2 capability
  fingerprint or its semantic trace.

### 2026-09-05 — Slice 4.7 final review

- Coverage is 77 registered, tested and complete out of 77, with zero partial
  cards or diagnostics. Official basic and expanded presets are enabled;
  missing authenticated metadata remains an explicit unavailable client state.
- AGT-402 advances the agent and policy to v6 (policy schema stays 5). Review
  added hosted-install continuation handling and exact Botulus subroutine
  semantics, with deterministic selection across reordered issued actions.
- [SCN-403](../data/m4/scenarios/scn-403-official-presets.json) drives actual
  official preset setup, keep/mulligan decisions, complete games, save/load at
  every yield and replay verification. Seed 47042 ends basic after 922 actions
  with Runner deck-out victory, and expanded after 111 actions with Corp
  flatline victory. These require the authenticated content lane; the synthetic
  lane checks catalog completeness and reports the missing proof prerequisite.
- UI-408 consumes exact Core choice keys, access counts and public ICE topology.
  It preserves hidden definitions while nesting hosted Trojans beneath their
  physical ICE, shows both swap positions and derez events, and keeps run-end
  choices visible without confusing a completed run with a completed match.
- Terminal review found active run events and fatal operations could remain in
  transient play areas after deck-out/flatline. Shared terminal cleanup now
  moves those cards to discard and clears live run state without run-end rewards;
  actual Overclock/NBN, Overclock/Diviner and Neurospike regressions cover it.
- Final `make verify` passed: 1,329 tests, formatting, deterministic proofs,
  authenticated content/gallery, generated 77/77 coverage, all Godot smokes,
  responsiveness and secret scanning. Both client seats prove setup, mulligan,
  full turns, identities and terminal presentation. The eight-check UI-408
  smoke also passed natively; its [screenshot](evidence/slice47/slice47-ui-smoke.png)
  is a detached presentation fixture, separate from the official game proofs.
