# Editing the game UI in Godot

Use the shared production scenes to tune appearance without changing C#. See the
[progress record](M4_10_UI_PROGRESS.md) for completed checks and remaining gates.

## First launch

From the repository root:

```sh
source scripts/env.sh
make build
"$GODOT4" --editor --path src/Netrunner.Godot
```

Use the repository's pinned Godot .NET editor. C# script types need an initial
build before their exported properties appear. Changing appearance values after
that build does not require rebuilding C#.

For a real match with **F5** and Remote editing, launch the editor with the
local official content configuration (the same metadata used by `make play`):

```sh
source scripts/env.sh
export NetrunnerContentEnabled=true
export NetrunnerCardDataMode=official
export NetrunnerCardDataInputRoot="$PWD/data/vendor/system-gateway"
dotnet build src/Netrunner.Godot --no-restore
"$GODOT4" --editor --path src/Netrunner.Godot
```

Synthetic previews work without that metadata. If the editor was started with
the synthetic configuration, **F6** can still launch `UiPreview.tscn`; use
`make play` from a terminal to build and launch an official match with your
saved appearance values. Do not rebuild after each visual edit.

## Tune the glow and save it

Open `scenes/UiPreview.tscn` in the FileSystem dock. Select its root and set
**Page** to **Cards**, or open `scenes/components/CardViewPreview.tscn` for a
single card. Use the 2D workspace zoom controls to get a comfortable view.

The card scene and the game share `themes/CardAppearance.tres`.

1. Keep the preview cards visible in the 2D workspace.
2. Select `themes/CardAppearance.tres` in the FileSystem dock to inspect it.
3. Under **Action glow**, adjust outer extent, inner extent, opacity and falloff.
   Greater extent spreads the glow farther; opacity controls its strength;
   falloff changes how quickly it fades toward the outer edge.
4. Change an actionable or selected state color to check shared updates.
   All cards in that state should update together. Selecting an individual card
   in gameplay must not alter this resource.
5. Save the resource using the Inspector's resource menu. Saving only an
   unrelated scene is not a substitute for saving the edited `.tres`.
6. Reopen the scene and launch it to compare the saved result.

Use Undo to reverse a Local experiment. To return to the repository baseline,
review and discard only your intended appearance-resource changes with Git.
Keep any other work you want to retain.

## Local versus Remote

**Local** shows authored scenes and resources in the editor. Save these edits
to retain them across editor restarts and future game launches.

**Remote** shows nodes in a running process. While playing from the editor,
select a live card in the Remote scene tree and expand its **Appearance**
resource in the Inspector. Edits there are temporary. Copy the chosen values
into the Local `themes/CardAppearance.tres` and save that resource to keep them.
Restarting the game discards Remote-only values.

Editing the Local resource does not promise synchronization with an already
running process. The editor preview and the running game have separate
resource instances.

## Board and component editing

Select the `UiPreview` root to change **Viewer Seat**, **Density** (empty,
normal or dense), **Decision**, **History Expanded**, and **Show Hover Preview**.
The **Page** property also exposes setup, inspection and public pile examples.
These are authoring samples; their buttons do not submit game actions.

| What to change | Authoring location |
| --- | --- |
| Glow, state colors, card sizes and typography | `themes/CardAppearance.tres` |
| Lane width, hand overlap, card separation and board margins | `themes/BoardLayout.tres` |
| Common button/focus styles and heading variations | `themes/M4Theme.tres` |
| Fixed board regions | `scenes/components/GameBoard.tscn` |
| Lane, player area, local hand and pile composition | `ServerLane.tscn`, `PlayerArea.tscn`, `HandView.tscn`, `PileView.tscn` in `scenes/components` |
| History widths, row spacing, text and styles | Root of `scenes/components/MatchHistoryPanel.tscn` |
| Dock and action-button layout | `DecisionDock.tscn` and `ActionButton.tscn` |
| Hover dimensions and card magnification | Root of `CardHoverPreview.tscn` |
| Outer margins and setup/header composition | `scenes/Main.tscn` and `components/MatchSetup.tscn` |

Variable cards, server lanes and history rows are created from component scenes.
Edit the component scene to retain layout changes; do not save a synthetic
board's generated runtime instances as production content. The Cards page
provides authored sample instances for selecting cards in the Local tree.

The board resource owns its named geometry settings. Card appearance owns
custom drawing and typography; component scenes own their fixed label layouts.
The bottom status badge background is the **Status Badge Style** resource on
each card label layout root (`components/layouts/CardLabelLayout*.tscn`). It
renders beneath the inner glow; status text and its padding render above it.
History's exported text/style settings own its rows. These explicit authorities
prevent an arbitrary raw-node override from competing with the setting that
the game actually uses. Other component-specific StyleBoxes remain editable
in their scenes and intentionally override the common panel style.

## Scope and troubleshooting

- Preview samples are synthetic presentation data. They require neither
  official card metadata nor local card artwork, and do not run a match.
- Use scene nodes for layout and the shared resource for custom card drawing.
  A resource-owned property can override a raw node size; use its named setting.
- A selected or target card has an instance state; it does not change the
  shared default color. White indicates an actionable source, blue selection,
  and cyan a possible target.
- If custom properties are absent, build the Godot C# project and check the
  editor's Errors panel before reopening the scene.

## Independent authoring exercise

After the implementation checks pass, tune the glow without changing C#,
save the resource, reopen the preview, and launch a game. Record whether the
result persists and whether any step required help. This user exercise is a
required M4.10 gate; automated checks do not replace it.

## Practice drag interactions

Run `make interaction-lab`, or select **Practice board interactions** on the
setup screen. Choose a scenario and drag the practice card from hand. **Reset
scenario** restores the fixture immediately. The lab uses synthetic presentation
data and the production interaction controls; it is not a rules simulator or a
saved match. No official card metadata is required.

Tune `themes/DragAppearance.tres` for drag threshold, preview scale, return
duration, edge scrolling, and destination styles. Production scenes are
`BoardDragController.tscn`, `DropDestination.tscn`, and `BoardChoicePanel.tscn`.
Use the Remote Inspector for temporary live changes and the local resources for
persistent edits.

Dragged cards are centered on the pointer. `FollowSmoothing` controls how softly
they follow movement (default 0.04 seconds; higher is softer, zero is immediate).
Reduced motion also makes tracking immediate. Drop targets follow the pointer
directly even while the preview is catching up.
