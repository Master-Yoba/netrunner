namespace Netrunner.Core;

/// <summary>
/// VP-SCORE-STEAL-COST (CR 26.03 1.16.10, 7.1.6a): the additional costs to steal an agenda: the agenda's own clicks and the
/// credits every active Corp card adds. The engine and the state validator both call this, so their steal offers agree.
/// </summary>
internal static class StealCosts
{
    public static (int Clicks, int Credits) For(
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        IBehaviorCatalog catalog,
        CardDefinitionId agenda)
    {
        int clicks = catalog.TryGetAgenda(agenda, out AgendaBehaviorSpec spec) ? spec.StealClicks : 0;
        int credits = 0;
        foreach (CardInstanceState card in cards)
        {
            if (card.OwnerSide == PlayerSide.Corp
                && catalog.TryGet(card.DefinitionId, out CardBehaviorDefinition? definition)
                && definition.StealCredits > 0
                && RuntimeCardActivity.IsActive(card, zones))
            {
                credits = checked(credits + definition.StealCredits);
            }
        }

        return (clicks, credits);
    }
}
