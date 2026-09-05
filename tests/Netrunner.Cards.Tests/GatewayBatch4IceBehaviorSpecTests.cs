using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>Card-owned printed declarations for the Slice 4.5 ICE.</summary>
public sealed class GatewayBatch4IceBehaviorSpecTests
{
    [Fact]
    [Trait("GatewayCard", "30038")]
    public void AnselBindsTheSharedBioroidAndPrintedSubroutinesInOrder()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        IceBehaviorSpec spec = Assert.Single(catalog.IceSpecs, candidate => candidate.DefinitionId == GatewayBatch4CardCodes.Ansel);

        Assert.Equal(4, spec.Strength);
        Assert.Equal(IceType.Sentry, spec.IceType);
        Assert.Equal(6, spec.RezCost);
        Assert.Equal(3, spec.Subroutines.Count);
        Assert.IsType<TrashInstalledRunnerCardSubroutineSpec>(spec.Subroutines[0]);
        Assert.Equal(
            new InstallCardSubroutineSpec(),
            Assert.IsType<InstallCardSubroutineSpec>(spec.Subroutines[1]));
        Assert.Equal(
            RunRestrictionKind.CannotStealOrTrashCorpCards,
            Assert.IsType<RunRestrictionSubroutineSpec>(spec.Subroutines[2]).Restriction);
        Assert.Equal(
            GatewayBatch4BioroidBehaviorSpecs.BioroidAbilities,
            spec.EncounteredBioroidAbilities);
    }

    [Fact]
    [Trait("GatewayCard", "30039")]
    public void BranBindsTheSharedBioroidAndInwardIgnoreCostThenTwoEndRuns()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        IceBehaviorSpec spec = Assert.Single(catalog.IceSpecs, candidate => candidate.DefinitionId == GatewayBatch4CardCodes.Bran);

        Assert.Equal(6, spec.Strength);
        Assert.Equal(IceType.Barrier, spec.IceType);
        Assert.Equal(6, spec.RezCost);
        InstallCardSubroutineSpec install = Assert.IsType<InstallCardSubroutineSpec>(spec.Subroutines[0]);
        Assert.Equal(IceInstallDestinationRule.DirectlyInward, install.DestinationRule);
        Assert.Equal(IceInstallCostRule.IgnoreAllCosts, install.CostRule);
        Assert.True(install.IceOnly);
        Assert.IsType<EndRunSubroutineSpec>(spec.Subroutines[1]);
        Assert.IsType<EndRunSubroutineSpec>(spec.Subroutines[2]);
        Assert.Equal(GatewayBatch4BioroidBehaviorSpecs.BioroidAbilities, spec.EncounteredBioroidAbilities);
    }

    [Fact]
    [Trait("GatewayCard", "30054")]
    public void FunhousePrintedSubroutineIsPayFourOrTakeOneTagAndReactionRemainsSeparate()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();
        IceBehaviorSpec spec = Assert.Single(catalog.IceSpecs, candidate => candidate.DefinitionId == GatewayBatch4CardCodes.Funhouse);
        PayCreditsOrTagSubroutineSpec pay = Assert.IsType<PayCreditsOrTagSubroutineSpec>(Assert.Single(spec.Subroutines));

        Assert.Equal(4, pay.CreditAmount);
        Assert.Equal(1, pay.TagAmount);
        Assert.Contains(
            catalog.GenericSpecs,
            definition => definition.DefinitionId == GatewayBatch4CardCodes.Funhouse
                && definition.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.IceEncountered));
    }
}
