using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>A tier of the A9 tracker: what it knows of the opponent's deck and how much it keeps.</summary>
public sealed record BeliefTier(string Name, bool KnowsList, TrackerMemory Memory)
{
    public static IReadOnlyList<BeliefTier> All { get; } =
    [
        new("easy", false, TrackerMemory.Forgetful),
        new("standard", false, TrackerMemory.Full),
        new("hard", true, TrackerMemory.Full),
    ];
}

/// <summary>
/// One tier's score over saved games (A9a): the tracker's predictions of the Corp's hidden cards at each Runner turn start,
/// against the truth, next to the prior alone (the pool, or Hard's list). Lower log loss, error and Brier are better.
/// </summary>
public sealed record BeliefScore(string Tier, int Slots, double SlotLogLoss, double SlotPriorLogLoss, double SlotTop1, double SlotPriorTop1,
    int HqSamples, double HqAgendaError, double HqPriorAgendaError, int RdSamples, double RdTopBrier, double RdTopPriorBrier,
    double TrackedHandPrecision);

/// <summary>
/// A9a's accuracy measure: replays saved games, feeds the Runner's own observations to a tracker per tier, and scores
/// what it believes of the Corp's face-down installs, HQ's agendas and R&amp;D's top against the hidden state the save holds.
/// </summary>
public static class BeliefAccuracy
{
    private sealed class Totals
    {
        public int Slots, HqSamples, RdSamples;
        public double SlotLogLoss, SlotPriorLogLoss, SlotTop1, SlotPriorTop1, HqError, HqPriorError, RdBrier, RdPriorBrier;
        public double TrackedHand, TrackedHandCorrect;
    }

    public static IReadOnlyList<BeliefScore> Score(IEnumerable<(LadderSavedGame Game, FullMatchDefinition Definition)> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        var totals = BeliefTier.All.ToDictionary(tier => tier.Name, _ => new Totals(), StringComparer.Ordinal);
        foreach ((LadderSavedGame game, FullMatchDefinition definition) in games)
            foreach ((string tier, Totals add) in ScoreGame(game, definition))
                Merge(totals[tier], add);
        return [.. BeliefTier.All.Select(tier => Result(tier.Name, totals[tier.Name]))];
    }

    private static IEnumerable<(string Tier, Totals Totals)> ScoreGame(LadderSavedGame saved, FullMatchDefinition definition)
    {
        ICardEvaluationCatalog catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        Dictionary<string, double> pool = Pool(catalog), list = List(definition.Request.CorpDeck);
        var trackers = BeliefTier.All.ToDictionary(tier => tier.Name, _ => CardTracker.Empty, StringComparer.Ordinal);
        var results = BeliefTier.All.ToDictionary(tier => tier.Name, _ => new Totals(), StringComparer.Ordinal);
        GameState state = saved.Replay.InitialSnapshot.ToState();
        PlayerSide? lastActive = null;
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            ApplyResult applied = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime);
            if (!applied.Accepted) break;
            state = applied.State;
            AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime),
                AgentFeatures.VersionWithHiddenAdvancements);
            foreach (BeliefTier tier in BeliefTier.All)
            {
                Dictionary<string, double> prior = Remaining(tier.KnowsList ? list : pool, features, catalog);
                trackers[tier.Name] = trackers[tier.Name].Observe(features, catalog, zone => Fitting(prior, catalog, zone), tier.Memory);
            }
            // Score at each Runner turn start: what the Runner believes as it plans its turn.
            if (state.Turn.ActiveSide == PlayerSide.Runner && lastActive == PlayerSide.Corp)
                foreach (BeliefTier tier in BeliefTier.All)
                    Measure(results[tier.Name], trackers[tier.Name], features, state, catalog, Remaining(tier.KnowsList ? list : pool, features, catalog));
            lastActive = state.Turn.ActiveSide;
        }
        return results.Select(pair => (pair.Key, pair.Value));
    }

    /// <summary>A game's tracked HQ against the truth at each Runner turn start, for one tier, to debug the tracker.</summary>
    public static IEnumerable<string> Trace(LadderSavedGame saved, FullMatchDefinition definition, string tierName)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definition);
        BeliefTier tier = BeliefTier.All.Single(value => value.Name == tierName);
        ICardEvaluationCatalog catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        Dictionary<string, double> prior = tier.KnowsList ? List(definition.Request.CorpDeck) : Pool(catalog);
        CardTracker tracker = CardTracker.Empty;
        GameState state = saved.Replay.InitialSnapshot.ToState();
        foreach (ReplayAction recorded in saved.Replay.Actions)
        {
            ApplyResult applied = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime);
            if (!applied.Accepted) break;
            GameState before = state;
            state = applied.State;
            AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, PlayerSide.Runner, definition.Behavior, definition.Runtime),
                AgentFeatures.VersionWithHiddenAdvancements);
            CardTracker previous = tracker;
            tracker = tracker.Observe(features, catalog, zone => Fitting(prior, catalog, zone), tier.Memory);
            string Hand(CardTracker value) => string.Join(", ", value.Cards.Where(card => card.Place == TrackedPlace.Hand)
                .GroupBy(card => card.DefinitionId).Select(group => $"{Title(group.Key)} {group.Sum(card => card.Weight):0.##}"));
            if (Hand(previous) != Hand(tracker))
            {
                string truth = string.Join(", ", state.GetZone(ZoneKind.CorpHand).CardIds.Select(id => Title(state.GetCard(id).DefinitionId.Value)).Order(StringComparer.Ordinal));
                LegalAction? chosen = before.PendingDecision?.LegalActions.FirstOrDefault(action => action.Id == recorded.Submission.ActionId);
                yield return $"#{recorded.Sequence} t{state.Turn.TurnNumber} {before.GetPlayer(recorded.Submission.Actor).Side} {chosen?.Kind} {chosen?.Description}";
                yield return $"    tracked HQ: {Hand(tracker)}";
                yield return $"    true HQ:    {truth}";
            }
        }
    }

    private static readonly Lazy<Dictionary<string, string>> Titles = new(() =>
    {
        try { return Netrunner.Cards.CardDataResource.LoadEmbedded().Cards.ToDictionary(card => card.Code, card => card.Title, StringComparer.Ordinal); }
        catch (InvalidOperationException) { return new(StringComparer.Ordinal); }
    });

    private static string Title(string code) => Titles.Value.TryGetValue(code, out string? title) ? title : code;

    private static void Measure(Totals totals, CardTracker tracker, AgentFeatures features, GameState state, ICardEvaluationCatalog catalog,
        Dictionary<string, double> prior)
    {
        // Face-down Corp installs.
        foreach (AgentServer server in features.Board.Servers)
            foreach ((string zone, IReadOnlyList<AgentCard?> cards, ZoneKind kind) in new[] { ("ice", (IReadOnlyList<AgentCard?>)server.Ice, ZoneKind.ServerIce),
                ("root", server.Root, ZoneKind.ServerRoot) })
            {
                ZoneState? truthZone = state.Zones.FirstOrDefault(value => value.ServerId?.Value == server.Id && value.Kind == kind);
                for (int index = 0; index < cards.Count; index++)
                {
                    if (cards[index] is not null || truthZone is null || index >= truthZone.CardIds.Count) continue;
                    string truth = state.GetCard(truthZone.CardIds[index]).DefinitionId.Value;
                    IReadOnlyDictionary<string, double> belief = tracker.SlotBelief(server.Id, zone, index);
                    Dictionary<string, double> fitting = Conditional(prior, catalog, zone);
                    double residual = Math.Max(0, 1 - belief.Values.Sum());
                    double Predict(string definition) => belief.GetValueOrDefault(definition) + residual * fitting.GetValueOrDefault(definition);
                    totals.Slots++;
                    totals.SlotLogLoss -= Math.Log(Math.Max(1e-4, Predict(truth)));
                    totals.SlotPriorLogLoss -= Math.Log(Math.Max(1e-4, fitting.GetValueOrDefault(truth)));
                    string? best = fitting.Keys.Concat(belief.Keys).Distinct(StringComparer.Ordinal).OrderByDescending(Predict)
                        .ThenBy(value => value, StringComparer.Ordinal).FirstOrDefault();
                    string? priorBest = fitting.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).FirstOrDefault().Key;
                    if (best == truth) totals.SlotTop1++;
                    if (priorBest == truth) totals.SlotPriorTop1++;
                }
            }

        // HQ's agendas.
        double agendaShare = Share(prior, catalog, IsAgenda);
        int hand = features.Board.HiddenZoneCounts.CorpHand;
        List<string> trueHand = [.. state.GetZone(ZoneKind.CorpHand).CardIds.Select(id => state.GetCard(id).DefinitionId.Value)];
        double trackedHand = tracker.Cards.Where(card => card.Place == TrackedPlace.Hand).Sum(card => card.Weight);
        double trackedAgendas = tracker.Cards.Where(card => card.Place == TrackedPlace.Hand && IsAgenda(catalog, card.DefinitionId)).Sum(card => card.Weight);
        double predicted = trackedAgendas + Math.Max(0, hand - trackedHand) * agendaShare;
        int actual = trueHand.Count(definition => IsAgenda(catalog, definition));
        totals.HqSamples++;
        totals.HqError += Math.Abs(predicted - actual);
        totals.HqPriorError += Math.Abs(hand * agendaShare - actual);
        foreach (IGrouping<string, TrackedCard> group in tracker.Cards.Where(card => card.Place == TrackedPlace.Hand).GroupBy(card => card.DefinitionId))
        {
            double mass = group.Sum(card => card.Weight);
            totals.TrackedHand += mass;
            totals.TrackedHandCorrect += Math.Min(mass, trueHand.Count(definition => definition == group.Key));
        }

        // R&D's top card: an agenda?
        ZoneState deck = state.GetZone(ZoneKind.CorpDeck);
        if (deck.CardIds.Count > 0)
        {
            bool topAgenda = IsAgenda(catalog, state.GetCard(deck.CardIds[0]).DefinitionId.Value);
            double p = tracker.DeckTopAt(0) is { } known ? IsAgenda(catalog, known) ? 1 : 0 : agendaShare;
            totals.RdSamples++;
            totals.RdBrier += Math.Pow(p - (topAgenda ? 1 : 0), 2);
            totals.RdPriorBrier += Math.Pow(agendaShare - (topAgenda ? 1 : 0), 2);
        }
    }

    /// <summary>The pool prior: every Corp card of the content, up to the deck-building limit of three copies.</summary>
    private static Dictionary<string, double> Pool(ICardEvaluationCatalog catalog) => catalog.DefinitionIds
        .Where(id => catalog.TryGet(id, out CardEvaluationDescriptor card) && card.Printed.Side == PlayerSide.Corp && card.Printed.CardClass != RuntimeCardClass.Identity)
        .ToDictionary(id => id.Value, _ => 3.0, StringComparer.Ordinal);

    /// <summary>
    /// The copies that can still be hidden: the prior's copies less those in public view now (Archives face up, scored,
    /// installed face up, in play). A first use of A9c's counting, from where the copies are now, so recursion returns them.
    /// </summary>
    public static Dictionary<string, double> Remaining(Dictionary<string, double> prior, AgentFeatures features, ICardEvaluationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(features);
        var seen = features.VisibleCards().Where(pair => pair.Location != "access")
            .GroupBy(pair => pair.Card.Id, StringComparer.Ordinal).Select(group => group.First().Card.DefinitionId)
            .GroupBy(definition => definition, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return prior.ToDictionary(pair => pair.Key, pair => Math.Max(0, pair.Value - seen.GetValueOrDefault(pair.Key)), StringComparer.Ordinal);
    }

    /// <summary>Hard's prior: the Corp's decklist.</summary>
    private static Dictionary<string, double> List(DeckRequest deck) => deck.Cards
        .GroupBy(entry => entry.Code, StringComparer.Ordinal).ToDictionary(group => group.Key, group => (double)group.Sum(entry => entry.Quantity), StringComparer.Ordinal);

    private static bool Fits(ICardEvaluationCatalog catalog, string definitionId, string zone) => catalog.TryGet(new CardDefinitionId(definitionId), out CardEvaluationDescriptor card)
        && (zone == "ice" ? card.Printed.CardClass == RuntimeCardClass.Ice
            : card.Printed.CardClass is RuntimeCardClass.Agenda or RuntimeCardClass.Asset or RuntimeCardClass.Upgrade);

    private static bool IsAgenda(ICardEvaluationCatalog catalog, string definitionId) =>
        catalog.TryGet(new CardDefinitionId(definitionId), out CardEvaluationDescriptor card) && card.Agenda is not null;

    private static double Share(Dictionary<string, double> prior, ICardEvaluationCatalog catalog, Func<ICardEvaluationCatalog, string, bool> test)
    {
        double total = prior.Values.Sum();
        return total <= 0 ? 0 : prior.Where(pair => test(catalog, pair.Key)).Sum(pair => pair.Value) / total;
    }

    /// <summary>The share of the opponent's deck that fits a zone, which the tracker uses for unknown hand cards.</summary>
    public static double Fitting(Dictionary<string, double> prior, ICardEvaluationCatalog catalog, string zone) =>
        Share(prior, catalog, (value, definition) => Fits(value, definition, zone));

    private static Dictionary<string, double> Conditional(Dictionary<string, double> prior, ICardEvaluationCatalog catalog, string zone)
    {
        Dictionary<string, double> fitting = prior.Where(pair => Fits(catalog, pair.Key, zone)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        double total = fitting.Values.Sum();
        return total <= 0 ? fitting : fitting.ToDictionary(pair => pair.Key, pair => pair.Value / total, StringComparer.Ordinal);
    }

    private static void Merge(Totals into, Totals add)
    {
        into.Slots += add.Slots; into.HqSamples += add.HqSamples; into.RdSamples += add.RdSamples;
        into.SlotLogLoss += add.SlotLogLoss; into.SlotPriorLogLoss += add.SlotPriorLogLoss; into.SlotTop1 += add.SlotTop1; into.SlotPriorTop1 += add.SlotPriorTop1;
        into.HqError += add.HqError; into.HqPriorError += add.HqPriorError; into.RdBrier += add.RdBrier; into.RdPriorBrier += add.RdPriorBrier;
        into.TrackedHand += add.TrackedHand; into.TrackedHandCorrect += add.TrackedHandCorrect;
    }

    private static BeliefScore Result(string tier, Totals t)
    {
        static double Mean(double sum, int count) => count == 0 ? 0 : Math.Round(sum / count, 4);
        return new(tier, t.Slots, Mean(t.SlotLogLoss, t.Slots), Mean(t.SlotPriorLogLoss, t.Slots), Mean(t.SlotTop1, t.Slots), Mean(t.SlotPriorTop1, t.Slots),
            t.HqSamples, Mean(t.HqError, t.HqSamples), Mean(t.HqPriorError, t.HqSamples), t.RdSamples, Mean(t.RdBrier, t.RdSamples), Mean(t.RdPriorBrier, t.RdSamples),
            t.TrackedHand <= 0 ? 0 : Math.Round(t.TrackedHandCorrect / t.TrackedHand, 4));
    }
}
