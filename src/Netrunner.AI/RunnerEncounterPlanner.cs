using System.Numerics;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>Bounded set-cover calculation over the visible encounter, never over engine states.</summary>
internal static class RunnerEncounterPlanner
{
    private sealed record Plan(int Mask, int Credits, int Clicks, int Opportunity, ActionId? First, string? Breaker);
    private sealed record Method(AgentCard? Card, IcebreakerBehaviorSpec? Breaker, int CounterLimit, bool Bioroid);

    public static IReadOnlyDictionary<ActionId, AgentUtility> Evaluate(HeuristicContext context, AgentWorkBudget budget)
    {
        var result = new Dictionary<ActionId, AgentUtility>();
        AgentRun? run = context.Features.Board.Run;
        AgentEncounter? encounter = run?.Encounter;
        IceBehaviorSpec? ice = context.Definition(encounter?.Ice)?.Ice;
        if (run?.Step != RunTimingStep.EncounterPaidWindow || encounter is null || ice is null) return result;
        AgentSubroutine[] remaining = encounter.Subroutines.Where(subroutine => !subroutine.Broken && !subroutine.Resolved).OrderBy(subroutine => subroutine.Ordinal).ToArray();
        if (remaining.Length > 8) return result; // unsupported pool expansion is a conservative fallback
        int all = (1 << remaining.Length) - 1;
        int strength = encounter.StrengthQuote?.EffectiveValue ?? ice.Strength;
        var methods = new List<Method>();
        foreach (AgentCard card in context.Features.Board.RunnerRig.OrderBy(card => card.Id, StringComparer.Ordinal))
        {
            CardEvaluationDescriptor? definition = context.Definition(card);
            if (definition?.Icebreaker is { } breaker && breaker.InterfaceType is var type && (type == IceType.Any || type == ice.IceType))
                methods.Add(new(card, breaker, 0, false));
            else if (definition?.Behavior?.Hosting?.BreakHostSubroutine == true && card.HostCardId == encounter.Ice?.Id)
                methods.Add(new(card, null, card.Counters.Where(counter => counter.Purpose == CardCounterPurpose.Virus).Sum(counter => counter.Count), false));
        }
        if (context.Features.Actions.Any(action => action.Kind == ActionKind.UseBioroidAbility)) methods.Add(new(null, null, 0, true));
        var variants = new List<(int Reduction, int Opportunity, ActionId? First)> { (0, 0, null) };
        foreach (AgentAction action in context.Features.Actions.Where(action => action.Kind == ActionKind.UseAbility))
        {
            var ability = (AbilityActionParameters)action.Parameters;
            AgentCard? source = context.Card(ability.SourceCardId);
            BehaviorPaidAbility? declaration = context.Definition(source)?.Behavior?.PaidAbilities.SingleOrDefault(value => value.Key == ability.AbilityKey);
            if (declaration?.Instructions is not { Count: 1 } instructions || instructions[0].Kind != BehaviorInstructionKind.ModifyEncounteredIceStrength
                || instructions[0].Amount >= 0 || ability.Costs.Count != 1 || ability.Costs[0].Kind != BehaviorCostKind.Counter) continue;
            int available = source?.Counters.Where(counter => counter.CounterName == ability.Costs[0].CounterName).Sum(counter => counter.Count) ?? 0;
            int limit = Math.Min(available / ability.Costs[0].Amount, Math.Max(0, strength));
            for (int count = 1; count <= limit; count++)
            {
                if (!budget.TrySpend()) break;
                variants.Add((-instructions[0].Amount * count, count * ability.Costs[0].Amount * 25, action.Id));
            }
        }
        foreach (var variant in variants)
        {
            var frontier = new List<Plan> { new(0, 0, 0, variant.Opportunity, variant.First, null) };
            foreach (Method method in methods)
            {
                var next = new Dictionary<(int Mask, int Credits, int Clicks, ActionId? First), Plan>();
                foreach (Plan plan in frontier)
                {
                    Keep(plan);
                    int available = all & ~plan.Mask;
                    for (int mask = available; mask > 0; mask = (mask - 1) & available)
                    {
                        if (!budget.TrySpend()) break;
                        int count = BitOperations.PopCount((uint)mask);
                        int credits = 0, clicks = 0, opportunity = 0;
                        AgentAction? first;
                        if (method.Bioroid)
                        {
                            clicks = count;
                            first = context.Features.Actions.FirstOrDefault(action => action.Parameters is RunBioroidAbilityParameters choice
                                && Contains(mask, choice.SubroutineOrdinal));
                        }
                        else if (method.Breaker is { } breaker)
                        {
                            int current = encounter.BreakerStrengths.FirstOrDefault(value => value.CardId == method.Card!.Id)?.StrengthQuote.EffectiveValue ?? breaker.BaseStrength;
                            int increment = RunnerValues.PumpStrength(context, breaker);
                            int deficit = Math.Max(0, strength - variant.Reduction - current);
                            if (deficit > 0 && increment <= 0) continue;
                            int pumps = deficit == 0 ? 0 : (deficit + increment - 1) / increment;
                            int limit = breaker.BreakLimit == 0 ? count : breaker.BreakLimit;
                            credits = pumps * breaker.PumpCost + (count + limit - 1) / limit * RunnerValues.BreakCost(context, method.Card!, breaker);
                            bool disposable = breaker.RunEndCleanup is not null && !run.LingeringEffects.Any(effect =>
                                effect.Kind == RunLingeringEffectKind.IcebreakerRunEndCleanup && effect.SourceCard?.Id == method.Card!.Id);
                            opportunity = disposable ? RunnerValues.Card(context, method.Card) : 0;
                            first = pumps > 0 ? context.Features.Actions.FirstOrDefault(action => action.Parameters is RunBreakerParameters pump && pump.BreakerId.Value == method.Card!.Id)
                                : FirstBreak(method.Card!.Id, mask);
                        }
                        else
                        {
                            if (count > method.CounterLimit) continue;
                            opportunity = count * 25;
                            first = FirstBreak(method.Card!.Id, mask);
                        }
                        int spent = plan.Credits + credits;
                        if (spent > context.Self.Credits + run.RunCredits || plan.Clicks + clicks > context.Self.Clicks) continue;
                        ActionId? initial = plan.First ?? first?.Id;
                        if (initial is null) continue;
                        Keep(new(plan.Mask | mask, spent, plan.Clicks + clicks, plan.Opportunity + opportunity, initial,
                            plan.Breaker ?? method.Card?.Id));
                    }
                }
                frontier = next.Values.ToList();
                if (budget.Exhausted) break;

                void Keep(Plan plan)
                {
                    var key = (plan.Mask, plan.Credits, plan.Clicks, plan.First);
                    if (!next.TryGetValue(key, out Plan? existing) || plan.Opportunity < existing.Opportunity) next[key] = plan;
                }
            }
            foreach (Plan plan in frontier)
            {
                AgentUtility utility = Assess(plan);
                if (plan.First is { } first)
                {
                    if (!result.TryGetValue(first, out AgentUtility? previous) || Better(utility, previous)) result[first] = utility;
                }
                else foreach (AgentAction pass in context.Features.Actions.Where(action => action.Kind == ActionKind.PassPriority)) result[pass.Id] = utility;
            }
            if (budget.Exhausted) break;
        }
        foreach (AgentAction action in context.Features.Actions.Where(action => action.Kind is ActionKind.PumpBreaker or ActionKind.BreakSubroutines or ActionKind.UseBioroidAbility
            || action.Parameters is AbilityActionParameters ability && context.Definition(ability.SourceCardId)?.Behavior?.PaidAbilities
                .Any(value => value.Key == ability.AbilityKey && value.Instructions.Any(instruction => instruction.Kind == BehaviorInstructionKind.ModifyEncounteredIceStrength)) == true))
            result.TryAdd(action.Id, new(-200, 0, "encounter/no-feasible-useful-plan", true));
        if (budget.Exhausted)
            foreach (ActionId id in result.Keys.ToArray()) result[id] = result[id] with { UsedFallback = true, Approximate = true };
        return result;

        bool Contains(int mask, int ordinal) => remaining.Select((subroutine, index) => (subroutine, index))
            .Any(value => value.subroutine.Ordinal == ordinal && (mask & (1 << value.index)) != 0);
        AgentAction? FirstBreak(string breaker, int mask) => context.Features.Actions.Where(action => action.Parameters is RunBreakParameters choice
            && choice.BreakerId.Value == breaker && (choice.SubroutineOrdinals ?? remaining.Take(choice.Quantity).Select(value => value.Ordinal).ToArray()).All(ordinal => Contains(mask, ordinal)))
            .OrderByDescending(action => ((RunBreakParameters)action.Parameters).Quantity).ThenBy(action => action.Id.Value, StringComparer.Ordinal).FirstOrDefault();

        AgentUtility Assess(Plan plan)
        {
            int personal = Math.Max(0, plan.Credits - run.RunCredits);
            int balance = context.Self.Credits - personal;
            int damage = 0, loss = 0, tags = 0;
            bool ends = false, restricted = false;
            foreach (var pair in remaining.Select((subroutine, index) => (subroutine, index)))
            {
                if ((plan.Mask & (1 << pair.index)) != 0 || ends) continue;
                if (pair.subroutine.Ordinal >= ice.Subroutines.Count) return new(-200, 0, "encounter/unknown-subroutine", true, UsedFallback: true);
                switch (ice.Subroutines[pair.subroutine.Ordinal])
                {
                    case EndRunSubroutineSpec: ends = true; break;
                    case EndRunIfCreditsAtMostSubroutineSpec conditional: ends = balance <= conditional.CreditLimit; break;
                    case DamageSubroutineSpec hit: damage += hit.Amount; break;
                    case LoseCreditsSubroutineSpec drain:
                        int lost = Math.Min(balance, drain.Amount); balance -= lost; loss += lost * context.CreditValue; break;
                    case GiveTagsSubroutineSpec tag: tags += tag.Amount; break;
                    case PayCreditsOrTagSubroutineSpec: tags++; break;
                    case TrashInstalledRunnerCardSubroutineSpec trash:
                        int atRisk = context.Features.Board.RunnerRig.Where(card => trash.RequiredCardClass is null || context.Definition(card)?.Printed.CardClass == trash.RequiredCardClass)
                            .Select(card => RunnerValues.Card(context, card)).DefaultIfEmpty(0).Max();
                        if (trash.EndRunAlternative && atRisk > 600) ends = true; else loss += atRisk;
                        break;
                    case RunRestrictionSubroutineSpec: restricted = true; break;
                    case GainCreditsSubroutineSpec gain: loss += gain.Amount * 20; break;
                    case InstallCardSubroutineSpec: loss += 30; break;
                }
            }
            bool lethal = damage > context.Features.Hand.Length;
            bool winning = !ends && !restricted && RunnerValues.Winning(context, RunnerValues.Attacked(context));
            int continuation = ends ? 0 : restricted ? 80 : Math.Max(600, RunnerValues.Server(context, RunnerValues.Attacked(context)));
            int score = continuation - damage * 160 - loss - (tags > 0 ? RunnerValues.TagRisk(context, tags) : 0)
                - personal * context.CreditValue - Math.Min(plan.Credits, run.RunCredits) * 15 - plan.Clicks * 100 - plan.Opportunity;
            return new(lethal ? -100 : winning ? 100 : 0, score, "encounter/complete-plan-and-reserves", true,
                new("encounter", run.Id, encounter.Id, plan.Breaker));
        }
    }

    private static bool Better(AgentUtility first, AgentUtility second) => first.Priority > second.Priority || first.Priority == second.Priority && first.Score > second.Score;
}
