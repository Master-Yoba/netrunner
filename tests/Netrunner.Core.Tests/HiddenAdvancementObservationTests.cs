using Xunit;

namespace Netrunner.Core.Tests;

public sealed class HiddenAdvancementObservationTests
{
    [Fact]
    public void RunnerSeesCountersOnAFaceDownCardByPositionOnly()
    {
        (GameState state, ServerId remote, CardInstanceId card) = StateWithFaceDownAdvancedCard(counters: 2);

        Observation runner = ObservationBuilder.Build(state, PlayerSide.Runner);

        var entries = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(runner.PublicState["hiddenAdvancements"]);
        Dictionary<string, object?> entry = Assert.Single(entries);
        Assert.Equal(remote.Value, entry["serverId"]);
        Assert.Equal("root", entry["zone"]);
        Assert.Equal(0, entry["index"]);
        Assert.Equal(2, entry["advancementCounters"]);
        // The card's ID and identity stay hidden; only its position is used.
        string serialized = EngineJson.Serialize(runner.PublicState);
        Assert.DoesNotContain(card.Value, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("test:hidden-advanced", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void CorpObservationAndUnadvancedCardsAddNothing()
    {
        (GameState advanced, _, _) = StateWithFaceDownAdvancedCard(counters: 2);
        (GameState plain, _, _) = StateWithFaceDownAdvancedCard(counters: 0);

        Assert.False(ObservationBuilder.Build(advanced, PlayerSide.Corp).PublicState.ContainsKey("hiddenAdvancements"));
        Assert.False(ObservationBuilder.Build(plain, PlayerSide.Runner).PublicState.ContainsKey("hiddenAdvancements"));
    }

    private static (GameState State, ServerId Remote, CardInstanceId Card) StateWithFaceDownAdvancedCard(int counters)
    {
        GameState baseState = GameState.CreateNew(311);
        var allocator = new StableIdAllocator(baseState.Allocator);
        var remote = new ServerState(allocator.NextServerId(), ServerKind.Remote, allocator.NextZoneId(), allocator.NextZoneId());
        CardInstanceId cardId = new("card:hidden-advanced");
        CardInstanceState card = new(cardId, new CardDefinitionId("test:hidden-advanced"), remote.RootZoneId, PlayerSide.Corp,
            advancementCounters: counters);
        ZoneState[] zones = baseState.Zones
            .Append(new ZoneState(remote.IceZoneId, ZoneKind.ServerIce, PlayerSide.Corp, remote.Id))
            .Append(new ZoneState(remote.RootZoneId, ZoneKind.ServerRoot, PlayerSide.Corp, remote.Id, [cardId]))
            .ToArray();
        var state = new GameState(
            baseState.SchemaVersion,
            baseState.GameId,
            baseState.RulesVersion,
            baseState.CardDataVersion,
            baseState.Players,
            baseState.Cards.Append(card).ToArray(),
            zones,
            baseState.Servers.Append(remote).ToArray(),
            baseState.Turn,
            baseState.Outcome,
            baseState.RandomState,
            allocator.State);
        return (state, remote.Id, cardId);
    }
}
