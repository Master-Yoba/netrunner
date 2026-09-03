using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Generic, immutable behavior declarations for the Slice 4.3 Batch 2
/// checkpoint.  The declarations contain only typed Core primitives; printed
/// card metadata is supplied independently by the runtime card catalog.
/// </summary>
public static class GatewayBatch2BehaviorSpecs
{
    private const string SpinDoctorArchivesChoiceKey = "spin-doctor-archives";

    private static readonly BehaviorTarget ChosenArchivesCards =
        BehaviorTarget.CardsInZone(
            BehaviorZone.Archives,
            new BehaviorCardFilter(ownerSide: PlayerSide.Corp));

    private static readonly BehaviorChoiceSpec SpinDoctorArchivesChoice = new(
        SpinDoctorArchivesChoiceKey,
        BehaviorChooser.Controller,
        BehaviorChoiceMode.TargetSelection,
        BehaviorChoiceCardinality.UpTo(2),
        target: ChosenArchivesCards);

    private static readonly CardBehaviorDefinition SpinDoctor = new(
        GatewayBatch2CardCodes.SpinDoctor,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "spin-doctor-rez",
                OccurrenceKind.CardRezzed,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 2)]),
        ],
        paidAbilities:
        [
            new BehaviorPaidAbility(
                "spin-doctor-archives",
                BehaviorAbilityTiming.AnyPaidWindow,
                BehaviorAbilityActivation.PaidAbility,
                costs: [new BehaviorCost(BehaviorCostKind.RemoveSelfFromGame)],
                conditions: [],
                targets: [],
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.Choose,
                        choice: SpinDoctorArchivesChoice),
                    new BehaviorInstruction(
                        BehaviorInstructionKind.ShuffleIntoDeck,
                        target: new BehaviorTarget(
                            BehaviorTargetKind.ChosenCard,
                            selector: SpinDoctorArchivesChoiceKey),
                        sourceZone: BehaviorZone.Archives,
                        destinationZone: BehaviorZone.Deck),
                ],
                BehaviorAbilityUsageLimit.Unlimited),
        ]);

    /// <summary>All assigned Batch 2 definition IDs.</summary>
    public static IReadOnlyList<CardDefinitionId> AssignedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>
        ([
            GatewayBatch2CardCodes.Pennyshaver,
            GatewayBatch2CardCodes.TeleworkContract,
            GatewayBatch2CardCodes.SmartwareDistributor,
            GatewayCardCodes.NicoCampaign,
            GatewayBatch2CardCodes.SpinDoctor,
            GatewayBatch2CardCodes.RegolithMiningLicense,
        ]);

    /// <summary>Batch 2 cards intentionally withheld from this checkpoint.</summary>
    public static IReadOnlyList<CardDefinitionId> BlockedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>
        ([.. GatewayBatch2EconomyBehaviorSpecs.BlockedDefinitionIds]);

    /// <summary>Immutable generic declarations composed into Gateway.</summary>
    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
        new ReadOnlyCollection<CardBehaviorDefinition>
        ([
            GatewayBatch2EconomyBehaviorSpecs.GenericSpecs.Single(
                static definition => definition.DefinitionId == GatewayBatch2CardCodes.Pennyshaver),
            GatewayBatch2EconomyBehaviorSpecs.GenericSpecs.Single(
                static definition => definition.DefinitionId == GatewayBatch2CardCodes.TeleworkContract),
            GatewayBatch2EconomyBehaviorSpecs.GenericSpecs.Single(
                static definition => definition.DefinitionId == GatewayCardCodes.NicoCampaign),
            SpinDoctor,
            GatewayBatch2EconomyBehaviorSpecs.GenericSpecs.Single(
                static definition => definition.DefinitionId == GatewayBatch2CardCodes.RegolithMiningLicense),
        ]);
}
