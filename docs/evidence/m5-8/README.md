# M5.8 evidence

This folder contains allowlisted summaries and one screenshot without official
card faces. Full accepted-action replays, private snapshots, saves, diagnostic
logs and artwork remain in ignored local storage or a private temporary folder.

- `failed-preflight-summary.json`: the preserved 999/1,000 exploratory result,
  including its one classified Retribution resolution failure. The repaired
  full run is a separate attempt and does not replace this denominator.
- `checker-attempt-summary.json`: the first quick-gate validation attempt,
  preserving the two Corp manifest-encoding rejections. Core-compatible
  escaping fixes the checker; tactical inputs and outcomes were not changed.
- `offline-summary.json`: four cases and eight process phases, moved-save
  checkpoint hashes, terminal outcomes, network/profile/source denial,
  archive and bundle provenance, and two real-renderer cases with eight image
  hashes. Its `humanAcceptance` field deliberately remains false.
- `offline-no-art-1920.png`: the actual exported Runner board after a paused
  load, using fallback card faces and the generic backs bundled with the app.
  This image was captured with networking and the previous profile denied.
- `gate-summary.json` and the twelve suite summaries: exact input/report hashes,
  actual denominators, outcome and replay counts, complete tactical categories
  and named misses, and paired seat/preset economy and action metrics.
- `comparison-timing.json`: measured benchmark-call latency by comparison group,
  seat and preset, explicitly distinguishing per-game percentile summaries from
  an overall percentile. Direct-policy performance is measured separately.
- `performance-summary.json`: 13,828 direct policy timings from twenty serial
  Standard games, summarized by seat/category with action-set sizes, budgets,
  hardware/runtime/build provenance and the raw report's hash. Every measured
  replay matches the ordinary benchmark.
- `validation.json`: exact verification-log hashes, test counts, preserved
  failure disposition and the separately measured real-renderer frame budget.

The final headless run is retained locally at
`artifacts/m5/gate-20260923T081537Z-55389`. Its immutable CLI copy is built from
`6c40688`, including the rules repair in `90457ea`. The offline release carries
that build revision plus the Godot asset probe/export changes subsequently
committed in `a6f9300`; exact compiled bundle hashes are recorded. Policy weights
are unchanged from the frozen previous-policy reference `de4e1cf`.

The completed offline proof is outside the checkout at
`/private/tmp/netrunner-m5-8-offline-20260923`. Both the headless and renderer
proofs use its copied app, with no access to the source checkout or existing
application profiles. The 77-card archive is created, unpacked and validated
under network denial. Official-art screenshots and the archive are not copied
into this folder.

Reproduction commands and interpretation are in [M5_GATE.md](../../M5_GATE.md).
Automated opponents completing these flows do not constitute human playtesting.
