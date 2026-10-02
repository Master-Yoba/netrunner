using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>EL.3 presentation: agenda and flip counters, flipped identities and declared-use credits.</summary>
public sealed class ElevationPresentationTests
{
    private static CardViewModel Identity(params CardCounterViewModel[] counters) =>
        new CardViewModel("card:1", "35057", "Nebula Talent Management: Making Stars", "identity", true, false, 0,
            null, null, null, null, [], string.Empty, "nbn")
        { TypedCounters = counters };

    [Fact]
    public void AgendaAndFlipCountersHaveTheirOwnLabels()
    {
        Assert.Equal("AGENDA ×2", new CardCounterViewModel("agenda", CardCounterPurpose.Generic, 2, null).ChipText);
        Assert.Equal("FLIPPED ×1", new CardCounterViewModel("flipped", CardCounterPurpose.Generic, 1, null).ChipText);
        Assert.Equal(
            "HOSTED CREDIT ×2 · CARD'S OWN USES ONLY",
            new CardCounterViewModel("credit", CardCounterPurpose.HostedCredit, 2, HostedCreditSpendRestriction.DeclaredUseOnly).Summary);
    }

    [Fact]
    public void AFlippedIdentityShowsItsBackFace()
    {
        var front = new IdentityPanelViewModel(PlayerSide.Corp, Identity());
        var back = new IdentityPanelViewModel(PlayerSide.Corp,
            Identity(new CardCounterViewModel("flipped", CardCounterPurpose.Generic, 1, null)));

        Assert.False(front.IsFlipped);
        Assert.True(back.IsFlipped);
        Assert.EndsWith(" · back face", back.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("back face", front.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlayerSide.Runner)]
    [InlineData(PlayerSide.Corp)]
    public void HostedFaceupCardsAndRemovedFromGamePilesArePublic(PlayerSide viewer)
    {
        GameState baseline = GameState.CreateNew(seed: 35006);
        ZoneState rig = baseline.GetZone(ZoneKind.RunnerRig);
        var hostedZone = new ZoneState(new ZoneId("zone:hosted-faceup"), ZoneKind.RunnerHostedFaceUp, PlayerSide.Runner);
        var removedZone = new ZoneState(new ZoneId("zone:corp-removed"), ZoneKind.CorpRemovedFromGame, PlayerSide.Corp);
        var console = new CardInstanceState(new CardInstanceId("card:console"), new CardDefinitionId("35006"), rig.Id,
            PlayerSide.Runner, faceUp: true);
        var hosted = new CardInstanceState(new CardInstanceId("card:hosted"), new CardDefinitionId("30006"), hostedZone.Id,
            PlayerSide.Runner, faceUp: true, hostCardId: console.Id);
        var removed = new CardInstanceState(new CardInstanceId("card:removed"), new CardDefinitionId("35081"), removedZone.Id,
            PlayerSide.Corp, faceUp: true);
        ZoneState[] zones = baseline.Zones
            .Select(zone => zone.Id == rig.Id
                ? new ZoneState(zone.Id, zone.Kind, zone.OwnerSide, zone.ServerId, [console.Id])
                : zone)
            .Append(new ZoneState(hostedZone.Id, hostedZone.Kind, hostedZone.OwnerSide, null, [hosted.Id]))
            .Append(new ZoneState(removedZone.Id, removedZone.Kind, removedZone.OwnerSide, null, [removed.Id]))
            .ToArray();
        GameState state = new(
            baseline.GameId, baseline.RulesVersion, baseline.CardDataVersion,
            players: baseline.Players, cards: [console, hosted, removed], zones: zones, servers: baseline.Servers,
            turn: baseline.Turn, outcome: baseline.Outcome, randomState: baseline.RandomState,
            allocator: baseline.Allocator, rulesState: RulesState.CreateDefault(baseline.Players));

        MatchViewModel view = ObservationViewMapper.Build(ObservationBuilder.Build(state, viewer));

        CardViewModel shown = Assert.Single(view.HostedFaceUp);
        Assert.Equal("card:hosted", shown.Id);
        Assert.Equal("30006", shown.DefinitionId);
        Assert.Equal("card:console", shown.HostCardId);
        Assert.Equal("card:removed", Assert.Single(view.CorpRemovedFromGame).Id);
        Assert.Empty(view.RunnerRemovedFromGame);
    }
}
