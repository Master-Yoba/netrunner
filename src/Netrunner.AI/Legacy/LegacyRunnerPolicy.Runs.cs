using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class LegacyRunnerPolicy
{
    /// <param name="knownSuccessValue">v24: value the run pays only if it succeeds, counted when no ICE on the path is unknown.</param>
    /// <param name="gripSpent">v26: cards leaving the grip to make the run (a run event itself).</param>
    private static AgentUtility Run(HeuristicContext context, AgentServer? server, int extraRunCredits = 0, int extraValue = 0,
        int upfrontCost = 0, int knownSuccessValue = 0, int gripSpent = 0)
    {
        if (server is null) return Value(-1000, "run/unknown-server", true, true);
        // v26: the grip the run is made with, once a run event has left it (PR #97's review: every lethal check reads it).
        int grip = context.Generation >= 22 ? context.Features.Hand.Length - gripSpent : context.Features.Hand.Length;
        (int cost, int loss, int damage, bool blocked, int unknown) = Protection(context, server, server.Ice.Length - 1, upfrontCost);
        // v7: the whole-run model (A4a) costs the run as one path, tolls included.
        // v21 (A9b): face-down ICE it's confident of from what it saw is planned as that ICE, which the Corp can rez.
        AgentServer planned = context.Generation >= 17 ? RunnerInference.WithBelievedIce(context, server) : server;
        RunForecast? forecast = context.Generation >= 6
            ? RunModel.Plan(context, planned, planned.Ice.Length - 1, context.Self.Credits + extraRunCredits, context.Self.Clicks - 1, upfrontCost,
                corpRezBudget: ReferenceEquals(planned, server) ? null : context.Opponent.Credits)
            : null;
        if (forecast is not null)
            (cost, loss, damage, blocked, unknown) = (forecast.Credits, forecast.CreditsLost, forecast.Damage, !forecast.CanGetIn, forecast.UnknownIce);
        int reserve = server.Root.Where(card => RunnerValues.Trash(context, card) >= 350).Select(card => context.Definition(card)?.Printed.TrashCost ?? 0).DefaultIfEmpty(0).Min();
        int funds = context.Self.Credits - upfrontCost + extraRunCredits;
        if (blocked || damage > grip || cost > funds - loss)
            return Value(-1000, "run/known-unaffordable-or-lethal", true);
        // V5: the run itself spends a click, and what's left must pay a known approach toll.
        (bool tollBlocked, int tollCredits, int tollClicks) = forecast is not null ? (false, 0, forecast.Clicks)
            : context.Generation >= 4
            ? ApproachToll(context, server, context.Self.Credits - upfrontCost - Math.Max(0, cost - extraRunCredits) - loss, context.Self.Clicks - 1)
            : (false, 0, 0);
        if (tollBlocked) return Value(-1000, "run/known-approach-toll-unaffordable", true);
        int tagCost = (forecast?.Tags ?? 0) * 300;
        // v6: an advanced card the Runner can't identify may be a trap dealing 2 plus
        // its counters on access (Urtica Cipher); with too few cards in the grip that's a flatline.
        int trapRisk = 0;
        if (context.Generation >= 17)
        {
            // v21 (A9b): each face-down root card is a trap as likely as the tracker says (an Urtica Cipher seen in HQ, now in a
            // naked remote), and the rest as before: an advanced unknown card in proportion to the Corp playing traps (Hard knows
            // the list).
            // v26 (self-flatlines): with the grip as it is once a run event has left it; Clean Getaway was played into Byte!
            // with the ambush priced against the grip it was still in.
            trapRisk = TrackedTrapRisk(context, server, grip);
        }
        else if (context.Generation >= 8)
        {
            // An advanced unknown card is more often an agenda; it's a trap in proportion to how
            // likely this Corp is to play them, which rises once the Runner has seen one.
            double trap = SeenTrap(context) ? 0.6 : 0.15;
            trapRisk = UnknownAdvancedRoots(context, server).Sum(counters => (int)(trap
                * (2 + counters > context.Features.Hand.Length ? 6000 : (2 + counters) * 100)));
        }
        else if (context.Generation >= 5)
            foreach (AgentCard? root in server.Root)
            {
                if (root is not { AdvancementCounters: > 0 } advanced || context.Definition(advanced) is not null) continue;
                int trapDamage = 2 + (advanced.AdvancementCounters ?? 0);
                trapRisk += trapDamage >= context.Features.Hand.Length ? 700 : trapDamage * 40;
            }
        // v15 (reviews of the losses to Codex, 1 October): with the Corp one agenda from winning, an advanced card in a
        // remote is most likely that agenda. Leaving it loses if it is one, running loses only if it's a trap, so
        // neither the trap's risk nor even a possible flatline keeps the Runner out.
        bool matchPoint = context.Generation >= 11 && server.Kind == ServerKind.Remote && UnknownAdvancedRoots(context, server).Any()
            && context.Opponent.AgendaPoints >= 4;
        // v26 (self-flatlines): not while the Runner can draw before it runs; it ran a trap with an empty grip and four clicks.
        if (matchPoint && context.Generation >= 22 && grip <= 1 && context.Self.Clicks >= 2) matchPoint = false;
        // v11 (self-play review, 28 September): a possible trap that would flatline the Runner isn't worth the access.
        if (context.Generation >= 8 && !matchPoint && LethalTrap(context, server, grip)) return Value(-1000, "run/possible-lethal-trap", true);
        if (context.Generation >= 17 && !matchPoint && LikelyLethalTrap(context, server, grip)) return Value(-1000, "run/likely-lethal-trap", true);
        // v13 (playtest, 30 September): a known trap's damage is certain (the Runner kept running a known Urtica Cipher).
        if (context.Generation >= 9)
        {
            int trap = KnownAccessDamage(context, server);
            if (trap > grip) return Value(-1000, "run/known-lethal-access", true);
            damage += trap;
        }
        int rewards = SuccessRewards(context, server);
        // v26: a breaker trashed when the run ends (Mayfly) is spent by the run, as the encounter planner counts it; the Runner
        // started runs through it and then wouldn't spend it, 17 times in one game.
        int disposable = context.Generation >= 22 && forecast is not null ? forecast.Crossings.Select(crossing => crossing.Method).Distinct()
            .Select(method => context.Card(new CardInstanceId(method))).OfType<AgentCard>()
            .Where(card => context.Definition(card)?.Icebreaker?.RunEndCleanup is not null).Sum(card => RunnerValues.Card(context, card)) : 0;
        int repeated = context.Features.Board.TurnFacts?.Runner.ThisTurn?.AttemptedRunServerIds.Count(id => id == server.Id) ?? 0;
        int risk = unknown * (context.Self.Credits < 3 || context.Features.Hand.Length < 2 ? 450 : 260) * context.Parameter("risk-scale", 100) / 100;
        if (context.Generation >= 8 && unknown > 0)
        {
            // v11 (self-play review, 28 September): a flat fear of unrezzed ICE made the Runner stop
            // running, so the Corp never rezzed and nothing became known. The fear shrinks with what the
            // Corp can pay to rez and with the ICE types the Runner's breakers cover, and a run that makes
            // the Corp rez or reveal its ICE is worth something in itself.
            double budget = Math.Clamp(context.Opponent.Credits / 8.0, 0.3, 1);
            int covered = context.Features.Board.RunnerRig.Select(card => context.Definition(card)?.Icebreaker?.InterfaceType)
                .OfType<IceType>().Distinct().Count();
            double coverage = context.RigComplete ? 0.4 : Math.Max(0.4, 1 - 0.2 * covered);
            risk = (int)(risk * budget * coverage);
            if (context.Self.Credits >= 4) risk -= unknown * 60;
        }
        // v25 (bot loop 2): with one card or none in the grip, unknown ICE that does damage flatlines the Runner, and with a
        // click to spare it draws first; it ran an unknown remote into Empiricist's 3 net damage with one card.
        if (context.Generation >= 21 && unknown > 0 && context.Features.Hand.Length <= 1 && context.Self.Clicks >= 2) risk += unknown * 250;
        if (unknown == 0) extraValue += knownSuccessValue;
        if (matchPoint)
        {
            trapRisk = Math.Min(trapRisk, 200);
            extraValue += context.Opponent.AgendaPoints >= 5 ? 700 : 400;
        }
        int score = RunnerValues.Server(context, server) + rewards + extraValue
            - (upfrontCost + Math.Max(0, cost - extraRunCredits) + loss) * context.CreditValue
            - damage * 160 - risk - repeated * 180 - (funds - cost - loss - tollCredits < reserve ? 220 : 0)
            - (tollCredits + tollClicks * ClickCredits) * context.CreditValue - trapRisk - tagCost - disposable;
        // v11: an aggressive deck runs whenever it can afford to; a big rig holds back until its rig covers every ICE type.
        // v15 (reviews of the losses to Codex, 1 October): not on a run that can gain nothing, such as an Archives of
        // operations the Runner has already seen; aggression sent it there turn after turn.
        if (context.Generation >= 8 && (context.Generation < 11 || RunnerValues.Server(context, server) + rewards + extraValue > 0))
        {
            double aggression = context.RunnerProfile.Aggression;
            if (aggression > 0 || !context.RigComplete) score += (int)(aggression * 100);
        }
        bool win = unknown == 0 && RunnerValues.Winning(context, server);
        return new(win ? 100 : 0, score, "run/public-value-protection-and-access-reserve", true, new("run", null, null, null));
    }

    private static AgentUtility Continue(HeuristicContext context)
    {
        AgentRun? run = context.Features.Board.Run;
        AgentServer? server = RunnerValues.Attacked(context);
        if (run is null || server is null) return Value(10, "run/issued-continuation");
        // At the movement jack-out window the completed encounter still names
        // the ICE at CurrentIceIndex. Continuing moves inward; that ICE cannot
        // hit the Runner a second time.
        int lastIce = run.Encounter is { Status: RunEncounterStatus.Complete } encounter
            && encounter.Ice?.Id == run.CurrentIce?.Id ? run.CurrentIceIndex - 1 : run.CurrentIceIndex;
        // v25 (bot loop 2): at the jack-out decision the current ICE is passed, encountered or not; an unrezzed Ansel the
        // Runner had just passed counted as unknown ICE ahead, and it jacked out of an open path to an agenda.
        if (context.Generation >= 21 && run.Step == RunTimingStep.JackOutDecision) lastIce = run.CurrentIceIndex - 1;
        (int cost, int loss, int damage, bool blocked, int unknown) = Protection(context, server, lastIce);
        RunForecast? forecast = context.Generation >= 6
            ? RunModel.Plan(context, server, lastIce, context.Self.Credits + run.RunCredits, context.Self.Clicks)
            : null;
        if (forecast is not null)
            (cost, loss, damage, blocked, unknown) = (forecast.Credits, forecast.CreditsLost, forecast.Damage, !forecast.CanGetIn, forecast.UnknownIce);
        if (damage > context.Features.Hand.Length || blocked || cost > context.Self.Credits + run.RunCredits - loss)
            return Value(-1000, "run/jack-out-before-unaffordable-threat", true);
        (bool tollBlocked, int tollCredits, int tollClicks) = forecast is not null ? (false, 0, forecast.Clicks)
            : context.Generation >= 4
            ? ApproachToll(context, server, context.Self.Credits - Math.Max(0, cost - run.RunCredits) - loss, context.Self.Clicks)
            : (false, 0, 0);
        if (tollBlocked) return Value(-1000, "run/jack-out-before-unaffordable-toll", true);
        if (context.Generation >= 8 && LethalTrap(context, server)) return Value(-1000, "run/jack-out-before-possible-lethal-trap", true);
        if (context.Generation >= 17 && LikelyLethalTrap(context, server)) return Value(-1000, "run/jack-out-before-likely-lethal-trap", true);
        return new(unknown == 0 && RunnerValues.Winning(context, server) ? 100 : 0,
            Math.Max(250, RunnerValues.Server(context, server)) + SuccessRewards(context, server)
            - (Math.Max(0, cost - run.RunCredits) + loss + tollCredits + tollClicks * ClickCredits) * context.CreditValue
            - damage * 160 - unknown * 220 - (forecast?.Tags ?? 0) * 300,
            "run/continue-visible-affordable-line", true);
    }

    /// <summary>What a click is worth in credits when paying a toll.</summary>
    private const int ClickCredits = 2;

    /// <summary>
    /// A rezzed card in the server's root that, on approach, makes the Runner
    /// choose a cost or the run ends (Manegarm Skunkworks). Returns the cheapest
    /// payable option from what's left after the ICE, or blocked if none is.
    /// </summary>
    internal static (bool Blocked, int Credits, int Clicks) ApproachToll(HeuristicContext context, AgentServer server, int credits, int clicks)
    {
        int tollCredits = 0, tollClicks = 0;
        foreach (AgentCard? card in server.Root)
        {
            if (card is not { Rezzed: true } || context.Definition(card)?.Behavior is not { } behavior) continue;
            foreach (BehaviorTrigger trigger in behavior.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.ServerApproached))
                foreach (BehaviorChoiceSpec choice in trigger.Instructions.Select(instruction => instruction.Choice).OfType<BehaviorChoiceSpec>())
                {
                    if (choice.Chooser != BehaviorChooser.ActingPlayer
                        || !choice.Options.Any(option => option.Semantic == BehaviorChoiceOptionSemantic.Fallback
                            && option.Instructions.Any(instruction => instruction.Kind == BehaviorInstructionKind.EndRun))) continue;
                    (int Credits, int Clicks)? best = null;
                    foreach (BehaviorChoiceOption option in choice.Options.Where(option => option.Semantic != BehaviorChoiceOptionSemantic.Fallback))
                    {
                        IReadOnlyList<BehaviorCost> costs = option.Costs ?? [];
                        if (costs.Any(cost => cost.Kind is not (BehaviorCostKind.Credits or BehaviorCostKind.Clicks))) continue;
                        int optionCredits = costs.Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount);
                        int optionClicks = costs.Where(cost => cost.Kind == BehaviorCostKind.Clicks).Sum(cost => cost.Amount);
                        if (credits - tollCredits < optionCredits || clicks - tollClicks < optionClicks) continue;
                        if (best is not { } current || optionCredits + optionClicks * ClickCredits < current.Credits + current.Clicks * ClickCredits)
                            best = (optionCredits, optionClicks);
                    }
                    if (best is not { } paid) return (true, tollCredits, tollClicks);
                    tollCredits += paid.Credits;
                    tollClicks += paid.Clicks;
                }
        }
        return (false, tollCredits, tollClicks);
    }

    private static (int Cost, int Loss, int Damage, bool Blocked, int Unknown) Protection(HeuristicContext context, AgentServer server, int lastIndex,
        int upfrontCost = 0)
    {
        int total = 0, damage = 0, unknown = 0, creditLoss = 0;
        for (int index = Math.Min(lastIndex, server.Ice.Length - 1); index >= 0; index--)
        {
            AgentCard? card = server.Ice[index];
            IceBehaviorSpec? ice = context.Definition(card)?.Ice;
            if (ice is null) { unknown++; continue; }
            int strength = ice.Strength;
            bool currentEncounter = context.Features.Board.Run?.Encounter is { } active && active.Ice?.Id == card!.Id;
            // Current encounter quotes include all boosts and reductions.
            if (currentEncounter)
                strength = context.Features.Board.Run!.Encounter!.StrengthQuote?.EffectiveValue ?? strength;
            else
            {
                strength += ice.StrengthModifiers.Where(modifier => modifier.Condition == IceStrengthModifierCondition.ProtectingRemoteServer).Sum(modifier => server.Kind == ServerKind.Remote ? modifier.Delta : 0);
                strength += (ice.ValueModifiers ?? []).Where(modifier => modifier.Source == BehaviorValueSourceKind.CounterThreshold
                    && (modifier.CounterName == "advancement" ? card!.AdvancementCounters ?? 0
                        : card!.Counters.Where(counter => counter.CounterName == modifier.CounterName).Sum(counter => counter.Count))
                    >= (modifier.Threshold ?? int.MaxValue)).Sum(modifier => modifier.Delta);
            }
            int dangerous = ice.Subroutines.Count(subroutine => subroutine is not (GainCreditsSubroutineSpec or InstallCardSubroutineSpec));
            int hostedBreaks = context.Features.Board.RunnerRig.Where(rig => rig.HostCardId == card!.Id
                && context.Definition(rig)?.Behavior?.Hosting?.BreakHostSubroutine == true)
                .Sum(rig => rig.Counters.Where(counter => counter.Purpose == CardCounterPurpose.Virus).Sum(counter => counter.Count));
            int uncovered = Math.Max(0, dangerous - hostedBreaks);
            int cheapest = uncovered == 0 ? 0 : int.MaxValue;
            foreach (AgentCard rig in context.Features.Board.RunnerRig)
            {
                IcebreakerBehaviorSpec? breaker = context.Definition(rig)?.Icebreaker;
                if (breaker is null || breaker.InterfaceType != IceType.Any && breaker.InterfaceType != ice.IceType) continue;
                int? quotedStrength = currentEncounter ? context.Features.Board.Run!.Encounter!.BreakerStrengths
                    .FirstOrDefault(value => value.CardId == rig.Id)?.StrengthQuote.EffectiveValue : null;
                int current = quotedStrength ?? breaker.BaseStrength;
                if (quotedStrength is null)
                    current += (breaker.ValueModifiers ?? []).Where(modifier => modifier.Source == BehaviorValueSourceKind.PerInstalledCard)
                        .Sum(modifier => modifier.Delta * context.Features.Board.RunnerRig.Count(known => context.Definition(known)?.Icebreaker is not null));
                int increment = RunnerValues.PumpStrength(context, breaker);
                if (strength > current && increment <= 0) continue;
                int pumps = strength <= current ? 0 : (strength - current + increment - 1) / increment;
                int limit = breaker.BreakLimit == 0 ? Math.Max(1, uncovered) : breaker.BreakLimit;
                int cost = pumps * breaker.PumpCost + (uncovered + limit - 1) / limit * RunnerValues.BreakCost(context, rig, breaker,
                    useIssuedQuote: currentEncounter);
                cheapest = Math.Min(cheapest, cost);
            }
            if (dangerous == 0) continue;
            if (cheapest != int.MaxValue) total += cheapest;
            else
            {
                int balance = Math.Max(0, context.Self.Credits - upfrontCost - total - creditLoss);
                foreach (IceSubroutineSpec subroutine in ice.Subroutines)
                {
                    switch (subroutine)
                    {
                        case LoseCreditsSubroutineSpec loss when loss.Target == PlayerSide.Runner:
                            int lost = Math.Min(balance, loss.Amount);
                            balance -= lost;
                            creditLoss += lost;
                            break;
                        case DamageSubroutineSpec hit:
                            damage += hit.Amount;
                            break;
                        case EndRunSubroutineSpec:
                        case TrashInstalledRunnerCardSubroutineSpec:
                        case RunRestrictionSubroutineSpec:
                            return (total, creditLoss, damage, true, unknown);
                        case EndRunIfCreditsAtMostSubroutineSpec conditional when conditional.Target == PlayerSide.Runner
                            && balance <= conditional.CreditLimit:
                            return (total, creditLoss, damage, true, unknown);
                    }
                }
            }
        }
        return (total, creditLoss, damage, false, unknown);
    }

    /// <summary>
    /// Whether the server's root holds an advanced card the Runner can't identify
    /// whose trap damage, 2 plus its counters (Urtica Cipher), would exceed the grip,
    /// against a Corp the Runner has seen play such traps.
    /// </summary>
    private static bool LethalTrap(HeuristicContext context, AgentServer server, int? grip = null) => SeenTrap(context)
        && UnknownAdvancedRoots(context, server).Any(counters => 2 + counters > (grip ?? context.Features.Hand.Length));

    /// <summary>v21 (A9b): the trap risk of a server's face-down root cards, from the tracker and the prior over the rest.</summary>
    private static int TrackedTrapRisk(HeuristicContext context, AgentServer server, int? gripAfter = null)
    {
        double risk = 0;
        int grip = gripAfter ?? context.Features.Hand.Length;
        for (int index = 0; index < server.Root.Length; index++)
        {
            if (server.Root[index] is not null || context.RememberedRoot(server, index) is not null) continue;
            int counters = context.HiddenAdvancement(server.Id, "root", index);
            (IReadOnlyList<(int Damage, double Probability)> traps, double residual) = RunnerInference.RootTraps(context, server, index, counters);
            foreach ((int damage, double probability) in traps)
                risk += probability * (damage > grip ? 6000 : damage * 100);
            if (counters > 0)
            {
                double share = context.Knowledge?.OpponentList is not null ? RunnerInference.ListTrapShare(context) : SeenTrap(context) ? 0.6 : 0.15;
                risk += residual * share * (2 + counters > grip ? 6000 : (2 + counters) * 100);
            }
            // v24 (bot loop 1): an unadvanced card may be an ambush that flatlines a small grip; the Runner ran an unknown remote
            // with 2 cards into Byte!'s 3 net damage.
            else if (context.Generation >= 20) risk += residual * RunnerInference.LethalAmbushShare(context, grip) * 6000;
        }
        return (int)risk;
    }

    /// <summary>v21 (A9b): whether the tracker puts an even chance or more on accessing a trap that would flatline the Runner.</summary>
    private static bool LikelyLethalTrap(HeuristicContext context, AgentServer server, int? gripAfter = null)
    {
        int grip = gripAfter ?? context.Features.Hand.Length;
        double lethal = 0;
        for (int index = 0; index < server.Root.Length; index++)
        {
            if (server.Root[index] is not null || context.RememberedRoot(server, index) is not null) continue;
            (IReadOnlyList<(int Damage, double Probability)> traps, _) = RunnerInference.RootTraps(context, server, index,
                context.HiddenAdvancement(server.Id, "root", index));
            lethal += traps.Where(trap => trap.Damage > grip).Sum(trap => trap.Probability);
        }
        return lethal >= 0.5;
    }

    /// <summary>Whether the Runner has seen this Corp play a trap that grows with its counters (Urtica Cipher, Clearinghouse).</summary>
    private static bool SeenTrap(HeuristicContext context) => context.Knowledge?.Reveals.Any(reveal =>
        context.Catalog.TryGet(new CardDefinitionId(reveal.DefinitionId), out CardEvaluationDescriptor card)
        && card.Printed.Side == PlayerSide.Corp && card.Ice is null && card.Agenda is null && card.Behavior?.CanBeAdvanced == true
        && card.Behavior.Triggers.SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
            .Any(instruction => instruction.DamageAmountOperand?.Kind == BehaviorDamageAmountKind.SourceAdvancementCounters)) == true;

    /// <summary>
    /// The advancement counters on root cards the Runner can't identify. A
    /// face-down card is a null entry whose public counters come separately
    /// (earlier versions read only visible cards, so they missed face-down traps).
    /// </summary>
    /// <summary>
    /// v13: net damage from accessing the root cards the Runner knows, face up or remembered: an
    /// access trigger's damage, plus the card's counters when it scales with them (Urtica Cipher).
    /// </summary>
    private static int KnownAccessDamage(HeuristicContext context, AgentServer server)
    {
        int total = 0;
        for (int index = 0; index < server.Root.Length; index++)
        {
            // v25 (bot loop 2): the Corp's identity may punish accessing a faceup agenda (BANGUN's 2 meat damage); the Runner
            // accessed one with a card in its grip.
            if (context.Generation >= 21 && server.Root[index] is { } faceUp && context.Definition(faceUp)?.Agenda is not null)
                total += context.Features.Board.CorpIdentity.SelectMany(identity => context.Definition(identity)?.Behavior?.Triggers ?? [])
                    .Where(trigger => trigger.Occurrence == OccurrenceKind.CardAccessed && trigger.Condition == BehaviorTriggerCondition.FaceUpInstalledSubject
                        && trigger.SubjectCardFilter?.CardType == BehaviorCardType.Agenda)
                    .SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
                    .Where(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage).Sum(instruction => instruction.Amount);
            AgentCard? card = server.Root[index] ?? context.RememberedRoot(server, index);
            if (context.Definition(card)?.Behavior is not { } behavior) continue;
            foreach (BehaviorInstruction instruction in behavior.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.CardAccessed)
                .SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions)).Where(instruction => instruction.Kind == BehaviorInstructionKind.DealDamage))
                total += instruction.DamageAmountOperand is { Kind: BehaviorDamageAmountKind.SourceAdvancementCounters } operand
                    ? operand.BaseAmount + (card!.AdvancementCounters ?? 0) : instruction.Amount;
        }
        return total;
    }

    private static IEnumerable<int> UnknownAdvancedRoots(HeuristicContext context, AgentServer server)
    {
        for (int index = 0; index < server.Root.Length; index++)
        {
            if (context.RememberedRoot(server, index) is not null) continue;
            AgentCard? root = server.Root[index];
            int counters = root is null ? context.HiddenAdvancement(server.Id, "root", index)
                : context.Definition(root) is null ? root.AdvancementCounters ?? 0 : 0;
            if (counters > 0) yield return counters;
        }
    }

    /// <summary>v15: a card in the grip that adds nothing installed: a breaker of a type the rig breaks, or a unique card in play.</summary>
    private static bool Redundant(HeuristicContext context, AgentCard card)
    {
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition is null) return false;
        if (definition.Printed.Unique && context.Features.Board.RunnerRig.Any(installed => installed.DefinitionId == card.DefinitionId)) return true;
        return definition.Icebreaker is { } breaker && context.Features.Board.RunnerRig.Any(installed =>
            context.Definition(installed)?.Icebreaker?.InterfaceType == breaker.InterfaceType);
    }

    private static int SuccessRewards(HeuristicContext context, AgentServer server)
    {
        int value = 0;
        bool successful = context.Features.Board.TurnFacts?.Runner.ThisTurn?.SuccessfulRun == true;
        // v15 (reviews of the losses to Codex, 1 October): only for a card worth installing; the discount on a second
        // Carmen sent the Runner into an empty Archives turn after turn.
        if (!successful && context.Self.Clicks >= 2)
            value += context.Features.Hand.Where(card => context.Generation < 11 || !Redundant(context, card)).Sum(card => context.Definition(card)?.Behavior?.CostModifiers?
                .Where(modifier => modifier.Kind == BehaviorCostModifierKind.RunnerInstallCreditCost).Sum(modifier => -modifier.Delta * 170) ?? 0);
        foreach (AgentCard card in context.Features.Board.RunnerRig.Concat(context.Features.Board.RunnerIdentity))
        {
            CardBehaviorDefinition? behavior = context.Definition(card)?.Behavior;
            if (behavior is null) continue;
            value += behavior.Triggers.Where(trigger => trigger.ServerKindRequirement is null || trigger.ServerKindRequirement == server.Kind)
                .Where(trigger => trigger.Occurrence is OccurrenceKind.RunSuccessful or OccurrenceKind.RunEnded)
                .SelectMany(trigger => trigger.Instructions).Sum(instruction => instruction.Kind switch
                {
                    BehaviorInstructionKind.GainCredits => instruction.Amount * 80,
                    BehaviorInstructionKind.AddCounter => instruction.Amount * 70,
                    _ => 0,
                });
            if (server.Kind == ServerKind.Headquarters && context.Features.Board.TurnFacts?.Runner.ThisTurn?.HqBreachOccurrenceServerIds.Length == 0)
                value += (behavior.FirstHqBreachAccessModifiers ?? []).Sum(modifier => modifier.Delta * 140);
        }
        return value;
    }
}
