using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// EL.3 economy cards: loaded assets, click and credit operations and
/// events. CR-26.03 citations are in <c>data/elevation/batch-registry.json</c>.
/// </summary>
public static class ElevationBatch1EconomySpecs
{
    // 35040 — EL-CR-10.9-load-empty; EL-CR-1.11-clicks.
    private static readonly CardBehaviorDefinition OttoCampaign = new(
        ElevationCardCodes.OttoCampaign,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("otto-rez", OccurrenceKind.CardRezzed, BehaviorTriggerScope.Self, [LoadHosted(6)]),
            Trigger("otto-turn-begin", OccurrenceKind.TurnBegin, BehaviorTriggerScope.Controller, [TakeHosted(2)]),
            Trigger("otto-empty", OccurrenceKind.HostedCreditPoolEmptied, BehaviorTriggerScope.Self,
                [TrashSelf(), GainClicks(2)]),
        ]);

    // 35072 — EL-CR-10.9-load-empty.
    private static readonly CardBehaviorDefinition AnthillExcavationContract = new(
        ElevationCardCodes.AnthillExcavationContract,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("anthill-rez", OccurrenceKind.CardRezzed, BehaviorTriggerScope.Self, [LoadHosted(8)]),
            Trigger("anthill-turn-begin", OccurrenceKind.TurnBegin, BehaviorTriggerScope.Controller,
                [TakeHosted(4), Draw(1)]),
            Trigger("anthill-empty", OccurrenceKind.HostedCreditPoolEmptied, BehaviorTriggerScope.Self, [TrashSelf()]),
        ]);

    // 35043 — EL-CR-8.6-play; EL-CR-1.11-clicks.
    private static readonly CardBehaviorDefinition Nanomanagement = new(
        ElevationCardCodes.Nanomanagement,
        PlayerSide.Corp,
        costs: [Credits(4)],
        instructions: [GainClicks(2)]);

    // 35014 — EL-CR-8.6-play; EL-CR-6.7-success. The run is the run-event
    // contract; the success gain is this generic self trigger.
    private static readonly CardBehaviorDefinition CleanGetaway = new(
        ElevationCardCodes.CleanGetaway,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("clean-getaway-success", OccurrenceKind.RunSuccessful, BehaviorTriggerScope.Self, [Gain(6)]),
        ]);

    // 35011 — EL-CR-9.5-paid-abilities.
    private static readonly CardBehaviorDefinition RentRioters = new(
        ElevationCardCodes.RentRioters,
        PlayerSide.Runner,
        paidAbilities:
        [
            ClickAbility("rent-rioters-cash-out", [Click(3), TrashSelfCost()], [Gain(9)]),
        ]);

    // 35026 — EL-CR-8.6-play; EL-CR-1.11-clicks. EL-AMOUNT: clicks remaining.
    private static readonly CardBehaviorDefinition Ritual = new(
        ElevationCardCodes.Ritual,
        PlayerSide.Runner,
        costs: [Credits(0)],
        instructions:
        [
            new BehaviorInstruction(
                BehaviorInstructionKind.Draw,
                amountOperand: BehaviorAmountOperand.Of(BehaviorAmountKind.ControllerClicks)),
        ]);

    // 35015 — EL-CR-8.6-play; EL-CR-1.16-costs; EL-CR-10.5-tags. Double:
    // the additional click is a Clicks play cost.
    private static readonly CardBehaviorDefinition LieLow = new(
        ElevationCardCodes.LieLow,
        PlayerSide.Runner,
        costs: [Credits(1), Click(1)],
        instructions:
        [
            Choose(new BehaviorChoiceSpec(
                "lie-low-mode",
                BehaviorChooser.Controller,
                BehaviorChoiceMode.Mode,
                BehaviorChoiceCardinality.Exactly(1),
                [
                    new BehaviorChoiceOption("draw", [Draw(4)]),
                    new BehaviorChoiceOption("remove-tags", [new BehaviorInstruction(BehaviorInstructionKind.RemoveTags, amount: 2)]),
                ])),
        ]);

    // 35017 — EL-CR-6.7-success; EL-CR-10.5-tags. EL-AMOUNT: twice the
    // credits the Corp actually lost.
    private static readonly CardBehaviorDefinition TransferOfWealth = new(
        ElevationCardCodes.TransferOfWealth,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("transfer-of-wealth-success", OccurrenceKind.RunSuccessful, BehaviorTriggerScope.Self,
            [
                new BehaviorInstruction(BehaviorInstructionKind.AddTags, amount: 1, recipient: BehaviorPlayerSelector.Runner),
                new BehaviorInstruction(BehaviorInstructionKind.LoseCredits, amount: 3,
                    recipient: BehaviorPlayerSelector.Corp, recordAs: "corp-lost"),
                new BehaviorInstruction(BehaviorInstructionKind.GainCredits,
                    amountOperand: BehaviorAmountOperand.Recorded("corp-lost", multiplier: 2)),
            ]),
        ]);

    // 35065 — EL-CR-10.5-tags; EL-CR-8.6-play. Remove any number of tags:
    // one option per count, each offered while the Runner has that many.
    private const int BiggerPictureMaximumTags = 15;

    private static readonly CardBehaviorDefinition BiggerPicture = new(
        ElevationCardCodes.BiggerPicture,
        PlayerSide.Corp,
        costs: [Credits(0)],
        conditions: [BehaviorCondition.Tagged(PlayerSide.Runner)],
        instructions:
        [
            Choose(new BehaviorChoiceSpec(
                "bigger-picture-mode",
                BehaviorChooser.Controller,
                BehaviorChoiceMode.Mode,
                BehaviorChoiceCardinality.Exactly(1),
                [
                    new BehaviorChoiceOption("give-tag",
                        [new BehaviorInstruction(BehaviorInstructionKind.AddTags, amount: 1, recipient: BehaviorPlayerSelector.Runner)]),
                    .. Enumerable.Range(1, BiggerPictureMaximumTags).Select(count => new BehaviorChoiceOption(
                        $"remove-{count}-tags",
                        [
                            new BehaviorInstruction(BehaviorInstructionKind.RemoveTags, amount: count),
                            new BehaviorInstruction(BehaviorInstructionKind.LoseCredits, amount: 5 * count,
                                recipient: BehaviorPlayerSelector.Runner, recordAs: "runner-lost"),
                            new BehaviorInstruction(BehaviorInstructionKind.GainCredits,
                                amountOperand: BehaviorAmountOperand.Recorded("runner-lost")),
                        ],
                        condition: BehaviorCondition.RunnerTagsAtLeast(count))),
                ])),
        ]);

    // 35078 — EL-CR-1.17.1a-threat; EL-CR-10.4-damage. The trash cost is
    // runtime metadata (EL-OPERATION-TRASH-COST).
    private static readonly CardBehaviorDefinition MeasuredResponse = new(
        ElevationCardCodes.MeasuredResponse,
        PlayerSide.Corp,
        costs: [Credits(5)],
        conditions:
        [
            BehaviorCondition.ThreatLevelAtLeast(4),
            new BehaviorCondition(BehaviorConditionKind.SuccessfulRunDuringRunnerLastTurn),
        ],
        instructions:
        [
            Choose(new BehaviorChoiceSpec(
                "measured-response-pay-or-damage",
                BehaviorChooser.Runner,
                BehaviorChoiceMode.Mode,
                BehaviorChoiceCardinality.Exactly(1),
                [
                    new BehaviorChoiceOption("pay-eight", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
                        costs: [Credits(8)]),
                    new BehaviorChoiceOption("take-damage",
                        [new BehaviorInstruction(BehaviorInstructionKind.DealDamage, amount: 4, damageKind: DamageKind.Meat)],
                        semantic: BehaviorChoiceOptionSemantic.Fallback),
                ])),
        ]);

    // 35044 — EL-CR-8.6-play; EL-CR-8.5-install.
    private static readonly CardBehaviorDefinition TopDownSolutions = new(
        ElevationCardCodes.TopDownSolutions,
        PlayerSide.Corp,
        costs: [Credits(2)],
        instructions:
        [
            Draw(2),
            InstallFrom(BehaviorZone.Headquarters, PlayerSide.Corp),
            InstallFrom(BehaviorZone.Headquarters, PlayerSide.Corp),
        ]);

    // 35025 — EL-CR-6.7-success; EL-CR-8.5-install. Up to three installs
    // from the grip, each 1 cheaper.
    private static readonly CardBehaviorDefinition Illumination = new(
        ElevationCardCodes.Illumination,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("illumination-success", OccurrenceKind.RunSuccessful, BehaviorTriggerScope.Self,
            [
                InstallFrom(BehaviorZone.Grip, PlayerSide.Runner, discount: 1),
                InstallFrom(BehaviorZone.Grip, PlayerSide.Runner, discount: 1),
                InstallFrom(BehaviorZone.Grip, PlayerSide.Runner, discount: 1),
            ]),
        ]);

    // 35077 — EL-CR-8.6-play; EL-CR-9.12.6-modal. Two different modes in
    // either order: one option per ordered pair.
    private static readonly (string Key, IReadOnlyList<BehaviorInstruction> Instructions)[] KpiModes =
    [
        ("draw-shuffle",
        [
            Draw(1),
            Choose(new BehaviorChoiceSpec(
                "kpi-shuffle-back",
                BehaviorChooser.Controller,
                BehaviorChoiceMode.Mandatory,
                BehaviorChoiceCardinality.Exactly(1),
                target: BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, new BehaviorCardFilter(ownerSide: PlayerSide.Corp)))),
            new BehaviorInstruction(BehaviorInstructionKind.ShuffleIntoDeck, target: ChosenBy("kpi-shuffle-back"),
                sourceZone: BehaviorZone.Headquarters, destinationZone: BehaviorZone.Deck),
        ]),
        ("install-ice",
        [
            InstallFrom(BehaviorZone.Headquarters, PlayerSide.Corp, BehaviorCardType.Ice, optional: false,
                costRule: BehaviorInstallCostRule.IgnoreAllCosts),
        ]),
        ("advance", AdvanceInstalled("kpi-advance", 1)),
        ("gain", [Gain(2)]),
    ];

    private static readonly CardBehaviorDefinition KeyPerformanceIndicators = new(
        ElevationCardCodes.KeyPerformanceIndicators,
        PlayerSide.Corp,
        costs: [Credits(1)],
        instructions:
        [
            Choose(new BehaviorChoiceSpec(
                "kpi-modes",
                BehaviorChooser.Controller,
                BehaviorChoiceMode.Mode,
                BehaviorChoiceCardinality.Exactly(1),
                KpiModes
                    .SelectMany(first => KpiModes
                        .Where(second => second.Key != first.Key)
                        .Select(second => new BehaviorChoiceOption(
                            $"{first.Key}-then-{second.Key}",
                            [.. first.Instructions, .. second.Instructions])))
                    .ToArray())),
        ]);

    public static IReadOnlyList<AssetBehaviorSpec> AssetSpecs { get; } =
    [
        new(ElevationCardCodes.OttoCampaign, trashCost: 2),
        new(ElevationCardCodes.AnthillExcavationContract, trashCost: 1),
    ];

    public static IReadOnlyList<RunEventBehaviorSpec> RunEventSpecs { get; } =
    [
        new(ElevationCardCodes.CleanGetaway, cost: 3, hostedCredits: 0),
        new(ElevationCardCodes.TransferOfWealth, cost: 0, hostedCredits: 0,
            allowedTargetServers: [ServerKind.Headquarters]),
        new(ElevationCardCodes.Illumination, cost: 0, hostedCredits: 0,
            allowedTargetServers: [ServerKind.RAndD]),
    ];

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
    [
        OttoCampaign,
        AnthillExcavationContract,
        Nanomanagement,
        CleanGetaway,
        RentRioters,
        Ritual,
        LieLow,
        TransferOfWealth,
        BiggerPicture,
        MeasuredResponse,
        TopDownSolutions,
        Illumination,
        KeyPerformanceIndicators,
    ];
}
