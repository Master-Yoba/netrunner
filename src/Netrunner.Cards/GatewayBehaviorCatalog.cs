namespace Netrunner.Cards;

/// <summary>
/// Explicit, idempotent registration of every compiled System Gateway behavior.
/// Composition layers that consume M2 state (scenario setup, proof coordinators)
/// call this before validating or driving gateway cards; the spec registry
/// tolerates repeated registration of structurally identical specs.
/// </summary>
public static class GatewayBehaviorCatalog
{
    public static void RegisterAll()
    {
        GatewayIceBehaviorSpecs.RegisterAll();
        GatewayIcebreakerBehaviorSpecs.RegisterAll();
        GatewayRunEventBehaviorSpecs.RegisterAll();
        GatewayAgendaBehaviorSpecs.RegisterAll();
        GatewayAssetBehaviorSpecs.RegisterAll();
    }
}
