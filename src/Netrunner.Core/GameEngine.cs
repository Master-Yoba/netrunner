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

    public static ApplyResult Apply(GameState state, ActionSubmission? submission)
    {
        return ApplyCore(state, submission, DefaultResolutionStepBudget);
    }

    private static ApplyResult ApplyCore(
        GameState state,
        ActionSubmission? submission,
        int maxResolutionSteps)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (submission is null)
        {
            return ApplyResult.Reject(
                state,
                ApplyRejectionCode.InvalidInputState,
                "An action submission is required.");
        }

        if (!StateInvariantValidator.TryValidate(state, out IReadOnlyList<StateInvariantViolation> violations))
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

        var working = WorkingState.From(state);
        try
        {
            working.PendingDecision = null;
            ConsumeAction(working, action);
            Advance(working, maxResolutionSteps);
            working.ActionSequence = NextSequence(working.ActionSequence, "action");

            GameState next = working.Build(state);
            StateInvariantValidator.Validate(next);
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
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(frame);
        StateInvariantValidator.Validate(state);

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

        var working = WorkingState.From(state);
        try
        {
            working.ResolutionStack.Add(ResolutionFrameCloner.Copy(frame));
            Advance(working, maxResolutionSteps);
            GameState next = working.Build(state);
            StateInvariantValidator.Validate(next);
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

    private static void Advance(WorkingState working, int maxResolutionSteps)
    {
        int steps = 0;
        while (working.PendingDecision is null && working.ResolutionStack.Count > 0)
        {
            if (steps >= maxResolutionSteps)
            {
                throw new ResolutionStepBudgetExceededException(maxResolutionSteps);
            }

            steps++;
            ResolutionFrame frame = working.ResolutionStack[^1]
                ?? throw new ResolutionFailureException("The resolution stack contained a null frame.");

            IReadOnlyList<StateInvariantViolation> frameViolations = ResolutionFrameValidator.Collect(frame);
            if (frameViolations.Count > 0)
            {
                throw new ResolutionFailureException(FormatViolations(frameViolations));
            }

            switch (frame)
            {
                case ChooseNumberThenAdjustCreditsFrame chooseThenAdjust:
                    StepChooseNumberThenAdjustCredits(working, chooseThenAdjust);
                    break;
                case ChooseNumberFrame chooseNumber:
                    StepChooseNumber(working, chooseNumber);
                    break;
                case ChooseCardFrame chooseCard:
                    StepChooseCard(working, chooseCard);
                    break;
                case AdjustCreditsFrame adjustCredits:
                    working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
                    ApplyCreditDelta(working, adjustCredits.Target, adjustCredits.Delta);
                    break;
                case ToyTurnFrame toyTurn:
                    StepToyTurn(working, toyTurn);
                    break;
                case RunTimingFrame runTiming:
                    StepRunTiming(working, runTiming);
                    break;
                case EffectResolutionFrame effectResolution:
                    StepEffectResolution(working, effectResolution);
                    break;
                case EffectCheckpointFrame effectCheckpoint:
                    StepEffectCheckpoint(working, effectCheckpoint);
                    break;
                case MemoryCleanupFrame memoryCleanup:
                    StepMemoryCleanup(working, memoryCleanup);
                    break;
                default:
                    throw new ResolutionFailureException(
                        $"No resolution behavior is registered for frame type '{frame.GetType().Name}'.");
            }
        }
    }

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
            case (ActionKind.JackOut, NoActionParameters):
            case (ActionKind.ContinueRun, NoActionParameters):
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

    private static void ConsumeToyAction(
        WorkingState working,
        ActionKind kind,
        CardInstanceId? targetCardId)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not ToyTurnFrame frame
            || frame.Stage != ToyTurnStage.AwaitingAction
            || working.Turn.ActiveSide != frame.ActiveSide)
        {
            throw new ResolutionFailureException("The submitted toy action does not match the active toy-turn frame.");
        }

        PlayerSide side = frame.ActiveSide;
        PlayerState player = working.Players.SingleOrDefault(candidate => candidate.Side == side)
            ?? throw new ResolutionFailureException($"The active '{side}' player does not exist.");

        switch (kind)
        {
            case ActionKind.GainCredit:
                RequireNoToyTarget(targetCardId, kind);
                RequireClicks(player, kind);
                ApplyCreditDelta(working, player.Id, 1);
                SpendClick(working, player.Id);
                break;
            case ActionKind.DrawCard:
                RequireNoToyTarget(targetCardId, kind);
                RequireClicks(player, kind);
                SpendClick(working, player.Id);
                if (!TryDrawCard(working, side))
                {
                    if (side == PlayerSide.Corp)
                    {
                        CompleteToyGame(working, PlayerSide.Runner, GameWinReason.DeckOut);
                    }
                    else
                    {
                        throw new ResolutionFailureException("The Runner cannot draw from an empty stack.");
                    }

                    return;
                }
                break;
            case ActionKind.InstallCard:
                RequireToyTarget(targetCardId, kind);
                RequireClicks(player, kind);
                InstallToyCard(working, side, targetCardId.GetValueOrDefault());
                SpendClick(working, player.Id);
                break;
            case ActionKind.AdvanceAgenda:
                RequireToyTarget(targetCardId, kind);
                RequireClicks(player, kind);
                SpendClick(working, player.Id);
                AdvanceToyAgenda(working, targetCardId.GetValueOrDefault());
                break;
            case ActionKind.ScoreAgenda:
                RequireToyTarget(targetCardId, kind);
                ScoreToyAgenda(working, targetCardId.GetValueOrDefault());
                break;
            case ActionKind.StealAgenda:
                RequireToyTarget(targetCardId, kind);
                RequireClicks(player, kind);
                SpendClick(working, player.Id);
                StealToyAgenda(working, targetCardId.GetValueOrDefault());
                break;
            case ActionKind.EndTurn:
                RequireNoToyTarget(targetCardId, kind);
                EndToyTurn(working, side);
                return;
            default:
                throw new ResolutionFailureException($"Action kind '{kind}' is not a toy-turn action.");
        }
    }

    private static void StepToyTurn(WorkingState working, ToyTurnFrame frame)
    {
        if (working.Turn.ActiveSide != frame.ActiveSide)
        {
            throw new ResolutionFailureException("Toy-turn frame side does not match the active turn side.");
        }

        switch (frame.Stage)
        {
            case ToyTurnStage.CorpTurnStartDraw:
                if (frame.ActiveSide != PlayerSide.Corp)
                {
                    throw new ResolutionFailureException("Only the Corp may have a mandatory draw stage.");
                }

                PlayerState corp = GetPlayer(working, PlayerSide.Corp);
                Emit(working, EngineEventKinds.TurnStarted, new TurnStartedEventPayload(
                    PlayerSide.Corp,
                    working.Turn.TurnNumber,
                    corp.Clicks));
                if (!TryDrawCard(working, PlayerSide.Corp))
                {
                    CompleteToyGame(working, PlayerSide.Runner, GameWinReason.DeckOut);
                    return;
                }

                working.ResolutionStack[^1] = new ToyTurnFrame(
                    1,
                    ResolutionBehaviorIds.ToyTurn,
                    PlayerSide.Corp,
                    ToyTurnStage.AwaitingAction);
                break;
            case ToyTurnStage.RunnerTurnStart:
                if (frame.ActiveSide != PlayerSide.Runner)
                {
                    throw new ResolutionFailureException("Only the Runner may have a Runner turn-start stage.");
                }

                PlayerState runner = GetPlayer(working, PlayerSide.Runner);
                Emit(working, EngineEventKinds.TurnStarted, new TurnStartedEventPayload(
                    PlayerSide.Runner,
                    working.Turn.TurnNumber,
                    runner.Clicks));
                working.ResolutionStack[^1] = new ToyTurnFrame(
                    1,
                    ResolutionBehaviorIds.ToyTurn,
                    PlayerSide.Runner,
                    ToyTurnStage.AwaitingAction);
                break;
            case ToyTurnStage.AwaitingAction:
                YieldToyDecision(working, frame.ActiveSide);
                break;
            default:
                throw new ResolutionFailureException($"Unsupported toy-turn stage '{frame.Stage}'.");
        }
    }

    private static void YieldToyDecision(WorkingState working, PlayerSide side)
    {
        PlayerState player = GetPlayer(working, side);
        ZoneState hand = GetZone(working, side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip);
        ZoneState stack = GetZone(working, side == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack);
        ZoneState remoteRoot = GetRemoteRoot(working);
        IReadOnlyList<ToyActionSpec> expected = ToyActionRules.Expected(
            side,
            player,
            hand,
            stack,
            remoteRoot,
            working.Cards);
        var actions = new List<LegalAction>(expected.Count);
        foreach (ToyActionSpec spec in expected)
        {
            string description = spec.Target.HasValue
                ? $"{GetToyActionVerb(spec.Kind)} {spec.Target.Value}"
                : GetToyActionVerb(spec.Kind);
            ActionParameters parameters = spec.Target.HasValue
                ? new CardChoiceParameters(spec.Target.Value)
                : new NoActionParameters();
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                spec.Kind,
                description,
                parameters));
        }

        DecisionId decisionId = working.Allocator.NextDecisionId();
        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            decisionId,
            working.DecisionRevision,
            player.Id,
            DecisionKind.ToyTurn,
            side == PlayerSide.Corp ? "Choose a Corp action" : "Choose a Runner action",
            actions);
    }

    private static string GetToyActionVerb(ActionKind kind) => kind switch
    {
        ActionKind.GainCredit => "Gain 1 credit",
        ActionKind.DrawCard => "Draw a card",
        ActionKind.InstallCard => "Install card",
        ActionKind.AdvanceAgenda => "Advance agenda",
        ActionKind.ScoreAgenda => "Score agenda",
        ActionKind.StealAgenda => "Steal agenda",
        ActionKind.EndTurn => "End turn",
        _ => throw new ResolutionFailureException($"Action kind '{kind}' is not a toy action."),
    };

    private static void InstallToyCard(WorkingState working, PlayerSide side, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ToyCardBehavior behavior = ToyCardBehaviorCatalog.Get(card.DefinitionId);
        ZoneKind handKind = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneState hand = GetZone(working, handKind);
        if (card.OwnerSide != side || card.ZoneId != hand.Id)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not in the active player's hand.");
        }

        if (behavior.OwnerSide != side
            || (side == PlayerSide.Corp && behavior.Kind != ToyCardKind.CorpAgenda)
            || (side == PlayerSide.Runner && behavior.Kind != ToyCardKind.RunnerProgram))
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not installable by '{side}'.");
        }

        PlayerState player = GetPlayer(working, side);
        if (player.Credits < behavior.InstallCost)
        {
            throw new ResolutionFailureException($"Installing '{cardId}' requires {behavior.InstallCost} credits.");
        }

        ZoneState destination = side == PlayerSide.Corp
            ? GetRemoteRoot(working)
            : GetZone(working, ZoneKind.RunnerRig);
        if (side == PlayerSide.Corp && destination.CardIds.Count > 0)
        {
            throw new ResolutionFailureException("The fixture remote already contains an installed card.");
        }

        MoveCard(working, card, hand, destination);
        ApplyCreditDelta(working, player.Id, -behavior.InstallCost);
        Emit(working, EngineEventKinds.CardInstalled, new CardInstalledEventPayload(
            side,
            cardId,
            hand.Id,
            destination.Id,
            behavior.InstallCost));
    }

    private static void AdvanceToyAgenda(WorkingState working, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ToyCardBehavior behavior = ToyCardBehaviorCatalog.Get(card.DefinitionId);
        ZoneState remoteRoot = GetRemoteRoot(working);
        PlayerState corp = GetPlayer(working, PlayerSide.Corp);
        if (corp.Side != PlayerSide.Corp
            || card.OwnerSide != PlayerSide.Corp
            || card.ZoneId != remoteRoot.Id
            || behavior.Kind != ToyCardKind.CorpAgenda
            || card.AdvancementCounters >= behavior.AdvancementRequirement)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not an installed Corp agenda.");
        }

        if (corp.Credits < 1)
        {
            throw new ResolutionFailureException("Advancing an agenda requires one credit.");
        }

        int counters = checked(card.AdvancementCounters + 1);
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            card.ZoneId,
            card.OwnerSide,
            card.FaceUp,
            card.Rezzed,
            counters));
        ApplyCreditDelta(working, corp.Id, -1);
        Emit(working, EngineEventKinds.AgendaAdvanced, new AgendaAdvancedEventPayload(
            cardId,
            counters,
            behavior.AdvancementRequirement));
    }

    private static void ScoreToyAgenda(WorkingState working, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ToyCardBehavior behavior = ToyCardBehaviorCatalog.Get(card.DefinitionId);
        ZoneState remoteRoot = GetRemoteRoot(working);
        if (card.OwnerSide != PlayerSide.Corp
            || card.ZoneId != remoteRoot.Id
            || behavior.Kind != ToyCardKind.CorpAgenda
            || card.AdvancementCounters < behavior.AdvancementRequirement)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not a completed installed Corp agenda.");
        }

        ZoneState score = GetZone(working, ZoneKind.CorpScore);
        MoveCard(working, card, remoteRoot, score);
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            score.Id,
            PlayerSide.Corp,
            true,
            card.Rezzed,
            card.AdvancementCounters));
        AddAgendaPoints(working, PlayerSide.Corp, behavior.AgendaPoints);
        Emit(working, EngineEventKinds.AgendaScored, new AgendaScoredEventPayload(
            PlayerSide.Corp,
            cardId,
            behavior.AgendaPoints));
        if (GetPlayer(working, PlayerSide.Corp).AgendaPoints >= ToyRules.AgendaPointsToWin)
        {
            CompleteToyGame(working, PlayerSide.Corp, GameWinReason.AgendaPoints);
        }
    }

    private static void StealToyAgenda(WorkingState working, CardInstanceId cardId)
    {
        CardInstanceState card = GetCard(working, cardId);
        ToyCardBehavior behavior = ToyCardBehaviorCatalog.Get(card.DefinitionId);
        ZoneState remoteRoot = GetRemoteRoot(working);
        if (card.OwnerSide != PlayerSide.Corp
            || card.ZoneId != remoteRoot.Id
            || behavior.Kind != ToyCardKind.CorpAgenda)
        {
            throw new ResolutionFailureException($"Card '{cardId}' is not an installed agenda available to steal.");
        }

        ZoneState score = GetZone(working, ZoneKind.RunnerScore);
        MoveCard(working, card, remoteRoot, score);
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            score.Id,
            PlayerSide.Corp,
            true,
            card.Rezzed,
            card.AdvancementCounters));
        AddAgendaPoints(working, PlayerSide.Runner, behavior.AgendaPoints);
        Emit(working, EngineEventKinds.AgendaStolen, new AgendaStolenEventPayload(
            PlayerSide.Runner,
            cardId,
            behavior.AgendaPoints));
        if (GetPlayer(working, PlayerSide.Runner).AgendaPoints >= ToyRules.AgendaPointsToWin)
        {
            CompleteToyGame(working, PlayerSide.Runner, GameWinReason.AgendaPoints);
        }
    }

    private static bool TryDrawCard(WorkingState working, PlayerSide side)
    {
        ZoneState deck = GetZone(working, side == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack);
        if (deck.CardIds.Count == 0)
        {
            return false;
        }

        ZoneState hand = GetZone(working, side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip);
        CardInstanceState card = GetCard(working, deck.CardIds[0]);
        MoveCard(working, card, deck, hand);
        Emit(working, EngineEventKinds.CardDrawn, new CardDrawnEventPayload(
            side,
            card.Id,
            deck.Id,
            hand.Id));
        return true;
    }

    private static void EndToyTurn(WorkingState working, PlayerSide side)
    {
        PlayerState current = GetPlayer(working, side);
        ReplacePlayer(working, new PlayerState(current.Id, current.Side, current.Credits, 0, current.AgendaPoints));
        Emit(working, EngineEventKinds.TurnEnded, new TurnEndedEventPayload(side, working.Turn.TurnNumber));
        PlayerSide nextSide = side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        int nextTurnNumber = side == PlayerSide.Runner
            ? checked(working.Turn.TurnNumber + 1)
            : working.Turn.TurnNumber;
        PlayerState next = GetPlayer(working, nextSide);
        ReplacePlayer(working, new PlayerState(
            next.Id,
            next.Side,
            next.Credits,
            nextSide == PlayerSide.Corp ? ToyRules.CorpClicksPerTurn : ToyRules.RunnerClicksPerTurn,
            next.AgendaPoints));
        working.Turn = new TurnState(
            nextSide,
            nextSide == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner,
            nextTurnNumber);
        working.ResolutionStack[^1] = new ToyTurnFrame(
            1,
            ResolutionBehaviorIds.ToyTurn,
            nextSide,
            nextSide == PlayerSide.Corp ? ToyTurnStage.CorpTurnStartDraw : ToyTurnStage.RunnerTurnStart);
    }

    private static void CompleteToyGame(WorkingState working, PlayerSide winner, GameWinReason reason)
    {
        working.PendingDecision = null;
        working.ResolutionStack.Clear();
        foreach (PlayerState player in working.Players.ToArray())
        {
            ReplacePlayer(working, new PlayerState(player.Id, player.Side, player.Credits, 0, player.AgendaPoints));
        }

        working.Outcome = new GameOutcome(GameOutcomeKind.Won, winner, reason);
        working.Turn = new TurnState(winner, TurnPhase.GameOver, working.Turn.TurnNumber);
        Emit(working, EngineEventKinds.GameWon, new GameWonEventPayload(winner, reason));
    }

    private static void AddAgendaPoints(WorkingState working, PlayerSide side, int points)
    {
        PlayerState player = GetPlayer(working, side);
        ReplacePlayer(working, new PlayerState(
            player.Id,
            player.Side,
            player.Credits,
            player.Clicks,
            checked(player.AgendaPoints + points)));
    }

    private static void SpendClick(WorkingState working, PlayerId playerId)
    {
        PlayerState player = working.Players.Single(candidate => candidate.Id == playerId);
        if (player.Clicks <= 0)
        {
            throw new ResolutionFailureException($"Player '{playerId}' has no clicks remaining.");
        }

        int remaining = player.Clicks - 1;
        ReplacePlayer(working, new PlayerState(
            player.Id,
            player.Side,
            player.Credits,
            remaining,
            player.AgendaPoints));
        Emit(working, EngineEventKinds.ClickSpent, new ClickSpentEventPayload(player.Side, remaining));
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
        ReplaceCard(working, new CardInstanceState(
            card.Id,
            card.DefinitionId,
            destination.Id,
            card.OwnerSide,
            card.FaceUp,
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

    private sealed class WorkingState
    {
        private WorkingState(GameState source)
        {
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

        public static WorkingState From(GameState source) => new(source);

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
            RulesState);
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
