using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// EL.3 Corp ICE. Printed strengths, rez costs and subroutines are
/// hand-copied; CR-26.03 citations are in <c>data/elevation/batch-registry.json</c>.
/// </summary>
public static class ElevationBatch1IceSpecs
{
    // 35063 — EL-CR-9.8-subroutines; EL-CR-10.1-tags.
    private static readonly IceBehaviorSpec DoomscrollIce = new(
        ElevationCardCodes.Doomscroll,
        strength: 3,
        IceType.Sentry,
        rezCost: 3,
        [
            new GiveTagsSubroutineSpec(1),
            new DamageSubroutineSpec(DamageKind.Net, 1),
            new BehaviorSubroutineSpec("doomscroll-tagged-damage"),
        ],
        []);

    private static readonly CardBehaviorDefinition Doomscroll = new(
        ElevationCardCodes.Doomscroll,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine(
                "doomscroll-tagged-damage",
                [Branch(BehaviorCondition.RunnerTagsAtLeast(2), [NetDamage(2)])]),
        ]);

    // 35075 — EL-CR-9.8-subroutines; the Runner cannot trash it while rezzed.
    private static readonly IceBehaviorSpec KessleroidIce = new(
        ElevationCardCodes.Kessleroid,
        strength: 1,
        IceType.Barrier,
        rezCost: 2,
        [new EndRunSubroutineSpec(), new EndRunSubroutineSpec()],
        []);

    private static readonly CardBehaviorDefinition Kessleroid = new(
        ElevationCardCodes.Kessleroid,
        PlayerSide.Corp,
        runnerCannotTrash: true);

    // 35076 — EL-CR-8.2-advancement; EL-CR-9.1-triggered.
    private static readonly IceBehaviorSpec SyailendraIce = new(
        ElevationCardCodes.Syailendra,
        strength: 5,
        IceType.CodeGate,
        rezCost: 4,
        [
            new BehaviorSubroutineSpec("syailendra-advance"),
            new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2),
            new DamageSubroutineSpec(DamageKind.Net, 1),
        ],
        []);

    private static readonly CardBehaviorDefinition Syailendra = new(
        ElevationCardCodes.Syailendra,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "syailendra-encounter-advance",
                OccurrenceKind.IceEncountered,
                TriggerMode.Optional,
                BehaviorTriggerScope.Self,
                null,
                AdvanceInstalled("syailendra-encounter-target", 1),
                requirement: BehaviorCondition.SourceAdvancementAtLeast(3)),
            Subroutine("syailendra-advance", AdvanceInstalled("syailendra-subroutine-target", 1, optional: true)),
        ],
        canBeAdvanced: true);

    // 35080 — EL-CR-9.8-subroutines; EL-CR-9.1-triggered (scored or stolen
    // from the protected server).
    private static readonly IceBehaviorSpec LamplighterIce = new(
        ElevationCardCodes.Lamplighter,
        strength: 3,
        IceType.Sentry,
        rezCost: 2,
        [
            new PayCreditsOrTagSubroutineSpec(creditAmount: 3, tagAmount: 1),
            new BehaviorSubroutineSpec("lamplighter-tagged-end-run"),
        ],
        []);

    private static readonly CardBehaviorDefinition Lamplighter = new(
        ElevationCardCodes.Lamplighter,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine(
                "lamplighter-tagged-end-run",
                [Branch(BehaviorCondition.Tagged(PlayerSide.Runner), [new BehaviorInstruction(BehaviorInstructionKind.EndRun)])]),
            new BehaviorTrigger(
                "lamplighter-agenda-trash",
                OccurrenceKind.AgendaScoredOrStolen,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Any,
                null,
                [TrashSelf()],
                requireSourceOnAttackedServer: true),
        ]);

    public static IReadOnlyList<IceBehaviorSpec> IceSpecs { get; } =
        [DoomscrollIce, KessleroidIce, SyailendraIce, LamplighterIce];

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
        [Doomscroll, Kessleroid, Syailendra, Lamplighter];
}
