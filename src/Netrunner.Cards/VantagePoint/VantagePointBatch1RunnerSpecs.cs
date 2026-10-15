using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// VP.3 Runner cards: bad publicity, stealth credits, scoring and economy. CR-26.03 citations are in
/// <c>data/vantage-point/batch-registry.json</c>.
/// </summary>
public static class VantagePointBatch1RunnerSpecs
{
    private const string Stealth = "stealth";

    private static readonly BehaviorStaticModifier PlusOneMemory = new(BehaviorStaticModifierKind.MemoryUnits, delta: 1);

    /// <summary>VP-STEALTH-CREDITS: credits placed on the source, spendable during runs ("You can spend hosted credits during runs").</summary>
    private static BehaviorInstruction PlaceRunCredits(int amount) =>
        new(BehaviorInstructionKind.PlaceHostedCredits, amount: amount, spendRestriction: HostedCreditSpendRestriction.RunCost);

    // 36002 — VP-CR-10.6-bad-publicity; VP-CR-9.6-conditional. The run is the run-event contract; it is removed from the
    // game when the run ends (VP-RUN-EVENT-REMOVED).
    private static readonly RunEventBehaviorSpec TakeADiveRun = new(
        VantagePointCardCodes.TakeADive,
        cost: 2,
        hostedCredits: 0,
        allowedTargetServers: [ServerKind.Headquarters, ServerKind.RAndD],
        removedFromGameAfterRun: true);

    private static readonly CardBehaviorDefinition TakeADive = new(
        VantagePointCardCodes.TakeADive,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("take-a-dive-success", OccurrenceKind.RunSuccessful, BehaviorTriggerScope.Self,
                [new BehaviorInstruction(BehaviorInstructionKind.TakeBadPublicity, amount: 1)],
                condition: BehaviorTriggerCondition.SubroutineResolvedThisRun),
        ]);

    // 36004 — VP-CR-1.10.4-credit-sources. No pump: the strength ability, paid only with credits from stealth cards,
    // lowers the encountered barrier's strength (VP-ENCOUNTER-SUBTYPE).
    private static readonly IcebreakerBehaviorSpec CorsairBreaker = new(
        VantagePointCardCodes.Corsair,
        baseStrength: 0,
        interfaceType: IceType.Barrier,
        breakCost: 1,
        breakLimit: 1,
        pumpCost: 0,
        pumpStrength: 0);

    private static readonly CardBehaviorDefinition Corsair = new(
        VantagePointCardCodes.Corsair,
        PlayerSide.Runner,
        paidAbilities:
        [
            PaidAbility(
                "corsair-barrier-strength",
                BehaviorAbilityTiming.EncounterPaidWindow,
                [new BehaviorCost(BehaviorCostKind.Credits, 1, creditSourceSubtype: Stealth)],
                [new BehaviorInstruction(BehaviorInstructionKind.ModifyEncounteredIceStrength, amount: -3, argument: "corsair-barrier-strength")],
                conditions: [BehaviorCondition.EncounteringSubtype("barrier")]),
        ]);

    // 36009 — VP-CR-10.5-tags. A click ability of the identity.
    private static readonly CardBehaviorDefinition VirtualIntelligencePI = new(
        VantagePointCardCodes.VirtualIntelligencePI,
        PlayerSide.Runner,
        paidAbilities:
        [
            ClickAbility("vic-draw-untag", [Click(), Credits(1)], [Draw(1), RemoveTags(1)], BehaviorAbilityUsageLimit.OncePerTurn),
        ]);

    // 36011 — VP-CR-1.16-costs. VP-TRASH-AS-COST: the play action names the installed resource it trashes.
    private static readonly CardBehaviorDefinition SellOut = new(
        VantagePointCardCodes.SellOut,
        PlayerSide.Runner,
        costs:
        [
            Credits(1),
            new BehaviorCost(
                BehaviorCostKind.Additional,
                amount: 1,
                additionalKey: "sell-out-trash-resource",
                additionalTarget: BehaviorTarget.CardsInZone(
                    BehaviorZone.Rig,
                    new BehaviorCardFilter(BehaviorCardType.Resource, ownerSide: PlayerSide.Runner)),
                additionalKind: BehaviorAdditionalCostKind.TrashCards),
        ],
        instructions: [Gain(4), Draw(2)]);

    // 36012 — VP-CR-2.3-cost-modifiers. VP-PLAY-COST-MODIFIER: 1 less for each piece of ICE protecting HQ.
    private static readonly RunEventBehaviorSpec Tailgate = new(
        VantagePointCardCodes.Tailgate,
        cost: 3,
        hostedCredits: 0,
        allowedTargetServers: [ServerKind.Headquarters],
        breachAccessModifier: new BehaviorRunBreachAccessModifier(delta: 2, reasonKey: "tailgate-additional-access"),
        costLoweredPerIceOn: ServerKind.Headquarters);

    // 36013 — VP-CR-10.5-tags.
    private static readonly CardBehaviorDefinition BorrowedGoods = new(
        VantagePointCardCodes.BorrowedGoods,
        PlayerSide.Runner,
        staticModifiers: [PlusOneMemory],
        triggers:
        [
            Trigger("borrowed-goods-install", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Self,
                [new BehaviorInstruction(BehaviorInstructionKind.AddTags, amount: 1)],
                requirement: BehaviorCondition.NotTagged(PlayerSide.Runner)),
        ]);

    // 36016 — VP-CR-9.6-conditional. VP-RUN-FACTS: ICE rezzed this turn.
    private static readonly CardBehaviorDefinition UnderdomeIrregulars = new(
        VantagePointCardCodes.UnderdomeIrregulars,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("underdome-action-phase-end", OccurrenceKind.ActionPhaseEnded, BehaviorTriggerScope.Controller,
            [
                Branch(
                    BehaviorCondition.TurnEventCountAtLeast(TurnEventKind.IceRezzed, 1),
                    [
                        ChooseOne(
                            "underdome-reward",
                            BehaviorChooser.Controller,
                            Option("draw", [Draw(2)]),
                            Option("remove-tag", [RemoveTags(1)], condition: BehaviorCondition.Tagged(PlayerSide.Runner))),
                    ],
                    [TrashSelf()]),
            ]),
        ]);

    // 36020 — VP-CR-1.10.4-credit-sources; VP-CR-1.16-costs. The grip hardware trashed is the option's cost.
    private static readonly CardBehaviorDefinition Methuselah = new(
        VantagePointCardCodes.Methuselah,
        PlayerSide.Runner,
        staticModifiers: [PlusOneMemory],
        triggers:
        [
            Trigger("methuselah-run-begin", OccurrenceKind.RunBegan, BehaviorTriggerScope.Controller,
            [
                ChooseOne(
                    "methuselah-trash-hardware",
                    BehaviorChooser.Controller,
                    Option(
                        "trash-hardware",
                        [PlaceRunCredits(2)],
                        costs:
                        [
                            new BehaviorCost(
                                BehaviorCostKind.Additional,
                                amount: 1,
                                additionalKey: "methuselah-grip-hardware",
                                additionalTarget: BehaviorTarget.CardsInZone(
                                    BehaviorZone.Grip,
                                    new BehaviorCardFilter(BehaviorCardType.Hardware, ownerSide: PlayerSide.Runner)),
                                additionalKind: BehaviorAdditionalCostKind.TrashCards),
                        ]),
                    Decline()),
            ]),
        ]);

    // 36021 — VP-CR-1.10.4-credit-sources; VP-CR-9.6-conditional. VP-FIRST-EVENT.
    private static readonly CardBehaviorDefinition Touchstone = new(
        VantagePointCardCodes.Touchstone,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("touchstone-first-event", OccurrenceKind.FirstEventPlayedThisTurn, BehaviorTriggerScope.Controller,
                [PlaceRunCredits(1)]),
        ]);

    // 36024 — VP-CR-1.13-hosting; VP-CR-9.6-conditional. Installed only on a piece of ICE; its server is its host's
    // (VP-HOSTED-SERVER).
    private static readonly CardBehaviorDefinition Stowaway = new(
        VantagePointCardCodes.Stowaway,
        PlayerSide.Runner,
        triggers:
        [
            new BehaviorTrigger(
                "stowaway-successful-run",
                OccurrenceKind.RunSuccessful,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Controller,
                null,
                [Gain(2)],
                requireSourceOnAttackedServer: true),
        ],
        hosting: new HostingBehaviorSpec());

    // 36025 — VP-CR-1.16.10-steal-score-costs; VP-CR-10.1.3-as-an-agenda; VP-CR-9.6-conditional.
    private static readonly CardBehaviorDefinition WordOnTheStreet = new(
        VantagePointCardCodes.WordOnTheStreet,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("word-on-the-street-score", OccurrenceKind.AgendaScoredOrStolen, BehaviorTriggerScope.Any,
                [TrashSelf(), Gain(4), Draw(1)],
                occurrenceController: PlayerSide.Corp,
                condition: BehaviorTriggerCondition.SubjectNotInstalledThisTurn),
        ],
        scoreCostAsAgenda: new BehaviorScoreCostAsAgenda(-1, CannotForfeit: true, OnlyAgendasInstalledThisTurn: true));

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
    [
        TakeADive, Corsair, VirtualIntelligencePI, SellOut, BorrowedGoods, UnderdomeIrregulars, Methuselah, Touchstone,
        Stowaway, WordOnTheStreet,
    ];

    public static IReadOnlyList<RunEventBehaviorSpec> RunEventSpecs { get; } = [TakeADiveRun, Tailgate];

    public static IReadOnlyList<IcebreakerBehaviorSpec> IcebreakerSpecs { get; } = [CorsairBreaker];
}
