using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildRunnerAccess(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        switch (metadata.Id)
        {
            case "runner-access-01":
                board.RunnerPoints = 5;
                board.Add("win", "30067", ZoneKind.ServerRoot);
                GameState agenda = Access(board, "remote");
                TacticalFixture steal = TacticalFixture.FirstChoice(metadata, board, agenda, action => action.Kind == ActionKind.NoAccessAction, requireAlternative: false);
                return steal with { ForcedContinuation = true, Success = trace => steal.Success(trace) && trace.State.Outcome.Winner == PlayerSide.Runner };
            case "runner-access-02":
                board.RunnerCredits = 2;
                board.Add("economy", "30037", ZoneKind.ServerRoot, rezzed: true).Counter("economy", "credit", 9, CardCounterPurpose.HostedCredit);
                TacticalFixture trash = TacticalFixture.FirstChoice(metadata, board, Access(board, "remote"), action => action.Kind == ActionKind.TrashAccessedCard);
                return trash with { Success = trace => trash.Success(trace) && InDiscard(trace.State, board.Card("economy")) && trace.Player(PlayerSide.Runner).Credits == 0 };
            case "runner-access-03":
                board.RunnerCredits = 4;
                // No agenda is installed here for Malapert to support. Spending
                // four to trash it prevents trashing the live two-credit Nico.
                board.Add("spent", "30066", ZoneKind.ServerRoot, rezzed: true)
                    .Add("valuable", "30037", ZoneKind.ServerRoot, rezzed: true).Counter("valuable", "credit", 9, CardCounterPurpose.HostedCredit);
                GameState early = board.Until(board.Run("remote"), state => state.PendingDecision!.LegalActions.Any(action => action.Kind == ActionKind.TrashAccessedCard),
                    state => SelectAccess(state, board.Card("spent")));
                return AccessSequence(metadata, board, early, "valuable", "spent") with
                {
                    Counterexample = trace => trace.State.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.TrashAccessedCard),
                };
            case "runner-access-04":
                board.RunnerCredits = 3;
                board.Add("multiaccess", "30013", ZoneKind.RunnerRig)
                    .Add("valuable", "30058", ZoneKind.ServerRoot, "hq", rezzed: true)
                    .Add("hidden-one", "30075", ZoneKind.CorpHand).Add("hidden-two", "30064", ZoneKind.CorpHand);
                GameState breach = board.Until(board.Run("hq"), state => state.RunState?.Step == RunTimingStep.BreachCandidateSelection);
                return AccessSequence(metadata, board, breach, "valuable") with
                {
                    Success = trace => trace.State.RunState is null && InDiscard(trace.State, board.Card("valuable"))
                        && trace.OwnSteps.First().Action.Parameters is BreachCandidateParameters first && first.CardId == board.Card("valuable")
                        && trace.OwnSteps.Count(step => step.Action.Kind == ActionKind.ChooseBreachCandidate) == 3,
                    Counterexample = trace => trace.State.PendingDecision!.LegalActions.First(action => action.Parameters is BreachCandidateParameters { Source: not null }),
                };
            case "runner-access-05":
                board.RunnerCredits = 2;
                board.Add("carnivore", "30003", ZoneKind.RunnerRig).Add("needed", "30006", ZoneKind.RunnerGrip)
                    .Add("economy", "30030", ZoneKind.RunnerGrip).Add("valuable", "30037", ZoneKind.ServerRoot, rezzed: true)
                    .Counter("valuable", "credit", 9, CardCounterPurpose.HostedCredit);
                GameState optional = Access(board, "remote");
                TacticalFixture pay = TacticalFixture.FirstChoice(metadata, board, optional, action => action.Kind == ActionKind.TrashAccessedCard);
                return pay with
                {
                    Success = trace => pay.Success(trace) && InDiscard(trace.State, board.Card("valuable"))
                        && trace.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count == 2,
                    Counterexample = trace => trace.State.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.UseAbility),
                };
            default: throw new NotSupportedException(metadata.Id);
        }
    }

    private static GameState Access(TacticalBoard board, string server) => board.Until(board.Run(server), state =>
        state.RunState?.Step == RunTimingStep.BreachAccessAbility, RunProgress);

    private static bool InDiscard(GameState state, CardInstanceId card) => state.GetZone(state.GetCard(card).ZoneId).Kind == ZoneKind.CorpDiscard;

    private static LegalAction SelectAccess(GameState state, CardInstanceId preferred) => state.PendingDecision!.LegalActions.FirstOrDefault(action =>
        action.Parameters is BreachCandidateParameters candidate && candidate.CardId == preferred) ?? RunProgress(state);

    private static TacticalFixture AccessSequence(TacticalCaseMetadata metadata, TacticalBoard board, GameState initial, string trash, string? preserve = null) =>
        new(metadata, board, initial,
            trace => trace.State.PendingDecision!.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.TrashAccessedCard
                    && action.Parameters is RunCardPaymentParameters payment && payment.CardId == board.Card(trash))
                ?? SelectAccess(trace.State, board.Card(trash)),
            trace => trace.State.RunState is null,
            trace => InDiscard(trace.State, board.Card(trash)) && (preserve is null || !InDiscard(trace.State, board.Card(preserve))));
}
