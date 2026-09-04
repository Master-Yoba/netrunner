using System.Reflection;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class MatchCoordinatorBoundaryTests
{
    [Fact]
    public void RunnerPublicationOmitsHiddenAnselAndBranInstallCardIdsButKeepsPublicRez()
    {
        using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
            },
            new FirstLegalAgent());
        var rezzedIce = new CardInstanceId("card:1");
        GameState state = WithRezzedCard(
            M2ScenarioSetup.Build(LoadRepresentativeWithoutStateHash()),
            rezzedIce);
        SetPrivateState(coordinator, state);

        ZoneId corpHand = state.Zones.Single(zone => zone.Kind == ZoneKind.CorpHand).Id;
        ZoneId remoteRoot = state.Servers
            .Single(server => server.Kind == ServerKind.Remote)
            .RootZoneId;
        var hiddenAnsel = new CardInstanceId("card:3");
        var hiddenBran = new CardInstanceId("card:4");
        var publicCard = new CardInstanceId("card:5");
        IReadOnlyList<EngineEvent> events =
        [
            new EngineEvent(
                1,
                EngineEventKinds.CardMoved,
                new CardMovedEventPayload(hiddenAnsel, corpHand, remoteRoot)),
            new EngineEvent(
                2,
                EngineEventKinds.CardInstalled,
                new CardInstalledEventPayload(
                    PlayerSide.Corp,
                    hiddenBran,
                    corpHand,
                    remoteRoot,
                    cost: 0)),
            new EngineEvent(
                3,
                EngineEventKinds.CardRezzed,
                new CardRezzedEventPayload(publicCard, creditCost: 0)),
            new EngineEvent(
                4,
                EngineEventKinds.IceApproached,
                new IceApproachedEventPayload(new RunId("run:public-ice"), rezzedIce)),
        ];

        MatchSnapshot snapshot = InvokePublish(coordinator, events, state);

        Assert.DoesNotContain(snapshot.Events, value => value.Kind == EngineEventKinds.CardMoved);
        Assert.DoesNotContain(snapshot.Events, value => value.Kind == EngineEventKinds.CardInstalled);
        Assert.Contains(
            snapshot.Events,
            value => value.Kind == EngineEventKinds.CardRezzed
                && ((CardRezzedEventPayload)value.Payload).CardId == publicCard);
        Assert.Contains(
            snapshot.Events,
            value => value.Kind == EngineEventKinds.IceApproached
                && ((IceApproachedEventPayload)value.Payload).IceId == rezzedIce);
        Assert.DoesNotContain(
            EngineJson.Serialize(snapshot.Events),
            hiddenAnsel.Value,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            EngineJson.Serialize(snapshot.Events),
            hiddenBran.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeCatalogOptionBindsSetupAndEveryCoordinatorApply()
    {
        RuntimeCardCatalog runtime = GatewayRuntimeCardCatalog.CreateEmbedded();
        var counting = new CountingRuntimeCatalog(runtime);
        await using MatchCoordinator coordinator = new(
            LoadRepresentativeWithoutStateHash(),
            new FirstLegalAgent(),
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
                RuntimeCardCatalog = counting,
            });

        MatchSnapshot started = await coordinator.StartAsync();
        Assert.True(counting.LookupCount > 0);
        Assert.Equal(runtime.Version, GetPrivateState(coordinator).RuntimeCardCatalogVersion);
        Assert.Equal(runtime.Fingerprint, GetPrivateState(coordinator).RuntimeCardCatalogFingerprint);

        LegalAction pass = Assert.Single(
            started.Observation.LegalActions,
            value => value.Kind == ActionKind.PassPriority);
        int lookupsBeforeApply = counting.LookupCount;
        MatchSubmissionResult submitted = await coordinator.SubmitRunnerActionAsync(pass.Id);

        Assert.True(submitted.Accepted, submitted.Feedback);
        Assert.True(
            counting.LookupCount > lookupsBeforeApply,
            "The coordinator must pass its injected runtime catalog to GameEngine.Apply.");
    }

    [Fact]
    public void RunnerPublicationFailsClosedForFutureTypedCardReferences()
    {
        using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
            },
            new FirstLegalAgent());
        GameState state = M2ScenarioSetup.Build(LoadRepresentativeWithoutStateHash());
        var hiddenCorpCard = new CardInstanceId("card:1");
        var visibleRunnerCard = new CardInstanceId("card:5");

        MatchSnapshot snapshot = InvokePublish(
            coordinator,
            [
                new EngineEvent(
                    1,
                    "future-card-reference",
                    new FutureCardReferenceEventPayload(hiddenCorpCard)),
                new EngineEvent(
                    2,
                    "future-card-reference-null",
                    new FutureCardReferenceEventPayload(null)),
                new EngineEvent(
                    3,
                    "future-card-reference-public",
                    new FutureCardReferenceEventPayload(visibleRunnerCard)),
            ],
            state);

        Assert.DoesNotContain(
            snapshot.Events,
            value => value.Kind == "future-card-reference");
        Assert.Contains(
            snapshot.Events,
            value => value.Kind == "future-card-reference-null");
        Assert.Contains(
            snapshot.Events,
            value => value.Kind == "future-card-reference-public");
    }

    private static MatchSnapshot InvokePublish(
        MatchCoordinator coordinator,
        IReadOnlyList<EngineEvent> events,
        GameState state)
    {
        SetPrivateState(coordinator, state);
        return (MatchSnapshot)typeof(MatchCoordinator)
            .GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [events, null, false])!;
    }

    private static M2ScenarioDocument LoadRepresentativeWithoutStateHash()
    {
        M2ScenarioDocument source = M2ScenarioProvider.LoadRepresentative();
        return new M2ScenarioDocument(
            source.SchemaVersion,
            source.ScenarioId,
            source.Seed,
            source.RulesVersion,
            source.CardDataVersion,
            source.Turn,
            source.Remotes,
            source.Run,
            source.Players,
            source.Cards,
            source.Citations,
            source.CrossCheckNote,
            expectedInitialStateHash: null,
            behaviorRegistryVersion: source.BehaviorRegistryVersion,
            behaviorCatalogFingerprint: source.BehaviorCatalogFingerprint);
    }

    private static void SetPrivateState(MatchCoordinator coordinator, GameState state) =>
        typeof(MatchCoordinator)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(coordinator, state);

    private static GameState GetPrivateState(MatchCoordinator coordinator) =>
        (GameState)typeof(MatchCoordinator)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(coordinator)!;

    private static GameState WithRezzedCard(GameState state, CardInstanceId cardId) => new(
        state.SchemaVersion,
        state.GameId,
        state.RulesVersion,
        state.CardDataVersion,
        state.Players,
        state.Cards.Select(card => card.Id == cardId
            ? new CardInstanceState(
                card.Id,
                card.DefinitionId,
                card.ZoneId,
                card.OwnerSide,
                card.FaceUp,
                rezzed: true,
                advancementCounters: card.AdvancementCounters)
            : card).ToArray(),
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
        state.BehaviorRegistryVersion,
        state.BehaviorCatalogFingerprint,
        state.RuntimeCardCatalogVersion,
        state.RuntimeCardCatalogFingerprint);

    private sealed record FutureCardReferenceEventPayload : EngineEventPayload
    {
        public FutureCardReferenceEventPayload(CardInstanceId? cardId)
            : base(1)
        {
            CardId = cardId;
        }

        public CardInstanceId? CardId { get; }
    }

    private sealed class CountingRuntimeCatalog : IRuntimeCardCatalog
    {
        private readonly IRuntimeCardCatalog _inner;

        public CountingRuntimeCatalog(IRuntimeCardCatalog inner) =>
            _inner = inner;

        public int LookupCount { get; private set; }

        public string Version => _inner.Version;

        public string Fingerprint => _inner.Fingerprint;

        public IReadOnlyList<CardDefinitionId> DefinitionIds => _inner.DefinitionIds;

        public bool TryGet(CardDefinitionId definitionId, out CardRuntimeSpec spec)
        {
            LookupCount++;
            return _inner.TryGet(definitionId, out spec!);
        }

        public CardRuntimeSpec GetSpec(CardDefinitionId definitionId)
        {
            LookupCount++;
            return _inner.GetSpec(definitionId);
        }
    }
}
