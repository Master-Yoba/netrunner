using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Immutable composition of every compiled behavior in the System Gateway +
/// Elevation pool: the unchanged Gateway catalog plus the Elevation batches.
/// The Gateway catalog keeps its own version and fingerprint, so Gateway
/// games, saves and the frozen M5 pins are unaffected by this composition.
/// </summary>
public static class ElevationBehaviorCatalog
{
    /// <summary>Version of the current Gateway + Elevation composition.</summary>
    public const string Version = "gateway-elevation-el.1";

    /// <summary>
    /// Printed icebreaker subtypes of the pool, hand-copied, for strength
    /// filters that count cards by subtype (Rising Tide counts fracters).
    /// </summary>
    private static readonly (string Code, string[] Subtypes)[] BreakerSubtypes =
    [
        ("30005", ["icebreaker", "decoder"]),
        ("30006", ["icebreaker", "fracter"]),
        ("30015", ["icebreaker", "killer"]),
        ("30016", ["icebreaker", "fracter"]),
        ("30025", ["icebreaker", "killer"]),
        ("30026", ["icebreaker", "decoder"]),
        ("30032", ["icebreaker", "ai"]),
        ("35008", ["icebreaker", "killer", "virus"]),
        ("35009", ["icebreaker", "fracter"]),
        ("35020", ["icebreaker", "decoder"]),
        ("35032", ["icebreaker", "fracter"]),
    ];

    /// <summary>
    /// Printed connection and job resource subtypes of the pool, hand-copied,
    /// for hosted credits that pay for those installs (Open Market).
    /// </summary>
    private static readonly (string Code, string[] Subtypes)[] ResourceSubtypes =
    [
        ("30018", ["job"]),
        ("30027", ["job"]),
        ("30033", ["connection"]),
        ("35011", ["connection", "seedy"]),
        ("35021", ["connection"]),
        ("35022", ["job", "location"]),
        ("35033", ["connection"]),
        ("35034", ["job"]),
    ];

    /// <summary>Builds the immutable Gateway + Elevation behavior catalog.</summary>
    public static BehaviorCatalog Create()
    {
        BehaviorCatalogBuilder builder = Compose().TrackTurnEvents();
        foreach ((string code, string[] subtypes) in BreakerSubtypes.Concat(ResourceSubtypes))
        {
            builder.AddCardSubtypes(new CardDefinitionId(code), subtypes);
        }

        return builder.Build();
    }

    private static BehaviorCatalogBuilder Compose() =>
        new BehaviorCatalogBuilder(Version, GatewayBehaviorCatalog.Create())
            .AddAsset(ElevationBatch1EconomySpecs.AssetSpecs)
            .AddRunEvent(ElevationBatch1EconomySpecs.RunEventSpecs)
            .Add(ElevationBatch1EconomySpecs.GenericSpecs)
            .AddIcebreaker(ElevationBatch1RunnerSpecs.IcebreakerSpecs)
            .Add(ElevationBatch1RunnerSpecs.GenericSpecs)
            .AddIce(ElevationBatch1IceSpecs.IceSpecs)
            .Add(ElevationBatch1IceSpecs.GenericSpecs)
            .AddAsset(ElevationBatch1CorpSpecs.AssetSpecs)
            .AddAgenda(ElevationBatch1CorpSpecs.AgendaSpecs)
            .Add(ElevationBatch1CorpSpecs.GenericSpecs);
}
