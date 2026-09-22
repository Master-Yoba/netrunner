# M5.1 agent foundation

The foundation is a separate policy driver. `FullGameContractAgent` v6 remains
unchanged and is still the production/default benchmark agent. Corp/Runner
heuristics, measured Easy/Standard profiles and the encounter solver belong to
M5.3–4; the foundation does not claim those policies are implemented.

## Input and metadata

`ObservationReader.Read` turns a Core-built observation into immutable
`AgentFeatures`. Native dictionaries/enums and JSON values follow the same
decoding path. Optional hand-size, tag, memory, damage and timing information
remains nullable. Concealed server slots stay null; counts do not become card
identities. Actions retain their issued IDs and detached typed parameters,
including frozen costs, payment sources, targets and replacement choices.
Prompt and action-description prose never enters features or policy scoring.

Serialized observations deliberately omit `Observation.Decision`. A host must
rebuild through Core or supply the explicit viewer-owned envelope before action
selection. Passive updates have no decision or actions when the other side owns
the prompt. The reader rejects a missing/mismatched envelope for action choices.

The only observation addition is optional `PrivateState.choiceCards`: while an
owned private search is pending, Core projects eligible cards from that player's
draw deck using the existing `ObserveCard` permission check. Cards are sorted by
opaque ID, not deck position; ineligible cards, the opponent's search and deck
order remain unavailable. The field disappears when permission expires.

`ICardEvaluationCatalog` is defined in Core and implemented in Cards by
`GatewayEvaluationCatalog`. Descriptors detach public compiled behavior and
printed metadata, including ICE/subroutines, breakers, run events, assets,
agendas and effect categories. Strategic valuation is explicitly approximate
for every design. The adapter does not apply effects or decide legal costs.
Unknown definitions yield `TryGet == false` for conservative policy fallback.
AI continues to reference Core only.

## Configuration and transaction

`OfflineAgentConfiguration` pins schema, driver/policy/feature versions, side,
profile ID, immutable parameters and their hash, policy seed, work budget and
near-tie tolerance. Profile names are configuration identifiers; accepting an
identifier does not manufacture a playable difficulty policy. A policy object
must match the configured version.

`OfflineAgentCheckpoint` stores the configuration digest, independent serialized
`DeterministicRandom` state, committed decision count, observation sequence and
digest, bounded knowledge/history, optional tactical objective and last commit.
Match derives independent side seeds with the versioned domain
`netrunner/offline-policy-seed/v1`; agents receive neither the match seed nor
Core's random state.

The single-writer host protocol is:

1. Deliver the initial filtered observation at action sequence zero.
2. Evaluate from the committed checkpoint. Repeated evaluation at the same
   boundary returns the same issued action. The checkpoint does not change.
3. Apply the issued action in Core. On acceptance, commit its exact decision ID,
   revision, action ID and accepted-action sequence.
4. Deliver each side's filtered observation after every accepted action,
   including when that side does not own a decision.
5. Discard tentative evaluation on cancellation, rejection or invalidation.

Repeated identical commits/observation deliveries are idempotent. Skipped or
conflicting observation cursors and stale commits are rejected. Checkpoints can
be serialized/restored independently of an in-flight evaluation. The coordinator
and `FullMatchRunner` implement the optional `IAgentLifecycle`; existing agents
do not need to implement it.

The driver compares integer priorities first, then integer utility, and samples
only the configured near-tie set in stable action-ID order. Policies receive a
deterministic operation budget with cancellation checks; an exhausted budget
uses the policy's explicit fallback. Policies are required to be pure and charge
additional bounded work through that budget. A wall-clock watchdog belongs to
the benchmark host and cannot choose an alternate action. Private candidate
diagnostics are opt-in and never enter normal match history.

## Memory and limits

Current visible identities/locations are reconstructed from every observation.
When a card becomes untrackable, the agent keeps only its previously seen public
definition, not an instance/location belief in a shuffled or hidden deck.
History records observed install/reveal, advancement, rez and run transitions;
an install/reveal is explicitly not asserted to be a newly installed hidden card.
History retains 128 entries and remembered definitions retain 256 entries, using
deterministic trimming. Tactical objectives expire when their run, encounter or
visible target no longer matches.

This slice does not persist policy commits in the durable session trace. Legacy
trace recovery with a stateful lifecycle agent is rejected explicitly rather
than inventing fresh memory. Product save/recovery, durable policy checkpoints,
and the bounded asynchronous decision pump are M5.5 work. Factory/UI selection
of the heuristic remains M5.6 work.

## Evidence

Headless tests cover native/JSON equivalence, all decision-family envelopes,
hidden-state twins for both seats (hands, decks, unrezzed ICE, Archives, private
searches and access), identical features/scores/choices/next checkpoints,
retry/cancel/restore, knowledge invalidation, bounded work, metadata detachment
and host delivery. Real search/access fixtures generate decisions through Core.
Lifecycle-wrapped contract agents produce the same complete replay as the
unchanged agents. Synthetic CI covers all 77 compiled definitions with neutral
printed fixture values; the provisioned official lane additionally checks all
77 real printed definitions.

See [progress](M5_PROGRESS.md) for verification commands and completion status.
