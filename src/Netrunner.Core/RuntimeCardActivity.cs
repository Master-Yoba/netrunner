namespace Netrunner.Core;

/// <summary>
/// Pure runtime-catalog activity projection shared by rez cleanup and state
/// validation. Unknown definitions are intentionally absent from the result.
/// </summary>
internal static class RuntimeCardActivity
{
    internal static bool IsActive(
        CardInstanceState card,
        IReadOnlyList<ZoneState> zones)
    {
        ZoneState? zone = zones.FirstOrDefault(candidate => candidate.Id == card.ZoneId);
        if (zone is null || !zone.CardIds.Contains(card.Id))
        {
            return false;
        }

        return card.OwnerSide switch
        {
            PlayerSide.Runner => card.FaceUp
                && zone.Kind == ZoneKind.RunnerRig
                && zone.OwnerSide == PlayerSide.Runner
                && zone.ServerId is null,
            PlayerSide.Corp => card.FaceUp
                && card.Rezzed
                && zone.Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce
                && zone.OwnerSide == PlayerSide.Corp
                && zone.ServerId is not null,
            _ => false,
        };
    }

    internal static IEnumerable<(CardInstanceState Card, CardRuntimeSpec Spec)> EnumerateKnownActive(
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        IRuntimeCardCatalog runtimeCardCatalog)
    {
        foreach (CardInstanceState card in cards)
        {
            if (runtimeCardCatalog.TryGet(card.DefinitionId, out CardRuntimeSpec? spec)
                && spec.Side == card.OwnerSide
                && IsActive(card, zones))
            {
                yield return (card, spec);
            }
        }
    }
}
