using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed partial class GatewayBatch4ReactionRuntimeTests
{
    private static BehaviorCatalog Batch5ConditionalCatalog() => new BehaviorCatalogBuilder("fixture:batch5-conditional")
        .AddIce(GatewayBatch5IceBehaviorSpecs.IceSpecs)
        .Add(GatewayBatch5ConditionalBehaviorSpecs.GenericSpecs)
        .Build();

    private static RuntimeCardCatalog Batch5ConditionalRuntime() => new RuntimeCardCatalogBuilder("fixture:batch5-conditional-runtime")
        .Add(new CardRuntimeSpec(GatewayBatch5CardCodes.Ping, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 2))
        .Add(new CardRuntimeSpec(GatewayBatch5CardCodes.Pharos, PlayerSide.Corp, RuntimeCardClass.Ice, rezCost: 7))
        .Build();

    [Fact]
    [Trait("GatewayCard", "30055")]
    public void PingGivesOneTagOnActualRezAgainstItsServer()
    {
        BehaviorCatalog catalog = Batch5ConditionalCatalog();
        RuntimeCardCatalog runtime = Batch5ConditionalRuntime();
        GameState state = BuildState(catalog, runtime, GatewayBatch5CardCodes.Ping,
            sourceInIce: true, sourceRezzed: false);
        GameState approach = ReachApproach(state, catalog, runtime, ServerKind.Remote);
        // The Runner passes first in the approach paid window.
        GameState corpPriority = Pass(approach, catalog, runtime).State;
        LegalAction rez = Assert.Single(corpPriority.PendingDecision!.LegalActions,
            action => action.Kind == ActionKind.RezCard);
        ApplyResult result = Apply(corpPriority, rez, catalog, runtime);
        Assert.True(result.Accepted, result.Rejection?.Message);
        Assert.Equal(1, result.State.RulesState!.Players.Single(player => player.PlayerId == result.State.GetPlayerBySide(PlayerSide.Runner).Id).Tags);
        Assert.Empty(StateInvariantValidator.Collect(result.State, catalog, runtime));
    }

    [Fact]
    [Trait("GatewayCard", "30055")]
    public void PingDoesNotGiveATagWhenRezzedOutsideARun()
    {
        BehaviorCatalog catalog = Batch5ConditionalCatalog();
        RuntimeCardCatalog runtime = Batch5ConditionalRuntime();
        GameState state = BuildState(catalog, runtime, GatewayBatch5CardCodes.Ping, sourceInIce: true);
        CardInstanceState ping = Assert.Single(state.Cards);
        PlayerId corp = state.GetPlayerBySide(PlayerSide.Corp).Id;
        ServerId server = state.Zones.Single(zone => zone.Id == ping.ZoneId).ServerId!.Value;
        GameState result = GameEngine.BeginResolution(state,
            new EffectResolutionFrame(corp, new CostBundle(),
                new EffectBundle(new GameplayOccurrenceEffect(OccurrenceKind.CardRezzed, corp, ping.Id, server))),
            catalog, runtime);
        Assert.Equal(0, result.RulesState!.Players.Single(player => player.PlayerId == result.GetPlayerBySide(PlayerSide.Runner).Id).Tags);
        Assert.Empty(StateInvariantValidator.Collect(result, catalog, runtime));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 10)]
    [Trait("GatewayCard", "30063")]
    public void PharosProjectsThresholdStrengthFromHostedAdvancementCounters(int counters, int expected)
    {
        BehaviorCatalog catalog = Batch5ConditionalCatalog();
        RuntimeCardCatalog runtime = Batch5ConditionalRuntime();
        GameState state = BuildState(catalog, runtime, GatewayBatch5CardCodes.Pharos,
            sourceInIce: true, advancementCounters: counters);
        state = ReachEncounter(state, catalog, runtime, ServerKind.Remote);
        CardInstanceId pharos = Assert.Single(state.Cards).Id;
        ValueQuote quote = Assert.IsType<ValueQuote>(EncounterStrength.ProjectIceQuote(state, catalog, pharos));
        Assert.Equal(expected, quote.EffectiveValue);
        Assert.True(catalog.GenericSpecs.Single(spec => spec.DefinitionId == GatewayBatch5CardCodes.Pharos).CanBeAdvanced);
    }
}
