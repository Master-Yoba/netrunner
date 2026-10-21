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

    /// <summary>
    /// The instructions a run modifier's reason key names on its source: the keyed trigger's or paid ability's, or, under
    /// <see cref="GameEngine.CardBehaviorRestrictionReason"/>, the card's own; nested choices and branches included. The
    /// validator authenticates run restrictions and all-ICE strength changes against them (PR #154's review).
    /// </summary>
    internal static IEnumerable<BehaviorInstruction> InstructionsUnder(CardBehaviorDefinition definition, string reasonKey) =>
        Flatten(definition.Triggers.Where(trigger => string.Equals(trigger.Key, reasonKey, StringComparison.Ordinal))
                .SelectMany(static trigger => trigger.Instructions)
            .Concat(definition.PaidAbilities.Where(ability => string.Equals(ability.Key, reasonKey, StringComparison.Ordinal))
                .SelectMany(static ability => ability.Instructions))
            .Concat(string.Equals(reasonKey, GameEngine.CardBehaviorRestrictionReason, StringComparison.Ordinal)
                ? definition.Instructions
                : []));

    /// <summary>Every instruction in the list, with those nested in branches and choice options.</summary>
    internal static IEnumerable<BehaviorInstruction> FlattenInstructions(IEnumerable<BehaviorInstruction> instructions) => Flatten(instructions);

    private static IEnumerable<BehaviorInstruction> Flatten(IEnumerable<BehaviorInstruction> instructions)
    {
        foreach (BehaviorInstruction instruction in instructions)
        {
            yield return instruction;
            foreach (BehaviorInstruction nested in Flatten((instruction.Choice?.Options ?? []).SelectMany(static option => option.Instructions)
                .Concat(instruction.Branch is { } branch ? branch.WhenTrue.Concat(branch.WhenFalse) : [])))
            {
                yield return nested;
            }
        }
    }

    /// <summary>VP-RESOLVE-SUBROUTINE: some ability, trigger or play of the definition holds the instruction kind.</summary>
    internal static bool DeclaresInstruction(CardBehaviorDefinition definition, BehaviorInstructionKind kind) =>
        Flatten(definition.Instructions
                .Concat(definition.Triggers.SelectMany(static trigger => trigger.Instructions))
                .Concat(definition.PaidAbilities.SelectMany(static ability => ability.Instructions)))
            .Any(instruction => instruction.Kind == kind);

    /// <summary>
    /// VP-PAID-REDIRECT: the definition's would-approach reaction may change the attacked server to the kind (Baker's
    /// click run on Archives may become a run on HQ or R&amp;D).
    /// </summary>
    internal static bool DefinitionRedirectsTo(CardBehaviorDefinition definition, ServerKind serverKind) =>
        Flatten(definition.Triggers.Where(static trigger => trigger.Occurrence == OccurrenceKind.ServerWouldBeApproached)
                .SelectMany(static trigger => trigger.Instructions))
            .Any(instruction => instruction.Kind == BehaviorInstructionKind.ChangeAttackedServer
                && string.Equals(instruction.Argument, serverKind.ToString(), StringComparison.Ordinal));

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

    /// <summary>
    /// Whether a definition declares an <see cref="BehaviorInstructionKind.AddBreachAccess"/>
    /// instruction with this reason and amount anywhere in its abilities or triggers.
    /// </summary>
    internal static bool HasBehaviorBreachAccessDeclaration(
        CardBehaviorDefinition definition,
        string reasonKey,
        int delta)
    {
        return definition.Instructions
            .Concat(definition.Triggers.SelectMany(static trigger => trigger.Instructions))
            .Concat(definition.PaidAbilities.SelectMany(static ability => ability.Instructions))
            .Any(instruction => DeclaresBreachAccess(instruction, reasonKey, delta));
    }

    private static bool DeclaresBreachAccess(BehaviorInstruction instruction, string reasonKey, int delta) =>
        instruction.Kind == BehaviorInstructionKind.AddBreachAccess
            && instruction.Amount == delta
            && string.Equals(instruction.Argument, reasonKey, StringComparison.Ordinal)
        || instruction.Branch is { } branch
            && branch.WhenTrue.Concat(branch.WhenFalse).Any(nested => DeclaresBreachAccess(nested, reasonKey, delta))
        || instruction.Choice is { } choice
            && choice.Options.SelectMany(static option => option.Instructions).Any(nested => DeclaresBreachAccess(nested, reasonKey, delta));
}
