using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// CR 9.8 encounter subroutine-break tests. The fixture breaker is installed
/// in the rig, so the encounter window exercises the rig breaker's break
/// offer; per-activation materialization is covered by
/// EncounterBreakerTests.
/// </summary>
public sealed class RunBreakTests
{
    [Fact]
    public void EncounterPaidWindowOffersBreakQuantitiesAndPassForRigBreaker()
    {
        (GameState preRun, CardInstanceId breakerId, _) =
            EncounterBreakerTests.CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);

        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.Equal(RunTimingStep.EncounterPaidWindow, encounter.RunState!.Step);
        Assert.Equal(PlayerSide.Runner, RunTimingTests.SideOf(encounter, decision.Owner));
        // Pass + both break quantities + the pump offered by the same window.
        Assert.Equal(4, decision.LegalActions.Count);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker);
        LegalAction breakOne = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.BreakSubroutines
            && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 1);
        LegalAction breakTwo = Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.BreakSubroutines
            && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 2);
        Assert.Equal(breakerId, Assert.IsType<RunBreakParameters>(breakOne.Parameters).BreakerId);
        Assert.Equal(breakerId, Assert.IsType<RunBreakParameters>(breakTwo.Parameters).BreakerId);
        Assert.Equal(
            $"Break 2 subroutines on {encounter.RunState.Encounter!.IceId.Value} with {breakerId.Value} (pay 1 wallet credit(s))",
            breakTwo.Description);
    }

    [Fact]
    public void BreakingAllSubroutinesChargesCostMarksAllBrokenAndRetainsPriority()
    {
        (GameState preRun, CardInstanceId breakerId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);
        LegalAction breakTwo = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 2);

        ApplyResult broken = RunTimingTests.ApplyAction(encounter, breakTwo);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        GameState afterBreak = broken.State;
        RunEncounterState? encounterState = afterBreak.RunState!.Encounter;
        Assert.NotNull(encounterState);
        Assert.Equal(2, encounterState.Subroutines.Count(subroutine => subroutine.Broken));
        Assert.All(encounterState.Subroutines, subroutine => Assert.False(subroutine.Resolved));
        Assert.Equal(4, afterBreak.GetPlayer(RunnerId(afterBreak)).Credits);

        SubroutinesBrokenEventPayload payload = Assert.IsType<SubroutinesBrokenEventPayload>(
            broken.Events.Single(value => value.Kind == EngineEventKinds.SubroutinesBroken).Payload);
        Assert.Equal(afterBreak.RunState.Id, payload.RunId);
        Assert.Equal(encounterState.Id, payload.EncounterId);
        Assert.Equal(breakerId, payload.BreakerId);
        Assert.Equal(2, payload.BrokenCount);

        // Breaking is an ability: the Runner keeps the floor. With every
        // subroutine broken, only the pass remains on the re-yielded window.
        PendingDecision runnerWindow = Assert.IsType<PendingDecision>(afterBreak.PendingDecision);
        Assert.Equal(RunTimingStep.EncounterPaidWindow, afterBreak.RunState.Step);
        Assert.Equal(PlayerSide.Runner, RunTimingTests.SideOf(afterBreak, runnerWindow.Owner));
        Assert.Single(runnerWindow.LegalActions, action => action.Kind == ActionKind.PassPriority);
    }

    [Fact]
    public void PartialBreakBreaksFirstUnbrokenInOrdinalOrderAndAllowsSecondUse()
    {
        GameState encounter = ReachBreakWindow(FixtureIceBehaviorSpecs.BreakerBarrier);
        LegalAction breakOne = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 1);

        ApplyResult firstBreak = RunTimingTests.ApplyAction(encounter, breakOne);
        Assert.True(firstBreak.Accepted, firstBreak.Rejection?.Message);
        RunEncounterState? afterFirst = firstBreak.State.RunState!.Encounter;
        Assert.NotNull(afterFirst);
        Assert.True(afterFirst.Subroutines.Single(subroutine => subroutine.Ordinal == 0).Broken);
        Assert.False(afterFirst.Subroutines.Single(subroutine => subroutine.Ordinal == 1).Broken);
        // One subroutine remains. A second activation is legal and reuses the
        // printed BreakLimit for that activation.
        PendingDecision runnerWindow = Assert.IsType<PendingDecision>(firstBreak.State.PendingDecision);
        Assert.Equal(PlayerSide.Runner, RunTimingTests.SideOf(firstBreak.State, runnerWindow.Owner));
        LegalAction secondBreak = Assert.Single(runnerWindow.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 1);
        ApplyResult secondUse = RunTimingTests.ApplyAction(firstBreak.State, secondBreak);
        Assert.True(secondUse.Accepted, secondUse.Rejection?.Message);
        Assert.Equal(2, secondUse.State.RunState!.Encounter!.Subroutines.Count(subroutine => subroutine.Broken));
        Assert.Equal(3, secondUse.State.GetPlayer(RunnerId(secondUse.State)).Credits);
    }

    [Fact]
    public void BreakNotOfferedWhenRunnerCannotAfford()
    {
        (GameState preRun, _, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(WithRunnerCredits(preRun, 0));

        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.DoesNotContain(decision.LegalActions, action => action.Kind == ActionKind.BreakSubroutines);
        Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
    }

    [Fact]
    public void BreakNotOfferedWhenBreakerStrengthIsBelowIceStrength()
    {
        GameState encounter = ReachBreakWindow(FixtureIceBehaviorSpecs.BreakerBarrier, iceDefinition: FixtureIceBehaviorSpecs.Strong);

        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.DoesNotContain(decision.LegalActions, action => action.Kind == ActionKind.BreakSubroutines);
        // The pump is not strength-gated, so it remains the only ability.
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker);
        Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
    }

    [Fact]
    public void BreakNotOfferedWhenBreakerInterfaceDoesNotMatchIceType()
    {
        GameState encounter = ReachBreakWindow(
            FixtureIceBehaviorSpecs.BreakerCodeGate,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);

        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.DoesNotContain(decision.LegalActions, action => action.Kind == ActionKind.BreakSubroutines);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker);
        Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
    }

    [Fact]
    public void ForgedBreakQuantityBeyondTheBreakLimitReportsRunViolation()
    {
        GameState encounter = ReachBreakWindow(FixtureIceBehaviorSpecs.BreakerBarrier);
        CardInstanceId iceId = encounter.RunState!.Encounter!.IceId;
        CardInstanceId breakerId = RigBreakerId(encounter);

        PendingDecision pending = encounter.PendingDecision!;
        LegalAction forged = new LegalAction(
            new StableIdAllocator(encounter.Allocator).NextActionId(),
            ActionKind.BreakSubroutines,
            $"Break 5 subroutines on {iceId.Value} with {breakerId.Value}",
            new RunBreakParameters(breakerId, 5));
        GameState tampered = EncounterBreakerTests.WithPendingDecision(encounter, [.. pending.LegalActions, forged]);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(tampered, FixtureBehaviorCatalog.Catalog);
        Assert.Contains(violations, violation => violation.Code == "run.extra-action");
    }

    [Fact]
    public void BreakSubroutinesAtNonEncounterStepIsRejectedWithByteIdenticalState()
    {
        (GameState preRun, CardInstanceId breakerId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState prompt = GameEngine.BeginRun(preRun, RunTimingTests.RemoteId(preRun), FixtureBehaviorCatalog.Catalog);
        GameState approach = RunTimingTests.PassTwice(prompt).State;
        Assert.Equal(RunTimingStep.ApproachPaidWindow, approach.RunState!.Step);

        CardInstanceId iceId = approach.RunState.CurrentIceId!.Value;
        PendingDecision pending = approach.PendingDecision!;
        LegalAction forged = new LegalAction(
            new StableIdAllocator(approach.Allocator).NextActionId(),
            ActionKind.BreakSubroutines,
            $"Break 1 subroutine on {iceId.Value} with {breakerId.Value}",
            new RunBreakParameters(breakerId, 1));
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
    public void SaveLoadAfterPartialBreakRestoresTheWindow()
    {
        GameState encounter = ReachBreakWindow(FixtureIceBehaviorSpecs.BreakerBarrier);
        LegalAction breakOne = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines && Assert.IsType<RunBreakParameters>(action.Parameters).Quantity == 1);

        ApplyResult firstBreak = RunTimingTests.ApplyAction(encounter, breakOne);
        Assert.True(firstBreak.Accepted, firstBreak.Rejection?.Message);
        GameState afterBreak = firstBreak.State;

        string json = EngineJson.Serialize(afterBreak);
        GameState restored = EngineJson.DeserializeState(json, FixtureBehaviorCatalog.Catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));
        LegalAction left = Assert.Single(afterBreak.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines);
        LegalAction right = Assert.Single(restored.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines);
        ApplyResult leftBreak = RunTimingTests.ApplyAction(afterBreak, left);
        ApplyResult rightBreak = RunTimingTests.ApplyAction(restored, right);
        Assert.True(leftBreak.Accepted, leftBreak.Rejection?.Message);
        Assert.True(rightBreak.Accepted, rightBreak.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(leftBreak.State), EngineJson.Serialize(rightBreak.State));
        Assert.Equal(DeterministicHash.ForEvents(leftBreak.Events), DeterministicHash.ForEvents(rightBreak.Events));
        Assert.Equal(2, leftBreak.State.RunState!.Encounter!.Subroutines.Count(subroutine => subroutine.Broken));
        StateInvariantValidator.Validate(leftBreak.State, FixtureBehaviorCatalog.Catalog);
    }

    private static GameState ReachBreakWindow(CardDefinitionId breakerDefinition, CardDefinitionId? iceDefinition = null)
        => EncounterBreakerTests.ReachEncounterWindow(EncounterBreakerTests.CreateRunStateWithBreaker(
            breakerDefinition,
            iceDefinition: iceDefinition ?? FixtureIceBehaviorSpecs.TwoSubroutines).State);

    private static CardInstanceId RigBreakerId(GameState state) =>
        state.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig && zone.OwnerSide == PlayerSide.Runner)
            .CardIds.Single();

    private static PlayerId RunnerId(GameState state) => state.Players.Single(player => player.Side == PlayerSide.Runner).Id;

    internal static GameState WithRunnerCredits(GameState state, int credits)
    {
        var players = state.Players.Select(player => player.Side == PlayerSide.Runner
            ? new PlayerState(player.Id, player.Side, credits, player.Clicks, player.AgendaPoints)
            : player).ToArray();
        return new GameState(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players: players,
            cards: state.Cards,
            zones: state.Zones,
            servers: state.Servers,
            turn: state.Turn,
            outcome: state.Outcome,
            randomState: state.RandomState,
            allocator: state.Allocator,
            schemaVersion: state.SchemaVersion,
            decisionRevision: state.DecisionRevision,
            eventSequence: state.EventSequence,
            resolutionStack: state.ResolutionStack,
            pendingDecision: state.PendingDecision,
            actionSequence: state.ActionSequence,
            initialSeed: state.InitialSeed,
            runState: state.RunState);
    }
}
