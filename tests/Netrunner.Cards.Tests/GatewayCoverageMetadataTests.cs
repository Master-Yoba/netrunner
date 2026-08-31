using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// CARD-401's test metadata contract.  The coverage tool reads these xUnit
/// traits from the compiled test assembly with PE metadata; it never invokes
/// the test runner while building the coverage report.
/// </summary>
public sealed class GatewayCoverageMetadataTests
{
    [Fact]
    public void ImmutableGatewayCatalogSurfaceIncludesEveryCompiledDefinition()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        IReadOnlySet<CardDefinitionId> expected = catalog.IceSpecs
            .Select(static spec => spec.DefinitionId)
            .Concat(catalog.IcebreakerSpecs.Select(static spec => spec.DefinitionId))
            .Concat(catalog.AssetSpecs.Select(static spec => spec.DefinitionId))
            .Concat(catalog.AgendaSpecs.Select(static spec => spec.DefinitionId))
            .Concat(catalog.RunEventSpecs.Select(static spec => spec.DefinitionId))
            .Concat(catalog.GenericSpecs.Select(static spec => spec.DefinitionId))
            .ToHashSet();

        Assert.Equal(expected, catalog.AllDefinitionIds.ToHashSet());
    }

    [Fact]
    [Trait("GatewayCard", "30005")]
    public void BuzzsawHasARegisteredBehavior()
    {
        Assert.True(GatewayBehaviorCatalog.Create().TryGetIcebreaker(GatewayCardCodes.Buzzsaw, out _));
    }

    [Fact]
    [Trait("GatewayCard", "30006")]
    public void CleaverHasARegisteredBehavior()
    {
        Assert.True(GatewayBehaviorCatalog.Create().TryGetIcebreaker(GatewayCardCodes.Cleaver, out _));
    }

    [Fact]
    [Trait("GatewayCard", "30029")]
    public void OverclockHasARegisteredBehavior()
    {
        Assert.True(GatewayBehaviorCatalog.Create().TryGetRunEvent(GatewayCardCodes.Overclock, out _));
    }

    [Fact]
    [Trait("GatewayCard", "30037")]
    public void NicoCampaignHasARegisteredBehavior()
    {
        Assert.True(GatewayBehaviorCatalog.Create().TryGetAsset(GatewayCardCodes.NicoCampaign, out _));
    }

    [Fact]
    [Trait("GatewayCard", "30067")]
    public void OffworldOfficeHasARegisteredBehavior()
    {
        Assert.True(GatewayBehaviorCatalog.Create().TryGetAgenda(GatewayCardCodes.OffworldOffice, out _));
    }

    [Fact]
    [Trait("GatewayCard", "30072")]
    public void PalisadeHasARegisteredBehavior()
    {
        Assert.True(GatewayBehaviorCatalog.Create().TryGetIce(GatewayCardCodes.Palisade, out _));
    }

    [Fact]
    [Trait("GatewayCard", "30074")]
    public void WhitespaceHasARegisteredBehavior()
    {
        Assert.True(GatewayBehaviorCatalog.Create().TryGetIce(GatewayCardCodes.Whitespace, out _));
    }
}
