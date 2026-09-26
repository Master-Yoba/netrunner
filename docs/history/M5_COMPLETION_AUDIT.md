# M5 completion audit

Audited 23 September 2026 against [M5_PLAN.md](M5_PLAN.md), including its
architecture, slice exits and milestone gate. Implementation is complete;
the user supplied general acceptance and authorized GitHub publication on
25 September 2026. The scope of that feedback is recorded below. Automated
evidence from 23 September remains distinct from human feedback and remote CI.

## Deliverable coverage

| Requirement | Implementation and proof | Status |
|---|---|---|
| AI-501: typed, filtered input and public descriptors (§3.1–3.3) | [Agent foundation](M5_AGENT_FOUNDATION.md); [foundation tests](../../tests/Netrunner.AI.Tests/OfflineAgentFoundationTests.cs) cover native/JSON agreement, prompt/order independence, every decision family, hidden hands/decks/ICE/Archives/search/access and bounded knowledge. [Catalog tests](../../tests/Netrunner.Cards.Tests/GatewayEvaluationCatalogTests.cs) cover all 77 detached descriptors. AI references Core only. | Implemented and verified |
| AI-502: Corp policy (§4.1–4.2) | [Corp policy](../../src/Netrunner.AI/CorpHeuristicPolicy.cs), [regressions](../../tests/Netrunner.AI.Tests/CorpHeuristicPolicyTests.cs), 40 independent tactical roots and frozen full games. Standard: 39/40 tactics, all 11 critical cases. | Implemented and verified |
| AI-503: Runner policy and encounters (§4.3–4.4) | [Runner policy](../../src/Netrunner.AI/RunnerHeuristicPolicy.cs), [bounded planner](../../src/Netrunner.AI/RunnerEncounterPlanner.cs) and [planner regressions](../../tests/Netrunner.AI.Tests/RunnerEncounterPlannerTests.cs) cover matching breakers, follow-up funding, bioroid clicks, hosted counters, expiring credits and disposable breakers. Standard: 38/40 tactics, all 17 critical cases. | Implemented and verified |
| AI-504: deterministic profiles and diagnostics (§3.4, §4.5) | [OfflineAgent](../../src/Netrunner.AI/OfflineAgent.cs) evaluates tentative checkpoint/RNG state and commits accepted actions only. Foundation tests cover retries, discard, cancellation and restore. Easy/Standard configurations are frozen. [Private decision logs](../../src/Netrunner.Match/PolicyDecisionLog.cs) add versioned context, all candidate utilities/reasons, chosen ID, observation/checkpoint hashes and work/time; default off. [Log regressions](../../tests/Netrunner.Match.Tests/PolicyDecisionLogTests.cs) check replay equivalence, failed/canceled attempts and report separation. | Implemented; diagnostics gap closed during audit |
| AI-505: reproducible benchmarks (§5) | [Benchmark guide](../M5_BENCHMARKS.md), [runner tests](../../tests/Netrunner.Match.Tests/AiBenchmarkTests.cs), [failure tests](../../tests/Netrunner.Match.Tests/BenchmarkFailureTests.cs) and [gate checker](../../scripts/m5_gate.py). Independent workers, fixed seeds/configurations, progress detection without sequence churn, explicit failures, verified replay prefixes and private recovery artifacts. [Gate evidence](../M5_GATE.md): Standard 1,000/1,000, Easy 100/100, paired comparisons 40/40 each, all critical tactics, exact worker-count repeats. | Implemented and verified |
| SAVE-501: durable session saves (§6) | [Save contract](../M5_SAVES.md), [save tests](../../tests/Netrunner.Match.Tests/SoloSaveTests.cs), [nested matrix](../../tests/Netrunner.Match.Tests/SessionPolicyMatrixTests.cs) and [process-kill tests](../../tests/Netrunner.Match.Tests/AtomicFileProcessTests.cs). Both seats/presets, repeated reloads, setup, run/trace/payment/install/trigger boundaries, thinking/playback, terminal state, compatibility rejection, corrupt entries, backup, write failure and crash recovery. | Implemented and verified |
| MATCH-501: offline client and lifecycle (§6.2, §6.4) | [Client guide](M5_CLIENT.md), [service](../../src/Netrunner.Match/SoloSessionService.cs) and [service tests](../../tests/Netrunner.Match.Tests/SoloSessionServiceTests.cs). One-action coordinator pump, off-thread policy/storage, serialized autosaves and generation guards; setup, Standard/Easy, seed, save browser, paused resume and outcome. [Native matrix](../evidence/m5-6/client-matrix.json): five cases, ten process phases, including failures, stale work, focus/Escape and restart. | Implemented and automatically verified; general user acceptance recorded |
| DEV-501: reproduce and step (§7) | [Developer guide](../M5_DEVELOPER.md), [tests](../../tests/Netrunner.Match.Tests/DeveloperSessionTests.cs) and [native evidence](../evidence/m5-7/README.md). All 80 tactical roots reopen/step/replay; bounded advance, reveal without observation changes, validated snapshots and new replay roots for raw imports. Explicit development build plus launch flag; actual release excludes controls even when flagged. | Implemented and verified |

## Cross-cutting exit checks

- The [final gate record](../M5_GATE.md) retains full denominators, category and
  held-out scores, comparison metrics, exact manifests/report hashes and every
  failed attempt. Three noncritical Standard misses remain disclosed. Held-out
  cases that informed earlier correctness fixes are described as regression
  evidence, not unseen evaluation. No new strategic tuning is claimed.
- The real Retribution/memory-loss failure is fixed and replayed. The failed
  999/1,000 preflight remains separate from the passing 1,000-game run. Rules,
  replay, coverage, existing board interactions and formatting remain under the
  provisioned M4 gate; official metadata/artwork stay out of source control.
- The [offline release proof](../evidence/m5-8/offline-summary.json) runs outside
  the checkout with networking and existing profiles denied. Four seat/preset/
  artwork cases complete eight save/restart phases, with moved saves and exact
  engine/policy hashes. The cache is packed, restored and validated offline;
  both missing-art and restored-art rendering pass. This proves the recorded
  macOS runtime, not offline toolchain provisioning or another OS package.
- [Direct policy timings](../evidence/m5-8/performance-summary.json) cover all
  13,828 choices in 20 serial games with diagnostics disabled. Each seat and
  category meets its 50/200 ms p95 budget, and every replay matches the normal
  benchmark. [Renderer evidence](../evidence/m5-8/validation.json) records actual
  1080p/1440p runs, dense boards, history, motion, 3.05 ms idle frame p95 and
  410 bytes allocated per idle frame.
- CI definitions include quick tactical/game repeats, save/developer native
  checks and scheduled full gates. Uploads contain allowlisted summaries only.
  These workflow definitions were checked locally; no remote execution is
  represented as evidence.

The audit found one missing implementation detail: full per-decision diagnostics
could previously be inspected only in memory or failure-specific tactical
artifacts. `ai-benchmark --policy-diagnostics` now explicitly writes private
per-game JSONL, before applying each selected action. It is off in normal play,
ordinary benchmarks and CI; logging I/O is excluded from policy timing.
Cancellation is rechecked after the asynchronous write. The log's
`selected-before-apply` stage intentionally does not claim that an action was
accepted. It contains no authoritative snapshot, but still counts as private
strategic evidence and is never uploaded automatically.

Post-audit verification passes all 2,050 official solution tests, the complete
provisioned M4 gate and all twelve cases of the full M5 gate. Each repeated
case's deterministic digest equals the earlier passing result. The final
logged/unlogged official smoke verifies 13,828 records across twenty games
without changing any replay. [Audit validation](../evidence/m5-8/audit/validation.json)
records exact build scope and log hashes; [diagnostic evidence](../evidence/m5-8/audit/diagnostics-summary.json)
contains only allowlisted counts/hashes, with private JSONL retained locally.

## Human acceptance

The historical M4 acceptance distinction remains in
[M5_PROGRESS.md](M5_PROGRESS.md#scope-authorization-and-acceptance-reconciliation).
On 25 September the user answered the M5 playtest request with “it seems to work
fine, can push to GH”. This resolves the acceptance/publication blocker through
general user sign-off on the completed implementation. See the dated
[acceptance record](../evidence/m5-8/acceptance.json). Individual manual results
for the §9 matrix were not supplied; neither this feedback nor scripted games
is represented as proof of each specific human-play case.

Retain the following for future playtesting; a discovered defect should include its
seed, action/decision boundary and a locally retained save or diagnostic trace.
Private saves and official-art screenshots do not belong in repository sources.

| Field | Required record |
|---|---|
| Games | One complete Standard game in each human seat; cover Basic and Expanded presets; sample Easy |
| Reproduction | Build/commit, human seat, both decks, profile and game seed |
| Save/resume | Midgame save point, restart result, and a run or nested-choice continuation |
| Opponent behavior | Corp economy/replacements; Runner pressure, matching breaker use and payment behavior |
| Client clarity | Waiting and animations, save discovery/feedback, paused resume and outcome/new game |
| Presentation | 1080p and 1440p; normal and reduced motion |
| Disposition | Pass or concrete defect for each exercised criterion; explicit acceptance of any remaining limitation |

Optional resource grants, broader search, LLM transport, cloud saves, a
deckbuilder and installers remain outside the required M5 deliverables. The
acceptance record does not imply those features must be added.
