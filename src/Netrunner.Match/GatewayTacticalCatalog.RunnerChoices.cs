using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildRunnerChoices(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        switch (metadata.Id)
        {
            case "runner-choices-01":
                board.RunnerClicks = 0;
                board.Add("needed", "30006", ZoneKind.RunnerGrip).Add("barrier", "30072", ZoneKind.ServerIce, "rd", rezzed: true);
                for (int index = 0; index < 5; index++) board.Add($"duplicate-{index}", index < 3 ? "30031" : "30034", ZoneKind.RunnerGrip);
                TacticalFixture discard = TacticalFixture.FirstChoice(metadata, board, board.Discard(PlayerSide.Runner),
                    action => action.Parameters is CardChoiceParameters card && card.CardId != board.Card("needed"));
                return discard with { Success = trace => discard.Success(trace) && trace.State.GetZone(ZoneKind.RunnerGrip).CardIds.Contains(board.Card("needed")) };
            case "runner-choices-02":
                board.RunnerCredits = 3;
                board.SuccessfulRun = true;
                board.AttemptedServers.Add("archives");
                board.SuccessfulServers.Add("archives");
                board.Add("search", "30011", ZoneKind.RunnerGrip).Add("needed", "30006", ZoneKind.RunnerStack)
                    .Add("redundant", "30005", ZoneKind.RunnerStack).Add("decoder", "30005", ZoneKind.RunnerRig)
                    .Add("barrier", "30072", ZoneKind.ServerIce, "rd", rezzed: true);
                GameState action = board.Action(PlayerSide.Runner);
                GameState search = board.Apply(action, action.PendingDecision!.LegalActions.First(value => value.Parameters is PlayCardParameters play && play.CardId == board.Card("search"))).State;
                return new(metadata, board, search, trace =>
                    trace.State.PendingDecision!.LegalActions.FirstOrDefault(value => value.Parameters is BehaviorChoiceParameters choice
                        && choice.OptionKeys.Contains("install"))
                    ?? (trace.State.PendingDecision.Kind == DecisionKind.BehaviorChoice ? SelectTargets(trace.State, [board.Card("needed")])
                        : MinimalInstallChoice(trace.State, board.Card("needed"))),
                    trace => trace.OwnSteps.Any() && trace.State.PendingDecision?.Kind is DecisionKind.TurnTiming or DecisionKind.TurnAction,
                    trace => trace.State.GetZone(trace.State.GetCard(board.Card("needed")).ZoneId).Kind == ZoneKind.RunnerRig
                        && trace.Player(PlayerSide.Runner).Credits == 0 && trace.State.GetZone(trace.State.GetCard(board.Card("decoder")).ZoneId).Kind == ZoneKind.RunnerRig,
                    Counterexample: trace => trace.State.PendingDecision!.LegalActions.First(value => value.Parameters is BehaviorChoiceParameters choice
                        && choice.CardIds.Contains(board.Card("redundant"))));
            case "runner-choices-03":
                board.RunnerCredits = 0;
                board.Add("carnivore", "30003", ZoneKind.RunnerRig).Add("needed", "30006", ZoneKind.RunnerGrip)
                    .Add("spare-one", "30031", ZoneKind.RunnerGrip).Add("spare-two", "30031", ZoneKind.RunnerGrip)
                    .Add("valuable", "30037", ZoneKind.ServerRoot, rezzed: true).Counter("valuable", "credit", 9, CardCounterPurpose.HostedCredit)
                    .Add("barrier", "30072", ZoneKind.ServerIce, "rd", rezzed: true);
                return new(metadata, board, Access(board, "remote"), trace =>
                    trace.State.PendingDecision!.LegalActions.FirstOrDefault(value => value.Parameters is AbilityActionParameters ability
                        && ability.AbilityKey == "carnivore-access" && ability.AdditionalCostSelectionsOrEmpty.All(selection => !selection.CardIds.Contains(board.Card("needed"))))
                    ?? RunProgress(trace.State),
                    trace => trace.State.RunState is null,
                    trace => InDiscard(trace.State, board.Card("valuable")) && trace.State.GetZone(ZoneKind.RunnerGrip).CardIds.SequenceEqual([board.Card("needed")])
                        && trace.OwnSteps.Count(step => step.Action.Kind == ActionKind.UseAbility) == 1,
                    Counterexample: trace => trace.State.PendingDecision!.LegalActions.First(value => value.Parameters is AbilityActionParameters ability
                        && ability.AdditionalCostSelectionsOrEmpty.Any(selection => selection.CardIds.Contains(board.Card("needed")))));
            case "runner-choices-04":
            case "runner-choices-05":
                board.Identities(runner: "30019");
                board.RunnerCredits = 2;
                board.Add("agenda", "30067", ZoneKind.ServerRoot, advancement: _runtime.GetSpec(new("30067")).AdvancementRequirement)
                    .Add("easy", "30072", ZoneKind.ServerIce, metadata.Id == "runner-choices-04" ? "archives" : "rd", rezzed: true)
                    .Add("hard", "30063", ZoneKind.ServerIce, metadata.Id == "runner-choices-04" ? "rd" : "archives", rezzed: true)
                    .Add("conduit", "30024", ZoneKind.RunnerRig);
                if (metadata.Id == "runner-choices-04")
                    board.Add("pantograph", "30023", ZoneKind.RunnerRig).Add("needed", "30006", ZoneKind.RunnerGrip);
                GameState triggered = board.Until(Score(board), state => state.GetPlayer(state.PendingDecision!.Owner).Side == PlayerSide.Runner, TriggerOrPass);
                if (metadata.Id == "runner-choices-05")
                    return TacticalFixture.FirstChoice(metadata, board, triggered, value => value.Kind == ActionKind.DeclineTrigger);
                return new(metadata, board, triggered, trace =>
                    trace.State.PendingDecision!.Kind == DecisionKind.BehaviorChoice
                        ? SelectTargets(trace.State, trace.State.PendingDecision.LegalActions.Any(value => value.Parameters is BehaviorChoiceParameters { ChoiceKey: "tao-ice" })
                            ? [board.Card("easy"), board.Card("hard")] : [board.Card("needed")])
                        : trace.State.PendingDecision.Kind == DecisionKind.InstallChoice ? MinimalInstallChoice(trace.State, board.Card("needed"))
                        : TriggerOrPass(trace.State),
                    trace => trace.OwnSteps.Any() && trace.State.PendingDecision?.Kind == DecisionKind.TurnTiming,
                    trace => trace.State.GetCard(board.Card("easy")).ZoneId == trace.State.GetServer(board.Server("rd")).IceZoneId
                        && trace.State.GetZone(trace.State.GetCard(board.Card("needed")).ZoneId).Kind == ZoneKind.RunnerRig
                        && trace.Player(PlayerSide.Runner).Credits == 0
                        && trace.OwnSteps.Count(step => step.Action.Kind == ActionKind.UseTrigger) == 1,
                    Opponent: TriggerOrPass, Counterexample: trace => trace.State.PendingDecision!.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.DeclineTrigger));
            default: throw new NotSupportedException(metadata.Id);
        }
    }
}
