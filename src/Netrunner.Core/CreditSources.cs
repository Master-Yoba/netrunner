namespace Netrunner.Core;

/// <summary>
/// VP-STEALTH-CREDITS: where a Runner payment during a run may draw credits from outside the credit pool (Vantage Point
/// progress, VP.2's design). The engine and the state validator both call these, so the offers they compute agree.
/// Credits from outside the pool are spent in a fixed order: the bad publicity fund, the active run event's credits, then
/// the hosted credits on installed Runner cards that may be spent during runs. Non-stealth credits go before stealth ones and
/// expiring before lasting, so the order never costs the Runner a payment it could otherwise make.
/// </summary>
internal static class CreditSources
{
    /// <summary>The subtype of the cards whose credits pay a stealth-only cost (CR 26.03 2.16).</summary>
    public const string Stealth = "stealth";

    /// <summary>
    /// The hosted credit pools on active Runner cards that may be spent during runs (Methuselah, Touchstone), optionally only
    /// those of cards with a printed subtype. In spending order: for an unrestricted payment non-stealth pools come before
    /// stealth ones, so a later stealth-only cost keeps every credit it can use (PR #152's review); then card order.
    /// </summary>
    public static IEnumerable<(CardInstanceId Card, int Credits)> RunSpendablePools(
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        RulesState? rules,
        IRuntimeCardCatalog? runtime,
        string? subtype = null)
    {
        if (rules is null)
        {
            yield break;
        }

        var pools = new List<(CardInstanceId Card, int Credits, bool Stealth)>();
        // An AnyCost pool pays run costs too (PR #152's review): the shared predicate decides.
        foreach (CardCounterState counter in rules.Counters
            .Where(static counter => counter.Count > 0 && CardCounterRules.CanSpend(counter, HostedCreditSpendRestriction.RunCost))
            .OrderBy(static counter => counter.CardId.Value, StringComparer.Ordinal))
        {
            CardInstanceState? card = cards.FirstOrDefault(candidate => candidate.Id == counter.CardId);
            if (card is null
                || card.OwnerSide != PlayerSide.Runner
                || !RuntimeCardActivity.IsActive(card, zones)
                || subtype is not null && !HasSubtype(runtime, card.DefinitionId, subtype))
            {
                continue;
            }

            pools.Add((card.Id, counter.Count, subtype is null && HasSubtype(runtime, card.DefinitionId, Stealth)));
        }

        foreach ((CardInstanceId card, int credits, _) in pools.OrderBy(static pool => pool.Stealth ? 1 : 0))
        {
            yield return (card, credits);
        }
    }

    /// <summary>The hosted credits on active Runner cards that may be spent during runs.</summary>
    public static int RunSpendable(
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        RulesState? rules,
        IRuntimeCardCatalog? runtime,
        string? subtype = null) =>
        RunSpendablePools(cards, zones, rules, runtime, subtype).Sum(static pool => pool.Credits);

    /// <summary>
    /// Every credit outside the Runner's pool a payment during this run may spend: the run-scoped credits (the bad
    /// publicity fund and the active run event's credits) and the run-spendable hosted credits.
    /// </summary>
    public static int OutsidePool(
        RunState run,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        RulesState? rules,
        IRuntimeCardCatalog? runtime) =>
        checked(run.RunCredits + RunSpendable(cards, zones, rules, runtime));

    /// <summary>
    /// The credits a cost payable only with credits from cards of the subtype may spend: the active run event's own credits
    /// when the event has the subtype (never the bad publicity fund), then such cards' run-spendable hosted credits.
    /// </summary>
    public static int RestrictedTo(
        string subtype,
        RunState? run,
        IReadOnlyList<CardInstanceState> cards,
        IReadOnlyList<ZoneState> zones,
        RulesState? rules,
        IRuntimeCardCatalog? runtime)
    {
        int eventCredits = run?.ActiveRunEventId is { } eventId
            && cards.FirstOrDefault(card => card.Id == eventId) is { } runEvent
            && HasSubtype(runtime, runEvent.DefinitionId, subtype)
                ? run.RunCredits - run.BadPublicityCredits
                : 0;
        int hosted = run is null ? 0 : RunSpendable(cards, zones, rules, runtime, subtype);
        return checked(eventCredits + hosted);
    }

    /// <summary>Whether the card's printed subtypes include the subtype.</summary>
    public static bool HasSubtype(IRuntimeCardCatalog? runtime, CardDefinitionId definitionId, string subtype) =>
        runtime?.TryGet(definitionId, out CardRuntimeSpec spec) == true
        && spec.Subtypes.Contains(subtype, StringComparer.OrdinalIgnoreCase);
}
