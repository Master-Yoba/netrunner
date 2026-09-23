using Netrunner.Core;

namespace Netrunner.AI;

internal static class BehaviorChoiceLookup
{
    internal sealed record ChoicePlan(PlayerSide Controller, BehaviorChoiceSpec? Choice, IReadOnlyList<BehaviorInstruction> Consumers,
        BehaviorInstructionKind? Intent = null);

    public static ChoicePlan? Find(ICardEvaluationCatalog catalog, string key)
    {
        foreach (CardDefinitionId id in catalog.DefinitionIds)
        {
            if (!catalog.TryGet(id, out var descriptor) || descriptor.Behavior is not { } behavior) continue;
            foreach (IReadOnlyList<BehaviorInstruction> instructions in new[] { behavior.Instructions }
                .Concat(behavior.Triggers.Select(trigger => trigger.Instructions)).Concat(behavior.PaidAbilities.Select(ability => ability.Instructions)))
                if (Visit(instructions) is { } plan) return plan;

            ChoicePlan? Visit(IReadOnlyList<BehaviorInstruction> instructions)
            {
                for (int index = 0; index < instructions.Count; index++)
                {
                    BehaviorInstruction instruction = instructions[index];
                    if (instruction.Kind == BehaviorInstructionKind.Search && instruction.Argument == key)
                        return new(behavior.Controller, null, [], BehaviorInstructionKind.AddToHand);
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
