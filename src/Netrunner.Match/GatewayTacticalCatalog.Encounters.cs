using Netrunner.Core;

namespace Netrunner.Match;

public sealed partial class GatewayTacticalCatalog
{
    private TacticalFixture BuildRunnerEncounter(TacticalCaseMetadata metadata)
    {
        TacticalBoard board = Board().FillDecks();
        switch (metadata.Id)
        {
            case "runner-encounter-01":
                board.RunnerCredits = 2;
                board.Add("ice", "30072", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("matching", "30006", ZoneKind.RunnerRig).Add("wrong-type", "30005", ZoneKind.RunnerRig);
                GameState matching = Encounter(board, "hq");
                if (matching.PendingDecision!.LegalActions.Any(action => action.Parameters is RunBreakerParameters pump && pump.BreakerId == board.Card("wrong-type")
                    || action.Parameters is RunBreakParameters broken && broken.BreakerId == board.Card("wrong-type")))
                    throw new InvalidOperationException("A mismatched breaker must not have a pump/break offer.");
                return BreakLine(metadata, board, matching, "matching", [0], minimumCredits: 1);
            case "runner-encounter-02":
                board.RunnerCredits = 6;
                board.RunnerClicks = 0;
                board.Add("ice", "30063", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("efficient", "30006", ZoneKind.RunnerRig).Add("stranding", "30016", ZoneKind.RunnerRig)
                    .Add("essential-resource", "30018", ZoneKind.RunnerRig).Counter("essential-resource", "credit", 12, CardCounterPurpose.HostedCredit);
                TacticalFixture reserve = BreakLine(metadata, board, Encounter(board, "hq"), "efficient", [0, 1, 2], minimumCredits: 0);
                return reserve with
                {
                    Success = trace => reserve.Success(trace) && trace.Player(PlayerSide.Runner).Credits == 0
                        && trace.OwnSteps.Count(step => step.Action.Kind == ActionKind.PumpBreaker) == 2,
                    Counterexample = trace => trace.State.PendingDecision!.LegalActions.First(action => action.Parameters is RunBreakerParameters pump
                        && pump.BreakerId == board.Card("stranding")),
                };
            case "runner-encounter-03":
                board.RunnerCredits = 1;
                board.Add("ice", "30073", ZoneKind.ServerIce, "hq", rezzed: true).Add("killer", "30015", ZoneKind.RunnerRig);
                TacticalFixture damage = BreakLine(metadata, board, Encounter(board, "hq"), "killer", [0], minimumCredits: 0);
                return damage with
                {
                    Success = trace => damage.Success(trace) && trace.Player(PlayerSide.Corp).Credits == board.CorpCredits + 1,
                    Counterexample = trace => trace.State.PendingDecision!.LegalActions.First(action => action.Parameters is RunBreakParameters broken
                        && broken.SubroutineOrdinals!.SequenceEqual([1])),
                };
            case "runner-encounter-04":
                board.RunnerCredits = 2;
                board.RunnerClicks = 3;
                board.Add("ice", "30038", ZoneKind.ServerIce, rezzed: true).Add("killer", "30015", ZoneKind.RunnerRig)
                    .Add("campaign", "30037", ZoneKind.ServerRoot, rezzed: true).Counter("campaign", "credit", 9, CardCounterPurpose.HostedCredit);
                TacticalFixture clicks = BreakLine(metadata, board, Encounter(board, "remote"), "killer", [0, 2], minimumCredits: 2);
                return clicks with
                {
                    Opponent = state => state.PendingDecision!.LegalActions.FirstOrDefault(action => action.Parameters is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.Decline })
                        ?? TacticalBoard.Passive(state),
                    Oracle = trace => trace.State.PendingDecision!.LegalActions.FirstOrDefault(action => action.Parameters is RunBioroidAbilityParameters bioroid
                        && bioroid.SubroutineOrdinal is 0 or 2) ?? TacticalBoard.Passive(trace.State),
                    Counterexample = trace => trace.State.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.PumpBreaker),
                };
            case "runner-encounter-05":
                board.RunnerCredits = 4;
                board.Add("ice", "30072", ZoneKind.ServerIce, "hq", rezzed: true)
                    .Add("persistent", "30006", ZoneKind.RunnerRig).Add("future-coverage", "30032", ZoneKind.RunnerRig);
                GameState encounter = Encounter(board, "hq");
                // Put both alternatives at sufficient strength. The current
                // break costs are equal; only Mayfly's future loss differs.
                encounter = board.Apply(encounter, encounter.PendingDecision!.LegalActions.First(action => action.Parameters is RunBreakerParameters pump
                    && pump.BreakerId == board.Card("future-coverage"))).State;
                TacticalFixture disposable = BreakLine(metadata, board, encounter, "persistent", [0], minimumCredits: 2);
                return disposable with
                {
                    Oracle = trace => trace.State.RunState?.Step == RunTimingStep.EncounterPaidWindow
                        ? disposable.Oracle(trace) : RunProgress(trace.State),
                    Complete = trace => trace.State.RunState is null,
                    Success = trace => trace.OwnSteps.Any(step => step.Action.Parameters is RunBreakParameters broken && broken.BreakerId == board.Card("persistent"))
                        && trace.State.GetZone(trace.State.GetCard(board.Card("future-coverage")).ZoneId).Kind == ZoneKind.RunnerRig
                        && trace.State.Turn.ThisTurnFacts?.SuccessfulRun == true && trace.Player(PlayerSide.Runner).Credits >= 2,
                    Counterexample = trace => trace.State.PendingDecision!.LegalActions.First(action => action.Parameters is RunBreakParameters broken
                        && broken.BreakerId == board.Card("future-coverage")),
                };
            default: throw new NotSupportedException(metadata.Id);
        }
    }

    private static GameState Encounter(TacticalBoard board, string server) => board.Until(board.Run(server), state =>
        state.RunState?.Step == RunTimingStep.EncounterPaidWindow && state.GetPlayer(state.PendingDecision!.Owner).Side == PlayerSide.Runner);

    private static TacticalFixture BreakLine(TacticalCaseMetadata metadata, TacticalBoard board, GameState initial,
        string breakerRole, int[] requiredOrdinals, int minimumCredits)
    {
        CardInstanceId breaker = board.Card(breakerRole);
        EncounterId encounter = initial.RunState!.Encounter!.Id;
        return new(metadata, board, initial,
            trace => BreakOrPump(trace.State, breaker, requiredOrdinals),
            trace => trace.State.RunState is null || trace.State.RunState.Encounter is { Status: RunEncounterStatus.Complete },
            trace => trace.State.RunState?.Encounter is { Status: RunEncounterStatus.Complete } finished && finished.Id == encounter
                && finished.Subroutines.Where(subroutine => requiredOrdinals.Contains(subroutine.Ordinal)).All(subroutine => subroutine.Broken)
                && trace.State.Outcome.Kind == GameOutcomeKind.Ongoing && trace.Player(PlayerSide.Runner).Credits >= minimumCredits
                && trace.State.GetZone(trace.State.GetCard(breaker).ZoneId).Kind == ZoneKind.RunnerRig,
            ActionCap: 40, Counterexample: trace => trace.State.PendingDecision!.LegalActions.First(action => action.Kind == ActionKind.PassPriority));
    }

    private static LegalAction BreakOrPump(GameState state, CardInstanceId breaker, IReadOnlyCollection<int> required)
    {
        int[] outstanding = state.RunState!.Encounter!.Subroutines.Where(subroutine => required.Contains(subroutine.Ordinal) && !subroutine.Broken)
            .Select(subroutine => subroutine.Ordinal).ToArray();
        if (outstanding.Length == 0) return TacticalBoard.Passive(state);
        return state.PendingDecision!.LegalActions.Where(action => action.Parameters is RunBreakParameters broken && broken.BreakerId == breaker
                && broken.SubroutineOrdinals!.All(outstanding.Contains))
            .OrderByDescending(action => ((RunBreakParameters)action.Parameters).Quantity)
            .ThenByDescending(action => ((RunBreakParameters)action.Parameters).RunCredits).FirstOrDefault()
            ?? state.PendingDecision.LegalActions.Where(action => action.Parameters is RunBreakerParameters pump && pump.BreakerId == breaker)
                .OrderByDescending(action => ((RunBreakerParameters)action.Parameters).RunCredits).FirstOrDefault()
            ?? TacticalBoard.Passive(state);
    }

    private static LegalAction RunProgress(GameState state) => state.PendingDecision!.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ChooseBreachCandidate)
        ?? TacticalBoard.Passive(state);
}
