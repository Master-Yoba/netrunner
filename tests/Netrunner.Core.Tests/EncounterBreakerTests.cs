using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// CR 26.03 6.5/9.8/3.9.5 encounter-scoped icebreaker tests. The engine is
/// driven through the public Apply boundary exactly like the CR 6.9 timing
/// matrix tests. There is no persistent "attach breaker to server" concept:
/// every registered rig icebreaker materializes its break and pump abilities
/// for each encounter it is eligible for (named-subtype match for breaking,
/// CR 3.9.5h; sufficient current strength, CR 3.9.5g), the printed break
/// limit applies per interface ability activation, and breakers always stay
/// in the rig.
/// </summary>
public sealed class EncounterBreakerTests
{
    internal static readonly CardDefinitionId NotABreaker = new("fixture:program-not-a-breaker");

    [Fact]
    public void EncounterPaidWindowOffersEveryEligibleRigBreakerAbilityAndPassOnlyToCorp()
    {
        (GameState preRun, CardInstanceId breakerId, CardInstanceId? secondBreakerId) =
            CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                secondBreakerDefinition: FixtureIceBehaviorSpecs.BreakerCodeGate,
                iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        CardInstanceId codeGateBreaker = Assert.NotNull(secondBreakerId);
        GameState encounter = ReachEncounterWindow(preRun);

        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.Equal(RunTimingStep.EncounterPaidWindow, encounter.RunState!.Step);
        Assert.Equal(PlayerSide.Runner, RunTimingTests.SideOf(encounter, decision.Owner));
        // Pass, both break quantities of the eligible barrier breaker, and a
        // pump for each registered rig breaker: the code gate breaker cannot
        // break barrier ICE, but its pump is not subtype-gated (CR 3.9.5h).
        Assert.Equal(5, decision.LegalActions.Count);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
        Assert.Equal(2, decision.LegalActions.Count(action => action.Kind == ActionKind.BreakSubroutines));
        Assert.All(
            decision.LegalActions.Where(action => action.Kind == ActionKind.BreakSubroutines),
            action => Assert.Equal(breakerId, ((RunBreakParameters)action.Parameters).BreakerId));
        Assert.Equal(2, decision.LegalActions.Count(action => action.Kind == ActionKind.PumpBreaker));
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker
            && ((RunBreakerParameters)action.Parameters).BreakerId == breakerId);
        Assert.Contains(decision.LegalActions, action => action.Kind == ActionKind.PumpBreaker
            && ((RunBreakerParameters)action.Parameters).BreakerId == codeGateBreaker);

        ApplyResult runnerPass = RunTimingTests.ApplyKind(encounter, ActionKind.PassPriority);
        Assert.True(runnerPass.Accepted, runnerPass.Rejection?.Message);
        Assert.Equal(RunTimingStep.EncounterPaidWindow, runnerPass.State.RunState!.Step);
        PendingDecision corpWindow = Assert.IsType<PendingDecision>(runnerPass.State.PendingDecision);
        Assert.Equal(PlayerSide.Corp, RunTimingTests.SideOf(runnerPass.State, corpWindow.Owner));
        Assert.Single(corpWindow.LegalActions, action => action.Kind == ActionKind.PassPriority);
    }

    [Fact]
    public void BothRigBreakersActInOneRunWhenTheIceTypeChanges()
    {
        (GameState preRun, CardInstanceId breakerId, CardInstanceId? secondBreakerId) =
            CreateTwoIceRunState(
                innerIceDefinition: FixtureIceBehaviorSpecs.CodeGateLoseCredits,
                outerIceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        CardInstanceId codeGateBreaker = Assert.NotNull(secondBreakerId);
        CardInstanceId innerIceId = preRun.GetZone(preRun.Servers.Single(server => server.Kind == ServerKind.Remote).IceZoneId)
            .CardIds[0];

        GameState encounterOne = ReachEncounterWindow(preRun);
        LegalAction breakTwo = Assert.Single(encounterOne.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).Quantity == 2
            && ((RunBreakParameters)action.Parameters).BreakerId == breakerId);

        ApplyResult broken = RunTimingTests.ApplyAction(encounterOne, breakTwo);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        // The next encounter is a code gate: the barrier breaker is no longer
        // eligible, so the other installed breaker breaks it instead — both
        // breakers act in the same run and no attachment carries over.
        GameState encounterTwo = DriveUntilEncounter(broken.State, innerIceId);
        PendingDecision decisionTwo = Assert.IsType<PendingDecision>(encounterTwo.PendingDecision);
        Assert.DoesNotContain(decisionTwo.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).BreakerId == breakerId);
        LegalAction secondBreak = Assert.Single(decisionTwo.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).BreakerId == codeGateBreaker);

        ApplyResult brokenSecond = RunTimingTests.ApplyAction(encounterTwo, secondBreak);
        Assert.True(brokenSecond.Accepted, brokenSecond.Rejection?.Message);

        GameState ended = DriveToRunEnd(brokenSecond.State);
        ZoneState rigZone = ended.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig && zone.OwnerSide == PlayerSide.Runner);
        Assert.Contains(breakerId, rigZone.CardIds);
        Assert.Contains(codeGateBreaker, rigZone.CardIds);
    }

    [Fact]
    public void BreakLimitAppliesAgainAtTheNextIce()
    {
        (GameState preRun, CardInstanceId breakerId, _) =
            CreateTwoIceRunState(
                innerIceDefinition: FixtureIceBehaviorSpecs.LoseCreditsOnly,
                outerIceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        CardInstanceId innerIceId = preRun.GetZone(preRun.Servers.Single(server => server.Kind == ServerKind.Remote).IceZoneId)
            .CardIds[0];

        GameState encounterOne = ReachEncounterWindow(preRun);
        LegalAction breakTwo = Assert.Single(encounterOne.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).Quantity == 2);
        ApplyResult broken = RunTimingTests.ApplyAction(encounterOne, breakTwo);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        // A later encounter is a separate interface-ability context and can
        // use the same printed limit again.
        GameState encounterTwo = DriveUntilEncounter(broken.State, innerIceId);
        LegalAction freshBreak = Assert.Single(encounterTwo.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).BreakerId == breakerId
            && ((RunBreakParameters)action.Parameters).Quantity == 1);

        ApplyResult brokenSecond = RunTimingTests.ApplyAction(encounterTwo, freshBreak);
        Assert.True(brokenSecond.Accepted, brokenSecond.Rejection?.Message);
        DriveToRunEnd(brokenSecond.State);
    }

    [Fact]
    public void BreakLimitResetsForEachInterfaceAbilityActivation()
    {
        (GameState preRun, CardInstanceId breakerId, _) =
            CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                iceDefinition: FixtureIceBehaviorSpecs.ThreeSubroutines);
        GameState encounter = ReachEncounterWindow(preRun);
        LegalAction breakTwo = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).Quantity == 2);

        ApplyResult broken = RunTimingTests.ApplyAction(encounter, breakTwo);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        // One subroutine remains unbroken. The first activation's limit does
        // not exhaust the interface ability for the next activation.
        Assert.Single(broken.State.RunState!.Encounter!.Subroutines, subroutine => !subroutine.Broken);
        PendingDecision reoffered = Assert.IsType<PendingDecision>(broken.State.PendingDecision);
        LegalAction breakOne = Assert.Single(reoffered.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).Quantity == 1);
        Assert.Contains(reoffered.LegalActions, action => action.Kind == ActionKind.PumpBreaker);
        Assert.Contains(reoffered.LegalActions, action => action.Kind == ActionKind.PassPriority);

        ApplyResult secondActivation = RunTimingTests.ApplyAction(broken.State, breakOne);
        Assert.True(secondActivation.Accepted, secondActivation.Rejection?.Message);
        Assert.All(secondActivation.State.RunState!.Encounter!.Subroutines, subroutine => Assert.True(subroutine.Broken));
    }

    [Fact]
    public void UnregisteredRigProgramIsNotOfferedForEncounterAbilities()
    {
        (GameState preRun, _, _) = CreateRunStateWithBreaker(NotABreaker);
        GameState encounter = ReachEncounterWindow(preRun);

        PendingDecision decision = Assert.IsType<PendingDecision>(encounter.PendingDecision);
        Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
    }

    [Fact]
    public void ForgedBreakBeyondRemainingSubroutinesReportsRunViolation()
    {
        (GameState preRun, CardInstanceId breakerId, _) =
            CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState encounter = ReachEncounterWindow(preRun);
        LegalAction breakOne = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).Quantity == 1);
        ApplyResult firstBreak = RunTimingTests.ApplyAction(encounter, breakOne);
        Assert.True(firstBreak.Accepted, firstBreak.Rejection?.Message);

        // Only one subroutine remains; a forged quantity of 2 exceeds the
        // number of currently breakable subroutines even though a fresh
        // activation may reuse the printed BreakLimit.
        CardInstanceId iceId = firstBreak.State.RunState!.Encounter!.IceId;
        PendingDecision pending = firstBreak.State.PendingDecision!;
        LegalAction forged = new LegalAction(
            new StableIdAllocator(firstBreak.State.Allocator).NextActionId(),
            ActionKind.BreakSubroutines,
            $"Break 2 subroutines on {iceId.Value} with {breakerId.Value}",
            new RunBreakParameters(breakerId, 2));
        GameState tampered = WithPendingDecision(firstBreak.State, [.. pending.LegalActions, forged]);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(tampered);
        Assert.Contains(violations, violation => violation.Code == "run.extra-action");
    }

    [Fact]
    public void TamperedPendingDecisionWithUnofferedBreakReportsViolation()
    {
        (GameState preRun, _, CardInstanceId? extraId) = CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.BreakerBarrier,
            extraRigCardDefinition: NotABreaker);
        GameState encounter = ReachEncounterWindow(preRun);
        CardInstanceId nonBreaker = Assert.NotNull(extraId);

        CardInstanceId iceId = encounter.RunState!.Encounter!.IceId;
        PendingDecision pending = encounter.PendingDecision!;
        LegalAction forged = new LegalAction(
            new StableIdAllocator(encounter.Allocator).NextActionId(),
            ActionKind.BreakSubroutines,
            $"Break 1 subroutine on {iceId.Value} with {nonBreaker.Value}",
            new RunBreakParameters(nonBreaker, 1));
        GameState tampered = WithPendingDecision(encounter, [.. pending.LegalActions, forged]);

        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(tampered);
        Assert.Contains(violations, violation => violation.Code == "run.extra-action");
        Assert.DoesNotContain(violations, violation => violation.Code == "decision.unknown-card-action");
    }

    [Fact]
    public void SaveLoadAfterBreakRestoresTheWindowWithoutUsageLedger()
    {
        (GameState preRun, CardInstanceId breakerId, _) =
            CreateRunStateWithBreaker(
                FixtureIceBehaviorSpecs.BreakerBarrier,
                iceDefinition: FixtureIceBehaviorSpecs.TwoSubroutines);
        GameState encounter = ReachEncounterWindow(preRun);
        LegalAction breakOne = Assert.Single(encounter.PendingDecision!.LegalActions, action =>
            action.Kind == ActionKind.BreakSubroutines
            && ((RunBreakParameters)action.Parameters).Quantity == 1);

        ApplyResult firstBreak = RunTimingTests.ApplyAction(encounter, breakOne);
        Assert.True(firstBreak.Accepted, firstBreak.Rejection?.Message);
        GameState afterBreak = firstBreak.State;
        string json = EngineJson.Serialize(afterBreak);
        GameState restored = EngineJson.DeserializeState(json);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.DoesNotContain("breakerUsages", json, StringComparison.Ordinal);

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
        StateInvariantValidator.Validate(leftBreak.State);
    }

    internal static GameState ReachEncounterWindow(GameState preRun)
    {
        GameState prompt = GameEngine.BeginRun(preRun, RunTimingTests.RemoteId(preRun));
        return RunTimingTests.PassTwice(RunTimingTests.PassTwice(prompt).State).State;
    }

    internal static GameState ReachEncounterWindowWithSource(GameState preRun, CardInstanceId initiatingSource)
    {
        GameState prompt = GameEngine.BeginRun(
            preRun,
            RunTimingTests.RemoteId(preRun),
            initiatingSource: initiatingSource);
        return RunTimingTests.PassTwice(RunTimingTests.PassTwice(prompt).State).State;
    }

    /// <summary>
    /// Builds a pre-run state with one rezzed barrier ICE and the named rig
    /// breaker(s); returns the rig breaker's id plus, when present, the id of
    /// the second rig card (a second breaker or a non-breaker extra card).
    /// </summary>
    internal static (GameState State, CardInstanceId BreakerId, CardInstanceId? ExtraId) CreateRunStateWithBreaker(
        CardDefinitionId breakerDefinition,
        CardDefinitionId? secondBreakerDefinition = null,
        CardDefinitionId? extraRigCardDefinition = null,
        CardDefinitionId? iceDefinition = null)
    {
        GameState baseState = RunTimingTests.CreateRunState(iceCount: 1, rezzed: true, iceDefinition: iceDefinition);
        ZoneState rig = baseState.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig && zone.OwnerSide == PlayerSide.Runner);
        StableIdAllocator allocator = new(baseState.Allocator);
        CardInstanceId breakerId = allocator.NextCardInstanceId();
        CardInstanceId? extraId = secondBreakerDefinition is not null || extraRigCardDefinition is not null
            ? allocator.NextCardInstanceId()
            : null;

        var cards = new List<CardInstanceState>
        {
            new CardInstanceState(breakerId, breakerDefinition, rig.Id, PlayerSide.Runner, faceUp: true),
        };
        if (extraId is not null)
        {
            CardDefinitionId extraDefinition = secondBreakerDefinition ?? extraRigCardDefinition
                ?? throw new ArgumentException("Expected a second rig card definition.");
            cards.Add(new CardInstanceState(
                extraId.Value,
                extraDefinition,
                rig.Id,
                PlayerSide.Runner,
                faceUp: true));
        }

        var rigCardIds = new List<CardInstanceId>(rig.CardIds) { breakerId };
        if (extraId is not null)
        {
            rigCardIds.Add(extraId.Value);
        }
        ZoneState attachedRig = new ZoneState(rig.Id, rig.Kind, rig.OwnerSide, rig.ServerId, rigCardIds);

        GameState state = new(
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            players: baseState.Players,
            cards: baseState.Cards.Concat(cards),
            zones: baseState.Zones.Select(zone => zone.Id == rig.Id ? attachedRig : zone).ToArray(),
            servers: baseState.Servers,
            turn: baseState.Turn,
            outcome: baseState.Outcome,
            randomState: baseState.RandomState,
            allocator: allocator.State,
            schemaVersion: baseState.SchemaVersion,
            decisionRevision: baseState.DecisionRevision,
            eventSequence: baseState.EventSequence,
            resolutionStack: baseState.ResolutionStack,
            pendingDecision: baseState.PendingDecision,
            actionSequence: baseState.ActionSequence,
            initialSeed: baseState.InitialSeed,
            runState: baseState.RunState);
        StateInvariantValidator.Validate(state);
        return (state, breakerId, extraId);
    }

    /// <summary>
    /// Builds a pre-run state with two rezzed ICE on the remote server (index
    /// 0 is the innermost), a barrier rig breaker, and a code gate rig breaker.
    /// </summary>
    private static (GameState State, CardInstanceId BreakerId, CardInstanceId? SecondBreakerId) CreateTwoIceRunState(
        CardDefinitionId innerIceDefinition,
        CardDefinitionId outerIceDefinition)
    {
        // RunTimingTests.CreateRunState already creates the remote server (with
        // an empty ICE zone), so the ICE is placed into its zone.
        GameState baseState = RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);
        ZoneState rig = baseState.Zones.Single(zone => zone.Kind == ZoneKind.RunnerRig && zone.OwnerSide == PlayerSide.Runner);
        ServerState remote = baseState.Servers.Single(server => server.Kind == ServerKind.Remote);
        StableIdAllocator allocator = new(baseState.Allocator);
        CardInstanceId innerIceId = allocator.NextCardInstanceId();
        CardInstanceId outerIceId = allocator.NextCardInstanceId();
        CardInstanceId breakerId = allocator.NextCardInstanceId();
        CardInstanceId secondBreakerId = allocator.NextCardInstanceId();

        var cards = new List<CardInstanceState>
        {
            new CardInstanceState(innerIceId, innerIceDefinition, remote.IceZoneId, PlayerSide.Corp, faceUp: true, rezzed: true),
            new CardInstanceState(outerIceId, outerIceDefinition, remote.IceZoneId, PlayerSide.Corp, faceUp: true, rezzed: true),
            new CardInstanceState(breakerId, FixtureIceBehaviorSpecs.BreakerBarrier, rig.Id, PlayerSide.Runner, faceUp: true),
            new CardInstanceState(secondBreakerId, FixtureIceBehaviorSpecs.BreakerCodeGate, rig.Id, PlayerSide.Runner, faceUp: true),
        };

        ZoneState attachedRig = new ZoneState(rig.Id, rig.Kind, rig.OwnerSide, rig.ServerId, [.. rig.CardIds, breakerId, secondBreakerId]);
        ZoneState populatedIce = new ZoneState(remote.IceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, remote.Id, [innerIceId, outerIceId]);

        GameState state = new(
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            players: baseState.Players,
            cards: baseState.Cards.Concat(cards),
            zones: baseState.Zones
                .Select(zone => zone.Id == rig.Id ? attachedRig : zone.Id == remote.IceZoneId ? populatedIce : zone)
                .ToArray(),
            servers: baseState.Servers,
            turn: baseState.Turn,
            outcome: baseState.Outcome,
            randomState: baseState.RandomState,
            allocator: allocator.State,
            schemaVersion: baseState.SchemaVersion,
            decisionRevision: baseState.DecisionRevision,
            eventSequence: baseState.EventSequence,
            resolutionStack: baseState.ResolutionStack,
            pendingDecision: baseState.PendingDecision,
            actionSequence: baseState.ActionSequence,
            initialSeed: baseState.InitialSeed,
            runState: baseState.RunState);
        StateInvariantValidator.Validate(state);
        return (state, breakerId, secondBreakerId);
    }

    /// <summary>
    /// Passes/continues every yielded decision until the run reaches the paid
    /// window of the encounter with <paramref name="iceId"/>.
    /// </summary>
    private static GameState DriveUntilEncounter(GameState state, CardInstanceId iceId)
    {
        for (int guard = 0; guard < 64; guard++)
        {
            if (state.RunState is { Step: RunTimingStep.EncounterPaidWindow, Encounter: { IceId: CardInstanceId current } }
                && current == iceId)
            {
                return state;
            }

            if (state.RunState is null || state.PendingDecision is null)
            {
                throw new InvalidOperationException($"The run ended before encountering ICE '{iceId.Value}'.");
            }

            LegalAction next = state.PendingDecision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ContinueRun)
                ?? state.PendingDecision.LegalActions.Single(action => action.Kind == ActionKind.PassPriority);
            ApplyResult applied = RunTimingTests.ApplyAction(state, next);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            state = applied.State;
        }

        throw new InvalidOperationException($"The run stalled before encountering ICE '{iceId.Value}'.");
    }

    /// <summary>Passes/continues every yielded decision until the run ends.</summary>
    private static GameState DriveToRunEnd(GameState state)
    {
        for (int guard = 0; state.RunState is not null && guard < 64; guard++)
        {
            if (state.PendingDecision is null)
            {
                throw new InvalidOperationException("The run stalled without a pending decision.");
            }

            LegalAction next = state.PendingDecision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ContinueRun)
                ?? state.PendingDecision.LegalActions.Single(action => action.Kind == ActionKind.PassPriority);
            ApplyResult applied = RunTimingTests.ApplyAction(state, next);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            state = applied.State;
        }

        return state;
    }

    internal static GameState WithPendingDecision(GameState state, IReadOnlyList<LegalAction> actions)
    {
        PendingDecision? pending = state.PendingDecision;
        if (pending is null)
        {
            throw new InvalidOperationException("Expected a pending decision.");
        }

        return new GameState(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            players: state.Players,
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
            pendingDecision: new PendingDecision(pending.Id, pending.Revision, pending.Owner, pending.Kind, pending.Prompt, actions),
            actionSequence: state.ActionSequence,
            initialSeed: state.InitialSeed,
            runState: state.RunState);
    }
}
