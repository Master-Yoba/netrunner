using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Generic declarations for the Slice 4.3 trigger/counter/install cards.
/// These definitions are authored from the authenticated Gateway snapshot;
/// all executable values are typed Core primitives and no printed text is
/// parsed at runtime.
/// </summary>
public static class GatewayBatch2TriggerBehaviorSpecs
{
    private const string FermenterVirusCounter = "virus";
    private const string CookbookTriggerSubject = "trigger-subject";
    private const string PantographInstallBinding = "pantograph-install";

    private static readonly BehaviorCardFilter RunnerVirusProgramFilter =
        new(
            BehaviorCardType.Program,
            ownerSide: PlayerSide.Runner,
            subtype: "virus");

    private static readonly CardBehaviorDefinition Fermenter = new(
        GatewayBatch2CardCodes.Fermenter,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "fermenter-install",
                OccurrenceKind.CardInstalled,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions: [AddVirusCounter()]),
            new BehaviorTrigger(
                "fermenter-turn-begin",
                OccurrenceKind.TurnBegin,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Controller,
                subjectCardFilter: null,
                instructions: [AddVirusCounter()]),
        ],
        paidAbilities:
        [
            new BehaviorPaidAbility(
                "fermenter-cashout",
                BehaviorAbilityTiming.ActionPhase,
                BehaviorAbilityActivation.ClickAction,
                costs:
                [
                    new BehaviorCost(BehaviorCostKind.Clicks),
                    new BehaviorCost(BehaviorCostKind.TrashSelf),
                ],
                conditions: [],
                targets: [],
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.GainCredits,
                        amountOperand: new BehaviorAmountOperand(
                            FermenterVirusCounter,
                            multiplier: 2)),
                ],
                BehaviorAbilityUsageLimit.Unlimited),
        ]);

    private static readonly CardBehaviorDefinition Cookbook = new(
        GatewayBatch2CardCodes.Cookbook,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "cookbook-virus-install",
                OccurrenceKind.CardInstalled,
                TriggerMode.Optional,
                BehaviorTriggerScope.Controller,
                subjectCardFilter: RunnerVirusProgramFilter,
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.AddCounter,
                        amount: 1,
                        argument: "virus",
                        target: new BehaviorTarget(
                            BehaviorTargetKind.ChosenCard,
                            selector: CookbookTriggerSubject)),
                ]),
        ]);

    private static readonly BehaviorChoiceSpec PantographInstallChoice = new(
        PantographInstallBinding,
        BehaviorChooser.Controller,
        BehaviorChoiceMode.Optional,
        BehaviorChoiceCardinality.UpTo(1),
        target: BehaviorTarget.CardInZone(
            BehaviorZone.Grip,
            new BehaviorCardFilter(ownerSide: PlayerSide.Runner)));

    private static readonly CardBehaviorDefinition Pantograph = new(
        GatewayBatch2CardCodes.Pantograph,
        PlayerSide.Runner,
        staticModifiers:
        [
            new BehaviorStaticModifier(
                BehaviorStaticModifierKind.MemoryUnits,
                delta: 1),
        ],
        triggers:
        [
            new BehaviorTrigger(
                "pantograph-agenda",
                OccurrenceKind.AgendaScoredOrStolen,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Any,
                subjectCardFilter: null,
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.GainCredits,
                        amount: 1),
                    new BehaviorInstruction(
                        BehaviorInstructionKind.Choose,
                        choice: PantographInstallChoice),
                    new BehaviorInstruction(
                        BehaviorInstructionKind.InstallCard,
                        target: new BehaviorTarget(
                            BehaviorTargetKind.ChosenCard,
                            selector: PantographInstallBinding),
                        sourceZone: BehaviorZone.Grip,
                        destinationZone: BehaviorZone.Rig,
                        optional: true),
                ]),
        ]);

    /// <summary>Assigned trigger/counter/install definitions in card order.</summary>
    public static IReadOnlyList<CardDefinitionId> AssignedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>
        ([
            GatewayBatch2CardCodes.Fermenter,
            GatewayBatch2CardCodes.Cookbook,
            GatewayBatch2CardCodes.Pantograph,
        ]);

    /// <summary>Complete executable definitions from this submodule.</summary>
    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
        new ReadOnlyCollection<CardBehaviorDefinition>
        ([
            Fermenter,
            Cookbook,
            Pantograph,
        ]);

    private static BehaviorInstruction AddVirusCounter() =>
        new(
            BehaviorInstructionKind.AddCounter,
            amount: 1,
            argument: FermenterVirusCounter);
}
