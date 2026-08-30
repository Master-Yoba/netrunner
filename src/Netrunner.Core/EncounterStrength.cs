namespace Netrunner.Core;

/// <summary>
/// Pure CR 26.03 effective-strength model for encounter resolution (CR 6.5 and
/// 9.8).  The functions read compiled behavior specs and typed run state only;
/// they never touch the engine, the catalog, or the RNG, so they are safe to
/// call from the validator and from later break/pump reducers.
/// </summary>
public static class EncounterStrength
{
    /// <summary>Behavior identifier of the icebreaker pump lingering effect.</summary>
    public const string IcebreakerPumpBehavior = "icebreaker-pump";

    /// <summary>
    /// Effective strength of one ICE defending the given server kind: the base
    /// printed strength plus every conditional modifier that applies to the
    /// server kind.
    /// </summary>
    public static int Ice(IceBehaviorSpec spec, ServerKind serverKind)
    {
        ArgumentNullException.ThrowIfNull(spec);
        int strength = spec.Strength;
        foreach (IceStrengthModifierSpec modifier in spec.StrengthModifiers)
        {
            if (Applies(modifier.Condition, serverKind))
            {
                strength += modifier.Delta;
            }
        }

        return strength;
    }

    /// <summary>
    /// Effective strength of one icebreaker during an encounter: its base
    /// printed strength
    /// plus one pump contribution per active encounter-expiring pump effect it
    /// sources.  Run-expiring and foreign-source effects never contribute.
    /// </summary>
    public static int Breaker(
        IcebreakerBehaviorSpec spec,
        IReadOnlyList<RunLingeringEffect> activeEffects,
        CardInstanceId breakerId)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(activeEffects);
        int strength = spec.BaseStrength;
        foreach (RunLingeringEffect effect in activeEffects)
        {
            if (effect.BehaviorId == IcebreakerPumpBehavior
                && effect.SourceCardId == breakerId
                && effect.Expiry == RunLingeringEffectExpiry.Encounter)
            {
                strength += spec.PumpStrength;
            }
        }

        return strength;
    }

    private static bool Applies(IceStrengthModifierCondition condition, ServerKind serverKind) =>
        condition switch
        {
            IceStrengthModifierCondition.ProtectingRemoteServer => serverKind == ServerKind.Remote,
            // Unknown future conditions never contribute in M2.
            _ => false,
        };
}
