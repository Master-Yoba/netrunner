using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Focused CR 6.9.4g and encounter occurrence coverage.</summary>
public sealed class RunApproachOccurrenceTests
{
    [Fact]
    public void ServerApproachedTriggerRequiresItsSourceToOccupyTheAttackedServer()
    {
        CardDefinitionId sourceDefinition = new("test:server-approached-source");
        CardInstanceId sourceId = new("card:server-approached-source");
        BehaviorTrigger trigger = new(
            "approached",
            OccurrenceKind.ServerApproached,
            TriggerMode.Optional,
            BehaviorTriggerScope.Any,
            subjectCardFilter: null,
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)],
            requireSourceOnAttackedServer: true);
        Assert.True(trigger.RequireSourceOnAttackedServer);
        string triggerJson = EngineJson.Serialize(trigger);
        Assert.Contains("\"requireSourceOnAttackedServer\":true", triggerJson, StringComparison.Ordinal);
        Assert.Equal(trigger, EngineJson.Deserialize<BehaviorTrigger>(triggerJson));
        Assert.Throws<ArgumentException>(() => new BehaviorTrigger(
            "invalid-server-scope",
            OccurrenceKind.RunEnded,
            TriggerMode.Optional,
            BehaviorTriggerScope.Any,
            subjectCardFilter: null,
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)],
            requireSourceOnAttackedServer: true));
        BehaviorCatalog catalog = new BehaviorCatalogBuilder(
            "test:server-approached-occurrence",
            FixtureBehaviorCatalog.Catalog)
            .Add(new CardBehaviorDefinition(sourceDefinition, PlayerSide.Corp, triggers: [trigger]))
            .Build();
        BehaviorTrigger unrestrictedTrigger = new(
            "approached",
            OccurrenceKind.ServerApproached,
            TriggerMode.Optional,
            BehaviorTriggerScope.Any,
            subjectCardFilter: null,
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)]);
        BehaviorCatalog unrestrictedCatalog = new BehaviorCatalogBuilder(
            "test:server-approached-occurrence",
            FixtureBehaviorCatalog.Catalog)
            .Add(new CardBehaviorDefinition(sourceDefinition, PlayerSide.Corp, triggers: [unrestrictedTrigger]))
            .Build();
        Assert.NotEqual(catalog.Fingerprint, unrestrictedCatalog.Fingerprint);
        GameState state = CreateStateWithCorpSource(catalog, sourceDefinition, sourceId);
        ServerId sourceServer = RunTimingTests.RemoteId(state);
        ServerId differentServer = state.Servers.Single(server => server.Kind == ServerKind.RAndD).Id;

        ApplyResult sameServer = CompleteInitiation(state, sourceServer, catalog);

        Assert.True(sameServer.Accepted, sameServer.Rejection?.Message);
        Assert.Equal(RunTimingStep.Success, sameServer.State.RunState?.Step);
        Assert.Equal(DecisionKind.TriggerChoice, sameServer.State.PendingDecision?.Kind);
        Assert.Equal(
            [EngineEventKinds.RunPriorityPassed, EngineEventKinds.ServerApproached],
            sameServer.Events.Select(@event => @event.Kind));
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(sameServer.State.ResolutionStack[^1]);
        Assert.Equal(OccurrenceKind.ServerApproached, checkpoint.Occurrence.Kind);
        Assert.Equal(sourceServer, Assert.IsType<GameplayOccurrenceEffect>(checkpoint.Occurrence.Effect).ServerId);

        ApplyResult different = CompleteInitiation(state, differentServer, catalog);

        Assert.True(different.Accepted, different.Rejection?.Message);
        Assert.Null(different.State.RunState);
        Assert.Null(different.State.PendingDecision);
        Assert.Empty(different.State.ResolutionStack);
        Assert.Equal(
            [
                EngineEventKinds.RunPriorityPassed,
                EngineEventKinds.ServerApproached,
                EngineEventKinds.RunSuccessful,
                EngineEventKinds.BreachStarted,
                EngineEventKinds.RunEnded,
            ],
            different.Events.Select(@event => @event.Kind));
    }

    [Fact]
    public void IceEncounteredSelfTriggerMatchesOnlyTheEncounteredIce()
    {
        CardDefinitionId iceDefinition = new("test:ice-encountered-source");
        BehaviorTrigger trigger = new(
            "encountered-self",
            OccurrenceKind.IceEncountered,
            TriggerMode.Optional,
            BehaviorTriggerScope.Self,
            subjectCardFilter: null,
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 1)],
            requireSourceOnAttackedServer: true);
        BehaviorCatalog catalog = new BehaviorCatalogBuilder(
            "test:ice-encountered-occurrence",
            FixtureBehaviorCatalog.Catalog)
            .AddIce(new IceBehaviorSpec(
                iceDefinition,
                strength: 0,
                iceType: IceType.Barrier,
                rezCost: 0,
                subroutines: [],
                strengthModifiers: []))
            .Add(new CardBehaviorDefinition(iceDefinition, PlayerSide.Corp, triggers: [trigger]))
            .Build();
        GameState state = WithCatalog(
            RunTimingTests.CreateRunState(iceCount: 2, rezzed: true, iceDefinition: iceDefinition),
            catalog);
        ServerId server = RunTimingTests.RemoteId(state);
        CardInstanceId outerIce = state.GetZone(
                state.GetServer(server).IceZoneId)
            .CardIds[^1];
        CardInstanceId innerIce = state.GetZone(
                state.GetServer(server).IceZoneId)
            .CardIds[0];
        Assert.NotEqual(outerIce, innerIce);

        ApplyResult approach = CompleteInitiation(state, server, catalog);
        Assert.True(approach.Accepted, approach.Rejection?.Message);
        ApplyResult encounter = PassTwice(approach.State, catalog);

        Assert.True(encounter.Accepted, encounter.Rejection?.Message);
        Assert.Equal(RunTimingStep.EncounterPaidWindow, encounter.State.RunState?.Step);
        Assert.Equal(DecisionKind.TriggerChoice, encounter.State.PendingDecision?.Kind);
        Assert.Equal(
            [EngineEventKinds.RunPriorityPassed, EngineEventKinds.EncounterInitialized],
            encounter.Events.Select(@event => @event.Kind));
        EffectCheckpointFrame checkpoint = Assert.IsType<EffectCheckpointFrame>(encounter.State.ResolutionStack[^1]);
        Assert.Equal(OccurrenceKind.IceEncountered, checkpoint.Occurrence.Kind);
        Assert.Equal(outerIce, checkpoint.Occurrence.SourceCardId);
        TriggerInvocation invocation = Assert.Single(checkpoint.TriggerQueue);
        Assert.Equal(outerIce, invocation.SourceCardId);
        Assert.NotEqual(innerIce, invocation.SourceCardId);
    }

    [Fact]
    public void ServerApproachedNoTriggerPathEmitsApproachBeforeSuccess()
    {
        GameState state = RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);
        ServerId server = RunTimingTests.RemoteId(state);

        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(
            state,
            server,
            FixtureBehaviorCatalog.Catalog);
        ApplyResult first = ApplyPass(start.State, FixtureBehaviorCatalog.Catalog);
        ApplyResult completed = ApplyPass(first.State, FixtureBehaviorCatalog.Catalog);

        Assert.True(completed.Accepted, completed.Rejection?.Message);
        IReadOnlyList<EngineEvent> stream = start.Events
            .Concat(first.Events)
            .Concat(completed.Events)
            .ToArray();
        Assert.Equal(
            [
                EngineEventKinds.RunInitiated,
                EngineEventKinds.RunPriorityPassed,
                EngineEventKinds.RunPriorityPassed,
                EngineEventKinds.ServerApproached,
                EngineEventKinds.RunSuccessful,
                EngineEventKinds.BreachStarted,
                EngineEventKinds.RunEnded,
            ],
            stream
                .Where(@event => @event.Kind != EngineEventKinds.RunTimingWindowOpened)
                .Select(@event => @event.Kind));
        Assert.Contains(stream, @event => @event.Kind == EngineEventKinds.RunTimingWindowOpened);
        Assert.Null(completed.State.RunState);
        Assert.Null(completed.State.PendingDecision);
        Assert.Empty(completed.State.ResolutionStack);
    }

    private static ApplyResult CompleteInitiation(
        GameState state,
        ServerId server,
        IBehaviorCatalog catalog)
    {
        GameEngine.RunStartResult start = GameEngine.BeginRunWithEvents(state, server, catalog);
        ApplyResult first = ApplyPass(start.State, catalog);
        return ApplyPass(first.State, catalog);
    }

    private static ApplyResult PassTwice(GameState state, IBehaviorCatalog catalog)
    {
        ApplyResult first = ApplyPass(state, catalog);
        return ApplyPass(first.State, catalog);
    }

    private static ApplyResult ApplyPass(GameState state, IBehaviorCatalog catalog)
    {
        PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.PassPriority);
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, pass.Id),
            catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result;
    }

    private static GameState CreateStateWithCorpSource(
        BehaviorCatalog catalog,
        CardDefinitionId sourceDefinition,
        CardInstanceId sourceId)
    {
        GameState baseline = RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);
        ServerId server = RunTimingTests.RemoteId(baseline);
        ZoneState sourceZone = baseline.Zones.Single(zone =>
            zone.Kind == ZoneKind.ServerRoot && zone.ServerId == server);
        CardInstanceState source = new(
            sourceId,
            sourceDefinition,
            sourceZone.Id,
            PlayerSide.Corp,
            faceUp: true,
            rezzed: true);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == sourceZone.Id
                ? new ZoneState(
                    zone.Id,
                    zone.Kind,
                    zone.OwnerSide,
                    zone.ServerId,
                    zone.CardIds.Append(sourceId).ToArray())
                : zone)
            .ToArray();
        return WithCatalog(
            new GameState(
                baseline.SchemaVersion,
                baseline.GameId,
                baseline.RulesVersion,
                baseline.CardDataVersion,
                baseline.Players,
                baseline.Cards.Append(source).ToArray(),
                zones,
                baseline.Servers,
                baseline.Turn,
                baseline.Outcome,
                baseline.RandomState,
                baseline.Allocator,
                baseline.DecisionRevision,
                baseline.EventSequence,
                baseline.ResolutionStack,
                baseline.PendingDecision,
                baseline.ActionSequence,
                baseline.InitialSeed,
                baseline.RunState,
                baseline.RulesState),
            catalog);
    }

    private static GameState WithCatalog(GameState state, BehaviorCatalog catalog) => new(
        state.SchemaVersion,
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
        state.DecisionRevision,
        state.EventSequence,
        state.ResolutionStack,
        state.PendingDecision,
        state.ActionSequence,
        state.InitialSeed,
        state.RunState,
        state.RulesState,
        catalog.Version,
        catalog.Fingerprint,
        state.RuntimeCardCatalogVersion,
        state.RuntimeCardCatalogFingerprint);
}
