using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class RunnerHeuristicPolicy
{
    private static AgentUtility Run(HeuristicContext context, AgentServer? server, int extraRunCredits = 0, int extraValue = 0,
        int upfrontCost = 0)
    {
        if (server is null) return Value(-1000, "run/unknown-server", true, true);
        (int cost, int loss, int damage, bool blocked, int unknown) = Protection(context, server, server.Ice.Length - 1, upfrontCost);
        int reserve = server.Root.Where(card => RunnerValues.Trash(context, card) >= 350).Select(card => context.Definition(card)?.Printed.TrashCost ?? 0).DefaultIfEmpty(0).Min();
        int funds = context.Self.Credits - upfrontCost + extraRunCredits;
        if (blocked || damage > context.Features.Hand.Length || cost > funds - loss)
            return Value(-1000, "run/known-unaffordable-or-lethal", true);
        int rewards = SuccessRewards(context, server);
        int repeated = context.Features.Board.TurnFacts?.Runner.ThisTurn?.AttemptedRunServerIds.Count(id => id == server.Id) ?? 0;
        int risk = unknown * (context.Self.Credits < 3 || context.Features.Hand.Length < 2 ? 450 : 260) * context.Parameter("risk-scale", 100) / 100;
        int score = RunnerValues.Server(context, server) + rewards + extraValue
            - (upfrontCost + Math.Max(0, cost - extraRunCredits) + loss) * context.CreditValue
            - damage * 160 - risk - repeated * 180 - (funds - cost - loss < reserve ? 220 : 0);
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
        (int cost, int loss, int damage, bool blocked, int unknown) = Protection(context, server, lastIce);
        if (damage > context.Features.Hand.Length || blocked || cost > context.Self.Credits + run.RunCredits - loss)
            return Value(-1000, "run/jack-out-before-unaffordable-threat", true);
        return new(unknown == 0 && RunnerValues.Winning(context, server) ? 100 : 0,
            Math.Max(250, RunnerValues.Server(context, server)) + SuccessRewards(context, server)
            - (Math.Max(0, cost - run.RunCredits) + loss) * context.CreditValue - damage * 160 - unknown * 220,
            "run/continue-visible-affordable-line", true);
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

    private static int SuccessRewards(HeuristicContext context, AgentServer server)
    {
        int value = 0;
        bool successful = context.Features.Board.TurnFacts?.Runner.ThisTurn?.SuccessfulRun == true;
        if (!successful && context.Self.Clicks >= 2)
            value += context.Features.Hand.Sum(card => context.Definition(card)?.Behavior?.CostModifiers?
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
