using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Compiled asset behavior specs for the implemented System Gateway assets.
/// Values are hand-copied from the official printed card text; no text
/// parsing happens here.
/// </summary>
public static class GatewayAssetBehaviorSpecs
{
    private static readonly AssetBehaviorSpec NicoCampaign = new(
        GatewayCardCodes.NicoCampaign,
        trashCost: 2);

    private static readonly AssetBehaviorSpec SpinDoctor = new(
        GatewayBatch2CardCodes.SpinDoctor,
        trashCost: 2);

    /// <summary>The compiled asset specs, in card order.</summary>
    public static IReadOnlyList<AssetBehaviorSpec> AssetSpecs { get; } =
        new ReadOnlyCollection<AssetBehaviorSpec>(new[] { NicoCampaign, SpinDoctor });

    /// <summary>Registers <see cref="AssetSpecs"/> into the shared catalog (idempotent).</summary>
    public static void RegisterAll() => CardBehaviorSpecCatalog.RegisterAsset(AssetSpecs);
}
