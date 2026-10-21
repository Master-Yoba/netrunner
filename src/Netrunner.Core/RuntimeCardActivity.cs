namespace Netrunner.Core;

/// <summary>
/// Pure runtime-catalog activity projection shared by rez cleanup and state
/// validation. Unknown definitions are intentionally absent from the result.
/// </summary>
internal static class RuntimeCardActivity
{
    /// <summary>
    /// Checks the canonical installed-zone topology represented by a frozen
    /// source snapshot. This deliberately does not inspect current card
    /// membership: a source may move after an invocation is materialized.
    /// </summary>
    internal static bool IsActiveInstalledZone(
        PlayerSide ownerSide,
        ZoneState zone,
        IReadOnlyList<ZoneState> zones,
        IReadOnlyList<ServerState> servers)
    {
        return ownerSide switch
        {
            PlayerSide.Runner => zone.Kind == ZoneKind.RunnerRig
                && zone.OwnerSide == PlayerSide.Runner
                && zone.ServerId is null
                && zones.Count(candidate => candidate.Kind == ZoneKind.RunnerRig
                    && candidate.OwnerSide == PlayerSide.Runner
                    && candidate.ServerId is null) == 1,
            PlayerSide.Corp => zone.Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce
                && zone.OwnerSide == PlayerSide.Corp
                && zone.ServerId is { } serverId
                && servers.Any(server => server.Id == serverId
                    && (zone.Kind == ZoneKind.ServerRoot
                        ? server.RootZoneId == zone.Id
                        : server.IceZoneId == zone.Id)),
            _ => false,
        };
    }

    internal static bool IsActive(
        CardInstanceState card,
        IReadOnlyList<ZoneState> zones)
    {
        ZoneState? zone = zones.FirstOrDefault(candidate => candidate.Id == card.ZoneId);
        if (zone is null || !zone.CardIds.Contains(card.Id))
        {
            return false;
        }

        if (card.FaceUp && zone.ServerId is null
            && (zone.Kind == ZoneKind.CorpIdentity && card.OwnerSide == PlayerSide.Corp
                || zone.Kind == ZoneKind.RunnerIdentity && card.OwnerSide == PlayerSide.Runner
                || zone.Kind == ZoneKind.CorpScore && card.OwnerSide == PlayerSide.Corp)) return true;

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
