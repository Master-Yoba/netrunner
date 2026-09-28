using System.Collections.Immutable;
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
        BehaviorTarget? chosenTarget = null;
        foreach (BehaviorInstruction instruction in instructions)
        {
            if (instruction.Kind == BehaviorInstructionKind.Choose && instruction.Choice?.Target is { } target) chosenTarget = target;
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
                    total += amount > context.Features.Board.HiddenZoneCounts.RunnerGrip ? 10000
                        : (int)(amount * 100 * (context.Generation >= 7 ? context.CorpProfile.DamageWeight : 1));
                    break;
                case BehaviorInstructionKind.AddTags when context.Generation >= 4:
                    // v7: clearing a tag costs the Runner a click and 2 credits, and
                    // while tagged its resources can be trashed (Wave 4 C1 audit).
                    // v12: a tag deck's punishments make each tag worth more.
                    total += (int)(amount * (250 + context.Features.Board.RunnerRig
                        .Where(card => context.Definition(card)?.Printed.CardClass == RuntimeCardClass.Resource)
                        .Select(card => context.CardValue(card, installed: true)).DefaultIfEmpty(0).Max() / 2)
                        * (context.Generation >= 7 ? context.CorpProfile.TagWeight : 1));
                    break;
                case BehaviorInstructionKind.AddTags:
                    total += context.Features.Board.RunnerRig.Where(card => context.Definition(card)?.Printed.CardClass == RuntimeCardClass.Resource)
                        .Select(card => context.CardValue(card, installed: true)).DefaultIfEmpty(0).Max() / 3;
                    break;
                case BehaviorInstructionKind.TrashCard when context.Generation >= 4 && self
                    && instruction.Target is { Kind: BehaviorTargetKind.ChosenCard }:
                    // v7: a trash effect is worth its best legal Runner target (Retribution).
                    total += context.Features.Board.RunnerRig.Where(card => MatchesTarget(context, card, chosenTarget))
                        .Select(card => context.CardValue(card, installed: true)).DefaultIfEmpty(0).Max();
                    break;
                case BehaviorInstructionKind.Choose when context.Generation >= 3 && instruction.Choice is { Options.Count: 0 }:
                    break; // A card-target choice; the following instructions value the best target.
                case BehaviorInstructionKind.Choose when context.Generation >= 4
                    && instruction.Choice is { Options.Count: > 0 } runnerChoice
                    && ChooserSide(runnerChoice.Chooser, controller) != context.Features.Side:
                    // v7: the opponent picks the option least good for us, and the
                    // credits it pays are ours to value (Public Trail: a tag or 8 credits).
                    total += runnerChoice.Options.Min(option => Effects(context, option.Instructions, source, controller)
                        + (option.Costs ?? []).Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) * 100);
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
                case BehaviorInstructionKind.AdvanceCard when context.Generation >= 3:
                    // Playing the card spends a click before the counters land.
                    total += AdvanceTargets(context).Select(target => AdvanceTargetValue(context, target, amount, context.Self.Clicks - 1))
                        .DefaultIfEmpty(0).Max();
                    break;
                case BehaviorInstructionKind.AdvanceCard: total += amount * 110; break;
                case BehaviorInstructionKind.RezIgnoringCosts: total += 300; break;
                case BehaviorInstructionKind.Search: total += 180; break;
                case BehaviorInstructionKind.TrashSelf: total -= context.CardValue(source, installed: true); break;
            }
        }
        return total;
    }

    /// <summary>Which side makes a behavior choice.</summary>
    private static PlayerSide ChooserSide(BehaviorChooser chooser, PlayerSide controller) => chooser switch
    {
        BehaviorChooser.Corp => PlayerSide.Corp,
        BehaviorChooser.Runner => PlayerSide.Runner,
        BehaviorChooser.Opponent => controller == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp,
        _ => controller,
    };

    /// <summary>Whether an installed Runner card fits a behavior target's card filter.</summary>
    private static bool MatchesTarget(HeuristicContext context, AgentCard card, BehaviorTarget? target)
    {
        if (target?.Filter is not { } filter) return true;
        RuntimeCardClass? cardClass = context.Definition(card)?.Printed.CardClass;
        return filter.CardType switch
        {
            BehaviorCardType.Any => true,
            BehaviorCardType.Hardware => cardClass == RuntimeCardClass.Hardware,
            BehaviorCardType.Resource => cardClass == RuntimeCardClass.Resource,
            BehaviorCardType.Icebreaker => context.Definition(card)?.Icebreaker is not null,
            BehaviorCardType.ProgramOrHardware => cardClass is RuntimeCardClass.Program or RuntimeCardClass.Hardware,
            _ => cardClass == RuntimeCardClass.Program,
        };
    }

    /// <summary>Installed Corp cards an effect can advance: installed before this turn.</summary>
    private static IEnumerable<AgentCard> AdvanceTargets(HeuristicContext context)
    {
        ImmutableArray<string> thisTurn = context.Features.Board.TurnFacts?.Corp.ThisTurn?.InstalledCardIds ?? [];
        return context.Features.Board.Servers.SelectMany(server => server.Root.Concat(server.Ice)).OfType<AgentCard>()
            .Select(card => context.Card(new CardInstanceId(card.Id)) ?? card)
            .Where(card => !thisTurn.Contains(card.Id));
    }

    /// <summary>
    /// What <paramref name="amount"/> counters on <paramref name="card"/> are worth: most when they
    /// complete an agenda or let the remaining clicks finish it, some as progress, nothing on
    /// cards that don't use counters.
    /// </summary>
    private static int AdvanceTargetValue(HeuristicContext context, AgentCard? card, int amount, int clicksAfter)
    {
        CardEvaluationDescriptor? definition = context.Definition(card);
        if (definition is null || card is null) return 0;
        if (definition.Agenda is not null)
        {
            int remaining = definition.Printed.AdvancementRequirement - (card.AdvancementCounters ?? 0) - amount;
            int points = definition.Printed.AgendaPoints;
            if (remaining <= 0) return 700 + points * 60;
            if (remaining <= Math.Min(clicksAfter, context.Self.Credits)) return 450 + points * 50;
            return amount * 110;
        }
        bool scalesWithCounters = definition.Behavior?.Triggers.SelectMany(trigger => HeuristicContext.Flatten(trigger.Instructions))
            .Any(instruction => instruction.DamageAmountOperand?.Kind == BehaviorDamageAmountKind.SourceAdvancementCounters) == true;
        return scalesWithCounters ? amount * 90 : 0;
    }
}
