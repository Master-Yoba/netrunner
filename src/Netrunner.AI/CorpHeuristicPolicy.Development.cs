using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class CorpHeuristicPolicy
{
    private static AgentUtility Install(HeuristicContext context, InstallCardParameters install)
    {
        AgentCard? card = context.Card(install.CardId);
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition is null) return Value(-100, "install/unknown-metadata", approximate: true, fallback: true);
        if (context.Features.Decision?.Kind == DecisionKind.InstallChoice)
        {
            int required = install.SelectedReplacementCardIds.Count(install.RequiredReplacementCardIds.Contains);
            int optionalLoss = install.SelectedReplacementCardIds.Where(id => !install.RequiredReplacementCardIds.Contains(id)).Sum(id => context.CardValue(context.Card(id), installed: true));
            return new(20, (install.FinalizeSelection ? 1000 : required * 100) - optionalLoss,
                "install/complete-required-replacement", Objective: new("install", null, null, install.CardId.Value));
        }
        AgentServer? server = context.Server(install.ServerId);
        int lost = install.RequiredReplacementCardIds.Sum(id => context.CardValue(context.Card(id), installed: true));
        int score;
        if (definition.Ice is { } ice)
        {
            bool hasScoringRemote = context.Features.Board.Servers.Any(value => value.Kind == ServerKind.Remote && value.Ice.Length > 0);
            score = install.CreateNewRemote ? context.AgendaCount > 0 && !hasScoringRemote ? 350 : 20
                : context.ServerValue(server) / ((server?.Ice.Length ?? 0) + 1);
            score -= install.CreditCost * context.CreditValue;
            if (context.Self.Credits - install.CreditCost < ice.RezCost) score -= 200;
        }
        else if (definition.Agenda is not null)
        {
            int protection = (server?.Ice.Length ?? 0) * 110 + (server?.Root.Where(value => context.Definition(value)?.Printed.CardClass == RuntimeCardClass.Upgrade).Count() ?? 0) * 170;
            score = 260 + protection - lost;
            if (context.AgendaCount > 1) score += 40;
            if (context.Self.Credits < definition.Printed.AdvancementRequirement) score -= 120;
        }
        else if (definition.Printed.CardClass == RuntimeCardClass.Asset)
        {
            int existingEconomy = context.Features.Board.Servers.SelectMany(value => value.Root).Count(value => HeuristicContext.Hosted(value) > 0);
            score = 180 - existingEconomy * 60 - lost;
            if (server?.Root.Any(value => context.Definition(value)?.Printed.CardClass == RuntimeCardClass.Upgrade) == true) score -= 80;
            if (context.Self.Credits < (definition.Printed.RezCost ?? 0)) score -= 150;
        }
        else
            score = 70 + (server?.Root.Length ?? 0) * 60 + (server?.Ice.Length ?? 0) * 40 - lost
                + (server?.Root.Count(value => value is { Rezzed: true } && context.Definition(value)?.Printed.CardClass == RuntimeCardClass.Upgrade) ?? 0) * 60;
        return Value(score, "development/destination-minus-replacement-value", approximate: true, objective: new("install", null, null, install.CardId.Value));
    }

    private static AgentUtility Score(HeuristicContext context, ScoreAgendaParameters score) =>
        new(context.Self.AgendaPoints + score.AgendaPoints >= 7 ? 100 : 30, score.AgendaPoints * 100, "score/available-points");

    private static AgentUtility Advance(HeuristicContext context, AdvanceCardParameters advance)
    {
        AgentCard? card = context.Card(advance.CardId);
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition?.Agenda is null) return Value(-20, "advance/nonagenda-opportunity", approximate: true);
        int remaining = definition.Printed.AdvancementRequirement - (card!.AdvancementCounters ?? 0);
        bool finish = remaining > 0 && remaining <= Math.Min(context.Self.Clicks, context.Self.Credits);
        int score = finish ? 600 + definition.Printed.AgendaPoints * 50 : 30 + (context.ServerOf(card.Id)?.Ice.Length ?? 0) * 35;
        if (!finish && context.Self.Credits <= context.RezReserve) score -= 200;
        return new(finish ? 10 : 0, score, finish ? "score/finish-this-turn" : "score/future-window",
            Approximate: !finish, Objective: new("score", null, null, card.Id));
    }

    private static AgentUtility Rez(HeuristicContext context, CardInstanceId id, int cost)
    {
        CardEvaluationDescriptor? definition = context.Definition(id);
        AgentCard? card = context.Card(id);
        AgentServer? server = context.ServerOf(id.Value);
        if (definition?.Ice is { } ice)
        {
            if (context.Features.Board.Run?.CurrentIce?.Id != id.Value) return Value(-10, "rez/wait-for-approach");
            int force = ice.Subroutines.Any(subroutine => subroutine is EndRunSubroutineSpec) ? 240 : 90;
            return Value(force + context.ServerValue(server) - cost * 30, "rez/relevant-protection", approximate: true);
        }
        if (context.Self.Credits - cost < context.RezReserve) return Value(-300, "rez/preserve-ice-budget");
        if (definition?.Printed.CardClass == RuntimeCardClass.Asset)
        {
            int futureIncome = definition.Behavior?.Triggers.SelectMany(trigger => trigger.Instructions)
                .Where(instruction => instruction.Kind == BehaviorInstructionKind.LoadHostedCredits).Sum(instruction => instruction.Amount) ?? 0;
            int immediateDraw = definition.Behavior?.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.CardRezzed)
                .SelectMany(trigger => trigger.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.Draw).Sum(instruction => instruction.Amount) ?? 0;
            if (futureIncome == 0 && immediateDraw == 0) return Value(-10, "rez/no-current-payoff", approximate: true);
            if (immediateDraw >= context.Features.Board.HiddenZoneCounts.CorpDeck) return Value(-10000, "rez/avoid-deck-out");
            return Value(futureIncome * 45 + immediateDraw * context.DrawValue - cost * context.CreditValue, "rez/economy-payback", approximate: true);
        }
        bool relevant = context.Features.Board.Run?.AttackedServer == server?.Id;
        return Value(relevant ? context.ServerValue(server) / 2 - cost * 60 : -10, "rez/upgrade-relevance", approximate: true);
    }

    private static AgentUtility Purge(HeuristicContext context, PurgeVirusCountersParameters purge)
    {
        int pressure = context.Features.Board.RunnerRig.Sum(card => card.Counters.Where(counter => counter.Purpose == CardCounterPurpose.Virus)
            .Sum(counter => counter.Count * (card.HostCardId is not null ? 95 : 45)));
        return Value(pressure - purge.ClickCost * 100, "pressure/purge-virus-impact", approximate: true);
    }
}
