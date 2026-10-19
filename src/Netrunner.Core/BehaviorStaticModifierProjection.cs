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
        BehaviorStaticModifierKind kind,
        IRuntimeCardCatalog? runtime = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(zones);

        // Few cards carry a modifier of this kind, so ask the catalog before finding the card's zone (S5.0: a
        // dictionary of every zone was built on every call).
        long total = 0;
        foreach (CardInstanceState card in cards)
        {
            if (card.OwnerSide != side
                || !catalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
                || !HasModifier(definition, kind) && !(kind == BehaviorStaticModifierKind.MaximumHandSize && definition.HostsResources is not null)
                || FirstZone(zones, card.ZoneId) is not { } zone
                || !(IsActiveInstalled(card, zone, side) || RuntimeCardActivity.IsActive(card, zones)))
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

            // VP-HOST-RESOURCES: while the host holds a resource of every subtype it hosts (Hackerspace).
            if (kind == BehaviorStaticModifierKind.MaximumHandSize && definition.HostsResources is { HandSizeWhileHostingEach: > 0 } hosting
                && runtime is not null
                && hosting.Subtypes.All(subtype => cards.Any(hosted => hosted.HostCardId == card.Id
                    && runtime.TryGet(hosted.DefinitionId, out CardRuntimeSpec? spec)
                    && spec.Subtypes.Contains(subtype, StringComparer.OrdinalIgnoreCase))))
            {
                total = checked(total + hosting.HandSizeWhileHostingEach);
            }
        }

        if (total is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidOperationException("Static behavior modifiers exceed the supported integer range.");
        }

        return (int)total;
    }

    private static bool HasModifier(CardBehaviorDefinition definition, BehaviorStaticModifierKind kind)
    {
        foreach (BehaviorStaticModifier modifier in definition.StaticModifiers)
        {
            if (modifier.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    private static ZoneState? FirstZone(IReadOnlyList<ZoneState> zones, ZoneId id)
    {
        foreach (ZoneState zone in zones)
        {
            if (zone.Id == id)
            {
                return zone;
            }
        }

        return null;
    }

    private static bool IsActiveInstalled(
        CardInstanceState card,
        ZoneState zone,
        PlayerSide side) =>
        side == PlayerSide.Runner
            ? zone.Kind == ZoneKind.RunnerRig && card.FaceUp
            : zone.Kind is ZoneKind.ServerRoot or ZoneKind.ServerIce && card.Rezzed;
}
