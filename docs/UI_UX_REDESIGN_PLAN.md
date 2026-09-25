# UI/UX redesign plan

Status: proposed, not started
Baseline: Godot client on `feat/m5-8-final-gate` at `1b5a58f`
Depends on: [engine plan](ENGINE_ARCHITECTURE_PLAN.md) phase E4 (prompt keys,
event subject side, event completeness) for U1 and U3
Related: [AI plan](AI_OPPONENT_PLAN.md) phase A6 (pacing belongs here)

## 1. Goal

Make the game feel like a card game on a table rather than a rules debugger.
Every state change should be visible, readable and attributable. Most
interaction should happen on the board. The existing contracts stay: the
engine never waits on an animation, clients submit only engine-issued action
IDs, and events reaching the client are already filtered per viewer
(`MatchCoordinator.cs:812`).

## 2. What a live session showed (25 September 2026)

This is from an official Basic vs Basic game, seed 42, playing Runner against
the Standard bot, on Godot 4.7.2 with GL Compatibility on Xvfb at 1920×1080,
with local card art. Frames were captured at 4–8 per second. The frames are
not committed because they contain official card art, which stays local-only.

**What works and should be kept:**
- Hovering a hand card lifts it.
- Dragging a card to play or install works, and legal drop zones light up.
- Choosing a run target is spatial: servers highlight with "Run X · 1 click".
- The card art is excellent, and the hover preview is readable.
- The developer controls are already in the cogwheel menu.

**The board doesn't show the game:**
- **The Corp scored invisibly.** On turn 2 the Corp advanced three times and
  scored Superconducting Hub. On screen, the remote's root card vanished and a
  small score counter went 0 → 1. No advancement counters appeared, nothing
  moved to a score area, and there was no emphasis at all.
- **Runs are invisible.** During a run on Remote 1 the only sign of the run was
  an orange "▶ Whitespace · 2 unbroken" line. When the subroutines resolved
  (lose 3 credits, end the run), the run simply stopped existing. Across 12
  captured frames nothing moved except text and one number.
- **Credits jump.** Sure Gamble took credits from 5 to 9 with no counting and
  no flight.

**Motion glitches from the before/after comparison approach**
(`GameTableControl.Motion.cs`):
- **Bounce-back on drop:** the dropped card snaps back into the hand while the
  action commits, then a ghost copy flies to the rig.
- **Destination appears early:** the installed card is already drawn in the
  rig while its ghost is still flying toward it and overlapping it.
- **Full board rebuilds:** `GameTableControl.RenderView` rebuilds the board
  whenever the board fingerprint changes (around lines 569–600), so card nodes
  never persist between updates.
- **The event queue only drives text:** `Main.EventFeedRegion.cs`,
  `AnimateEventsAsync`, updates a text ticker at 0.18 s per event, while the
  board has already jumped to the final state.

**Engine language reaches the player:**
- The dock showed "Run on Remote 1 · EncounterPaidWindow · Runner: CR 6.9.3b:
  encounter paid window."
- History shows "Corp: advance card." and "Corp: use trigger."
- Right after playing an event, a brief "use a paid ability or pass priority"
  prompt appears.

**Bugs:**
- **Wrong side on history rows.** Turn and step markers are tagged with the
  side whose action caused them, not the side they describe: "Corp turn 2
  begins" is tagged RUNNER, and "Runner ends runner action" is tagged CORP. The
  cause is that `MatchHistoryProjector` tags each entry with the `actor`.
- **The hand fan draws on top of the open history panel,** hiding its newest
  entries.

**Layout:**
- The board is boxed equal-width columns with scaffolding labels such as
  "ICE · inner → outer", "ROOT · remote content" and "Archives · 0 cards".
- About two-thirds of the Corp area is empty early in the game.
- ICE is small and landscape with cropped art, and subroutines are only
  visible on hover.
- Scored agendas are hidden behind a "Scored" button.
- The hover preview is a large panel that's mostly empty and covers the rest
  of the hand.
- The bottom-left decision dock competes with the hand for space.

**Pacing:** the Corp's turn 2 took about 9.4 s on screen for 16 actions
(from `ui-diagnostics.jsonl`), so there's enough time. The time just isn't
spent showing anything.

**Code structure:**
- `Main` is one partial class spread over 57 files and about 13.8K lines.
- About 6K of those lines are smoke and gate code: about 3.4K in
  `Main.*Smoke.cs` and about 2.6K in the milestone-named `Main.Slice43.cs` to
  `Main.Slice48.cs`.
- This makes iterating on feel slow and risky.

## 3. Design principles

1. **The board is the interface.** Act on cards and servers directly. Dialogs
   are only for genuinely abstract choices, such as picking a number or
   ordering triggers.
2. **Every change gets a beat.** Each visible change in the event stream gets a
   short, readable animation. Important beats (score, steal, flatline, run
   success) get emphasis and a moment to register.
3. **Show, then settle.** Animation plays from the filtered event stream. When
   a batch finishes, the board reconciles to the authoritative view model, and
   the authoritative view always wins.
4. **Player language.** No step names, rules citations or internal action
   names in the play screen. Those belong in a developer overlay.
5. **Continuity.** A card on screen is the same node until it leaves
   visibility. It moves, flips and fades rather than disappearing and
   reappearing.
6. **Respect settings.** Reduced motion, playback speed and skip keep working,
   and every beat must still be understandable without motion (for example a
   highlight or a log line).

## 4. Work breakdown

Sizes: S is a few days, M is one to two weeks, L is several weeks.

### U0. Tooling and structure (M) — enabling work

- **Capture harness.** Commit a script (`scripts/ui-capture.sh`) that starts
  the client on a virtual display, drives a scripted scenario, and records
  frame bursts plus a contact sheet into `artifacts/ui-capture/`. The session
  above used Xvfb, software OpenGL (llvmpipe) and XTEST input. Also add a
  deterministic in-engine mode, `--capture-script <file>`, that feeds actions
  through `MatchCoordinator` and saves viewport frames at named beats. That's
  more reliable than simulated mouse input, and it works on the Apple M5
  machine too.
- **Split `Main`.** Create a `MatchScreen` scene with separate controllers:
  board, hand, dock or prompt, history, overlays and session. Move all
  `*Smoke.cs` and `Main.Slice4x.cs` code into a separate validation scene or
  autoload that is only loaded by smoke command-line flags, and give the
  remaining files domain names. No behavior change; the existing smoke suite
  must pass unchanged.

Gate: `make godot-import`, `make event-smoke` and `make m4-gate` are unchanged,
and the capture harness produces a contact sheet for the run and score
scenarios.

### U1. Quick wins (S) — some parts need engine E4

- **History side labels.** Group history by the event's subject side (engine
  E4), not by the actor. As a stopgap before E4, derive the subject from the
  event payload in `MatchHistoryProjector` (`turn.ActiveSide`, `phase.Side`,
  `credits.Target` and so on).
- **Z-order.** When history is open, the hand fan goes below it or moves aside.
- **Player-facing prompts.** Map engine prompts to player text in
  `Netrunner.Match`. Before E4 lands, strip rules citations and step names from
  the dock and put them in a developer tooltip. For example, "Whitespace blocks
  your way — break subroutines or let them fire" instead of
  "EncounterPaidWindow · CR 6.9.3b".
- **Specific history lines.** Examples: "Corp advances a card in Remote 1 (2
  counters)" and "Corp scores Superconducting Hub (1 point)".
- **Scored agendas on the board.** Show a visible score pile next to each
  identity, with the real cards, instead of a "Scored" button.
- **Compact hover preview.** Show the card image plus only the text that isn't
  printed on the card (current strength, counters, hosted cards). It shouldn't
  cover the hand it's previewing.

Gate: before-and-after contact sheets for the history, encounter prompt and
scoring scenarios.

### U2. Persistent board (M) — fixes most motion glitches

- **A reconciler keyed by `CardInstanceId`,** plus stable keys for
  hidden-but-tracked cards the viewer is allowed to follow (for example "this
  face-down card in Remote 1"). Each render computes target slots and
  transforms. Existing nodes tween to their targets, new nodes enter from their
  source zone, and removed nodes exit toward their destination zone. Replaces
  rebuilding on fingerprint change and ghost copies.
- **Pending drop state.** On drop, the dragged card stays where it was dropped,
  dimmed, until the engine accepts. On accept it settles into its slot; on
  reject it animates back to the hand with the rejection shown next to it.
  Removes the bounce-back.
- **Counter and stat tweens.** Credits, clicks, memory, tags and advancement
  counters animate between values: counting numbers, pips filling and
  emptying, a counter dropping onto the card.
- **Layout.** Keep layout as a pure function from view model to slot
  rectangles, so it can be unit-tested headlessly in `Netrunner.Match` or a
  small Godot-free layout assembly.

Gate: frame captures of install, play, discard and advance show no duplicate
card and no snap-back, and the reduced-motion path still pulses.

### U3. Event timeline presenter (L) — needs engine E4; the heart of "feel"

Turn the filtered event batches into a timeline of beats that drives the
board, not a text ticker.

- **Beat model.** A `Beat` has a kind, subject nodes, a duration, an emphasis
  level and an optional "hold for input" flag. A `BeatPlanner` in
  `Netrunner.Match` maps event kinds to beats and is unit-tested headlessly.
  `Netrunner.Godot` only plays beats.
- **Intermediate display state.** Each beat applies its change to a client-side
  display model, starting from the previous authoritative view. At the end of
  a batch the reconciler (U2) settles to the new authoritative view. Any
  mismatch snaps and logs a diagnostic, so gaps in event completeness show up
  in development.
- **Beat catalog.** The first pass needs these:

| Event | Beat |
|---|---|
| Turn starts | Side banner, click pips refill |
| Draw | Card slides from the deck to the hand (face-down for the opponent) |
| Gain or lose credits | Credit tokens fly from source to pool; number counts |
| Install | Card travels to its slot (face-down for Corp installs) |
| Rez | 3D-style flip and a brief glow; cost tokens leave the Corp pool |
| Advance | Counter drops onto the card; running total shown |
| Score | Card lifts, reveals and flies to the score pile; points pulse; short hold |
| Run starts | Runner marker moves to the server; board focuses that server; other servers dim |
| Approach and encounter ICE | ICE grows in place with its subroutines listed beside it |
| Break subroutine | Strike-through on the subroutine, with the breaker highlighted |
| Subroutine fires | Subroutine pulses, then its effect beat (credits, damage, end run) |
| Access | Card reveals large at centre with its actions (U4) |
| Steal or trash | Card flies to the score pile or the discard pile |
| Damage | Grip cards flare and fly to the heap |
| Run ends | "Run successful" or "Run ended by Whitespace" banner; focus clears |

- **Pacing.** Opponent actions play at readable speed: base beat lengths of
  about 150–400 ms, multiplied by the speed setting, with holds on score,
  steal, flatline and run results. The bot keeps deciding instantly; the
  coordinator queues publications, and the presenter drains them in order. An
  optional "fast opponent" setting compresses low-emphasis beats. The
  existing skip button drains the queue to the latest state.
- **Auto-pass.** About 70% of all decisions are `PassPriority` (engine plan
  section 2). Add a setting to auto-pass paid-ability windows when the player
  has no relevant option, with a "stop" affordance that opens a window on
  request. Today's `AutoPassPriority` covers only the single-legal-action case.

Gate: the capture harness shows every catalog beat for a full scripted game in
both seats. Snap mismatches are zero on the official fuzz corpus (engine E3)
replayed through the presenter headlessly.

### U4. Board layout and interaction redesign (L)

Target layout at 1920×1080. It must also work at the existing compact and
2560 sizes.

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ [Corp ID]  ⬡12  ●●●○  ★ 2 [agenda][agenda]                     ⚙  ☰ log │
│                                                                          │
│      Archives        R&D           HQ          Remote 1      Remote 2    │
│      [discard]     [deck 28]    [3 cards]     [root][upg]    [root]      │
│       ─────         ─────        ─────         ─────          ─────      │
│      [ICE ]        [ICE ]       [ICE ]        [ICE  ]        [ICE ]      │
│                                 [ICE ]        [ICE  ]                    │
│                                                                          │
│   · · · · · · · · · · · · · · · · · · · · · · · · · · · · · · · · · ·    │
│   programs:  [Cleaver] [Carmen]           hardware: [..]  resources: [..]│
│ [Runner ID] ⬡4 ●●●● MU 1/4  tags 0  ★ 0                  [stack] [heap]  │
│                        ╭──────── hand fan ────────╮                      │
└────────────────────────┴──────────────────────────┴──────────────────────┘
```

- **No boxes or scaffolding labels.** Servers are defined by their cards and
  a subtle floor highlight. Server widths follow their content; the number of
  remotes drives horizontal spacing, and scrolling kicks in only past a
  threshold.
- **ICE is readable.** Use a larger landscape card frame that shows the full
  art and name. Subroutine chips are visible on rezzed ICE during runs. Strength
  shows as a badge.
- **Rig grouped by type.** Programs, hardware and resources in rows, with
  hosted cards fanned under their host.
- **A compact HUD** per side: identity card, credits, clicks as pips, memory,
  tags and a visible score pile. The status line at the top becomes a turn
  banner, not a log.
- **Contextual actions replace the dock:**
  - Clicking a card shows action chips next to it: Install, Play, Use, Rez,
    Advance, Trash.
  - Basic actions (credit, draw, run) are on the deck, the credit pool and the
    servers themselves.
  - The dock shrinks to a one-line instruction and "Pass" or "Done" button in
    the context that needs it.
  - "All legal actions…" stays as an accessibility and debug fallback behind a
    key or menu, not as a primary button.
- **Encounter overlay at the ICE.** Build on the lab's breaker panel: the ICE
  grows in place, with subroutine rows and breaker buttons that show exact cost
  quotes ("Break both · 2 credits"). Subroutines that will fire are marked
  before you commit.
- **Access overlay at centre.** A large card with Steal, Trash (cost) and
  Leave buttons, and a count of the remaining accesses ("2 of 3").
- **Accessibility.** Keep the existing keyboard focus order, non-color cues
  and reduced motion. Each new overlay must be reachable by keyboard and have
  screen-reader-friendly labels, as today.

Gate: manual playtest of a full game in each seat without opening "All legal
actions…". The capture contact sheets are reviewed for the run, encounter,
access and score scenes at 1920 and 2560.

### U5. Art direction, feel and audio (M, ongoing)

- **Visual identity.** Pick a palette, typography and surface treatment (a
  table material, and subtle neon for the Runner and corporate chrome for the
  Corp) instead of the default teal-on-dark panels. Record it as tokens in the
  existing theme resources (`themes/`) so it's editable in the Inspector, as
  M4.10 intended.
- **Motion vocabulary.** Ease-out for arrivals, a slight overshoot for
  emphasis, and a card flip shader for rez and reveal. The hover lift already
  exists; add a slight tilt toward the cursor. Brief screen shake and a red
  vignette for damage, scaled down by reduced motion.
- **Audio.** Card slide, flip, credit clink, click spend, ICE rez hum,
  subroutine fire, break, score and steal stingers, damage, and run
  success/failure. Include master, effects and music volumes and a mute
  option. Audio assets must be license-clean and recorded in
  `THIRD_PARTY_NOTICES.md`.
- **Idle life.** A subtle ambient animation on the attacked server during
  runs and on the active identity. Keep it cheap: the M5 frame-time gate
  (p95 3.05 ms against a 16.67 ms budget) must keep passing.

### U6. Onboarding and playtest loop (S → M)

- **First-game hints** (dismissable, off after the first game): how to run,
  how to break, how to advance.
- **Opponent intent cues,** optional: "Corp is advancing a card in Remote 1".
  These are derived from public events only.
- **Playtest protocol.** Use the existing `ui-diagnostics.jsonl` to measure
  time per turn, rejected drops and misclicks, how often "All legal actions…"
  is used, and skip usage. Review one recorded game per slice.

## 5. Sequencing

```text
U0 capture + Main split ──> everything
E4 (engine) ──> U1 quick wins ──> U3 timeline
U2 persistent board ──────────> U3 timeline ──> U4 layout ──> U5 feel/audio
U6 alongside U4/U5
```

Suggested first vertical slice: U0 capture, U1 history and prompt fixes, U2
persistent board, then U3 for exactly these beats: advance, score, run start,
encounter, subroutine fires, run end. Make one Runner-vs-bot run and one Corp
score feel right end to end, then widen.

## 6. Verification for every slice

- `make verify`, `make event-smoke` and, with official content,
  `make m4-gate`.
- Capture harness contact sheets before and after, attached to the PR per the
  repository guidelines. Screenshots with official art are shared privately,
  not committed. Committed evidence uses the synthetic lab fixtures.
- The frame-time gate from M5 still passes on the reference hardware.
- Reduced motion and speed settings are checked for every new beat.
