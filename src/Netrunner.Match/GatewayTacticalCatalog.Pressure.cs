using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildCorpPressure(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        switch (metadata.Id)
        {
            case "corp-pressure-01":
                board.CorpPoints = 3;
                board.Add("scored", "30069", ZoneKind.CorpScore).Add("damage", "30049", ZoneKind.CorpHand)
                    .Add("grip-one", "30030", ZoneKind.RunnerGrip).Add("grip-two", "30006", ZoneKind.RunnerGrip);
                board.AgendaChanges.Add(new(board.Card("scored"), PlayerSide.Corp, 3, AgendaTurnChangeKind.Score));
                TacticalFixture lethal = TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Corp),
                    action => action.Parameters is PlayCardParameters play && play.CardId == board.Card("damage"));
                return lethal with { Success = trace => lethal.Success(trace) && trace.State.Outcome.Winner == PlayerSide.Corp };
            case "corp-pressure-02":
                board.TurnNumber = 2;
                board.PreviousRunnerTurn = new(1, PlayerSide.Runner, 1, 4, 0, true, false, true, true,
                    successfulRun: true, attemptedRunServerIds: [board.Server("hq")], successfulRunServerIds: [board.Server("hq")]);
                board.Add("trail", "30057", ZoneKind.CorpHand).Add("economy", "30075", ZoneKind.CorpHand);
                GameState options = board.Action(PlayerSide.Corp);
                if (!options.PendingDecision!.LegalActions.Any(action => action.Parameters is PlayCardParameters play && play.CardId == board.Card("trail")))
                    throw new InvalidOperationException("The unprofitable tag alternative must actually be legal.");
                return TacticalFixture.FirstChoice(metadata, board, options,
                    action => action.Parameters is PlayCardParameters play && play.CardId == board.Card("economy"));
            case "corp-pressure-03":
                board.RunnerTags = 1;
                board.Add("valuable", "30018", ZoneKind.RunnerRig).Counter("valuable", "credit", 12, CardCounterPurpose.HostedCredit)
                    .Add("nearly-spent", "30027", ZoneKind.RunnerRig).Counter("nearly-spent", "credit", 2, CardCounterPurpose.HostedCredit);
                TacticalFixture trash = TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Corp),
                    action => action.Parameters is TrashTaggedResourceParameters target && target.CardId == board.Card("valuable"));
                return trash with
                {
                    Success = trace => trash.Success(trace)
                    && trace.State.GetZone(trace.State.GetCard(board.Card("valuable")).ZoneId).Kind == ZoneKind.RunnerHeap
                    && trace.State.GetZone(trace.State.GetCard(board.Card("nearly-spent")).ZoneId).Kind == ZoneKind.RunnerRig
                };
            case "corp-pressure-04":
                board.Add("ansel", "30038", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("future-barrier", "30063", ZoneKind.ServerIce, "rd", rezzed: true)
                    .Add("breaker", "30006", ZoneKind.RunnerRig).Add("expendable", "30007", ZoneKind.RunnerRig)
                    .Counter("expendable", "virus", 1);
                GameState prompt = board.Until(board.Run("hq"), state => state.PendingDecision?.Kind == DecisionKind.IceSubroutineChoice);
                TacticalFixture targetChoice = TacticalFixture.FirstChoice(metadata, board, prompt,
                    action => action.Parameters is IceSubroutineChoiceParameters target && target.CardId == board.Card("breaker"));
                return targetChoice with
                {
                    Success = trace => targetChoice.Success(trace)
                    && trace.State.GetZone(trace.State.GetCard(board.Card("breaker")).ZoneId).Kind == ZoneKind.RunnerHeap
                };
            case "corp-pressure-05":
                board.CorpCredits = 15;
                board.Add("planogram", "30056", ZoneKind.CorpHand);
                GameState phase = board.Action(PlayerSide.Corp);
                GameState choice = board.Apply(phase, phase.PendingDecision!.LegalActions.Single(action => action.Parameters is PlayCardParameters)).State;
                TacticalFixture draw = TacticalFixture.FirstChoice(metadata, board, choice,
                    action => action.Parameters is BehaviorChoiceParameters mode && mode.OptionKeys.SequenceEqual(["draw"]));
                return draw with { Success = trace => draw.Success(trace) && trace.State.GetZone(ZoneKind.CorpHand).CardIds.Count == 3 };
            default: throw new NotSupportedException(metadata.Id);
        }
    }
}
