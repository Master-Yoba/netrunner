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

    /// <param name="threat">v31: whether the Corp's tag punishers count (<see cref="RunnerInference.TagThreat"/>); removing a
    /// tag counts what the removal saves itself.</param>
    public static int TagRisk(HeuristicContext context, int additional = 1, bool threat = true) =>
        180 * additional + (context.Features.Board.RunnerRig.Where(card => context.Definition(card)?.Printed.CardClass == RuntimeCardClass.Resource)
            .Select(card => Card(context, card)).DefaultIfEmpty(0).Max() * (context.Self.Clicks < (context.Self.Tags ?? 0) + additional ? 1 : 0))
            + (context.Self.Clicks == 0 ? 120 : 0)
            // v21 (A9b): the tag punishers it knows the Corp holds (Retribution seen in HQ).
            + (context.Generation >= 17 ? RunnerInference.KnownTagThreat(context) : 0)
            // v31 (the review of #133): what the Corp's next turn can take for the tags taken here, in every tag choice
            // (Funhouse's take a tag or end the run, its pay 4 or take a tag).
            + (threat && context.Generation == HeuristicContext.TagGeneration
                ? (int)(RunnerInference.TagThreat(context, (context.Self.Tags ?? 0) + additional) - RunnerInference.TagThreat(context, context.Self.Tags ?? 0))
                : 0);

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
    /// Runner v28: an unadvanced face-down card in a remote, worth 190 (what any unknown card was) plus the chance it is an
    /// agenda times what an agenda is worth over that, weighted by the profile's inference scale.
    /// </summary>
    private static int UnknownRemoteRoot(HeuristicContext context, AgentServer server, int index, double inference)
    {
        (double chance, double value) = RunnerInference.RootAgenda(context, server, index);
        return 190 + (int)(inference * chance * Math.Max(0, value - 190));
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
            // v13: a card the Runner accessed before is known, face down or not.
            card ??= context.RememberedRoot(server, index);
            // v21 (A9b): or one it's confident of from what it saw (an upgrade it left in HQ).
            if (context.Generation >= 17) card ??= RunnerInference.Believed(context, server, "root", index);
            // v28: an unadvanced face-down card in a remote is worth the agenda it may be (the Corp protects its agendas).
            // v32 (bot plan H3 batch 4, ZW05, LE15): an advanced one is worth the agenda an advanced card likely is; the Corp
            // scored 85% of the remotes the Runner let through with counters already showing.
            root += card is null && advancement > 0 && server.Kind == ServerKind.Remote && context.Generation >= 27
                ? 190 + (int)(inference * RunnerInference.AdvancedRootAgenda(context, server, index) switch { var (chance, value) => chance * Math.Max(0, value - 190) }
                    // R06: a steal also denies the Corp the score an advanced card is a turn from; half an agenda's worth again.
                    * AdvancedDenial)
                : card is null && advancement == 0 && server.Kind == ServerKind.Remote && context.Generation >= 23
                ? UnknownRemoteRoot(context, server, index, inference)
                : card is null
                ? 190 + (int)(inference * advancement * 200)
                : context.Definition(card)?.Agenda is { } agenda ? 500 + agenda.AgendaPoints * 180
                : context.Generation >= 9 ? AffordableTrash(context, card) : Trash(context, card);
        }

        // v22 (A9c): a Runner that knows the Corp's list counts the agendas left in it, not the deck-size estimate.
        double factor = (context.Generation >= 18 ? OpponentCounting.ListAgendaDensity(context) : null) ?? context.AgendaDensityFactor();
        double density = 1 + inference * (factor - 1);
        int central = server.Kind switch
        {
            ServerKind.Headquarters => context.Features.Board.HiddenZoneCounts.CorpHand > 0 ? (int)(320 * density) : 0,
            ServerKind.RAndD => context.Features.Board.HiddenZoneCounts.CorpDeck > 0 ? (int)(340 * density) : 0,
            ServerKind.Archives => context.Features.Board.CorpDiscard.Sum(card => context.Definition(card)?.Agenda is { } agenda ? agenda.AgendaPoints * 220 : 0)
                + Math.Max(0, (context.Features.Board.ZoneCounts?.CorpDiscard ?? 0) - context.Features.Board.CorpDiscard.Length) * 70,
            _ => 0,
        };
        // v21 (A9b): HQ and R&D are worth the agendas they likely hold. The unseen cards keep the deck-size estimate above;
        // what the Runner has seen moves it, against what it would expect knowing nothing (a known non-agenda on R&D's top,
        // an HQ it has seen to hold none, or one it knows holds an agenda). This replaces v10's freshness.
        // Only on real knowledge: HQ when the tracker knows at least half of it, R&D when it knows the top card (an agenda
        // raises it; anything else keeps v10's floor for a seen top, which a multi-access run such as Conduit still values).
        // Otherwise v10's freshness, so small stale shares don't move close calls.
        double? known = context.Generation >= 17 && RunnerInference.AgendaShare(context) is > 0 and var unseen
            ? server.Kind switch
            {
                ServerKind.Headquarters when RunnerInference.HqKnownShare(context) >= 0.5 => Math.Min(3, RunnerInference.HqAgendaShare(context) / unseen),
                // v33 (bot plan H3 batch 5, NE12): the Corp bots hold their agendas in HQ, and HqAgendaShare's untracked part
                // already carries the pool's fitted HQ factor, so it is read however little of HQ the tracker knows (HQ held an
                // agenda in 80% of the Runner's turns against Nebula, and it ran HQ in 13% of those).
                // Not against an ambush in HQ or a live steal-damage engine (MATCHUP_PT_UNTAIAN.md PT09: blind central farming
                // while Phật is active pays its damage; Byte! waits in HQ).
                ServerKind.Headquarters when context.Generation >= 28 && RunnerInference.StealDamage(context) == 0
                    && !RunnerInference.Evidence(context, card => card.Agenda is null && RunnerInference.AccessDamage(card, 0) > 0)
                    => Math.Min(3, RunnerInference.HqAgendaShare(context) / unseen),
                ServerKind.RAndD when RunnerInference.Tracker(context)?.DeckTopAt(0) is { } top =>
                    context.Catalog.TryGet(new CardDefinitionId(top), out CardEvaluationDescriptor card) && card.Agenda is not null ? 3 : 0.3,
                _ => null,
            }
            : null;
        if (known is { } relative) central = (int)(central * (1 + inference * (relative - 1)));
        else if (context.Generation >= 7) central = (int)(central * CentralFreshness(context, server.Kind));
        if (context.Generation >= 8)
            central = (int)(central * server.Kind switch
            {
                ServerKind.RAndD => context.RunnerProfile.RdFocus,
                ServerKind.Headquarters => context.RunnerProfile.HqFocus,
                _ => 1,
            });
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

    /// <summary>
    /// v13: what a known non-agenda root card is worth to a run, which is only its trash, and only
    /// if the Runner can pay for it (playtest, 30 September: the Runner kept running a known Nico
    /// Campaign it couldn't afford to trash).
    /// </summary>
    private static int AffordableTrash(HeuristicContext context, AgentCard card)
    {
        int cost = context.Definition(card)?.Printed.TrashCost ?? int.MaxValue;
        if (cost > context.Self.Credits) return 0;
        return Math.Max(0, Trash(context, card) - cost * context.CreditValue);
    }

    /// <summary>
    /// v13: the Runner's own gain from trashing an accessed card: triggers on its cards that fire
    /// on the trash, such as René "Loup" Arcemont's first trash each turn (a credit and a card).
    /// </summary>
    public static int TrashReward(HeuristicContext context)
    {
        int reward = 0;
        foreach (AgentCard card in context.Features.Board.RunnerRig.Concat(context.Features.Board.RunnerIdentity))
        {
            if (context.Definition(card)?.Behavior is not { } behavior) continue;
            foreach (BehaviorTrigger trigger in behavior.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.AccessedCardTrashed))
            {
                // A once-per-turn reward is spent by the first trash this turn.
                if (trigger.OncePerTurn && context.Features.Board.TurnFacts?.Runner.ThisTurn?.TrashedCardCount > 0) continue;
                reward += HeuristicContext.Flatten(trigger.Instructions).Sum(instruction => instruction.Kind switch
                {
                    BehaviorInstructionKind.GainCredits => instruction.Amount * context.CreditValue,
                    BehaviorInstructionKind.Draw => instruction.Amount * context.DrawValue,
                    _ => 0,
                });
            }
        }
        return reward;
    }

    public static int Trash(HeuristicContext context, AgentCard? card)
    {
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition is null) return 70;
        if (context.Generation >= 9) return TrashV13(context, card!, definition) + TrashReward(context);
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

    /// <summary>
    /// v13 (playtest, 30 September): an economy asset is worth the money it will still make (an
    /// unrezzed Nico Campaign loads 9 credits when rezzed); a trap that only hurts on access is
    /// worth little once known, since the Runner won't come back (trashing Urtica Cipher rarely pays).
    /// </summary>
    private static int TrashV13(HeuristicContext context, AgentCard card, CardEvaluationDescriptor definition)
    {
        int hosted = HeuristicContext.Hosted(card);
        // v33 (bot plan H3 batch 5, MATCHUP_NEBULA.md NE15): the Corp takes its next withdrawal before the Runner is back,
        // so a trash denies only the credits after it (Regolith's last 3 deny nothing); the Runner paid 4.3 a game against
        // Nebula to trash accessed cards, against 2.9 against LEO.
        if (hosted > 0 && context.Generation >= 28)
        {
            int take = definition.Behavior?.PaidAbilities.SelectMany(ability => ability.Instructions)
                .Where(instruction => instruction.Kind is BehaviorInstructionKind.TakeHostedCredits or BehaviorInstructionKind.TakeUpToHostedCredits)
                .Select(instruction => instruction.Amount).DefaultIfEmpty(0).Max() ?? 0;
            int left = take > 0 ? Math.Max(0, hosted - take) : hosted;
            return left == 0 ? 60 : 160 + left * 90;
        }
        if (hosted > 0) return 160 + hosted * 90;
        if (definition.Behavior is { } behavior)
        {
            int load = behavior.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.CardRezzed)
                .SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
                .Where(instruction => instruction.Kind == BehaviorInstructionKind.LoadHostedCredits).Sum(instruction => instruction.Amount);
            int perTurn = behavior.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.TurnBegin)
                .SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
                .Where(instruction => instruction.Kind == BehaviorInstructionKind.GainCredits).Sum(instruction => instruction.Amount);
            if (card.Rezzed != true && load > 0) return 160 + load * 45;
            if (perTurn > 0) return 160 + perTurn * 120;
            // v30 (MATCHUP_PT_UNTAIAN.md PT08, the guide's R17): a card whose own triggers damage the Runner, not on access
            // (Phật Gioan Baotixita on the first agenda of every turn), is an engine to remove before farming agendas.
            int engine = behavior.Triggers.Where(trigger => trigger.Occurrence != OccurrenceKind.CardAccessed)
                .SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
                .Where(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage).Select(instruction => instruction.Amount)
                .DefaultIfEmpty(0).Max();
            if (context.Generation >= 25 && engine > 0 && !behavior.CanBeAdvanced) return EngineTrash + engine * 150;
            bool accessTrap = behavior.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.CardAccessed
                && HeuristicContext.Flatten(trigger.Instructions).Any(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage));
            if (accessTrap) return 40;
            if (behavior.CanBeAdvanced && (card.AdvancementCounters ?? 0) > 0) return 180 + (card.AdvancementCounters ?? 0) * 200;
        }
        if (definition.Printed.CardClass == RuntimeCardClass.Upgrade)
        {
            bool protection = definition.Behavior?.Triggers.Any(trigger => trigger.Occurrence == OccurrenceKind.ServerApproached) == true;
            return protection ? 430 : 110;
        }
        return definition.Printed.CardClass == RuntimeCardClass.Asset ? 180 : 50;
    }

    /// <summary>v32 (R06): an advanced remote agenda's worth with the score its steal denies the Corp.</summary>
    private const double AdvancedDenial = 1.5;

    /// <summary>v30: a damage engine's worth before its damage, above an ordinary asset's 180.</summary>
    private const int EngineTrash = 400;

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
