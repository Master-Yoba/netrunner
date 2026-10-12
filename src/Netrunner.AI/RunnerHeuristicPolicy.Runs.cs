using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class RunnerHeuristicPolicy
{
    /// <param name="knownSuccessValue">v24: value the run pays only if it succeeds, counted when no ICE on the path is unknown.</param>
    /// <param name="gripSpent">v26: cards leaving the grip to make the run (a run event itself).</param>
    private static AgentUtility Run(HeuristicContext context, AgentServer? server, int extraRunCredits = 0, int extraValue = 0,
        int upfrontCost = 0, int knownSuccessValue = 0, int gripSpent = 0)
    {
        if (server is null) return Value(-1000, "run/unknown-server", true, true);
        // v26: the grip the run is made with, once a run event has left it (PR #97's review: every lethal check reads it).
        int grip = context.Features.Hand.Length - gripSpent;
        // v7: the whole-run model (A4a) costs the run as one path, tolls included.
        // v21 (A9b): face-down ICE it's confident of from what it saw is planned as that ICE, which the Corp can rez.
        AgentServer planned = RunnerInference.WithBelievedIce(context, server);
        RunForecast forecast = RunModel.Plan(context, planned, planned.Ice.Length - 1, context.Self.Credits + extraRunCredits,
            context.Self.Clicks - 1, upfrontCost, corpRezBudget: ReferenceEquals(planned, server) ? null : context.Opponent.Credits);
        (int cost, int loss, int damage, bool blocked, int unknown) =
            (forecast.Credits, forecast.CreditsLost, forecast.Damage, !forecast.CanGetIn, forecast.UnknownIce);
        int reserve = server.Root.Where(card => RunnerValues.Trash(context, card) >= 350).Select(card => context.Definition(card)?.Printed.TrashCost ?? 0).DefaultIfEmpty(0).Min();
        int funds = context.Self.Credits - upfrontCost + extraRunCredits;
        if (blocked || damage > grip || cost > funds - loss)
            return Value(-1000, "run/known-unaffordable-or-lethal", true);
        // V5: the run itself spends a click, and what's left must pay a known approach toll; the whole-run model counts its
        // clicks.
        int tollClicks = forecast.Clicks;
        int tagCost = forecast.Tags * 300;
        // v31 (MATCHUP_NEBULA.md NE17, NE25): a tag the turn can still clear costs its clearing (a click and 2 credits);
        // one it can't costs what the Corp's next turn can take for it.
        // The review of #133: with the tags a known agenda gives when it is stolen (Tomorrow's Headline), unless the steal wins.
        int expectedTags = forecast.Tags + (context.Generation == HeuristicContext.TagGeneration
            && !RunnerInference.StealWins(context.Self.AgendaPoints, RunnerInference.SmallestAgenda(context))
                ? RunnerInference.StealTags(context, server) : 0);
        if (context.Generation == HeuristicContext.TagGeneration && expectedTags > 0)
        {
            int tags = context.Self.Tags ?? 0;
            int clearable = Math.Min(expectedTags, Math.Min(Math.Max(0, context.Self.Clicks - 1 - tollClicks),
                Math.Max(0, (funds - cost - loss) / 2)));
            double kept = expectedTags * 180 + RunnerInference.TagThreat(context, tags + expectedTags) - RunnerInference.TagThreat(context, tags);
            double cleared = clearable * (ClickCredits + 2) * context.CreditValue + (expectedTags - clearable) * 180
                + RunnerInference.TagThreat(context, tags + expectedTags - clearable) - RunnerInference.TagThreat(context, tags);
            tagCost = (int)Math.Min(kept, cleared);
        }
        // v6: an advanced card the Runner can't identify may be a trap dealing 2 plus
        // its counters on access (Urtica Cipher); with too few cards in the grip that's a flatline.
        // v21 (A9b): each face-down root card is a trap as likely as the tracker says (an Urtica Cipher seen in HQ, now in a
        // naked remote), and the rest as before: an advanced unknown card in proportion to the Corp playing traps (Hard knows
        // the list).
        // v26 (self-flatlines): with the grip as it is once a run event has left it; Clean Getaway was played into Byte!
        // with the ambush priced against the grip it was still in.
        int trapRisk = TrackedTrapRisk(context, server, grip);
        // v15 (reviews of the losses to Codex, 1 October): with the Corp one agenda from winning, an advanced card in a
        // remote is most likely that agenda. Leaving it loses if it is one, running loses only if it's a trap, so
        // neither the trap's risk nor even a possible flatline keeps the Runner out.
        bool matchPoint = server.Kind == ServerKind.Remote && UnknownAdvancedRoots(context, server).Any()
            && context.Opponent.AgendaPoints >= 4;
        // v26 (self-flatlines): not while the Runner can draw before it runs; it ran a trap with an empty grip and four clicks.
        if (matchPoint && grip <= 1 && context.Self.Clicks >= 2) matchPoint = false;
        // v11 (self-play review, 28 September): a possible trap that would flatline the Runner isn't worth the access.
        if (!matchPoint && LethalTrap(context, server, grip)) return Value(-1000, "run/possible-lethal-trap", true);
        if (!matchPoint && LikelyLethalTrap(context, server, grip)) return Value(-1000, "run/likely-lethal-trap", true);
        // v13 (playtest, 30 September): a known trap's damage is certain (the Runner kept running a known Urtica Cipher).
        int trap = KnownAccessDamage(context, server);
        if (trap > grip) return Value(-1000, "run/known-lethal-access", true);
        damage += trap;
        int rewards = SuccessRewards(context, server);
        // v26: a breaker trashed when the run ends (Mayfly) is spent by the run, as the encounter planner counts it; the Runner
        // started runs through it and then wouldn't spend it, 17 times in one game.
        int disposable = forecast.Crossings.Select(crossing => crossing.Method).Distinct()
            .Select(method => context.Card(new CardInstanceId(method))).OfType<AgentCard>()
            .Where(card => context.Definition(card)?.Icebreaker?.RunEndCleanup is not null).Sum(card => RunnerValues.Card(context, card));
        int repeated = context.Features.Board.TurnFacts?.Runner.ThisTurn?.AttemptedRunServerIds.Count(id => id == server.Id) ?? 0;
        int risk = unknown * (context.Self.Credits < 3 || context.Features.Hand.Length < 2 ? 450 : 260) * context.Parameter("risk-scale", 100) / 100;
        if (unknown > 0)
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
        if (unknown > 0 && context.Features.Hand.Length <= 1 && context.Self.Clicks >= 2) risk += unknown * 250;
        // v29 (bot plan H3, R18): a cushion against the damage the run may meet that the grip left after it can't absorb.
        if (context.Generation >= 24) risk += Cushion(context, server, unknown, grip - damage, context.Self.Clicks - 1 - tollClicks);
        if (unknown == 0) extraValue += knownSuccessValue;
        if (matchPoint)
        {
            trapRisk = Math.Min(trapRisk, 200);
            extraValue += context.Opponent.AgendaPoints >= 5 ? 700 : 400;
        }
        int score = RunnerValues.Server(context, server) + rewards + extraValue
            - (upfrontCost + Math.Max(0, cost - extraRunCredits) + loss) * context.CreditValue
            - damage * 160 - risk - repeated * 180 - (funds - cost - loss < reserve ? 220 : 0)
            - tollClicks * ClickCredits * context.CreditValue - trapRisk - tagCost - disposable;
        // v11: an aggressive deck runs whenever it can afford to; a big rig holds back until its rig covers every ICE type.
        // v15 (reviews of the losses to Codex, 1 October): not on a run that can gain nothing, such as an Archives of
        // operations the Runner has already seen; aggression sent it there turn after turn.
        if ((RunnerValues.Server(context, server) + rewards + extraValue > 0))
        {
            double aggression = context.RunnerProfile.Aggression;
            if (aggression > 0 || !context.RigComplete) score += (int)(aggression * 100);
        }
        bool win = unknown == 0 && RunnerValues.Winning(context, server);
        return new(win ? 100 : 0, score, "run/public-value-protection-and-access-reserve", true, new("run", null, null, null));
    }

    /// <summary>
    /// v29 (bot plan H3, the strategy guide's R18): what the run risks against the damage it may meet beyond what the
    /// run model knows, with the grip <paramref name="gripAfter"/> left once the known damage is taken: unknown ICE that
    /// would deal more than the grip (a flatline) or exactly the grip (no card left), an ambush among HQ's or R&amp;D's cards
    /// (Byte!), and an operation that punishes a successful run (Measured Response) when the clicks left after the run
    /// can't draw the grip up to survive it. A run that leaves a card to spare risks none of it.
    /// </summary>
    private static int Cushion(HeuristicContext context, AgentServer server, int unknown, int gripAfter, int clicksAfter)
    {
        double risk = 0;
        if (unknown > 0)
        {
            (double lethal, double bare) = RunnerInference.UnknownIceDamage(context, gripAfter);
            risk += Exposure(unknown, lethal, bare);
        }
        if (server.Kind is ServerKind.Headquarters or ServerKind.RAndD)
        {
            (double lethal, double bare) = RunnerInference.CentralAmbush(context, gripAfter);
            risk += Exposure(1, lethal, bare);
        }
        (int need, double chance) = RunnerInference.Punishers(context);
        bool evidence = RunnerInference.PunisherEvidence(context);
        if (!evidence) need = 0;
        bool armed = context.Features.Board.TurnFacts?.Runner.ThisTurn?.SuccessfulRun == true;
        bool survivableBefore = !armed || need <= context.Features.Hand.Length + context.Self.Clicks;
        if (need > 0 && survivableBefore && need > gripAfter + Math.Max(0, clicksAfter)) risk += chance * PunishedGame;
        if (context.Generation >= 25)
        {
            // v30 (MATCHUP_PT_UNTAIAN.md PT13, PT15): a non-winning steal fires the Corp's agenda-event damage (Phật).
            double agenda = RunnerInference.AgendaChance(context, server);
            // A winning steal ends the game before the damage; a steal of the Corp's smallest agenda must win for none to count
            // (the review of #132: a one-pointer, Greenmail, takes a Runner on 5 only to 6).
            int steal = RunnerInference.StealWins(context.Self.AgendaPoints, RunnerInference.SmallestAgenda(context)) ? 0
                : RunnerInference.StealDamage(context);
            if (steal > 0) risk += Exposure(1, steal > gripAfter ? agenda : 0, steal == gripAfter ? agenda : 0);
            // v34 (PT16): a steal that raises the threat level arms the Corp's punishing operation for its next turn.
            if (context.Generation == HeuristicContext.FactionGeneration && evidence && need == 0 && agenda > 0
                && !RunnerInference.StealWins(context.Self.AgendaPoints, RunnerInference.SmallestAgenda(context)))
            {
                double arms = RunnerInference.StealArms(context, gripAfter, Math.Max(0, clicksAfter), out double held);
                risk += agenda * arms * held * PunishedGame;
            }
            // v30 (PT03, PT04): a card of damage costs about a basic draw to recover, so the damage the run is expected to
            // deal beyond the clicks left after it is a card the turn can't rebuild: dangerous runs go early in the turn.
            double expected = agenda * steal + unknown * RunnerInference.ExpectedIceDamage(context)
                + (server.Kind is ServerKind.Headquarters or ServerKind.RAndD ? RunnerInference.ExpectedAmbushDamage(context) : 0);
            risk += Math.Max(0, expected - Math.Max(0, clicksAfter)) * RecoveryCard;
        }
        return (int)risk;
    }

    /// <summary>v30 (PT04): a card of damage the turn has no click left to draw back, about a basic draw's worth.</summary>
    private const int RecoveryCard = 125;

    /// <summary>v29: <paramref name="count"/> chances to meet damage that flatlines the Runner or leaves it no card.</summary>
    private static double Exposure(int count, double lethal, double bare) =>
        (1 - Math.Pow(1 - lethal, count)) * LethalExposure + (1 - Math.Pow(1 - bare, count)) * BareExposure;

    /// <summary>v29: a flatline, as the trap risk prices one.</summary>
    private const int LethalExposure = 6000;

    /// <summary>v29: damage that leaves the grip empty: the Runner survives it, and the next damage or punisher does not.</summary>
    private const int BareExposure = 500;

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
        if (run.Step == RunTimingStep.JackOutDecision) lastIce = run.CurrentIceIndex - 1;
        RunForecast forecast = RunModel.Plan(context, server, lastIce, context.Self.Credits + run.RunCredits, context.Self.Clicks);
        (int cost, int loss, int damage, bool blocked, int unknown) =
            (forecast.Credits, forecast.CreditsLost, forecast.Damage, !forecast.CanGetIn, forecast.UnknownIce);
        if (damage > context.Features.Hand.Length || blocked || cost > context.Self.Credits + run.RunCredits - loss)
            return Value(-1000, "run/jack-out-before-unaffordable-threat", true);
        if (LethalTrap(context, server)) return Value(-1000, "run/jack-out-before-possible-lethal-trap", true);
        if (LikelyLethalTrap(context, server)) return Value(-1000, "run/jack-out-before-likely-lethal-trap", true);
        return new(unknown == 0 && RunnerValues.Winning(context, server) ? 100 : 0,
            Math.Max(250, RunnerValues.Server(context, server)) + SuccessRewards(context, server)
            - (Math.Max(0, cost - run.RunCredits) + loss + forecast.Clicks * ClickCredits) * context.CreditValue
            - damage * 160 - unknown * 220 - forecast.Tags * 300,
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
            else risk += residual * RunnerInference.LethalAmbushShare(context, grip) * 6000;
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
            if (server.Root[index] is { } faceUp && context.Definition(faceUp)?.Agenda is not null)
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
            value += context.Features.Hand.Where(card => !Redundant(context, card)).Sum(card => context.Definition(card)?.Behavior?.CostModifiers?
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
