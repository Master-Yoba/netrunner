using System.Collections.ObjectModel;

using Netrunner.Core;

namespace Netrunner.Cards;

/// <summary>
/// Generic, immutable behavior declarations for Slice 4.2 Batch 1.
///
/// This module is deliberately separate from the M2 compatibility
/// registrations. It is composed by <see cref="GatewayBehaviorCatalog.Create"/>
/// only, so loading this batch cannot mutate the process-wide legacy catalog.
/// Values are authored from the authenticated System Gateway card metadata and
/// CR primitive audit; this module does not parse or embed card text. The
/// content provenance is the local authenticated snapshot named by
/// <c>data/vendor/system-gateway/manifest.json</c>; rules citations are the
/// CR-26.03 rows in <c>docs/history/M4_PRIMITIVES.md</c>.
/// </summary>
public static class GatewayBatch1BehaviorSpecs
{
    private static readonly BehaviorTarget ChosenCard =
        new(BehaviorTargetKind.ChosenCard);

    private static readonly BehaviorTarget InstalledCard =
        new(BehaviorTargetKind.InstalledCard);

    private static readonly BehaviorTarget CorpInstalledCardNotThisTurn =
        new(
            BehaviorTargetKind.InstalledCardNotThisTurn,
            filter: new BehaviorCardFilter(ownerSide: PlayerSide.Corp));

    private static readonly BehaviorTarget HeadquartersCardsTarget =
        BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, BehaviorCardFilter.Any);

    private static readonly BehaviorCardFilter MutualFavorIcebreakerFilter =
        new(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner);

    private static readonly BehaviorTarget MutualFavorSearchTarget =
        BehaviorTarget.CardInZone(
            BehaviorZone.Stack,
            MutualFavorIcebreakerFilter,
            reveal: false);

    private static readonly BehaviorChoiceSpec MutualFavorDispositionChoice = new(
        "mutual-favor-disposition",
        BehaviorChooser.Controller,
        BehaviorChoiceMode.Mode,
        BehaviorChoiceCardinality.Exactly(1),
        [
            new BehaviorChoiceOption(
                "install",
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.InstallCard,
                        target: ChosenCard,
                        sourceZone: BehaviorZone.Stack,
                        destinationZone: BehaviorZone.Rig),
                ]),
            new BehaviorChoiceOption(
                "grip",
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.AddToHand,
                        target: ChosenCard,
                        sourceZone: BehaviorZone.Stack,
                        destinationZone: BehaviorZone.Grip),
                ]),
        ]);

    private static readonly BehaviorChoiceSpec WildcatStrikeChoice = new(
        "wildcat-strike-effect",
        BehaviorChooser.Corp,
        BehaviorChoiceMode.Mode,
        BehaviorChoiceCardinality.Exactly(1),
        [
            new BehaviorChoiceOption(
                "gain",
                [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 6)]),
            new BehaviorChoiceOption(
                "draw",
                [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 4)]),
        ]);

    private static readonly BehaviorChoiceSpec SprintHeadquartersChoice = new(
        "sprint-headquarters",
        BehaviorChooser.Controller,
        BehaviorChoiceMode.TargetSelection,
        BehaviorChoiceCardinality.Exactly(2),
        target: HeadquartersCardsTarget);

    private static readonly BehaviorChoiceSpec HanseiReviewHeadquartersChoice = new(
        "hansei-review-headquarters",
        BehaviorChooser.Controller,
        // Mandatory means the target is required whenever the candidate set
        // is non-empty; the action resolver must skip the prompt for an empty
        // HQ instead of deadlocking on an impossible exactly-one selection.
        BehaviorChoiceMode.Mandatory,
        BehaviorChoiceCardinality.Exactly(1),
        target: HeadquartersCardsTarget);

    private static readonly BehaviorChoiceSpec SeamlessLaunchChoice = new(
        "seamless-launch-target",
        BehaviorChooser.Corp,
        BehaviorChoiceMode.Mandatory,
        BehaviorChoiceCardinality.Exactly(1),
        target: CorpInstalledCardNotThisTurn);

    private static readonly BehaviorChoiceSpec PredictivePlanogramSingleChoice = new(
        "predictive-planogram-single-effect",
        BehaviorChooser.Controller,
        BehaviorChoiceMode.Mode,
        BehaviorChoiceCardinality.Exactly(1),
        [
            new BehaviorChoiceOption(
                "gain",
                [new BehaviorInstruction(BehaviorInstructionKind.GainCredits, amount: 3)]),
            new BehaviorChoiceOption(
                "draw",
                [new BehaviorInstruction(BehaviorInstructionKind.Draw, amount: 3)]),
        ]);

    private static readonly BehaviorChoiceSpec PredictivePlanogramOrderChoice = new(
        "predictive-planogram-both-order",
        BehaviorChooser.Controller,
        BehaviorChoiceMode.OrderSelection,
        BehaviorChoiceCardinality.Exactly(1),
        [
            new BehaviorChoiceOption(
                "gain-first",
                [Gain(3), Draw(3)]),
            new BehaviorChoiceOption(
                "draw-first",
                [Draw(3), Gain(3)]),
        ]);

    private static readonly BehaviorChoiceSpec PredictivePlanogramTaggedChoice = new(
        "predictive-planogram-tagged-effect",
        BehaviorChooser.Controller,
        BehaviorChoiceMode.Mode,
        BehaviorChoiceCardinality.Exactly(1),
        [
            new BehaviorChoiceOption(
                "gain",
                [Gain(3)]),
            new BehaviorChoiceOption(
                "draw",
                [Draw(3)]),
            new BehaviorChoiceOption(
                "both",
                [Choose(PredictivePlanogramOrderChoice)]),
        ]);

    // 30002 — CR-8.6-play; Corp-owned mode choice.
    private static readonly CardBehaviorDefinition WildcatStrike = new(
        GatewayBatch1CardCodes.WildcatStrike,
        PlayerSide.Runner,
        costs: [CreditCost(2)],
        instructions: [Choose(WildcatStrikeChoice)]);

    // 30011 — CR-8.6-play / CR-8.7-search / CR-8.5-install. The search is
    // private and bounded to at most one Runner-owned icebreaker in Stack;
    // the chosen card remains bound through the shuffle/reveal branches.
    private static readonly CardBehaviorDefinition MutualFavor = new(
        GatewayBatch1CardCodes.MutualFavor,
        PlayerSide.Runner,
        costs: [CreditCost(0)],
        instructions:
        [
            new BehaviorInstruction(
                BehaviorInstructionKind.Search,
                argument: "mutual-favor-search",
                target: MutualFavorSearchTarget,
                sourceZone: BehaviorZone.Stack,
                filter: MutualFavorIcebreakerFilter),
            new BehaviorInstruction(
                BehaviorInstructionKind.ShuffleSearchedDeck,
                sourceZone: BehaviorZone.Stack),
            new BehaviorInstruction(
                BehaviorInstructionKind.Branch,
                branch: new BehaviorBranchSpec(
                    BehaviorCondition.FoundCard(),
                    whenTrue:
                    [
                        new BehaviorInstruction(
                            BehaviorInstructionKind.Reveal,
                            target: ChosenCard),
                        new BehaviorInstruction(
                            BehaviorInstructionKind.Branch,
                            branch: new BehaviorBranchSpec(
                                BehaviorCondition.SuccessfulRunThisTurn(),
                                whenTrue: [Choose(MutualFavorDispositionChoice)],
                                whenFalse:
                                [
                                    new BehaviorInstruction(
                                        BehaviorInstructionKind.AddToHand,
                                        target: ChosenCard,
                                        sourceZone: BehaviorZone.Stack,
                                        destinationZone: BehaviorZone.Grip),
                                ],
                                key: "mutual-favor-successful-run")),
                    ],
                    whenFalse: [],
                    key: "mutual-favor-found-card")),
        ]);

    // 30020 — CR-8.6-play / CR-5.4-action-phase.
    private static readonly CardBehaviorDefinition CreativeCommission = new(
        GatewayBatch1CardCodes.CreativeCommission,
        PlayerSide.Runner,
        costs: [CreditCost(1)],
        instructions:
        [
            Gain(5),
            new BehaviorInstruction(
                BehaviorInstructionKind.LoseClicks,
                amount: 1,
                condition: BehaviorCondition.ClicksRemainingAtLeast(1)),
        ]);

    // 30021 — CR-8.4-draw / CR-5.4-action-phase.
    private static readonly CardBehaviorDefinition VRcation = new(
        GatewayBatch1CardCodes.VRcation,
        PlayerSide.Runner,
        costs: [CreditCost(1)],
        instructions:
        [
            Draw(4),
            new BehaviorInstruction(
                BehaviorInstructionKind.LoseClicks,
                amount: 1,
                condition: BehaviorCondition.ClicksRemainingAtLeast(1)),
        ]);

    // 30030 — CR-8.6-play.
    private static readonly CardBehaviorDefinition SureGamble = new(
        GatewayBatch1CardCodes.SureGamble,
        PlayerSide.Runner,
        costs: [CreditCost(5)],
        instructions: [Gain(9)]);

    // 30031 — CR-3.8-console / CR-8.5-install; static values only.
    private static readonly CardBehaviorDefinition T400MemoryDiamond = new(
        GatewayBatch1CardCodes.T400MemoryDiamond,
        PlayerSide.Runner,
        costs: [CreditCost(2)],
        staticModifiers:
        [
            new BehaviorStaticModifier(BehaviorStaticModifierKind.MemoryUnits, delta: 1),
            new BehaviorStaticModifier(BehaviorStaticModifierKind.MaximumHandSize, delta: 1),
        ]);

    // 30041 — CR-8.4-draw / CR-8.3-arrange.
    private static readonly CardBehaviorDefinition Sprint = new(
        GatewayBatch1CardCodes.Sprint,
        PlayerSide.Corp,
        costs: [CreditCost(0)],
        instructions:
        [
            Draw(3),
            Choose(SprintHeadquartersChoice),
            new BehaviorInstruction(
                BehaviorInstructionKind.ShuffleIntoDeck,
                target: ChosenCard,
                sourceZone: BehaviorZone.Headquarters,
                destinationZone: BehaviorZone.Deck),
        ]);

    // 30048 — CR-8.6-play / CR-4.3-hidden; mandatory-if-able HQ choice.
    private static readonly CardBehaviorDefinition HanseiReview = new(
        GatewayBatch1CardCodes.HanseiReview,
        PlayerSide.Corp,
        costs: [CreditCost(5)],
        instructions:
        [
            Gain(10),
            new BehaviorInstruction(
                BehaviorInstructionKind.Choose,
                choice: HanseiReviewHeadquartersChoice),
            new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenCard),
        ]);

    // 30040 — CR-8.6-play / CR-1.18-advance. The real play reducer owns the
    // printed one-credit play cost; the behavior only selects an eligible
    // Corp-installed card and applies the exact two-counter effect.
    private static readonly CardBehaviorDefinition SeamlessLaunch = new(
        GatewayBatch1CardCodes.SeamlessLaunch,
        PlayerSide.Corp,
        costs: [CreditCost(1)],
        targets: [CorpInstalledCardNotThisTurn],
        instructions:
        [
            new BehaviorInstruction(
                BehaviorInstructionKind.Choose,
                choice: SeamlessLaunchChoice),
            new BehaviorInstruction(
                BehaviorInstructionKind.AdvanceCard,
                amount: 2,
                target: ChosenCard),
        ]);

    // 30056 — CR-8.6-play / CR-8.4-draw; tagged branch and ordered both mode.
    private static readonly CardBehaviorDefinition PredictivePlanogram = new(
        GatewayBatch1CardCodes.PredictivePlanogram,
        PlayerSide.Corp,
        costs: [CreditCost(0)],
        instructions:
        [
            new BehaviorInstruction(
                BehaviorInstructionKind.Branch,
                branch: new BehaviorBranchSpec(
                    BehaviorCondition.Tagged(PlayerSide.Runner),
                    whenTrue: [Choose(PredictivePlanogramTaggedChoice)],
                    whenFalse: [Choose(PredictivePlanogramSingleChoice)],
                    key: "predictive-planogram-tagged")),
        ]);

    // 30064 — CR-8.6-play.
    private static readonly CardBehaviorDefinition GovernmentSubsidy = new(
        GatewayBatch1CardCodes.GovernmentSubsidy,
        PlayerSide.Corp,
        costs: [CreditCost(10)],
        instructions: [Gain(15)]);

    // 30075 — CR-8.6-play.
    private static readonly CardBehaviorDefinition HedgeFund = new(
        GatewayBatch1CardCodes.HedgeFund,
        PlayerSide.Corp,
        costs: [CreditCost(5)],
        instructions: [Gain(9)]);

    /// <summary>All twelve assigned Batch 1 IDs.</summary>
    public static IReadOnlyList<CardDefinitionId> AssignedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>
        ([
            GatewayBatch1CardCodes.WildcatStrike,
            GatewayBatch1CardCodes.MutualFavor,
            GatewayBatch1CardCodes.CreativeCommission,
            GatewayBatch1CardCodes.VRcation,
            GatewayBatch1CardCodes.SureGamble,
            GatewayBatch1CardCodes.T400MemoryDiamond,
            GatewayBatch1CardCodes.SeamlessLaunch,
            GatewayBatch1CardCodes.Sprint,
            GatewayBatch1CardCodes.HanseiReview,
            GatewayBatch1CardCodes.PredictivePlanogram,
            GatewayBatch1CardCodes.GovernmentSubsidy,
            GatewayBatch1CardCodes.HedgeFund,
        ]);

    /// <summary>
    /// IDs intentionally withheld from the executable catalog. This batch has
    /// no blocked definitions now that all twelve declarations have their
    /// serializable Core runtime handoffs.
    /// </summary>
    public static IReadOnlyList<CardDefinitionId> BlockedDefinitionIds { get; } =
        new ReadOnlyCollection<CardDefinitionId>([]);

    /// <summary>
    /// Immutable complete generic declarations. The catalog builder accepts
    /// these declarations repeatedly when the composition graph is assembled
    /// by more than one caller.
    /// </summary>
    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
        new ReadOnlyCollection<CardBehaviorDefinition>(
        [
            WildcatStrike,
            MutualFavor,
            CreativeCommission,
            VRcation,
            SureGamble,
            T400MemoryDiamond,
            Sprint,
            HanseiReview,
            SeamlessLaunch,
            PredictivePlanogram,
            GovernmentSubsidy,
            HedgeFund,
        ]);

    private static BehaviorCost CreditCost(int amount) =>
        new(BehaviorCostKind.Credits, amount);

    private static BehaviorInstruction Gain(int amount) =>
        new(BehaviorInstructionKind.GainCredits, amount);

    private static BehaviorInstruction Draw(int amount) =>
        new(BehaviorInstructionKind.Draw, amount);

    private static BehaviorInstruction Choose(BehaviorChoiceSpec choice) =>
        new(BehaviorInstructionKind.Choose, choice: choice);
}
