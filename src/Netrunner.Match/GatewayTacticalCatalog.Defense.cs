using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildRunnerDefense(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        switch (metadata.Id)
        {
            case "runner-defense-01":
                board.RunnerCredits = 0;
                board.Add("lethal", "30047", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("outer", "30072", ZoneKind.ServerIce, "hq").Add("grip", "30030", ZoneKind.RunnerGrip);
                GameState exit = board.Until(board.Run("hq"), state => state.RunState?.Step == RunTimingStep.JackOutDecision);
                TacticalFixture jack = TacticalFixture.FirstChoice(metadata, board, exit, action => action.Kind == ActionKind.JackOut);
                return jack with { Success = trace => jack.Success(trace) && trace.State.RunState is null && trace.State.Outcome.Kind == GameOutcomeKind.Ongoing };
            case "runner-defense-02":
                board.RunnerCredits = 2;
                board.RunnerClicks = 1;
                board.RunnerTags = 1;
                board.Add("essential", "30018", ZoneKind.RunnerRig).Counter("essential", "credit", 12, CardCounterPurpose.HostedCredit);
                TacticalFixture remove = TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Runner), action => action.Kind == ActionKind.RemoveTag);
                return remove with { Success = trace => remove.Success(trace) && trace.State.RulesState!.Players.Single(player => player.PlayerId == trace.Player(PlayerSide.Runner).Id).Tags == 0 };
            case "runner-defense-03":
                board.RunnerCredits = 6;
                board.RunnerClicks = 1;
                board.Add("funhouse", "30054", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("essential", "30018", ZoneKind.RunnerRig).Counter("essential", "credit", 12, CardCounterPurpose.HostedCredit);
                // The mandatory encounter tag has resolved. With one click
                // remaining, a second tag cannot also be removed this turn.
                GameState branch = board.Until(board.Run("hq"), state => state.PendingDecision?.Kind == DecisionKind.IceSubroutineChoice,
                    state => state.PendingDecision!.LegalActions.FirstOrDefault(action => action.Parameters is BehaviorChoiceParameters choice
                        && choice.OptionKeys.Contains("take-tag")) ?? TacticalBoard.Passive(state));
                TacticalFixture pay = TacticalFixture.FirstChoice(metadata, board, branch,
                    action => action.Parameters is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.PayCredits });
                return pay with { Success = trace => pay.Success(trace) && trace.State.RulesState!.Players.Single(player => player.PlayerId == trace.Player(PlayerSide.Runner).Id).Tags == 1 };
            case "runner-defense-04":
                board.RunnerPoints = 5;
                board.RunnerCredits = 3;
                board.Add("inner", "30072", ZoneKind.ServerIce, rezzed: true).Add("outer", "30073", ZoneKind.ServerIce)
                    .Add("win", "30067", ZoneKind.ServerRoot, faceUp: true).Add("breaker", "30006", ZoneKind.RunnerRig);
                GameState continuing = board.Until(board.Run("remote"), state => state.RunState?.Step == RunTimingStep.JackOutDecision);
                return new(metadata, board, continuing,
                    trace => trace.State.RunState?.Step == RunTimingStep.EncounterPaidWindow
                        && trace.State.RunState.Encounter?.Status != RunEncounterStatus.Complete
                        ? BreakOrPump(trace.State, board.Card("breaker"), [0]) : RunProgress(trace.State),
                    trace => trace.State.RunState is null || trace.State.Outcome.Kind != GameOutcomeKind.Ongoing,
                    trace => trace.State.Outcome.Winner == PlayerSide.Runner && trace.Player(PlayerSide.Runner).AgendaPoints >= 7,
                    Counterexample: trace => trace.State.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.JackOut));
            case "runner-defense-05":
                board.RunnerCredits = 2;
                board.Add("karuna", "30047", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("grip-one", "30030", ZoneKind.RunnerGrip).Add("grip-two", "30020", ZoneKind.RunnerGrip);
                GameState damaged = board.Until(board.Run("hq"), state => state.PendingDecision?.Kind == DecisionKind.IceSubroutineChoice);
                TacticalFixture paidExit = TacticalFixture.FirstChoice(metadata, board, damaged,
                    action => action.Parameters is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.JackOut });
                return paidExit with
                {
                    Success = trace => paidExit.Success(trace) && trace.State.RunState is null
                    && trace.State.Outcome.Kind == GameOutcomeKind.Ongoing && trace.State.GetZone(ZoneKind.RunnerGrip).CardIds.Count == 0
                };
            default: throw new NotSupportedException(metadata.Id);
        }
    }
}
