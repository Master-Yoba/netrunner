using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Generic behavior declarations for the Slice 4.4 Batch 3 cards.
/// Leech is expressed entirely through the typed trigger and encounter-paid
/// ability vocabulary; the other declarations use the shared typed modifier
/// primitives. No title/code branch is required by Core.
/// </summary>
public static class GatewayBatch3BehaviorSpecs
{
    private const string VirusCounter = "virus";
    private const string EncounterAbilityKey = "leech-encounter-strength";
    private const string EncounterReasonKey = "leech-encounter-ice-strength";

    private static readonly CardBehaviorDefinition DzmzOptimizer = new(
        GatewayBatch3CardCodes.DzmzOptimizer,
        PlayerSide.Runner,
        staticModifiers:
        [
            new BehaviorStaticModifier(
                BehaviorStaticModifierKind.MemoryUnits,
                delta: 1),
        ],
        firstProgramInstallCostModifiers:
        [
            new BehaviorFirstProgramInstallCostModifier(
                delta: -1,
                reasonKey: "dzmz-first-program-install"),
        ]);

    private static readonly CardBehaviorDefinition DocklandsPass = new(
        GatewayBatch3CardCodes.DocklandsPass,
        PlayerSide.Runner,
        firstHqBreachAccessModifiers:
        [
            new BehaviorFirstHqBreachAccessModifier(
                delta: 1,
                reasonKey: "docklands-pass-first-hq-breach"),
        ]);

    private static readonly CardBehaviorDefinition VerbalPlasticity = new(
        GatewayBatch3CardCodes.VerbalPlasticity,
        PlayerSide.Runner,
        firstBasicDrawReplacements:
        [
            new BehaviorFirstBasicDrawReplacement(
                drawCount: 2,
                reasonKey: "verbal-plasticity-first-basic-draw"),
        ]);

    private static readonly CardBehaviorDefinition Leech = new(
        GatewayBatch3CardCodes.Leech,
        PlayerSide.Runner,
        triggers:
        [
            CentralRunSuccessTrigger("leech-hq-success", ServerKind.Headquarters),
            CentralRunSuccessTrigger("leech-r-and-d-success", ServerKind.RAndD),
            CentralRunSuccessTrigger("leech-archives-success", ServerKind.Archives),
        ],
        paidAbilities:
        [
            new BehaviorPaidAbility(
                EncounterAbilityKey,
                BehaviorAbilityTiming.EncounterPaidWindow,
                BehaviorAbilityActivation.PaidAbility,
                costs:
                [
                    new BehaviorCost(
                        BehaviorCostKind.Counter,
                        amount: 1,
                        counterName: VirusCounter),
                ],
                conditions: [new BehaviorCondition(BehaviorConditionKind.Encountering)],
                targets: [],
                instructions:
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.ModifyEncounteredIceStrength,
                        amount: -1,
                        argument: EncounterReasonKey),
                ],
                BehaviorAbilityUsageLimit.Unlimited),
        ]);

    private static BehaviorTrigger CentralRunSuccessTrigger(string key, ServerKind serverKind) =>
        new(
            key,
            OccurrenceKind.RunSuccessful,
            TriggerMode.Mandatory,
            BehaviorTriggerScope.Controller,
            subjectCardFilter: null,
            instructions:
            [
                new BehaviorInstruction(
                    BehaviorInstructionKind.AddCounter,
                    amount: 1,
                    argument: VirusCounter,
                    target: new BehaviorTarget(BehaviorTargetKind.Self)),
            ],
            serverKindRequirement: serverKind);

    /// <summary>Immutable generic declarations composed into Gateway.</summary>
    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
        new ReadOnlyCollection<CardBehaviorDefinition>
        ([
            Leech,
            DzmzOptimizer,
            DocklandsPass,
            VerbalPlasticity,
        ]);
}
