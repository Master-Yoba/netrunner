using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Shared typed declaration for the Runner interface ability printed on
/// Ansel 1.0 and Brân 1.0. Both ICE definitions reference this same immutable
/// declaration so their action payloads and catalog fingerprints cannot
/// drift while their other subroutines are added in later card work.
/// </summary>
public static class GatewayBatch4BioroidBehaviorSpecs
{
    public static readonly IReadOnlyList<CardDefinitionId> BoundIceDefinitionIds =
        new ReadOnlyCollection<CardDefinitionId>(
        [
            GatewayBatch4CardCodes.Ansel,
            GatewayBatch4CardCodes.Bran,
        ]);

    public const string BioroidBreakAbilityKey = "bioroid-break-one";

    public static EncounteredBioroidAbilitySpec BioroidBreakAbility { get; } =
        new(BioroidBreakAbilityKey, new BehaviorCost(BehaviorCostKind.LoseClick, amount: 1));

    public static IReadOnlyList<EncounteredBioroidAbilitySpec> BioroidAbilities { get; } =
        new ReadOnlyCollection<EncounteredBioroidAbilitySpec>([BioroidBreakAbility]);

    /// <summary>
    /// Attaches the shared Runner interface declaration to a later-authored
    /// Ansel/Brân ICE spec while retaining that spec's strength, subtype,
    /// printed subroutines, and other declarations. The helper keeps this
    /// checkpoint's binding compatible with future ICE effect work without
    /// making the Core engine aware of either card title.
    /// </summary>
    public static IceBehaviorSpec Bind(IceBehaviorSpec ice)
    {
        ArgumentNullException.ThrowIfNull(ice);
        if (!BoundIceDefinitionIds.Contains(ice.DefinitionId))
        {
            throw new ArgumentException(
                $"Only Ansel 1.0 or Brân 1.0 may receive the shared bioroid interface declaration; got '{ice.DefinitionId}'.",
                nameof(ice));
        }

        return new IceBehaviorSpec(
            ice.DefinitionId,
            ice.Strength,
            ice.IceType,
            ice.RezCost,
            ice.Subroutines,
            ice.StrengthModifiers,
            ice.ValueModifiers,
            BioroidAbilities);
    }
}
