# M2 timing scenarios (CR 26.03)

This document is the checked-in evidence index for the thirteen required
timing scenario categories of the M2 exit gate
(`docs/M2_ARCHITECTURE.md`, "Required timing scenarios"). Each category lists
the concrete scenario evidence in this repository (checked-in scenario files
and tests), the applicable CR 26.03 rule numbers, and a short behavioral
cross-check note against how the Jinteki.net automation presents the same
timing. Where a Jinteki.net behavior could be read differently, the difference
is documented here; nothing was silently copied.

Conventions:

- "Scenario" means a checked-in, deterministic, engine-produced event stream:
  either a unit-test-driven run (state built by the test, every step applied
  through `GameEngine.Apply` with the same action IDs a real client would be
  offered) or the pinned M2 scenario document
  `data/m2/representative-run.json` driven by `M2ProofRunner`.
- Every run in the index opens and closes with the typed `run-initiated` and
  `run-ended` events; when a run event is used, its placement/payment event
  precedes the nested `run-initiated` event. Every yielded decision is a real
  pending decision (no test bypasses the action layer).
- CR 26.03 section references use the run-timing sections (6.9), the breach
  sections (7.1–7.5), the encounter/subroutine rules (6.9.3, 8.1), and the
  credit rules (1.10.3/1.10.4: wallet credits, player-chosen hosted-credit
  spending, and run-scoped Overclock credits).

## 1. Initiation against unprotected and protected central/remote servers

Evidence:

- `RunTimingTests.Cr691eInitiationPaidWindowAlternatesRunnerThenCorp` —
  initiation against a remote with face-down ICE: the 6.9.1e initiation paid
  window alternates Runner then Corp before the first approach.
- `data/m2/representative-run.json` + `M2ProofRunner` (both policies) —
  initiation against the protected remote `remote:0` (two face-down ICE),
  including the Overclock run event (`card:7`) paid at initiation (6.9.1) and
  hosting the run-scoped Overclock credits (1.10.4).
- `RunEventTests.BeginRunWithRegisteredRunEventChargesCostOnlyAndSeedsRunCredits`
  and `RunEventTests.UnregisteredEventSourceIsRejected` — the printed cost is
  paid from the wallet only (the hosted credits seed the run-scoped pool,
  1.10.4), and a source that is not a registered RunnerGrip run event is
  rejected fail-closed.
- `RunTimingTests.Cr695aSuccessHasNoPaidWindowAndBreachBoundaryIsAutomatic` —
  the protected path continues past success; the unprotected path (no ICE)
  reaches 6.9.5a directly, covered by the same window-state machine.

CR 26.03: 6.9.1a–f (initiation, initiation paid window), 1.10.3c/1.10.4
(spending credits; run credits hosted by a run event).

Jinteki.net cross-check: Jinteki.net presents initiation as a run click on
the target server, then an immediate "continue" choice for both players before
the first ICE is approached; a run event's hosted credits are available to the
Runner during the run only. Matches the engine: the 6.9.1e window is the first
timing window of every run, and hosted credits expire with the run.

## 2. Each initiation/approach/encounter/movement/success/run-end paid window

Evidence:

- 6.9.1e initiation: `RunTimingTests.Cr691eInitiationPaidWindowAlternatesRunnerThenCorp`.
- 6.9.2b approach: `RunTimingTests.Cr692bApproachAlwaysYieldsPaidWindowAndRezIsOptional`.
- 6.9.3a/6.9.3b encounter: `RunTimingTests.Cr693bEncounterPaidWindowAlternatesBothSeats`,
  `EncounterSubroutineTests.EncounterPaidWindowRoundTripsAndContinuesIdentically`.
- 6.9.4b/6.9.4e movement: `RunTimingTests.Cr694dContinueRunMovesInwardAutomaticallyThenOpensAfterMoveWindow`.
- 6.9.5a success (no paid window) and 6.9.5b breach boundary:
  `RunTimingTests.Cr695aSuccessHasNoPaidWindowAndBreachBoundaryIsAutomatic`.
- Run end: `RunTimingTests.Cr694cJackOutIsRunnerOnlyAndCleansUpRun`
  (run-ended after a 6.9.4c jack out) and `EncounterResolutionTests.EndRunSubroutineStopsTheRemainingSequence`
  (run-ended by an end-the-run subroutine).
- `RunTimingTraceValidator` (W2) enforces the whole window sequence
  automatically: event placement/payment (when applicable), `run-initiated`,
  the 6.9.1e window first, then per-ICE
  approach → (encounter) → movement → jack-out → after-move windows, then
  success → breach windows, then `run-ended` last; `RunTimingTests` and the
  M2 proof run are checked against it.

CR 26.03: 6.9.1e, 6.9.2b, 6.9.3a–b, 6.9.4b, 6.9.4c, 6.9.4e, 6.9.5a, 6.9.5b.

Jinteki.net cross-check: Jinteki.net's run flow interleaves exactly these
"continue" prompts (initiation, each approach, each encounter, each move, and
the final continue before the run ends) and does not offer a prompt at success
itself. Matches the engine window set one-to-one.

## 3. Corp rez and decline-to-rez paths; unrezzed ICE is passed without encounter

Evidence:

- Rez: `RunRezCostTests.RezChargesPrintedCost`,
  `RunRezCostTests.UnaffordableRezIsNotOffered`,
  `RunRezCostTests.ExactlyAffordableRezIsOfferedAndLeavesZeroCredits`;
  the M2 proof run rezzes Palisade (`card:2`, 3 credits) and Whitespace
  (`card:1`, 2 credits) through 6.9.2b windows.
- Decline: `RunTimingTests.Cr692bPassingApproachLeavesIceUnrezzedAndSkipsEncounter`
  — Corp passes the approach window; the ICE stays unrezzed and face-down,
  no encounter window opens, and the run moves inward.
- `RunTimingTests.RunActionRollbackKeepsExactStateForForgedRezTarget` — a
  rez submitted for the wrong ICE is rejected with a byte-identical state.

CR 26.03: 6.9.2a–d (approach window, optional rez), 8.1 (rez costs and state).

Jinteki.net cross-check: on approach, Jinteki.net offers the Corp "rez" or
"continue"; continuing leaves the ICE face-down and unencountered. Matches:
the engine treats rez as optional and never encounters face-down, unrezzed
ICE.

## 4. Breaker strength below/equal/above ICE, and partial/full break

Evidence:

- Strength rules: `EncounterStrengthTests.IceUsesBaseStrengthWhenNoModifiersApply`,
  `IceRemoteProtectingModifierAppliesOnlyOnRemotes`,
  `BreakerUsesBaseStrengthWithoutActivePumps`,
  `BreakerAddsPumpStrengthPerEncounterExpiringPumpItSources`,
  `BreakerIgnoresForeignSourcesRunExpiryAndOtherBehaviors` — below/equal/above
  comparisons are decided by `EncounterStrength`, including pump and modifier
  contributions.
- Break offering and quantity: `RunBreakTests.EncounterPaidWindowOffersBreakQuantitiesAndPassForRigBreaker`,
  `BreakingAllSubroutinesChargesCostMarksAllBrokenAndRetainsPriority` (full
  break), `PartialBreakBreaksFirstUnbrokenInOrdinalOrderAndAllowsSecondUse`
  (partial break), `BreakNotOfferedWhenRunnerCannotAfford`,
  `BreakNotOfferedWhenBreakerStrengthIsBelowIceStrength` (below → no break),
  `BreakNotOfferedWhenBreakerInterfaceDoesNotMatchIceType`,
  `ForgedBreakQuantityBeyondTheBreakLimitReportsRunViolation`.
- Payment choices: `RunEventTests.PumpCostSplittingOffersEveryLegalWalletHostedDivision`,
  `RunEventTests.BreakCostCanBePaidFromHostedRunCreditsByChoice`, and
  `BreachAccessTests.AccessTrashOffersEveryLegalWalletHostedDivision` — when
  more than one division is legal, each wallet/hosted split is a serialized
  action and the reducer applies the selected split atomically.
- Pumps closing the gap: `RunPumpTests.PumpRaisesStrengthSoBreaksBecomeLegal`,
  `RepeatedPumpsAccumulateDistinctEncounterEffects`.
- The M2 proof run: Cleaver (base 3, pump +1 = 4) meets Palisade (base 2 +
  remote-protecting 2 = 4) — the equal-strength full break of all
  subroutines, paid from the hosted run-credit pool (5 → 3 → 2), followed by
  the second encounter's two breaks (2 → 1 → 0; the ledger entries are part
  of the pinned trace).

CR 26.03: 6.9.3c–e (encounter, breaking), icebreaker rules (3.9.5),
1.10.3c/1.10.4 (break/pump costs may be divided by the Runner between the
wallet and hosted run credits; each legal division is an engine-issued
action).

Jinteki.net cross-check: Jinteki.net only enables the "break" button when the
icebreaker's current strength meets or exceeds the ICE's current strength and
the Runner can pay; partial break is a quantity choice up to the number of
subroutines. Matches: break actions are offered only when legal, with each
activation's quantity bounded by the printed BreakLimit and the remaining
unbroken subroutines.

## 5. Ordered unbroken subroutines, including end-the-run cleanup

Evidence:

- `EncounterSubroutineTests.EncounterInitializationCreatesPrintedSubroutinesUnbrokenAndUnresolved`,
  `PlainFixtureIceEncountersWithZeroSubroutines`,
  `UnregisteredIceDefinitionFailsClosedAtEncounterInitialization`.
- `EncounterResolutionTests.UnbrokenSubroutinesResolveInPrintedOrderAndRunContinues`,
  `BrokenSubroutinesAreSkippedAndRemainingSubroutinesFire`,
  `EndRunSubroutineStopsTheRemainingSequence` (end-the-run ends the run
  mid-sequence with run cleanup: run state removed, run credits expired,
  encounter-scoped effects cleaned),
  `EncounterScopedPumpsExpireAtEncounterCompletion`,
  `FullEncounterFlowBreaksAndEndsTheRunWithCleanup`,
  `LoseCreditsSubroutineClampsToAvailableCredits`.
- The M2 proof run: both of Whitespace's subroutines (lose 3 credits; end
  the run if at most 6 credits) are broken during the encounter, so no
  subroutine fires and the end-the-run check never runs; the ordered
  encounter evidence (pump, break, break) is pinned in
  `M2ProofTests.CuratedRunSatisfiesTheExitGateOrdering`.

CR 26.03: 6.9.3e (unbroken subroutines fire in printed order), 6.9.4/6.9.6
(run cleanup on end-the-run), 1.10.3b (credit loss: from the wallet pool
only, clamped to the wallet's holdings — never from the run-scoped hosted
pool).

Jinteki.net cross-check: Jinteki.net fires subroutines one after another in
printed order, each as its own resolution step, and an "end the run"
subroutine immediately returns control with the run cleaned up. Matches;
the engine additionally clamps credit loss to available credits (Jinteki.net
does the same for the Runner wallet; here the loss comes from the wallet
only — the run-scoped pool is drawn first solely for spend costs, per
1.10.3b/1.10.3c).

## 6. Jack out at each legal movement decision and rejection elsewhere

Evidence:

- `RunTimingTests.Cr694cJackOutIsRunnerOnlyAndCleansUpRun` — the 6.9.4c
  jack-out decision is offered at the legal movement decision, is Runner-only,
  and ending the run by it performs full run cleanup (run credits expire).
- `RunTimingTests.Cr694dContinueRunMovesInwardAutomaticallyThenOpensAfterMoveWindow`
  — choosing "continue" moves inward and opens the 6.9.4e window.
- Rejection elsewhere: `RunBreakTests.BreakSubroutinesAtNonEncounterStepIsRejectedWithByteIdenticalState`,
  `RunPumpTests.PumpBreakerAtNonEncounterStepIsRejectedWithByteIdenticalState`
  (same window-scoping machinery: a run action submitted outside its legal
  step is rejected and the state is byte-identical), and
  `RunTimingTraceValidator` failing when a 6.9.4c window is missing after a
  movement window.
- The M2 proof run exercises the continue path at every movement decision
  (`ScriptedHumanRunPolicy` defaults to `ContinueRun`).

CR 26.03: 6.9.4c (jack-out decision), 6.9.4d (continue), 6.9.4e (after-move
window).

Jinteki.net cross-check: Jinteki.net offers "jack out" or "continue" at each
movement step; jacking out ends the run and returns all run-scoped state.
Matches; jack out is never offered during an encounter or before the first
move, exactly as the engine scopes the decision.

## 7. Remote, HQ, R&D, and Archives breach candidate behavior

Evidence:

- `BreachAccessTests.RemoteBreachAccessesRootCardsInChosenOrderAndEndsRun` —
  remote breach offers the hidden root cards as candidates in chosen order.
- `BreachAccessTests.HqBreachOffersSingleHiddenHandTokenAndDrawsDeterministically`
  — HQ breach: exactly one hidden hand card, drawn from the shuffled hand
  with the run's deterministic RNG.
- `BreachAccessTests.RndBreachAccessesTopCardOfDeck` — R&D breach: the top
  card of the stack.
- `BreachAccessTests.ArchivesBreachTurnsEveryDiscardFaceupAndKeepsItPublic`
  — Archives breach: every card in the discard is turned faceup
  authoritatively at breach start and remains public after the run (7.5.2).
- `BreachAccessTests.ArchivesBreachOffersRootCardsFirstThenDiscardAccess`
  — Archives breach: root-zone cards are explicit candidates listed first
  (7.4.1a) with the now-faceup discard following (7.4.1d); the facedown root
  card is anonymous to the Runner until selection and revealed to both viewers
  for the remainder of the breach (7.3.1a).
- `BreachAccessTests.ForgedBreachCandidateActionIsRejectedWithoutMutatingState`
  — an unoffered candidate selection is rejected with byte-identical state.
- The M2 proof run: remote breach of `remote:0` with two candidates, chosen
  by the Runner (Offworld Office `card:3` first, then Nico Campaign
  `card:4`) — pinned in `M2ProofTests.CuratedRunSatisfiesTheExitGateOrdering`.

CR 26.03: 7.4.1 (candidate pool per server kind), 7.5.1 (breach start),
7.5.3 (HQ/R&D hand/deck choice limit), 7.5.4 (candidate selection),
6.9.5b (nested breach boundary).

Jinteki.net cross-check: Jinteki.net shows the breachable server's cards as a
choice list (Archives in fixed order, remote in the player's chosen order,
HQ/R&D as a single hidden card), and the run ends when the last card is
accessed. Matches; the engine exposes hidden candidates without identity until
chosen (stricter than the visual card list, same legal choices).

## 8. Trash, no-action, and mandatory agenda steal access paths

Evidence:

- `BreachAccessTests.RemoteBreachOffersTrashForPayableAssetAndTrashesIt` —
  trash offered for a payable asset, paid atomically, card trashed.
- `BreachAccessTests.AccessTrashOffersEveryLegalWalletHostedDivision` — when
  both sources can pay an accessed-card trash cost, the wallet-only and split
  actions are both offered and the selected split is replayable.
- `BreachAccessTests.TrashNotOfferedWhenRunnerCannotAffordIt` — trash not
  offered when unaffordable (no-action is the only option).
- `BreachAccessTests.AssetInArchivesIsAccessedButNotTrashed` — trash is not
  part of Archives access.
- `BreachAccessTests.RemoteBreachAccessOfAgendaForcesStealAndScoresIt` and
  `ArchivesAgendaBreachAccessStillForcesSteal` — an accessed agenda is
  mandatorily stolen.
- `BreachAccessTests.PassingTheMidAccessWindowCannotSkipTheAgendaSteal` —
  passing the mid-access window cannot skip the mandatory steal.
- `BreachAccessTests.StealingWinningAgendaEndsTheGame` — steal resolves
  through the normal scoring/win path.
- `RunTimingTraceTests.CardAccessedWithoutMidAccessWindowIsRejectedEvenForNoActionAccess`,
  `DuplicateMidAccessWindowsAreRejectedWithinOneAccessSegment`, and
  `CrossCardAccessEffectIsRejected` — the trace validator requires exactly
  one bounded 7.2.2 window for every `CardAccessed`, including no-action
  accesses, and binds any steal/trash effect to that accessed card ID.
- The M2 proof run: steal Offworld Office (2 agenda points), then trash Nico
  Campaign for its printed cost — both 7.2.2 mid-access windows appear in the
  pinned trace.

CR 26.03: 7.1 (accessing cards; 7.1.5 basic trash ability; 7.1.6 mandatory
steal), 7.2 (steps of accessing a card; 7.2.2 single mid-access ability;
7.2.3 agenda steal).

Jinteki.net cross-check: Jinteki.net offers "trash" (with its cost), "steal"
(for agendas, mandatory), or "do nothing" per accessed card; trashing is
skipped when the cost cannot be paid. Matches one option-set per accessed
card, with steal the only legal choice for agendas.

## 9. Conditional mandatory/optional/simultaneous triggers and explicit ordering

Evidence:

- `CheckpointTests.MandatoryTriggerResolvesAutomaticallyWithoutAPlayerDecision`,
  `OptionalTriggerCanBeDeclinedAndLeavesOriginalEffectCommitted`,
  `OptionalTriggerCanBeUsedAsAChildResolution`,
  `SimultaneousSameControllerTriggersYieldExplicitOrdering` (same controller:
  printed/registration order),
  `ActivePlayerPrecedesInactiveControllerInSimultaneousTriggers` (simultaneous
  triggers with different controllers: active player first),
  `MaterializedTriggerSurvivesItsSourceMovingAfterMaterialization`,
  `SelfTriggeringTriggerResolvesExactlyOncePerOccurrence`,
  `DeclinedTriggerIsOfferedAgainForNewOccurrence`.
- `ResolutionEngineTests` (stale decision/revision rejection, atomic
  nested submissions, typed card/number materialization) is the frame the
  trigger checkpoints run in.
- The M2 proof run: both of Whitespace's subroutines are broken during the
  encounter, so its conditional end-the-run-if-credits-at-most check never
  fires; the trigger-window ordering this category describes is pinned by
  the M1 trigger tests above (see also category 5).

CR 26.03: 9.6 (conditional abilities; instances are created pending in the
next reaction window, 9.6.2), 9.2.8 (reaction windows: both players have
priority, starting with the active player — 9.2.8b), 9.6.15 (steps of
triggering and resolving a conditional ability).

Jinteki.net cross-check: Jinteki.net resolves simultaneous triggers in a
fixed order (active player's triggers first, then by registration) and
auto-resolves mandatory triggers without a prompt. Matches the engine's
explicit-ordering checkpoints; the engine additionally persists every
trigger checkpoint so a save/load mid-chain resumes identically (Jinteki.net
does not offer save/load mid-run).

## 10. Prevention, replacement, lingering expiry, damage/flatline, tags, memory cleanup, counters, purge

Evidence:

- Prevention: `CheckpointTests.PreventionCanPartiallyReduceDamageAndThenCommitTheRemainder`,
  `PreventionCanFullyPreventDamageWithoutACommittedDamageOccurrence`,
  `DeclinedPreventionInterruptIsOfferedAgainForReplacementOccurrence`.
- Replacement: `CheckpointTests.ReplacementResolvesInsteadOfOriginalEffect`.
- Lingering expiry: `RulesEffectsTests.LingeringEffectsHaveExplicitExpiryAndCleanup`,
  `EncounterResolutionTests.EncounterScopedPumpsExpireAtEncounterCompletion`,
  `RunEventTests.UnspentRunCreditsExpireAtRunEndWithoutReturningToTheWallet`
  (run lingering: hosted Overclock credits expire exactly at run end).
- `RunEventTests.RunEventIsTrashedToTheHeapFaceupAtRunEnd` and
  `RulesEffectsTests.DamageUsesRestorableRandomStateAndDeepSaveResume` — the active
  event is public in RunnerPlayArea while the nested run resolves and enters
  RunnerHeap faceup; damage and other heap moves use the same normalization.
- Damage/flatline: `RulesEffectsTests.DamageUsesRestorableRandomStateAndDeepSaveResume`,
  `DamageGreaterThanGripFlatlinesRunnerAndClearsResolution`,
  `CoreDamageChangesMaximumHandSizeAndTrashesCards`,
  `OverMemoryRequiresCleanupFrameAndFlatlineRequiresCorpWinner`.
- Tags: `RulesEffectsTests.TagUnderflowRollsBackEarlierEffects`.
- Mandatory memory cleanup: `RulesEffectsTests.MemoryOverageYieldsTypedCleanupAndActuallyMovesPrograms`.
- Counters and purge: `RulesEffectsTests.CountersSupportAddRemoveUnderflowAndGlobalVirusPurge`.
- The M2 proof run pins the encounter-lingering case end-to-end: the Cleaver
  pump expires at the Whitespace encounter boundary and the run credits
  (0 after the two Whitespace breaks — Whitespace's printed credit loss
  never fired because both of its subroutines were broken) confirm
  run-scope expiry in the ledger.

CR 26.03: 9.9 (interrupts and replacement effects — prevention/replacement
timing), 10.4 (damage), 10.4.4 (flatline; see 1.7.2b), 10.5 (tags),
2.10/3.9.3c (memory limit and mandatory cleanup) at the 10.3.1e checkpoint,
6.9.6b (run-scoped credit expiry; hosted Overclock credits leave with their
card, 1.10.4).

Jinteki.net cross-check: Jinteki.net resolves prevention/replacement in
response-to-announcement order, auto-cleans memory overage at the end of the
turn (here: at the cleanup point the rule names), and purges counters on the
global virus purge event. Matches; differences are none in behavior — the
engine's additional guarantee is byte-identical resume after save/load inside
each of these resolutions (see category 11).

## 11. Save/load at every yielded decision and representative nested interrupt

Evidence:

- `RunTimingTests.Cr696dSaveLoadAtEveryYieldedDecisionProducesIdenticalContinuation`
  (run decisions), `BreachAccessTests.SaveLoadMidBreachProducesIdenticalContinuation`
  (breach decisions), `RunBreakTests.SaveLoadAfterPartialBreakRestoresTheWindow`,
  `EncounterBreakerTests.SaveLoadAfterBreakRestoresTheWindowWithoutUsageLedger`,
  `RunPumpTests.SaveLoadAfterPumpRestoresTheWindow`,
  `RunEventTests.RunCreditsSurviveSaveLoadAndContinueIdentically`,
  `ResolutionEngineTests.InnerPromptRoundTripsAndCompletesIdenticallyAfterSaveLoad`
  (nested interrupt), `CheckpointTests.NestedSaveLoadAtEveryYieldedCheckpointDecision`,
  `CheckpointTests.IdenticalStateEventTraceAndHashesAfterMidScenarioReload`
  (nested trigger chain).
- `M2ProofRunner` itself saves and loads the canonical state at every yielded
  decision of the representative run and throws on any hash divergence;
  `M2ProofTests.SaveLoadSucceedsAtEveryYieldedDecision` asserts the checkpoint
  ledger and re-verifies the replay artifact.
- `RunTimingTests.LiveRunSnapshotIsDeeplyDetachedAndRoundTripsStrictly` —
  snapshots never alias live state.

CR 26.03: n/a (engine determinism requirement, M2 exit gate item 4); the
windows saved are the CR 26.03 6.9.x windows.

Jinteki.net cross-check: Jinteki.net has no public save/load of an in-flight
run; the equivalent guarantee there is that its state machine is a pure
function of (state, legal action). The engine makes the same property explicit
and tested: every yielded decision round-trips through `EngineJson` with
byte-identical continuation hashes.

## 12. Exact Corp/Runner observations with no hidden-information leakage

Evidence:

- `ObservationTests.BeforeRunBothViewersHaveTheExactPinnedShape` (exact
  string goldens for both viewers),
  `BeforeRunNeitherViewerSeesTheOpponentsHiddenZonesOrEngineInternals`,
  `ApproachingFaceDownIceOnlyTheCorpKnowsTheIdentity` (face-down ICE identity
  is Corp-private),
  `DuringBreachCandidateSelectionTheRunnerCannotSeeTheHiddenPool`,
  `DuringBreachMidAccessTheChosenHiddenCardIsRevealedToBothViewers` (and only
  the chosen card),
  `AfterRunTheScoredCardIsPublicAndTheRemainingHandStaysHidden`,
  `TheObservationNeverExposesEngineInternals` (no RNG state, no card IDs
  outside legal visibility, no debug data),
  `SameStateAndViewerProduceByteIdenticalJsonAcrossSerializationRoundTrips`,
  `AllFixturesValidateAsProducibleEngineStates`.
- `M2ProofRunner` drives both policies exclusively through
  `ObservationBuilder.Build(state, viewer)` — the same boundary a client
  would consume.
- `RunEventTests.ActiveRunEventIsFaceupAndVisibleToBothViewersDuringTheRun`
  and `RunEventTests.RunEventIsTrashedToTheHeapFaceupAtRunEnd` cover the
  active public play area and post-run heap visibility; the event is no longer
  in RunnerGrip, so grip damage cannot select it.

CR 26.03: information-visibility rules per side (6.9.2 face-down ICE, 7.5.4
hidden candidates, 7.1.2 accessed-card visibility, 7.3.1a breach-scoped
visibility, 7.5.2 permanent Archives-discard reveal, 4.4.6c facedown Archives
visible to the Corp only).

Jinteki.net cross-check: Jinteki.net's bot/observer model hides the same
sets: the Corp sees its face-down ICE and the Runner never sees hidden
candidate identities until chosen. Matches; the engine's negative goldens are
stricter (exact serialized shape, no internals), which Jinteki.net does not
publish but does not contradict.

## 13. Identical seed/actions yielding byte-identical state, events, timing trace, and replay hashes

Evidence:

- `M2ProofTests.BothPoliciesProduceByteIdenticalCuratedRuns` — two different
  policies (scripted-human and contract-agent) driving the same pinned
  scenario produce byte-identical final-state hashes and event-stream hashes;
  both are pinned as constants.
- `M2ProofTests.SaveLoadSucceedsAtEveryYieldedDecision` — the replay artifact
  of the representative run verifies successfully (replay from the pinned
  initial snapshot through the recorded action IDs reproduces the final
  state hash).
- `ToyMatchRunnerTests.SameSeedAndGoalProduceByteIdenticalStateReplayAndEventEvidence`
  and `TamperedPerStepStateOrEventEvidenceReportsTheFirstAction` (M1 match
  evidence), `ReplayCommandReportsSuccessAndFirstDivergence`,
  `PlayCanExportReplayThatTheReplayCommandVerifies`.
- `ToyRulesTests.SameSeedProducesIdenticalStateAndEvidenceAtEveryToyStep`,
  `DeterministicRandomTests.SameSeedProducesSameSequence`.
- The trace itself: `RunTimingTrace` entries for the representative run are
  derived only from the frozen event stream, so byte-identical events imply
  byte-identical rendered traces; the validator's problem list is part of the
  proof output.

CR 26.03: n/a (engine determinism requirement, M2 exit gate items 1 and 5).

Jinteki.net cross-check: Jinteki.net re-rolls its deterministic RNG per game
and its replay of a recorded game reproduces the same state; it does not
publish state hashes. The engine publishes and pins them (state, events,
trace), which is strictly stronger.

## Pinned representative run

`data/m2/representative-run.json` (scenario hash
`4733c8f84f1f054c6c1277e07d6450173cfb0d4741d11466cf8997cfd5ea4ac6`) is the
single curated run covering categories 1–8 and 11–13 end-to-end: initiation
with a run event, two rezzed encounters (the barrier is pumped to equal
strength and broken, then both code-gate subroutines are broken with the
second rig breaker), ordered movement/jack-out decisions, success, remote
breach with two chosen candidates (steal + trash), and run end — with
save/load and resume from the restored state at every yielded decision,
replay verification, and the cited trace validator all green. Its exact
window ordering and the run-credits ledger (3, 2, 1, 0) are asserted in
`M2ProofTests.CuratedRunSatisfiesTheExitGateOrdering`, and the human-readable
rendering with per-line CR citations is produced by
`RunTimingTrace.RenderText()` and emitted by the `m2-proof` CLI command.

The run-credits pool trajectory of the curated run is 5 (Overclock hosted
pool at run start) → 3 (Cleaver pump) → 2 (Cleaver break) → 1 (Buzzsaw
break) → 0 (Buzzsaw break); the typed events record the adjusted values
after each change (3, 2, 1, 0), the pool never goes negative and is exactly
zero at run end, and the wallet ends at 12 (15 − 1 Overclock − 2 Nico trash,
paid from the wallet after the pool was exhausted — 1.10.3b).
