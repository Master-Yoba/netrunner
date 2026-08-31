# M4 progress — System Gateway complete

Status: slice 4.1 complete; PR #5 open against `dev`

Date started: 31 August 2026

Rules/data baseline: Null Signal Comprehensive Rules 26.03 / System Gateway
vendored commit `236140cf84491c23995b028bc791281837520b97` (77 designs)

## Scope

Current implementation scope is limited to Slice 4.1 of `docs/M4_PLAN.md`:
architecture/version preparation, behavior-neutral engine organization, the
Gateway primitive audit, CARD-401/COV-401, the explicit gitignored-metadata
content build, catalog-driven presentation, and the first Theme/region split.
No Slice 4.2 rules or card behavior is in scope yet.

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

## Slice 4.1 exit checklist

- [x] ARCH-401 / ENG-400 / VER-401 implemented and reviewed.
- [x] PRIME-AUDIT complete in `docs/M4_PRIMITIVES.md`.
- [x] CARD-401 / COV-401 generated report distinguishes registered and complete.
- [x] DATA-400 content-enabled build is deterministic and offline after build.
- [x] UI-401 renders all 77 provisioned cards through an injected resolver.
- [x] UI-402 Theme/region extraction preserves M3 smoke and latency behavior.
- [x] Full `scripts/verify.sh` green with final test/smoke evidence recorded.
