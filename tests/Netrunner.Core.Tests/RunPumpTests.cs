using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// CR 9.8 encounter breaker-pump tests. The fixture breakers are installed
/// in the rig; every registered breaker is offered its pump for each
/// encounter (the pump is not subtype- or strength-gated).
/// </summary>
public sealed class RunPumpTests
{
    [Fact]
    public void EncounterPaidWindowOffersPumpAlongsideBreaksAndPass()
    {
        (GameState preRun, CardInstanceId breakerId, _) =
            EncounterBreakerTests.CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);

        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.Equal(4, decision.LegalActions.Count);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        Assert.Equal(2, decision.LegalActions.Count(action => action.Kind == ActionKind.BreakSubroutines));
        LegalAction pump = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker);
        Assert.Equal(breakerId, Assert.IsType<RunBreakerParameters>(pump.Parameters).BreakerId);
    }

    [Fact]
    public void PumpIsOfferedForEveryRegisteredRigBreaker()
    {
        (GameState preRun, CardInstanceId breakerId, CardInstanceId? secondBreakerId) =
            EncounterBreakerTests.CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                secondBreakerDefinition: FixtureIceBehaviorSpecs.BreakerCodeGate,
                iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        CardInstanceId codeGateBreaker = Assert.NotNull(secondBreakerId);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);

        // The barrier ICE only admits the barrier breaker to its break; the
        // code gate breaker still gets its pump offer.
        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.Equal(5, decision.LegalActions.Count);
        Assert.Equal(2, decision.LegalActions.Count(action => action.Kind == ActionKind.PumpBreaker));
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker
            && ((RunBreakerParameters)action.Parameters).BreakerId == breakerId);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker
            && ((RunBreakerParameters)action.Parameters).BreakerId == codeGateBreaker);
        Assert.All(
            decision.LegalActions.Where(action => action.Kind == ActionKind.BreakSubroutines),
            action => Assert.Equal(breakerId, ((RunBreakParameters)action.Parameters).BreakerId));
    }

    [Fact]
    public void UnregisteredRigProgramYieldsNoPumpOffer()
    {
        (GameState preRun, _, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            EncounterBreakerTests.NotABreaker,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);

        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.DoesNotContain(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker);
        Assert.DoesNotContain(decision.LegalActions, action => action.Kind == ActionKind.BreakSubroutines);
        Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
    }

    [Fact]
    public void PumpingChargesCostAddsEncounterExpiringEffectAndRetainsPriority()
    {
        (GameState preRun, CardInstanceId breakerId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);
        LegalAction pump = Assert.Single(encounter.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PumpBreaker);

        ApplyResult pumped = RunTimingTests.ApplyAction(encounter, pump);
        Assert.True(pumped.Accepted, pumped.Rejection?.Message);
        GameState afterPump = pumped.State;

        Assert.Equal(3, afterPump.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
        RunLingeringEffect effect = Assert.Single(afterPump.RunState!.LingeringEffects);
        Assert.Equal(EncounterStrength.IcebreakerPumpBehavior, effect.BehaviorId);
        Assert.Equal(breakerId, effect.SourceCardId);
        Assert.Equal(RunLingeringEffectExpiry.Encounter, effect.Expiry);

        BreakerPumpedEventPayload payload = Assert.IsType<BreakerPumpedEventPayload>(
            pumped.Events.Single(value => value.Kind == EngineEventKinds.BreakerPumped).Payload);
        Assert.Equal(afterPump.RunState.Id, payload.RunId);
        Assert.Equal(breakerId, payload.BreakerId);

        // Pumping is an ability: the Runner keeps the floor and the encounter
        // itself is unchanged by the pump.
        RunEncounterState? encounterState = afterPump.RunState.Encounter;
        Assert.NotNull(encounterState);
        Assert.Equal(RunEncounterStatus.Initialized, encounterState.Status);
        Assert.All(encounterState.Subroutines, subroutine => Assert.False(subroutine.Broken));
        PendingDecision runnerWindow = Assert.IsType<PendingDecision>(afterPump.PendingDecision);
        Assert.Equal(RunTimingStep.EncounterPaidWindow, afterPump.RunState.Step);
        Assert.Equal(PlayerSide.Runner, RunTimingTests.SideOf(afterPump, runnerWindow.Owner));
        Assert.Equal(4, runnerWindow.LegalActions.Count);
    }

    [Fact]
    public void RepeatedPumpsAccumulateDistinctEncounterEffects()
    {
        GameState encounter = ReachPumpWindow();
        LegalAction firstPump = Assert.Single(encounter.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PumpBreaker);

        ApplyResult first = RunTimingTests.ApplyAction(encounter, firstPump);
        Assert.True(first.Accepted, first.Rejection?.Message);
        LegalAction secondPump = Assert.Single(first.State.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PumpBreaker);
        ApplyResult second = RunTimingTests.ApplyAction(first.State, secondPump);
        Assert.True(second.Accepted, second.Rejection?.Message);

        Assert.Equal(2, second.State.RunState!.LingeringEffects.Count);
        Assert.Equal(2, second.State.RunState.LingeringEffects.Select(effect => effect.Id.Value).Distinct().Count());
        Assert.Equal(1, second.State.Players.Single(player => player.Side == PlayerSide.Runner).Credits);
    }

    [Fact]
    public void PumpRaisesStrengthSoBreaksBecomeLegal()
    {
        (GameState preRun, _, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.Strong);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);

        // Strength 3 against ICE 4: the break is not legal yet, only the pump.
        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.Equal(2, decision.LegalActions.Count);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker);

        ApplyResult pumped = RunTimingTests.ApplyAction(
            encounter,
            Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker));
        Assert.True(pumped.Accepted, pumped.Rejection?.Message);

        // Strength 4 now covers ICE 4: the single Strong subroutine is breakable.
        PendingDecision reoffered = Assert.IsType<PendingDecision>(pumped.State.PendingDecision);
        LegalAction breakOne = Assert.Single(reoffered.LegalActions, action => action.Kind == ActionKind.BreakSubroutines);
        ApplyResult broken = RunTimingTests.ApplyAction(pumped.State, breakOne);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        Assert.Equal(1, broken.State.RunState!.Encounter!.Subroutines.Count(subroutine => subroutine.Broken));
    }

    [Fact]
    public void PumpNotOfferedWhenRunnerCannotAfford()
    {
        (GameState preRun, _, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(RunBreakTests.WithRunnerCredits(preRun, 1));

        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.DoesNotContain(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker);
        // The 1-credit break is still legal; only the 2-credit pump is not.
        Assert.Equal(3, decision.LegalActions.Count);
    }

    [Fact]
    public void ForgedPumpWhenNoneExpectedReportsRunViolation()
    {
        (GameState preRun, CardInstanceId breakerId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        // Credits are lowered before the run so the materialized window
        // contains no pump offer to forge against.
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(RunBreakTests.WithRunnerCredits(preRun, 1));

        PendingDecision pending = encounter.PendingDecision!;
        LegalAction forged = new LegalAction(
            new StableIdAllocator(encounter.Allocator).NextActionId(),
            ActionKind.PumpBreaker,
            $"Pump {breakerId.Value}",
            new RunBreakerParameters(breakerId));
        GameState tampered = EncounterBreakerTests.WithPendingDecision(encounter, [.. pending.LegalActions, forged]);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(tampered, FixtureBehaviorCatalog.Catalog);
        Assert.Contains(violations, violation => violation.Code == "run.extra-action");
    }

    [Fact]
    public void PumpBreakerAtNonEncounterStepIsRejectedWithByteIdenticalState()
    {
        (GameState preRun, CardInstanceId breakerId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState prompt = GameEngine.BeginRun(preRun, RunTimingTests.RemoteId(preRun), FixtureBehaviorCatalog.Catalog);
        GameState approach = RunTimingTests.PassTwice(prompt).State;
        Assert.Equal(RunTimingStep.ApproachPaidWindow, approach.RunState!.Step);

        PendingDecision pending = approach.PendingDecision!;
        LegalAction forged = new LegalAction(
            new StableIdAllocator(approach.Allocator).NextActionId(),
            ActionKind.PumpBreaker,
            $"Pump {breakerId.Value}",
            new RunBreakerParameters(breakerId));
        GameState tampered = EncounterBreakerTests.WithPendingDecision(approach, [.. pending.LegalActions, forged]);

        string before = EngineJson.Serialize(tampered);
        ApplyResult rejected = GameEngine.Apply(
            tampered,
            new ActionSubmission(
                tampered.PendingDecision!.Owner,
                tampered.PendingDecision.Id,
                tampered.PendingDecision.Revision,
                forged.Id),
            FixtureBehaviorCatalog.Catalog);
        Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
        Assert.Same(tampered, rejected.State);
        Assert.Equal(before, EngineJson.Serialize(rejected.State));
    }

    [Fact]
    public void SaveLoadAfterPumpRestoresTheWindow()
    {
        GameState encounter = ReachPumpWindow();
        LegalAction pump = Assert.Single(encounter.PendingDecision!.LegalActions, action => action.Kind == ActionKind.PumpBreaker);

        ApplyResult pumped = RunTimingTests.ApplyAction(encounter, pump);
        Assert.True(pumped.Accepted, pumped.Rejection?.Message);
        GameState afterPump = pumped.State;

        string json = EngineJson.Serialize(afterPump);
        GameState restored = EngineJson.DeserializeState(json, FixtureBehaviorCatalog.Catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Single(restored.RunState!.LingeringEffects);

        LegalAction left = Assert.Single(afterPump.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 2);
        LegalAction right = Assert.Single(restored.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 2);
        ApplyResult leftBreak = RunTimingTests.ApplyAction(afterPump, left);
        ApplyResult rightBreak = RunTimingTests.ApplyAction(restored, right);
        Assert.True(leftBreak.Accepted, leftBreak.Rejection?.Message);
        Assert.True(rightBreak.Accepted, rightBreak.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(leftBreak.State), EngineJson.Serialize(rightBreak.State));
        Assert.Equal(DeterministicHash.ForEvents(leftBreak.Events), DeterministicHash.ForEvents(rightBreak.Events));
        StateInvariantValidator.Validate(leftBreak.State, FixtureBehaviorCatalog.Catalog);
    }

    private static GameState ReachPumpWindow()
        => EncounterBreakerTests.ReachEncounterWindow(EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines).State);
}
