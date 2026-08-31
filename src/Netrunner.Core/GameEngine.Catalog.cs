namespace Netrunner.Core;

/// <summary>Compatibility checks for an injected immutable behavior catalog.</summary>
public static partial class GameEngine
{
    private static bool CatalogMatchesState(
        GameState state,
        IBehaviorCatalog behaviorCatalog,
        out string? mismatch)
    {
        if (!string.Equals(state.BehaviorRegistryVersion, behaviorCatalog.Version, StringComparison.Ordinal))
        {
            mismatch =
                $"Behavior registry version '{state.BehaviorRegistryVersion}' does not match the injected catalog version '{behaviorCatalog.Version}'.";
            return false;
        }

        if (state.BehaviorCatalogFingerprint is not null
            && !string.Equals(state.BehaviorCatalogFingerprint, behaviorCatalog.Fingerprint, StringComparison.Ordinal))
        {
            mismatch =
                $"Behavior catalog fingerprint '{state.BehaviorCatalogFingerprint}' does not match the injected catalog fingerprint '{behaviorCatalog.Fingerprint}'.";
            return false;
        }

        mismatch = null;
        return true;
    }
}
