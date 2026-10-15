namespace Netrunner.Core;

/// <summary>How <see cref="GameEngine.Apply(GameState, ActionSubmission?, IBehaviorCatalog, IRuntimeCardCatalog, ApplyMode)"/> treats an action.</summary>
public enum ApplyMode
{
    /// <summary>Full invariant validation and hashing; the only mode for play, saves and replays.</summary>
    Authoritative,

    /// <summary>For search: no validation or hashing, and the result is marked simulated.</summary>
    Simulation,
}

/// <summary>
/// Deterministic reducer boundary for the M1 engine primitives. The reducer
/// never mutates the supplied state: a working copy owns all lists, the ID
/// allocator, the RNG cursor, and the event buffer until a prospective state
/// has passed invariant validation.
/// </summary>
public static partial class GameEngine
{
    public const int DefaultResolutionStepBudget = 128;

    internal static void ThrowIfSimulated(GameState state)
    {
        if (state.IsSimulated)
        {
            throw new InvalidOperationException(
                "A simulated state skipped invariant validation and can't be saved, hashed, snapshotted or replayed.");
        }
    }

    /// <summary>
    /// Applies an action in the given mode. <see cref="ApplyMode.Simulation"/>
    /// skips invariant validation and state/event hashing for search; its
    /// result is marked simulated and carries no accepted-action record.
    /// </summary>
    public static ApplyResult Apply(
        GameState state,
        ActionSubmission? submission,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog,
        ApplyMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return ApplyCore(
            state,
            submission,
            behaviorCatalog,
            runtimeCardCatalog,
            enforceCatalogCompatibility: true,
            maxResolutionSteps: DefaultResolutionStepBudget,
            mode);
    }

    /// <summary>
    /// Applies <paramref name="submission"/> in both modes from the same
    /// authoritative state and compares the resulting states and events. Used
    /// by differential tests and fuzzing; returns null when they match, or a
    /// description of the first difference. The simulated result never leaves
    /// this method.
    /// </summary>
    public static string? VerifySimulationEquivalence(
        GameState state,
        ActionSubmission submission,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ApplyResult authoritative = Apply(state, submission, behaviorCatalog, runtimeCardCatalog, ApplyMode.Authoritative);
        ApplyResult simulated = Apply(state, submission, behaviorCatalog, runtimeCardCatalog, ApplyMode.Simulation);
        if (authoritative.Accepted != simulated.Accepted)
        {
            return $"Authoritative accepted={authoritative.Accepted} but simulation accepted={simulated.Accepted}.";
        }

        if (!authoritative.Accepted)
        {
            return authoritative.Rejection?.Code == simulated.Rejection?.Code
                ? null
                : $"Rejection codes differ: {authoritative.Rejection?.Code} vs {simulated.Rejection?.Code}.";
        }

        simulated.State.IsSimulated = false;
        try
        {
            string expected = DeterministicHash.ForState(authoritative.State);
            string actual = DeterministicHash.ForState(simulated.State);
            if (expected != actual) return $"State hash differs: {expected} vs {actual}.";
            string expectedEvents = DeterministicHash.ForEvents(authoritative.Events);
            string actualEvents = DeterministicHash.ForEvents(simulated.Events);
            return expectedEvents == actualEvents ? null : $"Event hash differs: {expectedEvents} vs {actualEvents}.";
        }
        finally
        {
            simulated.State.IsSimulated = true;
        }
    }

    /// <summary>
    /// The turn-start boundary result.  Turn setup emits its opening click
    /// allotment and paid-window events before yielding the first decision;
    /// callers that need a complete event stream must prepend this batch to
    /// subsequent <see cref="ApplyResult.Events"/> batches.
    /// </summary>
    public sealed record TurnStartResult(GameState State, IReadOnlyList<EngineEvent> Events);

    /// <summary>Creates an engine session bound to one immutable catalog.</summary>
    public static GameEngineSession CreateSession(IBehaviorCatalog behaviorCatalog) =>
        new(behaviorCatalog);

    public static GameEngineSession CreateSession(
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog) =>
        new(behaviorCatalog, runtimeCardCatalog);

    public static ApplyResult Apply(GameState state, ActionSubmission? submission)
    {
        return ApplyCore(
            state,
            submission,
            BehaviorCatalog.Empty,
            runtimeCardCatalog: null,
            enforceCatalogCompatibility: false,
            maxResolutionSteps: DefaultResolutionStepBudget);
    }

    /// <summary>
    /// Applies an action using an explicitly composed immutable behavior
    /// catalog.  The catalog is captured for the whole atomic reduction and
    /// is never read from process-wide mutable registration state.
    /// </summary>
    public static ApplyResult Apply(
        GameState state,
        ActionSubmission? submission,
        IBehaviorCatalog behaviorCatalog)
    {
        return ApplyCore(
            state,
            submission,
            behaviorCatalog,
            runtimeCardCatalog: null,
            enforceCatalogCompatibility: true,
            maxResolutionSteps: DefaultResolutionStepBudget,
            ApplyMode.Authoritative);
    }

    /// <summary>Parameter-order adapter for composition callers.</summary>
    public static ApplyResult Apply(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        ActionSubmission? submission) =>
        Apply(state, submission, behaviorCatalog);

    /// <summary>
    /// Applies an action with separately injected executable behavior and
    /// printed-card metadata catalogs. The two fingerprints remain distinct.
    /// </summary>
    public static ApplyResult Apply(
        GameState state,
        ActionSubmission? submission,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        return ApplyCore(
            state,
            submission,
            behaviorCatalog,
            runtimeCardCatalog,
            enforceCatalogCompatibility: true,
            maxResolutionSteps: DefaultResolutionStepBudget);
    }

    private static ApplyResult ApplyCore(
        GameState state,
        ActionSubmission? submission,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        bool enforceCatalogCompatibility,
        int maxResolutionSteps,
        ApplyMode mode = ApplyMode.Authoritative)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        bool simulation = mode == ApplyMode.Simulation;
        if (state.IsSimulated && !simulation)
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.InvalidInputState,
                "A simulated state can't be applied authoritatively.");
        }

        if (submission is null)
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.InvalidInputState,
                "An action submission is required.");
        }

        if (enforceCatalogCompatibility
            && !CatalogMatchesState(state, behaviorCatalog, out string? catalogMismatch))
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.InvalidInputState,
                catalogMismatch!);
        }

        if (runtimeCardCatalog is not null
            && !CatalogMatchesState(state, runtimeCardCatalog, out string? runtimeMismatch))
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.InvalidInputState,
                runtimeMismatch!);
        }

        if (!simulation
            && !state.WasValidatedWith(enforceCatalogCompatibility ? behaviorCatalog : null, runtimeCardCatalog)
            && !StateInvariantValidator.TryValidate(
                state,
                enforceCatalogCompatibility ? behaviorCatalog : null,
                runtimeCardCatalog,
                out IReadOnlyList<StateInvariantViolation> violations))
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.InvalidInputState,
                FormatViolations(violations));
        }

        if (state.Outcome.Kind == GameOutcomeKind.Won)
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.TerminalState,
                "Actions cannot be applied after the game has reached a terminal outcome.");
        }

        PendingDecision? pending = state.PendingDecision;
        if (pending is null)
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.NoPendingDecision,
                "The state does not currently await a decision.");
        }

        if (submission.Actor != pending.Owner)
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.WrongOwner,
                $"Decision '{pending.Id}' belongs to player '{pending.Owner}', not '{submission.Actor}'.");
        }

        if (submission.DecisionId != pending.Id)
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.StaleDecisionId,
                $"Decision '{submission.DecisionId}' is stale; the current decision is '{pending.Id}'.");
        }

        if (submission.Revision != pending.Revision)
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.StaleRevision,
                $"Revision {submission.Revision} is stale; the current decision revision is {pending.Revision}.");
        }

        LegalAction? action = pending.LegalActions.FirstOrDefault(candidate => candidate.Id == submission.ActionId);
        if (action is null)
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.UnknownAction,
                $"Action '{submission.ActionId}' is not legal for decision '{pending.Id}'.");
        }

        if (maxResolutionSteps <= 0)
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.ResolutionFailure,
                "The resolution step budget must be positive.");
        }

        var working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        try
        {
            working.PendingDecision = null;
            ConsumeAction(working, action, pending.Owner);
            Advance(working, maxResolutionSteps);
            working.ActionSequence = NextSequence(working.ActionSequence, "action");

            GameState next = working.Build(state);
            IReadOnlyList<EngineEvent> events = FreezeEvents(working.Events);
            if (simulation)
            {
                return ApplyResult.AcceptSimulated(next.MarkSimulated(), events);
            }

            StateInvariantValidator.Validate(
                next,
                enforceCatalogCompatibility ? behaviorCatalog : null,
                runtimeCardCatalog);
            next.MarkValidated(enforceCatalogCompatibility ? behaviorCatalog : null, runtimeCardCatalog);
            var acceptedAction = new AcceptedActionRecord(
                working.ActionSequence,
                submission,
                DeterministicHash.ForState(next),
                DeterministicHash.ForEvents(events));
            return ApplyResult.Accept(next, events, acceptedAction);
        }
        catch (Exception exception) when (IsResolutionFailure(exception))
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.ResolutionFailure,
                $"The action could not be resolved: {exception.Message}");
        }
    }

    /// <summary>Convenience overload retaining the complete submission shape.</summary>
    public static ApplyResult Apply(
        GameState state,
        PlayerId actor,
        DecisionId decisionId,
        long revision,
        ActionId actionId) =>
        Apply(state, new ActionSubmission(actor, decisionId, revision, actionId));

    /// <summary>
    /// Starts a serializable resolution frame and advances it until it either
    /// yields a pending decision or completes. This setup boundary is useful
    /// to rule handlers and tests; action submission remains the only caller
    /// choice once a decision has been yielded.
    /// </summary>
    public static GameState BeginResolution(
        GameState state,
        ResolutionFrame frame,
        int maxResolutionSteps = DefaultResolutionStepBudget)
    {
        return BeginResolutionCore(
            state,
            frame,
            BehaviorCatalog.Empty,
            runtimeCardCatalog: null,
            enforceCatalogCompatibility: false,
            maxResolutionSteps: maxResolutionSteps);
    }

    /// <summary>Begins a resolution using an injected immutable catalog.</summary>
    public static GameState BeginResolution(
        GameState state,
        ResolutionFrame frame,
        IBehaviorCatalog behaviorCatalog,
        int maxResolutionSteps = DefaultResolutionStepBudget)
    {
        return BeginResolutionCore(
            state,
            frame,
            behaviorCatalog,
            runtimeCardCatalog: null,
            enforceCatalogCompatibility: true,
            maxResolutionSteps: maxResolutionSteps);
    }

    /// <summary>
    /// Begins a resolution with explicitly injected executable behavior and
    /// printed-card metadata catalogs. Both catalogs are checked against the
    /// state before any frame is opened or state is copied.
    /// </summary>
    public static GameState BeginResolution(
        GameState state,
        ResolutionFrame frame,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog,
        int maxResolutionSteps = DefaultResolutionStepBudget)
    {
        return BeginResolutionCore(
            state,
            frame,
            behaviorCatalog,
            runtimeCardCatalog,
            enforceCatalogCompatibility: true,
            maxResolutionSteps: maxResolutionSteps);
    }

    /// <summary>Parameter-order adapter for injected-session callers.</summary>
    public static GameState BeginResolution(
        GameState state,
        ResolutionFrame frame,
        int maxResolutionSteps,
        IBehaviorCatalog behaviorCatalog) =>
        BeginResolution(state, frame, behaviorCatalog, maxResolutionSteps);

    private static GameState BeginResolutionCore(
        GameState state,
        ResolutionFrame frame,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        bool enforceCatalogCompatibility,
        int maxResolutionSteps)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        if (enforceCatalogCompatibility
            && !CatalogMatchesState(state, behaviorCatalog, out string? catalogMismatch))
        {
            throw new ResolutionFailureException(catalogMismatch!);
        }

        if (runtimeCardCatalog is not null
            && !CatalogMatchesState(state, runtimeCardCatalog, out string? runtimeMismatch))
        {
            throw new ResolutionFailureException(runtimeMismatch!);
        }

        StateInvariantValidator.Validate(
            state,
            enforceCatalogCompatibility ? behaviorCatalog : null,
            runtimeCardCatalog);

        if (state.Outcome.Kind == GameOutcomeKind.Won)
        {
            throw new ResolutionFailureException("A terminal state cannot begin a new resolution.");
        }

        if (state.PendingDecision is not null)
        {
            throw new ResolutionFailureException("A new resolution cannot begin while a decision is pending.");
        }

        if (frame is BehaviorResolutionFrame)
        {
            throw new ResolutionFailureException(
                "Generic card behaviors may only be opened by an engine-owned card play/install invocation so source-zone and invocation provenance are validated.");
        }

        if (frame is InstallFrame)
        {
            throw new ResolutionFailureException(
                "Install frames may only be opened by an engine-issued install action in the active Runner turn.");
        }

        if (maxResolutionSteps <= 0)
        {
            throw new ResolutionStepBudgetExceededException(maxResolutionSteps);
        }

        if (frame is EffectResolutionFrame { Effects: { } effects }
            && effects.Instructions.Any(static instruction => instruction is TurnBoundaryEffect))
        {
            throw new ResolutionFailureException(
                "Turn-boundary occurrences may only be opened by the active turn frame.");
        }

        if (frame is EffectResolutionFrame { Effects: { } basicDrawEffects }
            && basicDrawEffects.Instructions.Any(static instruction => instruction is BasicDrawEffect))
        {
            throw new ResolutionFailureException(
                "BasicDrawEffect may only be opened by the real click-for-card turn action.");
        }

        var working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        try
        {
            // A cost checkpoint is a first-class serialized boundary. Allow
            // the same engine-owned entry point to resume the exact top
            // AbilityFrame after save/load instead of appending a duplicate
            // cursor beneath it. Other frame types retain the original
            // begin-only semantics.
            bool resumeAbility = frame is AbilityFrame ability
                && working.ResolutionStack.Count > 0
                && working.ResolutionStack[^1] is AbilityFrame activeAbility
                && AbilityFramesMatch(activeAbility, ability);
            if (!resumeAbility)
            {
                working.ResolutionStack.Add(ResolutionFrameCloner.Copy(frame));
            }

            Advance(working, maxResolutionSteps);
            GameState next = working.Build(state);
            StateInvariantValidator.Validate(
                next,
                enforceCatalogCompatibility ? behaviorCatalog : null,
                runtimeCardCatalog);
            return next;
        }
        catch (ResolutionFailureException)
        {
            throw;
        }
        catch (Exception exception) when (exception is StateInvariantException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new ResolutionFailureException(
                $"The resolution frame could not be started: {exception.Message}",
                exception);
        }
    }

    public static GameState StartResolution(
        GameState state,
        ResolutionFrame frame,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginResolution(state, frame, maxResolutionSteps);

    /// <summary>
    /// Starts the CR 5 turn shell for the already active side. The returned
    /// state is paused at the first required paid-ability window (or its
    /// corresponding decision), so the mandatory draw and formal turn-begin
    /// occurrence cannot be skipped by a caller.
    /// </summary>
    public static GameState BeginTurn(
        GameState state,
        PlayerSide side,
        int maxResolutionSteps = DefaultResolutionStepBudget)
        => BeginTurnWithEvents(state, side, maxResolutionSteps).State;

    /// <summary>Starts a turn for the side already recorded as active.</summary>
    public static GameState BeginTurn(
        GameState state,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginTurn(state, state.Turn.ActiveSide, maxResolutionSteps);

    public static GameState StartTurn(
        GameState state,
        PlayerSide side,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginTurn(state, side, maxResolutionSteps);

    /// <summary>Starts a turn with an explicitly composed behavior catalog.</summary>
    public static GameState BeginTurn(
        GameState state,
        PlayerSide side,
        IBehaviorCatalog behaviorCatalog,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginTurnWithEvents(state, side, behaviorCatalog, maxResolutionSteps).State;

    /// <summary>Starts a turn with both injected immutable catalogs.</summary>
    public static GameState BeginTurn(
        GameState state,
        PlayerSide side,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginTurnWithEvents(state, side, behaviorCatalog, runtimeCardCatalog, maxResolutionSteps).State;

    /// <summary>
    /// Starts a turn and returns the events emitted while opening its first
    /// decision.  This mirrors <see cref="BeginRunWithEvents"/> and removes
    /// the need for callers to reconstruct setup events from state fields.
    /// </summary>
    public static TurnStartResult BeginTurnWithEvents(
        GameState state,
        PlayerSide side,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginTurnWithEvents(
            state,
            side,
            BehaviorCatalog.Empty,
            runtimeCardCatalog: null,
            maxResolutionSteps: maxResolutionSteps,
            enforceCatalogCompatibility: false);

    /// <summary>Starts a turn for the side already recorded as active.</summary>
    public static TurnStartResult BeginTurnWithEvents(
        GameState state,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginTurnWithEvents(state, state.Turn.ActiveSide, maxResolutionSteps);

    /// <summary>Starts a turn with an explicitly injected immutable catalog.</summary>
    public static TurnStartResult BeginTurnWithEvents(
        GameState state,
        PlayerSide side,
        IBehaviorCatalog behaviorCatalog,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginTurnWithEvents(
            state,
            side,
            behaviorCatalog,
            runtimeCardCatalog: null,
            maxResolutionSteps: maxResolutionSteps,
            enforceCatalogCompatibility: true);

    /// <summary>Starts a turn with both injected immutable catalogs.</summary>
    public static TurnStartResult BeginTurnWithEvents(
        GameState state,
        PlayerSide side,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog runtimeCardCatalog,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginTurnWithEvents(
            state,
            side,
            behaviorCatalog,
            runtimeCardCatalog,
            maxResolutionSteps,
            enforceCatalogCompatibility: true);

    /// <summary>Starts the active side's turn with an injected catalog.</summary>
    public static TurnStartResult BeginTurnWithEvents(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        int maxResolutionSteps = DefaultResolutionStepBudget) =>
        BeginTurnWithEvents(state, state.Turn.ActiveSide, behaviorCatalog, maxResolutionSteps);

    /// <summary>Parameter-order adapter for an active-side injected turn.</summary>
    public static TurnStartResult BeginTurnWithEvents(
        GameState state,
        int maxResolutionSteps,
        IBehaviorCatalog behaviorCatalog) =>
        BeginTurnWithEvents(state, behaviorCatalog, maxResolutionSteps);

    /// <summary>Parameter-order adapter for callers that append dependencies.</summary>
    public static TurnStartResult BeginTurnWithEvents(
        GameState state,
        PlayerSide side,
        int maxResolutionSteps,
        IBehaviorCatalog behaviorCatalog) =>
        BeginTurnWithEvents(state, side, behaviorCatalog, maxResolutionSteps);

    private static TurnStartResult BeginTurnWithEvents(
        GameState state,
        PlayerSide side,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        int maxResolutionSteps,
        bool enforceCatalogCompatibility)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);
        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown turn side.");
        }

        if (state.Turn.ActiveSide != side)
        {
            throw new ResolutionFailureException("A turn can only begin for the currently active side.");
        }

        if (state.ResolutionStack.Count > 0)
        {
            throw new ResolutionFailureException("A turn cannot begin while another resolution is active.");
        }

        return BeginTurnWithEventsCore(
            state,
            new TurnFrame(side, state.Turn.TurnNumber),
            behaviorCatalog,
            runtimeCardCatalog,
            enforceCatalogCompatibility,
            maxResolutionSteps);
    }

    private static TurnStartResult BeginTurnWithEventsCore(
        GameState state,
        ResolutionFrame frame,
        IBehaviorCatalog behaviorCatalog,
        IRuntimeCardCatalog? runtimeCardCatalog,
        bool enforceCatalogCompatibility,
        int maxResolutionSteps)
    {
        if (enforceCatalogCompatibility
            && !CatalogMatchesState(state, behaviorCatalog, out string? catalogMismatch))
        {
            throw new ResolutionFailureException(catalogMismatch!);
        }

        if (runtimeCardCatalog is not null
            && !CatalogMatchesState(state, runtimeCardCatalog, out string? runtimeMismatch))
        {
            throw new ResolutionFailureException(runtimeMismatch!);
        }

        StateInvariantValidator.Validate(
            state,
            enforceCatalogCompatibility ? behaviorCatalog : null,
            runtimeCardCatalog);

        if (state.Outcome.Kind == GameOutcomeKind.Won)
        {
            throw new ResolutionFailureException("A terminal state cannot begin a turn.");
        }

        if (state.PendingDecision is not null)
        {
            throw new ResolutionFailureException("A turn cannot begin while a decision is pending.");
        }

        if (state.ResolutionStack.Count > 0)
        {
            throw new ResolutionFailureException("A turn cannot begin while another resolution is active.");
        }

        if (maxResolutionSteps <= 0)
        {
            throw new ResolutionStepBudgetExceededException(maxResolutionSteps);
        }

        var working = WorkingState.From(state, behaviorCatalog, runtimeCardCatalog);
        try
        {
            working.ResolutionStack.Add(ResolutionFrameCloner.Copy(frame));
            Advance(working, maxResolutionSteps);
            GameState next = working.Build(state);
            StateInvariantValidator.Validate(
                next,
                enforceCatalogCompatibility ? behaviorCatalog : null,
                runtimeCardCatalog);
            return new TurnStartResult(next, FreezeEvents(working.Events));
        }
        catch (ResolutionFailureException)
        {
            throw;
        }
        catch (Exception exception) when (exception is StateInvariantException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new ResolutionFailureException(
                $"The turn could not be started: {exception.Message}",
                exception);
        }
    }

    private static void ConsumeAction(
        WorkingState working,
        LegalAction action,
        PlayerId? decisionOwner = null)
    {
        RecordActionStarted(working, action);
        switch (action.Kind, action.Parameters)
        {
            case (ActionKind.ChooseCard, CardChoiceParameters discardChoice)
                when IsTurnDiscardFrameTop(working):
                ConsumeTurnDiscardAction(working, discardChoice.CardId);
                return;
            case (ActionKind.ChooseNumber, NumberChoiceParameters numberChoice):
                ConsumeNumberAction(working, numberChoice.Value);
                return;
            case (ActionKind.ChooseCard, CardChoiceParameters cardChoice):
                ConsumeCardAction(working, cardChoice.CardId);
                return;
            case (ActionKind.ChooseBehavior, BehaviorChoiceParameters behaviorChoice):
                ConsumeBehaviorChoice(working, behaviorChoice);
                return;
            case (ActionKind.ResolveIceSubroutine, IceSubroutineChoiceParameters iceSubroutineChoice):
                ConsumeIceSubroutineChoice(working, iceSubroutineChoice);
                return;
            case (ActionKind.UseAbility, AbilityActionParameters ability):
                ConsumeAbilityAction(working, ability, decisionOwner);
                return;
            case (ActionKind.InstallCard, BehaviorInstallChoiceParameters behaviorInstall):
                ConsumeBehaviorInstallChoice(working, behaviorInstall);
                return;
            case (ActionKind.InstallCard, InstallCardParameters install)
                when IsInstallFrameTop(working):
                ConsumeInstallChoiceAction(working, install);
                return;
            case (ActionKind.InstallCard, InstallCardParameters install):
                ConsumeTurnInstallAction(working, install);
                return;
            case (ActionKind.PlayCard, PlayCardParameters playCard):
                ConsumeTurnPlayAction(working, playCard);
                return;
            case (ActionKind.StartRun, RunServerParameters runServer):
                ConsumeTurnStartRunAction(working, runServer);
                return;
            case (ActionKind.AdvanceCard, AdvanceCardParameters advanceCard):
                ConsumeTurnAdvanceCardAction(working, advanceCard);
                return;
            case (ActionKind.RemoveTag, RemoveTagParameters removeTag):
                ConsumeRemoveTagAction(working, removeTag);
                return;
            case (ActionKind.TrashTaggedResource, TrashTaggedResourceParameters trashTaggedResource):
                ConsumeTrashTaggedResourceAction(working, trashTaggedResource);
                return;
            case (ActionKind.PurgeVirusCounters, PurgeVirusCountersParameters purgeVirusCounters):
                ConsumePurgeVirusCountersAction(working, purgeVirusCounters);
                return;
            case (ActionKind.ScoreAgenda, ScoreAgendaParameters scoreAgenda):
                ConsumeTurnScoreAgendaAction(working, scoreAgenda);
                return;
            case (ActionKind.RezCard, RezCardParameters rezCard):
                ConsumeRezCardAction(working, rezCard);
                return;
            case (ActionKind.GainCredit, NoActionParameters):
            case (ActionKind.DrawCard, NoActionParameters):
            case (ActionKind.EndTurn, NoActionParameters):
                if (IsTurnFrameTop(working))
                {
                    ConsumeTurnAction(working, action.Kind);
                    return;
                }

                ConsumeToyAction(working, action.Kind, null);
                return;
            case (ActionKind.InstallCard, CardChoiceParameters installChoice):
                ConsumeToyAction(working, action.Kind, installChoice.CardId);
                return;
            case (ActionKind.AdvanceAgenda, CardChoiceParameters advanceChoice):
                ConsumeToyAction(working, action.Kind, advanceChoice.CardId);
                return;
            case (ActionKind.ScoreAgenda, CardChoiceParameters scoreChoice):
                ConsumeToyAction(working, action.Kind, scoreChoice.CardId);
                return;
            case (ActionKind.StealAgenda, CardChoiceParameters stealChoice):
                ConsumeToyAction(working, action.Kind, stealChoice.CardId);
                return;
            case (ActionKind.PassPriority, NoActionParameters):
                if (IsTurnFrameTop(working))
                {
                    ConsumeTurnPriorityAction(working);
                    return;
                }

                ConsumeRunAction(working, action.Kind, action.Parameters);
                return;
            case (ActionKind.RezIce, RunIceParameters runIce):
            case (ActionKind.BreakSubroutines, RunBreakParameters):
            case (ActionKind.PumpBreaker, RunBreakerParameters):
            case (ActionKind.UseBioroidAbility, RunBioroidAbilityParameters):
            case (ActionKind.JackOut, NoActionParameters):
            case (ActionKind.ContinueRun, NoActionParameters):
            case (ActionKind.ChooseBreachCandidate, BreachCandidateParameters):
            case (ActionKind.TrashAccessedCard, RunCardPaymentParameters):
            case (ActionKind.StealAgenda, RunCardPaymentParameters):
            case (ActionKind.NoAccessAction, NoActionParameters):
                ConsumeRunAction(working, action.Kind, action.Parameters);
                return;
            case (ActionKind.TrashProgram, CardChoiceParameters trashProgram):
                ConsumeMemoryCleanupAction(working, trashProgram.CardId);
                return;
            case (ActionKind.UseTrigger, TriggerChoiceParameters useTrigger):
                ConsumeTriggerChoice(working, ActionKind.UseTrigger, useTrigger.TriggerId);
                return;
            case (ActionKind.DeclineTrigger, TriggerChoiceParameters declineTrigger):
                ConsumeTriggerChoice(working, ActionKind.DeclineTrigger, declineTrigger.TriggerId);
                return;
            case (ActionKind.OrderTriggers, TriggerOrderParameters orderTriggers):
                ConsumeTriggerOrdering(working, orderTriggers.TriggerId);
                return;
            case (ActionKind.UsePrevention, PreventionChoiceParameters usePrevention):
                ConsumePreventionChoice(working, usePrevention.InterruptId, usePrevention.Amount);
                return;
            case (ActionKind.DeclinePrevention, PreventionChoiceParameters declinePrevention):
                ConsumeInterruptDecline(working, ActionKind.DeclinePrevention, declinePrevention.InterruptId);
                return;
            case (ActionKind.UseReplacement, ReplacementChoiceParameters useReplacement):
                ConsumeReplacementChoice(working, useReplacement.InterruptId);
                return;
            case (ActionKind.DeclineReplacement, ReplacementChoiceParameters declineReplacement):
                ConsumeInterruptDecline(working, ActionKind.DeclineReplacement, declineReplacement.InterruptId);
                return;
            default:
                throw new ResolutionFailureException($"Action kind '{action.Kind}' has no registered resolver.");
        }
    }

    private static void ConsumeNumberAction(WorkingState working, int value)
    {
        if (working.ResolutionStack.Count == 0)
        {
            throw new ResolutionFailureException("The submitted number action does not match the top resolution frame.");
        }

        if (working.ResolutionStack[^1] is SetupFrame setup)
        {
            ConsumeSetupChoice(working, setup, value);
            return;
        }

        if (working.ResolutionStack[^1] is ChooseNumberThenAdjustCreditsFrame outerFrame)
        {
            if (outerFrame.Stage != NestedNumberStage.OuterPrompt)
            {
                throw new ResolutionFailureException("An inner number prompt is required before the nested parent can resume.");
            }

            if (value < outerFrame.Minimum || value > outerFrame.Maximum)
            {
                throw new ResolutionFailureException($"Number action value {value} is outside the legal range.");
            }

            working.ResolutionStack[^1] = new ChooseNumberThenAdjustCreditsFrame(
                outerFrame.Version,
                outerFrame.BehaviorId,
                outerFrame.Owner,
                outerFrame.Target,
                outerFrame.Minimum,
                outerFrame.Maximum,
                outerFrame.CreditsPerChoice,
                NestedNumberStage.InnerPrompt,
                value);
            working.ResolutionStack.Add(new ChooseNumberFrame(
                outerFrame.Owner,
                outerFrame.Minimum,
                outerFrame.Maximum));
            return;
        }

        if (working.ResolutionStack[^1] is not ChooseNumberFrame chooseNumber)
        {
            throw new ResolutionFailureException("The submitted number action does not match the top resolution frame.");
        }

        if (value < chooseNumber.Minimum || value > chooseNumber.Maximum)
        {
            throw new ResolutionFailureException($"Number action value {value} is outside the legal range.");
        }

        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);

        if (working.ResolutionStack.Count > 0
            && working.ResolutionStack[^1] is ChooseNumberThenAdjustCreditsFrame parent)
        {
            if (parent.Stage != NestedNumberStage.InnerPrompt
                || !parent.ChosenOuterValue.HasValue
                || parent.Owner != chooseNumber.Owner
                || parent.Minimum != chooseNumber.Minimum
                || parent.Maximum != chooseNumber.Maximum
                || chooseNumber.Continuation != ResolutionContinuation.Complete
                || chooseNumber.AdjustmentTarget.HasValue
                || chooseNumber.CreditsPerChoice != 0)
            {
                throw new ResolutionFailureException("The nested number prompt does not match its parent frame.");
            }

            working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
            long nestedDelta = checked((long)parent.ChosenOuterValue.Value * value);
            nestedDelta = checked(nestedDelta * parent.CreditsPerChoice);
            if (nestedDelta is < int.MinValue or > int.MaxValue)
            {
                throw new ResolutionFailureException($"Credit adjustment {nestedDelta} does not fit in an Int32.");
            }

            working.ResolutionStack.Add(new AdjustCreditsFrame(parent.Target, (int)nestedDelta));
            return;
        }

        if (chooseNumber.Continuation != ResolutionContinuation.AdjustCredits)
        {
            return;
        }

        PlayerId target = chooseNumber.AdjustmentTarget
            ?? throw new ResolutionFailureException("The credit-adjustment continuation has no target player.");
        int creditsPerChoice = chooseNumber.CreditsPerChoice;

        long delta = checked((long)value * creditsPerChoice);
        if (delta is < int.MinValue or > int.MaxValue)
        {
            throw new ResolutionFailureException($"Credit adjustment {delta} does not fit in an Int32.");
        }

        working.ResolutionStack.Add(new AdjustCreditsFrame(target, (int)delta));
    }

    private static void ConsumeCardAction(WorkingState working, CardInstanceId cardId)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not ChooseCardFrame chooseCard)
        {
            throw new ResolutionFailureException("The submitted card action does not match the top resolution frame.");
        }

        if (!chooseCard.CardOptions.Contains(cardId))
        {
            throw new ResolutionFailureException($"Card action '{cardId}' was not offered by the decision.");
        }

        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
    }

    private static bool IsResolutionFailure(Exception exception) =>
        exception is ResolutionFailureException
        or StateInvariantException
        or ArgumentException
        or InvalidOperationException
        or OverflowException;

}

public class ResolutionFailureException : InvalidOperationException
{
    public ResolutionFailureException(string message)
        : base(message)
    {
    }

    public ResolutionFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ResolutionStepBudgetExceededException : ResolutionFailureException
{
    public ResolutionStepBudgetExceededException(int budget)
        : base($"Resolution exceeded its hard step budget of {budget}.")
    {
        Budget = budget;
    }

    public int Budget { get; }
}
