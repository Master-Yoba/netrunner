using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>Stable card-pool identifiers.</summary>
public static class CardPoolIds
{
    public const string Gateway = "gateway";

    public const string GatewayElevation = "gateway-elevation";
}

/// <summary>
/// A card pool: the packs a game draws its cards from and the compiled
/// catalogs that play them. Formats name a pool; a saved game's behavior
/// version names the pool that loads it.
/// </summary>
public sealed class CardPool
{
    private readonly Func<BehaviorCatalog> _createBehavior;

    internal CardPool(
        string id,
        string name,
        IReadOnlyList<string> packCodes,
        string behaviorVersion,
        Func<BehaviorCatalog> createBehavior)
    {
        Id = id;
        Name = name;
        PackCodes = packCodes;
        BehaviorVersion = behaviorVersion;
        _createBehavior = createBehavior;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>The official packs in the pool, in pool order.</summary>
    public IReadOnlyList<string> PackCodes { get; }

    /// <summary>The behavior-registry version the pool's catalog carries.</summary>
    public string BehaviorVersion { get; }

    /// <summary>
    /// Whether this build embeds every pack of the pool. A synthetic build
    /// embeds only its fixture, which stands in for the Gateway pool.
    /// </summary>
    public bool IsEmbedded =>
        Id == CardPoolIds.Gateway || PackCodes.All(CardDataResource.HasEmbeddedPack);

    /// <summary>
    /// The pool's embedded card data. The Gateway pool is exactly the
    /// primary embedded payload, so its bytes and pins are unchanged.
    /// </summary>
    public CardDataSet LoadEmbeddedData() =>
        Id == CardPoolIds.Gateway
            ? CardDataResource.LoadEmbedded()
            : CardDataResource.LoadEmbeddedPool(PackCodes);

    public BehaviorCatalog CreateBehavior() => _createBehavior();

    /// <summary>Builds the pool's runtime catalog from its card data.</summary>
    public static RuntimeCardCatalog CreateRuntime(CardDataSet data) => GatewayRuntimeCardCatalog.Create(data);

    public RuntimeCardCatalog CreateEmbeddedRuntime() => CreateRuntime(LoadEmbeddedData());
}

/// <summary>The card pools this build can play.</summary>
public static class CardPools
{
    public static CardPool Gateway { get; } = new(
        CardPoolIds.Gateway,
        "System Gateway",
        [OfficialCardPacks.SystemGatewayCode],
        GatewayBehaviorCatalog.Version,
        GatewayBehaviorCatalog.Create);

    public static CardPool GatewayElevation { get; } = new(
        CardPoolIds.GatewayElevation,
        "System Gateway + Elevation",
        [OfficialCardPacks.SystemGatewayCode, OfficialCardPacks.ElevationCode],
        ElevationBehaviorCatalog.Version,
        ElevationBehaviorCatalog.Create);

    public static IReadOnlyList<CardPool> All { get; } = [Gateway, GatewayElevation];

    public static CardPool Get(string id) =>
        All.FirstOrDefault(pool => string.Equals(pool.Id, id, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Unknown card pool '{id}'.", nameof(id));

    /// <summary>The pool a format is played with.</summary>
    public static CardPool ForFormat(CardFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return Get(format.CardPool);
    }

    /// <summary>
    /// The pool whose catalog carries a behavior-registry version, as
    /// recorded in a saved game or replay.
    /// </summary>
    public static bool TryForBehaviorVersion(string? behaviorVersion, out CardPool pool)
    {
        pool = All.FirstOrDefault(candidate =>
            string.Equals(candidate.BehaviorVersion, behaviorVersion, StringComparison.Ordinal))!;
        return pool is not null;
    }
}
