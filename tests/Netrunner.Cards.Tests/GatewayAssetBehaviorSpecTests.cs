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
    public void AssetSpecsBindExactlyTheOneRepresentativeAssetDefinition()
    {
        Assert.Equal(
            new[] { GatewayCardCodes.NicoCampaign },
            GatewayAssetBehaviorSpecs.AssetSpecs
                .Select(static spec => spec.DefinitionId)
                .ToArray());
    }

    [Fact]
    public void RegisterAllIsIdempotentAndRetrievalSucceeds()
    {
        GatewayAssetBehaviorSpecs.RegisterAll();
        GatewayAssetBehaviorSpecs.RegisterAll();

        Assert.True(CardBehaviorSpecCatalog.TryGetAsset(GatewayCardCodes.NicoCampaign, out AssetBehaviorSpec? spec));
        Assert.Equal(GatewayAssetBehaviorSpecs.AssetSpecs[0], spec);
        Assert.Equal(
            GatewayAssetBehaviorSpecs.AssetSpecs[0],
            CardBehaviorSpecCatalog.GetAsset(GatewayCardCodes.NicoCampaign));
    }

    [Fact]
    public void GetAssetFailsClosedForUnregisteredDefinitions()
    {
        CardDefinitionId unregistered = new("30001");

        Assert.False(CardBehaviorSpecCatalog.TryGetAsset(unregistered, out _));
        Assert.Throws<ArgumentException>(() => CardBehaviorSpecCatalog.GetAsset(unregistered));
    }

    [Fact]
    public void RegisteringADifferentSpecForTheSameDefinitionThrows()
    {
        AssetBehaviorSpec canonical = GatewayAssetBehaviorSpecs.AssetSpecs[0];
        Assert.Equal(GatewayCardCodes.NicoCampaign, canonical.DefinitionId);
        CardBehaviorSpecCatalog.RegisterAsset(canonical);

        AssetBehaviorSpec conflicting = new(canonical.DefinitionId, trashCost: canonical.TrashCost + 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => CardBehaviorSpecCatalog.RegisterAsset(conflicting));
        Assert.Contains(canonical.DefinitionId.Value, exception.Message);
        Assert.Equal(canonical, CardBehaviorSpecCatalog.GetAsset(canonical.DefinitionId));
    }
}
