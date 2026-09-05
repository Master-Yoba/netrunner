using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Generic behavior declarations for the Slice 4.5 approach and encounter
/// reactions owned by this checkpoint. The declarations intentionally use
/// the shared occurrence, structured-choice, and player-recipient vocabulary
/// so Core does not need a title-specific branch for any of these cards.
/// </summary>
public static class GatewayBatch4BehaviorSpecs
{
    // Run-event success effects are ordinary catalog triggers. The run-event
    // declaration remains the source of truth for targeting and snapshots;
    // this binding supplies the mandatory self-scoped RunSuccessful draw.
    private static readonly CardBehaviorDefinition Jailbreak = new(
        GatewayBatch4CardCodes.Jailbreak,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "jailbreak-success-draw",
                OccurrenceKind.RunSuccessful,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 1),
                ]),
        ]);

    private const string ManegarmChoiceKey = "manegarm-approach-choice";
    private const string AnoeticVoidChoiceKey = "anoetic-void-approach-choice";
    private const string AnoeticVoidTrashCostKey = "anoetic-void-trash-hq";
    private const string FunhouseChoiceKey = "funhouse-encounter-choice";

    private static readonly BehaviorTarget AnoeticVoidHeadquartersCards =
        BehaviorTarget.CardsInZone(
            BehaviorZone.Headquarters,
            new BehaviorCardFilter(ownerSide: PlayerSide.Corp),
            reveal: false);

    private static readonly BehaviorChoiceSpec ManegarmApproachChoice = new(
        ManegarmChoiceKey,
        BehaviorChooser.ActingPlayer,
        BehaviorChoiceMode.Mode,
        BehaviorChoiceCardinality.Exactly(1),
        [
            new BehaviorChoiceOption(
                "spend-clicks",
                [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                costs: [new BehaviorCost(BehaviorCostKind.Clicks, amount: 2)],
                semantic: BehaviorChoiceOptionSemantic.Normal),
            new BehaviorChoiceOption(
                "pay-credits",
                [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                costs: [new BehaviorCost(BehaviorCostKind.Credits, amount: 5)],
                semantic: BehaviorChoiceOptionSemantic.Normal),
            new BehaviorChoiceOption(
                "end-run",
                [new BehaviorInstruction(BehaviorInstructionKind.EndRun)],
                semantic: BehaviorChoiceOptionSemantic.Fallback),
        ]);

    private static readonly BehaviorChoiceSpec AnoeticVoidApproachChoice = new(
        AnoeticVoidChoiceKey,
        BehaviorChooser.Controller,
        BehaviorChoiceMode.Mode,
        BehaviorChoiceCardinality.Exactly(1),
        [
            new BehaviorChoiceOption(
                "pay-and-trash-hq",
                [new BehaviorInstruction(BehaviorInstructionKind.EndRun)],
                costs:
                [
                    new BehaviorCost(BehaviorCostKind.Credits, amount: 2),
                    new BehaviorCost(
                        BehaviorCostKind.Additional,
                        amount: 2,
                        additionalKey: AnoeticVoidTrashCostKey,
                        additionalTarget: AnoeticVoidHeadquartersCards,
                        additionalKind: BehaviorAdditionalCostKind.TrashCards),
                ],
                semantic: BehaviorChoiceOptionSemantic.Normal),
            new BehaviorChoiceOption(
                "decline",
                [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                semantic: BehaviorChoiceOptionSemantic.Fallback),
        ]);

    private static readonly BehaviorChoiceSpec FunhouseEncounterChoice = new(
        FunhouseChoiceKey,
        BehaviorChooser.ActingPlayer,
        BehaviorChoiceMode.Mode,
        BehaviorChoiceCardinality.Exactly(1),
        [
            new BehaviorChoiceOption(
                "take-tag",
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.AddTags,
                        amount: 1,
                        recipient: BehaviorPlayerSelector.ActingPlayer),
                ],
                semantic: BehaviorChoiceOptionSemantic.Normal),
            new BehaviorChoiceOption(
                "end-run",
                [new BehaviorInstruction(BehaviorInstructionKind.EndRun)],
                semantic: BehaviorChoiceOptionSemantic.Fallback),
        ]);

    private static readonly CardBehaviorDefinition Manegarm = new(
        GatewayBatch4CardCodes.Manegarm,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "manegarm-server-approached",
                OccurrenceKind.ServerApproached,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Any,
                subjectCardFilter: null,
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.Choose,
                        choice: ManegarmApproachChoice),
                ],
                requireSourceOnAttackedServer: true),
        ]);

    private static readonly CardBehaviorDefinition AnoeticVoid = new(
        GatewayBatch4CardCodes.AnoeticVoid,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "anoetic-void-server-approached",
                OccurrenceKind.ServerApproached,
                TriggerMode.Optional,
                BehaviorTriggerScope.Any,
                subjectCardFilter: null,
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.Choose,
                        choice: AnoeticVoidApproachChoice),
                ],
                requireSourceOnAttackedServer: true),
        ]);

    private static readonly CardBehaviorDefinition Funhouse = new(
        GatewayBatch4CardCodes.Funhouse,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "funhouse-ice-encountered",
                OccurrenceKind.IceEncountered,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.Choose,
                        choice: FunhouseEncounterChoice),
                ]),
        ]);

    /// <summary>Definition IDs assigned by this checkpoint, in card order.</summary>
    public static IReadOnlyList<CardDefinitionId> AssignedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>
        ([
            GatewayBatch4CardCodes.Jailbreak,
            GatewayBatch4CardCodes.Manegarm,
            GatewayBatch4CardCodes.AnoeticVoid,
            GatewayBatch4CardCodes.Funhouse,
        ]);

    /// <summary>Immutable generic declarations composed into Gateway.</summary>
    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
        new ReadOnlyCollection<CardBehaviorDefinition>
        ([
            Jailbreak,
            Manegarm,
            AnoeticVoid,
            Funhouse,
        ]);
}
