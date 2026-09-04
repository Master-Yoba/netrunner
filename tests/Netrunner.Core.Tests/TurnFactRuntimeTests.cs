using Xunit;

namespace Netrunner.Core.Tests;

/// <summary>Focused ENG-413 production recording checks on formal turn state.</summary>
public sealed class TurnFactRuntimeTests
{
    [Fact]
    public void RealJackedOutRunRetainsAttemptedServerWithoutSuccessfulFact()
    {
        GameState actionState = OpenRunnerAction(RunTimingTests.CreateRunState(iceCount: 1, rezzed: false));
        ServerId server = RunTimingTests.RemoteId(actionState);
        PendingDecision actionDecision = Assert.IsType<PendingDecision>(actionState.PendingDecision);
        LegalAction startRun = Assert.Single(
            actionDecision.LegalActions,
            action => action.Kind == ActionKind.StartRun
                && Assert.IsType<RunServerParameters>(action.Parameters).ServerId == server);
        ApplyResult started = RunTimingTests.ApplyAction(actionState, startRun);
        Assert.True(started.Accepted, started.Rejection?.Message);
        var stream = new List<EngineEvent>(started.Events);

        GameState cursor = started.State;
        for (int step = 0; cursor.RunState?.Step != RunTimingStep.JackOutDecision; step++)
        {
            Assert.True(step < 32, "The real run did not reach its jack-out decision.");
            ApplyResult pass = RunTimingTests.ApplyKind(cursor, ActionKind.PassPriority);
            Assert.True(pass.Accepted, pass.Rejection?.Message);
            stream.AddRange(pass.Events);
            cursor = pass.State;
        }

        ApplyResult ended = RunTimingTests.ApplyKind(cursor, ActionKind.JackOut);
        Assert.True(ended.Accepted, ended.Rejection?.Message);
        stream.AddRange(ended.Events);
        Assert.Null(ended.State.RunState);
        Assert.Contains(ended.Events, @event => @event.Kind == EngineEventKinds.RunEnded);
        RunEndedEventPayload runEnded = Assert.IsType<RunEndedEventPayload>(
            ended.Events.Single(@event => @event.Kind == EngineEventKinds.RunEnded).Payload);
        Assert.False(runEnded.Successful);
        Assert.Equal([server], ended.State.Turn.ThisTurnFacts!.AttemptedRunServerIds);
        Assert.Empty(ended.State.Turn.ThisTurnFacts.SuccessfulRunServerIds);
        Assert.False(ended.State.Turn.ThisTurnFacts.SuccessfulRun);
        Assert.Empty(RunTimingTraceValidator.Validate(stream).Problems);
    }

    [Fact]
    public void FormalEffectResolutionRecordsRunnerTagAddAndRemoveInOrder()
    {
        GameState seed = RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);
        PlayerId runner = seed.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        TurnFactsState facts = new(
            1,
            PlayerSide.Runner,
            seed.Turn.TurnNumber,
            clicksAllotted: 4,
            clicksLost: 0,
            turnBegan: true,
            mandatoryDrawPerformed: false,
            actionPhaseEnded: false,
            discardPhaseEnded: false);
        GameState formal = WithRunnerFacts(seed, facts);

        GameState result = GameEngine.ResolveEffects(
            formal,
            runner,
            new CostBundle(),
            new EffectBundle(
                new TagAdjustmentEffect(runner, 1),
                new TagAdjustmentEffect(runner, -1)));

        Assert.Equal(0, result.RulesState!.Players.Single(player => player.PlayerId == runner).Tags);
        Assert.Equal(
            [new TagTurnChange(runner, 1), new TagTurnChange(runner, -1)],
            result.Turn.ThisTurnFacts!.TagTurnChanges);
        Assert.Empty(StateInvariantValidator.Collect(result));
    }

    [Fact]
    public void FormalDamageRecordsEachSelectedGripCardInPhysicalOrder()
    {
        GameState baseState = RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);
        CardInstanceId[] gripCards =
        [
            new("card:turn-fact-damage-a"),
            new("card:turn-fact-damage-b"),
            new("card:turn-fact-damage-c"),
        ];
        GameState state = AddRunnerGripCards(
            WithRunnerFacts(
                baseState,
                new TurnFactsState(
                    1,
                    PlayerSide.Runner,
                    baseState.Turn.TurnNumber,
                    clicksAllotted: 4,
                    clicksLost: 0,
                    turnBegan: true,
                    mandatoryDrawPerformed: false,
                    actionPhaseEnded: false,
                    discardPhaseEnded: false)),
            gripCards);
        PlayerId runner = state.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        CardInstanceId[] heapBefore = state.GetZone(ZoneKind.RunnerHeap).CardIds.ToArray();

        GameState result = GameEngine.ResolveEffects(
            state,
            runner,
            new CostBundle(),
            new EffectBundle(new DamageEffect(runner, DamageKind.Net, 2)));

        CardInstanceId[] moved = result.GetZone(ZoneKind.RunnerHeap).CardIds
            .Except(heapBefore)
            .ToArray();
        Assert.Equal(2, moved.Length);
        Assert.Equal(moved, result.Turn.ThisTurnFacts!.TrashedCardOccurrenceIds);
        Assert.All(moved, cardId => Assert.Contains(cardId, gripCards));
        Assert.Single(result.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Empty(StateInvariantValidator.Collect(result));
    }

    [Fact]
    public void StandaloneRunRecordingRemainsFactsFreeAndKeepsLegacyTurnShape()
    {
        GameState initial = RunTimingTests.CreateRunState(iceCount: 0, rezzed: false);
        ulong allocatorPosition = initial.Allocator.NextSequence;
        GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(
            initial,
            RunTimingTests.RemoteId(initial));

        Assert.Null(initial.Turn.History);
        Assert.Null(started.State.Turn.History);
        Assert.Equal(allocatorPosition, initial.Allocator.NextSequence);
        Assert.Equal(
            [EngineEventKinds.RunInitiated],
            started.Events.Select(@event => @event.Kind));
        GameState restored = EngineJson.DeserializeState(EngineJson.Serialize(started.State));
        Assert.Equal(DeterministicHash.ForState(started.State), DeterministicHash.ForState(restored));
        Assert.Equal(EngineJson.Serialize(started.State), EngineJson.Serialize(restored));
        Assert.Empty(StateInvariantValidator.Collect(started.State));
    }

    private static GameState OpenRunnerAction(GameState state)
    {
        state = GameEngine.BeginTurn(state, PlayerSide.Runner);
        for (int step = 0; state.PendingDecision?.Kind != DecisionKind.TurnAction; step++)
        {
            Assert.True(step < 8, "Runner turn did not reach its action window.");
            ApplyResult pass = RunTimingTests.ApplyKind(state, ActionKind.PassPriority);
            Assert.True(pass.Accepted, pass.Rejection?.Message);
            state = pass.State;
        }

        return state;
    }

    private static GameState WithRunnerFacts(GameState source, TurnFactsState facts) => new(
        source.GameId,
        source.RulesVersion,
        source.CardDataVersion,
        players: source.Players,
        cards: source.Cards,
        zones: source.Zones,
        servers: source.Servers,
        turn: new TurnState(
            PlayerSide.Runner,
            TurnPhase.Runner,
            source.Turn.TurnNumber,
            TurnStep.RunnerAction,
            new TurnHistoryState(1, null, null, facts, null)),
        outcome: source.Outcome,
        randomState: source.RandomState,
        allocator: source.Allocator,
        schemaVersion: source.SchemaVersion,
        decisionRevision: source.DecisionRevision,
        eventSequence: source.EventSequence,
        resolutionStack: source.ResolutionStack,
        pendingDecision: source.PendingDecision,
        actionSequence: source.ActionSequence,
        initialSeed: source.InitialSeed,
        runState: source.RunState,
        rulesState: source.RulesState,
        behaviorRegistryVersion: source.BehaviorRegistryVersion,
        behaviorCatalogFingerprint: source.BehaviorCatalogFingerprint,
        runtimeCardCatalogVersion: source.RuntimeCardCatalogVersion,
        runtimeCardCatalogFingerprint: source.RuntimeCardCatalogFingerprint);

    private static GameState AddRunnerGripCards(GameState source, IReadOnlyList<CardInstanceId> cardIds)
    {
        ZoneState grip = source.GetZone(ZoneKind.RunnerGrip);
        CardInstanceState[] additions = cardIds
            .Select(cardId => new CardInstanceState(
                cardId,
                new CardDefinitionId($"test:{cardId.Value}"),
                grip.Id,
                PlayerSide.Runner))
            .ToArray();
        ZoneState populatedGrip = new(
            grip.Id,
            grip.Kind,
            grip.OwnerSide,
            grip.ServerId,
            grip.CardIds.Concat(cardIds).ToArray());
        return new GameState(
            source.GameId,
            source.RulesVersion,
            source.CardDataVersion,
            source.Players,
            source.Cards.Concat(additions),
            source.Zones.Select(zone => zone.Id == grip.Id ? populatedGrip : zone),
            source.Servers,
            source.Turn,
            source.Outcome,
            source.RandomState,
            source.Allocator,
            source.SchemaVersion,
            source.DecisionRevision,
            source.EventSequence,
            source.ResolutionStack,
            source.PendingDecision,
            source.ActionSequence,
            source.InitialSeed,
            source.RunState,
            source.RulesState,
            source.BehaviorRegistryVersion,
            source.BehaviorCatalogFingerprint,
            source.RuntimeCardCatalogVersion,
            source.RuntimeCardCatalogFingerprint);
    }
}
