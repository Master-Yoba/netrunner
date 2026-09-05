using System.Text.Json;
using System.Text.Json.Nodes;

using Netrunner.Core;
using Xunit;

namespace Netrunner.Cards.Tests;

public sealed partial class GatewayBatch4ReactionRuntimeTests
{
    [Theory]
    [InlineData(ServerKind.Headquarters, 2)]
    [InlineData(ServerKind.RAndD, 1)]
    [InlineData(ServerKind.Remote, 0)]
    [Trait("GatewayCard", "30010")]
    public void ZahyaCountsAccessOccurrencesAndCarriesThemAcrossRunCleanup(ServerKind serverKind, int credits)
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        var identity = new CardDefinitionId("30010");
        RuntimeCardCatalog runtime = new("fixture:zahya-runtime",
            new CardRuntimeSpec(identity, PlayerSide.Runner, RuntimeCardClass.Identity),
            new CardRuntimeSpec(new CardDefinitionId("30077"), PlayerSide.Corp, RuntimeCardClass.Identity),
            new CardRuntimeSpec(GatewayBatch5CardCodes.AmazeAmusements, PlayerSide.Corp, RuntimeCardClass.Upgrade, rezCost: 1),
            new CardRuntimeSpec(HqCostCardA, PlayerSide.Corp, RuntimeCardClass.Operation, playCost: 0));
        GameState initial = BuildState(catalog, runtime, GatewayBatch5CardCodes.AmazeAmusements,
            sourceServerKind: serverKind, hqCards: serverKind == ServerKind.Headquarters ? 1 : 0);
        initial = AddRunIdentity(initial, identity);
        GameState state = BeginRun(initial, catalog, runtime, serverKind);
        int accessEvents = 0;
        bool sawRunEndContext = false;
        bool checkedLiveTamper = false;
        bool checkedRunEndTamper = false;
        for (int step = 0; step < 100 && state.PendingDecision is { } decision; step++)
        {
            state = EngineJson.DeserializeState(EngineJson.Serialize(state), catalog, runtime);
            if (state.RunState is { } run)
            {
                Assert.Equal(accessEvents, run.PersistentContext?.AccessOccurrenceCount ?? 0);
                // The run's AccessedCards is an occurrence list: the reducer
                // appends each access without Distinct(), including repeats.
                Assert.Equal(accessEvents, run.AccessedCards.Count);
                if (accessEvents > 0 && !checkedLiveTamper)
                {
                    AssertForgedAccessCountRejected(state, catalog, runtime);
                    checkedLiveTamper = true;
                }
            }
            else if (ObservationBuilder.Build(state, PlayerSide.Runner, catalog).PublicState
                .TryGetValue("runEnd", out object? projection))
            {
                var fields = Assert.IsType<Dictionary<string, object?>>(projection);
                Assert.Equal(accessEvents, fields["accessOccurrenceCount"]);
                sawRunEndContext = true;
                if (!checkedRunEndTamper)
                {
                    AssertForgedAccessCountRejected(state, catalog, runtime);
                    checkedRunEndTamper = true;
                }
            }
            LegalAction action = RunContextPolicy(decision);
            ApplyResult applied = Apply(state, action, catalog, runtime);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            accessEvents += applied.Events.Count(value => value.Kind == EngineEventKinds.CardAccessed);
            state = applied.State;
        }
        Assert.Null(state.PendingDecision);
        Assert.Equal(10 + credits, state.GetPlayerBySide(PlayerSide.Runner).Credits);
        Assert.True(checkedLiveTamper);
        Assert.Equal(credits > 0, sawRunEndContext);
        Assert.Equal(credits > 0, checkedRunEndTamper);
    }

    private static void AssertForgedAccessCountRejected(GameState state, BehaviorCatalog catalog, RuntimeCardCatalog runtime)
    {
        JsonNode json = JsonNode.Parse(EngineJson.Serialize(state))!;
        Assert.True(IncrementFirstAccessCount(json));
        Assert.Throws<JsonException>(() => EngineJson.DeserializeState(json.ToJsonString(), catalog, runtime));
    }

    private static bool IncrementFirstAccessCount(JsonNode node)
    {
        if (node is JsonObject value)
        {
            if (value["accessOccurrenceCount"] is { } count)
            {
                value["accessOccurrenceCount"] = count.GetValue<int>() + 1;
                return true;
            }
            return value.Any(property => property.Value is { } child && IncrementFirstAccessCount(child));
        }
        return node is JsonArray array && array.Any(child => child is not null && IncrementFirstAccessCount(child));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("GatewayCard", "30019")]
    public void TaoSwapsStableIcePositionsDuringStealWithoutReinstalling(bool sameServer)
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        var identity = new CardDefinitionId("30019");
        RuntimeCardCatalog runtime = new("fixture:tao-runtime",
            new CardRuntimeSpec(identity, PlayerSide.Runner, RuntimeCardClass.Identity),
            new CardRuntimeSpec(new CardDefinitionId("30077"), PlayerSide.Corp, RuntimeCardClass.Identity),
            new CardRuntimeSpec(GatewayCardCodes.OffworldOffice, PlayerSide.Corp, RuntimeCardClass.Agenda,
                agendaPoints: 2, advancementRequirement: 4),
            new CardRuntimeSpec(GatewayCardCodes.Palisade, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 3),
            new CardRuntimeSpec(new CardDefinitionId("30004"), PlayerSide.Runner, RuntimeCardClass.Program, installCost: 2, memoryUnits: 1));
        GameState initial = AddRunIdentity(BuildState(catalog, runtime, GatewayCardCodes.OffworldOffice,
            sourceRezzed: false), identity);
        var allocator = new StableIdAllocator(initial.Allocator);
        ZoneState hqIce = initial.GetZone(initial.Servers.Single(server => server.Kind == ServerKind.Headquarters).IceZoneId);
        ZoneState rdIce = sameServer ? hqIce : initial.GetZone(initial.Servers.Single(server => server.Kind == ServerKind.RAndD).IceZoneId);
        CardInstanceState first = new(allocator.NextCardInstanceId(), GatewayCardCodes.Palisade, hqIce.Id, PlayerSide.Corp,
            faceUp: true, rezzed: true, advancementCounters: 2);
        CardInstanceState second = new(allocator.NextCardInstanceId(), GatewayCardCodes.Palisade, rdIce.Id, PlayerSide.Corp);
        CardInstanceState trojan = new(allocator.NextCardInstanceId(), new CardDefinitionId("30004"),
            initial.GetZone(ZoneKind.RunnerRig).Id, PlayerSide.Runner, faceUp: true, hostCardId: first.Id);
        CardInstanceState[] cards = [.. initial.Cards, first, second, trojan];
        initial = CopyRunFixture(initial, cards, initial.Zones.Select(zone => new ZoneState(zone.Id, zone.Kind,
            zone.OwnerSide, zone.ServerId, cards.Where(card => card.ZoneId == zone.Id).Select(card => card.Id).ToArray())).ToArray(), allocator.State,
            new RulesState(initial.RulesState!.Players.Select(player => new PlayerRulesState(player.PlayerId,
                player.BaseHandSize, player.CoreDamage, player.Tags, player.BaseMemory,
                initial.GetPlayer(player.PlayerId).Side == PlayerSide.Runner ? 1 : 0)).ToArray(),
                programs: [new ProgramMemoryState(trojan.Id, initial.GetPlayerBySide(PlayerSide.Runner).Id, 1)],
                counters: [new CardCounterState(trojan.Id, "virus", 2)]));
        GameState state = BeginRun(initial, catalog, runtime, ServerKind.Remote);
        var events = new List<EngineEvent>();
        bool selectedPair = false;
        for (int step = 0; step < 100 && state.PendingDecision is { } decision; step++)
        {
            state = EngineJson.DeserializeState(EngineJson.Serialize(state), catalog, runtime);
            LegalAction action = decision.LegalActions.FirstOrDefault(value => value.Parameters is BehaviorChoiceParameters
            { CardIds.Count: 2 }) ?? RunContextPolicy(decision);
            selectedPair |= action.Parameters is BehaviorChoiceParameters { CardIds.Count: 2 };
            ApplyResult applied = Apply(state, action, catalog, runtime);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            events.AddRange(applied.Events);
            state = applied.State;
        }
        Assert.True(selectedPair);
        Assert.Equal(second.Id, state.GetZone(hqIce.Id).CardIds[0]);
        Assert.Equal(first.Id, state.GetZone(rdIce.Id).CardIds[sameServer ? 1 : 0]);
        Assert.True(state.GetCard(first.Id).Rezzed);
        Assert.False(state.GetCard(second.Id).Rezzed);
        Assert.Equal(2, state.GetCard(first.Id).AdvancementCounters);
        Assert.Equal(first.Id, state.GetCard(trojan.Id).HostCardId);
        Assert.Equal(2, Assert.Single(state.RulesState!.Counters).Count);
        Assert.Single(state.RulesState.Programs);
        Assert.Equal(2, state.GetPlayerBySide(PlayerSide.Runner).AgendaPoints);
        Assert.Single(events, value => value.Kind == EngineEventKinds.IceSwapped);
        Assert.DoesNotContain(events, value => value.Kind == EngineEventKinds.CardInstalled);
        Assert.Null(state.RunState);
        Assert.Empty(StateInvariantValidator.Collect(state, catalog, runtime));
    }

    [Theory]
    [InlineData("30063")]
    [InlineData("30054")]
    [Trait("GatewayCard", "30051")]
    public void NbnReactsToIceTagInstructionsThroughSerializableCheckpoints(string iceCode)
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        var ice = new CardDefinitionId(iceCode);
        RuntimeCardCatalog runtime = new("fixture:nbn-ice",
            new CardRuntimeSpec(new CardDefinitionId("30076"), PlayerSide.Runner, RuntimeCardClass.Identity),
            new CardRuntimeSpec(new CardDefinitionId("30051"), PlayerSide.Corp, RuntimeCardClass.Identity),
            new CardRuntimeSpec(ice, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 5));
        GameState initial = AddRunIdentity(BuildState(catalog, runtime, ice, sourceInIce: true, runnerCredits: 0),
            new CardDefinitionId("30076"), new CardDefinitionId("30051"));
        GameState state = BeginRun(initial, catalog, runtime, ServerKind.Remote);
        int benefits = 0;
        for (int step = 0; step < 150 && state.PendingDecision is { } decision; step++)
        {
            state = EngineJson.DeserializeState(EngineJson.Serialize(state), catalog, runtime);
            LegalAction? benefit = decision.LegalActions.FirstOrDefault(action => action.Parameters
                is BehaviorChoiceParameters { ChoiceKey: "reality-plus-benefit" } choice
                && choice.OptionKeys.Contains("gain-two"));
            if (benefit is not null) benefits++;
            LegalAction action = benefit ?? decision.LegalActions.FirstOrDefault(action => action.Parameters
                is IceSubroutineChoiceParameters { Choice: IceSubroutineChoiceKind.TakeTag })
                ?? RunContextPolicy(decision);
            ApplyResult result = Apply(state, action, catalog, runtime);
            Assert.True(result.Accepted, result.Rejection?.Message);
            state = result.State;
        }
        Assert.Equal(1, benefits);
        Assert.Equal(7, state.GetPlayerBySide(PlayerSide.Corp).Credits);
        Assert.True(Tags(state, PlayerSide.Runner) > 0);
    }

    private static LegalAction RunContextPolicy(PendingDecision decision) =>
        decision.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.PassPriority)
        ?? decision.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.ContinueRun)
        ?? decision.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.NoAccessAction)
        ?? decision.LegalActions.FirstOrDefault(value => value.Kind == ActionKind.UseTrigger)
        ?? decision.LegalActions[0];

    private static GameState AddRunIdentity(GameState state, CardDefinitionId definition, CardDefinitionId? corpDefinition = null)
    {
        var allocator = new StableIdAllocator(state.Allocator);
        ZoneId zoneId = allocator.NextZoneId();
        var identity = new CardInstanceState(allocator.NextCardInstanceId(), definition, zoneId, PlayerSide.Runner, faceUp: true);
        ZoneId corpZone = allocator.NextZoneId();
        var corpIdentity = new CardInstanceState(allocator.NextCardInstanceId(), corpDefinition ?? new CardDefinitionId("30077"),
            corpZone, PlayerSide.Corp, faceUp: true);
        return CopyRunFixture(state, [.. state.Cards, identity, corpIdentity],
            [.. state.Zones, new ZoneState(zoneId, ZoneKind.RunnerIdentity, PlayerSide.Runner, cardIds: [identity.Id]),
             new ZoneState(corpZone, ZoneKind.CorpIdentity, PlayerSide.Corp, cardIds: [corpIdentity.Id])], allocator.State);
    }

    private static GameState CopyRunFixture(GameState state, IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones, IdAllocatorState allocator, RulesState? rules = null) =>
        new(state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion, state.Players,
            cards, zones, state.Servers, state.Turn, state.Outcome, state.RandomState, allocator,
            initialSeed: state.InitialSeed, rulesState: rules ?? state.RulesState,
            behaviorRegistryVersion: state.BehaviorRegistryVersion, behaviorCatalogFingerprint: state.BehaviorCatalogFingerprint,
            runtimeCardCatalogVersion: state.RuntimeCardCatalogVersion, runtimeCardCatalogFingerprint: state.RuntimeCardCatalogFingerprint);
}
