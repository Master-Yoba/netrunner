# M0 Godot/.NET iteration spike

Date: 27 August 2026  
Result: stack accepted for M1, with a persistent-test workflow mitigation  
Raw local artifact: `artifacts/benchmarks/m0-loop-results.tsv` (ignored)

## Environment

- Linux 7.0.0 x86_64
- Intel Core i5-7200U, 2 physical cores / 4 threads, up to 3.1 GHz
- 15 GiB RAM; the desktop was under realistic memory pressure during the run
- .NET SDK 10.0.400; runtime 10.0.11 (plus 8.0.30 for `net8.0` test hosts)
- Godot 4.7.2 stable .NET (`ed1daf0bf`)
- Godot editor open, as it is during normal development
- Baseline commit `ecca07127fa2`, plus the uncommitted M0 bridge work

## Method

`scripts/benchmark-m0.sh` collected ten samples per loop. Each sample touched a
representative source/test file without changing content. Clean solution builds
ran after `dotnet clean` and a restore outside the timed region. Focused tests
included incremental compilation and test execution. Godot edit-to-observe
included C# compilation, headless scene startup, five event animations, and the
responsive-frame assertion.

The first sample is intentionally cold. p95 therefore captures the cold/tool
startup outlier as well as normal warm behavior.

| Loop | Mean | p50 | p95 |
|---|---:|---:|---:|
| Clean solution build | 5.362s | 3.880s | 15.804s |
| Focused Core edit + tests | 4.499s | 3.807s | 8.323s |
| Focused Card-test edit + tests | 5.945s | 5.544s | 7.138s |
| Godot C# edit + compile + observe | 3.467s | 3.091s | 4.538s |

The Godot result comfortably passes the plan's 10-second p50 ceiling. The
one-shot focused Core/Card commands miss the aspirational 3-second p50 ceiling,
by 0.807s and 2.544s respectively. Test execution itself reports tens of
milliseconds; process/MSBuild/test-host startup dominates the one-shot command.

## Decision

Keep Godot 4.7.2 + C# and begin M1. The evidence does not support a language or
client-stack change: the client edit-to-observe loop is about three seconds, the
Core/Card assemblies stay independent of Godot, and the slow portion is
one-shot test infrastructure rather than game code or Godot interop.

The triggered mitigation is explicit:

1. Use `make watch-core` and `make watch-cards` during implementation so the
   build/test process remains resident.
2. Keep card behavior and all rule fixtures in headless projects; never require
   a Godot rebuild for a card test.
3. Re-measure after the first ten real card behaviors. If persistent focused
   feedback exceeds three seconds p50, split the broad Card test assembly by
   primitive/side or introduce a narrower test executable before scaling to all
   77 designs.
4. Keep strict analyzers enabled; do not buy iteration speed by weakening the
   correctness gate.

This resolves OD-04 for M1. The follow-up measurement is an M1 workflow task,
not an M0 architecture blocker.

