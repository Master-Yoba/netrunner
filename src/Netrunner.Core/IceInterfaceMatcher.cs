namespace Netrunner.Core;

/// <summary>
/// Pure encountered-ICE/icebreaker interface legality. The encountered ICE
/// side must always be concrete; an icebreaker may name one concrete subtype
/// or the explicit <see cref="IceType.Any"/> wildcard.
/// </summary>
public static class IceInterfaceMatcher
{
    /// <summary>
    /// Returns whether the breaker can interface with the encountered ICE.
    /// No state, catalog, strength, affordability, or break-limit rules are
    /// consulted here; callers compose those typed legality checks around this
    /// single subtype matcher.
    /// </summary>
    public static bool Matches(
        IceBehaviorSpec encounteredIce,
        IcebreakerBehaviorSpec icebreaker)
    {
        ArgumentNullException.ThrowIfNull(encounteredIce);
        ArgumentNullException.ThrowIfNull(icebreaker);
        return Matches(encounteredIce.IceType, icebreaker.InterfaceType);
    }

    /// <summary>
    /// Matches a concrete encountered-ICE subtype against a concrete or
    /// wildcard icebreaker interface.
    /// </summary>
    public static bool Matches(IceType encounteredIceType, IceType interfaceType) =>
        IsConcrete(encounteredIceType)
        && (interfaceType == IceType.Any || encounteredIceType == interfaceType);

    /// <summary>
    /// The full encounter interface check: the ICE's printed subtype or one it
    /// gains (EL-ICE-GAINS-SUBTYPE), and any breaker-subtype restriction it
    /// prints (EL-BREAK-RESTRICTION).
    /// </summary>
    public static bool CanInterface(
        IceBehaviorSpec encounteredIce,
        bool iceGainsEveryType,
        IcebreakerBehaviorSpec icebreaker,
        IReadOnlyCollection<string> breakerSubtypes)
    {
        ArgumentNullException.ThrowIfNull(encounteredIce);
        ArgumentNullException.ThrowIfNull(icebreaker);
        ArgumentNullException.ThrowIfNull(breakerSubtypes);
        bool typeMatches = Matches(encounteredIce, icebreaker)
            || iceGainsEveryType && icebreaker.InterfaceType != IceType.Any;
        return typeMatches
            && (encounteredIce.BreakableOnlyBySubtype is not { } required
                || breakerSubtypes.Contains(required, StringComparer.Ordinal));
    }

    /// <summary>Returns whether a value is a legal concrete ICE subtype.</summary>
    public static bool IsConcrete(IceType type) => type is
        IceType.Barrier or IceType.CodeGate or IceType.Sentry;
}
