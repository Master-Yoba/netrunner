using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Compiled asset behavior specs for the representative System Gateway asset.
/// Values are hand-copied from the official printed card text; no text
/// parsing happens here.
/// </summary>
public static class GatewayAssetBehaviorSpecs
{
    private static readonly AssetBehaviorSpec NicoCampaign = new(
        GatewayCardCodes.NicoCampaign,
        trashCost: 2);

    /// <summary>The exactly one compiled asset spec, in card order.</summary>
    public static IReadOnlyList<AssetBehaviorSpec> AssetSpecs { get; } =
        new ReadOnlyCollection<AssetBehaviorSpec>(new[] { NicoCampaign });

    /// <summary>Registers <see cref="AssetSpecs"/> into the shared catalog (idempotent).</summary>
    public static void RegisterAll() => CardBehaviorSpecCatalog.RegisterAsset(AssetSpecs);
}
