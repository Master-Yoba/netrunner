using Xunit;

namespace Netrunner.Core.Tests;

public sealed class ExactSubroutineTargetTests
{
    [Fact]
    public void EverySubsetIsOfferedAndANonadjacentPairPaysOnceAfterSaveLoad()
    {
        GameState state = Encounter();
        RunBreakParameters[] choices = state.PendingDecision!.LegalActions.Where(action => action.Kind == ActionKind.BreakSubroutines)
            .Select(action => Assert.IsType<RunBreakParameters>(action.Parameters)).ToArray();
        Assert.Equal(["0", "1", "2", "0,1", "0,2", "1,2"], choices.Select(choice => string.Join(",", choice.SubroutineOrdinals!)));
        string json = EngineJson.Serialize(state);
        GameState restored = EngineJson.DeserializeState(json, FixtureBehaviorCatalog.Catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        LegalAction selected = Target(state, 0, 2);
        ApplyResult live = RunTimingTests.ApplyAction(state, selected);
        ApplyResult loaded = RunTimingTests.ApplyAction(restored, Target(restored, 0, 2));
        Assert.True(live.Accepted, live.Rejection?.Message);
        Assert.True(loaded.Accepted, loaded.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(live.State), EngineJson.Serialize(loaded.State));
        Assert.Equal([true, false, true], live.State.RunState!.Encounter!.Subroutines.Select(sub => sub.Broken));
        Assert.Equal(Credits(state) - 1, Credits(live.State));
        Assert.Empty(StateInvariantValidator.Collect(live.State, FixtureBehaviorCatalog.Catalog));
        Assert.Equal([1], Assert.IsType<RunBreakParameters>(Target(live.State, 1).Parameters).SubroutineOrdinals);
        string after = EngineJson.Serialize(live.State);
        ApplyResult stale = RunTimingTests.ApplyAction(live.State, selected);
        Assert.False(stale.Accepted);
        Assert.Equal(after, EngineJson.Serialize(stale.State));
    }

    [Fact]
    public void SelectingTheSecondSubroutineLeavesTheFirstUnbroken()
    {
        GameState state = Encounter();
        ApplyResult result = RunTimingTests.ApplyAction(state, Target(state, 1));
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal([false, true, false], result.State.RunState!.Encounter!.Subroutines.Select(sub => sub.Broken));
        Assert.Equal(Credits(state) - 1, Credits(result.State));
    }

    [Fact]
    public void MissingOrForgedTargetSetsAreRejectedBeforeSpending()
    {
        GameState state = Encounter();
        LegalAction selected = Target(state, 1);
        GameState missing = EncounterBreakerTests.WithPendingDecision(state, state.PendingDecision!.LegalActions.Where(action => action.Id != selected.Id).ToArray());
        Assert.Contains(StateInvariantValidator.Collect(missing, FixtureBehaviorCatalog.Catalog), violation => violation.Code == "run.missing-action");
        CardInstanceId breaker = Assert.IsType<RunBreakParameters>(selected.Parameters).BreakerId;
        foreach (int[] targets in new[] { new[] { 7 }, new[] { 0, 1, 2 } })
        {
            LegalAction forged = new(selected.Id, selected.Kind, selected.Description, new RunBreakParameters(breaker, targets.Length, subroutineOrdinals: targets));
            GameState tampered = EncounterBreakerTests.WithPendingDecision(state, state.PendingDecision.LegalActions.Select(action => action.Id == selected.Id ? forged : action).ToArray());
            string before = EngineJson.Serialize(tampered);
            ApplyResult result = RunTimingTests.ApplyAction(tampered, forged);
            Assert.False(result.Accepted);
            Assert.Equal(before, EngineJson.Serialize(result.State));
        }
        GameState broken = RunTimingTests.ApplyAction(state, selected).State;
        LegalAction staleTarget = new(Target(broken, 0).Id, selected.Kind, selected.Description, selected.Parameters);
        GameState brokenTarget = EncounterBreakerTests.WithPendingDecision(broken, broken.PendingDecision!.LegalActions.Select(action => action.Id == staleTarget.Id ? staleTarget : action).ToArray());
        Assert.False(RunTimingTests.ApplyAction(brokenTarget, staleTarget).Accepted);
    }

    [Fact]
    public void TargetsAreDetachedCanonicalAndRejectMalformedSets()
    {
        int[] supplied = [2, 0];
        var parameters = new RunBreakParameters(new("card:test"), 2, subroutineOrdinals: supplied);
        supplied[0] = 99;
        Assert.Equal([0, 2], parameters.SubroutineOrdinals);
        Assert.Throws<NotSupportedException>(() => ((IList<int>)parameters.SubroutineOrdinals!)[0] = 99);
        Assert.Throws<ArgumentException>(() => new RunBreakParameters(new("card:test"), 2, subroutineOrdinals: [0, 0]));
        Assert.Throws<ArgumentException>(() => new RunBreakParameters(new("card:test"), 1, subroutineOrdinals: [-1]));
        Assert.Throws<ArgumentException>(() => new RunBreakParameters(new("card:test"), 2, subroutineOrdinals: [0]));
    }

    [Fact]
    public void LegacyCountOnlySavedDecisionsStillBreakTheFirstUnbrokenSubroutines()
    {
        GameState state = Encounter();
        var quantities = new HashSet<int>();
        LegalAction[] actions = state.PendingDecision!.LegalActions.Where(action => action.Parameters is not RunBreakParameters value || quantities.Add(value.Quantity))
            .Select(action => action.Parameters is RunBreakParameters value
                ? new LegalAction(action.Id, action.Kind, action.Description, new RunBreakParameters(value.BreakerId, value.Quantity, value.HostedCredits, value.CostQuote)) : action).ToArray();
        state = EncounterBreakerTests.WithPendingDecision(state, actions);
        GameState restored = EngineJson.DeserializeState(EngineJson.Serialize(state), FixtureBehaviorCatalog.Catalog);
        LegalAction selected = Assert.Single(restored.PendingDecision!.LegalActions, action => action.Parameters is RunBreakParameters { Quantity: 2 });
        ApplyResult result = RunTimingTests.ApplyAction(restored, selected);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal([true, true, false], result.State.RunState!.Encounter!.Subroutines.Select(sub => sub.Broken));
    }

    private static GameState Encounter()
    {
        (GameState initial, _, _) = EncounterBreakerTests.CreateRunStateWithBreaker(FixtureIceBehaviorSpecs.BreakerBarrier, iceDefinition: FixtureIceBehaviorSpecs.ThreeSubroutines);
        return EncounterBreakerTests.ReachEncounterWindow(initial);
    }

    private static int Credits(GameState state) => state.Players.Single(player => player.Side == PlayerSide.Runner).Credits;

    private static LegalAction Target(GameState state, params int[] targets) => Assert.Single(state.PendingDecision!.LegalActions,
        action => action.Parameters is RunBreakParameters value && value.SubroutineOrdinals!.SequenceEqual(targets));
}
