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
            if (context.Generation >= 6) score = IcePlacementV11(context, server, install.CreateNewRemote, score);
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
            if (context.Generation >= 5)
                protection = ModelledProtection(context, server, install.CreditCost + definition.Printed.AdvancementRequirement);
            else if (context.Generation >= 2 && server is not null)
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
                if (context.Generation >= 7)
                {
                    // v12: when an agenda may go in depends on the deck's plan.
                    CorpArchetypeProfile profile = context.CorpProfile;
                    int protecting = install.CreateNewRemote ? 0 : server?.Ice.Length ?? 0;
                    if (!scoreableNow)
                        score -= (int)(Math.Max(0, profile.MinIceForAgenda - protecting) * 180 + profile.HoldAgendas * 300);
                    if (protecting >= 1 && context.Features.Board.Turn.TurnNumber <= 8) score += (int)(profile.RushBonus * 120);
                }
            }
        }
        else if (definition.Printed.CardClass == RuntimeCardClass.Asset)
        {
            int existingEconomy = context.Features.Board.Servers.SelectMany(value => value.Root).Count(value => HeuristicContext.Hosted(value) > 0);
            score = 180 - existingEconomy * 60 - lost;
            if (server?.Root.Any(value => context.Definition(value)?.Printed.CardClass == RuntimeCardClass.Upgrade) == true) score -= 80;
            if (context.Self.Credits < (definition.Printed.RezCost ?? 0)) score -= 150;
            if (context.Generation >= 7)
                score += (int)(context.CorpProfile.AssetBonus * 120 + (ScalesWithCounters(definition) ? (context.CorpProfile.TrapWeight - 1) * 150 : 0));
        }
        else
        {
            score = 70 + (server?.Root.Length ?? 0) * 60 + (server?.Ice.Length ?? 0) * 40 - lost
                + (server?.Root.Count(value => value is { Rezzed: true } && context.Definition(value)?.Printed.CardClass == RuntimeCardClass.Upgrade) ?? 0) * 60;
            if (context.Generation >= 6 && definition.Printed.CardClass == RuntimeCardClass.Upgrade)
                score = UpgradePlacementV11(context, definition, server, install, score);
        }
        return Value(score, "development/destination-minus-replacement-value", approximate: true, objective: new("install", null, null, install.CardId.Value));
    }

    /// <summary>
    /// v8: how well a server protects an agenda, from the whole-run model (A4a) run
    /// from the Runner's side: its credits, a full next turn's clicks and its grip
    /// are public, and the Corp's own ICE counts as rezzed while the Corp can pay
    /// for it after <paramref name="committed"/> credits. A server the Runner can't
    /// get into is worth most; otherwise what getting in costs the Runner.
    /// </summary>
    private static int ModelledProtection(HeuristicContext context, AgentServer? server, int committed)
    {
        if (server is null) return 0;
        RunForecast forecast = RunModel.Plan(context, server, server.Ice.Length - 1, context.Opponent.Credits + 2, clicks: 3,
            grip: context.Features.Board.HiddenZoneCounts.RunnerGrip, corpRezBudget: Math.Max(0, context.Self.Credits - committed));
        if (!forecast.CanGetIn) return 650;
        int cost = forecast.Credits + forecast.CreditsLost + forecast.Clicks * 2 + forecast.Tags * 2 + forecast.Damage * 2;
        return Math.Min(550, cost * 60) + forecast.UnknownIce * 80;
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
        if (definition?.Agenda is null && context.Generation >= 4 && ScalesWithCounters(definition))
        {
            // v7: an advanceable trap (Urtica Cipher, Clearinghouse) grows with every
            // counter, and an advanced card reads as an agenda to the Runner (Wave 4 C1).
            if (context.Self.Credits - 1 < context.RezReserve) return Value(-40, "advance/trap-keeps-ice-budget", approximate: true);
            int counters = card!.AdvancementCounters ?? 0;
            int bait = counters >= 4 ? 40 : 130 - counters * 15;
            if (context.Generation >= 7) bait = (int)(bait * context.CorpProfile.TrapWeight);
            return Value(bait, "advance/trap", approximate: true);
        }
        if (context.Generation >= 6 && definition?.Ice is not null && definition.Behavior?.CanBeAdvanced == true)
            return AdvanceIceV11(context, card!, definition);
        if (definition?.Agenda is null) return Value(-20, "advance/nonagenda-opportunity", approximate: true);
        int remaining = definition.Printed.AdvancementRequirement - (card!.AdvancementCounters ?? 0);
        bool finish = remaining > 0 && remaining <= Math.Min(context.Self.Clicks, context.Self.Credits);
        int ice = context.ServerOf(card.Id)?.Ice.Length ?? 0;
        int score = finish ? 600 + definition.Printed.AgendaPoints * 50 : 30 + ice * 35;
        // v4: an agenda too large to finish in one turn is advanced now so that
        // a full next turn finishes it, but only behind ICE. v6: a longer agenda
        // (five advancements) is also worked on across turns, but only behind two
        // or more ICE the Corp can afford to rez after finishing it; advancing it
        // in the open shows the Runner where to run (an eager version lost on the ladder).
        AgentServer? home = context.ServerOf(card.Id);
        int unrezzedCost = home?.Ice.Where(value => value is { Rezzed: false })
            .Sum(value => context.Definition(value)?.Printed.RezCost ?? 0) ?? 0;
        bool longAgenda = repairs && remaining - 1 > 3 && remaining - context.Self.Clicks <= 3 && ice >= 2
            && context.Self.Credits - remaining >= unrezzedCost;
        if (!finish && context.Generation >= 3 && ice > 0 && (remaining - 1 <= 3 || longAgenda))
            score = 180 + definition.Printed.AgendaPoints * 40;
        if (!finish && context.Self.Credits <= context.RezReserve) score -= 200;
        // v6: advancing spends a credit; don't spend the one that rezzes this agenda's ICE.
        if (repairs && !finish && context.Self.Credits - 1 < ProtectionReserve(context, context.ServerOf(card.Id))) score -= 250;
        // v12: a shell game leaves agendas unadvanced until the turn they score, so they look like its traps.
        if (context.Generation >= 7 && !finish) score -= (int)(context.CorpProfile.NeverAdvance * 120);
        // v11: an identity that pays for a card's first advancement (Built to Last) makes starting one cheaper.
        if (context.Generation >= 6 && (card.AdvancementCounters ?? 0) == 0) score += FirstAdvanceGain(context) * context.CreditValue;
        return new(finish ? 10 : 0, score, finish ? "score/finish-this-turn" : "score/future-window",
            Approximate: !finish, Objective: new("score", null, null, card.Id));
    }

    /// <summary>
    /// v11 (playtest, 28 September): ICE goes where it protects something. A new
    /// remote is worth ICE only while the Corp holds an agenda or asset to put
    /// there and no ICE'd remote stands empty for it; an empty remote gains little
    /// from more ICE; and HQ and R&amp;D are worth protecting until each has two
    /// pieces. Decks with many assets still go wide, since each asset wants a home.
    /// </summary>
    private static int IcePlacementV11(HeuristicContext context, AgentServer? server, bool newRemote, int score)
    {
        bool rootCandidate = context.Features.Hand.Any(card => context.Definition(card)?.Printed.CardClass
            is RuntimeCardClass.Agenda or RuntimeCardClass.Asset);
        bool openSlot = context.Features.Board.Servers.Any(value => value.Kind == ServerKind.Remote && value.Ice.Length > 0 && value.Root.Length == 0);
        CorpArchetypeProfile profile = context.CorpProfile;
        bool archetypes = context.Generation >= 7;
        int occupied = context.Features.Board.Servers.Count(value => value.Kind == ServerKind.Remote && value.Root.Length > 0);
        if (newRemote) return rootCandidate && !openSlot && (!archetypes || occupied < profile.MaxRemotes) ? score : -150;
        if (server is null) return score;
        int ice = server.Ice.Length;
        // v12: a glacier stacks its scoring remote before the agenda goes in.
        bool agendaHere = server.Kind == ServerKind.Remote && server.Root.Any(card => context.Definition(card)?.Agenda is not null);
        double stack = archetypes && server.Kind == ServerKind.Remote && ice < profile.ScoringIceTarget
            && (agendaHere || server.Root.Length == 0 && context.AgendaCount > 0) ? (profile.ScoringIceTarget - ice) * 140 : 0;
        if (server.Kind == ServerKind.Remote && server.Root.Length == 0) return (rootCandidate ? score - 60 : -100) + (int)stack;
        score += (int)stack;
        if (server.Kind is ServerKind.Headquarters or ServerKind.RAndD)
            score += (int)((archetypes
                ? Math.Max(0, profile.CentralIceTarget - ice) * 130 + (ice == 0 ? 130 : 0)
                : ice == 0 ? 260 : ice == 1 ? 130 : ice == 2 ? 30 : 0) * context.OpponentScorePressure);
        if (server.Kind == ServerKind.Archives && ice == 0) score += Math.Min(200, SuccessfulRunsOn(context, server) * 70);
        return score;
    }

    /// <summary>
    /// v11: an upgrade goes where it works. One that pays off when an agenda is
    /// scored from its own root (Malapert Data Vault) belongs in a remote that
    /// holds or will hold an agenda, and a unique upgrade never replaces its own copy.
    /// </summary>
    private static int UpgradePlacementV11(HeuristicContext context, CardEvaluationDescriptor definition, AgentServer? server,
        InstallCardParameters install, int score)
    {
        bool copyInstalled = definition.Printed.Unique && context.Features.Board.Servers.SelectMany(value => value.Root)
            .Any(card => card is not null && card.DefinitionId == definition.Printed.DefinitionId.Value);
        if (copyInstalled) return -1000;
        bool scoringUpgrade = definition.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.AgendaScoredOrStolen
            && trigger.RequireSourceOnAttackedServer) == true;
        if (!scoringUpgrade) return score;
        if (install.CreateNewRemote || server?.Kind is not ServerKind.Remote) return -400;
        bool agendaHere = server.Root.Any(card => card is null || context.Definition(card)?.Agenda is not null);
        return agendaHere || context.AgendaCount > 0 ? score + 120 : score - 120;
    }

    /// <summary>
    /// v11: advanceable ICE (Pharos) grows once its counters reach the threshold,
    /// and stays grown, so it is advanced toward it on a server worth protecting.
    /// </summary>
    private static AgentUtility AdvanceIceV11(HeuristicContext context, AgentCard card, CardEvaluationDescriptor definition)
    {
        int counters = card.AdvancementCounters ?? 0;
        int threshold = definition.Ice!.ValueModifiers?.Where(modifier => modifier.Source == BehaviorValueSourceKind.CounterThreshold
            && modifier.CounterName == "advancement").Select(modifier => modifier.Threshold ?? 0).DefaultIfEmpty(0).Max() ?? 0;
        if (threshold <= 0 || counters >= threshold) return Value(-30, "advance/ice-already-grown", approximate: true);
        if (context.Self.Credits - 1 < context.RezReserve) return Value(-40, "advance/ice-keeps-rez-budget", approximate: true);
        AgentServer? server = context.ServerOf(card.Id);
        bool important = server?.Kind is ServerKind.Headquarters or ServerKind.RAndD || server?.Root.Length > 0;
        int score = 40 + (important ? 70 : 0) + (card.Rezzed == true ? 30 : 0) + counters * 25
            + (counters == 0 ? FirstAdvanceGain(context) * context.CreditValue : 0);
        return Value(score, "advance/grow-ice", approximate: true);
    }

    /// <summary>Credits the Corp's identity pays for a card's first advancement counter (Built to Last).</summary>
    private static int FirstAdvanceGain(HeuristicContext context) => context.Features.Board.CorpIdentity
        .SelectMany(card => context.Definition(card)?.Behavior?.Triggers ?? [])
        .Where(trigger => trigger.Occurrence == OccurrenceKind.CardAdvancedFromZero)
        .SelectMany(trigger => trigger.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.GainCredits)
        .Sum(instruction => instruction.Amount);

    /// <summary>
    /// v11: what rezzing ICE on approach denies beyond this server's contents: the
    /// Runner's visible rewards for a successful run, and every later run it
    /// taxes, judged by how often the Runner has run this server successfully.
    /// </summary>
    private static int RunDenialValue(HeuristicContext context, AgentServer? server)
    {
        int rewards = context.Features.Board.RunnerRig.Concat(context.Features.Board.RunnerIdentity)
            .SelectMany(card => context.Definition(card)?.Behavior?.Triggers ?? [])
            .Where(trigger => trigger.Occurrence == OccurrenceKind.RunSuccessful
                && (trigger.ServerKindRequirement is null || trigger.ServerKindRequirement == server?.Kind))
            .SelectMany(trigger => trigger.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.GainCredits)
            .Sum(instruction => instruction.Amount) * 70;
        return 60 + rewards + Math.Min(240, SuccessfulRunsOn(context, server) * 80);
    }

    /// <summary>Successful runs the Corp has seen on a server, from its own remembered history.</summary>
    private static int SuccessfulRunsOn(HeuristicContext context, AgentServer? server) => server is null ? 0
        : context.Knowledge?.History.Count(entry => entry.Kind == "successful-run" && entry.ServerId == server.Id) ?? 0;

    /// <summary>A card whose damage grows with its advancement counters (an advanceable trap).</summary>
    private static bool ScalesWithCounters(CardEvaluationDescriptor? definition) =>
        definition?.Behavior is { CanBeAdvanced: true } behavior
        && behavior.Triggers.SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
            .Any(instruction => instruction.DamageAmountOperand?.Kind == BehaviorDamageAmountKind.SourceAdvancementCounters);

    private static AgentUtility Rez(HeuristicContext context, CardInstanceId id, int cost, bool repairs = false)
    {
        CardEvaluationDescriptor? definition = context.Definition(id);
        AgentCard? card = context.Card(id);
        AgentServer? server = context.ServerOf(id.Value);
        if (definition?.Ice is { } ice)
        {
            if (context.Features.Board.Run?.CurrentIce?.Id != id.Value) return Value(-10, "rez/wait-for-approach");
            int force = ice.Subroutines.Any(subroutine => subroutine is EndRunSubroutineSpec) ? 240 : 90;
            if (context.Generation >= 7 && ice.Subroutines.Any(subroutine => subroutine is GiveTagsSubroutineSpec))
                force += (int)((context.CorpProfile.TagWeight - 1) * 150);
            if (context.Generation >= 6)
                return Value(force + context.ServerValue(server) + RunDenialValue(context, server) - cost * 30,
                    "rez/relevant-protection-and-denial", approximate: true);
            return Value(force + context.ServerValue(server) - cost * 30, "rez/relevant-protection", approximate: true);
        }
        // v6: an upgrade in the server being run on works now or never, so it
        // isn't held back for ICE the Runner has already passed or chosen not to face.
        // It waits until the Runner is past the server's ICE, which may still stop the run.
        bool attacked = context.Features.Board.Run?.AttackedServer is { } attackedServer && attackedServer == server?.Id;
        if (repairs && attacked && definition?.Printed.CardClass == RuntimeCardClass.Upgrade && cost <= context.Self.Credits)
            return context.Features.Board.Run!.CurrentIceIndex < 0
                ? Value(Math.Max(120, context.ServerValue(server) / 2) - cost * 30, "rez/upgrade-in-attacked-server", approximate: true)
                : Value(-10, "rez/upgrade-wait-for-server");
        if (context.Self.Credits - cost < context.RezReserve) return Value(-300, "rez/preserve-ice-budget");
        if (definition?.Printed.CardClass == RuntimeCardClass.Asset)
        {
            int futureIncome = definition.Behavior?.Triggers.SelectMany(trigger => trigger.Instructions)
                .Where(instruction => instruction.Kind == BehaviorInstructionKind.LoadHostedCredits).Sum(instruction => instruction.Amount) ?? 0;
            int immediateDraw = definition.Behavior?.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.CardRezzed)
                .SelectMany(trigger => trigger.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.Draw).Sum(instruction => instruction.Amount) ?? 0;
            // v7: a trap that fires when the Corp's turn begins (Clearinghouse) must be
            // rezzed first; it pays off by its counters. Traps that work unrezzed aren't.
            if (context.Generation >= 4 && futureIncome == 0 && immediateDraw == 0 && ScalesWithCounters(definition)
                && definition.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.TurnBegin && !trigger.ActiveWhileUnrezzed) == true
                && (card?.AdvancementCounters ?? 0) > 0)
                return Value((card!.AdvancementCounters ?? 0) * 150 - cost * context.CreditValue, "rez/trap-before-turn", approximate: true);
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
