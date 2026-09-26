using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildRunnerRig(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        switch (metadata.Id)
        {
            case "runner-rig-01":
                board.RunnerCredits = 6;
                board.Add("barrier", "30072", ZoneKind.ServerIce, "rd", rezzed: true)
                    .Add("decoder", "30005", ZoneKind.RunnerRig).Add("needed", "30006", ZoneKind.RunnerGrip)
                    .Add("redundant", "30005", ZoneKind.RunnerGrip);
                return InstallLine(metadata, board, board.Action(PlayerSide.Runner), "needed", _ => true, "decoder");
            case "runner-rig-02":
                board.RunnerCredits = 3;
                board.SuccessfulRun = true;
                board.AttemptedServers.Add("archives");
                board.SuccessfulServers.Add("archives");
                board.Add("sentry", "30047", ZoneKind.ServerIce, "hq", rezzed: true).Add("needed", "30015", ZoneKind.RunnerGrip);
                TacticalFixture discount = InstallLine(metadata, board, board.Action(PlayerSide.Runner), "needed",
                    install => install.CreditCost == 3 && install.CostQuote?.EffectiveCost == 3);
                return discount with { Success = trace => discount.Success(trace) && trace.Player(PlayerSide.Runner).Credits == 0 };
            case "runner-rig-03":
                board.RunnerCredits = 3;
                board.RunnerClicks = 2;
                board.Add("optimizer", "30022", ZoneKind.RunnerRig).Add("earlier-program", "30006", ZoneKind.RunnerRig)
                    .Add("needed", "30005", ZoneKind.RunnerGrip).Add("code-gate", "30074", ZoneKind.ServerIce, "rd", rezzed: true);
                board.InstalledThisTurn.Add("earlier-program");
                board.ProgramsInstalledThisTurn.Add("earlier-program");
                GameState initial = board.Action(PlayerSide.Runner);
                if (initial.PendingDecision!.LegalActions.Any(action => action.Parameters is InstallCardParameters install && install.CardId == board.Card("needed")))
                    throw new InvalidOperationException("The consumed discount must not make this four-credit install affordable with three credits.");
                return new(metadata, board, initial,
                    trace => trace.State.PendingDecision!.Kind == DecisionKind.TurnAction
                        ? trace.State.PendingDecision.LegalActions.FirstOrDefault(action => action.Parameters is InstallCardParameters install && install.CardId == board.Card("needed"))
                            ?? trace.State.PendingDecision.LegalActions.First(action => action.Kind == ActionKind.GainCredit)
                        : trace.State.PendingDecision.Kind == DecisionKind.InstallChoice ? MinimalInstallChoice(trace.State, board.Card("needed"))
                        : TacticalBoard.Passive(trace.State),
                    trace => trace.State.GetZone(trace.State.GetCard(board.Card("needed")).ZoneId).Kind == ZoneKind.RunnerRig,
                    trace => trace.OwnSteps.Count(step => step.Action.Kind == ActionKind.GainCredit) == 1
                        && trace.OwnSteps.Any(step => step.Action.Parameters is InstallCardParameters { FinalizeSelection: true, CreditCost: 4 })
                        && trace.Player(PlayerSide.Runner).Credits == 0,
                    ActionCap: 16, Counterexample: trace => trace.State.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.DrawCard));
            case "runner-rig-04":
                board.Add("unique-answer", "30006", ZoneKind.RunnerRig).Add("duplicate-one", "30005", ZoneKind.RunnerRig)
                    .Add("duplicate-two", "30005", ZoneKind.RunnerRig).Add("invested-virus", "30007", ZoneKind.RunnerRig)
                    .Counter("invested-virus", "virus", 5).Add("needed", "30015", ZoneKind.RunnerGrip);
                TacticalFixture cleanup = InstallLine(metadata, board, board.Action(PlayerSide.Runner), "needed", _ => true,
                    "unique-answer", "duplicate-one", "invested-virus");
                CardInstanceId expendable = board.Card("duplicate-two");
                return cleanup with
                {
                    Oracle = trace => !trace.OwnSteps.Any() ? cleanup.Oracle(trace)
                        : trace.State.PendingDecision!.LegalActions.FirstOrDefault(action => action.Parameters is InstallCardParameters { FinalizeSelection: true })
                        ?? trace.State.PendingDecision.LegalActions.FirstOrDefault(action => action.Parameters is InstallCardParameters install
                            && install.SelectedReplacementCardIds.SequenceEqual([expendable]))
                        ?? trace.State.PendingDecision.LegalActions.FirstOrDefault(action => action.Parameters is CardChoiceParameters card && card.CardId == expendable)
                        ?? TacticalBoard.Passive(trace.State),
                    Success = trace => cleanup.Success(trace) && trace.State.GetZone(trace.State.GetCard(expendable).ZoneId).Kind == ZoneKind.RunnerHeap,
                };
            case "runner-rig-05":
                board.RunnerCredits = 4;
                board.Add("productive-console", "30014", ZoneKind.RunnerRig).Counter("productive-console", "credit", 4, CardCounterPurpose.HostedCredit)
                    .Add("breaker", "30006", ZoneKind.RunnerRig).Add("replacement", "30003", ZoneKind.RunnerGrip);
                foreach (string server in new[] { "hq", "rd", "archives" }) board.Add(server + "-ice", "30063", ZoneKind.ServerIce, server, rezzed: true);
                TacticalFixture reserve = TacticalFixture.FirstChoice(metadata, board, board.Action(PlayerSide.Runner), action =>
                    action.Kind is ActionKind.GainCredit or ActionKind.DrawCard
                    || action.Parameters is AbilityActionParameters ability && ability.SourceCardId == board.Card("productive-console"));
                return reserve with
                {
                    Success = trace => reserve.Success(trace) && trace.Player(PlayerSide.Runner).Credits >= 4
                    && trace.State.GetZone(trace.State.GetCard(board.Card("productive-console")).ZoneId).Kind == ZoneKind.RunnerRig
                };
            default: throw new NotSupportedException(metadata.Id);
        }
    }
}
