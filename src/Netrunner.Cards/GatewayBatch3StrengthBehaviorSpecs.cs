using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Compiled strength behavior for the Slice 4.4 Checkpoint C1 icebreakers.
/// Printed values are authored as typed declarations; no card text is parsed
/// by the engine.
/// </summary>
public static class GatewayBatch3StrengthBehaviorSpecs
{
    private static readonly BehaviorCardFilter InstalledRunnerIcebreakers =
        new(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner);

    private static readonly IcebreakerBehaviorSpec Echelon = new(
        GatewayBatch3CardCodes.Echelon,
        baseStrength: 0,
        interfaceType: IceType.Sentry,
        breakCost: 1,
        breakLimit: 1,
        pumpCost: 3,
        pumpStrength: 2,
        valueModifiers:
        [
            BehaviorValueModifier.PerInstalled(
                InstalledRunnerIcebreakers,
                delta: 1,
                reasonKey: "echelon-installed-icebreakers"),
        ]);

    private static readonly IcebreakerBehaviorSpec Unity = new(
        GatewayBatch3CardCodes.Unity,
        baseStrength: 1,
        interfaceType: IceType.CodeGate,
        breakCost: 1,
        breakLimit: 1,
        pumpCost: 1,
        pumpStrength: 0,
        dynamicPump: new BehaviorDynamicPumpDeclaration(
            InstalledRunnerIcebreakers,
            unitDelta: 1,
            variableKey: "unity-installed-icebreakers",
            reasonKey: "unity-installed-icebreakers"));

    /// <summary>The two C1 strength specs, in printed-code order.</summary>
    public static IReadOnlyList<IcebreakerBehaviorSpec> IcebreakerSpecs { get; } =
        new ReadOnlyCollection<IcebreakerBehaviorSpec>(new[] { Echelon, Unity });
}
