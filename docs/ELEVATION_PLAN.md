# Elevation implementation plan — the second card set

Status: plan, 2 October 2026. Decided at Wave 5's decision point ([implementation plan](IMPLEMENTATION_PLAN.md), section 2):
the next card set is Elevation, ahead of M6 and an RC, because the bots' remaining faults increasingly come from a pool
too small to need deck-aware play.

Companion documents:
- **[ELEVATION_PRIMITIVES.md](ELEVATION_PRIMITIVES.md):** the primitive audit, every card mapped to the vocabulary it
  needs.
- **`data/elevation/batch-registry.json`:** the batch assignment.
- **[M4 plan](history/M4_PLAN.md):** the Gateway milestone this one mirrors.

## 1. Scope and gate

Elevation (Null Signal Games, released 24 April 2025, pack `elev`) is 82 cards:

| Side | Identities | Agendas | Assets | Upgrades | ICE | Operations | Events | Hardware | Programs | Resources |
|---|---|---|---|---|---|---|---|---|---|---|
| Corp | 8 | 8 | 8 | 3 | 12 | 9 | | | | |
| Runner | 6 | | | | | | 9 | 5 | 8 | 6 |

That's 11 cards for each of the seven factions, plus 5 neutral. **This milestone implements all 82** on the engine that
plays System Gateway, with:
- the card data, decks and formats to play them;
- the bots able to play them sensibly;
- a client that shows every new card state.

**Hard exit gate:**
- **Registration:** 82 of 82 registered, tested and reviewed in the coverage report (the CARD-401 checklist, as for
  Gateway); no drift.
- **Core growth (E6):** in each card batch, at least 80% of its cards needed no Core change, measured by
  `scripts/core-growth.py` after that batch's primitive slice landed (D-EL-5). Every primitive was design-reviewed, and
  the whole-set figure is reported.
- **Formats (C2):** versioned format snapshots load, validate decks and are selectable. At least "System Gateway +
  Elevation" is playable end to end with its decks.
- **Rules:** 10,000 clean official fuzz games over the new pool (the engine plan's gate for a card set), the full
  `make m5-gate`, the tactics suite and every regression suite green.
- **Bots:**
  - every new card is played by the heuristic bots without errors, and with a valuation that isn't the default;
  - the deck-pool ladder over the new decks shows no deck warning against the previous versions;
  - a Claude Sonnet or Codex check on fixed deals doesn't regress.
- **Client:** both sides finish games with every new deck through the client. Every new state is presented: agenda
  counters, flipped identities, threat level, face-up agendas, hosted face-up cards, recurring credits and removed-
  from-game cards.

**Out of scope:**
- Vantage Point and the Liberation cycle (so neither Startup snapshot is complete; see D-EL-3);
- per-format bot data beyond what A7 needs here;
- M6 and RC work;
- any card art.

## 2. Baseline (2 October 2026)

- **Engine:**
  - all 77 Gateway cards, on a behaviour vocabulary of 37 instruction kinds, 26 trigger occurrences, 13 condition kinds,
    9 cost kinds and 2 static modifiers ([M4 primitive audit](M4_PRIMITIVES.md));
  - 10,000 clean official fuzz games (Wave 5).
- **Card data:** one local-only pack (`sg`, 77 records) from the pinned upstream commit
  `236140cf84491c23995b028bc791281837520b97`, which already contains `elev`. The build accepts one input directory.
- **Bots:**
  - Corp v26 and Runner v22, with A9's tracker of hidden cards, inference and copy counting;
  - Easy, Standard and Hard, and five personalities;
  - the Gateway deck-pool ladder;
  - Codex and Claude Sonnet as outside opponents (`scripts/ai-codex-play.sh`, `scripts/ai-claude-play.sh`).

**The E6 baseline** (`scripts/core-growth.py` over Gateway's slices, from git history):

| Gateway slice | Core lines added | New vocabulary | Cards needing no Core change |
|---|---|---|---|
| 4.2 | +19,932 | 208 | 0 of 12 |
| 4.3 | +12,514 | 91 | 0 of 14 |
| 4.4 | +5,524 | 24 | 0 of 7 |
| 4.5 | +5,814 | 46 | 0 of 7 |
| 4.6 | +1,077 | 21 | 3 of 12 |
| 4.7 | +934 | 16 | 9 of 20 |
| **All** | | | **12 of 72 (17%)** |

Gateway built its vocabulary from nearly nothing, so this is a floor. The share rose as the vocabulary matured.

**The Elevation audit:** 23 of the 82 cards (28%) use only existing vocabulary. The other 59 need 51 new primitives,
most of them used by one card. They fall in seven families: values and conditions; counters, scoring and credits; timing
occurrences; identity state; card sources and zones; information; and runs and ICE.

## 3. Decisions

**Made:**
- **D-EL-1, the rules authority:** Null Signal Comprehensive Rules 26.03, as for Gateway (`data/vendor/rules`).
- **D-EL-2, the card data:** the same pinned commit, extended to a multi-pack local-only snapshot (`sg` and `elev`), with
  one manifest per pack and the official checks per pack (77 and 82). It stays gitignored, and CI provisions it with the
  same `update-card-data.sh` call.
- **D-EL-5, the E6 budget:**
  - Primitives land in their own slices, before the card batches that use them.
  - The budget is at least 80% of each card batch's cards needing no Core change, measured over the batch's own range,
    after its primitive slice.
  - The whole-set share is reported, not gated; about 28% is expected.
  - A card that misses the budget triggers a design review of the primitive vocabulary, not a stop.
  - Single-card primitives are generalized where the rules allow. For example, one "source zone" option on install and
    play covers Archives, the heap, the score area and a host.
- **D-EL-6, bots per batch:** no batch closes until the heuristic bots and the contract agent play its cards without
  errors, and each card has a non-default valuation.
- **D-EL-7, the tutorial:** stays on the Gateway teaching decks with its frozen bot.

**For the user to decide (each has a default the plan proceeds with):**
- **D-EL-3, formats:**
  - Startup 2026 is System Gateway, Elevation and Vantage Point; Startup 2025 was System Gateway, Elevation and the
    Liberation cycle. Neither is complete with Elevation alone.
  - *Default:* ship a "System Gateway + Elevation" snapshot (Startup-shaped deck rules, the Startup 2026 ban list applied
    to the cards we have) as the playable format.
  - Ship the Startup 2025 and 2026 snapshots as data that validate but are marked incomplete, until Vantage Point or
    Liberation lands.
- **D-EL-4, decks:** Null Signal published no Elevation sample decks in the card data.
  - *Default:* build one Corp and one Runner deck per faction from System Gateway + Elevation (14 decks), legal under
    D-EL-3's snapshot and in the spirit of the Gateway sample decks (a clear plan each). Record their provenance as ours.
  - The user reviews the lists before they're offered in the menu.
  - Alternatively, curated NetrunnerDB decklists, where licensing and legality allow.
- **D-EL-8, the menu:**
  - *Default:* the Gateway decks stay; the new decks appear under a format choice (Gateway, Gateway + Elevation).
  - Random opponents draw from the chosen format.

## 4. Slices

Each slice is a branch and a PR into `dev`, gated as AGENTS.md says:
- `make bot-check` per change;
- the full gates once per batch, in the worktree (`make gates REF=…`);
- ladders and outside-opponent checks for bot changes.

Progress goes to `docs/ELEVATION_PROGRESS.md`.

### EL.1 — Preparation: card data, coverage, formats, deck rules

- **Card data:**
  - `update-card-data.sh` and `build-card-data.py` extract `sg` and `elev` from the pinned commit into a multi-pack
    snapshot. Each pack gets its own manifest, count and checksum; the official checks run per pack.
  - The generated payload carries both, and `CardDataResource` exposes the pool and per-pack membership.
  - DATA-400 tests cover a missing or corrupt pack.
- **Coverage:**
  - `tools/Netrunner.CardCoverage` and the CARD-401 validator read both registries (`data/m4` and `data/elevation`).
  - The report lists 159 cards: Gateway's 77 complete, Elevation's 82 planned. No drift in `verify.sh`.
- **The audit review:** confirm or correct every row of ELEVATION_PRIMITIVES.md against the CR and the engine. Add each
  card's citations to a rulings file (`data/elevation/rulings.json`).
- **C2, format snapshots:**
  - A schema and files under `data/formats/`: legal packs, banned and restricted cards, deck rules (minimum size,
    influence, copies, agenda points, the Startup 2026 "at most 3 agendas worth 3 or more points" rule) and an
    effective date.
  - Files for Gateway, Gateway + Elevation, Startup 2025 and Startup 2026, the last two marked incomplete.
  - A validator in DECK-401's style; deck validation takes a format.
- **E6:** extend `make core-growth` (prepared) to read both registries. The Gateway baseline goes into the progress doc.
- **Gate:** data loads and validates in both modes; the coverage report lists 159 cards; the formats validate the
  Gateway decks and reject an illegal one per rule; `verify.sh` green.

### EL.2 — Primitives A: values, conditions, counters, timing, identity state

The families "values and conditions", "counters, scoring and credits", "timing occurrences" and "identity state":
- threat level, dynamic amounts, tag-count and turn-fact conditions, conditional strength;
- Dividends and agenda counters, score-area abilities, forfeiting, recurring credits, the region limit, Persistent;
- the action-phase-end, tag-removed, hand-size-discard and rez-during-run occurrences;
- double-sided identities.

Each primitive gets engine tests (positive, negative), observation tests where hidden information is involved, and
replayable frames. **No cards yet.** The E6 report records this slice's Core growth.

### EL.3 — Batch 1 (44 cards): counters, economy, agendas and turn structure

Most of the economy, the Dividends agendas, the scoring-adjacent assets and operations, the six flip/turn-structure
identities, the breakers and other programs, and the simple events and resources. They're listed in the registry.

**Gate:** the per-batch process (section 6), with the E6 budget, at least 80% of the batch needing no Core change past
EL.2.

### EL.4 — Primitives B: card sources, zones and information

The families "card sources and zones" and "information":
- install and play from Archives, the heap and the score area (generalized);
- nested play, and hosted face-up cards with any-player abilities;
- face-up agendas, and moving an upgrade;
- private looks at a deck's top;
- revealing a grip, a subset of HQ, a card while accessed, or as a cost;
- random HQ trash, Sabotage, and "a copy of a card accessed".

Hidden-information tests are central here: what each side sees during a look, a reveal or a face-up host.

### EL.5 — Batch 2 (16 cards): zones and hidden information

Consoles that host cards, face-up agendas, Archives and score-area recursion, and the look and reveal identities and
operations. **Gate:** as EL.3.

### EL.6 — Primitives C: runs and ICE

The family "runs and ICE":
- moving the Runner and changing the attacked server;
- preventing a run's end unless the Corp pays;
- bypass, derez, and swapping the approached ICE with a card from HQ or Archives;
- break restrictions, resolving another ICE's subroutine, and ICE gaining subtypes;
- ICE the Runner can't trash, mods hosted on breakers, and strength that lasts the run;
- alternative and global rez-cost changes, and trash costs paid during a run.

Every run-timing primitive gets a scenario fixture with CR citations (the M2/M4 timing-scenario style).

### EL.7 — Batch 3 (22 cards): runs, ICE and encounters

The ICE, the run events, and the run-interacting hardware, resources, upgrades and identities. **Gate:** as EL.3.

### EL.8 — Integration: decks, formats, bots, client and the milestone gate

- **Decks:** D-EL-4's decks validated under D-EL-3's snapshots, with presets and provenance recorded.
- **The menu:** a format choice (D-EL-8). The saved game records the format.
- **Bots:**
  - A7 on this pool: the deck-pool ladder over the new decks, and per-format fitted evaluation weights if a refit on
    the new pool's games beats the general fit on held-out games.
  - The tracker (A9) follows the new zones: hosted face-up cards, face-up agendas, Archives installs and removed-from-
    game cards. `ai-belief` stays above its prior.
  - A Claude Sonnet check on fixed deals against the previous defaults.
- **Hardening:** 10,000 clean official fuzz games over the pool, and the full `make m5-gate` and tactics.
- **Client:** every new state presented, accessibility re-checked at 1920×1080 and 2560×1440, and the smoke
  matrices extended to a new deck.
- **The E6 report** over the whole milestone, in the progress doc.
- **Gate:** the checklist in section 7.

## 5. Card assignment

`data/elevation/batch-registry.json` holds every card's batch and the primitives it needs (`newPrimitives`).
[ELEVATION_PRIMITIVES.md](ELEVATION_PRIMITIVES.md) is the readable form. EL.1's audit review may move cards between
batches; the registry is the source of truth.

| Batch | Cards | Using only existing vocabulary |
|---|---|---|
| EL.3 | 44 | 21 |
| EL.5 | 16 | 0 |
| EL.7 | 22 | 2 |

## 6. Per-batch process

M4's process ([M4 plan](history/M4_PLAN.md), section 6), plus three steps:
1. **Primitives first:** they're in the previous slice, with engine tests and, for new timing windows, a scenario
   fixture with CR citations.
2. **Bind every card:** compiled specs in `Netrunner.Cards`, registered in the catalog. A reviewer checks the spec
   against the full printed text.
3. **Tests:**
   - at least one positive test per card, with the test metadata;
   - negative, interaction and ruling-derived tests in proportion to the card's complexity;
   - observation tests for every reveal, hidden zone or face-up host.
4. **The registry:** mark the batch complete in `data/elevation/batch-registry.json`, regenerate the coverage report;
   `verify.sh` shows no drift.
5. **E6:** run `make core-growth` for the batch's range and record it. Below 80% with no Core change, record a design
   review of the primitives that caused it.
6. **Bots:**
   - The contract agent plays every new decision.
   - The heuristic bots value each new card. A new policy version per side when behaviour changes.
   - A saved-game regression test for each valuation that needed thought.
   - `make bot-check`, and a ladder for the new version.
7. **Proofs:** extend the simulation decks and run the batch's headless proof (agent-driven, seed-pinned).
8. **The client:** each new state is shown in a smoke-flagged scene. The M3, latency and main-menu smokes stay green.
9. **Progress:** record the batch in `docs/ELEVATION_PROGRESS.md`: status, deviations, evidence and pins.

## 7. Milestone gate checklist

- [ ] **Coverage:** 159 of 159 bound, tested and checklist-complete; no drift; CARD-401 green.
- [ ] **E6:** each card batch at or above 80% with no Core change past its primitive slice; the whole-set report recorded.
- [ ] **Formats:** the snapshots load and validate; Gateway + Elevation is playable with its decks.
- [ ] **Decks:** each new deck validates, loads and plays human against bot through the client, on each side.
- [ ] **Hardening:** 10,000 clean official fuzz games over the pool; the full `make m5-gate`; tactics; every suite green.
- [ ] **Bots:**
  - every Elevation card played without errors, with a non-default valuation;
  - the deck-pool ladder has no deck warning;
  - `ai-belief` is above its prior on the new pool;
  - an outside-opponent check doesn't regress.
- [ ] **Client:** every new state presented; accessibility re-checked; the smoke matrices include a new deck.
- [ ] `docs/ELEVATION_PROGRESS.md` complete, with evidence and deviations.

## 8. Client work index

| State | Where it shows | Slice |
|---|---|---|
| Agenda counters and score-area abilities | Score areas, with the ability usable from either | EL.2, EL.3 |
| Threat level | Header, beside the scores | EL.2 |
| Flipped identities | The identity plate, both faces inspectable | EL.2 |
| Recurring credits | Hosted pools on the card | EL.2 |
| Face-up installed agendas | Their server root, marked inactive | EL.4 |
| Hosted face-up cards on consoles | Beside the console, playable like grip cards | EL.4 |
| Removed-from-game cards | A pile per player, inspectable | EL.4 |
| Bypass, derez, moved Runner and changed server | The run HUD and the history | EL.6 |

## 9. Risks

- **Rules complexity:** run-topology changes (moving the Runner, changing the attacked server, swapping the approached
  ICE) interact with every run-timing window. Mitigation: scenario fixtures with CR citations, and the 10,000-game fuzz
  in EL.8 with the differential check.
- **Hidden information:** looks, reveals and face-up hosts are new leak channels. Mitigation: an observation test per
  primitive, the twin suite, and the tracker reading only observations.
- **E6:** if a batch misses the budget, the primitive vocabulary is too specific. Mitigation: review each single-card
  primitive for a general form before it lands.
- **Bots:** new decisions may fall back to the default valuation and play badly unnoticed. Mitigation: D-EL-6 per batch,
  saved-game tests, the oddity lints, and outside-opponent reviews.
- **Formats:** Startup isn't complete without Vantage Point or Liberation. Mitigation: D-EL-3's interim format, and
  incomplete snapshots kept as data.

## 10. Prep done (branch `feat/elevation-prep`)

- **This plan, and [ELEVATION_PRIMITIVES.md](ELEVATION_PRIMITIVES.md):** the audit of all 82 cards.
- **`data/elevation/batch-registry.json`:** every card's batch and planned primitives, status `planned`.
- **E6 tooling:**
  - `netrunner-cli card-vocabulary [--json]`: each registered card's engine vocabulary, by reflection over its
    evaluation descriptor.
  - `scripts/core-growth.py`: per batch, the Core files, lines and vocabulary its git range added, and which of its
    cards used them.
  - `make core-growth CORE_GROWTH_ARGS='--registry … --range ID=BASE..HEAD'` (`scripts/core-growth.sh`) builds the
    official CLI, dumps the vocabulary and runs the report.
  - The Gateway baseline above.

## 11. For the goal session

- **Order:**
  - EL.1 first;
  - then EL.2 → EL.3 → EL.4 → EL.5 → EL.6 → EL.7 → EL.8;
  - a primitive slice and its batch may share a PR if both are small.
- **Defaults:** D-EL-3, D-EL-4 and D-EL-8 proceed on their defaults unless the user decides otherwise. The decklists
  (D-EL-4) wait for the user's review before the menu offers them.
- **Merging:** each slice's PR is merged into `dev` when its gates pass, if the user has allowed that for the session;
  otherwise the PRs are left ready. A stack is merged in one push.
- **Outside opponents:**
  - Claude Sonnet: about $0.90 a game; measure first and use paired fixed-deal arms.
  - Codex: when its quota allows.
- **Never commit** the official card data, card text, `.env` or `.tools/`.
