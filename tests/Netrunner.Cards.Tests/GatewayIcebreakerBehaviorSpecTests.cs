using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayIcebreakerBehaviorSpecTests
{
    [Fact]
    public void CleaverSpecMatchesTheOfficialCardText()
    {
        IcebreakerBehaviorSpec spec = GatewayIcebreakerBehaviorSpecs.IcebreakerSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayCardCodes.Cleaver);

        Assert.Equal(3, spec.BaseStrength);
        Assert.Equal(IceType.Barrier, spec.InterfaceType);
        Assert.Equal(1, spec.BreakCost);
        Assert.Equal(2, spec.BreakLimit);
        Assert.Equal(2, spec.PumpCost);
        Assert.Equal(1, spec.PumpStrength);
    }

    [Fact]
    public void BuzzsawSpecMatchesTheOfficialCardText()
    {
        IcebreakerBehaviorSpec spec = GatewayIcebreakerBehaviorSpecs.IcebreakerSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayCardCodes.Buzzsaw);

        Assert.Equal(3, spec.BaseStrength);
        Assert.Equal(IceType.CodeGate, spec.InterfaceType);
        Assert.Equal(1, spec.BreakCost);
        Assert.Equal(2, spec.BreakLimit);
        Assert.Equal(3, spec.PumpCost);
        Assert.Equal(1, spec.PumpStrength);
    }

    [Fact]
    public void IcebreakerSpecsBindAllRegisteredDefinitionsInCodeOrder()
    {
        Assert.Equal(
            new[]
            {
                GatewayCardCodes.Buzzsaw,
                GatewayCardCodes.Cleaver,
                GatewayBatch2CardCodes.Carmen,
                GatewayBatch2CardCodes.Marjanah,
            },
            GatewayIcebreakerBehaviorSpecs.IcebreakerSpecs
                .Select(static spec => spec.DefinitionId)
                .ToArray());
    }

    [Fact]
    public void ImmutableGatewayCatalogContainsTheCompiledIcebreakerSpecs()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.Equal(
            GatewayIcebreakerBehaviorSpecs.IcebreakerSpecs[0],
            catalog.IcebreakerSpecs.Single(spec => spec.DefinitionId == GatewayCardCodes.Buzzsaw));
        Assert.Equal(
            GatewayIcebreakerBehaviorSpecs.IcebreakerSpecs[1],
            catalog.IcebreakerSpecs.Single(spec => spec.DefinitionId == GatewayCardCodes.Cleaver));
    }

    [Fact]
    public void GetIcebreakerFailsClosedForUnregisteredDefinitions()
    {
        CardDefinitionId unregistered = new("30001");
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.False(catalog.TryGetIcebreaker(unregistered, out _));
    }

    [Fact]
    public void RegisteringADifferentSpecForTheSameDefinitionThrows()
    {
        IcebreakerBehaviorSpec canonical = GatewayIcebreakerBehaviorSpecs.IcebreakerSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayCardCodes.Cleaver);
        Assert.Equal(GatewayCardCodes.Cleaver, canonical.DefinitionId);
        BehaviorCatalogBuilder builder = new(GatewayBehaviorCatalog.Version);
        builder.AddIcebreaker(canonical);

        IcebreakerBehaviorSpec conflicting = new(
            canonical.DefinitionId,
            baseStrength: canonical.BaseStrength,
            interfaceType: canonical.InterfaceType,
            breakCost: canonical.BreakCost + 1,
            breakLimit: canonical.BreakLimit,
            pumpCost: canonical.PumpCost,
            pumpStrength: canonical.PumpStrength);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddIcebreaker(conflicting));
        Assert.Contains(canonical.DefinitionId.Value, exception.Message);
        Assert.Equal(canonical, builder.Build().IcebreakerSpecs.Single(spec => spec.DefinitionId == canonical.DefinitionId));
    }
}
