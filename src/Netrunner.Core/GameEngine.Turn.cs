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
}
