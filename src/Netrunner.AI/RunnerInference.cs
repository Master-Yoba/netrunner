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
        double untracked = TrackerPrior.UntrackedHqAgendas(hand, tracked, Prior(context), definition => IsAgenda(context, definition),
            context.Catalog.BehaviorVersion);
        return Math.Clamp((agendas + untracked) / hand, 0, 1);
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

    /// <summary>
    /// Runner v28: how much likelier a face-down card in a remote is to be an agenda when ICE protects the remote, and when
    /// none does, as odds against the unseen root cards' agenda share. The Corp protects its agendas and leaves assets and
    /// ambushes naked: in the Core Battle Box's saved games, an unadvanced face-down card behind ICE was an agenda 71% of
    /// the time and one in a naked remote never was, against an agenda share of about 44% among the pool's root cards.
    /// </summary>
    public const double ProtectedAgendaOdds = 3.0;
    public const double NakedAgendaOdds = 0.1;

    /// <summary>
    /// Runner v28: the chance a face-down card in a remote's root is an agenda, and an agenda's value there (500 and 180 a
    /// point, as a known one). What the tracker believes of the slot counts as it stands; the rest is the unseen root cards'
    /// agenda share, moved by whether ICE protects the remote.
    /// </summary>
    public static (double Chance, double Value) RootAgenda(HeuristicContext context, AgentServer server, int index)
    {
        static double Worth(CardEvaluationDescriptor card) => 500 + card.Agenda!.AgendaPoints * 180;
        (double share, double unseenValue) = context.Memo("root-agendas", () =>
        {
            Dictionary<string, double> roots = TrackerPrior.Conditional(Prior(context), definition => TrackerPrior.Fits(context.Catalog, definition, "root"));
            double agendas = 0, worth = 0;
            foreach ((string definition, double weight) in roots)
                if (context.Catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card) && card.Agenda is not null)
                {
                    agendas += weight;
                    worth += weight * Worth(card);
                }
            return (agendas, agendas > 0 ? worth / agendas : 0);
        });
        double odds = share >= 1 ? double.PositiveInfinity
            : share / (1 - share) * (server.Ice.Length > 0 ? ProtectedAgendaOdds : NakedAgendaOdds);
        double chance = double.IsPositiveInfinity(odds) ? 1 : odds / (1 + odds);
        if (Tracker(context) is not { } tracker) return (chance, unseenValue);
        double tracked = 0, trackedWorth = 0, believed = 0;
        foreach ((string definition, double probability) in tracker.SlotBelief(server.Id, "root", index))
        {
            believed += probability;
            if (context.Catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card) && card.Agenda is not null)
            {
                tracked += probability;
                trackedWorth += probability * Worth(card);
            }
        }
        double residual = Math.Max(0, 1 - believed);
        double total = tracked + residual * chance;
        return total <= 0 ? (0, unseenValue) : (total, (trackedWorth + residual * chance * unseenValue) / total);
    }

    /// <summary>
    /// Runner v24 (bot loop 1): the chance an unadvanced face-down root card is an ambush the Corp can pay for whose damage
    /// exceeds the grip (Byte!'s 3 net damage against 2 cards): its share of the root cards the Corp may hold, and at least one
    /// in five once the Runner has seen this Corp's copy.
    /// </summary>
    public static double LethalAmbushShare(HeuristicContext context, int grip) => context.Memo($"lethal-ambush-share/{grip}", () =>
    {
        bool Lethal(string definition) => context.Catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card)
            && card.Agenda is null && AccessDamage(card, 0) > grip && AmbushCost(card) <= context.Opponent.Credits;
        Dictionary<string, double> roots = TrackerPrior.Conditional(Prior(context), definition => TrackerPrior.Fits(context.Catalog, definition, "root"));
        double share = roots.Where(pair => Lethal(pair.Key)).Sum(pair => pair.Value);
        return context.Knowledge?.Reveals.Any(reveal => Lethal(reveal.DefinitionId)) == true ? Math.Max(share, 0.2) : share;
    });

    /// <summary>
    /// Runner v29 (bot plan H3, R18): for one unknown piece of ICE, the chance it is ICE the Corp can rez whose subroutines,
    /// all fired, would deal more damage than <paramref name="grip"/> holds, and the chance they would deal exactly the
    /// grip, leaving the Runner no card; from the tier's prior over the Corp's unseen ICE.
    /// </summary>
    public static (double Lethal, double Bare) UnknownIceDamage(HeuristicContext context, int grip) => context.Memo($"unknown-ice-damage/{grip}", () =>
    {
        if (!Evidence(context, card => SubroutineDamage(card) > 0)) return (0.0, 0.0);
        Dictionary<string, double> ice = TrackerPrior.Conditional(Prior(context), definition => TrackerPrior.Fits(context.Catalog, definition, "ice"));
        // R02: only what can fire. ICE of a type an installed breaker breaks is broken, not suffered.
        IceType[] covered = [.. context.Features.Board.RunnerRig.Select(card => context.Definition(card)?.Icebreaker?.InterfaceType).OfType<IceType>()];
        double lethal = 0, bare = 0;
        foreach ((string definition, double weight) in ice)
        {
            if (!context.Catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card) || card.Ice is not { } spec
                || spec.RezCost > context.Opponent.Credits || covered.Contains(IceType.Any) || covered.Contains(spec.IceType)) continue;
            int damage = SubroutineDamage(card);
            if (damage > grip) lethal += weight;
            else if (damage > 0 && damage == grip) bare += weight;
        }
        return (lethal, bare);
    });

    /// <summary>The damage all of an ICE's subroutines deal when they fire, its behavior subroutines (Elevation) included.</summary>
    public static int SubroutineDamage(CardEvaluationDescriptor card) => card.Ice?.Subroutines.Sum(subroutine => subroutine switch
    {
        DamageSubroutineSpec damage => damage.Amount,
        BehaviorSubroutineSpec behavior => card.Behavior?.Triggers.Where(trigger => trigger.Key == behavior.TriggerKey)
            .SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
            .Where(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage).Sum(instruction => instruction.Amount) ?? 0,
        _ => 0,
    }) ?? 0;

    /// <summary>
    /// Runner v29 (bot plan H3, R18): for one access in HQ or R&amp;D, the chance the card is an ambush the Corp can pay for
    /// whose damage is more than <paramref name="grip"/>, and the chance it is exactly the grip; from the tier's prior over
    /// the Corp's unseen cards, and at least three cards in a deck of 49 once the Runner has seen such an ambush this game
    /// (Byte! in HQ or R&amp;D).
    /// </summary>
    public static (double Lethal, double Bare) CentralAmbush(HeuristicContext context, int grip) => context.Memo($"central-ambush/{grip}", () =>
    {
        IReadOnlyDictionary<string, double> prior = Prior(context);
        double total = prior.Values.Sum();
        if (total <= 0 || !Evidence(context, card => card.Agenda is null && AccessDamage(card, 0) > 0)) return (0.0, 0.0);
        double lethal = 0, bare = 0;
        bool Ambush(CardEvaluationDescriptor card) => card.Agenda is null && AccessDamage(card, 0) > 0 && AmbushCost(card) <= context.Opponent.Credits;
        foreach ((string definition, double weight) in prior)
        {
            if (weight <= 0 || !context.Catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card) || !Ambush(card)) continue;
            int damage = AccessDamage(card, 0);
            if (damage > grip) lethal += weight;
            else if (damage == grip) bare += weight;
        }
        (lethal, bare) = (lethal / total, bare / total);
        bool seen = context.Knowledge?.Reveals.Any(reveal => context.Catalog.TryGet(new CardDefinitionId(reveal.DefinitionId), out CardEvaluationDescriptor card)
            && Ambush(card)) == true;
        return seen ? (lethal > 0 ? Math.Max(lethal, SeenAmbushShare) : lethal, bare > 0 ? Math.Max(bare, SeenAmbushShare) : bare) : (lethal, bare);
    });

    /// <summary>
    /// Runner v29 (bot plan H3; the guide marks R18 a matchup rule): whether the Runner has evidence the Corp plays a card
    /// <paramref name="test"/> takes: one on the Corp's list (Hard knows it), or one it has seen this game. Standard's prior
    /// is the whole pool, so without evidence every Corp would be priced as the one deck that deals damage.
    /// </summary>
    public static bool Evidence(HeuristicContext context, Func<CardEvaluationDescriptor, bool> test)
    {
        bool Takes(string definition) => context.Catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card) && test(card);
        return context.Knowledge?.OpponentList?.Any(entry => Takes(entry.DefinitionId)) == true
            || context.Knowledge?.Reveals.Any(reveal => Takes(reveal.DefinitionId)) == true
            || context.Features.Board.Servers.SelectMany(server => server.Ice.Concat(server.Root)).OfType<AgentCard>().Any(card => Takes(card.DefinitionId));
    }

    /// <summary>
    /// Runner v30 (bot plan H3, MATCHUP_PT_UNTAIAN.md PT13, PT15): the damage a non-winning steal deals this turn: the
    /// strongest option the Corp's rezzed cards can choose when an agenda is scored or stolen (Phật Gioan Baotixita's
    /// 1 to 3 net, by the power counters it holds), and none once an agenda has changed hands this turn, since it fires on
    /// the first only.
    /// </summary>
    public static int StealDamage(HeuristicContext context) => context.Memo("steal-damage", () =>
    {
        if (context.Features.Board.TurnFacts?.Runner.ThisTurn?.AgendaTurnChanges.Length > 0) return 0;
        int most = 0;
        foreach (AgentCard card in context.Features.Board.Servers.SelectMany(server => server.Root).OfType<AgentCard>().Where(card => card.Rezzed == true))
        {
            if (context.Definition(card)?.Behavior is not { } behavior) continue;
            foreach (BehaviorTrigger trigger in behavior.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.AgendaScoredOrStolen))
                foreach (BehaviorInstruction instruction in trigger.Instructions)
                {
                    IEnumerable<IReadOnlyList<BehaviorInstruction>> branches = instruction.Choice is { } choice
                        ? choice.Options.Where(option => option.Condition is not { Kind: BehaviorConditionKind.CounterAtLeast } condition
                            || card.Counters.Where(counter => counter.CounterName == condition.Fact).Sum(counter => counter.Count) >= (condition.Value ?? 0))
                            .Select(option => option.Instructions)
                        : [[instruction]];
                    foreach (IReadOnlyList<BehaviorInstruction> branch in branches)
                        most = Math.Max(most, HeuristicContext.Flatten(branch).Where(value => value.Kind == BehaviorInstructionKind.DealDamage).Sum(value => value.Amount));
                }
        }
        return most;
    });

    /// <summary>Runner v30: the fewest points among the agendas the Corp may still hold, by the tier's prior; 1 when it knows none.</summary>
    public static int SmallestAgenda(HeuristicContext context) => context.Memo("smallest-agenda", () =>
        Prior(context).Where(pair => pair.Value > 0).Select(pair => context.Catalog.TryGet(new CardDefinitionId(pair.Key), out CardEvaluationDescriptor card)
            ? card.Agenda?.AgendaPoints : null).OfType<int>().DefaultIfEmpty(1).Min());

    /// <summary>Runner v30: whether stealing an agenda of <paramref name="smallest"/> points takes a Runner on <paramref name="points"/> to seven.</summary>
    internal static bool StealWins(int points, int smallest) => points + smallest >= 7;

    /// <summary>
    /// Runner v30 (PT04): the damage one unknown piece of ICE is expected to deal, by the same prior and evidence as
    /// <see cref="UnknownIceDamage"/>: ICE the Corp can rez, of a type no installed breaker breaks.
    /// </summary>
    public static double ExpectedIceDamage(HeuristicContext context) => context.Memo("expected-ice-damage", () =>
    {
        if (!Evidence(context, card => SubroutineDamage(card) > 0)) return 0.0;
        IceType[] covered = [.. context.Features.Board.RunnerRig.Select(card => context.Definition(card)?.Icebreaker?.InterfaceType).OfType<IceType>()];
        return TrackerPrior.Conditional(Prior(context), definition => TrackerPrior.Fits(context.Catalog, definition, "ice"))
            .Sum(pair => context.Catalog.TryGet(new CardDefinitionId(pair.Key), out CardEvaluationDescriptor card) && card.Ice is { } spec
                && spec.RezCost <= context.Opponent.Credits && !covered.Contains(IceType.Any) && !covered.Contains(spec.IceType)
                    ? pair.Value * SubroutineDamage(card) : 0);
    });

    /// <summary>Runner v30 (PT04): the ambush damage one access in HQ or R&amp;D is expected to deal, by <see cref="CentralAmbush"/>'s prior.</summary>
    public static double ExpectedAmbushDamage(HeuristicContext context) => context.Memo("expected-ambush-damage", () =>
    {
        IReadOnlyDictionary<string, double> prior = Prior(context);
        double total = prior.Values.Sum();
        if (total <= 0 || !Evidence(context, card => card.Agenda is null && AccessDamage(card, 0) > 0)) return 0.0;
        return prior.Sum(pair => context.Catalog.TryGet(new CardDefinitionId(pair.Key), out CardEvaluationDescriptor card) && card.Agenda is null
            && AmbushCost(card) <= context.Opponent.Credits ? pair.Value * AccessDamage(card, 0) : 0) / total;
    });

    /// <summary>
    /// Runner v30: the chance a run's access finds an agenda: HQ's agenda share, R&amp;D's top card, a remote's root cards
    /// (a known agenda for certain, a face-down card at <see cref="RootAgenda"/>'s chance), and Archives' face-up agendas.
    /// </summary>
    public static double AgendaChance(HeuristicContext context, AgentServer server)
    {
        switch (server.Kind)
        {
            case ServerKind.Headquarters: return HqAgendaShare(context);
            case ServerKind.RAndD: return RdTopAgenda(context);
            case ServerKind.Archives: return context.Features.Board.CorpDiscard.Any(card => context.Definition(card)?.Agenda is not null) ? 1 : 0;
        }
        double none = 1;
        for (int index = 0; index < server.Root.Length; index++)
        {
            AgentCard? card = server.Root[index] ?? context.RememberedRoot(server, index);
            none *= card is not null ? context.Definition(card)?.Agenda is not null ? 0 : 1 : 1 - RootAgenda(context, server, index).Chance;
        }
        return 1 - none;
    }

    /// <summary>Three copies in a deck of 49: the least chance an access finds an ambush the Runner has seen this game.</summary>
    private const double SeenAmbushShare = 3.0 / 49;

    /// <summary>
    /// Runner v26 (self-flatlines): the grip a Runner needs at the end of a turn with a successful run, against the Corp's
    /// operations that punish one (Measured Response: threat 4, 4 meat damage unless the Runner pays 8), and the chance the
    /// Corp holds one. It counts each such operation the Corp may still hold, by the tier's prior, whose threat condition
    /// holds, which the Corp can pay for after a turn's credits, and which the Runner can't pay off. The chance is the
    /// operations' share of the Corp's unseen cards over a hand of them, and at least one in three once the Runner has seen
    /// one of them this game. Grip 0 when there is none.
    /// </summary>
    public static (int Grip, double Chance) Punishers(HeuristicContext context) => context.Memo("punishers", () =>
    {
        int threat = Math.Max(context.Self.AgendaPoints, context.Opponent.AgendaPoints);
        IReadOnlyDictionary<string, double> prior = Prior(context);
        int need = 0;
        double copies = 0;
        bool seen = false;
        foreach ((string definition, double weight) in prior)
        {
            // Every copy already seen: the Corp can't hold one (PR #97's review).
            if (weight <= 0) continue;
            if (!context.Catalog.TryGet(new CardDefinitionId(definition), out CardEvaluationDescriptor card)) continue;
            int damage = PunisherDamage(card, threat, context.Opponent.Credits, context.Self.Credits);
            if (damage == 0) continue;
            need = Math.Max(need, damage);
            copies += weight;
            seen |= context.Knowledge?.Reveals.Any(reveal => reveal.DefinitionId == definition) == true;
        }
        if (need == 0) return (0, 0.0);
        double total = prior.Values.Sum();
        int hand = Math.Max(1, context.Features.Board.HiddenZoneCounts.CorpHand);
        double chance = total <= 0 ? 0 : 1 - Math.Pow(1 - Math.Min(1, copies / total), hand);
        return (need, seen ? Math.Max(chance, 1.0 / 3) : chance);
    });

    /// <summary>
    /// The damage an operation that punishes a successful run deals, which the Runner can't pay its way out of with
    /// <paramref name="runnerCredits"/>: 0 unless it needs a successful run during the Runner's last turn, its threat
    /// condition holds, and the Corp can pay for it after a turn's credits. <see cref="Punishers"/> and evaluation set 5's
    /// Corp view read it.
    /// </summary>
    public static int PunisherDamage(CardEvaluationDescriptor card, int threat, int corpCredits, int runnerCredits)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (card.Printed.CardClass != RuntimeCardClass.Operation || card.Behavior is not { } behavior
            || !behavior.Conditions.Any(condition => condition.Kind == BehaviorConditionKind.SuccessfulRunDuringRunnerLastTurn)
            || behavior.Conditions.Any(condition => condition.Kind == BehaviorConditionKind.ThreatLevelAtLeast && threat < (condition.Value ?? 0))
            || (card.Printed.PlayCost ?? 0) > corpCredits + 3)
            return 0;
        return Damage(behavior.Instructions);

        // Damage the Runner can't pay its way out of with the credits it has.
        int Damage(IEnumerable<BehaviorInstruction> instructions) => instructions.Sum(instruction => instruction switch
        {
            { Kind: BehaviorInstructionKind.DealDamage } => instruction.Amount,
            { Kind: BehaviorInstructionKind.Choose, Choice: { Options.Count: > 0 } choice } when choice.Chooser == BehaviorChooser.Runner =>
                choice.Options.Where(option => (option.Costs ?? []).Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount)
                    <= runnerCredits).Select(option => Damage(option.Instructions)).DefaultIfEmpty(0).Min(),
            { Kind: BehaviorInstructionKind.Choose, Choice: { Options.Count: > 0 } choice } => choice.Options.Max(option => Damage(option.Instructions)),
            { Kind: BehaviorInstructionKind.Branch, Branch: { } branch } => Math.Max(Damage(branch.WhenTrue), Damage(branch.WhenFalse)),
            _ => 0,
        });
    }

    /// <summary>The credits the Corp pays for an ambush's damage on access (Byte!'s 4), or 0 when it costs nothing.</summary>
    private static int AmbushCost(CardEvaluationDescriptor card) => card.Behavior?.Triggers
        .Where(trigger => trigger.Occurrence == OccurrenceKind.CardAccessed)
        .SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions)).Where(instruction => instruction.Choice is not null)
        .SelectMany(instruction => instruction.Choice!.Options)
        .Where(option => HeuristicContext.Flatten(option.Instructions).Any(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage))
        .Select(option => (option.Costs ?? []).Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount))
        .DefaultIfEmpty(0).Min() ?? 0;

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
