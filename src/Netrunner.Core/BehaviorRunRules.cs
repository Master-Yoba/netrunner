namespace Netrunner.Core;

internal static class BehaviorRunRules
{
    /// <summary>
    /// The success draw for a run event is executed by the normal generic
    /// occurrence/checkpoint pipeline. Keep the binding deliberately
    /// structural: the authenticated generic definition must contain one
    /// mandatory, self-scoped RunSuccessful trigger whose only instruction is
    /// exactly the declared draw. A root Draw instruction is intentionally not
    /// accepted because it would execute at card-play time.
    /// </summary>
    internal static bool IsRunEventSuccessDrawDefinition(
        CardBehaviorDefinition definition,
        BehaviorRunSuccessDraw declaration)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(declaration);
        if (definition.Controller != PlayerSide.Runner
            || definition.Costs.Count != 0
            || definition.Conditions.Count != 0
            || definition.Targets.Count != 0
            || definition.Instructions.Count != 0)
        {
            return false;
        }

        if (definition.Triggers.Count != 1)
        {
            return false;
        }

        BehaviorTrigger trigger = definition.Triggers[0];
        if (!string.Equals(trigger.Key, declaration.ReasonKey, StringComparison.Ordinal)
            || trigger.Occurrence != OccurrenceKind.RunSuccessful
            || trigger.Mode != TriggerMode.Mandatory
            || trigger.Scope != BehaviorTriggerScope.Self
            || trigger.SubjectCardFilter is not null
            || trigger.RunOutcomeRequirement is not null
            || trigger.ServerKindRequirement is not null
            || trigger.RequireSourceOnAttackedServer
            || trigger.Instructions.Count != 1)
        {
            return false;
        }

        BehaviorInstruction instruction = trigger.Instructions[0];
        return instruction.Kind == BehaviorInstructionKind.Draw
            && instruction.Amount == declaration.DrawCount
            && instruction.AmountOperand is null
            && instruction.Target is null
            && instruction.Argument is null;
    }

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

    internal static bool HasFirstHqBreachAccessModifierDeclaration(
        CardBehaviorDefinition definition,
        string reasonKey,
        int delta)
    {
        return definition.FirstHqBreachAccessModifiers?.Any(modifier =>
            modifier.Delta == delta
            && string.Equals(modifier.ReasonKey, reasonKey, StringComparison.Ordinal)) == true;
    }
}
