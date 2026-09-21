# Board actions and targeting

Card actions now live beside their source. Selecting a card opens its legal
controls; selecting targets stages a local choice. The named source-only action
or final confirmation submits an engine-issued action ID. Escape clears only
unsubmitted selections. Mandatory decisions remain available until resolved.

Run, Purge virus counters, Remove tag, turn/run progression, numeric choices,
and optional reaction prompts remain buttons. Existing play/install/discard
and icebreaker gestures retain their behavior.

## Action inventory

| Actions | Surface | Status |
| --- | --- | --- |
| PlayCard, InstallCard | Hand drag and destination/installation controls | Existing; install follow-ups now work without drag tracking and during runs |
| GainCredit, DrawCard | Credit pool and deck | Existing |
| ChooseCard during TurnDiscard | Hand-to-pile drag | Existing |
| PumpBreaker, BreakSubroutines | Matching breaker controls and exact ICE rows | Existing |
| RezCard, RezIce, AdvanceCard, ScoreAgenda | Source card controls with prices | Migrated |
| UseAbility | Source (including identities and accessible piles), effect targets, separate additional card costs, confirmation | Migrated |
| TrashTaggedResource | Eligible resource and priced trash control | Migrated |
| ChooseCard, TrashProgram | Highlighted forced targets and confirmation | Migrated |
| ChooseBehavior with physical cards | Board or authorized candidate browser, ordered/set selection, confirmation | Migrated |
| ResolveIceSubroutine with card targets | Trash target or install card plus exact destination | Migrated |
| UseBioroidAbility | Encountered ICE, specific subroutine, click-cost confirmation | Migrated |
| ChooseBreachCandidate | Installed card/back, or HQ/R&D heading for aggregate access | Migrated |
| TrashAccessedCard | Accessed-card controls; one hosted-first payment | Migrated |
| StartRun | Run button and existing server picker | Intentionally retained |
| PurgeVirusCounters, RemoveTag | Global buttons | Intentionally retained |
| PassPriority, EndTurn, JackOut, ContinueRun, NoAccessAction | Progression buttons | Intentionally retained |
| ChooseNumber, abstract ChooseBehavior, non-spatial ResolveIceSubroutine | Decision buttons | Intentionally retained |
| UseTrigger, DeclineTrigger, OrderTriggers, UsePrevention, DeclinePrevention, UseReplacement, DeclineReplacement | Decision buttons; visible source highlighting | Intentionally retained |
| Setup keep/mulligan | Setup buttons | Intentionally retained |
| AdvanceAgenda, StealAgenda, Legacy | Compatibility contracts; no new manual action | Not migrated |

## Architecture and privacy

`BoardInteractionProjector` exposes `SpatialActions` alongside existing drag
surfaces. `BoardSpatialActions` projects only current typed issued actions.
`BoardSpatialSelection` distinguishes ordered effect targets from separate card
costs; it never modifies authoritative state. Labels come from the shared action
presenter. The fallback action list uses the same hosted-credit preference and
retains explicit target confirmation.

`Observation.BoardContext` is a nonserialized presentation sidecar, like the
existing decision/source metadata. It contains installed position handles,
viewer-authorized card views, and current selection cardinality/provenance.
Installed backs expose positions without definitions. Search candidates are
sorted by stable ID in the presentation metadata/browser, without deck ordering.
Random HQ and top-of-R&D access use aggregate server handles. Opponent-owned
choices do not expose this metadata. State schemas, serialized observation
contracts, legal-action allocation, and replay IDs are unchanged.

A confirmed large multi-card choice uses `BoardSelectionSubmission` to submit
current issued add-selection actions, reacquire each next decision, and submit
its finalization action. It checks continuation identity and the exact accepted
prefix at every step. A rejection or unexpected continuation stops the sequence
and exposes the authoritative remaining choice. Escape never undoes accepted
engine selections. Settlement, submission, opponent turns, and unrelated modals
block spatial input.

The Godot controller gives active target selection priority over source controls,
keeps contextual panels clear of target rectangles, and supports keyboard card
activation. Pile browsing remains inspection-first, with explicit card action
controls when abilities are available. Search and off-board targets appear in a
selectable card browser. Card artwork continues to come from the local cache.

## Verification

`make interaction-lab` includes twelve new scenarios (20–31) covering card-local
actions, identity abilities/card costs, forced run choices, hidden breach targets,
access payments, bioroid abilities, subroutine installs, incremental choices,
searches, tagged resources, pile abilities, and ordered targets. Existing memory,
pile, and multi-target scenarios now require confirmation.

The interaction smoke exercises native input at 1920×1080 and 2560×1440, local
selection without payment, Escape, keyboard selection, busy and modal guards,
fallback controls, and sequential incremental submissions. `make verify` requires
its `BOARD_SPATIAL_ACTIONS PASS` marker. Match tests cover typed projection,
source/target roles, hidden positions, ordering, payment collapsing, and interrupted
incremental sequences. Core tests cover search authorization and metadata privacy.

Synthetic screenshots are in `docs/evidence/m4-11/spatial-*.png`. Evidence runs
with live cached artwork are routed to ignored `artifacts/` instead.
