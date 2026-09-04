using Netrunner.Core;

using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayAssetBehaviorSpecTests
{
    [Fact]
    public void NicoCampaignSpecMatchesTheOfficialCardText()
    {
        AssetBehaviorSpec spec = GatewayAssetBehaviorSpecs.AssetSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayCardCodes.NicoCampaign);

        Assert.Equal("30037", spec.DefinitionId.Value);
        Assert.Equal(2, spec.TrashCost);
    }

    [Fact]
    public void AssetSpecsBindTheImplementedAssetDefinitionsInCardOrder()
    {
        Assert.Equal(
            new[]
            {
                GatewayCardCodes.NicoCampaign,
                GatewayBatch2CardCodes.SpinDoctor,
                GatewayBatch2CardCodes.RegolithMiningLicense,
            },
            GatewayAssetBehaviorSpecs.AssetSpecs
                .Select(static spec => spec.DefinitionId)
                .ToArray());

        AssetBehaviorSpec spinDoctor = GatewayAssetBehaviorSpecs.AssetSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayBatch2CardCodes.SpinDoctor);
        Assert.Equal(2, spinDoctor.TrashCost);
        AssetBehaviorSpec regolith = GatewayAssetBehaviorSpecs.AssetSpecs.Single(
            static candidate => candidate.DefinitionId == GatewayBatch2CardCodes.RegolithMiningLicense);
        Assert.Equal(3, regolith.TrashCost);
    }

    [Fact]
    public void ImmutableGatewayCatalogContainsTheCompiledAssetSpecs()
    {
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.True(catalog.TryGetAsset(GatewayCardCodes.NicoCampaign, out AssetBehaviorSpec? spec));
        Assert.Equal(GatewayAssetBehaviorSpecs.AssetSpecs[0], spec);
        Assert.Equal(
            GatewayAssetBehaviorSpecs.AssetSpecs[0],
            catalog.AssetSpecs.Single(spec => spec.DefinitionId == GatewayCardCodes.NicoCampaign));
        Assert.True(catalog.TryGetAsset(
            GatewayBatch2CardCodes.SpinDoctor,
            out AssetBehaviorSpec? spinDoctor));
        Assert.Equal(2, spinDoctor.TrashCost);
        Assert.True(catalog.TryGetAsset(
            GatewayBatch2CardCodes.RegolithMiningLicense,
            out AssetBehaviorSpec? regolith));
        Assert.Equal(3, regolith.TrashCost);
    }

    [Fact]
    public void GetAssetFailsClosedForUnregisteredDefinitions()
    {
        CardDefinitionId unregistered = new("30001");
        BehaviorCatalog catalog = GatewayBehaviorCatalog.Create();

        Assert.False(catalog.TryGetAsset(unregistered, out _));
    }

    [Fact]
    public void RegisteringADifferentSpecForTheSameDefinitionThrows()
    {
        AssetBehaviorSpec canonical = GatewayAssetBehaviorSpecs.AssetSpecs[0];
        Assert.Equal(GatewayCardCodes.NicoCampaign, canonical.DefinitionId);
        BehaviorCatalogBuilder builder = new(GatewayBehaviorCatalog.Version);
        builder.AddAsset(canonical);

        AssetBehaviorSpec conflicting = new(canonical.DefinitionId, trashCost: canonical.TrashCost + 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddAsset(conflicting));
        Assert.Contains(canonical.DefinitionId.Value, exception.Message);
        Assert.Equal(canonical, builder.Build().AssetSpecs.Single(spec => spec.DefinitionId == canonical.DefinitionId));
    }
}
