# Slice 4.11 — Drag-to-play and install

Status: implementation in progress. This slice follows M4.10 and precedes M5.

## Outcome

Dragging a known local hand card becomes the primary play/install interaction.
Existing menu paths remain available. Unique valid drops execute immediately;
spatial follow-up choices use the board, while non-spatial choices use a compact
contextual panel. Run, encounter and access controls retain their current flow.

## Interaction contract

- Events and operations drop into general play space. Run events additionally
  accept legal server drops; a general drop asks for the server before submission.
- Runner installs drop into the rig or an engine-issued legal host. Virus subtype
  alone does not imply hosting. Corp installs use separate server-root and
  outermost ICE regions, or a New remote destination after existing servers.
- Preview follows the pointer, hand space is retained, hover enlargement is
  suppressed, destinations are highlighted, and destination/cost feedback follows
  issued actions. Invalid drops and Escape/right-click/focus loss cancel locally.
- Scrollable hand, rig and server regions support edge scrolling during dragging.
  Hit testing respects clipping and overlap. Normal clicks and menus still work.
- Immediate play/install resolution supports card/server/host choices, replacement
  and memory cleanup. Non-spatial/ambiguous alternatives have explicit controls.
  Never choose an arbitrary first action. Pile choices use viewer-filtered cards.
- Cancellation after an engine submission dismisses presentation only. It cannot
  roll back committed selection steps; pending decisions remain in the menus.

## Architecture

Match projects sources, destinations and candidates from viewer observations and
immutable issued action parameters. Godot handles gestures, geometry and authored
overlays. Submissions retain current decision ID/revision and action ID validation;
no Core state mutation, fabricated actions, new rule logic or replay schema changes.
Stale gestures are invalidated. Input is disabled during setup, opponent decisions,
submission, recovery pause and terminal states. Diagnostics record outcomes rather
than pointer movement. Inspector resources control visual and motion settings.

## Checkpoints and gate

1. Projection contracts and headless mapping/privacy regressions.
2. Dragging, destinations, scrolling, cancellation and submission integration.
3. Immediate board choices and backup menu integration.
4. Editor fixtures, native checks, evidence, full M4 gate and PR into dev.

Validate both seats at 1920×1080 and 2560×1440, empty and dense boards, hosted
installs, new/existing servers, replacement/memory choices, stale/duplicate input,
focus loss, privacy, and equivalence with menu submissions and recovered traces.
Capture screenshots and a short drag demonstration. Preserve prior manual gate
statuses; automated smoke evidence is not a claim of independent user acceptance.

Desktop mouse is the initial target. Installed-card rearrangement, touch, dragging
paid abilities, and combat/access redesign are excluded.

## Interaction lab

User-requested addition: a setup entry and `make interaction-lab` launcher expose
13 resettable synthetic scenarios using the production drag and board-choice
controls. Fixtures test UI behavior without starting a match or requiring official
metadata. Keep them visibly distinct from engine-backed play and recovery tests.
