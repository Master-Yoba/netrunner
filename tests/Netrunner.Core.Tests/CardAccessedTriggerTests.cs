using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Focused C2b-5 coverage for real CardAccessed occurrences.</summary>
public sealed class CardAccessedTriggerTests
{
    [Fact]
    public void RealCardAccessedTriggerRunsAfterAccessBeforeMidAccessWindow()
    {
        Fixture fixture = CreateFixture(TriggerMode.Mandatory);
        AssertTypedAgendaSubjectFilter(fixture);
        GameState action = OpenRunnerAction(fixture);
        LegalAction startRun = Assert.Single(
            action.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.StartRun
                && Assert.IsType<RunServerParameters>(candidate.Parameters).ServerId == fixture.ServerId);

        var stream = new List<EngineEvent>();
        ApplyResult started = Apply(fixture, action, startRun);
        Assert.True(started.Accepted, started.Rejection?.Message);
        stream.AddRange(started.Events);
        ApplyResult firstPass = ApplyPass(fixture, started.State);
        stream.AddRange(firstPass.Events);
        ApplyResult secondPass = ApplyPass(fixture, firstPass.State);
        stream.AddRange(secondPass.Events);

        Assert.Equal(RunTimingStep.BreachCandidateSelection, secondPass.State.RunState?.Step);
        LegalAction chooseAgenda = Assert.Single(
            secondPass.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.ChooseBreachCandidate
                && Assert.IsType<BreachCandidateParameters>(candidate.Parameters).CardId == fixture.AgendaId);
        ApplyResult accessed = Apply(fixture, secondPass.State, chooseAgenda);
        Assert.True(accessed.Accepted, accessed.Rejection?.Message);
        stream.AddRange(accessed.Events);

        Assert.Equal(
            [
                EngineEventKinds.BreachCandidateChosen,
                EngineEventKinds.CardAccessed,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.RunTimingWindowOpened,
            ],
            accessed.Events.Select(@event => @event.Kind));
        CardAccessedEventPayload accessEvent = Assert.IsType<CardAccessedEventPayload>(
            accessed.Events.Single(@event => @event.Kind == EngineEventKinds.CardAccessed).Payload);
        Assert.Equal(fixture.AgendaId, accessEvent.CardId);
        Assert.Equal(fixture.ServerId, accessEvent.ServerId);
        CreditsAdjustedEventPayload triggerEvent = Assert.IsType<CreditsAdjustedEventPayload>(
            accessed.Events.Single(@event => @event.Kind == EngineEventKinds.CreditsAdjusted).Payload);
        Assert.Equal(2, triggerEvent.Delta);
        Assert.Equal(2, accessed.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(
            [fixture.AgendaId],
            accessed.State.RunState!.ChosenAccessCards.ToArray());
        Assert.Equal(
            [fixture.AgendaId],
            accessed.State.RunState.AccessedCards.ToArray());
        Assert.Equal(RunTimingStep.BreachAccessAbility, accessed.State.RunState.Step);
        Assert.Equal(DecisionKind.RunTiming, accessed.State.PendingDecision?.Kind);
        Assert.Single(
            accessed.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.NoAccessAction);

        // The inactive same-definition source and the active unknown source do
        // not contribute a second trigger invocation: exactly one +2 credit
        // event was emitted for the active external Runner source.
        Assert.Equal(
            1,
            accessed.Events.Count(
                @event => @event.Kind == EngineEventKinds.CreditsAdjusted));
        Assert.Equal(
            1,
            stream.Count(@event => @event.Kind == EngineEventKinds.CardAccessed));
        Assert.Equal(
            1,
            stream.Count(@event => @event.Kind == EngineEventKinds.BreachCandidateChosen));

        LegalAction noAction = Assert.Single(
            accessed.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.NoAccessAction);
        ApplyResult ended = Apply(fixture, accessed.State, noAction);
        Assert.True(ended.Accepted, ended.Rejection?.Message);
        stream.AddRange(ended.Events);

        Assert.Null(ended.State.RunState);
        Assert.Single(ended.State.ResolutionStack);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(ended.State.ResolutionStack[0]).Stage);
        Assert.Contains(ended.Events, @event => @event.Kind == EngineEventKinds.AgendaStolen);
        Assert.Contains(ended.Events, @event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.Equal(1, stream.Count(@event => @event.Kind == EngineEventKinds.CardAccessed));
        Assert.Equal(1, stream.Count(@event => @event.Kind == EngineEventKinds.BreachCandidateChosen));
        Assert.Empty(RunTimingTraceValidator.Validate(stream).Problems);
        Assert.Empty(StateInvariantValidator.Collect(accessed.State, fixture.Behavior, fixture.Runtime));
        Assert.Empty(StateInvariantValidator.Collect(ended.State, fixture.Behavior, fixture.Runtime));
    }

    [Fact]
    public void OptionalCardAccessedTriggerRoundTripsAtAccessWindowWithoutRepeatingAccess()
    {
        Fixture fixture = CreateFixture(TriggerMode.Optional);
        GameState action = OpenRunnerAction(fixture);
        LegalAction startRun = Assert.Single(
            action.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.StartRun
                && Assert.IsType<RunServerParameters>(candidate.Parameters).ServerId == fixture.ServerId);

        var stream = new List<EngineEvent>();
        ApplyResult started = Apply(fixture, action, startRun);
        Assert.True(started.Accepted, started.Rejection?.Message);
        stream.AddRange(started.Events);
        ApplyResult firstPass = ApplyPass(fixture, started.State);
        stream.AddRange(firstPass.Events);
        ApplyResult secondPass = ApplyPass(fixture, firstPass.State);
        stream.AddRange(secondPass.Events);

        LegalAction chooseAgenda = Assert.Single(
            secondPass.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.ChooseBreachCandidate
                && Assert.IsType<BreachCandidateParameters>(candidate.Parameters).CardId == fixture.AgendaId);
        ApplyResult opened = Apply(fixture, secondPass.State, chooseAgenda);
        Assert.True(opened.Accepted, opened.Rejection?.Message);
        stream.AddRange(opened.Events);

        Assert.Equal(DecisionKind.TriggerChoice, opened.State.PendingDecision?.Kind);
        Assert.Equal(RunTimingStep.BreachAccessAbility, opened.State.RunState?.Step);
        Assert.Equal([fixture.AgendaId], opened.State.RunState!.ChosenAccessCards.ToArray());
        Assert.Equal([fixture.AgendaId], opened.State.RunState.AccessedCards.ToArray());
        Assert.Equal(1, opened.Events.Count(@event => @event.Kind == EngineEventKinds.BreachCandidateChosen));
        Assert.Equal(1, opened.Events.Count(@event => @event.Kind == EngineEventKinds.CardAccessed));
        Assert.DoesNotContain(opened.Events, @event => @event.Kind == EngineEventKinds.CreditsAdjusted);

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(json, fixture.Behavior, fixture.Runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));
        Assert.Empty(StateInvariantValidator.Collect(restored, fixture.Behavior, fixture.Runtime));

        PendingDecision triggerDecision = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction decline = Assert.Single(
            triggerDecision.LegalActions,
            candidate => candidate.Kind == ActionKind.DeclineTrigger);
        ApplyResult declined = GameEngine.Apply(
            restored,
            new ActionSubmission(triggerDecision.Owner, triggerDecision.Id, triggerDecision.Revision, decline.Id),
            fixture.Behavior,
            fixture.Runtime);
        Assert.True(declined.Accepted, declined.Rejection?.Message);
        stream.AddRange(declined.Events);

        Assert.Equal(RunTimingStep.BreachAccessAbility, declined.State.RunState?.Step);
        Assert.Equal([fixture.AgendaId], declined.State.RunState!.ChosenAccessCards.ToArray());
        Assert.Equal([fixture.AgendaId], declined.State.RunState.AccessedCards.ToArray());
        Assert.Equal(DecisionKind.RunTiming, declined.State.PendingDecision?.Kind);
        Assert.Single(
            declined.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.NoAccessAction);
        Assert.DoesNotContain(
            declined.Events,
            @event => @event.Kind is EngineEventKinds.BreachCandidateChosen or EngineEventKinds.CardAccessed);
        Assert.Equal(1, stream.Count(@event => @event.Kind == EngineEventKinds.BreachCandidateChosen));
        Assert.Equal(1, stream.Count(@event => @event.Kind == EngineEventKinds.CardAccessed));

        LegalAction noAction = Assert.Single(
            declined.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.NoAccessAction);
        ApplyResult ended = Apply(fixture, declined.State, noAction);
        Assert.True(ended.Accepted, ended.Rejection?.Message);
        stream.AddRange(ended.Events);

        Assert.Null(ended.State.RunState);
        Assert.Contains(ended.Events, @event => @event.Kind == EngineEventKinds.AgendaStolen);
        Assert.Contains(ended.Events, @event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.Equal(1, stream.Count(@event => @event.Kind == EngineEventKinds.BreachCandidateChosen));
        Assert.Equal(1, stream.Count(@event => @event.Kind == EngineEventKinds.CardAccessed));
        Assert.Empty(RunTimingTraceValidator.Validate(stream).Problems);
        Assert.Empty(StateInvariantValidator.Collect(ended.State, fixture.Behavior, fixture.Runtime));
    }

    private static void AssertTypedAgendaSubjectFilter(Fixture fixture)
    {
        Assert.True(fixture.Behavior.TryGet(fixture.SourceDefinition, out CardBehaviorDefinition? source));
        BehaviorTrigger trigger = Assert.Single(source!.Triggers);
        Assert.Equal(OccurrenceKind.CardAccessed, trigger.Occurrence);
        Assert.Equal(BehaviorCardType.Agenda, trigger.SubjectCardFilter?.CardType);
    }

    private static Fixture CreateFixture(TriggerMode mode)
    {
        CardDefinitionId agendaDefinition = new("test:card-accessed-agenda");
        CardDefinitionId sourceDefinition = new("test:card-accessed-source");
        CardInstanceId agendaId = new("card:card-accessed-agenda");
        CardInstanceId sourceId = new("card:card-accessed-source");
        CardInstanceId inactiveId = new("card:card-accessed-inactive");
        CardInstanceId unknownId = new("card:card-accessed-unknown");
        BehaviorTrigger trigger = new(
            "accessed",
            OccurrenceKind.CardAccessed,
            mode,
            BehaviorTriggerScope.Any,
            new BehaviorCardFilter(cardType: BehaviorCardType.Agenda),
            [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 2)]);
        BehaviorCatalog behavior = new BehaviorCatalogBuilder(
            "test:card-accessed-occurrence",
            CardBehaviorSpecCatalog.Default)
            .AddAgenda(new AgendaBehaviorSpec(agendaDefinition, agendaPoints: 1))
            .Add(new CardBehaviorDefinition(sourceDefinition, PlayerSide.Runner, triggers: [trigger]))
            .Build();
        RuntimeCardCatalog runtime = new RuntimeCardCatalogBuilder("runtime:card-accessed-occurrence")
            .Add(new CardRuntimeSpec(
                agendaDefinition,
                PlayerSide.Corp,
                RuntimeCardClass.Agenda,
                agendaPoints: 1,
                advancementRequirement: 2))
            .Add(new CardRuntimeSpec(
                sourceDefinition,
                PlayerSide.Runner,
                RuntimeCardClass.Resource,
                installCost: 0))
            .Build();

        GameState baseState = GameState.CreateNew(
            703,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        var allocator = new StableIdAllocator(baseState.Allocator);
        ServerState remote = new(
            allocator.NextServerId(),
            ServerKind.Remote,
            allocator.NextZoneId() is ZoneId iceZoneId ? iceZoneId : throw new InvalidOperationException(),
            allocator.NextZoneId() is ZoneId rootZoneId ? rootZoneId : throw new InvalidOperationException());
        ZoneState root = new(remote.RootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, remote.Id, [agendaId]);
        ZoneState rig = baseState.GetZone(ZoneKind.RunnerRig);
        ZoneState grip = baseState.GetZone(ZoneKind.RunnerGrip);
        CardInstanceState[] cards =
        [
            new CardInstanceState(agendaId, agendaDefinition, root.Id, PlayerSide.Corp),
            new CardInstanceState(sourceId, sourceDefinition, rig.Id, PlayerSide.Runner, faceUp: true),
            new CardInstanceState(inactiveId, sourceDefinition, grip.Id, PlayerSide.Runner),
            new CardInstanceState(unknownId, new CardDefinitionId("test:card-accessed-unknown"), rig.Id, PlayerSide.Runner, faceUp: true),
        ];
        ZoneState[] zones = baseState.Zones
            .Append(new ZoneState(remote.IceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, remote.Id))
            .Append(root)
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(sourceId).Append(unknownId).ToArray())
                : zone.Id == grip.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(inactiveId).ToArray())
                    : zone)
            .ToArray();
        PlayerId runnerId = baseState.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        PlayerState[] players = baseState.Players
            .Select(player => player.Id == runnerId
                ? new PlayerState(player.Id, player.Side, credits: 0, clicks: 4)
                : player)
            .ToArray();
        GameState state = new(
            baseState.SchemaVersion,
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            players,
            cards,
            zones,
            baseState.Servers.Append(remote).ToArray(),
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, 1),
            baseState.Outcome,
            baseState.RandomState,
            allocator.State,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        StateInvariantValidator.Validate(state, behavior, runtime);
        return new Fixture(state, behavior, runtime, agendaId, sourceDefinition, remote.Id, runnerId);
    }

    private static GameState OpenRunnerAction(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(fixture.State, PlayerSide.Runner, fixture.Behavior, fixture.Runtime);
        for (int step = 0; state.PendingDecision?.Kind != DecisionKind.TurnAction; step++)
        {
            Assert.True(step < 8, "Runner turn did not reach its action window.");
            state = ApplyPass(fixture, state).State;
        }

        return state;
    }

    private static ApplyResult ApplyPass(Fixture fixture, GameState state)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        LegalAction pass = Assert.Single(pending.LegalActions, action => action.Kind == ActionKind.PassPriority);
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pass.Id),
            fixture.Behavior,
            fixture.Runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result;
    }

    private static ApplyResult Apply(Fixture fixture, GameState state, LegalAction action)
    {
        PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
        return GameEngine.Apply(
            state,
            new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
            fixture.Behavior,
            fixture.Runtime);
    }

    private sealed record Fixture(
        GameState State,
        BehaviorCatalog Behavior,
        RuntimeCardCatalog Runtime,
        CardInstanceId AgendaId,
        CardDefinitionId SourceDefinition,
        ServerId ServerId,
        PlayerId RunnerId);
}
