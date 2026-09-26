using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildRunnerPayments(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        switch (metadata.Id)
        {
            case "runner-payments-01":
                board.RunnerCredits = 3;
                board.Add("event", "30029", ZoneKind.RunnerGrip).Add("breaker", "30006", ZoneKind.RunnerRig)
                    .Add("ice", "30072", ZoneKind.ServerIce, "hq", rezzed: true);
                GameState action = board.Action(PlayerSide.Runner);
                GameState started = board.Apply(action, action.PendingDecision!.LegalActions.First(value => value.Parameters is PlayCardParameters play
                    && play.CardId == board.Card("event") && play.TargetServerId == board.Server("hq"))).State;
                GameState encounter = board.Until(started, state => state.RunState?.Step == RunTimingStep.EncounterPaidWindow
                    && state.GetPlayer(state.PendingDecision!.Owner).Side == PlayerSide.Runner);
                TacticalFixture temporary = BreakLine(metadata, board, encounter, "breaker", [0], 2);
                return temporary with
                {
                    Success = trace => temporary.Success(trace) && trace.State.RunState!.RunCredits == 4,
                    Counterexample = trace => trace.State.PendingDecision!.LegalActions.First(value => value.Parameters is RunBreakParameters { RunCredits: 0 }),
                };
            case "runner-payments-02":
                board.RunnerCredits = 1;
                board.Add("breaker", "30006", ZoneKind.RunnerRig).Add("virus", "30007", ZoneKind.RunnerRig).Counter("virus", "virus", 7)
                    .Add("ice", "30072", ZoneKind.ServerIce, "hq", rezzed: true);
                TacticalFixture noncredit = BreakLine(metadata, board, Encounter(board, "hq"), "breaker", [0], 0);
                return noncredit with
                {
                    Success = trace => noncredit.Success(trace) && trace.Player(PlayerSide.Runner).Credits == 0
                        && trace.State.RulesState!.Counters.Single(counter => counter.CardId == board.Card("virus")).Count == 7,
                };
            case "runner-payments-03":
                board.RunnerCredits = 7;
                board.SuccessfulRun = true;
                board.AttemptedServers.Add("archives");
                board.SuccessfulServers.Add("archives");
                board.Add("discounted", "30016", ZoneKind.RunnerRig).Add("alternative", "30006", ZoneKind.RunnerRig)
                    .Add("ice", "30063", ZoneKind.ServerIce, "hq", rezzed: true);
                GameState boosted = Encounter(board, "hq");
                for (int index = 0; index < 4; index++) boosted = board.Apply(boosted, boosted.PendingDecision!.LegalActions.First(value =>
                    value.Parameters is RunBreakerParameters pump && pump.BreakerId == board.Card("discounted"))).State;
                if (!boosted.PendingDecision!.LegalActions.Any(value => value.Parameters is RunBreakParameters { CostQuote.EffectiveCost: 1 }))
                    throw new InvalidOperationException("Marjanah's active discount must appear in the issued break quote.");
                TacticalFixture discount = BreakLine(metadata, board, boosted, "discounted", [0, 1, 2], 0);
                return discount with
                {
                    Success = trace => discount.Success(trace) && trace.Player(PlayerSide.Runner).Credits == 0
                        && trace.OwnSteps.Count(step => step.Action.Kind == ActionKind.BreakSubroutines) == 3,
                    Counterexample = trace => trace.State.PendingDecision!.LegalActions.First(value => value.Parameters is RunBreakerParameters pump
                        && pump.BreakerId == board.Card("alternative")),
                };
            case "runner-payments-04":
                board.RunnerCredits = 0;
                board.RunnerClicks = 0;
                board.Add("ice", "30063", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("virus", "30004", ZoneKind.RunnerRig, host: "ice").Counter("virus", "virus", 3)
                    .Add("ordinary", "30006", ZoneKind.RunnerRig);
                TacticalFixture virus = BreakLine(metadata, board, Encounter(board, "hq"), "virus", [0, 1, 2], 0);
                return virus with
                {
                    Success = trace => virus.Success(trace) && trace.State.RulesState!.Counters.All(counter => counter.CardId != board.Card("virus") || counter.Count == 0)
                        && trace.OwnSteps.Count(step => step.Action.Kind == ActionKind.BreakSubroutines) == 3,
                };
            case "runner-payments-05":
                (TacticalBoard first, GameState state) = Twin(false);
                (_, GameState hidden) = Twin(true);
                return TacticalFixture.FirstChoice(metadata, first, state, _ => true, requireAlternative: false) with { HiddenTwin = hidden };
            default: throw new NotSupportedException(metadata.Id);
        }

        (TacticalBoard Board, GameState State) Twin(bool different)
        {
            TacticalBoard hidden = Board().FillDecks(corp: 0);
            hidden.Add("ice", different ? "30047" : "30072", ZoneKind.ServerIce, "hq")
                .Add("hand", different ? "30067" : "30075", ZoneKind.CorpHand)
                .Add("archives", different ? "30064" : "30067", ZoneKind.CorpDiscard)
                .Add("deck", "30075", ZoneKind.CorpDeck).Add("economy", "30030", ZoneKind.RunnerGrip);
            return (hidden, hidden.Action(PlayerSide.Runner));
        }
    }
}
