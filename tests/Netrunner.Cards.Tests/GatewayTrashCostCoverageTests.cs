using Netrunner.Core;
using Xunit;

namespace Netrunner.Cards.Tests;

/// <summary>
/// Every Corp card with a printed trash cost can be trashed on access for that cost (CR 7.2.2).
/// Playtest, 30 September: the engine offered the basic trash only for cards with a trash spec,
/// and Manegarm Skunkworks, an upgrade, had none, so the Runner could never trash it.
/// </summary>
public sealed class GatewayTrashCostCoverageTests
{
    [Fact]
    public void EveryPrintedTrashCostIsTheAccessTrashCost()
    {
        RuntimeCardCatalog runtime = GatewayRuntimeCardCatalog.CreateEmbedded();
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        var missing = new List<string>();
        foreach (CardDefinitionId id in runtime.DefinitionIds)
        {
            if (!runtime.TryGet(id, out CardRuntimeSpec spec) || spec.Side != PlayerSide.Corp || spec.TrashCost is not { } printed) continue;
            int? cost = GameEngine.AccessTrashCost(behavior, runtime, id);
            if (cost != printed) missing.Add($"{id.Value} ({spec.CardClass}): access trash cost {cost?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}, printed {printed}");
        }
        Assert.True(missing.Count == 0, string.Join("; ", missing));
    }
}
