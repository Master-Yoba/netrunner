namespace Netrunner.Core;

/// <summary>CR turn and phase reducers for the legacy toy-turn contract.</summary>
public static partial class GameEngine
{
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

    // ---------------------------------------------------------------------
    // CR 5.6/5.7 turn shell (ENG-401)
    // ---------------------------------------------------------------------

    private static bool IsTurnFrameTop(WorkingState working) =>
        working.ResolutionStack.Count > 0
        && working.ResolutionStack[^1] is TurnFrame;

    private static bool IsTurnDiscardFrameTop(WorkingState working) =>
        working.ResolutionStack.Count > 0
        && working.ResolutionStack[^1] is TurnDiscardFrame;

    private static void StepTurn(WorkingState working, TurnFrame frame)
    {
        if (working.Turn.ActiveSide != frame.ActiveSide
            || working.Turn.TurnNumber != frame.TurnNumber)
        {
            throw new ResolutionFailureException("Turn frame does not match the active turn.");
        }

        switch (frame.Stage)
        {
            case TurnFrameStage.GainAllottedClicks:
                AllotTurnClicks(working, frame);
                return;
            case TurnFrameStage.BeforeTurnPriority:
            case TurnFrameStage.ActionPriority:
            case TurnFrameStage.DiscardPriority:
                YieldTurnPriorityDecision(working, frame);
                return;
            case TurnFrameStage.FormalTurnBegin:
                BeginFormalTurn(working, frame);
                return;
            case TurnFrameStage.MandatoryDraw:
                PerformMandatoryDraw(working, frame);
                return;
            case TurnFrameStage.AwaitingAction:
                if (GetPlayer(working, frame.ActiveSide).Clicks <= 0)
                {
                    working.ResolutionStack[^1] = new TurnFrame(
                        frame.Version,
                        frame.BehaviorId,
                        frame.ActiveSide,
                        frame.TurnNumber,
                        TurnFrameStage.ActionPhaseEnd,
                        forcedActionEnd: true);
                }
                else
                {
                    YieldTurnActionDecision(working, frame);
                }

                return;
            case TurnFrameStage.ActionPhaseEnd:
                if (GetPlayer(working, frame.ActiveSide).Clicks > 0 && !frame.ForcedActionEnd)
                {
                    throw new ResolutionFailureException("The action phase cannot end while clicks remain without a direct effect.");
                }

                EndTurnActionPhase(working, frame);
                return;
            case TurnFrameStage.DiscardSelection:
                BeginTurnDiscard(working, frame);
                return;
            case TurnFrameStage.LoseLeftoverClicks:
                LoseTurnClicks(working, frame);
                return;
            case TurnFrameStage.FormalTurnEnd:
                EndFormalTurn(working, frame);
                return;
            default:
                throw new ResolutionFailureException($"Unsupported CR turn stage '{frame.Stage}'.");
        }
    }

    private static void AllotTurnClicks(WorkingState working, TurnFrame frame)
    {
        PlayerState player = GetPlayer(working, frame.ActiveSide);
        int allotted = frame.ActiveSide == PlayerSide.Corp ? 3 : 4;
        ReplacePlayer(working, new PlayerState(
            player.Id,
            player.Side,
            player.Credits,
            allotted,
            player.AgendaPoints));

        TurnHistoryState history = (working.Turn.History ?? new TurnHistoryState())
            .Start(frame.ActiveSide, frame.TurnNumber, allotted);
        TurnStep step = frame.ActiveSide == PlayerSide.Corp ? TurnStep.CorpDraw : TurnStep.RunnerAction;
        ReplaceTurn(working, frame.ActiveSide, TurnPhaseFor(frame.ActiveSide), frame.TurnNumber, step, history);
        Emit(working, EngineEventKinds.TurnClicksAllotted, new TurnClicksAllottedEventPayload(
            frame.ActiveSide,
            frame.TurnNumber,
            allotted));

        working.ResolutionStack[^1] = new TurnFrame(
            frame.Version,
            frame.BehaviorId,
            frame.ActiveSide,
            frame.TurnNumber,
            TurnFrameStage.BeforeTurnPriority,
            TurnWindow(frame.ActiveSide),
            frame.ActiveSide,
            0,
            frame.ForcedActionEnd);
    }

    private static void YieldTurnPriorityDecision(WorkingState working, TurnFrame frame)
    {
        if (frame.PriorityWindow is null || frame.PriorityHolder is null)
        {
            throw new ResolutionFailureException("A CR turn priority stage requires an explicit window and holder.");
        }

        if (frame.ConsecutivePasses == 0)
        {
            Emit(working, EngineEventKinds.TurnTimingWindowOpened, new TurnTimingWindowOpenedEventPayload(
                frame.ActiveSide,
                frame.TurnNumber,
                frame.Stage,
                frame.PriorityWindow.Value,
                frame.PriorityHolder.Value));
        }

        PlayerState holder = GetPlayer(working, frame.PriorityHolder.Value);
        DecisionId decisionId = working.Allocator.NextDecisionId();
        ActionId passId = working.Allocator.NextActionId();
        var actions = new List<LegalAction>
        {
            new(
                passId,
                ActionKind.PassPriority,
                "Pass",
                new NoActionParameters()),
        };
        AddRezCardActions(working, actions);
        AddScoreActions(working, frame, actions);
        AddAbilityActions(working, actions);
        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            decisionId,
            working.DecisionRevision,
            holder.Id,
            DecisionKind.TurnTiming,
            $"{frame.PriorityWindow.Value} window ({frame.Stage})",
            actions);
    }

    private static void ConsumeTurnPriorityAction(WorkingState working)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame frame
            || frame.Stage is not (TurnFrameStage.BeforeTurnPriority
                or TurnFrameStage.ActionPriority
                or TurnFrameStage.DiscardPriority)
            || frame.PriorityHolder is null
            || frame.PriorityWindow is null)
        {
            throw new ResolutionFailureException("The submitted pass does not match an active CR turn priority window.");
        }

        PlayerSide passedBy = frame.PriorityHolder.Value;
        PlayerSide other = passedBy == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        int nextPasses = frame.ConsecutivePasses + 1;
        Emit(working, EngineEventKinds.TurnPriorityPassed, new TurnPriorityPassedEventPayload(
            frame.ActiveSide,
            frame.TurnNumber,
            frame.PriorityWindow.Value,
            passedBy,
            other,
            nextPasses));

        if (frame.ConsecutivePasses == 0)
        {
            working.ResolutionStack[^1] = new TurnFrame(
                frame.Version,
                frame.BehaviorId,
                frame.ActiveSide,
                frame.TurnNumber,
                frame.Stage,
                frame.PriorityWindow,
                other,
                nextPasses,
                frame.ForcedActionEnd);
            return;
        }

        TurnFrameStage nextStage = frame.Stage switch
        {
            TurnFrameStage.BeforeTurnPriority => TurnFrameStage.FormalTurnBegin,
            TurnFrameStage.ActionPriority => GetPlayer(working, frame.ActiveSide).Clicks > 0
                ? TurnFrameStage.AwaitingAction
                : TurnFrameStage.ActionPhaseEnd,
            TurnFrameStage.DiscardPriority => TurnFrameStage.LoseLeftoverClicks,
            _ => throw new ResolutionFailureException("Unsupported turn priority stage."),
        };
        working.ResolutionStack[^1] = new TurnFrame(
            frame.Version,
            frame.BehaviorId,
            frame.ActiveSide,
            frame.TurnNumber,
            nextStage,
            forcedActionEnd: nextStage == TurnFrameStage.ActionPhaseEnd);
    }

    private static void BeginFormalTurn(WorkingState working, TurnFrame frame)
    {
        TurnHistoryState history = (working.Turn.History ?? new TurnHistoryState())
            .MarkTurnBegan(frame.ActiveSide);
        TurnStep step = frame.ActiveSide == PlayerSide.Corp ? TurnStep.CorpDraw : TurnStep.RunnerAction;
        ReplaceTurn(working, frame.ActiveSide, TurnPhaseFor(frame.ActiveSide), frame.TurnNumber, step, history);
        PlayerState player = GetPlayer(working, frame.ActiveSide);
        Emit(working, EngineEventKinds.TurnStarted, new TurnStartedEventPayload(
            frame.ActiveSide,
            frame.TurnNumber,
            player.Clicks));
        Emit(working, EngineEventKinds.TurnPhaseStarted, new TurnPhaseStartedEventPayload(
            frame.ActiveSide,
            frame.TurnNumber,
            step));

        if (frame.ActiveSide == PlayerSide.Corp)
        {
            working.ResolutionStack[^1] = new TurnFrame(
                frame.Version,
                frame.BehaviorId,
                frame.ActiveSide,
                frame.TurnNumber,
                TurnFrameStage.MandatoryDraw);
        }
        else
        {
            working.ResolutionStack[^1] = new TurnFrame(
                frame.Version,
                frame.BehaviorId,
                frame.ActiveSide,
                frame.TurnNumber,
                TurnFrameStage.ActionPriority,
                TurnWindow(frame.ActiveSide),
                frame.ActiveSide);
        }

        QueueTurnBoundaryOccurrence(working, frame, OccurrenceKind.TurnBegin);
    }

    private static void PerformMandatoryDraw(WorkingState working, TurnFrame frame)
    {
        if (frame.ActiveSide != PlayerSide.Corp)
        {
            throw new ResolutionFailureException("Only the Corp has a mandatory turn draw.");
        }

        if (!TryDrawCard(working, PlayerSide.Corp))
        {
            CompleteTurnGame(working, PlayerSide.Runner, GameWinReason.DeckOut);
            return;
        }

        TurnHistoryState history = (working.Turn.History ?? new TurnHistoryState())
            .MarkMandatoryDraw(PlayerSide.Corp);
        ReplaceTurn(working, PlayerSide.Corp, TurnPhase.Corp, frame.TurnNumber, TurnStep.CorpAction, history);
        Emit(working, EngineEventKinds.TurnPhaseEnded, new TurnPhaseEndedEventPayload(
            PlayerSide.Corp,
            frame.TurnNumber,
            TurnStep.CorpDraw));
        working.ResolutionStack[^1] = new TurnFrame(
            frame.Version,
            frame.BehaviorId,
            frame.ActiveSide,
            frame.TurnNumber,
            TurnFrameStage.ActionPriority,
            TurnWindow(frame.ActiveSide),
            frame.ActiveSide);
    }

    private static void YieldTurnActionDecision(WorkingState working, TurnFrame frame)
    {
        PlayerState player = GetPlayer(working, frame.ActiveSide);
        ZoneKind deckKind = frame.ActiveSide == PlayerSide.Corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack;
        ZoneState? deck = working.Zones.FirstOrDefault(zone => zone.Kind == deckKind && zone.ServerId is null);
        var actions = new List<LegalAction>
        {
            new(
                working.Allocator.NextActionId(),
                ActionKind.GainCredit,
                "Gain 1 credit",
                new NoActionParameters()),
        };
        if (deck is not null && deck.CardIds.Count > 0)
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.DrawCard,
                "Draw a card",
                new NoActionParameters()));
        }

        // Real installs are materialized from the injected printed metadata
        // catalog only while the active side still has a click. The helper
        // exposes the bounded Runner and Corp-root streams; unsupported card
        // classes/destinations fail closed in the shared planner.
        if (player.Clicks > 0)
        {
            AddInstallActions(working, frame.ActiveSide, actions);
            AddPlayActions(working, actions);
            AddRunActions(working, actions);
            AddAdvanceCardActions(working, actions);
            AddTagResourcePurgeActions(working, actions);
            AddAbilityActions(working, actions);
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            player.Id,
            DecisionKind.TurnAction,
            frame.ActiveSide == PlayerSide.Corp ? "Choose a Corp action" : "Choose a Runner action",
            actions);
    }

    private static void ConsumeTurnAction(WorkingState working, ActionKind kind)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame frame
            || frame.Stage != TurnFrameStage.AwaitingAction)
        {
            throw new ResolutionFailureException("The submitted action does not match the active CR turn action stage.");
        }

        PlayerState player = GetPlayer(working, frame.ActiveSide);
        RequireClicks(player, kind);
        switch (kind)
        {
            case ActionKind.GainCredit:
                ApplyCreditDelta(working, player.Id, 1);
                SpendClick(working, player.Id);
                break;
            case ActionKind.DrawCard:
                SpendClick(working, player.Id);
                QueueBasicDrawAction(working, frame);
                return;
            case ActionKind.EndTurn:
                throw new ResolutionFailureException("EndTurn is not legal while clicks remain.");
            default:
                throw new ResolutionFailureException($"Action kind '{kind}' is not a CR 5 turn-shell action.");
        }

        working.ResolutionStack[^1] = new TurnFrame(
            frame.Version,
            frame.BehaviorId,
            frame.ActiveSide,
            frame.TurnNumber,
            TurnFrameStage.ActionPriority,
            TurnWindow(frame.ActiveSide),
            frame.ActiveSide);
    }

    /// <summary>
    /// Opens the real click-for-card action as a typed effect transaction.
    /// The click and turn fact are committed before the imminent checkpoint;
    /// replacement declarations can therefore observe that this is the first
    /// action while a stale/declined source can never re-arm it.
    /// </summary>
    private static void QueueBasicDrawAction(WorkingState working, TurnFrame frame)
    {
        TurnFactsState? facts = working.Turn.History?.ThisTurn(frame.ActiveSide);
        bool firstBasicDraw = facts is { TurnBegan: true, BasicDrawOccurrenceIds.Count: 0 };
        OccurrenceId occurrenceId = working.Allocator.NextOccurrenceId();
        RecordTurnBasicDraw(working, occurrenceId);

        working.ResolutionStack[^1] = new TurnFrame(
            frame.Version,
            frame.BehaviorId,
            frame.ActiveSide,
            frame.TurnNumber,
            TurnFrameStage.ActionPriority,
            TurnWindow(frame.ActiveSide),
            frame.ActiveSide);
        working.ResolutionStack.Add(new EffectResolutionFrame(
            GetPlayer(working, frame.ActiveSide).Id,
            new CostBundle(),
            new EffectBundle(new BasicDrawEffect(
                frame.ActiveSide,
                1,
                false,
                occurrenceId,
                firstBasicDraw))));
    }

    private static void EndTurnActionPhase(WorkingState working, TurnFrame frame)
    {
        TurnHistoryState history = (working.Turn.History ?? new TurnHistoryState())
            .MarkActionPhaseEnded(frame.ActiveSide);
        TurnStep actionStep = frame.ActiveSide == PlayerSide.Corp ? TurnStep.CorpAction : TurnStep.RunnerAction;
        TurnStep discardStep = frame.ActiveSide == PlayerSide.Corp ? TurnStep.CorpDiscard : TurnStep.RunnerDiscard;
        ReplaceTurn(working, frame.ActiveSide, TurnPhaseFor(frame.ActiveSide), frame.TurnNumber, discardStep, history);
        Emit(working, EngineEventKinds.TurnPhaseEnded, new TurnPhaseEndedEventPayload(
            frame.ActiveSide,
            frame.TurnNumber,
            actionStep));
        Emit(working, EngineEventKinds.TurnPhaseStarted, new TurnPhaseStartedEventPayload(
            frame.ActiveSide,
            frame.TurnNumber,
            discardStep));
        working.ResolutionStack[^1] = new TurnFrame(
            frame.Version,
            frame.BehaviorId,
            frame.ActiveSide,
            frame.TurnNumber,
            TurnFrameStage.DiscardSelection);
    }

    private static void BeginTurnDiscard(WorkingState working, TurnFrame frame)
    {
        PlayerState player = GetPlayer(working, frame.ActiveSide);
        ZoneKind handKind = frame.ActiveSide == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneState hand = GetZone(working, handKind);
        int maximum = MaximumHandSize(working, frame.ActiveSide);
        if (frame.ActiveSide == PlayerSide.Runner && maximum < 0)
        {
            CompleteTurnGame(working, PlayerSide.Corp, GameWinReason.Flatline);
            return;
        }
        int required = hand.CardIds.Count - maximum;
        if (required <= 0)
        {
            working.ResolutionStack[^1] = new TurnFrame(
                frame.Version,
                frame.BehaviorId,
                frame.ActiveSide,
                frame.TurnNumber,
                TurnFrameStage.DiscardPriority,
                TurnWindow(frame.ActiveSide),
                frame.ActiveSide);
            return;
        }

        working.ResolutionStack.Add(new TurnDiscardFrame(player.Id, required, hand.CardIds));
    }

    private static void StepTurnDiscard(WorkingState working, TurnDiscardFrame frame)
    {
        if (frame.Selected.Count >= frame.RequiredCount)
        {
            FinishTurnDiscard(working, frame);
            return;
        }

        PlayerState owner = working.Players.SingleOrDefault(player => player.Id == frame.Owner)
            ?? throw new ResolutionFailureException("The turn discard owner does not exist.");
        var actions = new List<LegalAction>();
        foreach (CardInstanceId cardId in frame.Candidates.Where(candidate => !frame.Selected.Contains(candidate)))
        {
            actions.Add(new LegalAction(
                working.Allocator.NextActionId(),
                ActionKind.ChooseCard,
                "Discard card",
                new CardChoiceParameters(cardId)));
        }

        if (actions.Count == 0)
        {
            throw new ResolutionFailureException("The turn discard frame has no remaining card choices.");
        }

        working.DecisionRevision = NextRevision(working.DecisionRevision);
        working.PendingDecision = new PendingDecision(
            working.Allocator.NextDecisionId(),
            working.DecisionRevision,
            owner.Id,
            DecisionKind.TurnDiscard,
            $"Choose {frame.RequiredCount - frame.Selected.Count} card(s) to discard",
            actions);
    }

    private static void ConsumeTurnDiscardAction(WorkingState working, CardInstanceId cardId)
    {
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnDiscardFrame frame
            || frame.Selected.Contains(cardId)
            || !frame.Candidates.Contains(cardId))
        {
            throw new ResolutionFailureException("The submitted discard is not a remaining materialized candidate.");
        }

        TurnDiscardFrame selected = frame.Select(cardId);
        if (selected.Selected.Count < selected.RequiredCount)
        {
            working.ResolutionStack[^1] = selected;
            return;
        }

        FinishTurnDiscard(working, selected);
    }

    private static void FinishTurnDiscard(WorkingState working, TurnDiscardFrame frame)
    {
        PlayerSide side = GetPlayer(working, frame.Owner).Side;
        ZoneKind handKind = side == PlayerSide.Corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip;
        ZoneState hand = GetZone(working, handKind);
        ZoneKind discardKind = side == PlayerSide.Corp ? ZoneKind.CorpDiscard : ZoneKind.RunnerHeap;
        ZoneState discard = GetZone(working, discardKind);
        int maximum = MaximumHandSize(working, side);
        foreach (CardInstanceId cardId in frame.Candidates.Where(candidate => frame.Selected.Contains(candidate)))
        {
            CardInstanceState card = GetCard(working, cardId);
            MoveCard(working, card, hand, discard);
            hand = GetZone(working, handKind);
            discard = GetZone(working, discardKind);
            Emit(working, EngineEventKinds.TurnCardDiscarded, new TurnCardDiscardedEventPayload(
                side,
                working.Turn.TurnNumber,
                cardId,
                hand.CardIds.Count,
                maximum));
        }

        working.ResolutionStack.RemoveAt(working.ResolutionStack.Count - 1);
        if (working.ResolutionStack.Count == 0
            || working.ResolutionStack[^1] is not TurnFrame parent
            || parent.Stage != TurnFrameStage.DiscardSelection)
        {
            throw new ResolutionFailureException("Turn discard frame has no matching discard-selection parent.");
        }

        working.ResolutionStack[^1] = new TurnFrame(
            parent.Version,
            parent.BehaviorId,
            parent.ActiveSide,
            parent.TurnNumber,
            TurnFrameStage.DiscardPriority,
            TurnWindow(parent.ActiveSide),
            parent.ActiveSide);
    }

    private static void LoseTurnClicks(WorkingState working, TurnFrame frame)
    {
        PlayerState player = GetPlayer(working, frame.ActiveSide);
        int clicksLost = player.Clicks;
        if (clicksLost > 0)
        {
            ReplacePlayer(working, new PlayerState(
                player.Id,
                player.Side,
                player.Credits,
                0,
                player.AgendaPoints));
        }

        Emit(working, EngineEventKinds.TurnClicksLost, new TurnClicksLostEventPayload(
            frame.ActiveSide,
            frame.TurnNumber,
            clicksLost));
        TurnHistoryState history = (working.Turn.History ?? new TurnHistoryState())
            .MarkDiscardPhaseEnded(frame.ActiveSide, clicksLost);
        TurnStep discardStep = frame.ActiveSide == PlayerSide.Corp ? TurnStep.CorpDiscard : TurnStep.RunnerDiscard;
        ReplaceTurn(working, frame.ActiveSide, TurnPhaseFor(frame.ActiveSide), frame.TurnNumber, discardStep, history);
        Emit(working, EngineEventKinds.TurnPhaseEnded, new TurnPhaseEndedEventPayload(
            frame.ActiveSide,
            frame.TurnNumber,
            discardStep));
        working.ResolutionStack[^1] = new TurnFrame(
            frame.Version,
            frame.BehaviorId,
            frame.ActiveSide,
            frame.TurnNumber,
            TurnFrameStage.FormalTurnEnd);
    }

    private static void EndFormalTurn(WorkingState working, TurnFrame frame)
    {
        Emit(working, EngineEventKinds.TurnEnded, new TurnEndedEventPayload(frame.ActiveSide, frame.TurnNumber));
        PlayerSide nextSide = frame.ActiveSide == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        int nextTurnNumber = frame.ActiveSide == PlayerSide.Runner
            ? checked(frame.TurnNumber + 1)
            : frame.TurnNumber;
        TurnStep nextStep = nextSide == PlayerSide.Corp ? TurnStep.CorpDraw : TurnStep.RunnerAction;
        ReplaceTurn(
            working,
            nextSide,
            TurnPhaseFor(nextSide),
            nextTurnNumber,
            nextStep,
            working.Turn.History ?? new TurnHistoryState());
        working.ResolutionStack[^1] = new TurnFrame(
            frame.Version,
            frame.BehaviorId,
            nextSide,
            nextTurnNumber,
            TurnFrameStage.GainAllottedClicks);
        QueueTurnBoundaryOccurrence(working, frame, OccurrenceKind.TurnEnd);
    }

    /// <summary>
    /// Routes formal turn boundaries through the same serializable occurrence
    /// checkpoint path as card effects.  The continuation turn frame is placed
    /// below the no-op effect frame, so a trigger decision can pause and resume
    /// without skipping the mandatory draw or advancing into the next turn.
    /// </summary>
    private static void QueueTurnBoundaryOccurrence(
        WorkingState working,
        TurnFrame frame,
        OccurrenceKind kind)
    {
        PlayerState controller = GetPlayer(working, frame.ActiveSide);
        working.ResolutionStack.Add(new EffectResolutionFrame(
            controller.Id,
            new CostBundle(),
            new EffectBundle(new TurnBoundaryEffect(kind, controller.Id, frame.TurnNumber))));
    }

    private static void CompleteTurnGame(WorkingState working, PlayerSide winner, GameWinReason reason)
    {
        working.PendingDecision = null;
        working.ResolutionStack.Clear();
        foreach (PlayerState player in working.Players.ToArray())
        {
            ReplacePlayer(working, new PlayerState(player.Id, player.Side, player.Credits, 0, player.AgendaPoints));
        }

        working.Outcome = new GameOutcome(GameOutcomeKind.Won, winner, reason);
        working.Turn = new TurnState(
            winner,
            TurnPhase.GameOver,
            working.Turn.TurnNumber,
            TurnStep.None,
            working.Turn.History);
        Emit(working, EngineEventKinds.GameWon, new GameWonEventPayload(winner, reason));
    }

    private static int MaximumHandSize(WorkingState working, PlayerSide side)
        => EffectiveMaximumHandSize(working, side);

    private static TurnPriorityWindowKind TurnWindow(PlayerSide side) =>
        side == PlayerSide.Corp ? TurnPriorityWindowKind.PaidRezAndScore : TurnPriorityWindowKind.PaidAndRez;

    private static TurnPhase TurnPhaseFor(PlayerSide side) =>
        side == PlayerSide.Corp ? TurnPhase.Corp : TurnPhase.Runner;

    private static void ReplaceTurn(
        WorkingState working,
        PlayerSide side,
        TurnPhase phase,
        int turnNumber,
        TurnStep step,
        TurnHistoryState history) =>
        working.Turn = new TurnState(side, phase, turnNumber, step, history);
}
