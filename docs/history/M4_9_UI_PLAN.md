# M4.9 — Card-table UI and understandable match history

Status: accepted for implementation. See [M4.9 progress](M4_9_UI_PROGRESS.md) for implementation, verification, and the remaining manual acceptance gate.

Date: 5 September 2026

Baseline: M4 is merged into `dev` at `06f0e2b`. Its rules, coverage and integration
checks remain valid, but manual playtesting exposed a separate usability gap.
This pass sits between M4 and M5. Completing a game automatically is insufficient
acceptance evidence for the new UI.

## 1. Outcome and scope

A player should see a Netrunner table, understand what changed after each action,
and find their next move without interpreting engine terminology. Preserve the
existing rules engine and replace the current diagnostic-panel composition with
a spatial board, persistent action history and contextual decisions.

The visual reference is [drbo6/chiriboga](https://github.com/drbo6/chiriboga).
Borrow its emphasis on physical cards, board geography, card inspection and
compact history. Build an original Godot interface; this is not a browser port
or a pixel-for-pixel reproduction.

M4.9 includes:

- Corp servers with ordered ICE and roots; Runner rig; both identities, scores,
  hands, decks and discard piles represented as recognizable table zones.
- Consistent card faces and backs, counter badges, selection, inspection and
  optional locally provisioned artwork.
- Persistent, readable, viewer-safe action history grouped by turn and action.
- Coherent movement/reveal feedback, contextual actions, run guidance and
  deliberate timing-window prompts.
- Native visual review and manual games in addition to automated checks.

M5 retains opponent heuristics, difficulty, benchmarks and user save/resume.
In particular, the Corp replacing useful Nico Campaigns is an AI problem for M5;
M4.9 must make the replacement unmistakable, not silently change the policy to
make its UI test easier. Multiplayer, a general replay browser, drag-and-drop,
mobile layouts, 3D tables and release packaging are outside this pass.

## 2. Reference review and current findings

Reference revision inspected: `c863f5af74ceb4fa821a4c84d3f643efffacb7ba`
on Chiriboga's `dev` branch. Source review is supplemented by the user-provided ongoing-game screenshot
`chiriboga.png` (local-only). The screenshot is a visual
reference; its exact build/revision is not established.

| Reference source | Observed pattern | Application here |
|---|---|---|
| [Card renderer](https://github.com/drbo6/chiriboga/blob/c863f5af74ceb4fa821a4c84d3f643efffacb7ba/cardrenderer/cardrenderer.js) | Individual card sprites, inspection/zoom, selection cues and sideways installed ICE | Card-shaped controls with stable positions, readable inspection and oriented ICE |
| [Styles](https://github.com/drbo6/chiriboga/blob/c863f5af74ceb4fa821a4c84d3f643efffacb7ba/style.css) | Right-edge scrolling history, compact side-specific entries and expanded tooltips | A compact right-side history rail with a readable expandable panel and keyboard/click access |
| [Game shell](https://github.com/drbo6/chiriboga/blob/c863f5af74ceb4fa821a4c84d3f643efffacb7ba/engine.php) and [logging](https://github.com/drbo6/chiriboga/blob/c863f5af74ceb4fa821a4c84d3f643efffacb7ba/utility.js) | Separate board controls/history, entries containing action text, downloadable diagnostics | Separate board, decisions and history; export a filtered playtest report |

The implementation will use our own scenes, visual assets and presentation
contracts. Card-image sourcing is a separate content input, not a dependency on
Chiriboga's runtime or bundled assets.

Current repository findings:

- `Main.BoardRegion.cs` divides the match into text-heavy vertical panels. Cards,
  identities, timing diagnostics and actions compete for the same space. The
  body scrolls vertically instead of preserving a recognizable table.
- `TextCardControl.cs` is a useful metadata and accessibility foundation, but its
  expanding text layout does not establish a consistent card silhouette.
- `EventFeedRegionPresenter` retains eight lines, newest first. The display is a
  transient technical feed rather than a history a player can read back.
- `Main.EventFeedRegion.cs` appends history while animating individual events;
  `SkipAnimations()` clears pending batches. Skipping visual delay can therefore
  remove events from the displayed history.
- `Main.PollCoordinator()` renders snapshots immediately and separately queues
  their events. Board state can get ahead of its explanation.
- `RunnerEventVisibility` deliberately omits events containing hidden card IDs.
  A new public summary must describe an observable face-down install without
  recovering or exposing the suppressed card identity.

### Screenshot review — concrete visual direction

The user-provided screenshot remains local-only because it contains card artwork.

The supplied 2048×1080 frame shows the Runner's action phase with several Corp
server positions, face-up and face-down ICE, a hosted Runner card, installed
Runner cards and a fanned hand. Adopt these visible composition principles:

- The table is the dominant surface. Individual cards sit directly on it with
  generous space between groups; large enclosing information panels are absent.
- Corp cards occupy the upper field and Runner cards the lower field. Each
  server's root and sideways ICE share a lane, making protection spatially clear.
- Deck/discard piles are recognizable card stacks with count badges. Hands
  overlap at the table edges; the local hand is visible at the bottom.
- Hosted cards visibly overlap their host. Counters sit on cards rather than in
  detached paragraphs; local highlights mark cards that can be acted on.
- Player resources occupy compact edge strips. The current phase has one clear
  heading, and the action area uses very little vertical space.
- History is a narrow rail of side-coded action markers on the far right. The
  screenshot establishes its collapsed appearance, not how expanded history
  behaves; readable expansion remains our explicit interaction requirement.

Keep our own restrained palette, clear typography and upright inspection text.
The green scanlines, intense glow, upside-down Corp faces, tightly clipped hands
and tiny history labels are reference styling choices, not acceptance targets.
Give our rail text/tooltips and keyboard access, and keep every local hand card
reachable. Card artwork contributes strongly to the reference's appearance;
UI-490 must demonstrate a few legally provisioned image faces if available and
an equally structured metadata fallback. Plain rectangular text panels are not
a satisfactory fallback design.

This screenshot changes the initial layout proposal: use the compact history
rail by default, with an expandable/pinnable readable panel. Do not permanently
reserve 280–320 pixels for history at 1920×1080.

### Reproduction that anchors the redesign

Basic decks, seed `47042`, both opening hands kept, human Runner passing Corp
priority windows:

1. Corp installs Nico Campaign and rezzes it for 2 credits.
2. Corp replaces it with another Nico Campaign, then rezzes that for 2 credits.
3. Corp replaces the second Nico with a different face-down card.

The present UI makes steps 1 and 2 look nearly identical, then shows `UNKNOWN
SLOT`. Required result: the player sees each outgoing card leave for Archives,
each new installation arrive, and a history entry explaining the replacement.
The final card is described only as a face-down card from the Runner's view.

## 3. Board composition

Use fixed game geography initially: Corp at the top and Runner at the bottom for
both human seats. Ownership is explicit; the local player's hand is face-up and
the opponent's hand is a counted back stack. All readable labels face the user.
This preserves server/ICE orientation while avoiding a second mirrored layout.
Revisit table rotation only if the Corp-seat playtest identifies a real problem.

Proposed desktop wireframe, revised from the supplied screenshot. The narrow
right column is the collapsed history rail; expanded history is a separate state.

```text
┌──────────────────────────────────────────────────────────────┬───┐
│ Menu        Corp hand backs              Turn / priority      │ H │
│ Corp resources                                               │ I │
│                                                              │ S │
│ Archives      R&D       HQ / identity     Remote 1   Remote 2  │ T │
│ root/pile     pile      root             root       root      │ O │
│               ━ ICE ━   ━ ICE + host ━    ━ ICE ━             │ R │
│                         ━ ICE ━                              │ Y │
│                  Run marker follows the server lane          │   │
│                                                              │ • │
│       Runner programs / hardware / resources                 │ • │
│                                            Stack / Heap      │ • │
│ Runner resources              Local hand fan     Identity    │ • │
├──────────────────────────────────────────────────────────────┤   │
│ Prompt · contextual actions · targets · Back / Pass            │ ⇤ │
└──────────────────────────────────────────────────────────────┴───┘
```

Layout rules:

- Reserve only a 56–72 logical-pixel history rail by default. Each marker has
  an action icon, a side cue and accessible text; click or keyboard activation
  opens its entry in the readable history panel.
- Expand history to an initially 300–360 logical-pixel panel. At 1920×1080 and
  larger, allow pinning it beside the board with a deliberate layout adjustment;
  in narrower resized windows use a dismissible overlay that leaves the
  decision dock accessible.
  Preserve the selected entry and scroll position across collapse/expansion.
  Validate these dimensions in native mockups, not from screenshot ratios alone.
- Keep identities and compact resource counters close to their own table edge.
  Make credits, remaining clicks, agenda points, MU and tags immediately legible.
- Give HQ, R&D and Archives permanent labeled lanes. Give remote servers stable
  display names such as “Remote 1”; do not rename remaining servers when one
  disappears. Server IDs stay in diagnostics.
- Put ICE between the Runner and the server root, with the outermost ICE closest
  to the Runner. Render ICE sideways, with upright readable summary overlays.
  Draw the active run route through the actual lane.
- Render roots, upgrades and hosted cards as distinct stacks/attachments. Expose
  stack counts and a click-to-expand view; a host badge opens its attachments.
- Render the local hand as a compact horizontal spread with individually
  focusable cards. Use controlled overlap and hover/focus lift; all cards remain
  selectable without relying on tiny exposed edges.
- Keep the main board from scrolling vertically as one large document. Overflow
  is local: horizontal remote lanes, expandable ICE stacks and hand browsing.
  Opening a run or selecting an offscreen target brings that area into view.
- Keep the decision dock visible. Large choice sets use a dedicated chooser,
  not a tall action list that squeezes the table away.
- At 2560×1440 increase usable table space and card size within limits. Provide
  a UI-scale preference; do not merely stretch empty panels or shrink text.

## 4. Card presentation and interaction

Create one reusable card component with hand, board, ICE, pile and enlarged
variants. Preserve a portrait silhouette around 5:7 for ordinary cards. Small
cards show title, type/important value and state badges; full rules belong in
inspection. Target sizes and typography are measured in the native prototype.

Required states: face-down, unrezzed but visible to its owner, rezzed, selected,
legal target, encountered, hosted, advanced, counters/credits and once-per-turn
used. Use labels/icons/borders alongside color. A face-down card has a deliberate
card back and a label such as “Face-down card”; it must not resemble a failed
metadata load. Missing metadata for a legitimately visible card has a separate
fallback that never substitutes a concealed card's title.

Artwork policy for this pass:

- Support a local card-image resolver keyed by known definition code, plus
  original neutral backs and frames. Missing images use the same polished card
  frame with real metadata, not the old expanding text panel.
- Inspect locally available assets/provenance in the first slice and document
  the source/import procedure if artwork is enabled. Never resolve an image for
  a concealed identity. Do not fetch card images during a match.
- Board usability must pass with the fallback and with any provisioned images;
  downloading artwork is not a prerequisite for fixing the table and history.

Interaction contract:

| Input | Behavior |
|---|---|
| Hover or keyboard focus | Preview a visible card; do not commit an action |
| Click a card | Select it and show its currently legal contextual actions |
| Click a server/pile | Select the zone; show available run/install/browse actions |
| Click a contextual action | Stage targets if needed, otherwise submit its issued action ID |
| Click a highlighted target | Complete the staged choice using an engine-issued option |
| Back / Escape | Cancel local staging or close inspection; never undo an accepted action |
| Tab / Shift+Tab, Enter / Space | Traverse and activate the same controls available to the mouse |

Keep global actions in a compact dock: gain credit, draw, run, end turn and pass
appear when legal. Show costs before commitment. Retain an “All legal actions”
expandable fallback for uncommon abilities and automated coverage, but normal
play should not depend on it. On revision changes, clear stale staging and
explain why; never invent legal actions or retain stale IDs for convenience.

## 5. Persistent action history

History is match data in the presentation layer, not an animation side effect.
Append entries exactly once when a publication is accepted. Animation speed,
skip, scrolling, inspection and redraw must not alter the retained history.
Keep the whole in-session history; virtualize the visible rows. Clear it on new
match, while offering an explicit export before restart if the player wants one.

History entries should contain a stable entry key, turn, acting side, action
group, plain-language summary and optional expanded details. Accepted-action
sequence plus event ordering can deduplicate publications; opponent-thinking
publications do not create actions. Rejected submissions get separate feedback
and must not read as successful actions.

The rail and expanded panel share one history model. The rail summarizes actions;
the expanded panel provides chronological turns, newest entries at the bottom. Auto-follow
only while the user is at the bottom; otherwise retain their reading position
and show a new-entry count. Provide turn separators, collapse/expand, card-name
inspection where permitted, and copy/export of the filtered history with seed,
deck pair, human side and build version. This is a playtest report, not M5
save/resume or an export of authoritative hidden state.

### Required language

| Event or action | Example player-facing summary |
|---|---|
| Hidden Corp install | Corp installs a face-down card in Remote 1. |
| Public rez | Corp rezzes Nico Campaign, paying 2 credits. |
| Replacement | Corp trashes Nico Campaign from Remote 1 and installs a new face-down card there. |
| Replacement subsequently rezzed | Corp rezzes the new Nico Campaign, paying 2 credits. |
| Basic economy | Corp spends 1 click to gain 1 credit. |
| Draw | Corp draws 1 card. Never name an opponent's hidden draw. |
| Run | Runner spends 1 click to run Remote 1; next, approaches its outermost ICE. |
| Choice or damage | Name the visible effect, choice/result and public resource change. |
| Priority | Distinguish “Runner passes priority” from “Runner ends their turn.” |

These are templates, not permission to infer unobserved events. Distinguish a
known old card leaving from the unknown identity of its replacement. Do not
retroactively name a card in older log entries merely because it is revealed
later. Multiple same-title copies may receive temporary presentation labels
such as “new copy”; internal instance IDs stay out of ordinary text.

Group low-level movement, payment and counter events into one understandable
action with expandable details. Preserve separate installs, rezzes and paid
abilities, even when they occur in the same click window. Show “Click 2” only
when a click was actually spent; passing priority and rezzing do not consume a
click. Nested selections that cause no committed board change should not create
misleading duplicate “installed” messages.

### Visibility boundary

Do not build this by pretty-printing the existing event feed alone. Its filtered
events intentionally omit some hidden-card operations, and its final-state
visibility is not an adequate historical naming policy.

Add an explicit viewer-scoped action-summary projection in Match, at the reducer
publication boundary. It may inspect the accepted action and ordered transition
inside the trusted coordinator, but only emits allowlisted public facts and
identities visible to that viewer at the relevant event. Do not send the raw
accepted opponent action, hidden card IDs or authoritative state to Godot.
Use prior/post viewer observations and explicit reveal events to validate the
projection. Where attribution or ordering cannot be proven, emit a generic
observable summary or extend the typed projection; never guess a private name.

Historical inspection uses the metadata frozen into that entry. A remembered
revealed identity must not become a tracker for a card shuffled into a hidden
zone. Hidden board positions use scoped presentation handles only where their
physical continuity is observable. Retire handles on shuffle/private movement;
current nullable card slots are not permission to leak Core IDs into the UI.

## 6. Coherent board transitions and prompts

Replace the independent snapshot/event queues with presentation batches that
associate an accepted publication, its visible transitions and its log entries.
Core continues running on its existing worker boundary; no game rule depends on
animation completion or wall-clock delay.

- Log entries are retained immediately. Board transitions present the same
  ordered action group: payment, outgoing card, incoming card, reveal/counters.
- Reconcile persistent visual nodes by safe visible identity/slot continuity.
  Do not rebuild the entire board on every thinking or priority publication.
- A replacement animates the old card to Archives and a distinct card from HQ
  to the server. Revealing a new Nico cannot reuse the old Nico's visual identity.
- Keep transitions short, approximately 150–300 ms for meaningful movement;
  coalesce redundant resource updates without dropping history. Highlight the
  changed location briefly in reduced-motion mode instead of moving cards.
- When a human decision becomes available, settle to its matching board before
  enabling actions. Inspection and log scrolling stay responsive during playback.
- Skip settles to the latest safe publication and cancels visual delay only.
  Bound animation backlog; never force a player to watch minutes of obsolete
  movement before acting.
- Display whose choice it is, what they are choosing and what Pass will do.
  Show a compact paid-window prompt rather than engine enum names. Automatic
  passing or new stop preferences are deferred because they change interaction
  semantics and need a separate timing design.
- Encounter panels anchor to the encountered ICE, show subroutines/breaker costs,
  and keep the route visible. Access, discard and nested choices use focused
  overlays/docks with clear selected/remaining counts and a safe cancel path
  where the engine permits cancellation.

## 7. Architecture and ownership

| Area | Planned change | Constraint |
|---|---|---|
| `Netrunner.Core` | Normally unchanged; add a narrowly typed event only if a proven public transition cannot be projected correctly | Preserve deterministic state, issued actions, replay/schema contracts; document any unavoidable compatibility change |
| `Netrunner.Cards` | Existing metadata remains authoritative; optional asset manifest/import support if needed | No card behavior redesign or game-time network dependency |
| `Netrunner.Match` | Board projection, action history projection/store, presentation batches and safe target mapping | No Godot types; independently testable; strict viewer boundary |
| `Netrunner.Godot` | Composed board scenes, card/zone controls, history panel, decision dock and transition controller | Render projections and submit issued IDs only |
| Tests and scripts | Focused history/visibility tests, native board checks, full-game UI smoke | Preserve M4 rules/coverage/proof gates |

Suggested components (final filenames may follow implementation discoveries):
`BoardViewModel`, `MatchHistoryEntry`, `MatchHistoryProjector`, `PresentationBatch`,
`GameBoard.tscn`, `CardView.tscn`, `ServerLane.tscn`, `PlayerArea.tscn`,
`MatchHistoryPanel.tscn`, `DecisionDock.tscn`, and `BoardTransitionController`.

Use Godot Controls and reusable scenes for layout/hit testing. Avoid replacing
one large `Main` partial with another. `Main` becomes composition/navigation;
region components own rendering and focus. Reuse existing card metadata,
calculation presenters and decision contracts. Preserve the diagnostic view
behind a developer-only switch during migration, then make the new board the
default once its gate passes.

## 8. Implementation slices and review checkpoints

Estimates are planning ranges for one implementer with architectural review,
not commitments. Expect roughly 15–25 engineering days including integration;
revise after the first native prototype and visibility spike.

| Slice | Deliverable | Dependencies | Acceptance / review |
|---|---|---|---|
| UI-490 — Visual baseline (1–2 days) | Native static board prototype at both sizes and both seats; card face/back, zones, compact/expanded history and choice dock; supplied screenshot comparison | None | Review table geography, card readability and density before wiring behavior. Compare directly with the supplied screenshot; review collapsed and expanded history states. |
| UI-491 — History and safe transitions (3–5 days) | Typed viewer-safe summaries, retained history, deduplication, filtered report export; Nico regression | UI-490 for panel shape; projection work can start independently | Root reviews visibility and event ordering. Skip/fast mode cannot drop history; no hidden names/IDs leak. |
| UI-492 — Live table and cards (4–6 days) | Persistent card nodes, player zones, servers/ICE, hands/piles/hosts, inspection and overflow | UI-490; consume UI-491 contracts | Both seats usable at 1080p; installed cards remain spatially stable; metadata fallback and optional art path reviewed. |
| UI-493 — Decisions and runs (3–5 days) | Contextual actions, target staging, priority prompts, run path, nested/discard/access choices | UI-492 | Complete named interactions mouse-first and keyboard-only; every legal action remains reachable; stale actions safely rejected. |
| UI-494 — Motion and comprehension (2–4 days) | Ordered visual batches, replacements/reveals, speed/skip, reduced motion, log-to-board highlighting | UI-491–493 | Nico sequence understandable without developer tools; human decisions never appear against an obsolete board. |
| UI-495 — Playtest gate (2–3 days) | Real manual sessions, screenshots, defect fixes, regression/CI and progress record | All previous slices | Explicit pre-M5 usability gate below passes. |

Architect/reviewer owns the history visibility model, safe identity continuity,
publication/animation synchronization and final acceptance. If implementation
is delegated, give Luna Max bounded component tasks after these contracts are
fixed: static scene composition, card controls, history rendering, or decision
presentation. Each task includes fixtures and acceptance criteria. Keep Core
visibility changes and shared coordinator contracts under one owner; do not
parallelize edits to `Main` without a file-ownership split.

Use a feature branch per accepted slice, checkpoint commits, and PRs into `dev`.
Each visible-change PR includes native screenshots and verification evidence.
Do not start all components in parallel before UI-490 and the UI-491 contract
review; that would lock in conflicting board and history assumptions.

## 9. Verification and pre-M5 exit gate

### Focused automated checks

- History order and exactly-once ingestion across actions, thinking snapshots,
  rejection, batched effects, redraw, skip and reduced-motion playback.
- Nico replacement regression verifies two distinct known copies leaving and a
  final hidden installation; verify Runner and Corp histories separately.
- Visibility tests for hidden installs/draws, reveal/access followed by movement,
  rezzed replacement, derez, private search, hidden-zone shuffle and history
  inspection/export. Include negative assertions on forbidden IDs and titles.
- Projection tests for stable server display names, ICE order, hosted stacks,
  known/unknown card distinction and target mapping.
- Native Godot checks for mouse and keyboard actions, focus restoration,
  scrolling/overflow, stale selection, same-title card replacement and restart.
- Stress fixtures: at least six remotes, five ICE in one lane, ten-card local
  hand, twelve installed Runner cards, hosted cards, counters and long history.
  All cards/choices must remain reachable without covering the decision dock.
- Full existing `make m4-gate`, synthetic `make verify` CI, formatting, coverage
  drift and replay proofs. UI changes must not silently repin engine outcomes.
  Adapt UI smoke assertions to semantic behavior, not obsolete label literals.
- Retain the responsiveness gate; profile a dense board and 2,000 history rows
  on a named reference machine. Target 60 FPS during ordinary play, with no
  synchronous engine work or rebuilding the entire history on every frame.

### Manual acceptance — required, not replaced by smoke tests

Test at 1920×1080 and 2560×1440, with screenshots of each human seat. Complete a
basic game as Runner and as Corp, and exercise expanded-deck runs, hosted cards
and nested choices. Record seed, deck pair, build and observed problems.

The reviewer/player must be able to:

1. Identify both players' credits, remaining clicks and score immediately.
2. Locate HQ, R&D, Archives, a remote's root and its next ICE without guessing.
3. Play/install a card, select its destination, use a paid ability and cancel a
   staged action without consulting the raw legal-action listing.
4. Follow a run through encounter, break, access and resolution, and understand
   whose decision is pending.
5. Explain all three Corp clicks in the Nico opening from the board and history
   alone, including that the final card is a different, concealed installation.
6. Read back the previous turn while new history arrives without losing position.
7. Skip animation and still find every completed action in history.
8. Finish a game, understand the outcome and start another match.

Release blockers for this pass: hidden-information leaks, unreachable legal
choices, ambiguous card replacement, lost history, action controls behind
content, stale-board submissions, or a manual session requiring developer help
to identify ordinary actions. Capture remaining cosmetic issues separately.

Completion evidence belongs in `docs/M4_9_UI_PROGRESS.md` and
`docs/evidence/m4-9/`: before/after screenshots, named manual sessions, automated
results, performance evidence and any deferred items. Update the implementation
roadmap to place this gate before M5 when this plan is accepted for execution.
M5 starts after the manual usability review passes, not merely after CI turns
green.

## 10. First execution step

Build UI-490 as a native, noninteractive table using real Gateway metadata and
the Nico opening as a visual storyboard. Show normal board, replacement,
encounter and nested-choice states. Use the supplied ongoing-game screenshot (local-only `chiriboga.png`)
as the board-density and composition reference. An expanded-history screenshot
would further inform that interaction, but does not block the prototype. Review those concrete frames before expanding the live
implementation.
