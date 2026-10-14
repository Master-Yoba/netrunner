# Vantage Point implementation plan — the third card set

Status: plan, 14 October 2026. The user's decision (14 October): after the bot's heuristic track found no large misplay
left on the Core Battle Box (the misplay sweep, `docs/BOT_VERSIONS.md`), the next work is the Vantage Point card set.
It completes Startup 2026 (System Gateway, Elevation and Vantage Point), the format the game was waiting for (D-EL-3,
D-EL-10).

Companion documents:
- **[VANTAGE_POINT_PRIMITIVES.md](VANTAGE_POINT_PRIMITIVES.md):** the primitive audit, every card mapped to the vocabulary
  it needs.
- **`data/vantage-point/batch-registry.json`** (VP.1): the batch assignment.
- **[Elevation plan](history/ELEVATION_PLAN.md):** the milestone this one mirrors, and its
  [progress record](history/ELEVATION_PROGRESS.md).

## 1. Scope and gate

Vantage Point (Null Signal Games, released 2 March 2026, pack `vp`) is 66 cards:

| Side | Identities | Agendas | Assets | Upgrades | ICE | Operations | Events | Hardware | Programs | Resources |
|---|---|---|---|---|---|---|---|---|---|---|
| Corp | 2 | 5 | 5 | 5 | 14 | 10 | | | | |
| Runner | 2 | | | | | | 7 | 5 | 6 | 5 |

That's 10 cards for each Corp faction, 8 for each Runner faction, and one neutral card a side. The two Corp identities
(Méliès U, Editorial Division) are Jinteki and NBN; the two Runner identities (Virtual Intelligence, Hiram) are Criminal
and Shaper. **This milestone implements all 66** on the engine that plays System Gateway and Elevation, with:
- the card data, decks and formats to play them, Startup 2026 among them;
- the bots able to play them sensibly;
- a client that shows every new card state.

**Hard exit gate** (Elevation's, section 1):
- **Registration:** 66 of 66 registered, tested and reviewed in the coverage report (the CARD-401 checklist); no drift.
- **Core growth (E6):** in each card batch, at least 80% of its cards needed no Core change after that batch's primitive
  slice landed (`scripts/core-growth.py`). Every primitive was design-reviewed, and the whole-set figure is reported.
- **Formats:** Startup 2026 complete and playable end to end with its decks; the other formats unchanged.
- **Rules:** 10,000 clean official fuzz games over the new pool, the full `make m5-gate`, the tactics suite and every
  regression suite green.
- **Bots:**
  - every new card is played by the heuristic bots without errors, and with a valuation that isn't the default;
  - the deck-pool ladder over the new decks shows no deck warning against the previous versions;
  - the Core Battle Box ladder doesn't move: its decks hold no Vantage Point card, so its games are unchanged unless a
    shared primitive changed them, and any change is measured.
- **Client:** both sides finish games with every new deck through the client. Every new state is presented: bad
  publicity and its run fund, stealth credits, cards scored as agendas, hosted face-down cards, ICE that gained a
  subroutine, virus counters on ICE, and Méliès U's hidden identity.

**Out of scope:**
- the Liberation cycle (so Startup 2025 stays incomplete);
- per-format bot data beyond what the ladders need;
- M6 and RC work;
- any card art.

## 2. Baseline (14 October 2026)

- **Engine:**
  - all 77 System Gateway and 82 Elevation cards, on a behaviour vocabulary of 59 instruction kinds and 37 trigger
    occurrences, with Elevation's conditions, costs, static modifiers and zones;
  - 10,000 clean official fuzz games over the Elevation pools (EL.8).
- **Card data:** a multi-pack local-only snapshot (`sg`, `elev`) from the pinned upstream commit
  `236140cf84491c23995b028bc791281837520b97`, which already contains `vp` (66 records, codes 36001–36066).
- **Rules:** Null Signal Comprehensive Rules 26.03 (`data/vendor/rules`), which already covers Vantage Point: bad
  publicity (10.6), interface abilities, Méliès U's copies (1.x) and Scapegoat are in it.
- **Formats:** `startup-2026.json` exists, marked incomplete with `missingPacks: ["vantage_point"]`; its ban list
  (Balance Update 26.05) already names one Vantage Point card, Let Them Dream (36066).
- **Bots:** Corp v34 and Runner v37 by default; the Core Battle Box, Gateway, Elevation and NSG core-set ladders; the
  misplay sweep (`ai-offers`), the oddity lints and the saved-game tests.

**The audit:** 18 of the 66 cards (27%) use only existing vocabulary. The other 48 need 33 new primitives, 21 of them for
one card, in six families: bad publicity; credit sources; scoring and agendas; costs and values; runs and ICE; and zones,
information and identities.

## 3. Decisions

**Made:**
- **D-VP-1, the rules authority:** CR 26.03, as for Gateway and Elevation.
- **D-VP-2, the card data:** the same pinned commit, a third pack in the multi-pack snapshot (`sg`, `elev`, `vp`), one
  manifest per pack and the official checks per pack (77, 82 and 66). It stays gitignored; CI provisions it with the same
  `update-card-data.sh` call.
- **D-VP-3, the E6 budget:** D-EL-5's, unchanged: primitives in their own slices before their cards; at least 80% of each
  card batch needing no Core change; the whole-set share reported, not gated; single-card primitives generalized where
  the rules allow (VP-SCORING-FACTS and VP-RUN-FACTS are turn facts beside Elevation's, not new mechanisms).
- **D-VP-4, bots per batch:** D-EL-6's: no batch closes until the heuristic bots and the contract agent play its cards
  without errors, each with a non-default valuation.

**For the user to decide (each has a default the plan proceeds with):**
- **D-VP-5, formats:**
  - *Default:* Startup 2026 becomes complete and playable (`complete: true`, no missing packs), with its 26.05 ban list.
  - A "System Gateway + Elevation + Vantage Point" format with no ban list, as D-EL-10 did for the Core Battle Box,
    only if D-VP-6's decks need a banned card.
- **D-VP-6, decks:** the bots and the menu need Startup 2026 decks.
  - *Default:* real players' lists, as the Core Battle Box was (D-EL-10): one Corp and one Runner deck per faction (14),
    chosen from NetrunnerDB's popular Startup 2026 lists, or a published Startup battle box if one exists. The user
    approves the source before VP.8.
  - The Core Battle Box stays the bots' main ladder pool until the new decks have a ladder of their own.
- **D-VP-7, the menu:** *default:* Startup 2026 appears as a format choice beside the existing ones; random opponents
  draw from the chosen format.
- **D-VP-8, Méliès U:** its hidden choice among identity copies is the set's hardest card for the engine, the client and
  the bots.
  - *Default:* implemented in full in VP.6 and VP.7, with an observation test that the Runner never sees the active copy
    before it flips.
  - Alternatively, registered last, so a delay doesn't hold the other 65.

## 4. Slices

Each slice is a branch and a PR into `dev`, gated as AGENTS.md says:
- `BOT_CHECK_SUITES=1 make bot-check` per change;
- the full gates once per batch, in the worktree (`make gates REF=…`);
- the per-slice fuzz on the affected pools, and a ladder for each new bot version.

Progress goes to `docs/VANTAGE_POINT_PROGRESS.md`.

### VP.1 — Preparation: card data, coverage, formats, the audit's review

- **Card data:** `update-card-data.sh` and `build-card-data.py` add `vp` to the snapshot, with its manifest, count and
  checksum; `CardDataResource` exposes it. DATA-400 tests cover a missing or corrupt `vp`.
- **Coverage:** `tools/Netrunner.CardCoverage` and the CARD-401 validator read a third registry,
  `data/vantage-point/batch-registry.json` (every card's batch and planned primitives, status `planned`). The report
  lists 225 cards: 159 complete, 66 planned. No drift in `verify.sh`.
- **The audit review:** confirm or correct every row of VANTAGE_POINT_PRIMITIVES.md against the CR and the engine
  (`card-vocabulary --json`), the rows marked "to confirm" first. Each card's citations go to
  `data/vantage-point/rulings.json`.
- **Formats:** Startup 2026 gains `vp` in its legal packs and keeps its ban list; it stays marked incomplete until VP.8.
- **E6:** `make core-growth` reads the third registry; the Elevation figure goes into the progress record.
- **Gate:** data loads and validates in both modes; the coverage report lists 225 cards; `verify.sh` green.

### VP.2 — Primitives A: bad publicity, credit sources, scoring, costs and values

The families "bad publicity", "credit sources", "scoring and agendas" and "costs and values":
- bad publicity: the counters, the run's fund at steps 6.9.1b and 6.9.6b, taking and removing it as effects and costs,
  its occurrence, and hosted bad publicity;
- credits tagged by source: stealth-only payments, run-only hosted credits, the pool lock, and spending from outside the
  pool as an occurrence;
- additional costs to steal and to score, cards scored as agendas, agenda points changed in the opponent's score area,
  and the scoring turn facts;
- trashing as a cost, play-, click- and rez-cost modifiers, the new amounts, the run turn facts, the advance occurrence and
  the uninstall interrupt.

Bad publicity and stealth credits change the run structure and every credit payment, so each lands with CR-cited
scenario fixtures (M2/M4 style) and the differential fuzz over every existing pool before any card uses them: Gateway
and Elevation games must replay unchanged. **No cards yet.**

### VP.3 — Batch 1 (33 cards): bad publicity, stealth, scoring and economy

The bad publicity cards, the stealth credit cards that don't interact with ICE, the scoring and score-area cards, and the
economy. Listed in the registry. **Gate:** the per-batch process (section 6), with the E6 budget.

### VP.4 — Primitives B: runs and ICE

The family "runs and ICE": passing, fully breaking and bypassing ICE as occurrences; strength changes on every ICE; the
subroutine-broken and encounter-ended occurrences; ICE gaining a subroutine; "can't steal or trash" for a run; jacking
out to prevent a subroutine; runs that can't be declared successful and access limits; paying an accessed card's cost;
virus counters on Corp ICE. Every run-timing primitive gets a scenario fixture with CR citations.

### VP.5 — Batch 2 (25 cards): ICE, runs and access

The ICE, the run events and run-interacting programs, hardware and upgrades. **Gate:** as VP.3.

### VP.6 — Primitives C: zones, information and identities

The family "zones, information and identities": arranging a deck's top; face-down hosting on a program; resources hosted
on a resource; Archives cards turned face up as an occurrence; an ability used from HQ with its own reveal and trash as
the cost; and Méliès U's secret identity (D-VP-8). Hidden-information tests are central: what each side sees of a
face-down host, an arranged deck and the active identity copy.

### VP.7 — Batch 3 (8 cards): zones and hidden information

Méliès U, Knowledge Seeker, Cultivate, Read-Write Share, Hackerspace, Nurse Hạnh, Tocsin and Hiram. **Gate:** as VP.3.

### VP.8 — Integration: decks, formats, bots, client and the milestone gate

- **Decks:** D-VP-6's decks validated under Startup 2026, with presets and provenance recorded; Startup 2026 marked
  complete.
- **The menu:** Startup 2026 as a format choice (D-VP-7). The saved game records the format.
- **Bots:**
  - the deck-pool ladder over the new decks, and the misplay sweep (`ai-offers`) over its games: no card the bots install
    and never use, the fault Elevation's Idiosyncresis and Plutus showed after their batches closed;
  - the run model counts the bad publicity fund and stealth credits, and the Runner's run choice counts additional steal
    costs;
  - the tracker (A9) follows the new hidden zones: face-down hosted cards and the secret identity. `ai-belief` stays above
    its prior.
- **Hardening:** 10,000 clean official fuzz games over the Startup 2026 pool, and the full `make m5-gate` and tactics.
- **Client:** every new state presented, accessibility re-checked at 1920×1080 and 2560×1440, and the smoke matrices
  extended to a new deck.
- **The E6 report** over the whole milestone, in the progress record.
- **Gate:** the checklist in section 7.

## 5. Card assignment

The registry (VP.1) holds every card's batch and the primitives it needs; [VANTAGE_POINT_PRIMITIVES.md](VANTAGE_POINT_PRIMITIVES.md)
is the readable form. VP.1's review may move cards between batches; the registry is the source of truth.

| Batch | Cards | Using only existing vocabulary |
|---|---|---|
| VP.3 | 33 | 7 |
| VP.5 | 25 | 10 |
| VP.7 | 8 | 1 |

## 6. Per-batch process

Elevation's ([Elevation plan](history/ELEVATION_PLAN.md), section 6), with one step added from what Elevation taught:
1. **Primitives first,** in the previous slice, with engine tests and, for new timing windows, a scenario fixture with CR
   citations.
2. **Bind every card:** compiled specs in `Netrunner.Cards` (`src/Netrunner.Cards/VantagePoint/`), registered in the
   catalog. A reviewer checks the spec against the full printed text.
3. **Tests:** at least one positive test per card; negative, interaction and ruling-derived tests in proportion to its
   complexity; observation tests for every reveal, hidden zone or face-down host.
4. **The registry:** mark the batch complete, regenerate the coverage report; `verify.sh` shows no drift.
5. **E6:** `make core-growth` for the batch's range, recorded; below 80%, a design review of the primitives that caused it.
6. **Bots:**
   - the contract agent plays every new decision;
   - the heuristic bots value each new card, with a new policy version per side when behaviour changes, a saved-game
     regression test for each valuation that needed thought, `make bot-check`, and a ladder for the new version;
   - **new:** the misplay sweep over a ladder of decks holding the batch's cards, so a card that is installed and then
     never advanced, rezzed or used is found while its batch is open, not after.
7. **Proofs:** extend the simulation decks and run the batch's headless proof (agent-driven, seed-pinned).
8. **The client:** each new state shown in a smoke-flagged scene; the M3, latency and main-menu smokes stay green.
9. **Progress:** the batch recorded in `docs/VANTAGE_POINT_PROGRESS.md`: status, deviations, evidence and pins.

## 7. Milestone gate checklist

- [ ] **Coverage:** 225 of 225 bound, tested and checklist-complete; no drift; CARD-401 green.
- [ ] **E6:** each card batch at or above 80% with no Core change past its primitive slice; the whole-set report recorded.
- [ ] **Formats:** Startup 2026 complete, loading and validating; playable with its decks.
- [ ] **Decks:** each new deck validates, loads and plays human against bot through the client, on each side.
- [ ] **Hardening:** 10,000 clean official fuzz games over the pool; the full `make m5-gate`; tactics; every suite green.
- [ ] **Bots:**
  - every Vantage Point card played without errors, with a non-default valuation, and none installed but never used in
    the sweep;
  - the deck-pool ladder has no deck warning; the Core Battle Box ladder unchanged or measured;
  - `ai-belief` is above its prior on the new pool.
- [ ] **Client:** every new state presented; accessibility re-checked; the smoke matrices include a new deck.
- [ ] `docs/VANTAGE_POINT_PROGRESS.md` complete, with evidence and deviations.

## 8. Client work index

| State | Where it shows | Slice |
|---|---|---|
| Bad publicity | The Corp's header, beside its credits; hosted bad publicity on its card | VP.2, VP.3 |
| The bad publicity fund | The run HUD, spendable in payments during the run | VP.2 |
| Stealth credits | Marked on their cards, and as a payment source when a cost accepts only them | VP.2, VP.3 |
| Cards scored as agendas | The score area, with their points and restrictions | VP.2, VP.3 |
| Additional steal and score costs | The steal and score prompts | VP.2, VP.3 |
| ICE that gained a subroutine | The encounter panel, the gained subroutine first | VP.4, VP.5 |
| Virus counters on ICE | Counters on the ICE | VP.4, VP.5 |
| Runs that can't succeed, access limits | The run HUD and the history | VP.4, VP.5 |
| Face-down hosted cards | Beside the host: their faces to the Runner, their backs to the Corp | VP.6, VP.7 |
| Méliès U | The identity plate: the Corp sees the active copy, the Runner a copy of unknown side until it flips | VP.6, VP.7 |

## 9. Risks

- **Bad publicity and stealth credits touch every run and every payment.** A regression there changes games of every
  existing pool. Mitigation: VP.2 lands them alone, with the differential fuzz over every pool and saved replays of
  Gateway and Elevation games replaying identically.
- **Hidden information:** Méliès U's active copy and face-down hosted cards are new leak channels. Mitigation: an
  observation test per primitive, the twin suite, and the tracker reading only observations.
- **E6:** 21 of 33 primitives serve one card. Mitigation: each is reviewed for a general form before it lands (the turn
  facts and cost modifiers are the likely ones to fold together).
- **Bots:** new cards that fall back to default valuations, and new card kinds the policies never value (Elevation's
  Idiosyncresis and Plutus went unused for weeks). Mitigation: D-VP-4 per batch, and the misplay sweep as a batch step.
- **Decks:** Startup 2026's ban list or the chosen lists may not fit together. Mitigation: D-VP-5's fallback format.

## 10. For the goal session

- **Order:** VP.1 first; then VP.2 → VP.3 → VP.4 → VP.5 → VP.6 → VP.7 → VP.8; a primitive slice and its batch may share a
  PR if both are small (VP.6 and VP.7 likely will).
- **Defaults:** D-VP-5 to D-VP-8 proceed on their defaults unless the user decides otherwise; D-VP-6's deck source waits
  for the user's approval before VP.8.
- **Merging:** each slice's PR is merged into `dev` when its gates pass and Codex's review is answered, under the
  standing routine; a stack is merged in one push.
- **No LLM playtesting or reviews:** the session doesn't run Codex or Claude games or reviews, nor spend model usage on
  testing. Bot quality is judged by the ladders, the sweep, the oddity lints and saved-game tests.
- **Never commit** the official card data, card text, `.env` or `.tools/`.
