using System.Runtime.CompilerServices;

namespace Netrunner.Core;

public static partial class StateInvariantValidator
{
    /// <summary>
    /// States that passed full validation, with the catalogs they passed
    /// against (engine plan E2). A <see cref="GameState"/> is immutable and
    /// validation depends only on the state and the catalogs, so a state that
    /// passed once passes again: authoritative Apply validates its input state,
    /// which is almost always the output it validated one action earlier, and
    /// that second full pass is skipped. Only the built-in immutable catalog
    /// types are trusted as cache keys; any other implementation is always
    /// validated in full. Known-bad states are never certified, so everything
    /// that was rejected before is still rejected.
    /// </summary>
    private static readonly ConditionalWeakTable<GameState, CertifiedCatalogs> Certified = new();

    private sealed record CertifiedCatalogs(IBehaviorCatalog? Behavior, IRuntimeCardCatalog? Runtime);

    /// <summary>Whether <paramref name="state"/> already passed validation against exactly these catalogs.</summary>
    public static bool IsCertified(GameState state, IBehaviorCatalog? behaviorCatalog, IRuntimeCardCatalog? runtimeCardCatalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Certified.TryGetValue(state, out CertifiedCatalogs? certified)
            && ReferenceEquals(certified.Behavior, behaviorCatalog)
            && ReferenceEquals(certified.Runtime, runtimeCardCatalog);
    }

    private static bool Cacheable(IBehaviorCatalog? behaviorCatalog, IRuntimeCardCatalog? runtimeCardCatalog) =>
        behaviorCatalog is null or BehaviorCatalog && runtimeCardCatalog is null or RuntimeCardCatalog;

    private static void Certify(GameState state, IBehaviorCatalog? behaviorCatalog, IRuntimeCardCatalog? runtimeCardCatalog)
    {
        if (Cacheable(behaviorCatalog, runtimeCardCatalog))
            Certified.AddOrUpdate(state, new CertifiedCatalogs(behaviorCatalog, runtimeCardCatalog));
    }
}
