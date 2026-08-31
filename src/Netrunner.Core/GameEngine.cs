namespace Netrunner.Core;

/// <summary>
/// Deterministic reducer boundary for the M1 engine primitives. The reducer
/// never mutates the supplied state: a working copy owns all lists, the ID
/// allocator, the RNG cursor, and the event buffer until a prospective state
/// has passed invariant validation.
/// </summary>
public static partial class GameEngine
{
    public const int DefaultResolutionStepBudget = 128;

    /// <summary>Creates an engine session bound to one immutable catalog.</summary>
    public static GameEngineSession CreateSession(IBehaviorCatalog behaviorCatalog) =>
        new(behaviorCatalog);

    public static ApplyResult Apply(GameState state, ActionSubmission? submission)
    {
        return ApplyCore(
            state,
            submission,
            CardBehaviorSpecCatalog.Default,
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
            enforceCatalogCompatibility: true,
            maxResolutionSteps: DefaultResolutionStepBudget);
    }

    /// <summary>Parameter-order adapter for composition callers.</summary>
    public static ApplyResult Apply(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        ActionSubmission? submission) =>
        Apply(state, submission, behaviorCatalog);

    private static ApplyResult ApplyCore(
        GameState state,
        ActionSubmission? submission,
        IBehaviorCatalog behaviorCatalog,
        bool enforceCatalogCompatibility,
        int maxResolutionSteps)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(behaviorCatalog);

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

        if (!StateInvariantValidator.TryValidate(
                state,
                enforceCatalogCompatibility ? behaviorCatalog : null,
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

        var working = WorkingState.From(state, behaviorCatalog);
        try
        {
            working.PendingDecision = null;
            ConsumeAction(working, action);
            Advance(working, maxResolutionSteps);
            working.ActionSequence = NextSequence(working.ActionSequence, "action");

            GameState next = working.Build(state);
            StateInvariantValidator.Validate(
                next,
                enforceCatalogCompatibility ? behaviorCatalog : null);
            IReadOnlyList<EngineEvent> events = FreezeEvents(working.Events);
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
            CardBehaviorSpecCatalog.Default,
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

        StateInvariantValidator.Validate(
            state,
            enforceCatalogCompatibility ? behaviorCatalog : null);

        if (state.Outcome.Kind == GameOutcomeKind.Won)
        {
            throw new ResolutionFailureException("A terminal state cannot begin a new resolution.");
        }

        if (state.PendingDecision is not null)
        {
            throw new ResolutionFailureException("A new resolution cannot begin while a decision is pending.");
        }

        if (maxResolutionSteps <= 0)
        {
            throw new ResolutionStepBudgetExceededException(maxResolutionSteps);
        }

        var working = WorkingState.From(state, behaviorCatalog);
        try
        {
            working.ResolutionStack.Add(ResolutionFrameCloner.Copy(frame));
            Advance(working, maxResolutionSteps);
            GameState next = working.Build(state);
            StateInvariantValidator.Validate(
                next,
                enforceCatalogCompatibility ? behaviorCatalog : null);
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

    private static void ConsumeAction(WorkingState working, LegalAction action)
    {
        switch (action.Kind, action.Parameters)
        {
            case (ActionKind.ChooseNumber, NumberChoiceParameters numberChoice):
                ConsumeNumberAction(working, numberChoice.Value);
                return;
            case (ActionKind.ChooseCard, CardChoiceParameters cardChoice):
                ConsumeCardAction(working, cardChoice.CardId);
                return;
            case (ActionKind.GainCredit, NoActionParameters):
            case (ActionKind.DrawCard, NoActionParameters):
            case (ActionKind.EndTurn, NoActionParameters):
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
            case (ActionKind.RezIce, RunIceParameters runIce):
            case (ActionKind.BreakSubroutines, RunBreakParameters):
            case (ActionKind.PumpBreaker, RunBreakerParameters):
            case (ActionKind.JackOut, NoActionParameters):
            case (ActionKind.ContinueRun, NoActionParameters):
            case (ActionKind.ChooseBreachCandidate, BreachCandidateParameters):
            case (ActionKind.TrashAccessedCard, RunCardPaymentParameters):
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

    private static void MoveCard(
        WorkingState working,
        CardInstanceState card,
        ZoneState source,
        ZoneState destination)
    {
        if (card.ZoneId != source.Id || !source.CardIds.Contains(card.Id))
        {
            throw new ResolutionFailureException($"Card '{card.Id}' is not in source zone '{source.Id}'.");
        }

        if (source.Id == destination.Id)
        {
            throw new ResolutionFailureException("A card move requires distinct source and destination zones.");
        }

        ReplaceZone(working, new ZoneState(
            source.Id,
            source.Kind,
            source.OwnerSide,
            source.ServerId,
            source.CardIds.Where(candidate => candidate != card.Id).ToArray()));
        ReplaceZone(working, new ZoneState(
            destination.Id,
            destination.Kind,
            destination.OwnerSide,
            destination.ServerId,
            destination.CardIds.Append(card.Id).ToArray()));
        // CR 26.03 4.4.6/8.6.7: cards in the Runner heap are public. Keep
        // this normalization at the single card-movement boundary so damage,
        // memory cleanup, explicit move effects, and run-event cleanup all
        // agree—even when a facedown Runner card enters the heap.
        bool entersRunnerHeap = destination.Kind == ZoneKind.RunnerHeap
            && destination.OwnerSide == PlayerSide.Runner;
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            destination.Id,
            card.OwnerSide,
            entersRunnerHeap || card.FaceUp,
            card.Rezzed,
            card.AdvancementCounters));
        Emit(working, EngineEventKinds.CardMoved, new CardMovedEventPayload(card.Id, source.Id, destination.Id));
    }

    private static void ReplacePlayer(WorkingState working, PlayerState player)
    {
        int index = working.Players.FindIndex(candidate => candidate.Id == player.Id);
        if (index < 0)
        {
            throw new ResolutionFailureException($"Player '{player.Id}' does not exist.");
        }

        working.Players[index] = player;
    }

    private static void ReplaceCard(WorkingState working, CardInstanceState card)
    {
        int index = working.Cards.FindIndex(candidate => candidate.Id == card.Id);
        if (index < 0)
        {
            throw new ResolutionFailureException($"Card '{card.Id}' does not exist.");
        }

        working.Cards[index] = card;
    }

    private static void ReplaceZone(WorkingState working, ZoneState zone)
    {
        int index = working.Zones.FindIndex(candidate => candidate.Id == zone.Id);
        if (index < 0)
        {
            throw new ResolutionFailureException($"Zone '{zone.Id}' does not exist.");
        }

        working.Zones[index] = zone;
    }

    private static PlayerState GetPlayer(WorkingState working, PlayerSide side) =>
        working.Players.SingleOrDefault(player => player.Side == side)
        ?? throw new ResolutionFailureException($"Player side '{side}' does not exist.");

    private static CardInstanceState GetCard(WorkingState working, CardInstanceId cardId) =>
        working.Cards.SingleOrDefault(card => card.Id == cardId)
        ?? throw new ResolutionFailureException($"Card '{cardId}' does not exist.");

    private static ZoneState GetZone(WorkingState working, ZoneKind kind) =>
        working.Zones.SingleOrDefault(zone => zone.Kind == kind && zone.ServerId is null)
        ?? throw new ResolutionFailureException($"Zone kind '{kind}' does not exist.");

    private static ZoneState GetRemoteRoot(WorkingState working) =>
        working.Servers
            .Where(server => server.Kind == ServerKind.Remote)
            .OrderBy(server => server.Id.Value, StringComparer.Ordinal)
            .Select(server => working.Zones.SingleOrDefault(zone => zone.Id == server.RootZoneId))
            .FirstOrDefault(zone => zone is not null)
        ?? throw new ResolutionFailureException("The fixture requires a Corp remote root zone.");

    private static void RequireClicks(PlayerState player, ActionKind kind)
    {
        if (player.Clicks <= 0)
        {
            throw new ResolutionFailureException($"Action '{kind}' requires at least one click.");
        }
    }

    private static void RequireNoToyTarget(CardInstanceId? target, ActionKind kind)
    {
        if (target.HasValue)
        {
            throw new ResolutionFailureException($"Action '{kind}' must not contain a card target.");
        }
    }

    private static void RequireToyTarget(CardInstanceId? target, ActionKind kind)
    {
        if (!target.HasValue)
        {
            throw new ResolutionFailureException($"Action '{kind}' requires a card target.");
        }
    }

    private static void Emit(WorkingState working, string kind, EngineEventPayload payload)
    {
        long sequence = NextSequence(working.EventSequence, "event");
        working.EventSequence = sequence;
        working.Events.Add(new EngineEvent(sequence, kind, payload));
    }

    private static void StepChooseNumberThenAdjustCredits(
        WorkingState working,
        ChooseNumberThenAdjustCreditsFrame frame)
    {
        if (frame.Stage != NestedNumberStage.OuterPrompt)
        {
            throw new ResolutionFailureException("An outer number prompt can only execute at the beginning of the nested fixture.");
        }

        YieldNumberDecision(
            working,
            frame.Owner,
            frame.Minimum,
            frame.Maximum,
            ResolutionContinuation.Complete,
            null,
            0);
    }

    private static void StepChooseNumber(WorkingState working, ChooseNumberFrame frame) =>
        YieldNumberDecision(
            working,
            frame.Owner,
            frame.Minimum,
            frame.Maximum,
            frame.Continuation,
            frame.AdjustmentTarget,
            frame.CreditsPerChoice);

    private static void StepChooseCard(WorkingState working, ChooseCardFrame frame) =>
        YieldCardDecision(working, frame.Owner, frame.CardOptions);

    private static void YieldNumberDecision(
        WorkingState working,
        PlayerId owner,
        int minimum,
        int maximum,
        ResolutionContinuation continuation,
        PlayerId? adjustmentTarget,
        int creditsPerChoice)
    {
        DecisionId decisionId = working.Allocator.NextDecisionId();
        var actions = new List<LegalAction>();
        for (long value = minimum; value <= maximum; value++)
        {
            int number = checked((int)value);
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.ChooseNumber,
                $"Choose {number}",
                new NumberChoiceParameters(number)));
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            decisionId,
            working.DecisionRevision,
            owner,
            DecisionKind.ChooseNumber,
            "Choose a number",
            actions);
    }

    private static void YieldCardDecision(
        WorkingState working,
        PlayerId owner,
        IReadOnlyList<CardInstanceId> cardOptions)
    {
        DecisionId decisionId = working.Allocator.NextDecisionId();
        var actions = new List<LegalAction>(cardOptions.Count);
        foreach (CardInstanceId cardId in cardOptions)
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.ChooseCard,
                $"Choose card {cardId}",
                new CardChoiceParameters(cardId)));
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            decisionId,
            working.DecisionRevision,
            owner,
            DecisionKind.ChooseCard,
            "Choose a card",
            actions);
    }

    private static long NextRevision(long current) =>
        current == long.MaxValue
            ? throw new ResolutionFailureException("The decision revision counter is exhausted.")
            : checked(current + 1);

    private static long NextSequence(long current, string counterName) =>
        current == long.MaxValue
            ? throw new ResolutionFailureException($"The {counterName} sequence counter is exhausted.")
            : checked(current + 1);

    private static void ApplyCreditDelta(WorkingState working, PlayerId target, int delta)
    {
        int index = working.Players.FindIndex(player => player.Id == target);
        if (index < 0)
        {
            throw new ResolutionFailureException($"Credit adjustment target '{target}' does not exist.");
        }

        PlayerState player = working.Players[index];
        long nextCredits = checked((long)player.Credits + delta);
        if (nextCredits is < 0 or > int.MaxValue)
        {
            throw new ResolutionFailureException($"Credit adjustment would produce invalid credits {nextCredits} for '{target}'.");
        }

        working.Players[index] = new PlayerState(
            player.Id,
            player.Side,
            (int)nextCredits,
            player.Clicks,
            player.AgendaPoints);

        long sequence = checked(working.EventSequence + 1);
        working.EventSequence = sequence;
        working.Events.Add(new EngineEvent(
            sequence,
            EngineEventKinds.CreditsAdjusted,
            new CreditsAdjustedEventPayload(target, delta, (int)nextCredits)));
    }

    private static bool IsResolutionFailure(Exception exception) =>
        exception is ResolutionFailureException
        or StateInvariantException
        or ArgumentException
        or InvalidOperationException
        or OverflowException;

    private static IReadOnlyList<EngineEvent> FreezeEvents(IEnumerable<EngineEvent> events) =>
        EngineEventValidator.FreezeBatch(events);

    private static string FormatViolations(IReadOnlyList<StateInvariantViolation> violations) =>
        string.Join("; ", violations.Select(static violation => violation.ToString()));

    private static bool CatalogMatchesState(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        out string? mismatch)
    {
        if (!string.Equals(state.BehaviorRegistryVersion, behaviorCatalog.Version, StringComparison.Ordinal))
        {
            mismatch =
                $"Behavior registry version '{state.BehaviorRegistryVersion}' does not match the injected catalog version '{behaviorCatalog.Version}'.";
            return false;
        }

        if (state.BehaviorCatalogFingerprint is not null
            && !string.Equals(state.BehaviorCatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            mismatch =
                $"Behavior catalog fingerprint '{state.BehaviorCatalogFingerprint}' does not match the injected catalog fingerprint '{behaviorCatalog.Fingerprint}'.";
            return false;
        }

        mismatch = null;
        return true;
    }

    private sealed class WorkingState
    {
        private WorkingState(GameState source, IBehaviorCatalog behaviorCatalog)
        {
            BehaviorCatalog = behaviorCatalog;
            Players = source.Players.ToList();
            Cards = source.Cards.ToList();
            Zones = source.Zones.ToList();
            Servers = source.Servers.ToList();
            InitialSeed = source.InitialSeed;
            RandomState = source.RandomState;
            Allocator = new StableIdAllocator(source.Allocator);
            Turn = new TurnState(source.Turn.ActiveSide, source.Turn.Phase, source.Turn.TurnNumber);
            Outcome = new GameOutcome(source.Outcome.Kind, source.Outcome.Winner, source.Outcome.Reason);
            DecisionRevision = source.DecisionRevision;
            EventSequence = source.EventSequence;
            ActionSequence = source.ActionSequence;
            ResolutionStack = source.ResolutionStack.Select(ResolutionFrameCloner.Copy).ToList();
            PendingDecision = source.PendingDecision?.Copy();
            RunState = source.RunState?.Copy();
            RulesState = source.RulesState?.Copy();
            Events = new List<EngineEvent>();
        }

        public List<PlayerState> Players { get; }

        /// <summary>Immutable catalog captured for this atomic reduction.</summary>
        public IBehaviorCatalog BehaviorCatalog { get; }

        public List<CardInstanceState> Cards { get; }

        public List<ZoneState> Zones { get; }

        public List<ServerState> Servers { get; }

        public ulong InitialSeed { get; }

        public ulong RandomState { get; set; }

        public StableIdAllocator Allocator { get; }

        public TurnState Turn { get; set; }

        public GameOutcome Outcome { get; set; }

        public long DecisionRevision { get; set; }

        public long EventSequence { get; set; }

        public long ActionSequence { get; set; }

        public List<ResolutionFrame> ResolutionStack { get; }

        public PendingDecision? PendingDecision { get; set; }

        public RunState? RunState { get; set; }

        public RulesState? RulesState { get; set; }

        public List<EngineEvent> Events { get; }

        public static WorkingState From(GameState source, IBehaviorCatalog? behaviorCatalog = null) =>
            new(source, behaviorCatalog ?? CardBehaviorSpecCatalog.Default);

        public GameState Build(GameState source) => new(
            source.SchemaVersion,
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            Players,
            Cards,
            Zones,
            Servers,
            Turn,
            Outcome,
            RandomState,
            Allocator.State,
            DecisionRevision,
            EventSequence,
            ResolutionStack,
            PendingDecision,
            ActionSequence,
            InitialSeed,
            RunState,
            RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint);
    }
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
