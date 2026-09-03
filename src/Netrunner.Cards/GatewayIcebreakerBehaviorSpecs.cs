using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Compiled icebreaker behavior specs for the two representative System
/// Gateway icebreakers. Values are hand-copied from the official printed card
/// text; no text parsing happens here.
/// </summary>
public static class GatewayIcebreakerBehaviorSpecs
{
    private static readonly IcebreakerBehaviorSpec Cleaver = new(
        GatewayCardCodes.Cleaver,
        baseStrength: 3,
        interfaceType: IceType.Barrier,
        breakCost: 1,
        breakLimit: 2,
        pumpCost: 2,
        pumpStrength: 1);

    private static readonly IcebreakerBehaviorSpec Buzzsaw = new(
        GatewayCardCodes.Buzzsaw,
        baseStrength: 3,
        interfaceType: IceType.CodeGate,
        breakCost: 1,
        breakLimit: 2,
        pumpCost: 3,
        pumpStrength: 1);

    private static readonly IcebreakerBehaviorSpec Carmen = new(
        GatewayBatch2CardCodes.Carmen,
        baseStrength: 2,
        interfaceType: IceType.Sentry,
        breakCost: 1,
        breakLimit: 1,
        pumpCost: 2,
        pumpStrength: 3);

    private static readonly IcebreakerBehaviorSpec Marjanah = new(
        GatewayBatch2CardCodes.Marjanah,
        baseStrength: 1,
        interfaceType: IceType.Barrier,
        breakCost: 2,
        breakLimit: 1,
        pumpCost: 1,
        pumpStrength: 1);

    /// <summary>The compiled icebreaker specs, in ascending printed-code order.</summary>
    public static IReadOnlyList<IcebreakerBehaviorSpec> IcebreakerSpecs { get; } =
        new ReadOnlyCollection<IcebreakerBehaviorSpec>(new[] { Buzzsaw, Cleaver, Carmen, Marjanah });

    /// <summary>Registers <see cref="IcebreakerSpecs"/> into the shared catalog (idempotent).</summary>
    public static void RegisterAll() => CardBehaviorSpecCatalog.RegisterIcebreaker(IcebreakerSpecs);
}
