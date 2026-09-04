using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayIceBehaviorSpecTests
{
    [Fact]
    public void PalisadeSpecMatchesTheOfficialCardText()
    {
        IceBehaviorSpec spec = GatewayIceBehaviorSpecs.IceSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayCardCodes.Palisade);

        Assert.Equal(2, spec.Strength);
        Assert.Equal(3, spec.RezCost);
        Assert.IsType<EndRunSubroutineSpec>(Assert.Single(spec.Subroutines));
        Assert.Single(
            spec.StrengthModifiers,
            static modifier => modifier.Delta == 2
                && modifier.Condition == IceStrengthModifierCondition.ProtectingRemoteServer);
    }

    [Fact]
    public void WhitespaceSpecMatchesTheOfficialCardTextInPrintedOrder()
    {
        IceBehaviorSpec spec = GatewayIceBehaviorSpecs.IceSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayCardCodes.Whitespace);

        Assert.Equal(0, spec.Strength);
        Assert.Equal(2, spec.RezCost);
        Assert.Empty(spec.StrengthModifiers);
        Assert.Equal(2, spec.Subroutines.Count);

        LoseCreditsSubroutineSpec first = Assert.IsType<LoseCreditsSubroutineSpec>(spec.Subroutines[0]);
        Assert.Equal(PlayerSide.Runner, first.Target);
        Assert.Equal(3, first.Amount);

        EndRunIfCreditsAtMostSubroutineSpec second =
            Assert.IsType<EndRunIfCreditsAtMostSubroutineSpec>(spec.Subroutines[1]);
        Assert.Equal(PlayerSide.Runner, second.Target);
        Assert.Equal(6, second.CreditLimit);
    }

    [Fact]
    public void IceSpecsBindExactlyTheTwoRepresentativeIceDefinitions()
    {
        Assert.Equal(
            new[] { GatewayCardCodes.Palisade, GatewayCardCodes.Whitespace },
            GatewayIceBehaviorSpecs.IceSpecs
                .Select(static spec => spec.DefinitionId)
                .ToArray());
    }

    [Fact]
    public void ImmutableGatewayCatalogContainsTheCompiledIceSpecs()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        IReadOnlyCollection<CardDefinitionId> definitionIds = catalog.DefinitionIds;
        Assert.Equal(2, definitionIds.Count);
        Assert.Contains(GatewayCardCodes.Palisade, definitionIds);
        Assert.Contains(GatewayCardCodes.Whitespace, definitionIds);

        Assert.Equal(
            GatewayIceBehaviorSpecs.IceSpecs[0],
            catalog.IceSpecs.Single(spec => spec.DefinitionId == GatewayCardCodes.Palisade));
        Assert.Equal(
            GatewayIceBehaviorSpecs.IceSpecs[1],
            catalog.IceSpecs.Single(spec => spec.DefinitionId == GatewayCardCodes.Whitespace));
    }

    [Fact]
    public void GetIceFailsClosedForUnregisteredDefinitions()
    {
        CardDefinitionId unregistered = new("30001");
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.False(catalog.TryGetIce(unregistered, out _));
    }

    [Fact]
    public void RegisteringADifferentSpecForTheSameDefinitionThrows()
    {
        IceBehaviorSpec canonical = GatewayIceBehaviorSpecs.IceSpecs[0];
        Assert.Equal(GatewayCardCodes.Palisade, canonical.DefinitionId);
        BehaviorCatalogBuilder builder = new(GatewayBehaviorCatalog.Version);
        builder.AddIce(canonical);

        IceBehaviorSpec conflicting = new(
            canonical.DefinitionId,
            strength: canonical.Strength + 1,
            iceType: canonical.IceType,
            rezCost: canonical.RezCost,
            subroutines: canonical.Subroutines,
            strengthModifiers: canonical.StrengthModifiers);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddIce(conflicting));
        Assert.Contains(canonical.DefinitionId.Value, exception.Message);
        Assert.Equal(canonical, builder.Build().IceSpecs.Single());
    }
}
