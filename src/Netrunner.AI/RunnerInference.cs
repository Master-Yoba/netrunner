using System.Collections.Immutable;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// A9b: what the Runner infers from what it has seen (the A9a tracker of the Corp's hidden cards), for Runner v21 on.
/// Every question combines the tracked cards with the prior over the rest: the Corp's decklist for Hard, the card pool
/// otherwise, counted from where the copies are now.
/// </summary>
internal static class RunnerInference
{
    /// <summary>A guess this sure is planned as the card (a face-down ICE or root card).</summary>
    public const double Confident = 0.6;

    public static CardTracker? Tracker(HeuristicContext context) => context.Knowledge?.Tracker;

    /// <summary>The Corp's unseen copies by definition, from what the Runner knows of its deck.</summary>
    public static IReadOnlyDictionary<string, double> Prior(HeuristicContext context) => context.Memo("corp-prior", () =>
        TrackerPrior.Remaining(FullPrior(context), context.Features));

    private static Dictionary<string, double> FullPrior(HeuristicContext context) =>
        context.Knowledge?.OpponentList is { } list
            ? TrackerPrior.List(list.Select(entry => (entry.DefinitionId, entry.Quantity)))
            : TrackerPrior.Pool(context.Catalog, PlayerSide.Corp);

    private static bool IsAgenda(HeuristicContext context, string definition) =>
        context.Catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card) && card.Agenda is not null;

    /// <summary>The agenda share of the Corp's unseen cards.</summary>
    public static double AgendaShare(HeuristicContext context) =>
        context.Memo("agenda-share", () => TrackerPrior.Share(Prior(context), definition => IsAgenda(context, definition)));

    /// <summary>How much of HQ the tracker knows, by weight.</summary>
    public static double HqKnownShare(HeuristicContext context)
    {
        int hand = context.Features.Board.HiddenZoneCounts.CorpHand;
        return hand == 0 ? 0 : Math.Min(1, (Tracker(context)?.Cards.Where(card => card.Place == TrackedPlace.Hand).Sum(card => card.Weight) ?? 0) / hand);
    }

    /// <summary>The expected share of HQ that is agendas: the tracked cards, and the rest at the unseen cards' rate.</summary>
    public static double HqAgendaShare(HeuristicContext context)
    {
        int hand = context.Features.Board.HiddenZoneCounts.CorpHand;
        if (hand == 0) return 0;
        ImmutableArray<TrackedCard> cards = Tracker(context)?.Cards ?? [];
        double tracked = cards.Where(card => card.Place == TrackedPlace.Hand).Sum(card => card.Weight);
        double agendas = cards.Where(card => card.Place == TrackedPlace.Hand && IsAgenda(context, card.DefinitionId)).Sum(card => card.Weight);
        return Math.Clamp((agendas + Math.Max(0, hand - tracked) * AgendaShare(context)) / hand, 0, 1);
    }

    /// <summary>The chance R&amp;D's top card is an agenda: known when the Runner has seen it, otherwise at the unseen rate.</summary>
    public static double RdTopAgenda(HeuristicContext context)
    {
        CardTracker? tracker = Tracker(context);
        if (tracker?.DeckTopAt(0) is { } top) return IsAgenda(context, top) ? 1 : 0;
        int deck = context.Features.Board.HiddenZoneCounts.CorpDeck;
        if (deck == 0) return 0;
        ImmutableArray<TrackedCard> cards = tracker?.Cards ?? [];
        double placed = cards.Where(card => card.Place == TrackedPlace.DeckTop).Sum(card => card.Weight);
        double loose = cards.Where(card => card.Place == TrackedPlace.Deck).Sum(card => card.Weight);
        double looseAgendas = cards.Where(card => card.Place == TrackedPlace.Deck && IsAgenda(context, card.DefinitionId)).Sum(card => card.Weight);
        double open = Math.Max(1, deck - placed);
        return Math.Clamp((looseAgendas + Math.Max(0, open - loose) * AgendaShare(context)) / open, 0, 1);
    }

    /// <summary>The net damage accessing a card deals: its access trigger's, plus its counters when it scales with them.</summary>
    public static int AccessDamage(CardEvaluationDescriptor? definition, int counters) => definition?.Behavior?.Triggers
        .Where(trigger => trigger.Occurrence == OccurrenceKind.CardAccessed)
        .SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions)).Where(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage)
        .Sum(instruction => instruction.DamageAmountOperand is { Kind: BehaviorDamageAmountKind.SourceAdvancementCounters } operand
            ? operand.BaseAmount + counters : instruction.Amount) ?? 0;

    /// <summary>
    /// What the tracker believes a face-down root card may be, as (damage on access, probability) per tracked trap, and
    /// the probability left for cards it doesn't track.
    /// </summary>
    public static (IReadOnlyList<(int Damage, double Probability)> Traps, double Residual) RootTraps(HeuristicContext context, AgentServer server, int index,
        int counters)
    {
        if (Tracker(context) is not { } tracker) return ([], 1);
        IReadOnlyDictionary<string, double> belief = tracker.SlotBelief(server.Id, "root", index);
        var traps = new List<(int, double)>();
        foreach ((string definition, double probability) in belief)
        {
            context.Catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card);
            int damage = AccessDamage(card, counters);
            if (damage > 0) traps.Add((damage, probability));
        }
        return (traps, Math.Max(0, 1 - belief.Values.Sum()));
    }

    /// <summary>For Hard, which knows the list: the chance an advanced unknown root card is a trap rather than an agenda or asset.</summary>
    public static double ListTrapShare(HeuristicContext context) => context.Memo("list-trap-share", () =>
    {
        IReadOnlyDictionary<string, double> prior = Prior(context);
        bool Advanceable(string definition) => context.Catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card)
            && (card.Agenda is not null || card.Behavior?.CanBeAdvanced == true) && TrackerPrior.Fits(context.Catalog, definition, "root");
        Dictionary<string, double> advanceable = TrackerPrior.Conditional(prior, Advanceable);
        return advanceable.Where(pair => context.Catalog.TryGet(new CardDefinitionId(pair.Key), out CardEvaluationDescriptor card) && AccessDamage(card, 0) > 0)
            .Sum(pair => pair.Value);
    });

    /// <summary>A face-down card the tracker is confident of, as a card to plan with (unrezzed); null otherwise.</summary>
    public static AgentCard? Believed(HeuristicContext context, AgentServer server, string zone, int index)
    {
        if (Tracker(context) is not { } tracker) return null;
        KeyValuePair<string, double> best = tracker.SlotBelief(server.Id, zone, index).OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal).FirstOrDefault();
        return best.Key is not null && best.Value >= Confident
            ? new AgentCard
            {
                Id = $"believed/{server.Id}/{zone}/{index}",
                DefinitionId = best.Key,
                Rezzed = false,
                AdvancementCounters = context.HiddenAdvancement(server.Id, zone, index)
            }
            : null;
    }

    /// <summary>The server with its face-down ICE the tracker is confident of filled in, or the server itself.</summary>
    public static AgentServer WithBelievedIce(HeuristicContext context, AgentServer server)
    {
        if (Tracker(context) is null || server.Ice.All(card => card is not null)) return server;
        AgentCard?[] ice = [.. server.Ice.Select((card, index) => card ?? Believed(context, server, "ice", index))];
        return ice.SequenceEqual(server.Ice) ? server : server with { Ice = [.. ice] };
    }

    /// <summary>
    /// The risk of being tagged into the Corp's turn from the tag punishers the Runner knows of, in HQ or on R&amp;D's top
    /// for the Corp's draw, each weighted by how likely it's there and only if the Corp can pay for it then. A punisher is
    /// read from its declared behaviour, not its title: an operation that needs the Runner tagged and trashes a Runner
    /// card (Retribution), or an agenda whose score damages a tagged Runner (Orbital Superiority).
    /// </summary>
    public static int KnownTagThreat(HeuristicContext context)
    {
        if (Tracker(context) is not { } tracker) return 0;
        double total = 0;
        foreach (TrackedCard card in tracker.Cards.Where(card => card.Place == TrackedPlace.Hand || card.Place == TrackedPlace.DeckTop && card.Depth == 0))
            total += card.Weight * Threat(context, card.DefinitionId);
        return (int)Math.Min(3000, total);
    }

    private static double Threat(HeuristicContext context, string definitionId)
    {
        if (!context.Catalog.TryGet(new CardDefinitionId(definitionId), out CardEvaluationDescriptor card) || card.Behavior is not { } behavior) return 0;
        // The Corp's next turn: what it has now and about two credits of income.
        int funds = context.Opponent.Credits + 2;
        bool needsTag = behavior.Conditions.Any(condition => condition.Kind == BehaviorConditionKind.Tagged);
        if (card.Printed.CardClass == RuntimeCardClass.Operation && needsTag)
        {
            int cost = (card.Printed.PlayCost ?? 0) + behavior.Costs.Where(value => value.Kind == BehaviorCostKind.Credits).Sum(value => value.Amount);
            if (cost > funds) return 0;
            IEnumerable<BehaviorInstruction> flat = HeuristicContext.Flatten(behavior.Instructions);
            if (!flat.Any(instruction => instruction.Kind == BehaviorInstructionKind.TrashCard)) return 0;
            BehaviorCardFilter[] filters = [.. behavior.Targets.Select(target => target.Filter).OfType<BehaviorCardFilter>()
                .Where(filter => filter.OwnerSide == PlayerSide.Runner)];
            return context.Features.Board.RunnerRig.Where(installed => filters.Any(filter => Matches(context, installed, filter)))
                .Select(installed => RunnerValues.Card(context, installed)).DefaultIfEmpty(0).Max();
        }
        if (card.Agenda is not null)
        {
            // Damage on score if the Runner is tagged; the Corp still has to score it, about an even chance next turn.
            int damage = behavior.Triggers.SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
                .Where(instruction => instruction.Branch is { Condition.Kind: BehaviorConditionKind.Tagged })
                .SelectMany(instruction => HeuristicContext.Flatten(instruction.Branch!.WhenTrue))
                .Where(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage).Sum(instruction => instruction.Amount);
            if (damage == 0) return 0;
            return 0.5 * (damage >= context.Features.Hand.Length ? 3000 : damage * 160);
        }
        return 0;
    }

    private static bool Matches(HeuristicContext context, AgentCard installed, BehaviorCardFilter filter)
    {
        CardEvaluationDescriptor? definition = context.Definition(installed);
        if (definition is null) return false;
        RuntimeCardClass cardClass = definition.Printed.CardClass;
        return filter.CardType switch
        {
            BehaviorCardType.Any => true,
            BehaviorCardType.ProgramOrHardware => cardClass is RuntimeCardClass.Program or RuntimeCardClass.Hardware,
            BehaviorCardType.ResourceOrHardware => cardClass is RuntimeCardClass.Resource or RuntimeCardClass.Hardware,
            BehaviorCardType.Program => cardClass == RuntimeCardClass.Program,
            BehaviorCardType.Icebreaker => definition.Icebreaker is not null,
            BehaviorCardType.Hardware => cardClass == RuntimeCardClass.Hardware,
            BehaviorCardType.Resource => cardClass == RuntimeCardClass.Resource,
            _ => false,
        };
    }
}
