# Post-M5 improvement roadmap

Status: proposed, not started
Covers: [engine and architecture](ENGINE_ARCHITECTURE_PLAN.md) (E*),
[AI opponent](AI_OPPONENT_PLAN.md) (A*) and
[UI/UX redesign](UI_UX_REDESIGN_PLAN.md) (U*)

This document orders the phases of the three plans. The plans say *what* to
build and how each phase is verified. This roadmap says *when*, *why in that
order*, and what must be true before moving on.

## 1. Ordering principles

1. **Feel first, with enablers pulled in just in time.** The UI is the weakest
   part of the product today, so the critical path runs through the UI track.
   Engine phases are scheduled right before the UI or AI phase that needs them,
   not as a separate engine milestone.
2. **Safety net before refactors.** Random-legal fuzzing (E3) and the capture
   harness (U0) come first, because later phases change engine internals and
   the board renderer, and regressions there are otherwise found late.
3. **Measure before tuning.** The AI rating ladder (A1) comes before any
   change in bot strength, so every AI phase can show evidence.
4. **One vertical slice before widening.** The event timeline (U3) and the
   layout (U4) are first proven on a single Runner run and a single Corp score,
   then extended to every event.
5. **Keep the repository contracts.** Every phase keeps `make verify` green,
   leaves frozen benchmark digests unchanged unless it deliberately re-freezes
   them in a dedicated commit, and preserves the information boundary. Every
   wave gate also runs the full official gates (see section 3).

## 2. Dependency graph

```mermaid
flowchart LR
  subgraph Engine
    E4a[E4a prompt keys + subject side]
    E3[E3 fuzzing]
    E1[E1 tiered Apply]
    E5[E5 determinization]
    E4b[E4b event completeness]
    E2[E2 validator split]
    E6[E6 naming + growth]
  end
  subgraph UI
    U0[U0 capture + Main split]
    U1[U1 quick wins]
    U2[U2 persistent board]
    U3s[U3 timeline: slice]
    U3[U3 timeline: full]
    U4[U4 layout redesign]
    U5[U5 art, feel, audio]
    U6[U6 onboarding]
  end
  subgraph AI
    A1[A1 rating ladder]
    A2[A2 heuristic fixes]
    A3[A3 turn planner]
    A4[A4 run search]
    A5[A5 weight tuning]
    A6[A6 difficulty]
  end
  E4a --> U1
  U0 --> U1
  U0 --> U2
  U2 --> U3s
  E4b --> U3s
  U3s --> U3
  U3 --> U4
  U4 --> U5
  U4 --> U6
  E3 --> E2
  A1 --> A2
  A2 --> A5
  E1 --> A3
  E5 --> A3
  A2 --> A3
  A3 --> A4
  A3 --> A6
  A4 --> A6
  E1 --> E5
```

The critical path is **U0 → U2 → U3 slice → U3 full → U4 → U5**. The AI track
has its own path, **A1 → A2 → (E1 + E5) → A3 → A4**, which can run alongside
the UI track in separate branches.

## 3. Waves

Sizes follow the plans: S is a few days, M is one to two weeks, L is several
weeks. The durations are rough ranges for one engineer switching between the
listed items, not commitments. Each wave ends at a gate; the next wave starts
once the gate passes.

Every wave gate includes the standard repository gates, run on the official
content snapshot, in addition to the wave-specific criteria below:

- `make verify`: formatting, tests, proofs, generated coverage and secret scan.
- `make m4-gate`: official client games in both seats and the resolution
  checks.
- `make m5-gate`: the full frozen headless suite. It covers the 1,000-game
  Standard and 100-game Easy self-play, the tactical suites, paired
  comparisons and worker-count determinism. `make m5-quick` is enough between
  gates, but not at one.
- `make m5-client-smoke`: the offline client flow, including save and resume.
- The M5 policy performance run (`ai-performance`) on the recorded reference
  hardware, whenever engine or policy code changed in the wave.

On a slower machine, run the gates with `NETRUNNER_TIMEOUT_SCALE` set (see the
README). It stretches only "give up after" limits, so a pass means the same
thing; performance budgets are still judged on the reference hardware.

If a wave deliberately changes frozen results, re-freeze the affected
manifests and fixtures in a dedicated commit, with the reason and the old and
new digests recorded in the wave's progress doc. Never regenerate held-out
seeds to hide an unfavorable result.

| Wave | Items | Rough length | Theme |
|---|---|---:|---|
| 0 | U0, E4a, E3 (CI part), A1 | 3–5 weeks | Tooling, safety nets and measurement |
| 1 | U1, E1, A2, A6a, E3 (nightly), E6 (renames) | 3–5 weeks | First visible wins; the engine gets fast; distinct difficulty |
| 2 | U2, E4b, U3 slice, E5 | 5–7 weeks | The board comes alive for one run and one score |
| 3 | U3 full, U4, E2 | 7–10 weeks | Real game feel |
| 4 | C1, U7, U5, U6, A1b, A4a, A5, A3, A4 | 8–11 weeks | Real decks; polish, and the bot plans turns and searches runs |
| 5 | A6 (compute tiers, personalities), hardening, decision point | 2–4 weeks | Difficulty, release readiness |

In total that's roughly 28–42 weeks. The UI track alone reaches "feels like a
game" at the end of Wave 3.

### Wave 0 — tooling, safety nets and measurement

| Item | Why now |
|---|---|
| U0 capture harness | Every later UI change needs before/after frames. Today's evidence had to be captured by hand. |
| U0 `Main` split | U1–U4 all edit the client; about 6K lines of smoke and gate code in `Main` makes that slow and risky. No behavior change. |
| E4a prompt keys and subject side | Small, and it removes the need for stopgap fixes in U1 (history labels, rules citations in the dock). |
| E3 random-legal fuzzing (CI part) | A cheap safety net before E1, E2 and U2 touch engine internals and rendering. |
| A1 rating ladder and human corpus | Nothing in the AI track can be judged without it. It needs no engine work. |

**Gate** (in addition to the standard gates above):
- The capture harness produces contact sheets for the run and score scenarios.
- The `Main` split changes no behavior: `make m4-gate`, `make event-smoke` and
  `make m5-client-smoke` pass unchanged.
- The dock shows no rules citations or step names, and the history side labels
  are correct.
- `make m5-gate` passes. If E4a adds serialized fields to Core events or
  prompts, the frozen replay hashes and deterministic digests change and must
  be re-frozen in a dedicated commit, with game outcomes, action sequences and
  tactical results otherwise identical to the M5 evidence. (Wave 0 derived both
  in `Netrunner.Match` instead, so the frozen results must be unchanged.)
- 50 fuzz games run in CI.
- The ladder separates Standard, Easy and random-legal per seat.

### Wave 1 — first visible wins; the engine gets fast

| Item | Why now |
|---|---|
| U1 quick wins | Visible improvements within weeks: correct history, player language, a visible score pile, a compact preview, and fixed z-order. |
| E1 tiered `Apply` | It unblocks A3 and E5, and the fuzzing from Wave 0 protects the refactor. |
| A2 heuristic fixes | Cheap strength gains (advancement inference, memory, breaker coverage, score race) measured on the A1 ladder. It also gives Wave 3's static evaluation its features. |
| A6a distinct difficulty | Wave 0 found Easy and Standard indistinguishable head to head (960 games). Make Easy measurably weaker with knobs that exist today (weights, the opponent-knowledge policy, a bounded rate of deliberately second-best choices), validated on the head-to-head ladder. Compute-based tiers wait for A3 in Wave 5. |
| E3 nightly fuzzing | Runs on official content once E1 exists, so large runs are affordable. |
| E6 renames | Low-risk cleanup while the affected files are being touched anyway. |

**Gate:**
- The U1 before/after contact sheets are reviewed.
- Simulation `Apply` is at least 5× faster than Authoritative, with the
  differential test green on the official corpus.
- A2 improves each side's ladder rating without lowering the other's.
- Head to head, Standard − Easy is separated (interval above zero) in both
  seats.
- A nightly run of at least 1,000 fuzz games is clean.

### Wave 2 — the board comes alive for one run and one score

| Item | Why now |
|---|---|
| U2 persistent board | It removes the bounce-back and early-arrival glitches, and U3 can't animate cards that are rebuilt on every update. |
| E4b event completeness | U3 needs an event, with payload, for every visible change. Scope it to the slice's events first. |
| U3 vertical slice | Only the advance, score, run start, encounter, subroutine fires and run end beats. This proves the timeline before building the whole catalog. |
| E5 determinization | Ready in time for A3 in Wave 3. It builds on E1. |

**Gate:**
- The captures show no duplicate or snap-back cards.
- A human-played Runner run and a Corp score are fully readable from the board
  alone, without the log.
- The presenter has zero snap mismatches on the slice's events.
- The sampler's self-consistency test (observation of the sample equals the
  input) passes, and so do the hidden-state twin tests.

### Wave 3 — real game feel

| Item | Why now |
|---|---|
| U3 full catalog | Extends the proven slice to every event in the catalog, and adds pacing and auto-pass settings. |
| U4 layout and interaction redesign | Needs U2 (persistent nodes) and U3 (overlays tied to beats). This is where the dock and the boxed columns go. |
| E2 validator split | By now E3 has built a corpus of known-bad states to prove authoritative commits still reject everything they did. Bot search already avoids validation through Simulation mode (E1), so this is about keeping authoritative play fast and the validator from becoming a second rules engine. |

**Gate:**
- A full game in each seat can be played without opening "All legal
  actions…".
- The capture review at 1920 and 2560 passes.
- Authoritative `Apply` rejects everything it did on the known-bad corpus, and
  validator time on the benchmark profile is measurably lower.

### Wave 4 — polish, and the bot plans turns and searches runs

| Item | Why now |
|---|---|
| C1 Gateway sample decks | First: the seven System Gateway-only sample decks from Null Signal Games use only cards we already implement, and tuning the bots further on the tutorial decks would fit them to two teaching decks. Every later bot item is measured on these decks. See below. |
| U7 main menu | With C1: the start screen is a developer scaffold (seed box, preset drop-downs, a metadata gallery button). C1's Tutorial and Gateway areas need a real menu to live in. See below. |
| A1b ladder over a deck pool | With C1: a fixed game budget sampled over deck pairings, sequential stopping, and per-deck warnings, so ladder time stays flat as decks are added ([plan](AI_OPPONENT_PLAN.md#a1b-ladder-over-a-deck-pool-m--with-c1-in-wave-4)). |
| U5 art direction, feel and audio | It only pays off once the layout (U4) is stable. |
| U6 onboarding and playtest loop | It needs the final interaction model from U4. |
| A4a whole-run model | First: per-server cost to get in and potential are the evaluation terms A3 lacked, and A4 builds on the same model. Inspired by chiriboga's run calculator (clean-room; it is GPL-3.0). |
| A5 fitted evaluation | Fit the evaluation to self-play outcomes over the A4a and A2 features, instead of hand-tuning. |
| A3 turn planner | Moved from Wave 3 ([record](WAVE_3_PROGRESS.md#a3-turn-planner)): the planning infrastructure works, but its end-of-turn evaluation didn't pick better turns. It returns on A4a and A5. |
| A4 run search | It builds on the A3 infrastructure and the A4a model, and adds the most for the Runner. |

**C1 Gateway sample decks (M).** Null Signal Games publishes
[sample decklists](https://nullsignal.games/players/getting-started-sample-decklists/);
seven are built from System Gateway alone:

| Side | Deck | Identity |
|---|---|---|
| Runner | Party Hard | René "Loup" Arcemont: Party Animal (Anarch) |
| Runner | Stolen Goods | Zahya Sadeghi: Versatile Smuggler (Criminal) |
| Runner | Planning Ahead | Tāo Salonga: Telepresence Magician (Shaper) |
| Corp | Discretion Advised | Haas-Bioroid: Precision Design |
| Corp | Advanced Yomi | Jinteki: Restoring Humanity |
| Corp | Hyper Velocity | NBN: Reality Plus |
| Corp | Quick and Dirty | Weyland Consortium: Built to Last |

All 77 Gateway cards, including these identities, are implemented. The
other 23 lists on that page need Elevation cards, so they wait for the next
card set.

1. **Data.** Commit the lists as card codes next to the existing presets
   (the card data stays local-only), under a new preset kind, `gateway`,
   alongside `starter` and `expanded`. `OfficialPresetCatalog` validates each
   list against the Gateway card counts and deck-building rules (minimum
   size, influence, agenda points).
2. **Deck choice in the menu (built in U7).** Two areas. **Tutorial**: the Catalyst and Syndicate
   starter and expanded decks, as today, for learning the game. **Gateway**:
   pick your seat's sample deck and the opponent's (or random), with each
   deck's identity, faction and a one-line description. The CLI, the saves
   and the ladder take the same deck IDs. Older saves keep their preset.
3. **Rules hardening.** The fuzz corpus covers every card, but not whole
   games of real faction decks: Jinteki net damage, NBN tag pressure, Tāo's
   ICE swaps, Loup's trash economy. Run the official fuzz and headless full
   games over all twelve pairings with zero invariant failures, caps or snap
   mismatches before a playtest.
4. **First bot audit.** Some policy logic is card-specific and was only
   tuned in Catalyst against Syndicate games. Play each faction, list the
   worst misplays, and fix them as policy repairs (new policy versions, as
   with Corp v6 and Runner v4). The deeper work (A4a, A5, A3) then happens
   on the deck pool.
5. **Benchmarks stay frozen.** The tutorial decks remain the frozen
   `make m5-gate` suite and its anchors, so existing baselines stay
   comparable. The ladder moves to the Gateway pool (A1b).

**U7 main menu (M).** Replace the setup scaffold with a game menu:

- **Title screen**: the game's name over a board backdrop, with **Continue**
  (the latest save, one click back into the game), **New game**, **Load
  game** (the save browser), **Settings** and **Quit**.
- **New game** leads to **Tutorial** (the Catalyst and Syndicate decks, with
  a line on what each teaches) or **Gateway** (C1's sample decks). Pick a
  seat, your deck and the opponent's (or random) as cards showing identity
  art, faction and a one-line pitch, and the opponent's difficulty. Then
  **Start**.
- **Settings** is the existing in-game menu (speed, fast opponent,
  auto-pass, reduced motion, accessibility), reachable before a game too.
- **Developer tools** (the seed, the metadata gallery, the interaction lab,
  session exports) move behind a Developer entry that release builds hide;
  a seed stays available there for reproducing a game.
- Missing official content gets one clear screen that explains how to
  provision it, instead of disabled drop-downs.
- Keyboard and controller focus order, and a layout that works at 1920 and
  2560, checked by the existing capture and accessibility smokes.

The saves, CLI and smokes keep their launch arguments; only the human path
through the menu changes. U7 gate: a new player reaches a Tutorial game, a
Gateway game and a resumed save without the developer entry, and the
capture review at both resolutions passes.

C1 gate: every Gateway pairing plays through the official fuzz and headless
full games cleanly; the main menu offers both areas and a human plays one
Gateway game in each seat; the per-faction audit's fixes don't regress the
ladder.

**Gate:**
- The M5 frame-time gate still passes with the new effects and audio.
- Playtest metrics (time per turn, rejected drops, "All legal actions…" use)
  improve against a Wave 0 recording.
- A4a's run model matches the engine: its predicted cost and outcome of a
  run agree with the run played out in Simulation mode on a corpus of
  positions.
- A3 shows a clear ladder gain in both seats within the latency budget.
- A4 and A5 show ladder gains, and hidden-state twins still pass.

### Wave 5 — difficulty, hardening and the next decision

- A6 compute-based difficulty tiers and personalities, validated by the ladder
  and a human playtest. (A6a, a first distinct Easy, moved to Wave 1.)
- A hardening pass: 10,000 clean official fuzz games (the engine plan's gate
  before any new card set), the full `make m5-gate`, and packaging.
- **Decision point**, recorded in `IMPLEMENTATION_PLAN.md`:
  - M6 (LLM/hybrid mode), or
  - the next card set, which requires the E6 Core-growth budget to be set, or
  - an RC.

## 4. Where M6 fits

M6 (safe LLM/hybrid mode) is independent of most of this work, but it gets
much easier after two items:
- E4a: typed prompts are what an LLM intent layer would consume.
- A1: the rating ladder is how hybrid play would be evaluated.

The recommendation is to start M6 after Wave 3. It could also run as a
parallel lane from Wave 1 if LLM play is a higher priority than feel. That's
a product decision to record in `IMPLEMENTATION_PLAN.md`.

## 5. Working agreements

- **One feature branch per plan item**, following `AGENTS.md` (for example
  `feat/u2-persistent-board` or `feat/a1-rating-ladder`). Each item gets a PR
  into `dev`.
- **Progress records** follow the existing convention: one progress doc per
  wave (`docs/WAVE_0_PROGRESS.md`, ...) with gate evidence, linked from this
  roadmap. Code is named after domain concepts, not after waves.
- **Evidence:**
  - UI PRs attach capture contact sheets. Screenshots with official card art
    are shared privately; committed evidence uses the synthetic lab fixtures.
  - AI PRs attach ladder reports.
  - Engine PRs attach the differential and performance reports described in
    the engine plan.
- **Re-plan at each gate.** If a gate fails, fix it within the wave rather than
  carrying the failure forward. If a wave's scope clearly needs to change,
  update this document in the same PR as the gate evidence.
