using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// A9: what a tracker assumes of the opponent's unseen cards. Easy and Standard know the card pool (every card of the
/// opponent's side, up to three copies); Hard knows the opponent's decklist. Counted from where the copies are now, so
/// a card in public view isn't also unseen, and recursion (Spin Doctor) returns it.
/// </summary>
public static class TrackerPrior
{
    /// <summary>Every non-identity card of a side in the content, at the deck-building limit of three copies.</summary>
    public static Dictionary<string, double> Pool(ICardEvaluationCatalog catalog, PlayerSide side)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.DefinitionIds
            .Where(id => catalog.TryGet(id, out CardEvaluationDescriptor card) && card.Printed.Side == side && card.Printed.CardClass != RuntimeCardClass.Identity)
            .ToDictionary(id => id.Value, _ => 3.0, StringComparer.Ordinal);
    }

    /// <summary>A decklist as copies by definition.</summary>
    public static Dictionary<string, double> List(IEnumerable<(string Code, int Quantity)> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards.GroupBy(entry => entry.Code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (double)group.Sum(entry => entry.Quantity), StringComparer.Ordinal);
    }

    /// <summary>The copies that can still be unseen: the prior's, less those in public view now.</summary>
    public static Dictionary<string, double> Remaining(IReadOnlyDictionary<string, double> prior, AgentFeatures features)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(features);
        var seen = features.VisibleCards().Where(pair => pair.Location != "access")
            .GroupBy(pair => pair.Card.Id, StringComparer.Ordinal).Select(group => group.First().Card.DefinitionId)
            .GroupBy(definition => definition, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return prior.ToDictionary(pair => pair.Key, pair => Math.Max(0, pair.Value - seen.GetValueOrDefault(pair.Key)), StringComparer.Ordinal);
    }

    /// <summary>Whether a card can be installed face down in a zone: roots take agendas, assets and upgrades; ICE positions take ICE.</summary>
    public static bool Fits(ICardEvaluationCatalog catalog, string definitionId, string zone)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.TryGet(new CardDefinitionId(definitionId), out CardEvaluationDescriptor card)
            && (zone == "ice" ? card.Printed.CardClass == RuntimeCardClass.Ice
                : card.Printed.CardClass is RuntimeCardClass.Agenda or RuntimeCardClass.Asset or RuntimeCardClass.Upgrade);
    }

    /// <summary>The share of the prior's copies that pass a test.</summary>
    public static double Share(IReadOnlyDictionary<string, double> prior, Func<string, bool> test)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(test);
        double total = prior.Values.Sum();
        return total <= 0 ? 0 : prior.Where(pair => test(pair.Key)).Sum(pair => pair.Value) / total;
    }

    /// <summary>The prior among the cards that pass a test, normalized.</summary>
    public static Dictionary<string, double> Conditional(IReadOnlyDictionary<string, double> prior, Func<string, bool> test)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(test);
        Dictionary<string, double> passing = prior.Where(pair => test(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        double total = passing.Values.Sum();
        return total <= 0 ? passing : passing.ToDictionary(pair => pair.Key, pair => pair.Value / total, StringComparer.Ordinal);
    }
}
