using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Compiled printed ICE declarations for the Batch 4 cards in Slice 4.5.
/// Every subroutine is a typed Core contract; the reducer never dispatches on
/// a card title, code, or rules-text string.
/// </summary>
public static class GatewayBatch4IceBehaviorSpecs
{
    private static readonly IceBehaviorSpec Ansel =
        GatewayBatch4BioroidBehaviorSpecs.Bind(new IceBehaviorSpec(
            GatewayBatch4CardCodes.Ansel,
            strength: 4,
            iceType: IceType.Sentry,
            rezCost: 6,
            subroutines:
            [
                new TrashInstalledRunnerCardSubroutineSpec(),
                new InstallCardSubroutineSpec(),
                new RunRestrictionSubroutineSpec(RunRestrictionKind.CannotStealOrTrashCorpCards),
            ],
            strengthModifiers: []));

    private static readonly IceBehaviorSpec Bran =
        GatewayBatch4BioroidBehaviorSpecs.Bind(new IceBehaviorSpec(
            GatewayBatch4CardCodes.Bran,
            strength: 6,
            iceType: IceType.Barrier,
            rezCost: 6,
            subroutines:
            [
                new InstallCardSubroutineSpec(
                    IceInstallDestinationRule.DirectlyInward,
                    IceInstallCostRule.IgnoreAllCosts,
                    iceOnly: true),
                new EndRunSubroutineSpec(),
                new EndRunSubroutineSpec(),
            ],
            strengthModifiers: []));

    private static readonly IceBehaviorSpec Funhouse = new(
        GatewayBatch4CardCodes.Funhouse,
        strength: 4,
        iceType: IceType.CodeGate,
        rezCost: 5,
        subroutines:
        [
            new PayCreditsOrTagSubroutineSpec(creditAmount: 4, tagAmount: 1),
        ],
        strengthModifiers: []);

    /// <summary>Compiled ICE specs in ascending printed-code order.</summary>
    public static IReadOnlyList<IceBehaviorSpec> IceSpecs { get; } =
        new ReadOnlyCollection<IceBehaviorSpec>([Ansel, Bran, Funhouse]);
}
