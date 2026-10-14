# Vantage Point progress

The record of the [Vantage Point plan](VANTAGE_POINT_PLAN.md)'s slices: status, deviations, evidence and pins.

## VP.1 — Preparation (15 October 2026)

**Status:** complete.

**Card data.**
- `update-card-data.sh` and `build-card-data.py` add the `vp` pack: 66 records from the pinned commit
  `236140cf84491c23995b028bc791281837520b97` into `data/vendor/vantage-point` (local-only and gitignored), with its own
  manifest and the official checks.
- `OfficialCardPacks.VantagePoint` pins its count, path and three sentinel titles. `CardDataResource` embeds it as a third
  pack, and an official build without it fails with an actionable DATA-400 diagnostic, which `verify.sh` checks.
- Fetched again from the pinned commit, the System Gateway and Elevation files came out byte for byte the same, so no
  frozen pin moved.
- The gate worktree links the new directory.

**The Startup pool.**
- `CardPools.Startup` (`sg`, `elev`, `vp`) plays with `VantagePointBehaviorCatalog` (`startup-vp.1`). That catalog
  composes the Gateway + Elevation catalog unchanged and registers no Vantage Point card yet.
- The official fuzz content now loads the Gateway + Elevation pool's own packs, not every embedded pack, so Elevation
  games never see the new cards.

**Formats.**
- Startup 2026 moves to Startup Balance Update 26.09 (D-VP-5), taken from Null Signal's announcement because the pinned
  card data predates it:
  - NBN: Reality Plus is unbanned;
  - Cleaver, Seamless Launch, Offworld Office, Mercia B4LL4RD and Let Them Dream stay banned;
  - a Corp deck may hold at most 4 agendas worth 3 or more points.
- FORMAT-401 tests: Reality Plus isn't banned; five 3-point agendas break the limit; a Vantage Point card is not yet
  legal in the format.

**Coverage.**
- The coverage tool reads a third registry, `data/vantage-point/batch-registry.json`: 66 cards in batches VP.3 (33),
  VP.5 (25) and VP.7 (8), status `planned`.
- The report lists 225 cards: 159 complete, 66 unregistered, no diagnostics. `verify.sh` checks the Vantage Point counts.

**The audit's review.**
- [VANTAGE_POINT_PRIMITIVES.md](VANTAGE_POINT_PRIMITIVES.md) has a "VP.1 review" section. Every card's citations,
  observation, hidden-information and serialization notes are in the registry.
- `data/vantage-point/rulings.json` has 31 rulings citing CR 26.03, each cross-checked against jinteki.net's
  implementation.
- The eight rows "to confirm" use existing vocabulary, so the audit stands at 18 of 66 cards (27%).

**E6.** `card-vocabulary` reads the Startup pool, and `scripts/core-growth.sh` passes the third data root and takes the
third registry. Elevation's whole-set figure was 26% of cards needing no Core change.

**Deviations from the plan.**
- **Vantage Point is not yet a legal pack of Startup 2026.** The plan has it join the legal packs in VP.1. But a
  format is complete exactly when it misses no pack, and a complete format is offered for play. So `vp` stays in
  Startup 2026's missing packs, by its pack code (it was the NetrunnerDB pool id `vantage_point`), until VP.8 completes
  the format. FORMAT-401 now accepts a ban on a card from a missing pack this build embeds (Let Them Dream).
- **Startup 2026 keeps the Gateway + Elevation pool** until VP.8 moves it to the Startup pool, for the same reason.

**Evidence.**
- The official build is clean.
- The Cards tests pass in both content lanes (543), with `VantagePointCardDataTests` new.
- The coverage report is regenerated; its counts are above.

## VP.2 — Primitives A (in progress)

### Design: bad publicity and credit sources

**Bad publicity** is one counter on the Corp (CR 1.9.5d), kept on `RulesState` and omitted from serialization while it is
zero, so every existing state, replay and pin keeps its bytes. Taking and removing it are instructions with an event, a
cost kind removes it (CR 1.14.3a), and the first time each turn the Corp takes it is an occurrence (Editorial Division).
Bad publicity hosted on a card (Luana Campos) is a card counter, which has no effect while hosted (CR 1.13.3).

**The bad publicity fund** is a run-scoped pool on `RunState`, also omitted while zero: at step 6.9.1b the Runner adds a
credit for each bad publicity the Corp has, and at step 6.9.6b what is left returns to the bank (CR 10.6.3).

**Credits from outside the pool.** A Runner payment during a run already chooses how much comes from the credit pool and
how much from run-scoped credits (Overclock's). That one choice stays, and its second share becomes "credits from
outside the pool", spent in a fixed order:

1. the bad publicity fund;
2. credits on the active run event that isn't stealth (Overclock);
3. credits on the active run event that is stealth (Aircheck);
4. hosted credits on installed stealth cards that may be spent during runs (Methuselah, Touchstone).

Non-stealth credits go before stealth ones and expiring credits before lasting ones, so the order never costs the Runner a
payment it could otherwise make (an engine ruling, as Elevation's for declared-use credits). A cost payable only with
credits from stealth cards (Corsair, Baker, Lampades) draws only on 3 and 4, in that order. Shackleton Grid's occurrence
fires whenever a payment's outside share is above zero, so it counts the fund as Null Signal's rules and jinteki.net do.
While Aircheck is active the Runner's credit pool offers nothing to a payment and can't lose credits.
