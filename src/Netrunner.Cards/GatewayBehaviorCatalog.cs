using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Immutable composition of every compiled System Gateway behavior.
/// Composition layers create one catalog and inject it into the session or
/// match that consumes it.
/// </summary>
public static class GatewayBehaviorCatalog
{
    /// <summary>
    /// Version of the current immutable Gateway behavior composition.
    /// </summary>
    public const string Version = "gateway-m4-4.4";

    /// <summary>
    /// Builds the immutable Gateway behavior catalog used by a production
    /// engine session.
    /// </summary>
    public static BehaviorCatalog Create()
    {
        var builder = new BehaviorCatalogBuilder(Version);
        return builder
            .AddIce(GatewayIceBehaviorSpecs.IceSpecs)
            .AddIcebreaker(GatewayIcebreakerBehaviorSpecs.IcebreakerSpecs)
            .AddIcebreaker(GatewayBatch3StrengthBehaviorSpecs.IcebreakerSpecs)
            .AddRunEvent(GatewayRunEventBehaviorSpecs.RunEventSpecs)
            .AddAgenda(GatewayAgendaBehaviorSpecs.AgendaSpecs)
            .AddAsset(GatewayAssetBehaviorSpecs.AssetSpecs)
            .Add(GatewayBatch1BehaviorSpecs.GenericSpecs)
            .Add(GatewayBatch2BehaviorSpecs.GenericSpecs)
            .Add(GatewayBatch3BehaviorSpecs.GenericSpecs)
            .Build();
    }

    /// <summary>Alias retained for callers that use build terminology.</summary>
    public static BehaviorCatalog Build() => Create();

}
