using Xunit;

namespace Netrunner.Core.Tests;

public sealed class CorrectnessBoundaryTests
{
    [Theory]
    [InlineData(ZoneKind.CorpDiscard, true)]
    [InlineData(ZoneKind.CorpHand, false)]
    [InlineData(ZoneKind.CorpDeck, false)]
    public void UninstalledCorpCardLosesRezAndAllCounters(ZoneKind destinationKind, bool faceUp)
    {
        GameState seed = GameState.CreateNew(42);
        ZoneState root = seed.Zones.First(zone => zone.Kind == ZoneKind.ServerRoot);
        CardInstanceState card = new(new CardInstanceId("card:countered"), new CardDefinitionId("definition:countered"),
            root.Id, PlayerSide.Corp, faceUp: true, rezzed: true, advancementCounters: 3);
        RulesState defaults = RulesState.CreateDefault(seed.Players);
        GameState state = new(seed.GameId, seed.RulesVersion, seed.CardDataVersion, seed.Players, [card],
            seed.Zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
                zone.Id == root.Id ? [card.Id] : [])), seed.Servers, seed.Turn, seed.Outcome,
            seed.RandomState, seed.Allocator, initialSeed: seed.InitialSeed,
            rulesState: new RulesState(defaults.Players, counters: [new CardCounterState(card.Id, "power", 2)]));
        GameState result = GameEngine.ResolveEffects(state, seed.Players[0].Id, new CostBundle(),
            new EffectBundle(new MoveCardEffect(card.Id, seed.GetZone(destinationKind).Id)));
        Assert.False(result.GetCard(card.Id).Rezzed);
        Assert.Equal(0, result.GetCard(card.Id).AdvancementCounters);
        Assert.Equal(faceUp, result.GetCard(card.Id).FaceUp);
        Assert.Empty(result.RulesState!.Counters);
        Assert.Equal(EngineJson.Serialize(result), EngineJson.Serialize(EngineJson.DeserializeState(EngineJson.Serialize(result))));
        Assert.Equal(3, state.GetCard(card.Id).AdvancementCounters);
    }

    [Fact]
    public void NeitherViewerCanObserveDrawDeckIdentitiesOrOrder()
    {
        GameState state = CreateState();
        GameState reordered = Copy(state, zones: state.Zones.Select(zone =>
            zone.Kind is ZoneKind.CorpDeck or ZoneKind.RunnerStack
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, zone.CardIds.Reverse().ToArray())
                : zone));
        foreach (PlayerSide side in Enum.GetValues<PlayerSide>())
        {
            string observed = EngineJson.Serialize(ObservationBuilder.Build(state, side));
            Assert.DoesNotContain("definition:deck", observed, StringComparison.Ordinal);
            Assert.Equal(observed, EngineJson.Serialize(ObservationBuilder.Build(reordered, side)));
        }
    }

    [Fact]
    public void NegativeHandSizeFlatlinesAtDiscardBeforeNextTurn()
    {
        GameState state = CreateState();
        RulesState rules = new(state.Players.Select(player => new PlayerRulesState(
            player.Id, 5, player.Side == PlayerSide.Runner ? 6 : 0, 0, 4, 0)).ToArray());
        state = Copy(state, rules: rules);
        state = GameEngine.BeginTurn(state, PlayerSide.Runner);
        for (int step = 0; step < 40 && state.Outcome.Kind == GameOutcomeKind.Ongoing; step++)
        {
            PendingDecision pending = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction action = pending.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.PassPriority)
                ?? pending.LegalActions.First(value => value.Kind == ActionKind.GainCredit);
            state = Apply(state, action).State;
        }

        Assert.Equal(GameOutcomeKind.Won, state.Outcome.Kind);
        Assert.Equal(PlayerSide.Corp, state.Outcome.Winner);
        Assert.Equal(GameWinReason.Flatline, state.Outcome.Reason);
        Assert.Empty(state.ResolutionStack);
        Assert.Null(state.PendingDecision);
        Assert.Equal(EngineJson.Serialize(state), EngineJson.Serialize(EngineJson.DeserializeState(EngineJson.Serialize(state))));
    }

    [Fact]
    public void ProductionReplayUsesBothCatalogsAndRejectsChangedRuntimeMetadata()
    {
        GameState seed = CreateState();
        CardDefinitionId definition = new("definition:hardware");
        BehaviorCatalog behavior = new BehaviorCatalogBuilder("correctness:behavior")
            .Add(new CardBehaviorDefinition(definition, PlayerSide.Runner)).Build();
        RuntimeCardCatalog runtime = new RuntimeCardCatalogBuilder("correctness:runtime")
            .Add(new CardRuntimeSpec(definition, PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 0)).Build();
        ZoneState grip = seed.GetZone(ZoneKind.RunnerGrip);
        CardInstanceState card = new(new CardInstanceId("card:hardware"), definition, grip.Id, PlayerSide.Runner);
        GameState state = new(seed.GameId, seed.RulesVersion, seed.CardDataVersion,
            seed.Players, [card], seed.Zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
                zone.Id == grip.Id ? [card.Id] : [])), seed.Servers, seed.Turn,
            seed.Outcome, seed.RandomState, seed.Allocator, initialSeed: seed.InitialSeed,
            behaviorRegistryVersion: behavior.Version, behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version, runtimeCardCatalogFingerprint: runtime.Fingerprint);
        state = GameEngine.BeginTurn(state, PlayerSide.Runner, behavior, runtime);
        while (state.PendingDecision!.Kind != DecisionKind.TurnAction)
        {
            state = Apply(state, state.PendingDecision.LegalActions.First(action => action.Kind == ActionKind.PassPriority), behavior, runtime).State;
        }

        ApplyResult installed = Apply(state, state.PendingDecision.LegalActions.First(action => action.Kind == ActionKind.InstallCard), behavior, runtime);
        ReplayArtifact artifact = new(state.CreateSnapshot(behavior, runtime), state.InitialSeed,
            [ReplayAction.From(installed.AcceptedAction!)], expectedFinalStateHash: DeterministicHash.ForState(installed.State));
        artifact = EngineJson.Deserialize<ReplayArtifact>(EngineJson.Serialize(artifact), behavior, runtime);
        ReplayVerificationResult verified = ReplayVerifier.Verify(artifact, behavior, runtime);
        Assert.True(verified.Success, verified.Message);
        Assert.False(ReplayVerifier.Verify(artifact, behavior).Success);
        RuntimeCardCatalog changed = new RuntimeCardCatalogBuilder(runtime.Version)
            .Add(new CardRuntimeSpec(definition, PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 1)).Build();
        Assert.False(ReplayVerifier.Verify(artifact, behavior, changed).Success);
    }

    private static ApplyResult Apply(GameState state, LegalAction action,
        IBehaviorCatalog? behavior = null, IRuntimeCardCatalog? runtime = null)
    {
        PendingDecision pending = state.PendingDecision!;
        ActionSubmission submission = new(pending.Owner, pending.Id, pending.Revision, action.Id);
        ApplyResult result = runtime is null ? GameEngine.Apply(state, submission)
            : GameEngine.Apply(state, submission, behavior!, runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result;
    }

    private static GameState CreateState()
    {
        GameState seed = GameState.CreateNew(42);
        var allocator = new StableIdAllocator(seed.Allocator);
        var cards = new List<CardInstanceState>();
        foreach (ZoneKind kind in new[] { ZoneKind.CorpDeck, ZoneKind.RunnerStack })
        {
            ZoneState zone = seed.GetZone(kind);
            for (int i = 0; i < 2; i++)
            {
                cards.Add(new CardInstanceState(allocator.NextCardInstanceId(), new CardDefinitionId($"definition:deck-{kind}-{i}"), zone.Id, zone.OwnerSide!.Value));
            }
        }

        return new GameState(seed.GameId, seed.RulesVersion, seed.CardDataVersion,
            seed.Players, cards, seed.Zones.Select(zone => new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId,
                cards.Where(card => card.ZoneId == zone.Id).Select(card => card.Id).ToArray())),
            seed.Servers, new TurnState(PlayerSide.Runner), seed.Outcome, seed.RandomState, allocator.State,
            initialSeed: seed.InitialSeed);
    }

    private static GameState Copy(GameState state, IEnumerable<ZoneState>? zones = null, RulesState? rules = null) =>
        new(state.GameId, state.RulesVersion, state.CardDataVersion, state.Players, state.Cards,
            zones ?? state.Zones, state.Servers, state.Turn, state.Outcome, state.RandomState, state.Allocator,
            initialSeed: state.InitialSeed, rulesState: rules);
}
