namespace Netrunner.Core;

/// <summary>
/// Stable provenance helpers for catalog-authored encounter ICE strength
/// adjustments. The effect identity names the frozen source definition and
/// paid-ability key, so resolution never needs to infer behavior from a card
/// title or re-query a mutable card location.
/// </summary>
internal static class BehaviorEncounterStrengthContracts
{
    private const string EffectPrefix = "behavior:encounter-strength:";

    internal static string EffectBehaviorId(CardDefinitionId definitionId, string abilityKey) =>
        $"{EffectPrefix}{definitionId.Value}:{abilityKey}";

    internal static bool HasEffectPrefix(string behaviorId) =>
        behaviorId.StartsWith(EffectPrefix, StringComparison.Ordinal);

    internal static bool TryGetExecutableInstruction(
        CardBehaviorDefinition definition,
        string abilityKey,
        out BehaviorPaidAbility? ability,
        out BehaviorInstruction? instruction)
    {
        ability = definition.PaidAbilities.SingleOrDefault(candidate =>
            string.Equals(candidate.Key, abilityKey, StringComparison.Ordinal));
        instruction = null;
        if (ability is null
            || definition.Controller != PlayerSide.Runner
            || ability.Timing != BehaviorAbilityTiming.EncounterPaidWindow
            || ability.Activation != BehaviorAbilityActivation.PaidAbility)
        {
            return false;
        }

        BehaviorInstruction[] modifiers = ability.Instructions
            .Where(static candidate =>
                candidate.Kind == BehaviorInstructionKind.ModifyEncounteredIceStrength)
            .ToArray();
        if (modifiers.Length != 1)
        {
            return false;
        }

        instruction = modifiers[0];
        return true;
    }

    internal static bool MatchesEffect(
        CardBehaviorDefinition definition,
        string abilityKey,
        string behaviorId,
        RunValueAdjustment adjustment,
        out BehaviorPaidAbility? ability,
        out BehaviorInstruction? instruction) =>
        TryGetExecutableInstruction(definition, abilityKey, out ability, out instruction)
        && string.Equals(
            EffectBehaviorId(definition.DefinitionId, abilityKey),
            behaviorId,
            StringComparison.Ordinal)
        && instruction is not null
        && instruction.Amount == adjustment.Delta
        && string.Equals(instruction.Argument, adjustment.ReasonKey, StringComparison.Ordinal);
}
