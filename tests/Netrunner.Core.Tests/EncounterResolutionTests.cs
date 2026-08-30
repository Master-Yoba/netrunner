using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// CR 6.5/9.8 encounter subroutine-resolution tests: printed-order resolution,
/// broken-subroutine skipping, end-the-run sequencing, encounter effect expiry,
/// and run-end cleanup, driven through the public Apply boundary.
/// </summary>
public sealed class EncounterResolutionTests
{
    [Fact]
    public void UnbrokenSubroutinesResolveInPrintedOrderAndRunContinues()
    {
        GameState window = ReachEncounterWindow(FixtureIceBehaviorSpecs.LoseCreditsOnly);

        ApplyResult finished = CloseEncounterWindow(window);
        GameState after = finished.State;

        Assert.Equal(RunTimingStep.MovementPaidWindow, after.RunState!.Step);
        RunEncounterState encounter = after.RunState.Encounter!;
        Assert.Equal(RunEncounterStatus.Complete, encounter.Status);
        RunSubroutineState subroutine = Assert.Single(encounter.Subroutines);
        Assert.True(subroutine.Resolved);
        Assert.False(subroutine.Broken);
        Assert.Equal(3, after.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        Assert.Contains(finished.Events, value => value.Kind == EngineEventKinds.EncounterCompleted);
    }

    [Fact]
    public void BrokenSubroutinesAreSkippedAndRemainingSubroutinesFire()
    {
        (GameState preRun, CardInstanceId breakerId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState window = EncounterBreakerTests.ReachEncounterWindow(preRun);
        LegalAction breakOne = Assert.Single(window.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 1);

        ApplyResult broken = RunTimingTests.ApplyAction(window, breakOne);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        ApplyResult finished = CloseEncounterWindow(broken.State);
        GameState after = finished.State;

        // The end-the-run subroutine fired: the run is gone, the breaker
        // stays in the rig, and the broken credit loss never applied.
        Assert.Null(after.RunState);
        Assert.Equal(4, after.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        CardInstanceState rigBreaker = Assert.Single(after.Cards, card => card.Id == breakerId);
        Assert.Equal(
            after.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig && zone.OwnerSide == PlayerSide.Runner).Id,
            rigBreaker.ZoneId);

        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(
            finished.Events.Single(value => value.Kind == EngineEventKinds.RunEnded).Payload);
        Assert.Equal(RunEndReason.EndedByRule, ended.Reason);
        Assert.False(ended.Successful);
    }

    [Fact]
    public void EndRunSubroutineStopsTheRemainingSequence()
    {
        GameState window = ReachEncounterWindow(FixtureIceBehaviorSpecs.EndRunFirst);

        ApplyResult finished = CloseEncounterWindow(window);
        GameState after = finished.State;

        Assert.Null(after.RunState);
        // The first subroutine ended the run, so the printed credit loss after
        // it never resolved.
        Assert.Equal(5, after.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        RunEndedEventPayload ended = Assert.IsType<RunEndedEventPayload>(
            finished.Events.Single(value => value.Kind == EngineEventKinds.RunEnded).Payload);
        Assert.Equal(RunEndReason.EndedByRule, ended.Reason);
    }

    [Fact]
    public void EncounterScopedPumpsExpireAtEncounterCompletion()
    {
        (GameState preRun, _, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.LoseCreditsOnly);
        GameState window = EncounterBreakerTests.ReachEncounterWindow(preRun);
        LegalAction pump = Assert.Single(window.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PumpBreaker);

        ApplyResult pumped = RunTimingTests.ApplyAction(window, pump);
        Assert.True(pumped.Accepted, pumped.Rejection?.Message);
        Assert.Single(pumped.State.RunState!.LingeringEffects);

        ApplyResult finished = CloseEncounterWindow(pumped.State);
        GameState after = finished.State;

        Assert.Equal(RunTimingStep.MovementPaidWindow, after.RunState!.Step);
        Assert.Empty(after.RunState.LingeringEffects);
        // 5 credits - 2 pump - 2 resolved credit loss.
        Assert.Equal(1, after.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
    }

    [Fact]
    public void FullEncounterFlowBreaksAndEndsTheRunWithCleanup()
    {
        (GameState preRun, CardInstanceId breakerId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState window = EncounterBreakerTests.ReachEncounterWindow(preRun);

        LegalAction breakOne = Assert.Single(window.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).Quantity == 1
            && ((RunBreakParameters)action.Parameters).BreakerId == breakerId);
        ApplyResult broken = RunTimingTests.ApplyAction(window, breakOne);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        Assert.Contains(broken.Events, value => value.Kind == EngineEventKinds.SubroutinesBroken);

        ApplyResult finished = CloseEncounterWindow(broken.State);
        GameState after = finished.State;

        Assert.Null(after.RunState);
        Assert.Equal(4, after.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        CardInstanceState rigBreaker = Assert.Single(after.Cards, card => card.Id == breakerId);
        Assert.Equal(
            after.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig && zone.OwnerSide == PlayerSide.Runner).Id,
            rigBreaker.ZoneId);
        Assert.Contains(finished.Events, value => value.Kind == EngineEventKinds.EncounterCompleted);
        Assert.Contains(finished.Events, value => value.Kind == EngineEventKinds.RunEnded);
    }

    [Fact]
    public void LoseCreditsSubroutineClampsToAvailableCredits()
    {
        GameState preRun = RunTimingTests.CreateRunState(
            iceCount: 1,
            rezzed: true,
            iceDefinition: FixtureIceBehaviorSpecs.LoseCreditsOnly);
        GameState prompt = GameEngine.BeginRun(RunBreakTests.WithRunnerCredits(preRun, 1), RunTimingTests.RemoteId(preRun));
        GameState window = RunTimingTests.PassTwice(RunTimingTests.PassTwice(prompt).State).State;

        ApplyResult finished = CloseEncounterWindow(window);
        GameState after = finished.State;

        Assert.Equal(RunTimingStep.MovementPaidWindow, after.RunState!.Step);
        Assert.Equal(0, after.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
    }

    private static GameState ReachEncounterWindow(CardDefinitionId iceDefinition)
    {
        GameState preRun = RunTimingTests.CreateRunState(iceCount: 1, rezzed: true, iceDefinition: iceDefinition);
        GameState prompt = GameEngine.BeginRun(preRun, RunTimingTests.RemoteId(preRun));
        return RunTimingTests.PassTwice(RunTimingTests.PassTwice(prompt).State).State;
    }

    private static ApplyResult CloseEncounterWindow(GameState window)
    {
        ApplyResult runnerPass = RunTimingTests.ApplyKind(window, ActionKind.PassPriority);
        Assert.True(runnerPass.Accepted, runnerPass.Rejection?.Message);
        ApplyResult corpPass = RunTimingTests.ApplyKind(runnerPass.State, ActionKind.PassPriority);
        Assert.True(corpPass.Accepted, corpPass.Rejection?.Message);
        return corpPass;
    }
}
