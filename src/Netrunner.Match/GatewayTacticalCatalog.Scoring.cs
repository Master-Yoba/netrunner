using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildCorpScoring(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        int requirement = _runtime.GetSpec(new("30067")).AdvancementRequirement;
        switch (metadata.Id)
        {
            case "corp-scoring-01":
                board.CorpPoints = 5;
                board.Add("agenda", "30067", ZoneKind.ServerRoot, advancement: requirement);
                TacticalFixture win = TacticalFixture.FirstChoice(metadata, board, board.Paid(PlayerSide.Corp, PlayerSide.Corp),
                    action => action.Kind == ActionKind.ScoreAgenda && action.Parameters is ScoreAgendaParameters score && score.CardId == board.Card("agenda"));
                return win with { Success = trace => win.Success(trace) && trace.State.Outcome.Winner == PlayerSide.Corp };
            case "corp-scoring-02":
                board.CorpCredits = 2;
                board.CorpClicks = 2;
                board.Add("agenda", "30067", ZoneKind.ServerRoot, advancement: requirement - 2);
                GameState initial = board.Action(PlayerSide.Corp);
                return new(metadata, board, initial,
                    trace => trace.State.PendingDecision!.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ScoreAgenda)
                        ?? trace.State.PendingDecision.LegalActions.FirstOrDefault(action => action.Parameters is AdvanceCardParameters advance && advance.CardId == board.Card("agenda"))
                        ?? TacticalBoard.Passive(trace.State),
                    trace => trace.State.GetZone(trace.State.GetCard(board.Card("agenda")).ZoneId).Kind == ZoneKind.CorpScore,
                    trace => trace.OwnSteps.Count(step => step.Action.Parameters is AdvanceCardParameters) == 2
                        && trace.OwnSteps.Count(step => step.Action.Kind == ActionKind.ScoreAgenda) == 1
                        && trace.Player(PlayerSide.Corp).AgendaPoints == 2 && trace.Player(PlayerSide.Corp).Credits == 7,
                    ActionCap: 16, Counterexample: trace => trace.State.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.GainCredit));
            case "corp-scoring-03":
                board.CorpCredits = 2;
                board.CorpClicks = 1;
                board.Add("agenda", "30067", ZoneKind.ServerRoot, advancement: 1)
                    .Add("defense", "30072", ZoneKind.ServerIce);
                return TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Corp), action => action.Kind == ActionKind.GainCredit);
            case "corp-scoring-04":
                board.Add("agenda", "30067", ZoneKind.ServerRoot, advancement: requirement - 2)
                    .Add("irrelevant", "30037", ZoneKind.ServerRoot, "economy")
                    .Add("launch", "30040", ZoneKind.CorpHand);
                GameState action = board.Action(PlayerSide.Corp);
                GameState choice = board.Apply(action, action.PendingDecision!.LegalActions.Single(value =>
                    value.Parameters is PlayCardParameters play && play.CardId == board.Card("launch"))).State;
                return TargetChain(metadata, board, choice, [board.Card("agenda")],
                    trace => trace.State.GetCard(board.Card("agenda")).AdvancementCounters == requirement
                        && trace.State.GetCard(board.Card("irrelevant")).AdvancementCounters == 0);
            case "corp-scoring-05":
                board.Add("agenda", "30069", ZoneKind.ServerRoot, advancement: _runtime.GetSpec(new("30069")).AdvancementRequirement)
                    .Add("important-ice", "30063", ZoneKind.ServerIce, "hq")
                    .Add("irrelevant-ice", "30073", ZoneKind.ServerIce, "archives")
                    .Add("hq-agenda", "30067", ZoneKind.CorpHand).Add("second-agenda", "30070", ZoneKind.CorpHand);
                GameState paid = board.Paid(PlayerSide.Corp, PlayerSide.Corp);
                GameState scored = board.Apply(paid, paid.PendingDecision!.LegalActions.Single(value => value.Kind == ActionKind.ScoreAgenda)).State;
                GameState accepted = board.Until(scored, state => state.PendingDecision?.Kind == DecisionKind.BehaviorChoice, TriggerOrPass);
                return TargetChain(metadata, board, accepted, [board.Card("important-ice")],
                    trace => trace.State.GetCard(board.Card("important-ice")).Rezzed && !trace.State.GetCard(board.Card("irrelevant-ice")).Rezzed);
            default: throw new NotSupportedException(metadata.Id);
        }
    }

    private static LegalAction TriggerOrPass(GameState state) => state.PendingDecision!.LegalActions.FirstOrDefault(action =>
        action.Kind is ActionKind.UseTrigger or ActionKind.OrderTriggers) ?? TacticalBoard.Passive(state);

    private static TacticalFixture TargetChain(TacticalCaseMetadata metadata, TacticalBoard board, GameState initial,
        IReadOnlyList<CardInstanceId> targets, Func<TacticalTrace, bool> outcome)
    {
        bool Expected(LegalAction action) => action.Parameters is BehaviorChoiceParameters choice
            && choice.CardIds.Count > 0 && choice.CardIds.All(targets.Contains);
        if (!initial.PendingDecision!.LegalActions.Any(Expected)) throw new InvalidOperationException($"{metadata.Id}: missing intended target choice.");
        return new(metadata, board, initial,
            trace => SelectTargets(trace.State, targets),
            trace => trace.OwnSteps.Any() && trace.State.PendingDecision?.Kind != DecisionKind.BehaviorChoice,
            trace => trace.OwnSteps.FirstOrDefault() is { } first && Expected(first.Action) && outcome(trace),
            Counterexample: trace => trace.State.PendingDecision!.LegalActions.First(action => !Expected(action)));
    }

    private static LegalAction SelectTargets(GameState state, IReadOnlyList<CardInstanceId> targets)
    {
        LegalAction[] choices = state.PendingDecision!.LegalActions.Where(action => action.Parameters is BehaviorChoiceParameters choice
            && choice.CardIds.All(targets.Contains)).ToArray();
        return choices.FirstOrDefault(action => action.Parameters is BehaviorChoiceParameters choice
                && choice.FinalizeSelection && targets.All(choice.CardIds.Contains))
            ?? choices.Where(action => action.Parameters is BehaviorChoiceParameters { FinalizeSelection: false })
                .OrderByDescending(action => ((BehaviorChoiceParameters)action.Parameters).CardIds.Count).FirstOrDefault()
            ?? throw new InvalidOperationException("Tactical target continuation has no matching issued action: " + EngineJson.Serialize(state.PendingDecision.LegalActions));
    }
}
