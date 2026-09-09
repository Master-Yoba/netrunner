using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class ArchivesPresentationTests
{
    [Theory]
    [InlineData(PlayerSide.Runner, 1)]
    [InlineData(PlayerSide.Corp, 3)]
    public void ArchivesTotalIncludesConcealedCardsWithoutExposingThem(PlayerSide viewer, int visibleCount)
    {
        GameState baseline = GameState.CreateNew(seed: 47042);
        ZoneState archives = baseline.GetZone(ZoneKind.CorpDiscard);
        CardInstanceState[] cards = Enumerable.Range(0, 3).Select(index => new CardInstanceState(
            new CardInstanceId($"card:archives-{index}"),
            new CardDefinitionId($"definition:archives-{index}"),
            archives.Id,
            PlayerSide.Corp,
            faceUp: index == 0)).ToArray();
        ZoneState[] zones = baseline.Zones.Select(zone => zone.Id == archives.Id
            ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, cards.Select(card => card.Id).ToArray())
            : zone).ToArray();
        GameState state = new(
            baseline.GameId, baseline.RulesVersion, baseline.CardDataVersion,
            players: baseline.Players, cards: cards, zones: zones, servers: baseline.Servers,
            turn: baseline.Turn, outcome: baseline.Outcome, randomState: baseline.RandomState,
            allocator: baseline.Allocator, rulesState: RulesState.CreateDefault(baseline.Players));

        Observation observation = ObservationBuilder.Build(state, viewer);
        MatchViewModel view = ObservationViewMapper.Build(observation);

        Assert.Equal(3, view.CorpDiscardCount);
        Assert.Equal(visibleCount, view.CorpDiscard.Count);
        if (viewer == PlayerSide.Runner)
        {
            Assert.DoesNotContain(view.CorpDiscard, card => card.Id == cards[1].Id.Value);
            Assert.DoesNotContain("faceDownArchives", observation.PrivateState.Keys);
            Assert.Single(Assert.IsAssignableFrom<IEnumerable<object>>(observation.PublicState["corpDiscard"]));
        }

        // Older observations remain usable without inventing hidden identities.
        var legacyPublic = new Dictionary<string, object?>(observation.PublicState);
        legacyPublic.Remove("zoneCounts");
        MatchViewModel legacy = ObservationViewMapper.Build(observation with { PublicState = legacyPublic });
        Assert.Equal(visibleCount, legacy.CorpDiscardCount);
    }

    [Fact]
    public void M1ObservationDoesNotAddZoneCountFields()
    {
        Observation observation = ObservationBuilder.Build(GameState.CreateNew(seed: 47042), PlayerSide.Runner);
        Assert.DoesNotContain("zoneCounts", observation.PublicState.Keys);
    }
}
