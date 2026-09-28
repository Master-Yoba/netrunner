using Netrunner.Core;

namespace Netrunner.AI;

internal static class RunnerValues
{
    public static int Card(HeuristicContext context, AgentCard? card)
    {
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition is null) return 80;
        int value = context.CardValue(card, installed: context.Features.Board.RunnerRig.Any(value => value.Id == card!.Id));
        value += card!.Counters.Where(counter => counter.Purpose == CardCounterPurpose.Virus).Sum(counter => counter.Count * 65);
        return value;
    }

    public static int TagRisk(HeuristicContext context, int additional = 1) =>
        180 * additional + (context.Features.Board.RunnerRig.Where(card => context.Definition(card)?.Printed.CardClass == RuntimeCardClass.Resource)
            .Select(card => Card(context, card)).DefaultIfEmpty(0).Max() * (context.Self.Clicks < (context.Self.Tags ?? 0) + additional ? 1 : 0))
            + (context.Self.Clicks == 0 ? 120 : 0);

    public static AgentServer? Attacked(HeuristicContext context) => context.Features.Board.Servers.FirstOrDefault(server => server.Id == context.Features.Board.Run?.AttackedServer);
    public static bool Winning(HeuristicContext context, AgentServer? server) => server is not null
        && server.Root.All(card => context.Definition(card) is not null)
        && server.Root.Any(card => context.Definition(card)?.Agenda is { } agenda
            && context.Self.AgendaPoints + agenda.AgendaPoints >= 7);

    public static int Server(HeuristicContext context, AgentServer? server)
    {
        if (server is null) return 0;
        if (context.Generation >= 2) return ServerV2(context, server);
        int root = server.Root.Sum(card => card is null ? 190 : context.Definition(card)?.Agenda is { } agenda
            ? 500 + agenda.AgendaPoints * 180 : Trash(context, card));
        return root + (server.Kind switch
        {
            ServerKind.Headquarters => context.Features.Board.HiddenZoneCounts.CorpHand > 0 ? 320 : 0,
            ServerKind.RAndD => context.Features.Board.HiddenZoneCounts.CorpDeck > 0 ? 340 : 0,
            ServerKind.Archives => context.Features.Board.CorpDiscard.Sum(card => context.Definition(card)?.Agenda is { } agenda ? agenda.AgendaPoints * 220 : 0)
                + Math.Max(0, (context.Features.Board.ZoneCounts?.CorpDiscard ?? 0) - context.Features.Board.CorpDiscard.Length) * 70,
            _ => 0,
        });
    }

    /// <summary>
    /// A2 server value. A face-down root card gains value from its public
    /// advancement counters (an advanced card is usually an agenda). HQ and
    /// R&D scale with the estimated agenda density of unseen Corp cards. Both
    /// cues are weighted by the profile's inference scale, and everything
    /// rises when the Corp is close to winning.
    /// </summary>
    private static int ServerV2(HeuristicContext context, AgentServer server)
    {
        double inference = context.Parameter("inference-scale", 100) / 100.0;
        int root = 0;
        for (int index = 0; index < server.Root.Length; index++)
        {
            AgentCard? card = server.Root[index];
            int advancement = context.HiddenAdvancement(server.Id, "root", index);
            root += card is null
                ? 190 + (int)(inference * advancement * 200)
                : context.Definition(card)?.Agenda is { } agenda ? 500 + agenda.AgendaPoints * 180 : Trash(context, card);
        }

        double density = 1 + inference * (context.AgendaDensityFactor() - 1);
        int central = server.Kind switch
        {
            ServerKind.Headquarters => context.Features.Board.HiddenZoneCounts.CorpHand > 0 ? (int)(320 * density) : 0,
            ServerKind.RAndD => context.Features.Board.HiddenZoneCounts.CorpDeck > 0 ? (int)(340 * density) : 0,
            ServerKind.Archives => context.Features.Board.CorpDiscard.Sum(card => context.Definition(card)?.Agenda is { } agenda ? agenda.AgendaPoints * 220 : 0)
                + Math.Max(0, (context.Features.Board.ZoneCounts?.CorpDiscard ?? 0) - context.Features.Board.CorpDiscard.Length) * 70,
            _ => 0,
        };
        if (context.Generation >= 7) central = (int)(central * CentralFreshness(context, server.Kind));
        bool contested = server.Kind is ServerKind.Headquarters or ServerKind.RAndD
            || Enumerable.Range(0, server.Root.Length).Any(index => context.HiddenAdvancement(server.Id, "root", index) > 0);
        double pressure = contested ? context.OpponentScorePressure : 1.0;
        return (int)((root + central) * pressure);
    }

    /// <summary>
    /// Runner v10 (A4a): how much of R&amp;D or HQ is new since the last access.
    /// R&amp;D's top card is known while the Corp hasn't drawn since; HQ is new in
    /// proportion to the cards the Corp has drawn since, against its hand now.
    /// </summary>
    internal static double CentralFreshness(HeuristicContext context, ServerKind kind)
    {
        const double Seen = 0.3;
        AgentCentralMemory? memory = context.Knowledge?.Centrals;
        int deck = context.Features.Board.HiddenZoneCounts.CorpDeck;
        int hand = context.Features.Board.HiddenZoneCounts.CorpHand;
        return kind switch
        {
            ServerKind.RAndD when memory?.DeckAfterRdAccess == deck => Seen,
            ServerKind.Headquarters when memory is { HandAfterHqAccess: not null, DeckAfterHqAccess: { } before } =>
                Math.Clamp((double)Math.Max(0, before - deck) / Math.Max(1, hand), Seen, 1),
            _ => 1,
        };
    }

    public static int Trash(HeuristicContext context, AgentCard? card)
    {
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition is null) return 70;
        int income = HeuristicContext.Hosted(card);
        if (income > 0) return 160 + income * 90;
        // v6: an advanced trap would deal its counters as damage (Clearinghouse).
        if (context.Generation >= 5 && definition.Behavior is { CanBeAdvanced: true } && (card?.AdvancementCounters ?? 0) > 0)
            return 180 + (card!.AdvancementCounters ?? 0) * 200;
        if (definition.Printed.CardClass == RuntimeCardClass.Upgrade)
        {
            bool protection = definition.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.ServerApproached) == true;
            return protection ? 430 : 110;
        }
        return definition.Printed.CardClass == RuntimeCardClass.Asset ? 180 : 50;
    }

    public static int BreakCost(HeuristicContext context, AgentCard card, IcebreakerBehaviorSpec breaker, bool useIssuedQuote = true)
    {
        RunBreakParameters? quote = useIssuedQuote
            ? context.Features.Actions.Select(action => action.Parameters).OfType<RunBreakParameters>()
                .FirstOrDefault(parameters => parameters.BreakerId.Value == card.Id)
            : null;
        if (quote?.CostQuote is { } cost) return cost.EffectiveCost;
        int delta = context.Features.Board.TurnFacts?.Runner.ThisTurn?.SuccessfulRun == true
            ? context.Definition(card)?.Behavior?.CostModifiers?.Where(modifier => modifier.Kind == BehaviorCostModifierKind.IcebreakerBreakCreditCost)
                .Sum(modifier => modifier.Delta) ?? 0 : 0;
        return Math.Max(0, breaker.BreakCost + delta);
    }

    public static int PumpStrength(HeuristicContext context, IcebreakerBehaviorSpec breaker) => breaker.DynamicPump is { } dynamic
        ? dynamic.UnitDelta * context.Features.Board.RunnerRig.Count(card => context.Definition(card)?.Icebreaker is not null) : breaker.PumpStrength;
}
