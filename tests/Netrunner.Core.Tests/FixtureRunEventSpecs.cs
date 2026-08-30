using System.Runtime.CompilerServices;

namespace Netrunner.Core.Tests;

/// <summary>
/// Fixture run event spec registered into the shared catalog at test
/// assembly load, so Core tests can exercise run-event initiation and
/// run-scoped hosted credits without referencing the Cards project. The
/// fixture mirrors Overclock's printed economics (cost 1, 5 hosted credits).
/// </summary>
internal static class FixtureRunEventSpecs
{
    public static readonly CardDefinitionId Overclock = new("fixture:event-overclock");
    public static readonly CardDefinitionId Spark = new("fixture:event-spark");

    [ModuleInitializer]
    internal static void Initialize()
    {
        CardBehaviorSpecCatalog.RegisterRunEvent(new RunEventBehaviorSpec(
            Overclock,
            cost: 1,
            hostedCredits: 5));
        // A small-pool run event so tests can build payment/loss amounts that
        // span the run pool and the wallet at once.
        CardBehaviorSpecCatalog.RegisterRunEvent(new RunEventBehaviorSpec(
            Spark,
            cost: 1,
            hostedCredits: 1));
    }
}
