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
| UseAbility | Source (including identities and accessible piles), effect targets, separate additional card costs, confirmation; a sole targeted ability opens targets immediately | Migrated |
| TrashTaggedResource | Eligible resource and priced trash control | Migrated |
| ChooseCard, TrashProgram | Highlighted forced targets and confirmation | Migrated |
| ChooseBehavior with physical cards | Board or authorized candidate browser, ordered/set selection, confirmation | Migrated |
| ResolveIceSubroutine with card targets | Trash target or install card plus exact destination | Migrated |
| UseBioroidAbility | Encountered ICE, specific subroutine, click-cost confirmation | Migrated |
| ChooseBreachCandidate | Installed card/back, or HQ/R&D heading for aggregate access | Migrated |
| TrashAccessedCard | Always-visible priced Trash button beside the accessed artwork; one run-credit-first payment | Direct access control; no source submenu |
| StartRun | Run button and existing server picker | Intentionally retained |
| PurgeVirusCounters, RemoveTag | Global buttons | Intentionally retained |
| PassPriority, EndTurn, JackOut, ContinueRun, NoAccessAction | Progression buttons; Leave card beside accessed non-agendas, Continue access for agendas | Intentionally retained |
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

Access already has a selected target. `AccessActionsPresenter` exposes Trash and
Leave card directly beside the artwork, with the exact issued price and payment
split. Clicking the art inspects it. Clicking Trash confirms payment without an
extra target or confirmation menu. No trash button is invented from the printed
cost when Core has not issued that action. Agenda continuation stays explicit:
the engine resolves the steal step after the mid-access window, so the UI never
offers to leave an agenda. Direct controls revalidate the current decision and
action, and are disabled while busy, inspecting, or choosing other targets.

The interaction audit also removed the action-family menu when a selected source
has only one targeted ability. This includes bioroid ICE and single-ability pile
cards. The ability and its costs remain visible while selecting targets; payment
still waits for confirmation. Sources with distinct abilities retain their menu.

### Credit source audit

`RunPaymentPreference` applies one policy to access, breakers, ICE payments, and
the fallback list: among otherwise equivalent issued actions, spend the maximum
legal run-credit amount before personal credits. Target sets, effect choices,
cost quotes, and costs remain distinct.

The audit found a separate engine gap: generic behavior choices and paid
abilities counted only personal credits. A Runner with Overclock credits but
fewer than five personal credits could not choose Mânegarm Skunkworks' credit
payment. Those costs now use the same run-payment reducer: include the Runner's
live run pool when checking affordability and spend it before personal credits.
Corp costs cannot use the Runner's pool. Credit loss effects and explicitly
restricted hosted-counter costs keep their existing semantics. Production
Mânegarm/Overclock and Anoetic Void regressions, mixed-source ability payments,
atomic stale rejection, and save/reload tests cover this behavior.

The current `RunCredits` pool already includes Overclock's expiring credits;
`HostedCredits` is the historical name for that contribution in action contracts.
Unspent run credits expire at run end, and credit loss effects do not consume
them. There is no separate bad-publicity resource or recurring-credit subsystem
in the current Gateway engine. Hosted counters on Telework, Red Team, campaigns,
and similar cards are restricted card-effect pools, not automatically spendable
wallets. The UI selects engine-issued payments and cannot spend those counters.

For future cards that introduce additional payment sources, extend Core with
source eligibility, expiry, serialized payment allocation, and replay checks
before extending the preference to temporary → eligible hosted → personal.
Porting Chiriboga's generic automatic spend loop into the UI would bypass those
contracts. The existing run-credit support needs no additional temporary pool.

`Observation.BoardContext` is a nonserialized presentation sidecar, like the
existing decision/source metadata. It contains installed position handles,
viewer-authorized card views, and current selection cardinality/provenance.
Installed backs expose positions without definitions. Search candidates are
sorted by stable ID in the presentation metadata/browser, without deck ordering.
Random HQ and top-of-R&D access use aggregate server handles. Opponent-owned
choices do not expose this metadata. The board projection does not change state
schemas or serialized observation contracts. The credit-cost fix can expose
previously missing affordable actions; saved-state schemas stay unchanged and
continuation remains deterministic under the corrected payment rules.

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

`make interaction-lab` groups 26 practice scenarios behind a Runner/Corp
selector (18 Runner, 8 Corp). The menu covers card actions, identity abilities/card
costs, forced run choices, hidden breach targets, access payments, bioroid
abilities, subroutine installs, incremental choices, searches, tagged resources,
pile abilities, and ordered targets. Memory, pile, and multi-target choices
require confirmation.

The menu uses stable fixture IDs rather than list positions. **Hand & board
basics** includes event/operation play, and **Install in a server or new remote**
covers both destinations. Separate event/operation, new-remote, generic target,
heap-choice, and dense-scrolling entries are omitted from the picker; all 32
original fixtures remain in the automated regression suite. This keeps layout
stress and equivalent target/browser paths covered without duplicating practice
entries. The selector smoke checks both side groups, reordered IDs, and the
merged play/install interactions at both resolutions.

The interaction smoke exercises native input at 1920×1080 and 2560×1440, local
selection without payment, Escape, keyboard selection, busy and modal guards,
fallback controls, and sequential incremental submissions. `make verify` requires
its `BOARD_SPATIAL_ACTIONS PASS` marker. Match tests cover typed projection,
source/target roles, hidden positions, ordering, payment collapsing, and interrupted
incremental sequences. Core tests cover search authorization and metadata privacy.

Synthetic screenshots are in `docs/evidence/m4-11/spatial-*.png`. Evidence runs
with live cached artwork are routed to ignored `artifacts/` instead.
