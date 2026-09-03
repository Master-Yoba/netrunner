using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Generic declarations for the hosted-credit and counter cards assigned to
/// the Slice 4.3 Batch 2 checkpoint.  The declarations are authored from the
/// authenticated System Gateway snapshot and use only typed Core primitives;
/// no card text is parsed at runtime.
/// </summary>
public static class GatewayBatch2EconomyBehaviorSpecs
{
    private static readonly CardBehaviorDefinition Pennyshaver = new(
        GatewayBatch2CardCodes.Pennyshaver,
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
                "pennyshaver-successful-run",
                OccurrenceKind.RunSuccessful,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Controller,
                subjectCardFilter: null,
                instructions: [PlaceHosted(1)]),
        ],
        paidAbilities:
        [
            new BehaviorPaidAbility(
                "pennyshaver-cashout",
                BehaviorAbilityTiming.ActionPhase,
                BehaviorAbilityActivation.ClickAction,
                costs: [ClickCost()],
                conditions: [],
                targets: [],
                instructions:
                [
                    PlaceHosted(1),
                    new BehaviorInstruction(BehaviorInstructionKind.TakeAllHostedCredits),
                ],
                BehaviorAbilityUsageLimit.Unlimited),
        ]);

    private static readonly CardBehaviorDefinition TeleworkContract = new(
        GatewayBatch2CardCodes.TeleworkContract,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "telework-install",
                OccurrenceKind.CardInstalled,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions: [LoadHosted(9)]),
            new BehaviorTrigger(
                "telework-empty",
                OccurrenceKind.HostedCreditPoolEmptied,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.TrashSelf)]),
        ],
        paidAbilities:
        [
            new BehaviorPaidAbility(
                "telework-take",
                BehaviorAbilityTiming.ActionPhase,
                BehaviorAbilityActivation.ClickAction,
                costs: [ClickCost()],
                conditions: [],
                targets: [],
                instructions: [TakeHosted(3)],
                BehaviorAbilityUsageLimit.OncePerTurn),
        ]);

    /// <summary>
    /// Smartware's click placement is a valid generic operation, but its
    /// mandatory turn-begin take must be deferred until Core supports a
    /// typed take-up-to/available operation.  Registering the declaration in
    /// this blocked surface keeps the audited shape visible without making an
    /// exact take from an empty pool look executable.
    /// </summary>
    private static readonly CardBehaviorDefinition SmartwareDistributor = new(
        GatewayBatch2CardCodes.SmartwareDistributor,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "smartware-turn-begin",
                OccurrenceKind.TurnBegin,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Controller,
                subjectCardFilter: null,
                instructions: [TakeHosted(1)]),
        ],
        paidAbilities:
        [
            new BehaviorPaidAbility(
                "smartware-place",
                BehaviorAbilityTiming.ActionPhase,
                BehaviorAbilityActivation.ClickAction,
                costs: [ClickCost()],
                conditions: [],
                targets: [],
                instructions: [PlaceHosted(3)],
                BehaviorAbilityUsageLimit.Unlimited),
        ]);

    private static readonly CardBehaviorDefinition RegolithMiningLicense = new(
        GatewayBatch2CardCodes.RegolithMiningLicense,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "regolith-rez",
                OccurrenceKind.CardRezzed,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions: [LoadHosted(15)]),
            new BehaviorTrigger(
                "regolith-empty",
                OccurrenceKind.HostedCreditPoolEmptied,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.TrashSelf)]),
        ],
        paidAbilities:
        [
            new BehaviorPaidAbility(
                "regolith-take",
                BehaviorAbilityTiming.ActionPhase,
                BehaviorAbilityActivation.ClickAction,
                costs: [ClickCost()],
                conditions: [],
                targets: [],
                instructions: [TakeHosted(3)],
                BehaviorAbilityUsageLimit.Unlimited),
        ]);

    private static readonly CardBehaviorDefinition NicoCampaign = new(
        GatewayCardCodes.NicoCampaign,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "nico-rez",
                OccurrenceKind.CardRezzed,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions: [LoadHosted(9)]),
            new BehaviorTrigger(
                "nico-turn-begin",
                OccurrenceKind.TurnBegin,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Controller,
                subjectCardFilter: null,
                instructions: [TakeHosted(3)]),
            new BehaviorTrigger(
                "nico-empty",
                OccurrenceKind.HostedCreditPoolEmptied,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions:
                [
                    new BehaviorInstruction(BehaviorInstructionKind.TrashSelf),
                    new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 1),
                ]),
        ]);

    /// <summary>Cards assigned to this economy submodule, in stable card order.</summary>
    public static IReadOnlyList<CardDefinitionId> AssignedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>
        ([
            GatewayBatch2CardCodes.Pennyshaver,
            GatewayBatch2CardCodes.TeleworkContract,
            GatewayBatch2CardCodes.SmartwareDistributor,
            GatewayCardCodes.NicoCampaign,
            GatewayBatch2CardCodes.RegolithMiningLicense,
        ]);

    /// <summary>
    /// Smartware remains outside the executable catalog until Core gains a
    /// mandatory take-up-to/available hosted-credit instruction.
    /// </summary>
    public static IReadOnlyList<CardDefinitionId> BlockedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>([GatewayBatch2CardCodes.SmartwareDistributor]);

    /// <summary>Complete executable declarations from this submodule.</summary>
    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
        new ReadOnlyCollection<CardBehaviorDefinition>
        ([
            Pennyshaver,
            TeleworkContract,
            NicoCampaign,
            RegolithMiningLicense,
        ]);

    /// <summary>Audited declarations intentionally withheld from the catalog.</summary>
    public static IReadOnlyList<CardBehaviorDefinition> BlockedSpecs { get; } =
        new ReadOnlyCollection<CardBehaviorDefinition>([SmartwareDistributor]);

    private static BehaviorCost ClickCost() =>
        new(BehaviorCostKind.Clicks, amount: 1);

    private static BehaviorInstruction LoadHosted(int amount) =>
        new(
            BehaviorInstructionKind.LoadHostedCredits,
            amount,
            spendRestriction: HostedCreditSpendRestriction.CardEffectOnly);

    private static BehaviorInstruction PlaceHosted(int amount) =>
        new(
            BehaviorInstructionKind.PlaceHostedCredits,
            amount,
            spendRestriction: HostedCreditSpendRestriction.CardEffectOnly);

    private static BehaviorInstruction TakeHosted(int amount) =>
        new(BehaviorInstructionKind.TakeHostedCredits, amount);
}
