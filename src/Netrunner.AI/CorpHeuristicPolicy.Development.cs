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
        // v28 (bot loop 1): a card replaced by its own copy is paid for again, so it is only worth it when the installed copy
        // has run down (a rezzed campaign out of credits); Mahkota Langit Grid replaced its unrezzed copy in R&D for a click.
        if (install.RequiredReplacementCardIds.Select(id => context.Card(id))
                .Any(installed => installed?.DefinitionId == card!.DefinitionId && !RunDown(context, installed)))
            return new(-100, -500, "development/replaces-its-own-copy", true, new("install", null, null, install.CardId.Value));
        int lost = install.RequiredReplacementCardIds.Sum(id => context.CardValue(context.Card(id), installed: true));
        // v6: a remote's root holds one agenda or asset, so installing another
        // there trashes it; an agenda thrown away costs far more than its card value.
        int displaced = DisplacedRootValue(context, server, definition);
        // v16: only a hidden agenda (see HidingPays) costs less than the card it trashes.
        lost = displaced < lost ? displaced : Math.Max(lost, displaced);
        int score;
        bool nakedAgenda = false, agendaWaits = false;
        if (definition.Ice is { } ice)
        {
            bool hasScoringRemote = context.Features.Board.Servers.Any(value => value.Kind == ServerKind.Remote && value.Ice.Length > 0);
            score = install.CreateNewRemote ? context.AgendaCount > 0 && !hasScoringRemote ? 350 : 20
                : context.ServerValue(server) / ((server?.Ice.Length ?? 0) + 1);
            score = IcePlacementV11(context, server, install.CreateNewRemote, score);
            // A2: ICE the Runner can't break yet taxes every future run,
            // and central ICE matters more as the Runner nears 7 points.
            score += RunnerCovers(context, ice.IceType) ? 0 : 100 * context.Parameter("coverage-scale", 100) / 100;
            // v26 (A9c): for a Corp that knows the Runner's list, ICE no breaker left in its deck can break taxes it for good.
            if (!RunnerCovers(context, ice.IceType) && OpponentCounting.NoBreakerLeftFor(context, ice.IceType))
                score += 100 * context.Parameter("coverage-scale", 100) / 100;
            if (server?.Kind is ServerKind.Headquarters or ServerKind.RAndD) score = (int)(score * context.OpponentScorePressure);
            score -= install.CreditCost * context.CreditValue;
            if (FreeIceInstalls(context) >= context.Features.Hand.Count(card => context.Definition(card)?.Ice is not null))
                score -= 100 + install.CreditCost * context.CreditValue;
            if (context.Self.Credits - install.CreditCost < ice.RezCost) score -= 200;
            // v33 (C01, C02): the layer an agenda waiting in HQ is waiting for, on the empty remote the Runner gets into.
            // The review of #137: on any remote without an agenda in its root (an upgrade may be there), the destinations the
            // agenda waits for, and only while its rez, modifiers included, is affordable.
            if (context.Configuration.PolicyVersion is V33 or V34 or V35 && !install.CreateNewRemote && server is { Kind: ServerKind.Remote }
                && !server.Root.Any(value => context.Definition(value)?.Agenda is not null)
                && context.AgendaCount > 0 && context.Self.Credits - install.CreditCost >= ice.RezCost + IceRezModifier(context)
                && RunnerGetsIn(context, server, 0))
                score += ProtectionBonus;
        }
        else if (definition.Agenda is not null)
        {
            int protection = (server?.Ice.Length ?? 0) * 110 + (server?.Root.Where(value => context.Definition(value)?.Printed.CardClass == RuntimeCardClass.Upgrade).Count() ?? 0) * 170;
            protection = ModelledProtection(context, server, install.CreditCost + definition.Printed.AdvancementRequirement);
            score = 260 + protection - lost;
            if (context.AgendaCount > 1) score += 40;
            if (context.Self.Credits < definition.Printed.AdvancementRequirement) score -= 120;
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
            // v12: when an agenda may go in depends on the deck's plan.
            CorpArchetypeProfile profile = context.CorpProfile;
            int protecting = install.CreateNewRemote ? 0 : server?.Ice.Length ?? 0;
            if (!scoreableNow)
                score -= (int)(Math.Max(0, profile.MinIceForAgenda - protecting) * 180 + profile.HoldAgendas * 300);
            if (protecting >= 1 && context.Features.Board.Turn.TurnNumber <= 8) score += (int)(profile.RushBonus * 120);
            // Self-play review, 28 September: the planner's turn-end horizon can't see the
            // steal, so an agenda that can't score this turn never goes in without ICE.
            nakedAgenda = !scoreableNow && protecting == 0;
            if (!scoreableNow && !install.CreateNewRemote && server is not null)
            {
                // v13 (playtest, 29 September): an agenda that goes in as soon as it's drawn shows the
                // Runner where to run and leaves HQ empty of them. It waits in HQ until its server can
                // hold it: the Corp can rez that server's ICE and still finish the agenda next turn.
                int unrezzed = server.Ice.Where(value => value is { Rezzed: false })
                    .Sum(value => context.Definition(value)?.Printed.RezCost ?? 0);
                int remaining = definition.Printed.AdvancementRequirement;
                int ready = context.Self.Credits - install.CreditCost - unrezzed - remaining;
                bool flooded = context.AgendaCount >= 2 || context.Features.Hand.Length >= (context.Self.MaximumHandSize ?? 5);
                if (ready < 0 && !flooded) score -= 250 + Math.Min(250, -ready * 40);
            }
            // v21 (reviews of the losses to Codex, 1 October): ICE isn't protection against a rig that breaks it. An
            // agenda that can't score this turn waits in HQ while the Runner would get into its server next turn
            // (a lone Palisade against Cleaver, Whitespace against Buzzsaw, Funhouse for a Runner who can clear tags).
            // Like an unprotected agenda, it is ranked below every other option, so the planner's turn-end horizon,
            // which can't see the steal, doesn't bring it back.
            // v23 (reviews of batch 4's losses to Codex, 1 October): two agendas in HQ push one out only when HQ is no
            // safer than the remote; it was, and four agendas went out into breakable remotes and were stolen.
            // v32 (bot plan H3 batch 6, the guide's C14): a breakable remote is a near-certain steal, HQ a fraction an access,
            // so a flooded HQ no longer pushes an agenda out; only a full one does, where a draw would discard.
            agendaWaits = context.Configuration.PolicyVersion == V32
                ? !scoreableNow && !nakedAgenda && server is not null
                    && RunnerGetsIn(context, server, install.CreditCost + definition.Printed.AdvancementRequirement)
                    && context.Features.Hand.Length < (context.Self.MaximumHandSize ?? 5)
                : !scoreableNow && !nakedAgenda && server is not null
                && RunnerGetsIn(context, server, install.CreditCost + definition.Printed.AdvancementRequirement)
                && (context.AgendaCount < 2
                    || context.Features.Board.Servers.FirstOrDefault(value => value.Kind == ServerKind.Headquarters) is { } hq
                    && !RunnerGetsIn(context, hq, install.CreditCost)
                    // v30 (bot loop 2's reviews): an HQ access takes one random card, a breakable remote's agenda is
                    // taken outright, so HQ is the safer place until agendas fill most of it (Off the Books behind a
                    // lone Kessleroid against Principia, two agendas in a hand of five).
                    || context.AgendaCount * 10 < context.Features.Hand.Length * 6);
            // v33 (bot plan H3 batch 7, the guide's C01, C02): a flooded HQ still lets an agenda out, but not into a server the
            // Runner gets into while the Corp holds ICE it can put there and rez: the ICE goes in this turn and the agenda
            // behind it the next (54% of the remote steals were behind one rezzed ICE, often from a Corp on 10 credits or more).
            if (!agendaWaits && context.Configuration.PolicyVersion is V33 or V34 or V35 && !scoreableNow && !nakedAgenda && server is not null
                && ProtectionInHand(context, server)
                && RunnerGetsIn(context, server, install.CreditCost + definition.Printed.AdvancementRequirement))
                agendaWaits = true;
        }
        else if (definition.Printed.CardClass == RuntimeCardClass.Asset)
        {
            int existingEconomy = context.Features.Board.Servers.SelectMany(value => value.Root).Count(value => HeuristicContext.Hosted(value) > 0);
            score = 180 - existingEconomy * 60 - lost;
            if (server?.Root.Any(value => context.Definition(value)?.Printed.CardClass == RuntimeCardClass.Upgrade) == true) score -= 80;
            if (context.Self.Credits < (definition.Printed.RezCost ?? 0)) score -= 150;
            score += (int)(context.CorpProfile.AssetBonus * 120 + (ScalesWithCounters(definition) ? (context.CorpProfile.TrapWeight - 1) * 150 : 0));
        }
        else
        {
            score = 70 + (server?.Root.Length ?? 0) * 60 + (server?.Ice.Length ?? 0) * 40 - lost
                + (server?.Root.Count(value => value is { Rezzed: true } && context.Definition(value)?.Printed.CardClass == RuntimeCardClass.Upgrade) ?? 0) * 60;
            if (definition.Printed.CardClass == RuntimeCardClass.Upgrade)
                score = UpgradePlacementV11(context, definition, server, install, score);
        }
        if (nakedAgenda)
            return new(-100, score, "development/agenda-never-unprotected", true, new("install", null, null, install.CardId.Value));
        if (agendaWaits)
            return new(-100, score, "development/agenda-waits-for-protection", true, new("install", null, null, install.CardId.Value));
        // v21: ICE on an Archives that protects nothing ranks below the other plays, whatever the planner's horizon makes of it.
        if (definition.Ice is not null && server?.Kind == ServerKind.Archives && server.Ice.Length == 0 && !ArchivesWorthIce(context))
            return new(-1, score, "development/archives-protects-nothing", true, new("install", null, null, install.CardId.Value));
        return Value(score, "development/destination-minus-replacement-value", approximate: true, objective: new("install", null, null, install.CardId.Value));
    }

    /// <summary>v33: whether HQ holds ICE the Corp can install on <paramref name="server"/> now and still rez.</summary>
    private static bool ProtectionInHand(HeuristicContext context, AgentServer server) => context.Self.Clicks >= 1
        && context.Features.Hand.Any(card => context.Definition(card)?.Ice is { } ice
            && context.Self.Credits >= server.Ice.Length + ice.RezCost + IceRezModifier(context));

    /// <summary>
    /// v33 (the review of #137): what the cards in play add to an ICE's rez cost (Fransofia Ward's +1), the rezzed Corp cards'
    /// and the Runner's installed ones' modifiers, so a layer is counted affordable only if its rez is.
    /// </summary>
    internal static int IceRezModifier(HeuristicContext context) => context.Features.Board.Servers.SelectMany(server => server.Root.Concat(server.Ice))
        .OfType<AgentCard>().Where(card => card.Rezzed == true)
        .Concat(context.Features.Board.RunnerRig).Concat(context.Features.Board.RunnerIdentity).Concat(context.Features.Board.CorpIdentity)
        .SelectMany(card => context.Definition(card)?.Behavior?.CostModifiers ?? [])
        .Where(modifier => modifier.Kind == BehaviorCostModifierKind.IceRezCreditCost).Sum(modifier => modifier.Delta);

    /// <summary>v33: what the layer an agenda waits for is worth, above an ordinary remote ICE's place.</summary>
    private const int ProtectionBonus = 300;

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
        RunForecast forecast = RunModel.Plan(context, server, server.Ice.Length - 1, context.Opponent.Credits + 2 + RunnerBurst(context), clicks: 3,
            grip: context.Features.Board.HiddenZoneCounts.RunnerGrip, corpRezBudget: Math.Max(0, context.Self.Credits - committed));
        if (!forecast.CanGetIn) return 650;
        int cost = forecast.Credits + forecast.CreditsLost + forecast.Clicks * 2 + forecast.Tags * 2 + forecast.Damage * 2;
        return Math.Min(550, cost * 60) + forecast.UnknownIce * 80;
    }

    /// <summary>v21: whether Archives is worth ICE: it holds an agenda, or runs on it pay the Runner. Earlier versions always.</summary>
    private static bool ArchivesWorthIce(HeuristicContext context) => RunnerSuccessPayoffs(context) > 0
        || context.Features.PrivateCards.FaceDownArchives.Concat(context.Features.Board.CorpDiscard).Any(card => context.Definition(card)?.Agenda is not null);

    /// <summary>
    /// v21: whether the whole-run model, run from the Runner's side, gets the Runner into the server next turn: it can
    /// spend three of its four clicks on credits and run with the last (a broke Runner still breaks a lone Palisade).
    /// </summary>
    private static bool RunnerGetsIn(HeuristicContext context, AgentServer server, int committed) =>
        RunModel.Plan(context, server, server.Ice.Length - 1, context.Opponent.Credits + 3 + RunnerBurst(context), clicks: 0,
            grip: context.Features.Board.HiddenZoneCounts.RunnerGrip, corpRezBudget: Math.Max(0, context.Self.Credits - committed)).CanGetIn;

    /// <summary>
    /// v26 (A9c): a Corp that knows the Runner's list counts its burst economy: while an event like Sure Gamble is unseen, the
    /// Runner can find its net gain beyond a click of credit; once they're all spent, it can't.
    /// </summary>
    private static int RunnerBurst(HeuristicContext context) =>
        Math.Max(0, OpponentCounting.BurstLeft(context) - 1);

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
            // v16: the steal it prevents, less the advancement and tempo thrown away. What goes in over
            // it is an asset; another agenda would stand in the same weak server.
            if (existing.Agenda is not null && installing.Agenda is null
                && HidingPays(context, server, existing, advanced))
                return 100 + advanced * 150 - existing.Printed.AgendaPoints * 250;
            return existing.Agenda is not null
                ? 1000 + existing.Printed.AgendaPoints * 300 + advanced * 150
                : context.CardValue(card, installed: true) + advanced * 100;
        });
    }

    /// <summary>
    /// v16 (playtest, 30 September): an agenda the Runner can reach before the Corp can score it is
    /// safer trashed by installing over it, when an installed card will shuffle it back from Archives
    /// into R&amp;D (Spin Doctor), which v15 does as soon as an agenda is there.
    /// </summary>
    private static bool HidingPays(HeuristicContext context, AgentServer server, CardEvaluationDescriptor agenda, int advanced)
    {
        int remaining = Math.Max(0, agenda.Printed.AdvancementRequirement - advanced);
        // Scorable this turn after the install's click: score it instead.
        if (context.Self.Clicks - 1 >= remaining && context.Self.Credits >= remaining) return false;
        return RecoversFromArchives(context) && ModelledProtection(context, server, remaining) < HidingThreshold;
    }

    /// <summary>Below this modelled protection the Runner is expected to get into the server next turn.</summary>
    private const int HidingThreshold = 300;

    /// <summary>True when an installed Corp card has an ability that shuffles cards from Archives into R&amp;D.</summary>
    private static bool RecoversFromArchives(HeuristicContext context) =>
        context.Features.Board.Servers.SelectMany(server => server.Root).Any(card => context.Definition(card)?.Behavior?.PaidAbilities
            .Any(ability => ability.Instructions.Any(instruction => instruction.Kind == BehaviorInstructionKind.ShuffleIntoDeck
                && instruction.SourceZone == BehaviorZone.Archives)) == true);

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

    private static AgentUtility Advance(HeuristicContext context, AdvanceCardParameters advance)
    {
        AgentCard? card = context.Card(advance.CardId);
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition?.Agenda is null && ScalesWithCounters(definition))
        {
            // v7: an advanceable trap (Urtica Cipher, Clearinghouse) grows with every
            // counter, and an advanced card reads as an agenda to the Runner (Wave 4 C1).
            if (context.Self.Credits - 1 < context.RezReserve) return Value(-40, "advance/trap-keeps-ice-budget", approximate: true);
            int counters = card!.AdvancementCounters ?? 0;
            // v20 (Codex review, 1 October): past the Runner's grip, another counter adds no threat; Urtica Cipher
            // took every spare click and credit while the Corp stopped scoring.
            // v25 (Wave 5 playtest, 2 October): ranked below every other option, so the planner's rollouts, which saw the
            // counters as progress, can't bring it back (two Urtica Ciphers went to +6 and +5).
            if (2 + counters > context.Features.Board.HiddenZoneCounts.RunnerGrip + 1)
                return new(-1, -30, "advance/trap-already-lethal", Approximate: true);
            // v23 (reviews of batch 4's losses to Codex, 1 October): the Runner has accessed this trap and won't run into it
            // again; every advancement after that was a click and a credit lost (four games).
            if (context.Knowledge?.Exposed?.Contains(card.Id) == true)
                return Value(-30, "advance/trap-known-to-runner", approximate: true);
            int bait = counters >= 4 ? 40 : 130 - counters * 15;
            bait = (int)(bait * context.CorpProfile.TrapWeight);
            return Value(bait, "advance/trap", approximate: true);
        }
        if (definition?.Ice is not null && definition.Behavior?.CanBeAdvanced == true)
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
        bool longAgenda = remaining - 1 > 3 && remaining - context.Self.Clicks <= 3 && ice >= 2
            && context.Self.Credits - remaining >= unrezzedCost;
        if (!finish && ice > 0 && (remaining - 1 <= 3 || longAgenda))
            score = 180 + definition.Printed.AgendaPoints * 40;
        if (!finish && context.Self.Credits <= context.RezReserve) score -= 200;
        // v6: advancing spends a credit; don't spend the one that rezzes this agenda's ICE.
        if (!finish && context.Self.Credits - 1 < ProtectionReserve(context, context.ServerOf(card.Id))) score -= 250;
        // v12: a shell game leaves agendas unadvanced until the turn they score, so they look like its traps.
        if (!finish) score -= (int)(context.CorpProfile.NeverAdvance * 120);
        // v11: an identity that pays for a card's first advancement (Built to Last) makes starting one cheaper.
        if ((card.AdvancementCounters ?? 0) == 0) score += FirstAdvanceGain(context) * context.CreditValue;
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
        int occupied = context.Features.Board.Servers.Count(value => value.Kind == ServerKind.Remote && value.Root.Length > 0);
        if (newRemote) return rootCandidate && !openSlot && occupied < profile.MaxRemotes ? score : -150;
        if (server is null) return score;
        int ice = server.Ice.Length;
        // v12: a glacier stacks its scoring remote before the agenda goes in.
        bool agendaHere = server.Kind == ServerKind.Remote && server.Root.Any(card => context.Definition(card)?.Agenda is not null);
        double stack = server.Kind == ServerKind.Remote && ice < profile.ScoringIceTarget
            && (agendaHere || server.Root.Length == 0 && context.AgendaCount > 0) ? (profile.ScoringIceTarget - ice) * 140 : 0;
        if (server.Kind == ServerKind.Remote && server.Root.Length == 0) return (rootCandidate ? score - 60 : -100) + (int)stack;
        score += (int)stack;
        // v18 (playtest, 30 September): agendas waiting in HQ make its ICE worth more. The planner's
        // fitted evaluation can't see the Corp's hand and put the first ICE on R&D over two agendas in
        // an open HQ; the bonus is only on HQ's ICE, so it never pushes an agenda out of HQ.
        if (server.Kind == ServerKind.Headquarters && ice < 2)
            score += context.Features.Hand.Sum(card => context.Definition(card)?.Agenda is { } agenda ? agenda.AgendaPoints : 0)
                * (ice == 0 ? 110 : 40);
        if (server.Kind is ServerKind.Headquarters or ServerKind.RAndD)
            score += (int)((Math.Max(0, profile.CentralIceTarget - ice) * 130 + (ice == 0 ? 130 : 0)) * context.OpponentScorePressure);
        // v21 (reviews of the losses to Codex, 1 October): an Archives of spent operations isn't worth a wall, however
        // often it's run; only agendas in it, or runs that pay the Runner, make it so.
        if (server.Kind == ServerKind.Archives && ice == 0 && ArchivesWorthIce(context))
            // v13 (playtest, 29 September): a Runner farming Archives for money is worth a wall.
            score += Math.Min(450, SuccessfulRunsOn(context, server) * 150) + RunnerSuccessPayoffs(context) * 60;
        return score;
    }

    /// <summary>
    /// v30: the ICE installs from HQ the Corp's rezzed cards make for it when its action phase ends (Mercia B4LL4RD's, a credit
    /// cheaper and without a click). While they cover every piece of ICE in HQ, installing one by hand spends a click and
    /// credits for nothing; Palisade went in on the last click and credit beside a rezzed Mercia.
    /// </summary>
    private static int FreeIceInstalls(HeuristicContext context) => context.Features.Board.Servers.SelectMany(server => server.Root)
        .OfType<AgentCard>().Where(card => card.Rezzed == true)
        .Count(card => context.Definition(card)?.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.ActionPhaseEnded
            && trigger.Instructions.Any(instruction => instruction.Kind == BehaviorInstructionKind.InstallCard
                && instruction.SourceZone == BehaviorZone.Headquarters && instruction.Target?.Filter?.CardType == BehaviorCardType.Ice)) == true);

    /// <summary>
    /// v28: whether an installed card has lost value a fresh copy would restore: rezzed, and holding fewer of the credits it
    /// loads when rezzed (a campaign). A card without such credits (an upgrade, recurring credits) loses nothing over time.
    /// </summary>
    private static bool RunDown(HeuristicContext context, AgentCard installed)
    {
        int load = context.Definition(installed)?.Behavior?.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.CardRezzed)
            .SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
            .Where(instruction => instruction.Kind == BehaviorInstructionKind.LoadHostedCredits).Sum(instruction => instruction.Amount) ?? 0;
        return installed.Rezzed == load > 0 && HeuristicContext.Hosted(installed) < load;
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

    /// <summary>Whether an upgrade does anything during a run: on approach, when the run ends, or on a steal.</summary>
    private static bool RunRelevant(CardEvaluationDescriptor definition) => definition.Behavior?.Triggers.Any(trigger =>
        trigger.Occurrence is OccurrenceKind.ServerApproached or OccurrenceKind.RunEnded or OccurrenceKind.IceEncountered
            or OccurrenceKind.RunSuccessful || trigger.RequireAgendaStolenDuringRun) == true
        || definition.Behavior?.PaidAbilities.Count > 0;

    /// <summary>What rezzing the unrezzed ICE protecting the Corp's installed agendas costs.</summary>
    private static int AgendaIceReserve(HeuristicContext context) => context.Features.Board.Servers
        .Where(server => server.Root.Any(card => context.Definition(card)?.Agenda is not null))
        .SelectMany(server => server.Ice).Where(card => card is { Rezzed: false })
        .Sum(card => context.Definition(card)?.Printed.RezCost ?? 0);

    /// <summary>The Runner's visible cards that pay it for successful runs (Pennyshaver, Red Team, Zahya).</summary>
    private static int RunnerSuccessPayoffs(HeuristicContext context) => context.Features.Board.RunnerRig
        .Concat(context.Features.Board.RunnerIdentity)
        .Count(card => context.Definition(card)?.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.RunSuccessful) == true);

    /// <summary>A card whose damage grows with its advancement counters (an advanceable trap).</summary>
    private static bool ScalesWithCounters(CardEvaluationDescriptor? definition) =>
        definition?.Behavior is { CanBeAdvanced: true } behavior
        && behavior.Triggers.SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
            .Any(instruction => instruction.DamageAmountOperand?.Kind == BehaviorDamageAmountKind.SourceAdvancementCounters);

    private static AgentUtility Rez(HeuristicContext context, CardInstanceId id, int cost)
    {
        CardEvaluationDescriptor? definition = context.Definition(id);
        AgentCard? card = context.Card(id);
        AgentServer? server = context.ServerOf(id.Value);
        if (definition?.Ice is { } ice)
        {
            if (context.Features.Board.Run?.CurrentIce?.Id != id.Value) return Value(-10, "rez/wait-for-approach");
            int force = ice.Subroutines.Any(subroutine => subroutine is EndRunSubroutineSpec) ? 240 : 90;
            if (ice.Subroutines.Any(subroutine => subroutine is GiveTagsSubroutineSpec))
                force += (int)((context.CorpProfile.TagWeight - 1) * 150);
            // v13 (playtest, 29 September): rezzing ICE on a server that doesn't hold an agenda mustn't leave
            // too little to rez the ICE that protects one; the Runner baits the Corp's money that way.
            // v20 (Codex review, 1 October): R&D, and HQ while the Corp holds an agenda, are agenda servers too; the
            // reserve declined Diviner, Ansel 1.0 and Whitespace there and the Runner stole from them.
            bool agendaServer = server?.Root.Any(card => context.Definition(card)?.Agenda is not null) == true
                || (server?.Kind == ServerKind.RAndD
                    || server?.Kind == ServerKind.Headquarters && context.Features.Hand.Any(card => context.Definition(card)?.Agenda is not null));
            if (!agendaServer
                && context.Self.Credits - cost < AgendaIceReserve(context))
                return Value(-200, "rez/keep-credits-for-the-agenda-ice", approximate: true);
            return Value(force + context.ServerValue(server) + RunDenialValue(context, server) - cost * 30,
                "rez/relevant-protection-and-denial", approximate: true);
        }
        // v6: an upgrade in the server being run on works now or never, so it
        // isn't held back for ICE the Runner has already passed or chosen not to face.
        // It waits until the Runner is past the server's ICE, which may still stop the run.
        bool attacked = context.Features.Board.Run?.AttackedServer is { } attackedServer && attackedServer == server?.Id;
        // v27 (bot loop 1, 3 October): upgrades that work outside scoring were held back as scoring upgrades.
        // Mercia B4LL4RD installs ICE from HQ when the action phase ends; Mahkota Langit Grid's credits pay rezzes in
        // its server. Each is rezzed once it has something to do.
        if (definition?.Printed.CardClass == RuntimeCardClass.Upgrade && definition.Behavior is { } upgrade)
        {
            bool installsIce = upgrade.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.ActionPhaseEnded
                && HeuristicContext.Flatten(trigger.Instructions).Any(instruction => instruction.Kind == BehaviorInstructionKind.InstallCard));
            bool paysRezzes = upgrade.Triggers.Any(trigger => HeuristicContext.Flatten(trigger.Instructions)
                .Any(instruction => instruction.Kind == BehaviorInstructionKind.RefillRecurringCredits));
            if (installsIce || paysRezzes)
            {
                bool work = installsIce
                    ? context.Features.Board.Turn.ActiveSide == PlayerSide.Corp && context.Features.Hand.Any(held => context.Definition(held)?.Ice is not null)
                    : server is not null && server.Ice.Concat(server.Root).Any(other => other is { Rezzed: false } && other.Id != card?.Id
                        && context.Definition(other)?.Printed.CardClass is RuntimeCardClass.Ice or RuntimeCardClass.Asset);
                return work && cost <= context.Self.Credits
                    ? Value(150 - cost * 30, "rez/utility-upgrade-has-work", approximate: true)
                    : Value(-20, "rez/utility-upgrade-waits-for-work");
            }
        }
        // v13 (playtest, 29 September): an upgrade that only pays off when the Corp scores from its
        // server (Malapert Data Vault) does nothing during a run; it is rezzed in the Corp's own turn,
        // once an agenda in its root can be scored.
        if (definition?.Printed.CardClass == RuntimeCardClass.Upgrade && !RunRelevant(definition))
        {
            bool scoring = context.Features.Board.Turn.ActiveSide == PlayerSide.Corp && context.Features.Board.Run is null
                && server?.Root.Any(card => context.Definition(card)?.Agenda is { } agenda
                    && agenda.AgendaPoints > 0
                    && context.Definition(card)!.Printed.AdvancementRequirement - (card!.AdvancementCounters ?? 0)
                        <= Math.Min(context.Self.Clicks, context.Self.Credits - cost)) == true;
            return scoring && cost <= context.Self.Credits
                ? Value(150 - cost * 30, "rez/scoring-upgrade-before-score", approximate: true)
                : Value(-20, "rez/scoring-upgrade-waits-for-score");
        }
        // v29 (bot loop 2): an upgrade that works only when an agenda is stolen does nothing in a remote without one (AMAZE
        // Amusements rezzed against a run on its own empty remote).
        if (attacked && server?.Kind == ServerKind.Remote && definition?.Printed.CardClass == RuntimeCardClass.Upgrade
            && definition.Behavior?.Triggers is { Count: > 0 } upgradeTriggers
            && upgradeTriggers.All(trigger => trigger.RequireAgendaStolenDuringRun || trigger.Occurrence == OccurrenceKind.AgendaScoredOrStolen)
            && !server.Root.Any(root => context.Definition(root)?.Agenda is not null))
            return Value(-10, "rez/upgrade-nothing-to-steal-here");
        if (attacked && definition?.Printed.CardClass == RuntimeCardClass.Upgrade && cost <= context.Self.Credits)
            return context.Features.Board.Run!.CurrentIceIndex < 0
                ? Value(Math.Max(120, context.ServerValue(server) / 2) - cost * 30, "rez/upgrade-in-attacked-server", approximate: true)
                : Value(-10, "rez/upgrade-wait-for-server");
        // v22 (reviews of the losses to Codex, 1 October): an asset loading three times its cost or more pays the budget
        // back at once (Regolith Mining License loads 15 for 2); holding it back left the Corp broke.
        // v23: only with a click left to start taking the credits; rezzed with none, it was trashed before it paid.
        bool paysBack = definition?.Printed.CardClass == RuntimeCardClass.Asset
            && (context.Features.Board.Turn.ActiveSide == PlayerSide.Corp && context.Self.Clicks > 0)
            && (definition.Behavior?.Triggers.SelectMany(trigger => trigger.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.LoadHostedCredits)
                .Sum(instruction => instruction.Amount) ?? 0) >= 3 * Math.Max(1, cost);
        if (!paysBack && context.Self.Credits - cost < context.RezReserve) return Value(-300, "rez/preserve-ice-budget");
        if (definition?.Printed.CardClass == RuntimeCardClass.Asset)
        {
            int futureIncome = definition.Behavior?.Triggers.SelectMany(trigger => trigger.Instructions)
                .Where(instruction => instruction.Kind == BehaviorInstructionKind.LoadHostedCredits).Sum(instruction => instruction.Amount) ?? 0;
            int immediateDraw = definition.Behavior?.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.CardRezzed)
                .SelectMany(trigger => trigger.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.Draw).Sum(instruction => instruction.Amount) ?? 0;
            // v7: a trap that fires when the Corp's turn begins (Clearinghouse) must be
            // rezzed first; it pays off by its counters. Traps that work unrezzed aren't.
            if (futureIncome == 0 && immediateDraw == 0 && ScalesWithCounters(definition)
                && definition.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.TurnBegin && !trigger.ActiveWhileUnrezzed) == true
                && (card?.AdvancementCounters ?? 0) > 0)
                return Value((card!.AdvancementCounters ?? 0) * 150 - cost * context.CreditValue, "rez/trap-before-turn", approximate: true);
            // v27 (bot loop 1, 3 October): an asset that works by a click ability (Humanoid Resources) or by counters it
            // gathers each turn (Phật Gioan Baotixita) has no income, and was never rezzed.
            if (futureIncome == 0 && immediateDraw == 0 && definition.Behavior is { } asset)
            {
                int clicks = asset.PaidAbilities.Select(ability => ability.Costs.Where(paid => paid.Kind is BehaviorCostKind.Clicks)
                    .Sum(paid => paid.Amount)).Where(total => total > 0).DefaultIfEmpty(0).Min();
                bool usable = clicks > 0 && context.Features.Board.Turn.ActiveSide == PlayerSide.Corp && context.Self.Clicks >= clicks;
                bool gathers = asset.Triggers.Any(trigger => trigger.Occurrence is OccurrenceKind.DiscardPhaseEnded or OccurrenceKind.TurnBegin
                    && HeuristicContext.Flatten(trigger.Instructions).Any(instruction => instruction.Kind == BehaviorInstructionKind.AddCounter));
                if (usable || gathers) return Value(100 - cost * 30, "rez/ability-or-counter-asset", approximate: true);
            }
            // v29 (bot loop 2): an asset that pays each turn (Public Access Plaza's credit) pays more the sooner it is rezzed.
            int perTurn = definition.Behavior?.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.TurnBegin)
                .SelectMany(trigger => trigger.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.GainCredits)
                .Sum(instruction => instruction.Amount) ?? 0;
            if (futureIncome == 0 && immediateDraw == 0 && perTurn == 0) return Value(-10, "rez/no-current-payoff", approximate: true);
            // v12 (self-play review, 28 September): an economy asset rezzed mid-way through the Runner's
            // turn shows it to a Runner who can still run and trash it; it waits for the last windows
            // before the Corp's turn, where auto-pass also stops a human.
            if (context.Features.Board.Turn.ActiveSide == PlayerSide.Runner
                && context.Features.Board.Turn.Step is not (TurnStep.RunnerDiscard or TurnStep.CorpDiscard))
                return Value(-10, "rez/economy-waits-for-its-turn", approximate: true);
            // v14 (playtest, 29 September): nor in the Corp's own turn. An asset whose money comes when
            // the Corp's turn begins earns the same rezzed at the end of the Runner's turn, and isn't
            // shown to the Runner for a whole turn first (Nico Campaign). A rez that pays now (a draw) goes.
            if (context.Features.Board.Turn.ActiveSide == PlayerSide.Corp && immediateDraw == 0
                && definition.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.TurnBegin) == true)
                return Value(-10, "rez/economy-waits-for-the-runner-turn-end", approximate: true);
            // It waits, as the assets above do, for the last windows of the Runner's turn.
            if (futureIncome == 0 && immediateDraw == 0)
                return Value(60 + perTurn * 80 - cost * 30, "rez/turn-income-asset", approximate: true);
            if (immediateDraw >= context.Features.Board.HiddenZoneCounts.CorpDeck) return Value(-10000, "rez/avoid-deck-out");
            return Value(futureIncome * 45 + immediateDraw * context.DrawValue - cost * context.CreditValue, "rez/economy-payback", approximate: true);
        }
        bool relevant = context.Features.Board.Run?.AttackedServer == server?.Id;
        return Value(relevant ? context.ServerValue(server) / 2 - cost * 60 : -10, "rez/upgrade-relevance", approximate: true);
    }

    /// <summary>
    /// v13 (playtest, 29 September): a tagged Runner's resource is worth trashing for what it earns the
    /// Runner (hosted credits, run payoffs, its install cost), not the Corp's own card value, which
    /// never beat the click and two credits the trash costs.
    /// </summary>
    private static AgentUtility TrashTagged(HeuristicContext context, TrashTaggedResourceParameters trash)
    {
        AgentCard? card = context.Card(trash.CardId);
        CardEvaluationDescriptor? definition = context.Definition(card);
        int worth = 200 + Math.Max(0, definition?.Printed.InstallCost ?? 0) * 60 + HeuristicContext.Hosted(card) * 90
            + (definition?.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.RunSuccessful) == true ? 250 : 0)
            + (definition?.Behavior?.PaidAbilities.Count > 0 ? 120 : 0);
        return Value((int)(worth * context.CorpProfile.TagWeight) - trash.CreditCost * context.CreditValue, "pressure/trash-tagged-resource-by-runner-loss");
    }

    private static AgentUtility Purge(HeuristicContext context, PurgeVirusCountersParameters purge)
    {
        int pressure = context.Features.Board.RunnerRig.Sum(card => card.Counters.Where(counter => counter.Purpose == CardCounterPurpose.Virus)
            .Sum(counter => counter.Count * (card.HostCardId is not null ? 95 : 45)));
        // v22 (reviews of the losses to Codex, 1 October): a broke Corp spent whole turns purging while Botulus grew back.
        if (context.Self.Credits < 3) pressure /= 2;
        return Value(pressure - purge.ClickCost * 100, "pressure/purge-virus-impact", approximate: true);
    }
}
