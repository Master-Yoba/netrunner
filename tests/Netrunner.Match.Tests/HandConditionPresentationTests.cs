using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class HandConditionPresentationTests
{
    private static readonly CardInstanceId Carmen = new("card:hand-carmen");
    private static readonly CardDefinitionId ConditionalEvent = new("test:conditional-event");
    private static readonly BehaviorCatalog Catalog = new BehaviorCatalogBuilder("test:hand-conditions", GatewayBehaviorCatalog.Create())
        .Add(new CardBehaviorDefinition(ConditionalEvent, PlayerSide.Runner,
            conditions: [BehaviorCondition.SuccessfulRunThisTurn()])).Build();
    private static readonly RuntimeCardCatalog Runtime = new("test:hand-costs",
        new CardRuntimeSpec(GatewayBatch2CardCodes.Carmen, PlayerSide.Runner, RuntimeCardClass.Program, installCost: 5),
        new CardRuntimeSpec(ConditionalEvent, PlayerSide.Runner, RuntimeCardClass.Event, playCost: 2));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void SuccessfulRunProjectsHandConditionsAndExactCostIndependentlyOfAffordability(int credits)
    {
        GameState state = State(successful: true, credits);
        string before = EngineJson.Serialize(state);
        MatchPresentationContext context = MatchPresentationProjector.Build(state, PlayerSide.Runner, Catalog, Runtime);
        MatchViewModel view = View(state, context);
        CardViewModel carmen = view.RunnerHand.VisibleCards.Single(card => card.Id == Carmen.Value);
        Assert.Equal(3, carmen.HandCondition!.InstallCost!.EffectiveCost);
        Assert.Equal(5, carmen.HandCondition.InstallCost.BaseCost);
        Assert.Contains("successful run this turn", carmen.HandCondition.Summary, StringComparison.Ordinal);
        Assert.Contains("Install 5 → 3", CardZoomRegionPresenter.Format(carmen), StringComparison.Ordinal);
        Assert.NotNull(view.RunnerHand.VisibleCards.Single(card => card.DefinitionId == ConditionalEvent.Value).HandCondition);
        Assert.Empty(view.LegalActions); // Conditions are not permission to submit an action.
        Assert.Empty(MatchPresentationProjector.Build(state, PlayerSide.Corp, Catalog, Runtime).HandConditions);
        Assert.Equal(before, EngineJson.Serialize(state));
    }

    [Fact]
    public void UnmetConditionsAndCardsOutsideHandDoNotRetainCues()
    {
        foreach (GameState state in new[] { State(false, 5), State(true, 5, ZoneKind.RunnerStack), State(true, 5, ZoneKind.RunnerRig) })
            Assert.Empty(MatchPresentationProjector.Build(state, PlayerSide.Runner, Catalog, Runtime).HandConditions);
        MatchViewModel expired = View(State(false, 5), MatchPresentationContext.Empty);
        Assert.All(expired.RunnerHand.VisibleCards, card => Assert.Null(card.HandCondition));
    }

    private static MatchViewModel View(GameState state, MatchPresentationContext context) => ObservationViewMapper.Build(
        ObservationBuilder.Build(state, PlayerSide.Runner, Catalog), CardMetadataResolvers.Default, Catalog,
        handSizeProjection: null, presentationContext: context);

    private static GameState State(bool successful, int credits, ZoneKind kind = ZoneKind.RunnerGrip)
    {
        GameState seed = GameState.CreateNew(419);
        ZoneState hand = seed.GetZone(kind);
        CardInstanceState[] cards = [new(Carmen, GatewayBatch2CardCodes.Carmen, hand.Id, PlayerSide.Runner),
            new(new("card:conditional-event"), ConditionalEvent, hand.Id, PlayerSide.Runner)];
        ZoneState[] zones = seed.Zones.Select(zone => zone.Id == hand.Id
            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, cards.Select(card => card.Id).ToArray()) : zone).ToArray();
        PlayerState[] players = seed.Players.Select(player => player.Side == PlayerSide.Runner
            ? new PlayerState(player.Id, player.Side, credits, 4, player.AgendaPoints) : player).ToArray();
        TurnFactsState facts = new(1, PlayerSide.Runner, 1, 4, 0, true, false, false, false, successfulRun: successful);
        return new(seed.GameId, seed.RulesVersion, seed.CardDataVersion, players, cards, zones, seed.Servers,
            new TurnState(PlayerSide.Runner, TurnPhase.Runner, 1, TurnStep.RunnerAction, new(1, null, null, facts, null)),
            seed.Outcome, seed.RandomState, seed.Allocator);
    }
}
