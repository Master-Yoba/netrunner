using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// A9c: copy counting for both bots. The opponent's unseen copies are its deck's (the decklist Hard knows, or the card
/// pool at three copies) less those in public view now, so a card shuffled back (Spin Doctor) counts as available again.
/// With the list the counts are exact; with the pool they only bound what is possible.
/// </summary>
internal static class OpponentCounting
{
    public static PlayerSide OpponentSide(HeuristicContext context) =>
        context.Features.Side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;

    public static bool KnowsList(HeuristicContext context) => context.Knowledge?.OpponentList is not null;

    /// <summary>The opponent's unseen copies by definition.</summary>
    public static IReadOnlyDictionary<string, double> Unseen(HeuristicContext context) => context.Memo("opponent-unseen", () =>
        TrackerPrior.Remaining(context.Knowledge?.OpponentList is { } list
            ? TrackerPrior.List(list.Select(entry => (entry.DefinitionId, entry.Quantity)))
            : TrackerPrior.Pool(context.Catalog, OpponentSide(context)), context.Features));

    /// <summary>How many unseen copies pass a test.</summary>
    public static double Count(HeuristicContext context, Func<CardEvaluationDescriptor, bool> test) =>
        Unseen(context).Sum(pair => context.Catalog.TryGet(new CardDefinitionId(pair.Key), out CardEvaluationDescriptor card) && test(card) ? pair.Value : 0);

    /// <summary>For a bot that knows the Runner's list: whether no breaker that interfaces with this ICE type is left unseen.</summary>
    public static bool NoBreakerLeftFor(HeuristicContext context, IceType iceType) => KnowsList(context)
        && Count(context, card => card.Icebreaker is { } breaker && (breaker.InterfaceType == IceType.Any || breaker.InterfaceType == iceType)) == 0;

    /// <summary>
    /// For a bot that knows the Runner's list: the most a burst economy event left unseen nets in a click (Sure Gamble's 4),
    /// or 0 when they're all spent; for the pool, 0 (it can't tell).
    /// </summary>
    public static int BurstLeft(HeuristicContext context)
    {
        if (!KnowsList(context)) return 0;
        return Unseen(context).Where(pair => pair.Value > 0)
            .Select(pair => context.Catalog.TryGet(new CardDefinitionId(pair.Key), out CardEvaluationDescriptor card)
                && card.Printed.CardClass == RuntimeCardClass.Event && card.Behavior is { } behavior
                ? HeuristicContext.Flatten(behavior.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.GainCredits)
                    .Sum(instruction => instruction.Amount) - (card.Printed.PlayCost ?? 0)
                : 0)
            .DefaultIfEmpty(0).Max() is var best && best >= 2 ? best : 0;
    }

    /// <summary>For a Runner that knows the Corp's list: unseen agendas per hidden Corp card, against the list's own share; null without the list.</summary>
    public static double? ListAgendaDensity(HeuristicContext context)
    {
        if (context.Knowledge?.OpponentList is not { } list) return null;
        AgentBoard board = context.Features.Board;
        int hidden = board.HiddenZoneCounts.CorpHand + board.HiddenZoneCounts.CorpDeck
            + board.Servers.Sum(server => server.Root.Count(card => card is null || context.Definition(card) is null));
        double size = list.Sum(entry => entry.Quantity);
        double agendas = list.Where(entry => context.Catalog.TryGet(new CardDefinitionId(entry.DefinitionId), out CardEvaluationDescriptor card) && card.Agenda is not null)
            .Sum(entry => entry.Quantity);
        if (hidden <= 0 || size <= 0 || agendas <= 0) return 0;
        double unseen = Count(context, card => card.Agenda is not null);
        return Math.Clamp(unseen / hidden / (agendas / size), 0, 2.5);
    }
}
