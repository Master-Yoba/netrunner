using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class CorpHeuristicPolicy
{
    private static AgentUtility Install(HeuristicContext context, InstallCardParameters install, bool repairs = false)
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
        // v6: a remote's root holds one agenda or asset, so installing another
        // there trashes it; an agenda thrown away costs far more than its card value.
        if (repairs) lost = Math.Max(lost, DisplacedRootValue(context, server, definition));
        int score;
        if (definition.Ice is { } ice)
        {
            bool hasScoringRemote = context.Features.Board.Servers.Any(value => value.Kind == ServerKind.Remote && value.Ice.Length > 0);
            score = install.CreateNewRemote ? context.AgendaCount > 0 && !hasScoringRemote ? 350 : 20
                : context.ServerValue(server) / ((server?.Ice.Length ?? 0) + 1);
            if (context.Generation >= 2)
            {
                // A2: ICE the Runner can't break yet taxes every future run,
                // and central ICE matters more as the Runner nears 7 points.
                score += RunnerCovers(context, ice.IceType) ? 0 : 100 * context.Parameter("coverage-scale", 100) / 100;
                if (server?.Kind is ServerKind.Headquarters or ServerKind.RAndD) score = (int)(score * context.OpponentScorePressure);
            }
            score -= install.CreditCost * context.CreditValue;
            if (context.Self.Credits - install.CreditCost < ice.RezCost) score -= 200;
        }
        else if (definition.Agenda is not null)
        {
            int protection = (server?.Ice.Length ?? 0) * 110 + (server?.Root.Where(value => context.Definition(value)?.Printed.CardClass == RuntimeCardClass.Upgrade).Count() ?? 0) * 170;
            if (context.Generation >= 2 && server is not null)
            {
                // A2: ICE the Runner's breakers already cover protects less.
                int coverage = context.Parameter("coverage-scale", 100);
                protection += server.Ice.Sum(value => context.Definition(value)?.Ice is { } protecting
                    ? (RunnerCovers(context, protecting.IceType) ? -50 : 50) * coverage / 100 : 0);
            }
            score = 260 + protection - lost;
            if (context.AgendaCount > 1) score += 40;
            if (context.Self.Credits < definition.Printed.AdvancementRequirement) score -= 120;
            if (context.Generation >= 2)
            {
                // A2: an agenda the Corp can install and fully advance this
                // turn never faces a run; an unprotected one it can't finish
                // is an easy steal, since advancement counters are public.
                int weight = context.Parameter("coverage-scale", 100);
                int requirement = definition.Printed.AdvancementRequirement;
                bool scoreableNow = context.Self.Clicks - 1 >= requirement
                    && context.Self.Credits - install.CreditCost >= requirement;
                bool unprotected = install.CreateNewRemote || (server?.Ice.Length ?? 0) == 0;
                if (scoreableNow) score += 150 * weight / 100;
                else if (unprotected) score -= 350 * weight / 100;
            }
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

    /// <summary>True when an installed Runner breaker can interface with ICE of this type.</summary>
    private static bool RunnerCovers(HeuristicContext context, IceType iceType) =>
        context.Features.Board.RunnerRig.Any(card => context.Definition(card)?.Icebreaker is { } breaker
            && (breaker.InterfaceType == IceType.Any || breaker.InterfaceType == iceType));

    private static AgentUtility Score(HeuristicContext context, ScoreAgendaParameters score) =>
        new(context.Self.AgendaPoints + score.AgendaPoints >= 7 ? 100 : 30, score.AgendaPoints * 100, "score/available-points");

    /// <summary>What installing an agenda or asset into a remote root would trash: its agenda or asset, advancement included.</summary>
    private static int DisplacedRootValue(HeuristicContext context, AgentServer? server, CardEvaluationDescriptor installing)
    {
        if (server is not { Kind: ServerKind.Remote }
            || installing.Printed.CardClass is not (RuntimeCardClass.Agenda or RuntimeCardClass.Asset)) return 0;
        return server.Root.Sum(card =>
        {
            CardEvaluationDescriptor? existing = context.Definition(card);
            if (card is null || existing?.Printed.CardClass is not (RuntimeCardClass.Agenda or RuntimeCardClass.Asset)) return 0;
            int advanced = card.AdvancementCounters ?? 0;
            return existing.Agenda is not null
                ? 1000 + existing.Printed.AgendaPoints * 300 + advanced * 150
                : context.CardValue(card, installed: true) + advanced * 100;
        });
    }

    /// <summary>
    /// v6: the credits needed to rez the ICE that protects what the Corp is
    /// building: the dearest unrezzed ICE on a server holding an agenda, or the
    /// cheapest on a central server when no agenda is installed.
    /// </summary>
    private static int ProtectionReserve(HeuristicContext context, AgentServer? agendaServer = null)
    {
        IEnumerable<AgentServer> agendaServers = agendaServer is not null ? [agendaServer]
            : context.Features.Board.Servers.Where(server => server.Root.Any(card => context.Definition(card)?.Agenda is not null));
        int protecting = agendaServers.SelectMany(server => server.Ice).Where(card => card is { Rezzed: false })
            .Select(card => context.Definition(card)?.Printed.RezCost ?? 0).DefaultIfEmpty(0).Max();
        return Math.Max(protecting, context.RezReserve);
    }

    private static AgentUtility Advance(HeuristicContext context, AdvanceCardParameters advance, bool repairs = false)
    {
        AgentCard? card = context.Card(advance.CardId);
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition?.Agenda is null) return Value(-20, "advance/nonagenda-opportunity", approximate: true);
        int remaining = definition.Printed.AdvancementRequirement - (card!.AdvancementCounters ?? 0);
        bool finish = remaining > 0 && remaining <= Math.Min(context.Self.Clicks, context.Self.Credits);
        int ice = context.ServerOf(card.Id)?.Ice.Length ?? 0;
        int score = finish ? 600 + definition.Printed.AgendaPoints * 50 : 30 + ice * 35;
        // v4: an agenda too large to finish in one turn is advanced now so that
        // a full next turn finishes it, but only behind ICE.
        if (!finish && context.Generation >= 3 && ice > 0 && remaining - 1 <= 3)
            score = 180 + definition.Printed.AgendaPoints * 40;
        if (!finish && context.Self.Credits <= context.RezReserve) score -= 200;
        // v6: advancing spends a credit; don't spend the one that rezzes this agenda's ICE.
        if (repairs && !finish && context.Self.Credits - 1 < ProtectionReserve(context, context.ServerOf(card.Id))) score -= 250;
        return new(finish ? 10 : 0, score, finish ? "score/finish-this-turn" : "score/future-window",
            Approximate: !finish, Objective: new("score", null, null, card.Id));
    }

    private static AgentUtility Rez(HeuristicContext context, CardInstanceId id, int cost, bool repairs = false)
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
        // v6: an upgrade in the server being run on works now or never, so it
        // isn't held back for ICE the Runner has already passed or chosen not to face.
        bool attacked = context.Features.Board.Run?.AttackedServer is { } attackedServer && attackedServer == server?.Id;
        if (repairs && attacked && definition?.Printed.CardClass == RuntimeCardClass.Upgrade && cost <= context.Self.Credits)
            return Value(Math.Max(120, context.ServerValue(server) / 2) - cost * 30, "rez/upgrade-in-attacked-server", approximate: true);
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
