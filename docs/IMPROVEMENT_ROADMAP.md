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

If a wave deliberately changes frozen results, re-freeze the affected
manifests and fixtures in a dedicated commit, with the reason and the old and
new digests recorded in the wave's progress doc. Never regenerate held-out
seeds to hide an unfavorable result.

| Wave | Items | Rough length | Theme |
|---|---|---:|---|
| 0 | U0, E4a, E3 (CI part), A1 | 3–5 weeks | Tooling, safety nets and measurement |
| 1 | U1, E1, A2, E3 (nightly), E6 (renames) | 3–5 weeks | First visible wins; the engine gets fast |
| 2 | U2, E4b, U3 slice, E5 | 5–7 weeks | The board comes alive for one run and one score |
| 3 | U3 full, U4, A3, E2 | 7–10 weeks | Real game feel; the bot plans turns |
| 4 | U5, U6, A4, A5 | 6–9 weeks | Polish, and the bot searches runs |
| 5 | A6, hardening, decision point | 2–4 weeks | Difficulty, release readiness |

In total that's roughly 26–40 weeks. The UI track alone reaches "feels like a
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
- `make m5-gate` passes. Adding `SubjectSide` to events (E4a) changes the event
  hashes in accepted-action records, so the frozen replay hashes and
  deterministic digests are expected to change. They must be re-frozen in a
  dedicated commit, with game outcomes, action sequences and tactical results
  otherwise identical to the M5 evidence.
- 50 fuzz games run in CI.
- The ladder separates Standard, Easy and random-legal per seat.

### Wave 1 — first visible wins; the engine gets fast

| Item | Why now |
|---|---|
| U1 quick wins | Visible improvements within weeks: correct history, player language, a visible score pile, a compact preview, and fixed z-order. |
| E1 tiered `Apply` | It unblocks A3 and E5, and the fuzzing from Wave 0 protects the refactor. |
| A2 heuristic fixes | Cheap strength gains (advancement inference, memory, breaker coverage, score race) measured on the A1 ladder. It also gives Wave 3's static evaluation its features. |
| E3 nightly fuzzing | Runs on official content once E1 exists, so large runs are affordable. |
| E6 renames | Low-risk cleanup while the affected files are being touched anyway. |

**Gate:**
- The U1 before/after contact sheets are reviewed.
- Simulation `Apply` is at least 5× faster than Authoritative, with the
  differential test green on the official corpus.
- A2 improves each side's ladder rating without lowering the other's.
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

### Wave 3 — real game feel; the bot plans turns

| Item | Why now |
|---|---|
| U3 full catalog | Extends the proven slice to every event in the catalog, and adds pacing and auto-pass settings. |
| U4 layout and interaction redesign | Needs U2 (persistent nodes) and U3 (overlays tied to beats). This is where the dock and the boxed columns go. |
| A3 turn planner | E1 and E5 are ready, and A2 provides the evaluation features. It's the largest single strength gain. |
| E2 validator split | By now E3 has built a corpus of known-bad states to prove the split loses no detection. This is also when validation cost starts to matter in bot search. |

**Gate:**
- A full game in each seat can be played without opening "All legal
  actions…".
- The capture review at 1920 and 2560 passes.
- A3 shows a clear ladder gain in both seats within the latency budget.
- Validator share is under 5% of benchmark time, with no loss in fuzz
  detection.

### Wave 4 — polish, and the bot searches runs

| Item | Why now |
|---|---|
| U5 art direction, feel and audio | It only pays off once the layout (U4) is stable. |
| U6 onboarding and playtest loop | It needs the final interaction model from U4. |
| A4 run search | It builds on the A3 infrastructure, and adds the most for the Runner. |
| A5 weight tuning | The ladder is fast and the weights are named, so tuning won't be undone by later structural changes. |

**Gate:**
- The M5 frame-time gate still passes with the new effects and audio.
- Playtest metrics (time per turn, rejected drops, "All legal actions…" use)
  improve against a Wave 0 recording.
- A4 and A5 show ladder gains, and hidden-state twins still pass.

### Wave 5 — difficulty, hardening and the next decision

- A6 difficulty levels and personalities, validated by the ladder and a human
  playtest.
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
