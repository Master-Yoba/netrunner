using System.Reflection;

using Netrunner.Cards;
using Netrunner.Core;

using Xunit;

namespace Netrunner.Match.Tests;

public sealed class RunnerDamageEventVisibilityTests
{
    [Fact]
    public void AccessedUrticaFlatlineRemainsPublicAfterRunVisibilityIsCleared()
    {
        Fixture fixture = Create();
        GameState state = GameEngine.BeginRun(fixture.State, fixture.RemoteId, fixture.Behavior, fixture.Runtime);
        ApplyResult? final = null;
        for (int step = 0; state.Outcome.Kind != GameOutcomeKind.Won; step++)
        {
            Assert.True(step < 12);
            PendingDecision decision = Assert.IsType<PendingDecision>(state.PendingDecision);
            LegalAction action = decision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.ChooseBreachCandidate)
                ?? Assert.Single(decision.LegalActions, action => action.Kind == ActionKind.PassPriority);
            final = GameEngine.Apply(state, new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
                fixture.Behavior, fixture.Runtime);
            Assert.True(final.Accepted, final.Rejection?.Message);
            state = final.State;
        }
        Assert.NotNull(final);
        Assert.Null(state.RunState);
        Assert.False(state.GetCard(fixture.SourceId).Rezzed);
        Assert.False(state.GetCard(fixture.SourceId).FaceUp);
        Assert.Contains(final.Events, entry => entry.Payload is CardAccessedEventPayload access && access.CardId == fixture.SourceId);
        EngineEvent original = Assert.Single(final.Events, entry => entry.Kind == EngineEventKinds.DamageDealt);
        EngineEvent projected = Assert.Single(Filter(final.Events, state), entry => entry.Kind == EngineEventKinds.DamageDealt);
        DamageDealtEventPayload damage = Assert.IsType<DamageDealtEventPayload>(projected.Payload);
        Assert.Equal(fixture.SourceId, damage.SourceCardId);
        Assert.Equal(DamageKind.Net, damage.Kind);
        Assert.Equal(2, damage.Amount);
        Assert.Equal(1, damage.CardsTrashed);
        Assert.True(damage.Flatlined);
        Assert.Equal(CanonicalJson.Serialize(original), CanonicalJson.Serialize(projected));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicDamageRedactsHiddenOrUnknownSourceWithoutDroppingItsResult(bool unknown)
    {
        Fixture fixture = Create();
        CardInstanceId source = unknown ? new CardInstanceId("card:unknown-damage-source") : fixture.SourceId;
        PlayerId runner = fixture.State.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        EngineEvent original = new(7, EngineEventKinds.DamageDealt,
            new DamageDealtEventPayload(runner, DamageKind.Core, 3, 2, 3, true,
                source, flatlineRisk: true, trashedCardPrintedCost: 4, conditionalEndRun: false));
        EngineEvent projected = Assert.Single(Filter([original], fixture.State));
        DamageDealtEventPayload damage = Assert.IsType<DamageDealtEventPayload>(projected.Payload);
        Assert.Null(damage.SourceCardId);
        Assert.Equal(7, projected.Sequence);
        Assert.Equal(runner, damage.Target);
        Assert.Equal(DamageKind.Core, damage.Kind);
        Assert.Equal(3, damage.Amount);
        Assert.Equal(2, damage.CardsTrashed);
        Assert.Equal(3, damage.CoreDamageAfter);
        Assert.True(damage.Flatlined);
        Assert.True(damage.FlatlineRisk);
        Assert.Equal(4, damage.TrashedCardPrintedCost);
        Assert.False(damage.ConditionalEndRun);
        Assert.Equal(source, Assert.IsType<DamageDealtEventPayload>(original.Payload).SourceCardId);
    }

    [Fact]
    public void LaterAccessDoesNotRetroactivelyRevealADamageSource()
    {
        Fixture fixture = Create();
        PlayerId runner = fixture.State.Players.Single(player => player.Side == PlayerSide.Runner).Id;
        EngineEvent damage = new(1, EngineEventKinds.DamageDealt,
            new DamageDealtEventPayload(runner, DamageKind.Net, 1, 1, 0, false, fixture.SourceId));
        EngineEvent access = new(2, EngineEventKinds.CardAccessed,
            new CardAccessedEventPayload(new RunId("run:visibility"), fixture.RemoteId, fixture.SourceId));
        IReadOnlyList<EngineEvent> projected = Filter([damage, access], fixture.State);
        Assert.Null(Assert.IsType<DamageDealtEventPayload>(projected[0].Payload).SourceCardId);
        Assert.IsType<CardAccessedEventPayload>(projected[1].Payload);
        Assert.Null(Assert.IsType<DamageDealtEventPayload>(Assert.Single(Filter([damage], fixture.State)).Payload).SourceCardId);
    }

    private static IReadOnlyList<EngineEvent> Filter(IReadOnlyList<EngineEvent> events, GameState state) =>
        (IReadOnlyList<EngineEvent>)typeof(MatchCoordinator).Assembly
            .GetType("Netrunner.Match.RunnerEventVisibility", throwOnError: true)!
            .GetMethod("Filter", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [events, state])!;

    private static Fixture Create()
    {
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        CardDefinitionId filler = new("30031");
        RuntimeCardCatalog runtime = new RuntimeCardCatalogBuilder("fixture:runner-damage-visibility")
            .Add(new CardRuntimeSpec(GatewayBatch5CardCodes.UrticaCipher, PlayerSide.Corp, RuntimeCardClass.Asset, rezCost: 0, trashCost: 2))
            .Add(new CardRuntimeSpec(filler, PlayerSide.Runner, RuntimeCardClass.Hardware, installCost: 2)).Build();
        GameState baseline = GameState.CreateNew(46045, behaviorRegistryVersion: behavior.Version,
            behaviorCatalogFingerprint: behavior.Fingerprint, runtimeCardCatalogVersion: runtime.Version,
            runtimeCardCatalogFingerprint: runtime.Fingerprint);
        var allocator = new StableIdAllocator(baseline.Allocator);
        ServerId remote = allocator.NextServerId();
        ZoneId ice = allocator.NextZoneId();
        ZoneId root = allocator.NextZoneId();
        CardInstanceId source = allocator.NextCardInstanceId();
        CardInstanceId hand = allocator.NextCardInstanceId();
        ZoneId grip = baseline.GetZone(ZoneKind.RunnerGrip).Id;
        CardInstanceState[] cards =
        [
            new(source, GatewayBatch5CardCodes.UrticaCipher, root, PlayerSide.Corp),
            new(hand, filler, grip, PlayerSide.Runner),
        ];
        ZoneState[] zones =
        [
            .. baseline.Zones.Select(zone => zone.Id == grip
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [hand]) : zone),
            new(ice, ZoneKind.ServerIce, PlayerSide.Corp, remote),
            new(root, ZoneKind.ServerRoot, PlayerSide.Corp, remote, [source]),
        ];
        GameState state = new(gameId: baseline.GameId, rulesVersion: baseline.RulesVersion,
            cardDataVersion: baseline.CardDataVersion, players: baseline.Players, cards: cards, zones: zones,
            servers: [.. baseline.Servers, new ServerState(remote, ServerKind.Remote, ice, root)],
            turn: new TurnState(PlayerSide.Runner, TurnPhase.Runner, 1), outcome: baseline.Outcome,
            randomState: baseline.RandomState, allocator: allocator.State, schemaVersion: baseline.SchemaVersion,
            initialSeed: baseline.InitialSeed, rulesState: RulesState.CreateDefault(baseline.Players),
            behaviorRegistryVersion: behavior.Version, behaviorCatalogFingerprint: behavior.Fingerprint,
            runtimeCardCatalogVersion: runtime.Version, runtimeCardCatalogFingerprint: runtime.Fingerprint);
        return new Fixture(state, behavior, runtime, remote, source);
    }

    private sealed record Fixture(GameState State, BehaviorCatalog Behavior, RuntimeCardCatalog Runtime,
        ServerId RemoteId, CardInstanceId SourceId);
}
