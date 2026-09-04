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

    /// <summary>Returns whether a value is a legal concrete ICE subtype.</summary>
    public static bool IsConcrete(IceType type) => type is
        IceType.Barrier or IceType.CodeGate or IceType.Sentry;
}
