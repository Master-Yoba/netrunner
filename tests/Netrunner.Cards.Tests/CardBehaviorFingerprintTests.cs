using Netrunner.Core;
using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>Per-card behavior fingerprints scope per-slice runs to the deck pools a change can affect (scripts/affected-pools.sh).</summary>
public sealed class CardBehaviorFingerprintTests
{
    [Fact]
    public void ACardsFingerprintIsStableAcrossBuildsOfItsCatalogAndUniqueToTheCard()
    {
        BehaviorCatalog first = ElevationBehaviorCatalog.Create();
        BehaviorCatalog second = ElevationBehaviorCatalog.Create();

        Assert.All(first.AllDefinitionIds, id =>
            Assert.Equal(first.GetCardBehaviorFingerprint(id), second.GetCardBehaviorFingerprint(id)));
        // Each card's fingerprint covers its own registrations, so two cards never share one.
        Assert.Equal(first.AllDefinitionIds.Count,
            first.AllDefinitionIds.Select(first.GetCardBehaviorFingerprint).Distinct(StringComparer.Ordinal).Count());
    }
}
