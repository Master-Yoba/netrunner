namespace Netrunner.Core;

internal static class BehaviorRunRules
{
    internal static bool TryGetFinalStartRun(
        BehaviorPaidAbility ability,
        out BehaviorRunOperand operand)
    {
        operand = null!;
        if (ability.Activation != BehaviorAbilityActivation.ClickAction
            || ability.Timing != BehaviorAbilityTiming.ActionPhase
            || ability.Instructions.Count == 0)
        {
            return false;
        }

        BehaviorInstruction final = ability.Instructions[^1];
        if (final.Kind != BehaviorInstructionKind.StartRun
            || final.RunOperand is not { } declared)
        {
            return false;
        }

        // The catalog constructor enforces this for ordinary declarations;
        // retain the check here for deserialized/forged definitions supplied
        // through an injected catalog.
        if (ability.Instructions.Take(ability.Instructions.Count - 1)
            .Any(static instruction => instruction.Kind == BehaviorInstructionKind.StartRun))
        {
            return false;
        }

        operand = declared;
        return true;
    }

    internal static bool DefinitionHasStartRunForServer(
        CardBehaviorDefinition definition,
        ServerKind serverKind)
    {
        return definition.PaidAbilities.Any(ability =>
            TryGetFinalStartRun(ability, out BehaviorRunOperand operand)
            && operand.ServerKind == serverKind);
    }

    internal static bool HasBreachAccessModifierDeclaration(
        CardBehaviorDefinition definition,
        ServerKind serverKind,
        string reasonKey,
        int delta)
    {
        return definition.PaidAbilities.Any(ability =>
            TryGetFinalStartRun(ability, out BehaviorRunOperand operand)
            && operand.ServerKind == serverKind
            && operand.BreachAccessDeltaPerCounter is { } slope
            && string.Equals(operand.BreachAccessReasonKey, reasonKey, StringComparison.Ordinal)
            && delta % slope == 0
            && delta / slope > 0);
    }
}
