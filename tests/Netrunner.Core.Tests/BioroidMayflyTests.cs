using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>
/// Adversarial coverage for the typed encountered-bioroid interface and the
/// Mayfly run-end cleanup declaration. These tests intentionally drive only
/// the public action boundary and mutate no authoritative engine state.
/// </summary>
public sealed class BioroidMayflyTests
{
    [Fact]
    public void BioroidAbilityMaterializesOneActionPerUnbrokenSubroutineAndRepeats()
    {
        (GameState preRun, _, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            EncounterBreakerTests.NotABreaker,
            iceDefinition: FixtureIceBehaviorSpecs.Bioroid);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);
        PendingDecision initial = Assert.IsType<PendingDecision>(encounter.PendingDecision);

        LegalAction[] abilities = initial.LegalActions
            .Where(action => action.Kind == ActionKind.UseBioroidAbility)
            .ToArray();
        Assert.Equal(3, abilities.Length);
        Assert.All(abilities, action =>
        {
            RunBioroidAbilityParameters parameters = Assert.IsType<RunBioroidAbilityParameters>(action.Parameters);
            Assert.Equal(FixtureIceBehaviorSpecs.BioroidAbilityKey, parameters.AbilityKey);
            Assert.Equal(BehaviorCostKind.LoseClick, parameters.Cost.Kind);
            Assert.Equal(1, parameters.Cost.Amount);
            Assert.Equal(1, parameters.ClickCost);
            Assert.Equal(
                encounter.RunState!.Encounter!.Subroutines.Single(subroutine =>
                    subroutine.Id == parameters.SubroutineId).Ordinal,
                parameters.SubroutineOrdinal);
            Assert.IsNotType<RunBreakParameters>(action.Parameters);
        });

        int clicksBefore = encounter.GetPlayer(PlayerSide.Runner).Clicks;
        var brokenSubroutines = new HashSet<SubroutineId>();
        foreach (SubroutineId target in abilities
            .Select(action => ((RunBioroidAbilityParameters)action.Parameters).SubroutineId))
        {
            LegalAction action = Assert.Single(
                encounter.PendingDecision!.LegalActions,
                candidate => candidate.Kind == ActionKind.UseBioroidAbility
                    && ((RunBioroidAbilityParameters)candidate.Parameters).SubroutineId == target);
            RunBioroidAbilityParameters parameters = Assert.IsType<RunBioroidAbilityParameters>(action.Parameters);
            ApplyResult result = RunTimingTests.ApplyAction(encounter, action);
            Assert.True(result.Accepted, result.Rejection?.Message);
            encounter = result.State;
            brokenSubroutines.Add(parameters.SubroutineId);

            Assert.Equal(clicksBefore - brokenSubroutines.Count, encounter.GetPlayer(PlayerSide.Runner).Clicks);
            Assert.Contains(
                result.Events,
                value => value.Kind == EngineEventKinds.BioroidAbilityUsed
                    && value.Payload is BioroidAbilityUsedEventPayload payload
                    && payload.SubroutineId == parameters.SubroutineId
                    && payload.ClickCost == 1);
        }

        Assert.All(encounter.RunState!.Encounter!.Subroutines, subroutine => Assert.True(subroutine.Broken));
        Assert.DoesNotContain(encounter.PendingDecision!.LegalActions, action => action.Kind == ActionKind.UseBioroidAbility);
    }

    [Fact]
    public void BioroidAbilityIsOmittedWhenRunnerHasZeroClicks()
    {
        GameState state = WithRunnerClicks(
            RunTimingTests.CreateRunState(
                iceCount: 1,
                rezzed: true,
                iceDefinition: FixtureIceBehaviorSpecs.Bioroid),
            clicks: 0);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(
            AddRigCard(state, EncounterBreakerTests.NotABreaker));

        Assert.DoesNotContain(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseBioroidAbility);
        StateInvariantValidator.Validate(encounter, FixtureBehaviorCatalog.Catalog);
    }

    [Fact]
    public void BioroidPromptRoundTripsAndStaleUseIsRejected()
    {
        (GameState preRun, _, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            EncounterBreakerTests.NotABreaker,
            iceDefinition: FixtureIceBehaviorSpecs.Bioroid);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);
        string json = EngineJson.Serialize(encounter);
        GameState restored = EngineJson.DeserializeState(json, FixtureBehaviorCatalog.Catalog);
        Assert.Equal(json, EngineJson.Serialize(restored));

        LegalAction original = Assert.Single(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseBioroidAbility
                && ((RunBioroidAbilityParameters)action.Parameters).SubroutineId.Value.EndsWith(":0", StringComparison.Ordinal));
        LegalAction replayed = Assert.Single(
            restored.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.UseBioroidAbility
                && ((RunBioroidAbilityParameters)action.Parameters).SubroutineId
                    == ((RunBioroidAbilityParameters)original.Parameters).SubroutineId);

        ApplyResult left = RunTimingTests.ApplyAction(encounter, original);
        ApplyResult right = RunTimingTests.ApplyAction(restored, replayed);
        Assert.True(left.Accepted, left.Rejection?.Message);
        Assert.True(right.Accepted, right.Rejection?.Message);
        Assert.Equal(EngineJson.Serialize(left.State), EngineJson.Serialize(right.State));
        Assert.Equal(DeterministicHash.ForEvents(left.Events), DeterministicHash.ForEvents(right.Events));

        ApplyResult stale = RunTimingTests.ApplyAction(left.State, original);
        Assert.Equal(ApplyRejectionCode.UnknownAction, stale.Rejection?.Code);
        Assert.Same(left.State, stale.State);
    }

    [Fact]
    public void ForgedBioroidActionAndForgedCleanupProvenanceAreRejectedByInvariants()
    {
        (GameState preRun, CardInstanceId mayflyId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.Mayfly,
            iceDefinition: FixtureIceBehaviorSpecs.Bioroid);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);
        PendingDecision pending = encounter.PendingDecision!;
        RunId runId = encounter.RunState!.Id;
        CardInstanceId iceId = encounter.RunState.Encounter!.IceId;
        LegalAction forged = new(
            new StableIdAllocator(encounter.Allocator).NextActionId(),
            ActionKind.UseBioroidAbility,
            "forged bioroid action",
            new RunBioroidAbilityParameters(
                iceId,
                new SubroutineId("subroutine:forged"),
                FixtureIceBehaviorSpecs.BioroidAbilityKey));
        GameState actionTampered = EncounterBreakerTests.WithPendingDecision(
            encounter,
            [.. pending.LegalActions, forged]);
        Assert.Contains(
            StateInvariantValidator.Collect(actionTampered, FixtureBehaviorCatalog.Catalog),
            violation => violation.Code == "run.extra-bioroid-action");

        LegalAction issued = pending.LegalActions.First(action => action.Kind == ActionKind.UseBioroidAbility);
        RunBioroidAbilityParameters issuedParameters =
            Assert.IsType<RunBioroidAbilityParameters>(issued.Parameters);
        LegalAction forgedOrdinal = new(
            issued.Id,
            issued.Kind,
            issued.Description,
            new RunBioroidAbilityParameters(
                issuedParameters.IceId,
                issuedParameters.SubroutineId,
                issuedParameters.AbilityKey,
                issuedParameters.Cost,
                issuedParameters.SubroutineOrdinal + 1));
        GameState ordinalTampered = EncounterBreakerTests.WithPendingDecision(
            encounter,
            pending.LegalActions.Select(action => action.Id == issued.Id ? forgedOrdinal : action).ToArray());
        IReadOnlyList<StateInvariantViolation> ordinalViolations =
            StateInvariantValidator.Collect(ordinalTampered, FixtureBehaviorCatalog.Catalog);
        Assert.Contains(ordinalViolations, violation => violation.Code == "run.extra-bioroid-action");
        Assert.Contains(ordinalViolations, violation => violation.Code == "run.missing-bioroid-action");

        RunLingeringEffect genericEffect = new(
            new RunEffectId($"effect:forged:{runId.Value}"),
            FixtureIceBehaviorSpecs.MayflyCleanupBehaviorId,
            mayflyId,
            RunLingeringEffectExpiry.Run);
        GameState cleanupTampered = WithRunState(
            encounter,
            encounter.RunState.WithLingeringEffects([genericEffect]));
        IReadOnlyList<StateInvariantViolation> violations = StateInvariantValidator.Collect(
            cleanupTampered,
            FixtureBehaviorCatalog.Catalog);
        Assert.Contains(violations, violation => violation.Code == "run.cleanup-kind-missing");
        Assert.DoesNotContain(violations, violation => violation.Code == "run.cleanup-provenance");
    }

    [Fact]
    public void MayflyCleanupArmsOnlyAfterBreakAndPhysicalTrashIsCardMovedEvent()
    {
        (GameState preRun, CardInstanceId mayflyId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.Mayfly,
            iceDefinition: FixtureIceBehaviorSpecs.LoseCreditsOnly);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);
        LegalAction breakAction = Assert.Single(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines);

        ApplyResult broken = RunTimingTests.ApplyAction(encounter, breakAction);
        Assert.True(broken.Accepted, broken.Rejection?.Message);
        RunLingeringEffect armed = Assert.Single(broken.State.RunState!.LingeringEffects);
        Assert.Equal(RunLingeringEffectKind.IcebreakerRunEndCleanup, armed.Kind);
        Assert.Equal(mayflyId, armed.SourceCardId);
        Assert.Equal(FixtureIceBehaviorSpecs.MayflyCleanupBehaviorId, armed.BehaviorId);

        (GameState ended, IReadOnlyList<EngineEvent> events) = DriveRunToEnd(broken.State);
        Assert.Contains(mayflyId, ended.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.DoesNotContain(mayflyId, ended.GetZone(ZoneKind.RunnerRig).CardIds);
        Assert.Contains(
            events,
            value => value.Kind == EngineEventKinds.LingeringEffectExpired
                && value.Payload is LingeringEffectExpiredEventPayload payload
                && payload.Expiry == LingeringEffectExpiry.Run);
        Assert.Contains(
            events,
                value => value.Kind == EngineEventKinds.CardMoved
                && value.Payload is CardMovedEventPayload payload
                && payload.CardId == mayflyId
                && payload.ToZoneId == ended.GetZone(ZoneKind.RunnerHeap).Id);
    }

    [Fact]
    public void MayflyUnusedOrPumpOnlyRemainsInstalled()
    {
        (GameState unusedPreRun, CardInstanceId unusedId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.Mayfly,
            iceDefinition: FixtureIceBehaviorSpecs.EndRunFirst);
        GameState unusedEncounter = EncounterBreakerTests.ReachEncounterWindow(unusedPreRun);
        (GameState unusedEnded, IReadOnlyList<EngineEvent> unusedEvents) = DriveRunToEnd(unusedEncounter);
        Assert.Contains(unusedId, unusedEnded.GetZone(ZoneKind.RunnerRig).CardIds);
        Assert.DoesNotContain(
            unusedEvents,
            value => value.Kind == EngineEventKinds.CardMoved
                && value.Payload is CardMovedEventPayload payload
                && payload.CardId == unusedId);

        (GameState pumpPreRun, CardInstanceId pumpId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.Mayfly,
            iceDefinition: FixtureIceBehaviorSpecs.EndRunFirst);
        GameState pumpEncounter = EncounterBreakerTests.ReachEncounterWindow(pumpPreRun);
        LegalAction pump = Assert.Single(
            pumpEncounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.PumpBreaker);
        ApplyResult pumped = RunTimingTests.ApplyAction(pumpEncounter, pump);
        Assert.True(pumped.Accepted, pumped.Rejection?.Message);
        Assert.DoesNotContain(
            pumped.State.RunState!.LingeringEffects,
            effect => effect.Kind == RunLingeringEffectKind.IcebreakerRunEndCleanup);

        (GameState pumpEnded, IReadOnlyList<EngineEvent> pumpEvents) = DriveRunToEnd(pumped.State);
        Assert.Contains(pumpId, pumpEnded.GetZone(ZoneKind.RunnerRig).CardIds);
        Assert.DoesNotContain(
            pumpEvents,
            value => value.Kind == EngineEventKinds.CardMoved
                && value.Payload is CardMovedEventPayload payload
                && payload.CardId == pumpId);
    }

    [Fact]
    public void MultipleMayflyBreakUsesRemainOneIdempotentCleanup()
    {
        (GameState preRun, CardInstanceId mayflyId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.Mayfly,
            iceDefinition: FixtureIceBehaviorSpecs.ThreeSubroutines);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);

        for (int index = 0; index < 2; index++)
        {
            LegalAction action = Assert.Single(
                encounter.PendingDecision!.LegalActions,
                candidate => candidate.Kind == ActionKind.BreakSubroutines
                && ((RunBreakParameters)candidate.Parameters).SubroutineOrdinals!.SequenceEqual([index]));
            ApplyResult broken = RunTimingTests.ApplyAction(encounter, action);
            Assert.True(broken.Accepted, broken.Rejection?.Message);
            encounter = broken.State;
            Assert.Single(
                encounter.RunState!.LingeringEffects,
                effect => effect.Kind == RunLingeringEffectKind.IcebreakerRunEndCleanup
                    && effect.SourceCardId == mayflyId);
        }

        (GameState ended, IReadOnlyList<EngineEvent> events) = DriveRunToEnd(encounter);
        Assert.Contains(mayflyId, ended.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.Single(
            events,
            value => value.Kind == EngineEventKinds.CardMoved
                && value.Payload is CardMovedEventPayload payload
                && payload.CardId == mayflyId);
    }

    [Fact]
    public void MovedMayflySourceIsNotTrashedByArmedCleanup()
    {
        (GameState preRun, CardInstanceId mayflyId, _) = EncounterBreakerTests.CreateRunStateWithBreaker(
            FixtureIceBehaviorSpecs.Mayfly,
            iceDefinition: FixtureIceBehaviorSpecs.LoseCreditsOnly);
        GameState encounter = EncounterBreakerTests.ReachEncounterWindow(preRun);
        LegalAction breakAction = Assert.Single(
            encounter.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.BreakSubroutines);
        ApplyResult broken = RunTimingTests.ApplyAction(encounter, breakAction);
        Assert.True(broken.Accepted, broken.Rejection?.Message);

        GameState movement = broken.State;
        while (movement.RunState is { Step: not RunTimingStep.MovementPaidWindow })
        {
            LegalAction pass = Assert.Single(
                movement.PendingDecision!.LegalActions,
                action => action.Kind == ActionKind.PassPriority);
            ApplyResult progressed = RunTimingTests.ApplyAction(movement, pass);
            Assert.True(progressed.Accepted, progressed.Rejection?.Message);
            movement = progressed.State;
        }

        GameState moved = MoveCardToHeap(movement, mayflyId);
        (GameState ended, IReadOnlyList<EngineEvent> events) = DriveRunToEnd(moved);
        Assert.Contains(mayflyId, ended.GetZone(ZoneKind.RunnerHeap).CardIds);
        Assert.DoesNotContain(
            events,
            value => value.Kind == EngineEventKinds.CardMoved
                && value.Payload is CardMovedEventPayload payload
                && payload.CardId == mayflyId);
    }

    private static GameState AddRigCard(GameState state, CardDefinitionId definitionId)
    {
        ZoneState rig = state.GetZone(ZoneKind.RunnerRig);
        StableIdAllocator allocator = new(state.Allocator);
        CardInstanceId cardId = allocator.NextCardInstanceId();
        CardInstanceState card = new(cardId, definitionId, rig.Id, PlayerSide.Runner, faceUp: true);
        return new GameState(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards.Append(card),
            state.Zones.Select(zone => zone.Id == rig.Id
                ? new ZoneState(rig.Id, rig.Kind, rig.OwnerSide, rig.ServerId, rig.CardIds.Append(cardId).ToArray())
                : zone),
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            allocator.State,
            state.SchemaVersion,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            state.RunState);
    }

    private static GameState WithRunnerClicks(GameState state, int clicks)
    {
        PlayerState runner = state.Players.Single(player => player.Side == PlayerSide.Runner);
        PlayerState replacement = new(runner.Id, runner.Side, runner.Credits, clicks, runner.AgendaPoints);
        return new GameState(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players.Select(player => player.Id == runner.Id ? replacement : player),
            state.Cards,
            state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.SchemaVersion,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            state.RunState);
    }

    private static GameState WithRunState(GameState state, RunState run)
    {
        return new GameState(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards,
            state.Zones,
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.SchemaVersion,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            run);
    }

    private static GameState MoveCardToHeap(GameState state, CardInstanceId cardId)
    {
        CardInstanceState card = state.GetCard(cardId);
        ZoneState source = state.GetZone(card.ZoneId);
        ZoneState heap = state.GetZone(ZoneKind.RunnerHeap);
        CardInstanceState movedCard = new(
            card.Id,
            card.DefinitionId,
            heap.Id,
            card.OwnerSide,
            faceUp: true,
            rezzed: card.Rezzed,
            advancementCounters: card.AdvancementCounters);
        return new GameState(
            state.GameId,
            state.RulesVersion,
            state.CardDataVersion,
            state.Players,
            state.Cards.Select(candidate => candidate.Id == cardId ? movedCard : candidate),
            state.Zones.Select(zone => zone.Id == source.Id
                ? new ZoneState(source.Id, source.Kind, source.OwnerSide, source.ServerId, source.CardIds.Where(id => id != cardId).ToArray())
                : zone.Id == heap.Id
                    ? new ZoneState(heap.Id, heap.Kind, heap.OwnerSide, heap.ServerId, heap.CardIds.Append(cardId).ToArray())
                    : zone),
            state.Servers,
            state.Turn,
            state.Outcome,
            state.RandomState,
            state.Allocator,
            state.SchemaVersion,
            state.DecisionRevision,
            state.EventSequence,
            state.ResolutionStack,
            state.PendingDecision,
            state.ActionSequence,
            state.InitialSeed,
            state.RunState);
    }

    private static (GameState State, IReadOnlyList<EngineEvent> Events) DriveRunToEnd(GameState state)
    {
        var events = new List<EngineEvent>();
        for (int guard = 0; state.RunState is not null && guard < 64; guard++)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction next = pending.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ContinueRun)
                ?? pending.LegalActions.Single(action => action.Kind == ActionKind.PassPriority);
            ApplyResult result = RunTimingTests.ApplyAction(state, next);
            Assert.True(result.Accepted, result.Rejection?.Message);
            events.AddRange(result.Events);
            state = result.State;
        }

        Assert.Null(state.RunState);
        return (state, events);
    }
}
