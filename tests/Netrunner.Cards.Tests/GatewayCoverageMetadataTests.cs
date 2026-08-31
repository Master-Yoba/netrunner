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
    [Trait("GatewayCard", "30005")]
    public void BuzzsawHasARegisteredBehavior()
    {
        Assert.Contains(
            GatewayCardCodes.Buzzsaw,
            GatewayIcebreakerBehaviorSpecs.IcebreakerSpecs.Select(static spec => spec.DefinitionId));
    }

    [Fact]
    [Trait("GatewayCard", "30006")]
    public void CleaverHasARegisteredBehavior()
    {
        Assert.Contains(
            GatewayCardCodes.Cleaver,
            GatewayIcebreakerBehaviorSpecs.IcebreakerSpecs.Select(static spec => spec.DefinitionId));
    }

    [Fact]
    [Trait("GatewayCard", "30029")]
    public void OverclockHasARegisteredBehavior()
    {
        Assert.Contains(
            GatewayCardCodes.Overclock,
            GatewayRunEventBehaviorSpecs.RunEventSpecs.Select(static spec => spec.DefinitionId));
    }

    [Fact]
    [Trait("GatewayCard", "30037")]
    public void NicoCampaignHasARegisteredBehavior()
    {
        Assert.Contains(
            GatewayCardCodes.NicoCampaign,
            GatewayAssetBehaviorSpecs.AssetSpecs.Select(static spec => spec.DefinitionId));
    }

    [Fact]
    [Trait("GatewayCard", "30067")]
    public void OffworldOfficeHasARegisteredBehavior()
    {
        Assert.Contains(
            GatewayCardCodes.OffworldOffice,
            GatewayAgendaBehaviorSpecs.AgendaSpecs.Select(static spec => spec.DefinitionId));
    }

    [Fact]
    [Trait("GatewayCard", "30072")]
    public void PalisadeHasARegisteredBehavior()
    {
        Assert.Contains(
            GatewayCardCodes.Palisade,
            GatewayIceBehaviorSpecs.IceSpecs.Select(static spec => spec.DefinitionId));
    }

    [Fact]
    [Trait("GatewayCard", "30074")]
    public void WhitespaceHasARegisteredBehavior()
    {
        Assert.Contains(
            GatewayCardCodes.Whitespace,
            GatewayIceBehaviorSpecs.IceSpecs.Select(static spec => spec.DefinitionId));
    }
}
