using Netrunner.Core;

namespace Netrunner.AI;

public sealed partial class CorpHeuristicPolicy
{
    private static AgentUtility Trigger(HeuristicContext context, TriggerId id, bool use)
    {
        if (!use) return Value(0, "trigger/decline");
        AgentTrigger? offered = context.Features.PrivateCards.Triggers.SingleOrDefault(trigger => trigger.Id == id.Value);
        BehaviorTrigger? declaration = offered is null ? null : context.Definition(offered.Source)?.Behavior?.Triggers.SingleOrDefault(trigger => trigger.Key == offered.Key);
        if (declaration is null) return Value(-10, "trigger/unknown-context", approximate: true, fallback: true);
        return Value(10 + Effects(context, declaration.Instructions, offered!.Source, context.Features.Side), "trigger/typed-benefit", approximate: true);
    }

    private static AgentUtility Subroutine(HeuristicContext context, IceSubroutineChoiceParameters choice)
    {
        if (choice.Choice == IceSubroutineChoiceKind.Decline) return Value(0, "subroutine/decline");
        if (choice.Choice == IceSubroutineChoiceKind.TrashInstalledRunnerCard && choice.CardId is { } target)
            return Value(context.CardValue(context.Card(target), installed: true), "subroutine/remove-coverage", approximate: true);
        if (choice.Choice == IceSubroutineChoiceKind.InstallCard && choice.CardId is { } incoming)
        {
            CardEvaluationDescriptor? definition = context.Definition(incoming);
            AgentServer? server = context.Server(choice.ServerId);
            bool exposingWinningHq = context.Opponent.AgendaPoints >= 5 && context.AgendaCount > 0
                && context.Features.Board.Run?.AttackedServer == context.Features.Board.Servers.Single(value => value.Kind == ServerKind.Headquarters).Id
                && definition?.Ice is { } ice && ice.RezCost > context.Self.Credits;
            int value = exposingWinningHq ? -1000 : context.ServerValue(server) / ((server?.Ice.Length ?? 0) + 1)
                - choice.CreditCost * context.CreditValue - Math.Max(0, (definition?.Printed.RezCost ?? 0) - context.Self.Credits) * 100;
            return Value(value, "subroutine/install-protection-and-reserve", approximate: true);
        }
        return Value(0, "subroutine/issued-continuation", approximate: true);
    }

    private static AgentUtility ChooseBehavior(HeuristicContext context, BehaviorChoiceParameters choice)
    {
        ChoicePlan? plan = FindChoice(context, choice.ChoiceKey);
        if (plan is null) return Value(choice.FinalizeSelection ? 1 : 0, "choice/unmodelled-finalize", approximate: true, fallback: true);
        if (choice.OptionKeys.Count > 0)
        {
            int value = choice.OptionKeys.Sum(key =>
            {
                BehaviorChoiceOption option = plan.Choice!.Options.Single(candidate => candidate.Key == key);
                return Effects(context, option.Instructions, null, plan.Controller)
                    - (option.Costs ?? []).Where(cost => cost.Kind == BehaviorCostKind.Credits).Sum(cost => cost.Amount) * context.CreditValue;
            });
            value -= choice.CardIds.Sum(id => context.CardValue(context.Card(id)));
            return Value(value, "choice/mode-and-additional-cost", approximate: true);
        }
        BehaviorInstructionKind intent = plan.Consumers.FirstOrDefault(instruction => instruction.Kind is BehaviorInstructionKind.TrashCard
            or BehaviorInstructionKind.AdvanceCard or BehaviorInstructionKind.RezIgnoringCosts or BehaviorInstructionKind.AddToHand
            or BehaviorInstructionKind.ShuffleIntoDeck)?.Kind ?? BehaviorInstructionKind.AddToHand;
        int score = choice.CardIds.Sum(id => TargetValue(context, id, intent));
        return Value(score + (choice.FinalizeSelection ? 1 : 0), "choice/target-value-and-confirmation", approximate: true);
    }

    private static int TargetValue(HeuristicContext context, CardInstanceId id, BehaviorInstructionKind intent)
    {
        AgentCard? card = context.Card(id);
        CardEvaluationDescriptor? definition = context.Definition(card);
        return intent switch
        {
            BehaviorInstructionKind.TrashCard => -context.CardValue(card),
            BehaviorInstructionKind.AdvanceCard => definition?.Agenda is not null ? 300 + Math.Min(2, Math.Max(0,
                definition.Printed.AdvancementRequirement - (card?.AdvancementCounters ?? 0))) * 100 : 10,
            BehaviorInstructionKind.RezIgnoringCosts => context.ServerValue(context.ServerOf(id.Value)) + (definition?.Printed.RezCost ?? 0) * 80,
            BehaviorInstructionKind.ShuffleIntoDeck => context.CardValue(card) + (definition?.Agenda is not null ? 200 : 0),
            _ => context.CardValue(card),
        };
    }

    private sealed record ChoicePlan(PlayerSide Controller, BehaviorChoiceSpec? Choice, IReadOnlyList<BehaviorInstruction> Consumers);

    private static ChoicePlan? FindChoice(HeuristicContext context, string key)
    {
        foreach (CardDefinitionId id in context.Catalog.DefinitionIds)
        {
            if (!context.Catalog.TryGet(id, out var descriptor) || descriptor.Behavior is not { } behavior) continue;
            foreach (IReadOnlyList<BehaviorInstruction> instructions in new[] { behavior.Instructions }
                .Concat(behavior.Triggers.Select(trigger => trigger.Instructions)).Concat(behavior.PaidAbilities.Select(ability => ability.Instructions)))
                if (Visit(instructions) is { } plan) return plan;

            ChoicePlan? Visit(IReadOnlyList<BehaviorInstruction> instructions)
            {
                for (int index = 0; index < instructions.Count; index++)
                {
                    BehaviorInstruction instruction = instructions[index];
                    if (instruction.Kind == BehaviorInstructionKind.Search && instruction.Argument == key)
                        return new(behavior.Controller, null, [new(BehaviorInstructionKind.AddToHand)]);
                    if (instruction.Choice is { } choice)
                    {
                        if (choice.Key == key) return new(behavior.Controller, choice, instructions.Skip(index + 1).ToArray());
                        foreach (BehaviorChoiceOption option in choice.Options) if (Visit(option.Instructions) is { } nested) return nested;
                    }
                    if (instruction.Branch is { } branch)
                    {
                        ChoicePlan? nested = Visit(branch.WhenTrue) ?? Visit(branch.WhenFalse);
                        if (nested is not null) return nested;
                    }
                }
                return null;
            }
        }
        return null;
    }
}
