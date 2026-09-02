# M4 progress — System Gateway complete

Status: slice 4.1 merged; slice 4.2 implementation in progress on `m4_2`

Date started: 31 August 2026

Rules/data baseline: Null Signal Comprehensive Rules 26.03 / System Gateway
vendored commit `236140cf84491c23995b028bc791281837520b97` (77 designs)

## Scope

Slice 4.1 of `docs/M4_PLAN.md` is complete and merged. Current implementation
scope is Slice 4.2: ENG-401 through ENG-406, MATCH-401A/B, DATA-401A, the 12
Batch 1 designs, AGT-401, SIM-401, UI-403, OBS-401, and their documented exit
evidence. Later M4 card batches remain out of scope.

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
metadata catalogs; compatibility statics remain only behind documented M2
adapters. Engine extraction is mechanical, schema/version migration is pinned,
and Godot consumes Match presentation boundaries rather than Cards records.

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
