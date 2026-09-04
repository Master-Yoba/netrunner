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

    /// <summary>
    /// Tread Lightly: cost 1 event — run any server. During that run, each
    /// ICE rez costs 3 more credits.
    /// </summary>
    private static readonly RunEventBehaviorSpec TreadLightly = new(
        GatewayBatch3CardCodes.TreadLightly,
        cost: 1,
        hostedCredits: 0,
        rezCostModifiers:
        [
                new BehaviorRunRezCostModifier(3, "tread-lightly-ice-rez-cost"),
        ]);

    /// <summary>
    /// Jailbreak: cost 0 event — "Run HQ or R&amp;D. If successful, draw 1 card
    /// and when you breach the attacked server, access 1 additional card."
    /// </summary>
    private static readonly RunEventBehaviorSpec Jailbreak = new(
        GatewayBatch4CardCodes.Jailbreak,
        cost: 0,
        hostedCredits: 0,
        allowedTargetServers:
        [
            ServerKind.Headquarters,
            ServerKind.RAndD,
        ],
        successDraw: new BehaviorRunSuccessDraw(
            drawCount: 1,
            reasonKey: "jailbreak-success-draw"),
        breachAccessModifier: new BehaviorRunBreachAccessModifier(
            delta: 1,
            reasonKey: "jailbreak-additional-access"));

    /// <summary>The compiled run event specs, in stable card order.</summary>
    public static IReadOnlyList<RunEventBehaviorSpec> RunEventSpecs { get; } =
        new ReadOnlyCollection<RunEventBehaviorSpec>(new[] { Overclock, TreadLightly, Jailbreak });

}
