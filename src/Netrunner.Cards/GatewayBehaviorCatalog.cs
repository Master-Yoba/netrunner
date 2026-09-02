using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Explicit, idempotent registration of every compiled System Gateway behavior.
/// Composition layers that consume M2 state (scenario setup, proof coordinators)
/// call this before validating or driving gateway cards; the spec registry
/// tolerates repeated registration of structurally identical specs.
/// </summary>
public static class GatewayBehaviorCatalog
{
    /// <summary>
    /// Builds the immutable Gateway behavior catalog used by a production
    /// engine session.  This is the composition path for M4; the legacy
    /// <see cref="RegisterAll"/> method remains available to M2 callers.
    /// </summary>
    public static BehaviorCatalog Create()
    {
        var builder = new BehaviorCatalogBuilder(CardBehaviorSpecCatalog.Version);
        return builder
            .AddIce(GatewayIceBehaviorSpecs.IceSpecs)
            .AddIcebreaker(GatewayIcebreakerBehaviorSpecs.IcebreakerSpecs)
            .AddRunEvent(GatewayRunEventBehaviorSpecs.RunEventSpecs)
            .AddAgenda(GatewayAgendaBehaviorSpecs.AgendaSpecs)
            .AddAsset(GatewayAssetBehaviorSpecs.AssetSpecs)
            .Add(GatewayBatch1BehaviorSpecs.GenericSpecs)
            .Build();
    }

    /// <summary>Alias retained for callers that use build terminology.</summary>
    public static BehaviorCatalog Build() => Create();

    public static void RegisterAll()
    {
        GatewayIceBehaviorSpecs.RegisterAll();
        GatewayIcebreakerBehaviorSpecs.RegisterAll();
        GatewayRunEventBehaviorSpecs.RegisterAll();
        GatewayAgendaBehaviorSpecs.RegisterAll();
        GatewayAssetBehaviorSpecs.RegisterAll();
    }
}
