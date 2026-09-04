namespace Netrunner.Core.Tests;

/// <summary>One immutable catalog shared by behavior-bearing Core fixtures.</summary>
internal static class FixtureBehaviorCatalog
{
    public static readonly CardDefinitionId BreachAsset = new("fixture-asset");
    public static readonly CardDefinitionId BreachAgenda = new("fixture-agenda");

    public static BehaviorCatalog Catalog { get; } = new BehaviorCatalogBuilder(
            EngineCompatibility.BehaviorRegistryVersion)
        .AddIce(FixtureIceBehaviorSpecs.IceSpecs)
        .AddIcebreaker(FixtureIceBehaviorSpecs.IcebreakerSpecs)
        .AddAgenda(FixtureAgendaSpecs.AgendaSpecs)
        .AddAgenda(new AgendaBehaviorSpec(BreachAgenda, agendaPoints: 2))
        .AddAsset(new AssetBehaviorSpec(BreachAsset, trashCost: 2))
        .AddRunEvent(FixtureRunEventSpecs.RunEventSpecs)
        .Build();
}
