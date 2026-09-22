using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildCorpChoices(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board();
        switch (metadata.Id)
        {
            case "corp-choices-01":
                board.FillDecks();
                board.CorpClicks = 0;
                board.CorpCredits = 15;
                board.Add("answer", "30067", ZoneKind.CorpHand);
                for (int index = 0; index < 5; index++) board.Add($"surplus-{index}", index < 3 ? "30075" : "30064", ZoneKind.CorpHand);
                TacticalFixture discard = TacticalFixture.FirstChoice(metadata, board, board.Discard(PlayerSide.Corp),
                    action => action.Parameters is CardChoiceParameters card && card.CardId != board.Card("answer"));
                return discard with { Success = trace => discard.Success(trace) && trace.State.GetZone(ZoneKind.CorpHand).CardIds.Contains(board.Card("answer")) };
            case "corp-choices-02":
            case "corp-choices-04":
                board.FillDecks().Identities(corp: "30035");
                board.Add("agenda", "30067", ZoneKind.ServerRoot, advancement: _runtime.GetSpec(new("30067")).AdvancementRequirement)
                    .Add("needed", "30072", ZoneKind.CorpDiscard, faceUp: true)
                    .Add("less-useful", "30064", ZoneKind.CorpDiscard, faceUp: true)
                    .Add("hand-agenda", "30069", ZoneKind.CorpHand).Add("second-agenda", "30070", ZoneKind.CorpHand)
                    .Add("decoder", "30005", ZoneKind.RunnerRig);
                GameState scored = Score(board);
                if (metadata.Id == "corp-choices-02")
                {
                    GameState targets = board.Until(scored, state => state.PendingDecision?.Kind == DecisionKind.BehaviorChoice, TriggerOrPass);
                    return TargetChain(metadata, board, targets, [board.Card("needed")],
                        trace => trace.State.GetZone(ZoneKind.CorpHand).CardIds.Contains(board.Card("needed"))
                            && trace.State.GetZone(ZoneKind.CorpDiscard).CardIds.Contains(board.Card("less-useful")));
                }
                if (scored.PendingDecision?.Kind != DecisionKind.TriggerOrdering)
                    throw new InvalidOperationException("Simultaneous score triggers must expose real ordering alternatives.");
                return new(metadata, board, scored,
                    trace => trace.State.PendingDecision!.Kind == DecisionKind.BehaviorChoice
                        ? SelectTargets(trace.State, [board.Card("needed")]) : TriggerOrPass(trace.State),
                    trace => trace.OwnSteps.Any() && trace.State.PendingDecision?.Kind == DecisionKind.TurnTiming,
                    trace => trace.Player(PlayerSide.Corp).Credits == board.CorpCredits + 7
                        && trace.State.GetZone(ZoneKind.CorpHand).CardIds.Contains(board.Card("needed"))
                        && trace.OwnSteps.Count(step => step.Action.Kind == ActionKind.UseTrigger) == 1,
                    Counterexample: trace => trace.State.PendingDecision!.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.DeclineTrigger));
            case "corp-choices-03":
                board.FillDecks(corp: 1);
                board.Add("agenda", "30070", ZoneKind.ServerRoot, advancement: _runtime.GetSpec(new("30070")).AdvancementRequirement);
                GameState optional = Score(board);
                TacticalFixture decline = TacticalFixture.FirstChoice(metadata, board, optional, action => action.Kind == ActionKind.DeclineTrigger);
                return decline with
                {
                    Success = trace => decline.Success(trace) && trace.State.Outcome.Kind == GameOutcomeKind.Ongoing
                    && trace.State.GetZone(ZoneKind.CorpDeck).CardIds.Count == 1
                };
            case "corp-choices-05":
                board.FillDecks();
                board.Add("agenda", "30067", ZoneKind.ServerRoot, advancement: _runtime.GetSpec(new("30067")).AdvancementRequirement)
                    .Add("search-upgrade", "30066", ZoneKind.ServerRoot, rezzed: true)
                    .Add("needed", "30072", ZoneKind.CorpDeck).Add("hq-agenda", "30069", ZoneKind.CorpHand);
                GameState search = board.Until(Score(board), state => state.PendingDecision?.Kind == DecisionKind.BehaviorChoice, TriggerOrPass);
                return TargetChain(metadata, board, search, [board.Card("needed")],
                    trace => trace.State.GetZone(ZoneKind.CorpHand).CardIds.Contains(board.Card("needed")));
            default: throw new NotSupportedException(metadata.Id);
        }
    }

    private static GameState Score(TacticalBoard board)
    {
        GameState state = board.Paid(PlayerSide.Corp, PlayerSide.Corp);
        return board.Apply(state, state.PendingDecision!.LegalActions.Single(action => action.Kind == ActionKind.ScoreAgenda)).State;
    }
}
