using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class CorpHeuristicPolicy
{
    private static AgentUtility Play(HeuristicContext context, PlayCardParameters play)
    {
        AgentCard? card = context.Card(play.CardId);
        CardBehaviorDefinition? behavior = context.Definition(card)?.Behavior;
        if (behavior is null) return Value(-50, "play/unmodelled", approximate: true, fallback: true);
        int effect = Effects(context, behavior.Instructions, card, behavior.Controller);
        return new(effect >= 10000 ? 100 : 0, effect - play.CreditCost * context.CreditValue,
            "play/effect-minus-current-cost", Approximate: effect < 10000);
    }

    private static AgentUtility Ability(HeuristicContext context, AbilityActionParameters ability)
    {
        AgentCard? card = context.Card(ability.SourceCardId);
        BehaviorPaidAbility? declaration = context.Definition(card)?.Behavior?.PaidAbilities.SingleOrDefault(value => value.Key == ability.AbilityKey);
        if (declaration is null) return Value(-50, "ability/unmodelled", approximate: true, fallback: true);
        int benefit = Effects(context, declaration.Instructions, card, context.Features.Side);
        int costs = ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) * context.CreditValue;
        costs += ability.Costs.Where(cost => cost.Kind == BehaviorCostKind.TrashSelf).Sum(_ => context.CardValue(card, installed: true));
        return Value(benefit - costs, "ability/payoff-minus-cost", approximate: true);
    }

    private static int Effects(HeuristicContext context, IEnumerable<BehaviorInstruction> instructions, AgentCard? source, PlayerSide controller)
    {
        int total = 0;
        foreach (BehaviorInstruction instruction in instructions)
        {
            bool self = controller == context.Features.Side;
            int sign = self ? 1 : -1;
            int amount = instruction.Amount;
            if (instruction.AmountOperand is { } operand)
                amount = (source?.Counters.Where(counter => counter.CounterName == operand.SourceCounterName).Sum(counter => counter.Count) ?? 0) * operand.Multiplier;
            switch (instruction.Kind)
            {
                case BehaviorInstructionKind.GainCredits: total += sign * amount * (self ? context.CreditValue : 100); break;
                case BehaviorInstructionKind.LoseCredits: total -= sign * amount * context.CreditValue; break;
                case BehaviorInstructionKind.GainClicks: total += sign * amount * 100; break;
                case BehaviorInstructionKind.Draw:
                    if (controller == PlayerSide.Corp && amount >= context.Features.Board.HiddenZoneCounts.CorpDeck) return -20000;
                    total += sign * amount * (self ? context.DrawValue : context.Features.Board.HiddenZoneCounts.RunnerGrip < 3 ? 140 : 75);
                    break;
                case BehaviorInstructionKind.TakeHostedCredits:
                case BehaviorInstructionKind.TakeUpToHostedCredits:
                    total += sign * Math.Min(amount, HeuristicContext.Hosted(source)) * context.CreditValue; break;
                case BehaviorInstructionKind.TakeAllHostedCredits:
                    total += sign * HeuristicContext.Hosted(source) * context.CreditValue; break;
                case BehaviorInstructionKind.PlaceHostedCredits:
                case BehaviorInstructionKind.LoadHostedCredits: total += sign * amount * 40; break;
                case BehaviorInstructionKind.DealDamage:
                    amount = instruction.DamageAmountOperand switch
                    {
                        { Kind: BehaviorDamageAmountKind.ScoredAgendaPrintedPointsThisTurn } => context.Features.Board.TurnFacts?.Corp.ThisTurn?.AgendaTurnChanges
                            .Where(change => change.Kind == AgendaTurnChangeKind.Score).Sum(change => change.Points) ?? 0,
                        { Kind: BehaviorDamageAmountKind.SourceAdvancementCounters } damageOperand => damageOperand.BaseAmount + (source?.AdvancementCounters ?? 0),
                        _ => amount,
                    };
                    total += amount > context.Features.Board.HiddenZoneCounts.RunnerGrip ? 10000 : amount * 100;
                    break;
                case BehaviorInstructionKind.AddTags:
                    total += context.Features.Board.RunnerRig.Where(card => context.Definition(card)?.Printed.CardClass == RuntimeCardClass.Resource)
                        .Select(card => context.CardValue(card, installed: true)).DefaultIfEmpty(0).Max() / 3;
                    break;
                case BehaviorInstructionKind.Choose:
                    if (instruction.Choice is { Options.Count: > 0 } choice)
                        total += choice.Options.Max(option => Effects(context, option.Instructions, source, controller)
                            - (option.Costs ?? []).Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) * context.CreditValue);
                    else total += 100;
                    break;
                case BehaviorInstructionKind.Branch:
                    if (instruction.Branch is { } branch)
                    {
                        bool condition = branch.Condition.Kind switch
                        {
                            BehaviorConditionKind.Tagged => context.Features.Board.Players.Runner.Tags > 0,
                            BehaviorConditionKind.NotTagged => context.Features.Board.Players.Runner.Tags == 0,
                            BehaviorConditionKind.SuccessfulRunThisTurn => context.Features.Board.TurnFacts?.Runner.ThisTurn?.SuccessfulRun == true,
                            _ => true,
                        };
                        total += Effects(context, condition ? branch.WhenTrue : branch.WhenFalse, source, controller);
                    }
                    break;
                case BehaviorInstructionKind.EndRun:
                    AgentServer? attacked = context.Features.Board.Servers.FirstOrDefault(server => server.Id == context.Features.Board.Run?.AttackedServer);
                    total += context.ServerValue(attacked);
                    if (attacked?.Root.Any(card => context.Definition(card)?.Agenda is { } agenda
                        && context.Opponent.AgendaPoints + agenda.AgendaPoints >= 7) == true) total += 5000;
                    break;
                case BehaviorInstructionKind.AdvanceCard: total += amount * 110; break;
                case BehaviorInstructionKind.RezIgnoringCosts: total += 300; break;
                case BehaviorInstructionKind.Search: total += 180; break;
                case BehaviorInstructionKind.TrashSelf: total -= context.CardValue(source, installed: true); break;
            }
        }
        return total;
    }
}
