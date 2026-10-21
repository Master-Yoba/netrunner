using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Focused C2b-4 coverage for real agenda score/steal occurrences.</summary>
public sealed class AgendaOccurrenceTriggerTests
{
    [Fact]
    public void RealCorpScoreDispatchesAfterCleanupToTheActionPriorityParent()
    {
        Fixture fixture = CreateScoreFixture(TriggerMode.Mandatory);
        GameState timing = OpenCorpTiming(fixture);
        LegalAction score = Assert.Single(
            timing.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.ScoreAgenda);

        ApplyResult result = Apply(fixture, timing, score);

        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(
            [
                EngineEventKinds.CardMoved,
                EngineEventKinds.AgendaScored,
                EngineEventKinds.CreditsAdjusted,
                EngineEventKinds.TurnTimingWindowOpened,
            ],
            result.Events.Select(@event => @event.Kind));
        Assert.Equal(2, result.State.GetPlayer(fixture.CorpId).Credits);
        Assert.Equal(1, result.State.GetPlayer(fixture.CorpId).AgendaPoints);
        Assert.Equal(result.State.GetZone(ZoneKind.CorpScore).Id, result.State.GetCard(fixture.AgendaId).ZoneId);
        Assert.True(result.State.GetCard(fixture.AgendaId).FaceUp);
        Assert.DoesNotContain(result.State.Servers, server => server.Id == fixture.ServerId);
        Assert.Single(result.State.ResolutionStack);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(result.State.ResolutionStack[0]).Stage);
        Assert.Equal(DecisionKind.TurnTiming, result.State.PendingDecision?.Kind);
        Assert.Empty(RunTimingTraceValidator.Validate(result.Events).Problems);
        Assert.Empty(StateInvariantValidator.Collect(result.State, fixture.Behavior, fixture.Runtime));
    }

    [Fact]
    public void OptionalRealCorpScoreRoundTripsAfterMoveAndPointsWithoutRepeatingThem()
    {
        Fixture fixture = CreateScoreFixture(TriggerMode.Optional);
        GameState timing = OpenCorpTiming(fixture);
        LegalAction score = Assert.Single(
            timing.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.ScoreAgenda);

        ApplyResult opened = Apply(fixture, timing, score);

        Assert.True(opened.Accepted, opened.Rejection?.Message);
        Assert.Equal(DecisionKind.TriggerChoice, opened.State.PendingDecision?.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(opened.State.ResolutionStack[0]).Stage);
        Assert.Equal(1, opened.State.GetPlayer(fixture.CorpId).AgendaPoints);
        Assert.Equal(1, opened.State.GetZone(ZoneKind.CorpScore).CardIds.Count(id => id == fixture.AgendaId));
        Assert.Equal(1, opened.Events.Count(@event => @event.Kind == EngineEventKinds.CardMoved));
        Assert.Equal(1, opened.Events.Count(@event => @event.Kind == EngineEventKinds.AgendaScored));

        string json = EngineJson.Serialize(opened.State);
        GameState restored = EngineJson.DeserializeState(json, fixture.Behavior, fixture.Runtime);
        Assert.Equal(json, EngineJson.Serialize(restored));
        Assert.Equal(DeterministicHash.ForState(opened.State), DeterministicHash.ForState(restored));

        PendingDecision decision = Assert.IsType<PendingDecision>(restored.PendingDecision);
        LegalAction decline = Assert.Single(
            decision.LegalActions,
            action => action.Kind == ActionKind.DeclineTrigger);
        ApplyResult declined = GameEngine.Apply(
            restored,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, decline.Id),
            fixture.Behavior,
            fixture.Runtime);

        Assert.True(declined.Accepted, declined.Rejection?.Message);
        Assert.Equal(DecisionKind.TurnTiming, declined.State.PendingDecision?.Kind);
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(declined.State.ResolutionStack[0]).Stage);
        Assert.Equal(1, declined.State.GetPlayer(fixture.CorpId).AgendaPoints);
        Assert.Equal(1, declined.State.GetZone(ZoneKind.CorpScore).CardIds.Count(id => id == fixture.AgendaId));
        Assert.DoesNotContain(declined.Events, @event => @event.Kind is EngineEventKinds.CardMoved or EngineEventKinds.AgendaScored);
        Assert.Empty(StateInvariantValidator.Collect(declined.State, fixture.Behavior, fixture.Runtime));
    }

    [Fact]
    public void RealRunnerStealDispatchesBeforeTheNextBreachCandidate()
    {
        Fixture fixture = CreateStealFixture();
        GameState action = OpenRunnerAction(fixture);
        LegalAction startRun = Assert.Single(
            action.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.StartRun
                && Assert.IsType<RunServerParameters>(candidate.Parameters).ServerId == fixture.ServerId);
        ApplyResult started = Apply(fixture, action, startRun);
        Assert.True(started.Accepted, started.Rejection?.Message);
        var stream = new List<EngineEvent>(started.Events);

        ApplyResult firstPass = ApplyPass(fixture, started.State);
        stream.AddRange(firstPass.Events);
        ApplyResult secondPass = ApplyPass(fixture, firstPass.State);
        stream.AddRange(secondPass.Events);
        GameState candidateSelection = secondPass.State;
        Assert.Equal(RunTimingStep.BreachCandidateSelection, candidateSelection.RunState?.Step);
        LegalAction access = Assert.Single(
            candidateSelection.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.ChooseBreachCandidate
                && Assert.IsType<BreachCandidateParameters>(candidate.Parameters).CardId == fixture.AgendaId);
        ApplyResult accessed = Apply(fixture, candidateSelection, access);
        Assert.True(accessed.Accepted, accessed.Rejection?.Message);
        stream.AddRange(accessed.Events);
        LegalAction noAction = Assert.Single(
            accessed.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.NoAccessAction);

        ApplyResult stolen = Apply(fixture, accessed.State, noAction);
        stream.AddRange(stolen.Events);

        Assert.True(stolen.Accepted, stolen.Rejection?.Message);
        Assert.Equal(RunTimingStep.BreachCandidateSelection, stolen.State.RunState?.Step);
        Assert.Equal(DecisionKind.RunTiming, stolen.State.PendingDecision?.Kind);
        Assert.Equal(2, stolen.State.GetPlayer(fixture.RunnerId).Credits);
        Assert.Equal(1, stolen.State.GetPlayer(fixture.RunnerId).AgendaPoints);
        Assert.Contains(fixture.AgendaId, stolen.State.GetZone(ZoneKind.RunnerScore).CardIds);
        Assert.Contains(
            stolen.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.ChooseBreachCandidate);
        int agendaStolen = stolen.Events.ToList().FindIndex(@event => @event.Kind == EngineEventKinds.AgendaStolen);
        int triggerCredit = stolen.Events.ToList().FindIndex(
            @event => @event.Kind == EngineEventKinds.CreditsAdjusted
                && Assert.IsType<CreditsAdjustedEventPayload>(@event.Payload).Delta == 2);
        Assert.True(agendaStolen >= 0);
        Assert.True(triggerCredit > agendaStolen);
        Assert.DoesNotContain(stolen.Events, @event => @event.Kind == EngineEventKinds.RunEnded);

        // The first steal leaves the run at the next candidate boundary. Finish
        // that breach as well so the complete real-turn stream can be checked
        // by RunTimingTraceValidator.
        LegalAction secondAccess = Assert.Single(
            stolen.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.ChooseBreachCandidate
                && Assert.IsType<BreachCandidateParameters>(candidate.Parameters).CardId
                    == fixture.AdditionalAgendaIds[0]);
        ApplyResult accessedSecond = Apply(fixture, stolen.State, secondAccess);
        Assert.True(accessedSecond.Accepted, accessedSecond.Rejection?.Message);
        stream.AddRange(accessedSecond.Events);
        LegalAction noActionSecond = Assert.Single(
            accessedSecond.State.PendingDecision!.LegalActions,
            candidate => candidate.Kind == ActionKind.NoAccessAction);
        ApplyResult ended = Apply(fixture, accessedSecond.State, noActionSecond);
        Assert.True(ended.Accepted, ended.Rejection?.Message);
        stream.AddRange(ended.Events);
        Assert.Null(ended.State.RunState);
        Assert.Single(ended.State.ResolutionStack);
        Assert.Equal(
            TurnFrameStage.ActionPriority,
            Assert.IsType<TurnFrame>(ended.State.ResolutionStack[0]).Stage);
        Assert.Contains(ended.Events, @event => @event.Kind == EngineEventKinds.RunEnded);

        Assert.Empty(RunTimingTraceValidator.Validate(stream).Problems);
        Assert.Empty(StateInvariantValidator.Collect(stolen.State, fixture.Behavior, fixture.Runtime));
        Assert.Empty(StateInvariantValidator.Collect(ended.State, fixture.Behavior, fixture.Runtime));
    }

    private static Fixture CreateScoreFixture(TriggerMode mode)
    {
        CardDefinitionId agendaDefinition = new("test:agenda-occurrence-score");
        CardDefinitionId sourceDefinition = new("test:agenda-occurrence-score-source");
        CardInstanceId agendaId = new("card:agenda-occurrence-score");
        CardInstanceId sourceId = new("card:agenda-occurrence-score-source");
        CardInstanceId inactiveId = new("card:agenda-occurrence-score-inactive");
        CardInstanceId unknownId = new("card:agenda-occurrence-score-unknown");
        BehaviorTrigger trigger = Trigger(
            "score",
            mode,
            BehaviorTriggerScope.Controller,
            new BehaviorCardFilter(cardType: BehaviorCardType.Agenda),
            amount: 2);
        BehaviorCatalog behavior = CreateBehaviorCatalog(
            agendaDefinition,
            new CardBehaviorDefinition(sourceDefinition, PlayerSide.Corp, triggers: [trigger]));
        RuntimeCardCatalog runtime = new RuntimeCardCatalogBuilder("runtime:agenda-occurrence-score")
            .Add(new CardRuntimeSpec(
                agendaDefinition,
                PlayerSide.Corp,
                RuntimeCardClass.Agenda,
                agendaPoints: 1,
                advancementRequirement: 2))
            .Add(new CardRuntimeSpec(
                sourceDefinition,
                PlayerSide.Corp,
                RuntimeCardClass.Asset,
                rezCost: 0))
            .Build();

        GameState baseState = GameState.CreateNew(
            701,
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
        ZoneState hqRoot = baseState.GetZone(baseState.Servers.Single(server => server.Kind == ServerKind.Headquarters).RootZoneId);
        ZoneState hand = baseState.GetZone(ZoneKind.CorpHand);
        ZoneState deck = baseState.GetZone(ZoneKind.CorpDeck);
        CardInstanceState[] cards =
        [
            new CardInstanceState(agendaId, agendaDefinition, remote.RootZoneId, PlayerSide.Corp, advancementCounters: 2),
            new CardInstanceState(sourceId, sourceDefinition, hqRoot.Id, PlayerSide.Corp, faceUp: true, rezzed: true),
            new CardInstanceState(inactiveId, sourceDefinition, hand.Id, PlayerSide.Corp),
            new CardInstanceState(unknownId, new CardDefinitionId("test:agenda-occurrence-score-unknown"), hqRoot.Id, PlayerSide.Corp, faceUp: true, rezzed: true),
            new CardInstanceState(new("card:agenda-occurrence-score-deck"), new("test:agenda-occurrence-score-deck"), deck.Id, PlayerSide.Corp),
        ];
        ZoneState[] zones = baseState.Zones
            .Append(new ZoneState(remote.IceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, remote.Id))
            .Append(new ZoneState(remote.RootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, remote.Id, [agendaId]))
            .Select(zone => zone.Id == hqRoot.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Concat([sourceId, unknownId]).ToArray())
                : zone.Id == hand.Id
                    ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(inactiveId).ToArray())
                    : zone.Id == deck.Id
                        ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(cards[^1].Id).ToArray())
                        : zone)
            .ToArray();
        PlayerId corpId = baseState.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        PlayerState[] players = baseState.Players
            .Select(player => player.Id == corpId
                ? new PlayerState(player.Id, player.Side, credits: 0, clicks: 3)
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
            new TurnState(PlayerSide.Corp, TurnPhase.Corp, 1),
            baseState.Outcome,
            baseState.RandomState,
            allocator.State,
            behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        StateInvariantValidator.Validate(state, behavior, runtime);
        return new Fixture(state, behavior, runtime, agendaId, [], remote.Id, corpId, baseState.Players.Single(player => player.Side == PlayerSide.Runner).Id);
    }

    private static Fixture CreateStealFixture()
    {
        CardDefinitionId agendaDefinition = new("test:agenda-occurrence-steal");
        CardDefinitionId sourceDefinition = new("test:agenda-occurrence-steal-source");
        CardInstanceId agendaId = new("card:agenda-occurrence-steal");
        CardInstanceId secondAgendaId = new("card:agenda-occurrence-steal-second");
        CardInstanceId sourceId = new("card:agenda-occurrence-steal-source");
        BehaviorCatalog behavior = CreateBehaviorCatalog(
            agendaDefinition,
            new CardBehaviorDefinition(
                sourceDefinition,
                PlayerSide.Runner,
                triggers:
                [
                    Trigger(
                        "steal",
                        TriggerMode.Mandatory,
                        BehaviorTriggerScope.Any,
                        new BehaviorCardFilter(cardType: BehaviorCardType.Agenda),
                        amount: 2),
                ]));
        RuntimeCardCatalog runtime = new RuntimeCardCatalogBuilder("runtime:agenda-occurrence-steal")
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
            702,
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
        ZoneState root = new ZoneState(remote.RootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, remote.Id, [agendaId, secondAgendaId]);
        ZoneState rig = baseState.GetZone(ZoneKind.RunnerRig);
        CardInstanceState[] cards =
        [
            new CardInstanceState(agendaId, agendaDefinition, root.Id, PlayerSide.Corp),
            new CardInstanceState(secondAgendaId, agendaDefinition, root.Id, PlayerSide.Corp),
            new CardInstanceState(sourceId, sourceDefinition, rig.Id, PlayerSide.Runner, faceUp: true),
        ];
        ZoneState[] zones = baseState.Zones
            .Append(new ZoneState(remote.IceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, remote.Id))
            .Append(root)
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Append(sourceId).ToArray())
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
        return new Fixture(state, behavior, runtime, agendaId, [secondAgendaId], remote.Id, baseState.Players.Single(player => player.Side == PlayerSide.Corp).Id, runnerId);
    }

    private static BehaviorCatalog CreateBehaviorCatalog(
        CardDefinitionId agendaDefinition,
        params CardBehaviorDefinition[] definitions) => new BehaviorCatalogBuilder(
        "test:agenda-occurrence",
            FixtureBehaviorCatalog.Catalog)
        .AddAgenda(new AgendaBehaviorSpec(agendaDefinition, agendaPoints: 1))
        .Add(definitions)
        .Build();

    private static BehaviorTrigger Trigger(
        string key,
        TriggerMode mode,
        BehaviorTriggerScope scope,
        BehaviorCardFilter? subjectFilter,
        int amount) => new(
        key,
        OccurrenceKind.AgendaScoredOrStolen,
        mode,
        scope,
        subjectFilter,
        [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount)]);

    private static GameState OpenCorpTiming(Fixture fixture)
    {
        GameState state = GameEngine.BeginTurn(fixture.State, PlayerSide.Corp, fixture.Behavior, fixture.Runtime);
        Assert.Equal(DecisionKind.TurnTiming, state.PendingDecision?.Kind);
        state = ApplyPass(fixture, state).State;
        state = ApplyPass(fixture, state).State;
        Assert.Equal(TurnFrameStage.ActionPriority, Assert.IsType<TurnFrame>(state.ResolutionStack[0]).Stage);
        return state;
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
        IReadOnlyList<CardInstanceId> AdditionalAgendaIds,
        ServerId ServerId,
        PlayerId CorpId,
        PlayerId RunnerId);
}
