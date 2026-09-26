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

    public static int Trash(HeuristicContext context, AgentCard? card)
    {
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition is null) return 70;
        int income = HeuristicContext.Hosted(card);
        if (income > 0) return 160 + income * 90;
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
