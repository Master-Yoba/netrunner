using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildRunnerTargets(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        switch (metadata.Id)
        {
            case "runner-targets-01":
                board.RunnerPoints = 5;
                board.Add("win", "30067", ZoneKind.ServerRoot, faceUp: true);
                GameState start = board.Action(PlayerSide.Runner);
                return new(metadata, board, start, trace => RunOrAccess(trace.State, board.Server("remote")),
                    trace => trace.OwnSteps.Any() && trace.State.RunState is null,
                    trace => trace.State.Outcome.Winner == PlayerSide.Runner && trace.Player(PlayerSide.Runner).AgendaPoints >= 7,
                    Counterexample: trace => trace.State.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.GainCredit));
            case "runner-targets-02":
                board.Add("conduit", "30024", ZoneKind.RunnerRig)
                    .Add("hq-ice", "30063", ZoneKind.ServerIce, "hq", rezzed: true);
                return TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Runner),
                    action => action.Parameters is RunServerParameters run && run.ServerId == board.Server("rd"));
            case "runner-targets-03":
                board.RunnerCredits = 2;
                board.Add("tool", "30006", ZoneKind.RunnerGrip);
                foreach (string server in new[] { "hq", "rd", "archives" }) board.Add(server, "30063", ZoneKind.ServerIce, server, rezzed: true);
                return TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Runner), action => action.Kind is ActionKind.GainCredit or ActionKind.DrawCard);
            case "runner-targets-04":
                board.RunnerCredits = 0;
                board.Add("unknown", "30047", ZoneKind.ServerIce, "hq").Add("grip", "30030", ZoneKind.RunnerGrip)
                    .Add("rd-ice", "30063", ZoneKind.ServerIce, "rd", rezzed: true).Add("archives-ice", "30063", ZoneKind.ServerIce, "archives", rezzed: true);
                return TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Runner), action => action.Kind is ActionKind.DrawCard or ActionKind.GainCredit);
            case "runner-targets-05":
                board.RunnerCredits = 3;
                board.RunnerClicks = 2;
                board.Add("discounted", "30015", ZoneKind.RunnerGrip)
                    .Add("hq-ice", "30063", ZoneKind.ServerIce, "hq", rezzed: true).Add("rd-ice", "30063", ZoneKind.ServerIce, "rd", rezzed: true);
                return new(metadata, board, board.Action(PlayerSide.Runner), trace =>
                    trace.State.RunState is not null ? RunProgress(trace.State)
                    : trace.State.Turn.ThisTurnFacts?.SuccessfulRun == true
                        ? trace.State.PendingDecision!.LegalActions.FirstOrDefault(action => action.Parameters is InstallCardParameters install && install.CardId == board.Card("discounted"))
                            ?? TacticalBoard.Passive(trace.State)
                        : RunOrAccess(trace.State, board.Server("archives")),
                    trace => trace.State.GetZone(trace.State.GetCard(board.Card("discounted")).ZoneId).Kind == ZoneKind.RunnerRig,
                    trace => trace.OwnSteps.First().Action.Parameters is RunServerParameters run && run.ServerId == board.Server("archives")
                        && trace.Player(PlayerSide.Runner).Credits == 0 && trace.State.Turn.ThisTurnFacts?.SuccessfulRun == true,
                    Counterexample: trace => trace.State.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.GainCredit));
            default: throw new NotSupportedException(metadata.Id);
        }
    }

    private static LegalAction RunOrAccess(GameState state, ServerId server) => state.PendingDecision!.LegalActions.FirstOrDefault(action =>
        action.Kind == ActionKind.StealAgenda || action.Parameters is RunServerParameters run && run.ServerId == server)
        ?? RunProgress(state);
}
