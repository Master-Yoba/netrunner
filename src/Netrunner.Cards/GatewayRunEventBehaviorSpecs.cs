using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Compiled run event behavior specs for the representative System Gateway
/// run event. Values are hand-copied from the official printed card text;
/// no text parsing happens here.
/// </summary>
public static class GatewayRunEventBehaviorSpecs
{
    /// <summary>
    /// Overclock: cost 1 event — "Put 5 credits on this event, then run any
    /// server. During that run, you may spend the hosted credits."
    /// </summary>
    private static readonly RunEventBehaviorSpec Overclock = new(
        GatewayCardCodes.Overclock,
        cost: 1,
        hostedCredits: 5);

    /// <summary>The exactly one compiled run event spec, in card order.</summary>
    public static IReadOnlyList<RunEventBehaviorSpec> RunEventSpecs { get; } =
        new ReadOnlyCollection<RunEventBehaviorSpec>(new[] { Overclock });

}
