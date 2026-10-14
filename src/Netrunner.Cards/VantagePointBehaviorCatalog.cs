using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Immutable composition of every compiled behavior in the Startup pool (System Gateway, Elevation and Vantage Point):
/// the unchanged Gateway + Elevation catalog plus the Vantage Point batches. That catalog keeps its own version and
/// fingerprint, so Gateway and Elevation games, saves and pins are unaffected by this composition. VP.1 registers no
/// Vantage Point card yet; the batches VP.3, VP.5 and VP.7 add them.
/// </summary>
public static class VantagePointBehaviorCatalog
{
    /// <summary>Version of the current Startup composition.</summary>
    public const string Version = "startup-vp.1";

    /// <summary>Builds the immutable Startup behavior catalog.</summary>
    public static BehaviorCatalog Create() => new BehaviorCatalogBuilder(Version, ElevationBehaviorCatalog.Create()).Build();
}
