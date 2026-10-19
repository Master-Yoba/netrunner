using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Immutable composition of every compiled behavior in the Startup pool (System Gateway, Elevation and Vantage Point):
/// the unchanged Gateway + Elevation catalog plus the Vantage Point batches. That catalog keeps its own version and
/// fingerprint, so Gateway and Elevation games, saves and pins are unaffected by this composition. The batches VP.3, VP.5
/// and VP.7 add the Vantage Point cards; all three are registered.
/// </summary>
public static class VantagePointBehaviorCatalog
{
    /// <summary>Version of the current Startup composition.</summary>
    public const string Version = "startup-vp.7";

    /// <summary>Builds the immutable Startup behavior catalog.</summary>
    public static BehaviorCatalog Create() =>
        new BehaviorCatalogBuilder(Version, ElevationBehaviorCatalog.Create())
            // VP-SCORING-FACTS, VP-RUN-FACTS and VP-CLICK-COST-MODIFIER read opt-in turn events.
            .RecordTurnEvents(TurnEventKind.AgendaScored, TurnEventKind.AgendaScoredNotInstalledThisTurn,
                TurnEventKind.IceRezzed, TurnEventKind.DoubleOperationPlayed)
            .AddIcebreaker(VantagePointBatch1RunnerSpecs.IcebreakerSpecs)
            .AddRunEvent(VantagePointBatch1RunnerSpecs.RunEventSpecs)
            .Add(VantagePointBatch1RunnerSpecs.GenericSpecs)
            .AddAgenda(VantagePointBatch1CorpSpecs.AgendaSpecs)
            .AddAsset(VantagePointBatch1CorpSpecs.AssetSpecs)
            .AddIce(VantagePointBatch1CorpSpecs.IceSpecs)
            .Add(VantagePointBatch1CorpSpecs.GenericSpecs)
            .AddRunEvent(VantagePointBatch2RunnerSpecs.RunEventSpecs)
            .Add(VantagePointBatch2RunnerSpecs.GenericSpecs)
            .AddAgenda(VantagePointBatch2CorpSpecs.AgendaSpecs)
            .AddIce(VantagePointBatch2CorpSpecs.IceSpecs)
            .Add(VantagePointBatch2CorpSpecs.GenericSpecs)
            .AddAgenda(VantagePointBatch3Specs.AgendaSpecs)
            .AddIce(VantagePointBatch3Specs.IceSpecs)
            .Add(VantagePointBatch3Specs.GenericSpecs)
            .Build();
}
