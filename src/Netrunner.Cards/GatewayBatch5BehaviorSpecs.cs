using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>Generic damage, tag, and trash declarations for Slice 4.6.</summary>
public static class GatewayBatch5BehaviorSpecs
{
    private static readonly BehaviorTarget RetributionTarget = new(
        BehaviorTargetKind.InstalledCard,
        filter: new BehaviorCardFilter(BehaviorCardType.ProgramOrHardware, ownerSide: PlayerSide.Runner));

    private static readonly CardBehaviorDefinition UrticaCipher = new(
        GatewayBatch5CardCodes.UrticaCipher,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "urtica-access-damage",
                OccurrenceKind.CardAccessed,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.DealDamage,
                        damageKind: DamageKind.Net,
                        damageAmountOperand: new BehaviorDamageAmountOperand(
                            BehaviorDamageAmountKind.SourceAdvancementCounters,
                            baseAmount: 2)),
                ],
                requireSourceInstalled: true,
                activeWhileUnrezzed: true),
        ],
        canBeAdvanced: true);

    private static readonly CardBehaviorDefinition Clearinghouse = new(
        GatewayBatch5CardCodes.Clearinghouse,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "clearinghouse-turn-damage",
                OccurrenceKind.TurnBegin,
                TriggerMode.Optional,
                BehaviorTriggerScope.Controller,
                subjectCardFilter: null,
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.DealDamage,
                        damageKind: DamageKind.Meat,
                        damageAmountOperand: new BehaviorDamageAmountOperand(
                            BehaviorDamageAmountKind.SourceAdvancementCounters,
                            trashSourceAsCost: true)),
                ]),
        ],
        canBeAdvanced: true);

    private static readonly CardBehaviorDefinition Neurospike = new(
        GatewayBatch5CardCodes.Neurospike,
        PlayerSide.Corp,
        costs: [new BehaviorCost(BehaviorCostKind.Credits, amount: 3)],
        instructions:
        [
            new BehaviorInstruction(
                BehaviorInstructionKind.DealDamage,
                damageKind: DamageKind.Net,
                damageAmountOperand: new BehaviorDamageAmountOperand(
                    BehaviorDamageAmountKind.ScoredAgendaPrintedPointsThisTurn)),
        ]);

    private static readonly CardBehaviorDefinition PublicTrail = new(
        GatewayBatch5CardCodes.PublicTrail,
        PlayerSide.Corp,
        costs: [new BehaviorCost(BehaviorCostKind.Credits, amount: 4)],
        conditions: [new BehaviorCondition(BehaviorConditionKind.SuccessfulRunDuringRunnerLastTurn)],
        instructions:
        [
            new BehaviorInstruction(
                BehaviorInstructionKind.Choose,
                choice: new BehaviorChoiceSpec(
                    "public-trail-tag-or-pay",
                    BehaviorChooser.Runner,
                    BehaviorChoiceMode.Mode,
                    BehaviorChoiceCardinality.Exactly(1),
                    [
                        new BehaviorChoiceOption(
                            "pay-eight",
                            [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                            costs: [new BehaviorCost(BehaviorCostKind.Credits, amount: 8)]),
                        new BehaviorChoiceOption(
                            "take-tag",
                            [new BehaviorInstruction(
                                BehaviorInstructionKind.AddTags,
                                amount: 1,
                                recipient: BehaviorPlayerSelector.Runner)],
                            semantic: BehaviorChoiceOptionSemantic.Fallback),
                    ])),
        ]);

    private static readonly CardBehaviorDefinition Retribution = new(
        GatewayBatch5CardCodes.Retribution,
        PlayerSide.Corp,
        costs: [new BehaviorCost(BehaviorCostKind.Credits, amount: 1)],
        conditions: [BehaviorCondition.Tagged(PlayerSide.Runner)],
        targets: [RetributionTarget],
        instructions:
        [
            new BehaviorInstruction(
                BehaviorInstructionKind.Choose,
                choice: new BehaviorChoiceSpec(
                    "retribution-target",
                    BehaviorChooser.Corp,
                    BehaviorChoiceMode.Mandatory,
                    BehaviorChoiceCardinality.Exactly(1),
                    target: RetributionTarget)),
            new BehaviorInstruction(
                BehaviorInstructionKind.TrashCard,
                target: new BehaviorTarget(BehaviorTargetKind.ChosenCard)),
        ]);

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
        new ReadOnlyCollection<CardBehaviorDefinition>(
            [UrticaCipher, Clearinghouse, Neurospike, PublicTrail, Retribution]);

    public static IReadOnlyList<CardDefinitionId> AssignedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>(GenericSpecs.Select(spec => spec.DefinitionId).ToArray());
}
