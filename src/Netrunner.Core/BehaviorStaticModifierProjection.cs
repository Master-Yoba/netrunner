namespace Netrunner.Core;

/// <summary>
/// Computes effective static values from the authoritative installed-card
/// projection. Static modifiers are never applied as an activation effect and
/// therefore cannot stack merely because a behavior was resumed or invoked
/// more than once. Moving/trashing/deactivating the source card naturally
/// removes its contribution on the next projection.
/// </summary>
internal static class BehaviorStaticModifierProjection
{
    public static int Sum(
        IBehaviorCatalog catalog,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        PlayerSide side,
        BehaviorStaticModifierKind kind)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(zones);

        var zonesById = zones
            .GroupBy(zone => zone.Id)
            .ToDictionary(group => group.Key, group => group.First());
        long total = 0;
        foreach (CardInstanceState card in cards)
        {
            if (card.OwnerSide != side
                || !zonesById.TryGetValue(card.ZoneId, out ZoneState? zone)
                || !IsActiveInstalled(card, zone, side)
                || !catalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition))
            {
                continue;
            }

            foreach (BehaviorStaticModifier modifier in definition.StaticModifiers)
            {
                if (modifier.Kind == kind)
                {
                    total = checked(total + modifier.Delta);
                }
            }
        }

        if (total is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidOperationException("Static behavior modifiers exceed the supported integer range.");
        }

        return (int)total;
    }

    private static bool IsActiveInstalled(
        CardInstanceState card,
        ZoneState zone,
        PlayerSide side) =>
        side == PlayerSide.Runner
            ? zone.Kind == ZoneKind.RunnerRig && card.FaceUp
            : zone.Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce && card.Rezzed;
}
