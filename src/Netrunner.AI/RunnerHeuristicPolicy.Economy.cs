using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class RunnerHeuristicPolicy
{
    private static AgentUtility Play(HeuristicContext context, PlayCardParameters play)
    {
        AgentCard? card = context.Card(play.CardId);
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition?.RunEvent is { } runEvent)
        {
            // v6: raising ICE rez costs is worth the unrezzed ICE it keeps down (Tread Lightly).
            int rezDelta = context.Generation >= 5 ? (runEvent.RezCostModifiers ?? []).Sum(modifier => modifier.Delta) : 0;
            int kept = rezDelta <= 0 ? 0 : context.Server(play.TargetServerId)?.Ice
                .Count(ice => ice is { Rezzed: not true }) is int unrezzed and > 0
                    ? unrezzed * (context.Opponent.Credits < rezDelta + 4 ? 450 : context.Opponent.Credits < rezDelta + 7 ? 250 : 120) : 0;
            AgentUtility utility = Run(context, context.Server(play.TargetServerId), runEvent.HostedCredits,
                (runEvent.BreachAccessModifier?.Delta ?? 0) * 140 + (runEvent.SuccessDraw?.DrawCount ?? 0) * context.DrawValue + kept,
                upfrontCost: play.CreditCost);
            return utility with { Reason = "run/event-benefit-and-issued-cost" };
        }
        if (definition?.Behavior is not { } behavior) return Value(-50, "play/unmodelled", true, true);
        return Value(Effects(context, behavior.Instructions, card, behavior.Controller) - play.CreditCost * context.CreditValue, "economy/event-net-payoff", true);
    }

    private static AgentUtility Ability(HeuristicContext context, AbilityActionParameters ability)
    {
        AgentCard? card = context.Card(ability.SourceCardId);
        BehaviorPaidAbility? declaration = context.Definition(card)?.Behavior?.PaidAbilities.SingleOrDefault(value => value.Key == ability.AbilityKey);
        if (declaration is null) return Value(-50, "ability/unmodelled", true, true);
        if (declaration.Instructions.FirstOrDefault(instruction => instruction.Kind == BehaviorInstructionKind.StartRun)?.RunOperand is { } run)
        {
            AgentServer? server = context.Features.Board.Servers.FirstOrDefault(server => server.Kind == run.ServerKind);
            int access = (run.BreachAccessDeltaPerCounter ?? 0) * (card?.Counters.Where(counter => counter.CounterName == run.SourceCounterName).Sum(counter => counter.Count) ?? 0);
            int payout = context.Definition(card)?.Behavior?.Triggers.Where(trigger => trigger.Occurrence == OccurrenceKind.RunEnded)
                .SelectMany(trigger => trigger.Instructions).Where(instruction => instruction.Kind == BehaviorInstructionKind.TakeHostedCredits)
                .Sum(instruction => Math.Min(instruction.Amount, HeuristicContext.Hosted(card))) ?? 0;
            int consumables = ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.Counter).Sum(cost => cost.Amount * 50)
                + ability.AdditionalCostSelectionsOrEmpty.SelectMany(selection => selection.CardIds)
                    .Sum(id => RunnerValues.Card(context, context.Card(id)))
                + ability.Costs.Where(cost => cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame)
                    .Sum(_ => RunnerValues.Card(context, card));
            return Run(context, server, extraValue: access * 140 + payout * context.CreditValue
                - (access == 0 && payout == 0 ? 30 : 0) - consumables,
                upfrontCost: ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount));
        }
        int benefit = Effects(context, declaration.Instructions, card, PlayerSide.Runner);
        int cost = ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) * context.CreditValue;
        cost += ability.AdditionalCostSelectionsOrEmpty.SelectMany(selection => selection.CardIds).Sum(id => RunnerValues.Card(context, context.Card(id)));
        cost += ability.Costs.Where(cost => cost.Kind is BehaviorCostKind.TrashSelf or BehaviorCostKind.RemoveSelfFromGame)
            .Sum(_ => RunnerValues.Card(context, card));
        return Value(benefit - cost, "ability/benefit-minus-issued-additional-costs", true);
    }

    private static int Effects(HeuristicContext context, IEnumerable<BehaviorInstruction> instructions, AgentCard? source, PlayerSide controller)
    {
        int total = 0;
        foreach (BehaviorInstruction instruction in instructions)
        {
            bool self = controller == PlayerSide.Runner;
            int sign = self ? 1 : -1;
            int amount = instruction.Amount;
            if (instruction.AmountOperand is { } operand)
                amount = (source?.Counters.Where(counter => counter.CounterName == operand.SourceCounterName).Sum(counter => counter.Count) ?? 0) * operand.Multiplier;
            switch (instruction.Kind)
            {
                case BehaviorInstructionKind.GainCredits: total += sign * amount * context.CreditValue; break;
                // v14 (Codex review, 1 October): Zahya pays a credit a card the ended run accessed; it scored 0 and was declined.
                case BehaviorInstructionKind.GainCreditsPerAccess when context.Generation >= 10:
                    total += sign * (context.Features.Board.RunEnd?.AccessOccurrenceCount ?? 0) * context.CreditValue;
                    break;
                case BehaviorInstructionKind.LoseCredits: total -= sign * amount * context.CreditValue; break;
                case BehaviorInstructionKind.GainClicks: total += sign * amount * 100; break;
                // v16 (reviews of the losses to Codex, 1 October): "if you have any clicks remaining, lose one" costs nothing
                // on the last click and a click before it, so Creative Commission waits for the last click.
                case BehaviorInstructionKind.LoseClicks when context.Generation >= 12 && self
                    && instruction.Condition is { Kind: BehaviorConditionKind.ClicksRemainingAtLeast } remaining:
                    if (context.Self.Clicks - 1 >= (remaining.Value ?? 1)) total -= amount * 200;
                    break;
                case BehaviorInstructionKind.LoseClicks: total -= sign * amount * 100; break;
                case BehaviorInstructionKind.Draw: total += sign * amount * context.DrawValue; break;
                case BehaviorInstructionKind.LoadHostedCredits:
                case BehaviorInstructionKind.PlaceHostedCredits:
                    // v4: a card that already holds a stockpile pays it out slowly (Smartware
                    // Distributor gives 1 a turn), so topping it up is worth far less than face value.
                    total += sign * amount * (context.Generation >= 3 && HeuristicContext.Hosted(source) >= amount ? 12 : 50);
                    break;
                case BehaviorInstructionKind.TakeHostedCredits:
                case BehaviorInstructionKind.TakeUpToHostedCredits: total += sign * Math.Min(amount, HeuristicContext.Hosted(source)) * context.CreditValue; break;
                case BehaviorInstructionKind.TakeAllHostedCredits: total += sign * HeuristicContext.Hosted(source) * context.CreditValue; break;
                case BehaviorInstructionKind.AddCounter: total += sign * amount * 70; break;
                case BehaviorInstructionKind.Search: total += 220; break;
                case BehaviorInstructionKind.AddToHand: total += 180; break;
                case BehaviorInstructionKind.InstallCard: total += 350; break;
                case BehaviorInstructionKind.TrashSelf: total -= RunnerValues.Card(context, source); break;
                case BehaviorInstructionKind.TrashCard:
                    total += context.Features.Board.Run?.Breach.Chosen.Where(card => card is not null).Select(card => RunnerValues.Trash(context, card)).DefaultIfEmpty(0).Max() ?? 0;
                    break;
                case BehaviorInstructionKind.SwapInstalledIce: total += BestSwap(context, []); break;
                case BehaviorInstructionKind.EndRun: total -= Math.Max(300, RunnerValues.Server(context, RunnerValues.Attacked(context))); break;
                case BehaviorInstructionKind.AddTags: total -= RunnerValues.TagRisk(context, amount); break;
                case BehaviorInstructionKind.Choose:
                    total += instruction.Choice is { Options.Count: > 0 } choice
                        ? choice.Options.Max(option => Effects(context, option.Instructions, source, controller) - (option.Costs ?? []).Sum(cost => Cost(context, cost)))
                        : 180;
                    break;
                case BehaviorInstructionKind.Branch:
                    if (instruction.Branch is { } branch)
                    {
                        bool condition = branch.Condition.Kind switch
                        {
                            BehaviorConditionKind.SuccessfulRunThisTurn => context.Features.Board.TurnFacts?.Runner.ThisTurn?.SuccessfulRun == true,
                            BehaviorConditionKind.Tagged => context.Self.Tags > 0,
                            BehaviorConditionKind.NotTagged => context.Self.Tags == 0,
                            _ => true,
                        };
                        total += Effects(context, condition ? branch.WhenTrue : branch.WhenFalse, source, controller);
                    }
                    break;
            }
        }
        return total;
    }

    private static int Cost(HeuristicContext context, BehaviorCost cost) => cost.Kind switch
    {
        BehaviorCostKind.Credits => cost.Amount * context.CreditValue,
        BehaviorCostKind.Clicks or BehaviorCostKind.LoseClick => cost.Amount * 100,
        BehaviorCostKind.Tags => RunnerValues.TagRisk(context, cost.Amount),
        BehaviorCostKind.Counter => cost.Amount * 50,
        _ => 0,
    };
}
