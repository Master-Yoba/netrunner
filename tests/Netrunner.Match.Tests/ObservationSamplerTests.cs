using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>Engine plan E5: samples are consistent with, and depend only on, the viewer's observation.</summary>
public sealed class ObservationSamplerTests
{
    [Fact]
    public async Task ASampleObservesExactlyLikeTheStateItWasDrawnFrom()
    {
        int checkedStates = 0;
        foreach ((FullMatchDefinition definition, GameState state) in await PositionsAsync())
        {
            foreach (PlayerSide viewer in new[] { PlayerSide.Corp, PlayerSide.Runner })
            {
                GameState sample = ObservationSampler.Sample(state, viewer, SamplerKnowledge.None, Pool(definition, viewer),
                    definition.Runtime, new DeterministicRandom(7));
                Assert.True(sample.IsSimulated);
                Assert.Equal(Observe(state, viewer, definition), Observe(sample, viewer, definition));
                checkedStates++;
            }
        }
        Assert.True(checkedStates >= 40, $"Only {checkedStates} positions.");
    }

    [Fact]
    public async Task HiddenCardsAreActuallyResampled()
    {
        bool differs = false;
        foreach ((FullMatchDefinition definition, GameState state) in (await PositionsAsync()).Take(40))
        {
            GameState sample = ObservationSampler.Sample(state, PlayerSide.Runner, SamplerKnowledge.None,
                Pool(definition, PlayerSide.Runner), definition.Runtime, new DeterministicRandom(3));
            differs |= sample.Cards.Zip(state.Cards).Any(pair => pair.First.DefinitionId != pair.Second.DefinitionId);
            Assert.Equal(Multiset(state, PlayerSide.Corp), Multiset(sample, PlayerSide.Corp));
        }
        Assert.True(differs);
    }

    [Theory]
    [InlineData(ZoneKind.CorpHand, ZoneKind.CorpDeck)]
    [InlineData(ZoneKind.ServerIce, ZoneKind.ServerIce)]
    public async Task TwinsWithTheSameObservationGiveTheSameSample(ZoneKind first, ZoneKind second)
    {
        int twins = 0;
        foreach ((FullMatchDefinition definition, GameState state) in await PositionsAsync())
        {
            GameState? twin = SwapTwoHiddenCorpCards(state, first, second);
            if (twin is null) continue;
            Assert.Equal(Observe(state, PlayerSide.Runner, definition), Observe(twin, PlayerSide.Runner, definition));
            foreach (ulong seed in new ulong[] { 1, 2, 3 })
            {
                GameState left = ObservationSampler.Sample(state, PlayerSide.Runner, SamplerKnowledge.None,
                    Pool(definition, PlayerSide.Runner), definition.Runtime, new DeterministicRandom(seed));
                GameState right = ObservationSampler.Sample(twin, PlayerSide.Runner, SamplerKnowledge.None,
                    Pool(definition, PlayerSide.Runner), definition.Runtime, new DeterministicRandom(seed));
                Assert.Equal(left.Cards, right.Cards);
                Assert.Equal(left.RandomState, right.RandomState);
            }
            twins++;
        }
        // The developer Corp installs a single kind of ICE, so face-down ICE twins need official content.
        if (first != ZoneKind.ServerIce || Official) Assert.True(twins >= 5, $"Only {twins} twin positions.");
    }

    [Fact]
    public async Task KnownHiddenCardsKeepTheirIdentity()
    {
        foreach ((FullMatchDefinition definition, GameState state) in (await PositionsAsync()).Take(30))
        {
            ZoneState deck = state.Zones.Single(zone => zone.Kind == ZoneKind.CorpDeck);
            if (deck.CardIds.Count == 0) continue;
            CardDefinitionId top = state.GetCard(deck.CardIds[0]).DefinitionId;
            var knowledge = new SamplerKnowledge([new KnownHiddenCard(deck.Id, 0, top)]);
            foreach (ulong seed in new ulong[] { 1, 2, 3, 4 })
            {
                GameState sample = ObservationSampler.Sample(state, PlayerSide.Runner, knowledge, Pool(definition, PlayerSide.Runner),
                    definition.Runtime, new DeterministicRandom(seed));
                Assert.Equal(top, sample.GetCard(deck.CardIds[0]).DefinitionId);
            }
        }
    }

    [Fact]
    public async Task SamplesCanBePlayedForwardInSimulationButNotPublished()
    {
        int played = 0;
        foreach ((FullMatchDefinition definition, GameState state) in (await PositionsAsync()).Take(60))
        {
            if (state.PendingDecision is not { } pending) continue;
            PlayerSide viewer = state.GetPlayer(pending.Owner).Side;
            GameState sample = ObservationSampler.Sample(state, viewer, SamplerKnowledge.None, Pool(definition, viewer),
                definition.Runtime, new DeterministicRandom(9));
            Assert.ThrowsAny<InvalidOperationException>(() => EngineJson.Serialize(sample));
            LegalAction action = pending.LegalActions[0];
            ApplyResult applied = GameEngine.Apply(sample, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action.Id),
                definition.Behavior, definition.Runtime, ApplyMode.Simulation);
            Assert.True(applied.Accepted, applied.Rejection?.Message);
            played++;
        }
        Assert.True(played >= 10, $"Only {played} positions.");
    }

    private static string Observe(GameState state, PlayerSide viewer, FullMatchDefinition definition) =>
        EngineJson.Serialize(ObservationBuilder.Build(state, viewer, definition.Behavior));

    private static OpponentCardPool.Decklist Pool(FullMatchDefinition definition, PlayerSide viewer)
    {
        DeckRequest deck = viewer == PlayerSide.Runner ? definition.Request.CorpDeck : definition.Request.RunnerDeck;
        return new OpponentCardPool.Decklist(deck.Cards.GroupBy(card => new CardDefinitionId(card.Code))
            .ToDictionary(group => group.Key, group => group.Sum(card => card.Quantity)));
    }

    private static string[] Multiset(GameState state, PlayerSide owner) =>
        [.. state.Cards.Where(card => card.OwnerSide == owner).Select(card => card.DefinitionId.Value).Order(StringComparer.Ordinal)];

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    /// <summary>Swaps the identities of two Corp cards hidden from the Runner in the given zones.</summary>
    private static GameState? SwapTwoHiddenCorpCards(GameState state, ZoneKind firstZone, ZoneKind secondZone)
    {
        Dictionary<ZoneId, ZoneState> zones = state.Zones.ToDictionary(zone => zone.Id);
        CardInstanceState[] hidden = [.. state.Cards.Where(card => card.OwnerSide == PlayerSide.Corp && !card.FaceUp
            && (zones[card.ZoneId].Kind == firstZone || zones[card.ZoneId].Kind == secondZone))];
        CardInstanceState? first = hidden.FirstOrDefault();
        CardInstanceState? second = hidden.FirstOrDefault(card => first is not null && card.DefinitionId != first.DefinitionId);
        if (first is null || second is null || state.RunState is not null || state.ResolutionStack.Count > 1) return null;
        CardInstanceState[] cards = [.. state.Cards.Select(card => card.Id == first.Id ? Recard(card, second.DefinitionId)
            : card.Id == second.Id ? Recard(card, first.DefinitionId) : card)];
        return new GameState(state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion, state.Players, cards,
            state.Zones, state.Servers, state.Turn, state.Outcome, state.RandomState, state.Allocator, state.DecisionRevision,
            state.EventSequence, state.ResolutionStack, state.PendingDecision, state.ActionSequence, state.InitialSeed,
            state.RunState, state.RulesState, state.BehaviorRegistryVersion, state.BehaviorCatalogFingerprint,
            state.RuntimeCardCatalogVersion, state.RuntimeCardCatalogFingerprint);
    }

    private static CardInstanceState Recard(CardInstanceState card, CardDefinitionId definition) => new(card.Id, definition,
        card.ZoneId, card.OwnerSide, card.FaceUp, card.Rezzed, card.AdvancementCounters, card.HostCardId);

    /// <summary>Every tenth position of a few random-legal games.</summary>
    private static async Task<IReadOnlyList<(FullMatchDefinition, GameState)>> PositionsAsync()
    {
        var positions = new List<(FullMatchDefinition, GameState)>();
        foreach (ulong seed in new ulong[] { 31, 32, 33, 34, 35, 36 })
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(
                Official ? RulesFuzzRunner.OfficialContentIds[0] : RulesFuzzRunner.DeveloperContentId, seed);
            var agents = new Dictionary<PlayerSide, IGameAgent>
            {
                [PlayerSide.Corp] = new RandomLegalAgent(seed),
                [PlayerSide.Runner] = new RandomLegalAgent(seed + 100),
            };
            GameState state = definition.Start().State;
            for (int count = 0; count < 500 && state.Outcome.Kind == GameOutcomeKind.Ongoing && state.PendingDecision is { } pending; count++)
            {
                if (count % 10 == 0) positions.Add((definition, state));
                PlayerSide side = state.GetPlayer(pending.Owner).Side;
                ActionId chosen = await agents[side].ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior));
                state = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, chosen),
                    definition.Behavior, definition.Runtime).State;
            }
        }
        return positions;
    }
}
