using Netrunner.Core;
using Xunit;

namespace Netrunner.Cards.Tests;

public sealed class GatewayEvaluationCatalogTests
{
    [Fact]
    public void EveryGatewayDesignHasDetachedTypedMetadataAndExplicitApproximateUtility()
    {
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        // In synthetic CI this checks the complete compiled 77-design registry
        // with deliberately neutral printed identity specs, not official values.
        var builder = new RuntimeCardCatalogBuilder("test:all-design-descriptors");
        foreach (CardDefinitionId id in behavior.AllDefinitionIds)
            builder.Add(new CardRuntimeSpec(id, PlayerSide.Corp, RuntimeCardClass.Identity));
        RuntimeCardCatalog runtime = builder.Build();
        var catalog = new GatewayEvaluationCatalog(runtime, behavior);
        Assert.Equal(77, catalog.DefinitionIds.Count);
        Assert.Equal(behavior.AllDefinitionIds, catalog.DefinitionIds);
        foreach (CardDefinitionId id in catalog.DefinitionIds)
        {
            Assert.True(catalog.TryGet(id, out CardEvaluationDescriptor descriptor));
            Assert.Equal(id, descriptor.Printed.DefinitionId);
            Assert.True(descriptor.RequiresApproximateEvaluation);
            if (behavior.TryGetIce(id, out IceBehaviorSpec? ice))
            {
                Assert.NotSame(ice, descriptor.Ice);
                Assert.Equal(EngineJson.Serialize(ice), EngineJson.Serialize(descriptor.Ice));
            }
            if (behavior.TryGetIcebreaker(id, out IcebreakerBehaviorSpec? breaker))
                Assert.Equal(EngineJson.Serialize(breaker), EngineJson.Serialize(descriptor.Icebreaker));
            if (behavior.TryGet(id, out CardBehaviorDefinition? definition))
            {
                Assert.NotSame(definition, descriptor.Behavior);
                Assert.Equal(EngineJson.Serialize(definition), EngineJson.Serialize(descriptor.Behavior));
            }
        }
        Assert.Equal(catalog.Fingerprint, new GatewayEvaluationCatalog(runtime, behavior).Fingerprint);
        Assert.False(catalog.TryGet(new("test:unknown"), out _));
    }

    [Fact]
    public void EmbeddedMetadataRetainsEveryAvailablePrintedValueAndBehaviorCategory()
    {
        RuntimeCardCatalog runtime = GatewayRuntimeCardCatalog.CreateEmbedded();
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        var catalog = new GatewayEvaluationCatalog(runtime, behavior);
        foreach (CardDefinitionId id in runtime.DefinitionIds.Intersect(behavior.AllDefinitionIds))
        {
            Assert.True(catalog.TryGet(id, out CardEvaluationDescriptor descriptor));
            Assert.True(runtime.TryGet(id, out CardRuntimeSpec? printed));
            Assert.Equal(EngineJson.Serialize(printed), EngineJson.Serialize(descriptor.Printed));
        }
        // The official lane must exercise all designs with real printed values.
        if (CardDataResource.LoadEmbedded().Manifest.PackCode == CardDataValidator.OfficialPackCode)
            Assert.Equal(77, catalog.DefinitionIds.Count);
    }
}
