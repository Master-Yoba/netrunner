using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayRunEventBehaviorSpecTests
{
    [Fact]
    public void OverclockSpecMatchesTheOfficialCardText()
    {
        RunEventBehaviorSpec spec = GatewayRunEventBehaviorSpecs.RunEventSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayCardCodes.Overclock);

        Assert.Equal(1, spec.Cost);
        Assert.Equal(5, spec.HostedCredits);
    }

    [Fact]
    public void RunEventSpecsBindExactlyTheOneRepresentativeRunEventDefinition()
    {
        Assert.Equal(
            new[] { GatewayCardCodes.Overclock },
            GatewayRunEventBehaviorSpecs.RunEventSpecs
                .Select(static spec => spec.DefinitionId)
                .ToArray());
    }

    [Fact]
    public void RegisterAllIsIdempotentAndRetrievalSucceeds()
    {
        GatewayRunEventBehaviorSpecs.RegisterAll();
        GatewayRunEventBehaviorSpecs.RegisterAll();

        Assert.Equal(
            GatewayRunEventBehaviorSpecs.RunEventSpecs[0],
            CardBehaviorSpecCatalog.GetRunEvent(GatewayCardCodes.Overclock));
    }

    [Fact]
    public void GetRunEventFailsClosedForUnregisteredDefinitions()
    {
        CardDefinitionId unregistered = new("30001");

        Assert.False(CardBehaviorSpecCatalog.TryGetRunEvent(unregistered, out _));
        Assert.Throws<ArgumentException>(() => CardBehaviorSpecCatalog.GetRunEvent(unregistered));
    }

    [Fact]
    public void RegisteringADifferentSpecForTheSameDefinitionThrows()
    {
        RunEventBehaviorSpec canonical = GatewayRunEventBehaviorSpecs.RunEventSpecs[0];
        Assert.Equal(GatewayCardCodes.Overclock, canonical.DefinitionId);
        CardBehaviorSpecCatalog.RegisterRunEvent(canonical);

        RunEventBehaviorSpec conflicting = new(
            canonical.DefinitionId,
            cost: canonical.Cost,
            hostedCredits: canonical.HostedCredits + 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CardBehaviorSpecCatalog.RegisterRunEvent(conflicting));
        Assert.Contains(canonical.DefinitionId.Value, exception.Message);
        Assert.Equal(canonical, CardBehaviorSpecCatalog.GetRunEvent(canonical.DefinitionId));
    }

    [Fact]
    public void CatalogVersionIsPinnedAfterTheRunEventAddition()
    {
        // The run-event registration bumped the catalog version; the proof
        // fixtures in Slice 4 key replay hashes off this exact version.
        Assert.Equal("gateway-m2-3", CardBehaviorSpecCatalog.Version);
    }

    [Fact]
    public void RunEventSpecRejectsNegativeCostAndHostedCredits()
    {
        Assert.Throws<ArgumentException>(
            () => new RunEventBehaviorSpec(GatewayCardCodes.Overclock, cost: -1, hostedCredits: 5));
        Assert.Throws<ArgumentException>(
            () => new RunEventBehaviorSpec(GatewayCardCodes.Overclock, cost: 1, hostedCredits: -1));
    }
}
