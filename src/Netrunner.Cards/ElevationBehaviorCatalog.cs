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

    /// <summary>Builds the immutable Gateway + Elevation behavior catalog.</summary>
    public static BehaviorCatalog Create() =>
        new BehaviorCatalogBuilder(Version, GatewayBehaviorCatalog.Create())
            .Build();
}
