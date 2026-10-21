using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Generic behavior declarations for the Slice 4.3 run and conditional-cost
/// cards. Printed metadata (install cost, memory, and card class) remains in
/// the runtime card catalog; these declarations contain only executable,
/// typed behavior primitives.
/// </summary>
public static class GatewayBatch2RunCostBehaviorSpecs
{
    private const string ConduitVirusCounter = "virus";
    private const string ConduitAccessReason = "conduit-virus-access";

    private static readonly CardBehaviorDefinition Carmen = new(
        GatewayBatch2CardCodes.Carmen,
        PlayerSide.Runner,
        costModifiers:
        [
            new BehaviorCostModifier(
                BehaviorCostModifierKind.RunnerInstallCreditCost,
                delta: -2,
                reasonKey: "carmen-successful-run-install"),
        ]);

    private static readonly CardBehaviorDefinition Marjanah = new(
        GatewayBatch2CardCodes.Marjanah,
        PlayerSide.Runner,
        costModifiers:
        [
            new BehaviorCostModifier(
                BehaviorCostModifierKind.IcebreakerBreakCreditCost,
                delta: -1,
                reasonKey: "marjanah-successful-run-break"),
        ]);

    private static readonly CardBehaviorDefinition RedTeam = new(
        GatewayBatch2CardCodes.RedTeam,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "red-team-install",
                OccurrenceKind.CardInstalled,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions: [LoadHosted(12)]),
            new BehaviorTrigger(
                "red-team-empty",
                OccurrenceKind.HostedCreditPoolEmptied,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions: [new BehaviorInstruction(BehaviorInstructionKind.TrashSelf)]),
            new BehaviorTrigger(
                "red-team-successful-run",
                OccurrenceKind.RunEnded,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                subjectCardFilter: null,
                instructions: [TakeHosted(3)],
                runOutcomeRequirement: RunOutcomeRequirement.Successful),
        ],
        paidAbilities:
        [
            CentralRunAbility(
                "red-team-run-hq",
                ServerKind.Headquarters),
            CentralRunAbility(
                "red-team-run-rnd",
                ServerKind.RAndD),
            CentralRunAbility(
                "red-team-run-archives",
                ServerKind.Archives),
        ]);

    private static readonly CardBehaviorDefinition Conduit = new(
        GatewayBatch2CardCodes.Conduit,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "conduit-successful-rnd",
                OccurrenceKind.RunEnded,
                TriggerMode.Optional,
                BehaviorTriggerScope.Controller,
                subjectCardFilter: null,
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.AddCounter,
                        amount: 1,
                        argument: ConduitVirusCounter),
                ],
                runOutcomeRequirement: RunOutcomeRequirement.Successful,
                serverKindRequirement: ServerKind.RAndD),
        ],
        paidAbilities:
        [
            new BehaviorPaidAbility(
                "conduit-run-rnd",
                BehaviorAbilityTiming.ActionPhase,
                BehaviorAbilityActivation.ClickAction,
                costs: [ClickCost()],
                conditions: [],
                targets: [],
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.StartRun,
                        runOperand: new BehaviorRunOperand(
                            ServerKind.RAndD,
                            sourceCounterName: ConduitVirusCounter,
                            breachAccessDeltaPerCounter: 1,
                            breachAccessReasonKey: ConduitAccessReason)),
                ],
                BehaviorAbilityUsageLimit.Unlimited),
        ]);

    /// <summary>All generic definitions in ascending printed-code order.</summary>
    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
        new ReadOnlyCollection<CardBehaviorDefinition>
        ([
            Carmen,
            Marjanah,
            RedTeam,
            Conduit,
        ]);

    /// <summary>All assigned definition IDs in ascending printed-code order.</summary>
    public static IReadOnlyList<CardDefinitionId> AssignedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>
        ([
            GatewayBatch2CardCodes.Carmen,
            GatewayBatch2CardCodes.Marjanah,
            GatewayBatch2CardCodes.RedTeam,
            GatewayBatch2CardCodes.Conduit,
        ]);

    private static BehaviorPaidAbility CentralRunAbility(string key, ServerKind serverKind) =>
        new(
            key,
            BehaviorAbilityTiming.ActionPhase,
            BehaviorAbilityActivation.ClickAction,
            costs: [ClickCost()],
            conditions: [],
            targets: [],
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.StartRun,
                    runOperand: new BehaviorRunOperand(
                        serverKind,
                        requireNotRunThisTurn: true)),
            ],
            BehaviorAbilityUsageLimit.Unlimited);

    private static BehaviorCost ClickCost() =>
        new(BehaviorCostKind.Clicks, amount: 1);

    private static BehaviorInstruction LoadHosted(int amount) =>
        new(
            BehaviorInstructionKind.LoadHostedCredits,
            amount,
            spendRestriction: HostedCreditSpendRestriction.CardEffectOnly);

    private static BehaviorInstruction TakeHosted(int amount) =>
        new(BehaviorInstructionKind.TakeHostedCredits, amount);
}
