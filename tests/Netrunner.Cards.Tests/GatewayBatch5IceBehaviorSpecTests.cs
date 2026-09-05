using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayBatch5IceBehaviorSpecTests
{
    [Theory]
    [Trait("GatewayCard", "30046")]
    [Trait("GatewayCard", "30047")]
    [Trait("GatewayCard", "30055")]
    [Trait("GatewayCard", "30062")]
    [Trait("GatewayCard", "30063")]
    [Trait("GatewayCard", "30073")]
    [InlineData("30046", 3, 2, IceType.CodeGate, 1)]
    [InlineData("30047", 3, 4, IceType.Sentry, 2)]
    [InlineData("30055", 1, 2, IceType.Barrier, 1)]
    [InlineData("30062", 4, 5, IceType.Sentry, 1)]
    [InlineData("30063", 5, 7, IceType.Barrier, 3)]
    [InlineData("30073", 1, 1, IceType.Sentry, 2)]
    public void PrintedIceValuesAreExact(string code, int strength, int rezCost, IceType iceType, int subroutines)
    {
        IceBehaviorSpec spec = Assert.Single(GatewayBatch5IceBehaviorSpecs.IceSpecs,
            value => value.DefinitionId.Value == code);
        Assert.Equal(strength, spec.Strength);
        Assert.Equal(rezCost, spec.RezCost);
        Assert.Equal(iceType, spec.IceType);
        Assert.Equal(subroutines, spec.Subroutines.Count);
    }

    [Fact]
    [Trait("GatewayCard", "30073")]
    public void TitheDealsDamageBeforeGainingOneCredit()
    {
        IceBehaviorSpec spec = Assert.Single(GatewayBatch5IceBehaviorSpecs.IceSpecs,
            value => value.DefinitionId == GatewayBatch5CardCodes.Tithe);
        Assert.Equal(new DamageSubroutineSpec(DamageKind.Net, 1), spec.Subroutines[0]);
        Assert.Equal(new GainCreditsSubroutineSpec(PlayerSide.Corp, 1), spec.Subroutines[1]);
    }

    [Fact]
    [Trait("GatewayCard", "30063")]
    public void PharosGivesOneTagBeforeItsTwoEndRunSubroutines()
    {
        IceBehaviorSpec spec = Assert.Single(GatewayBatch5IceBehaviorSpecs.IceSpecs,
            value => value.DefinitionId == GatewayBatch5CardCodes.Pharos);
        Assert.Equal(new GiveTagsSubroutineSpec(1), spec.Subroutines[0]);
        Assert.All(spec.Subroutines.Skip(1), value => Assert.IsType<EndRunSubroutineSpec>(value));
    }
}
