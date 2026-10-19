using System.Numerics;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>Bounded set-cover calculation over the visible encounter, never over engine states.</summary>
internal static class RunnerEncounterPlanner
{
    /// <summary>VP.3: the hosted credits on installed Runner cards with the subtype, which a cost restricted to it may spend.</summary>
    internal static int RestrictedCredits(HeuristicContext context, string subtype) =>
        context.Features.Board.RunnerRig.Where(card => context.Definition(card)?.Printed.Subtypes.Contains(subtype, StringComparer.Ordinal) == true)
            .Sum(card => card.Counters.Where(counter => counter.Purpose == CardCounterPurpose.HostedCredit).Sum(counter => counter.Count));

    private sealed record Plan(int Mask, int Credits, int Clicks, int Opportunity, ActionId? First, string? Breaker);
    private sealed record Method(AgentCard? Card, IcebreakerBehaviorSpec? Breaker, int CounterLimit, bool Bioroid);

    public static IReadOnlyDictionary<ActionId, AgentUtility> Evaluate(HeuristicContext context, AgentWorkBudget budget)
    {
        var result = new Dictionary<ActionId, AgentUtility>();
        AgentRun? run = context.Features.Board.Run;
        AgentEncounter? encounter = run?.Encounter;
        IceBehaviorSpec? ice = context.Definition(encounter?.Ice)?.Ice;
        if (run?.Step != RunTimingStep.EncounterPaidWindow || encounter is null || ice is null) return result;
        AgentSubroutine[] remaining = encounter.Subroutines.Where(subroutine => !subroutine.Broken && !subroutine.Resolved)
            // VP-ICE-GAINS-SUBROUTINE: a gained subroutine resolves before the printed ones, though its ordinal follows them.
            .OrderBy(subroutine => subroutine.GainedFrom is null).ThenBy(subroutine => subroutine.Ordinal).ToArray();
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
            // VP.3 (misplay sweep): or with credits only from cards of a subtype (Corsair's stealth credits), which pay little
            // else; that cost was never planned, so Corsair never lowered a barrier.
            bool counterCost = ability.Costs is [{ Kind: BehaviorCostKind.Counter }];
            bool restrictedCredits = ability.Costs is [{ Kind: BehaviorCostKind.Credits, CreditSourceSubtype: not null }];
            if (declaration?.Instructions is not { Count: 1 } instructions || instructions[0].Kind != BehaviorInstructionKind.ModifyEncounteredIceStrength
                || instructions[0].Amount >= 0 || !(counterCost || restrictedCredits)) continue;
            int available = counterCost
                ? source?.Counters.Where(counter => counter.CounterName == ability.Costs[0].CounterName).Sum(counter => counter.Count) ?? 0
                : RestrictedCredits(context, ability.Costs[0].CreditSourceSubtype!);
            int limit = Math.Min(available / ability.Costs[0].Amount, Math.Max(0, strength));
            for (int count = 1; count <= limit; count++)
            {
                if (!budget.TrySpend()) break;
                variants.Add((-instructions[0].Amount * count, count * ability.Costs[0].Amount * (counterCost ? 25 : 60), action.Id));
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
                            // VP-BIOROID-MULTI-BREAK: a use may break two of the subroutines (Ansel 2.0); that use goes first.
                            first = context.Features.Actions.Where(action => action.Parameters is RunBioroidAbilityParameters choice
                                    && Contains(mask, choice.SubroutineOrdinal)
                                    && (choice.SecondSubroutineOrdinal is not { } second || Contains(mask, second)))
                                .OrderBy(static action => ((RunBioroidAbilityParameters)action.Parameters).SecondSubroutineId is null)
                                .FirstOrDefault();
                            // v24 (bot loop 1): an ability may cost credits instead of a click (N-Pot's 3 credits a subroutine);
                            // as clicks, the Runner with none left saw no way through and passed.
                            if (context.ReadsBehaviorSubroutines && first?.Parameters is RunBioroidAbilityParameters { Cost: { Kind: BehaviorCostKind.Credits } price })
                                credits = count * price.Amount;
                            else if (first?.Parameters is RunBioroidAbilityParameters { Cost.Amount: > 1 } multi)
                            {
                                int perUse = context.Features.Actions.Any(action => action.Parameters is RunBioroidAbilityParameters pair
                                    && pair.AbilityKey == multi.AbilityKey && pair.SecondSubroutineId is not null) ? 2 : 1;
                                clicks = (count + perUse - 1) / perUse * multi.Cost.Amount;
                            }
                            else clicks = count;
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
            // The grip as the subroutines resolve, in order: a hit larger than it flatlines the Runner, and a draw after a
            // hit (Stick and Poke's gained subroutine, VP-ICE-GAINS-SUBROUTINE) refills it before the next.
            int grip = context.Features.Hand.Length, stack = context.Features.Board.HiddenZoneCounts.RunnerStack;
            bool ends = false, restricted = false, lethal = false;
            foreach (var pair in remaining.Select((subroutine, index) => (subroutine, index)))
            {
                if ((plan.Mask & (1 << pair.index)) != 0 || ends) continue;
                IceSubroutineSpec? declared = pair.subroutine.GainedFrom is { } source
                    ? (context.Catalog.TryGet(new(source), out var giver) ? giver.Behavior?.GainedIceSubroutine : null)
                    : pair.subroutine.Ordinal < ice.Subroutines.Count ? ice.Subroutines[pair.subroutine.Ordinal] : null;
                if (declared is null) return new(-200, 0, "encounter/unknown-subroutine", true, UsedFallback: true);
                switch (declared)
                {
                    case EndRunSubroutineSpec: ends = true; break;
                    case EndRunIfCreditsAtMostSubroutineSpec conditional: ends = balance <= conditional.CreditLimit; break;
                    case DamageSubroutineSpec hit:
                        damage += hit.Amount;
                        lethal |= hit.Amount > grip;
                        grip -= hit.Amount;
                        if (hit.FollowUp == DamageSubroutineFollowUp.RunnerDrawsOne && stack > 0 && !lethal) { grip++; stack--; }
                        break;
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
                    // v24 (bot loop 1): an Elevation subroutine written as a behavior does what it does (N-Pot's end the run
                    // at threat 2 and 4, Lamplighter's on a tagged Runner); as nothing, the Runner broke one and passed.
                    case BehaviorSubroutineSpec when context.ReadsBehaviorSubroutines:
                        SubroutineEffect effect = SubroutineEffect.Of(declared, context, encounter.Ice, tags,
                            context.Features.Hand.Length);
                        ends |= effect.EndsRun;
                        damage += effect.Damage;
                        lethal |= effect.Damage > grip;
                        grip -= effect.Damage;
                        tags += effect.Tags;
                        int drained = Math.Min(balance, effect.CreditsLost); balance -= drained; loss += drained * context.CreditValue;
                        if (effect.InstallsIce) loss += 30;
                        break;
                }
            }
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
