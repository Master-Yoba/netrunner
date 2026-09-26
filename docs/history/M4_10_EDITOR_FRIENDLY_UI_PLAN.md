# Slice 4.10 — Editor-friendly Godot UI

Status: implemented; independent user acceptance pending. See [the progress record](M4_10_UI_PROGRESS.md).

Date: 8 September 2026

Prerequisites: integrate the current M4.9 card table and its priority, hover and
glow follow-ups. This slice sits before M5. It closes the reusable-scene gap in
the [M4.9 architecture plan](M4_9_UI_PLAN.md#7-architecture-and-ownership) and
adds explicit acceptance criteria for visual editing. It does not declare the
remaining M4.9 manual usability gate passed.

## 1. Outcome

A developer or designer should be able to open a representative board in the
Godot editor, select a card or region, adjust its appearance in the Inspector,
and see the result immediately. Saving the scene or style resource must retain
the result across editor restarts and apply it to the game.

The first acceptance exercise is the request that prompted this slice:
adjust card glow width, opacity and fade while viewing representative cards,
without editing C# or rebuilding between value changes.

An initial C# build is expected when adding or changing script types. Subsequent
appearance edits must use scene/resource values and require no C# build.

## 2. Pre-slice implementation and gap

- `Main.tscn` contains a root Control with `Main.cs` and `M4Theme.tres` attached.
  Most visible children are created by C# builder methods at runtime.
- `Main.CardTable.cs` and `GameTableControl.cs` build the table, lanes, hands,
  dock and supporting panels. History and hover components also construct
  their UI in code.
- `TableCardControl.cs` draws card faces and glows using hardcoded sizes,
  colors and drawing parameters. Recreating styles in drawing code prevents
  direct editing of those styles as persistent resources.
- `M4Theme.tres` provides a useful foundation, but local code overrides many
  colors, fonts, margins and borders.
- The current scene scripts expose no `[Export]` appearance settings or
  `[Tool]` previews. A runtime Inspector change is not a persistent authoring
  workflow for this code-generated UI.

Use the existing behavior and screenshots as the migration baseline. Avoid a
simultaneous visual redesign that would obscure interaction regressions.

## 3. Scope

Included:

- Editor-authored layouts for the production setup screen, game board, cards,
  server lanes, player areas, hand/piles, history, action dock, access display,
  hover preview, inspection overlay and outcome presentation.
- Shared, Inspector-editable theme and appearance resources.
- Safe editor previews using authored sample presentation data, including a
  dense board and representative decisions.
- Live appearance tuning in the editor and temporary tuning in a running game.
- A documented persistent save/reset workflow and automated/native regressions.
- Removal of superseded production layout builders and conflicting visual
  overrides as each replacement is integrated.

Excluded:

- Rules, card behaviors, agent policy, action IDs or replay-format changes.
- New drag-and-drop behavior or a new public Corp-hand fan. That remains in the
  [Corp hand presentation plan](CORP_HAND_PRESENTATION_PLAN.md); migrate its
  existing identity/count and local playable hand without expanding it here.
- New artwork acquisition, an in-game theme editor, a general modding system,
  mobile layouts, or a renderer/shader rewrite solely to enable editing.
- Complete conversion of legacy diagnostic and milestone-specific smoke UIs.
  They remain available; shared components must still work in those callers.

## 4. Authoring workflows

| Workflow | Required behavior | Persistence |
| --- | --- | --- |
| Edit a component scene | Open its `.tscn`, select named nodes, change layout/theme settings and see sample content | Save scene or referenced resource |
| Tune shared appearance | Open the shared style resource with the preview board visible; all preview instances update | Save the `.tres`; reopen and launch to verify |
| Tune a running match | Launch from the editor, select a live card/shared style in the Remote Inspector and change exposed values | Temporary by default; record/copy chosen values into the saved Local resource |
| Reset an experiment | Undo Local edits or reload the saved resource; restart the game to discard Remote-only edits | The saved resource is the baseline |

Document which Inspector is being used. Do not imply that Remote edits are
saved automatically or promise that editing a Local resource automatically
updates an already-running process. Prove each supported path on the pinned
Godot .NET version. The persistent editor-preview workflow is the primary one;
a custom synchronization plugin is not a prerequisite.

Use Godot's documented [C# exports](https://docs.godotengine.org/en/4.7/tutorials/scripting/c_sharp/c_sharp_exports.html),
[editor tool scripts](https://docs.godotengine.org/en/stable/tutorials/plugins/running_code_in_the_editor.html)
and [runtime inspection](https://docs.godotengine.org/en/stable/tutorials/editor/game_embedding.html)
as the implementation references, checking their behavior in the installed
editor during the first checkpoint.

## 5. Architecture

### Scenes own composition; C# owns binding and behavior

Suggested scene boundaries (filenames may change with documented reasons):

| Scene | Responsibility |
| --- | --- |
| `Main.tscn` | Visible setup/game/overlay roots and composition |
| `MatchSetup.tscn` | Seat, preset, seed and start controls |
| `GameBoard.tscn` | Board regions, layout containers and overflow boundaries |
| `CardView.tscn` | Face/back, labels, counters and visual state layers |
| `ServerLane.tscn` | Header, root, ICE and run marker slots |
| `PlayerArea.tscn`, `HandView.tscn`, `PileView.tscn` | Identity/resources, existing hand and pile representations |
| `MatchHistoryPanel.tscn`, `HistoryEntry.tscn` | Collapsed/expanded rail, entries, pagination and controls |
| `DecisionDock.tscn`, `ActionButton.tscn` | Prompt, staged targets, legal choices and confirmation |
| `CardHoverPreview.tscn`, `CardInspection.tscn` | Nonmodal hover and explicit detailed inspection |
| `AccessPanel.tscn`, `OutcomePanel.tscn` | Accessed card and end-of-match presentation |

Instantiate variable numbers of cards, lanes, history entries and action
buttons from PackedScenes. Runtime creation of instances remains appropriate;
runtime reconstruction of their fixed internal layout does not.

Use named/unique node references or exported references with clear validation.
Avoid fragile child-index lookups. Preserve stable card controls, focus and
selection when only presentation values change. `Main` retains navigation,
coordinator hookup and snapshot routing; extracted components own their view
binding and interaction signals.

### Resources own appearance

Keep `M4Theme.tres` as the common theme. Use named theme type variations for
card labels, lane headings, primary/secondary actions and history controls.
Store editable StyleBoxes, fonts, margins and colors in resources instead of
reapplying constants from C# on every render.

Introduce focused custom Resources where Theme items are insufficient, such
as `CardAppearance.tres` and `BoardLayout.tres`. Expose descriptive properties
with units, range hints and Inspector groups:

- Cards: portrait/ICE sizes, corner radius, label spacing, counter styles,
  face/back presentation and preview sizing.
- Glows: outer extent, inner extent, opacity, falloff/softness and state colors.
  Preserve white actionable, blue selected and cyan target distinctions.
- Board: lane width, region spacing, hand overlap, card separation and margins.
- History/dock: rail and expanded widths, text styles, row spacing and focus cues.

Maintain one authoritative default for each value. Per-instance visual states
must not mutate shared defaults: selecting one card cannot recolor every card.
Document intentional local overrides. Cosmetic changes never feed back into
engine decisions, timers or game state.

For custom C# Resource properties, explicitly emit change notifications from
setters where required. Controls subscribe to appearance changes, update their
minimum size/layout or request redraw, and disconnect when replaced or freed.
Do not assume a C# property change will automatically trigger a custom redraw.

Retain custom drawing only where it serves the card renderer. All meaningful
visual parameters of that drawing must come from exported resources. Glows
must fade visibly without enlarging hit areas or escaping board clipping.

### Editor preview is presentation-only

Use narrowly scoped `[Tool]` view components or a dedicated preview host.
Opening a scene in the editor must never create a MatchCoordinator, drive an
agent, submit an action, write session traces, or access authoritative saves.
Guard runtime composition with `Engine.IsEditorHint()` where needed; do not
mark the existing gameplay composition as a tool script wholesale.

Create `UiPreview.tscn` with explicit sample resources or a Godot-only adapter
that produces the same filtered presentation models consumed at runtime.
Preview properties select viewer seat, layout size and sample state. Samples
are opt-in preview inputs, never fallback production game data.

Minimum sample set:

- Known card, anonymous back for each side, portrait and sideways ICE.
- Normal, actionable, selected, target, hovered/focused and disabled states.
- Counters, hosted cards, long titles/rules and optional artwork absent.
- Empty/normal/dense board, several remotes and multiple ICE per lane.
- HQ identity/count, local hand overflow, scores and discard piles.
- Collapsed/expanded history, access, nested choices, outcome and paused recovery.

Use synthetic sample metadata so a clean checkout can preview the UI without
official card data or local art. The committed generic NSG backs remain usable
under their existing `card-backs/SOURCE.md` provenance.

## 6. Implementation checkpoints

Implement incrementally; keep the current game runnable after each checkpoint.
Do not maintain two divergent production card renderers through the whole slice.

| Checkpoint | Deliverable | Acceptance before continuing |
| --- | --- | --- |
| UI-4100 — Editor workflow proof | One reusable card scene, exported appearance Resource and safe preview; demonstrate glow tuning and saving | Initial build followed by live value edits with no rebuild; saved values survive reopen and are consumed by a launched card |
| UI-4101 — Shared visual foundation | Card scene becomes the runtime card; shared themes/styles, known/back states, counters and preview integration | Editor and runtime use the same resources; visibility, focus, hover and action cues match the baseline |
| UI-4102 — Board and zones | Board/lane/player/hand/pile scenes; scene-based dynamic instancing and stable node reconciliation | Dense preview and real games in both seats retain server order, HQ count, host/ICE structure and overflow |
| UI-4103 — Decisions and history | Scene-based action dock, history entries/panel, access, inspection and outcome components | Every issued action remains reachable; history retention, pass filtering and access visibility regressions pass |
| UI-4104 — Shell and cleanup | Setup/navigation scenes, thin Main composition, remaining shared styles migrated; remove replaced builders | Production UI opens meaningfully in the editor; no unexplained code override defeats a supported Inspector setting |
| UI-4105 — Editing and gameplay gate | Authoring guide, sample gallery, saved example change, native evidence and complete regression run | Manual authoring exercise and the final gate below pass |

UI-4100 establishes scene/resource contracts before parallel component work.
The architect owns resource lifetime, shared-style semantics, preview/runtime
separation and snapshot/interaction contracts. If implementation is delegated,
give bounded scene/component work to implementers with distinct file ownership;
review the first migrated component before extending the pattern. Do not have
several implementers restructure `Main` concurrently.

Use a dedicated feature branch, scoped checkpoint commits and PRs into `dev`.
Include screenshots of both the editor preview and the running game. Record
actual progress and unresolved items in `M4_10_UI_PROGRESS.md` when work starts.

## 7. Behavior that must survive migration

- Only the engine mutates game state; UI submissions retain issued IDs and
  decision revisions. No rule, RNG or replay schema changes for visual editing.
- Auto-pass remains limited to a sole legal Pass Priority action. Durable logs
  retain it; visible history omits chatter but retains effects.
- Known-card hover, keyboard inspection, white action cues, distinct targets,
  staged confirmation and stale-action rejection remain functional.
- Hidden cards never resolve definitions/artwork, retain a previous preview,
  or gain inspectable private identities through editor/runtime binding.
- Recovery remains paused until continued. Editor preview creates no logs.
- Archives → R&D → HQ → remotes; HQ identity plus count; existing playable hand.
- Skip/reduced motion, ordered publications, history retention and reading
  position, focus stability and legal-action accessibility remain intact.

## 8. Final acceptance gate

The slice is incomplete until all of the following are demonstrated:

1. **Usable scene tree:** open the production board and component scenes in the
   editor and see representative content. Select meaningful named controls;
   a blank root whose entire UI appears only after launching does not pass.
2. **No-code appearance tuning:** adjust glow extent/opacity/falloff, a card
   state color, a font size and lane/hand spacing through Inspector properties.
   The preview updates immediately without C# edits or repeated builds.
3. **Persistence and parity:** save those changes, close/reopen the editor, and
   launch the game. The intended settings persist and match the preview.
4. **Runtime tuning:** demonstrate a temporary Remote Inspector glow edit in a
   running match. The guide explains how to retain chosen values in the Local
   resource and verifies that restarting discards unsaved Remote-only changes.
5. **Safe shared styles:** editing a shared default updates multiple preview
   cards; selecting one instance does not alter the others or the saved style.
6. **Preview isolation:** repeatedly open/reload previews with no coordinator,
   agent, game-state mutation, trace creation, or retained signal/resource leak.
7. **Playable UI:** native checks and manual playtests in both seats at
   1920×1080 and 2560×1440 cover dense layouts, choices, runs, access, hover,
   unknown cards, keyboard navigation and recovery. Resizing does not change
   game state, corrupt focus or clip required controls.
8. **Regression evidence:** run relevant headless tests, Godot import, existing
   native UI/full-match checks, formatting verification and `make verify`.
   Keep generated coverage current; report any unavailable gate explicitly.
9. **User authoring exercise:** the user independently tunes and saves the glow
   using the written guide. Record the result and outstanding usability issues;
   automated screenshot success alone is insufficient.

Add focused tests for resource-change propagation, scene binding, preview
isolation and hidden-card clearing. Do not assert every node count or duplicate
each scene's layout in tests. Capture a small set of editor/runtime before/after
screenshots and check that scene migration does not introduce material frame-time
or allocation regressions against the recorded M4.9 baseline.

## 9. Deliverables and completion record

- Production reusable scenes and their C# binding components.
- Shared theme/style/layout resources and safe editor preview samples.
- `docs/UI_EDITOR_GUIDE.md`: initial build, preview selection, editable settings,
  Local versus Remote editing, saving/reverting and troubleshooting overrides.
- Slice progress/evidence record with verification commands, results and the
  manual authoring exercise.
- Updated roadmap and any explicitly retained diagnostic-only builders.

Do not mark the slice complete by adding Inspector sliders to the existing
runtime-only Main shell. The delivered outcome is an editor-friendly production
UI with a repeatable visual authoring workflow. M5 remains after this slice and
the outstanding M4.9 manual usability gate.
