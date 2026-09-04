namespace Netrunner.Core.Tests;

/// <summary>
/// Immutable fixture run-event declarations. The fixture mirrors Overclock's
/// printed economics (cost 1, 5 hosted credits).
/// </summary>
internal static class FixtureRunEventSpecs
{
    public static readonly CardDefinitionId Overclock = new("fixture:event-overclock");
    public static readonly CardDefinitionId Spark = new("fixture:event-spark");

    public static RunEventBehaviorSpec OverclockSpec { get; } = new(
        Overclock,
        cost: 1,
        hostedCredits: 5);

    // A small-pool run event lets tests build payment/loss amounts that span
    // the run pool and the wallet at once.
    public static RunEventBehaviorSpec SparkSpec { get; } = new(
        Spark,
        cost: 1,
        hostedCredits: 1);

    public static IReadOnlyList<RunEventBehaviorSpec> RunEventSpecs { get; } =
    [OverclockSpec, SparkSpec];
}
