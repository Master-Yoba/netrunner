# M4.10 — Editor-friendly UI progress

Status: implementation in progress on `feat/m4-10-editor-ui`.

Specification: [editor-friendly UI plan](M4_10_EDITOR_FRIENDLY_UI_PLAN.md).

## Checkpoints

| Checkpoint | Status | Evidence |
| --- | --- | --- |
| UI-4100: card editor workflow proof | Passed | Godot 4.7.2 editor: live width 16→24, opacity .65→.75, falloff 2→1.6, font 10→12; 6 assertions, save/reopen 2 assertions, launched card reports saved values and shared resource |
| UI-4101: shared visual foundation | Pending | Runtime and editor must use the same card scene |
| UI-4102: board and zones | Pending | Starts after review of the card contract |
| UI-4103: decisions and history | Pending | Scene migration and interaction regressions |
| UI-4104: shell and cleanup | Pending | Authored setup/composition and removal of replaced builders |
| UI-4105: editing and gameplay gate | Pending | Guide, preview, native evidence, regression gate and user exercise |

## Architecture and baseline

- Preserve M4.9 behavior and visual baseline at `dd33647`.
- Presentation scenes own fixed composition; dynamic rows/cards instantiate
  reusable scenes. Engine state, action IDs and replay formats are unchanged.
- Custom appearance resources notify consumers when edited. Instance selection
  never mutates shared defaults.
- Editor samples use synthetic presentation data and never start a coordinator.
- Legacy diagnostic and milestone smoke layouts may retain their builders;
  production composition is migrated separately.

## Outstanding acceptance

The complete gate is recorded in the plan. In particular, the user must
independently tune and save the glow using the delivered guide before this
slice can be marked complete. The M4.9 manual usability gate remains separate.

Initial Godot project build: zero warnings/errors. Card scene and preview
headless launches passed. The editor proof used the live exported-property
setters through a temporary local automation suite; no rebuild occurred between
edits and launch. Experimental values were restored to the existing baseline
after the proof. Full editor restart, Remote tuning and user exercise remain
part of the final gate.
