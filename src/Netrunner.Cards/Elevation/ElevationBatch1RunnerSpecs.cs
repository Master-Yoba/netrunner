using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// EL.3 Runner programs, resources and events. CR-26.03 citations are in
/// <c>data/elevation/batch-registry.json</c>.
/// </summary>
public static class ElevationBatch1RunnerSpecs
{
    private static readonly BehaviorTarget AccessedNonAgenda = new(
        BehaviorTargetKind.AccessedCard,
        filter: new BehaviorCardFilter(BehaviorCardType.NonAgenda));

    // 35007 — EL-CR-7.1-access; EL-CR-9.5-paid-abilities.
    private static readonly CardBehaviorDefinition Gourmand = new(
        ElevationCardCodes.Gourmand,
        PlayerSide.Runner,
        paidAbilities:
        [
            PaidAbility(
                "gourmand-access",
                BehaviorAbilityTiming.BreachAccessPaidWindow,
                [TrashSelfCost()],
                [new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: AccessedNonAgenda), Draw(1)],
                targets: [AccessedNonAgenda]),
        ]);

    // 35008 — EL-CR-3.9.5-interface; EL-CR-1.9-counters. No credit pump; a
    // hosted virus counter pays for +2 strength this encounter.
    private static readonly IcebreakerBehaviorSpec HantuBreaker = new(
        ElevationCardCodes.Hantu,
        baseStrength: 2,
        interfaceType: IceType.Sentry,
        breakCost: 1,
        breakLimit: 1,
        pumpCost: 0,
        pumpStrength: 0);

    private static readonly CardBehaviorDefinition Hantu = new(
        ElevationCardCodes.Hantu,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("hantu-install", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Self,
                [AddCounter(VirusCounter, 2)]),
        ],
        paidAbilities:
        [
            PaidAbility(
                "hantu-virus-strength",
                BehaviorAbilityTiming.EncounterPaidWindow,
                [CounterCost(VirusCounter)],
                [new BehaviorInstruction(BehaviorInstructionKind.ModifySourceStrength, amount: 2, argument: "hantu-virus-strength")]),
        ]);

    private const string VirusCounter = "virus";

    // 35009 — EL-CR-3.9.5-interface; EL-CR-9.4-static. +1 strength per
    // fracter in the heap, counted from public heap cards.
    private static readonly IcebreakerBehaviorSpec RisingTide = new(
        ElevationCardCodes.RisingTide,
        baseStrength: 1,
        interfaceType: IceType.Barrier,
        breakCost: 1,
        breakLimit: 1,
        pumpCost: 1,
        pumpStrength: 1,
        valueModifiers:
        [
            BehaviorValueModifier.PerCardInZoneOf(
                BehaviorZone.Heap,
                new BehaviorCardFilter(BehaviorCardType.Any, subtype: "fracter", ownerSide: PlayerSide.Runner),
                delta: 1,
                reasonKey: "rising-tide-heap-fracters"),
        ]);

    // 35032 — EL-CR-3.9.5-interface; EL-CR-9.4-static.
    private static readonly IcebreakerBehaviorSpec PrincipiaBreaker = new(
        ElevationCardCodes.Principia,
        baseStrength: 2,
        interfaceType: IceType.Barrier,
        breakCost: 1,
        breakLimit: 1,
        pumpCost: 2,
        pumpStrength: 2);

    private static readonly CardBehaviorDefinition Principia = new(
        ElevationCardCodes.Principia,
        PlayerSide.Runner,
        costModifiers:
        [
            BehaviorCostModifier.Always(
                BehaviorCostModifierKind.RunnerInstallCreditCost,
                delta: -1,
                reasonKey: "principia-other-icebreakers",
                perInstalledFilter: new BehaviorCardFilter(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner)),
        ]);

    // 35020 — EL-CR-3.9.5-interface. The pump costs 2 less during a run a
    // run event started.
    private static readonly IcebreakerBehaviorSpec SangKancilBreaker = new(
        ElevationCardCodes.SangKancil,
        baseStrength: 2,
        interfaceType: IceType.CodeGate,
        breakCost: 1,
        breakLimit: 1,
        pumpCost: 3,
        pumpStrength: 2);

    private static readonly CardBehaviorDefinition SangKancil = new(
        ElevationCardCodes.SangKancil,
        PlayerSide.Runner,
        costModifiers:
        [
            new BehaviorCostModifier(
                BehaviorCostModifierKind.IcebreakerPumpCreditCost,
                delta: -2,
                BehaviorCondition.RunEventActive(),
                reasonKey: "sang-kancil-run-event"),
        ]);

    public static IReadOnlyList<IcebreakerBehaviorSpec> IcebreakerSpecs { get; } =
    [
        HantuBreaker,
        RisingTide,
        PrincipiaBreaker,
        SangKancilBreaker,
    ];

    // 35010 — EL-CR-9.1-triggered; EL-ACTION-PHASE-END; EL-SABOTAGE.
    private static readonly CardBehaviorDefinition Cacophony = new(
        ElevationCardCodes.Cacophony,
        PlayerSide.Runner,
        triggers:
        [
            Trigger(
                "cacophony-steal",
                OccurrenceKind.AgendaScoredOrStolen,
                BehaviorTriggerScope.Any,
                [AddCounter(PowerCounter, 1)],
                occurrenceController: PlayerSide.Runner,
                requirement: BehaviorCondition.TurnEventCountIs(TurnEventKind.CorpCardStolenOrTrashed, 1)),
            Trigger(
                "cacophony-trash",
                OccurrenceKind.AccessedCardTrashed,
                BehaviorTriggerScope.Any,
                [AddCounter(PowerCounter, 1)],
                requirement: BehaviorCondition.TurnEventCountIs(TurnEventKind.CorpCardStolenOrTrashed, 1)),
            Trigger(
                "cacophony-sabotage",
                OccurrenceKind.ActionPhaseEnded,
                BehaviorTriggerScope.Controller,
                [
                    RemoveCounter(PowerCounter, 2),
                    ChooseCards(
                        "cacophony-sabotage-hq",
                        BehaviorTarget.CardInZone(BehaviorZone.Headquarters, new BehaviorCardFilter(ownerSide: PlayerSide.Corp)),
                        BehaviorChoiceCardinality.UpTo(3),
                        BehaviorChooser.Corp),
                    new BehaviorInstruction(BehaviorInstructionKind.Sabotage, amount: 3, target: ChosenBy("cacophony-sabotage-hq")),
                ],
                TriggerMode.Optional,
                requirement: CounterAtLeast(PowerCounter, 2)),
        ]);

    // 35023 — EL-FLIP; EL-CR-6.7-success.
    private static readonly CardBehaviorDefinition DewiSubrotoputri = new(
        ElevationCardCodes.DewiSubrotoputri,
        PlayerSide.Runner,
        triggers:
        [
            Trigger(
                "dewi-front-success",
                OccurrenceKind.RunSuccessful,
                BehaviorTriggerScope.Controller,
                [FlipSelf(), Gain(1)],
                TriggerMode.Optional,
                requirement: new BehaviorCondition(BehaviorConditionKind.UnusedMemoryAtMost, value: 0),
                face: BehaviorFace.Front),
            Trigger(
                "dewi-back-success",
                OccurrenceKind.RunSuccessful,
                BehaviorTriggerScope.Controller,
                [FlipSelf(), Draw(1)],
                TriggerMode.Optional,
                requirement: new BehaviorCondition(BehaviorConditionKind.UnusedMemoryAtLeast, value: 1),
                face: BehaviorFace.Back),
        ]);

    private static readonly BehaviorInstruction SideHustlePayout = Branch(
        CounterAtLeast("credit", 6),
        [TakeAllHosted(), TrashSelf(), Draw(1)]);

    // 35034 — EL-CR-10.9-hosted-credits; a run begins (CR 6.9.1).
    private static readonly CardBehaviorDefinition SideHustle = new(
        ElevationCardCodes.SideHustle,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("side-hustle-install", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Self,
                [PlaceHosted(1), SideHustlePayout]),
            Trigger("side-hustle-run", OccurrenceKind.RunBegan, BehaviorTriggerScope.Any,
                [PlaceHosted(1), SideHustlePayout]),
        ]);

    // 35033 — a run begins (CR 6.9.1); EL-AMOUNT (printed install cost).
    private static readonly CardBehaviorDefinition KnickknackOBrian = new(
        ElevationCardCodes.KnickknackOBrian,
        PlayerSide.Runner,
        triggers:
        [
            Trigger(
                "knickknack-run",
                OccurrenceKind.RunBegan,
                BehaviorTriggerScope.Any,
                [
                    ChooseCards(
                        "knickknack-trash",
                        Installed(new BehaviorCardFilter(ownerSide: PlayerSide.Runner), other: true),
                        BehaviorChoiceCardinality.Exactly(1)),
                    new BehaviorInstruction(
                        BehaviorInstructionKind.GainCredits,
                        amountOperand: BehaviorAmountOperand.Of(BehaviorAmountKind.ChosenCardInstallCost)),
                    new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("knickknack-trash")),
                    Draw(1),
                ],
                TriggerMode.Optional,
                requirement: BehaviorCondition.TurnEventCountIs(TurnEventKind.RunBegan, 1)),
        ]);

    // 35031 — EL-CR-7.4-breach (additional access); EL-CR-1.9-counters.
    private static readonly CardBehaviorDefinition DevadattaDrone = new(
        ElevationCardCodes.DevadattaDrone,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("devadatta-install", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Self, [AddCounter(PowerCounter, 2)]),
            Trigger(
                "devadatta-breach",
                OccurrenceKind.RunSuccessful,
                BehaviorTriggerScope.Controller,
                [
                    RemoveCounter(PowerCounter, 1),
                    new BehaviorInstruction(BehaviorInstructionKind.AddBreachAccess, amount: 1, argument: "devadatta-drone-access"),
                ],
                TriggerMode.Optional,
                server: ServerKind.RAndD,
                requirement: CounterAtLeast(PowerCounter, 1)),
        ]);

    // 35029 — EL-RECURRING (trash costs).
    private static readonly CardBehaviorDefinition Azimat = new(
        ElevationCardCodes.Azimat,
        PlayerSide.Runner,
        triggers: [Trigger("azimat-install", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Self, [RefillRecurring()])],
        hostedCreditUses: [new BehaviorHostedCreditUse(BehaviorHostedCreditUseKind.TrashCost)],
        recurringCredits: 2);

    // 35022 — EL-CR-10.9-load-empty; hosted credits for job and connection installs.
    private static readonly CardBehaviorDefinition OpenMarket = new(
        ElevationCardCodes.OpenMarket,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("open-market-install", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Self,
                [LoadHosted(6, HostedCreditSpendRestriction.DeclaredUseOnly)]),
            Trigger("open-market-empty", OccurrenceKind.HostedCreditPoolEmptied, BehaviorTriggerScope.Self, [TrashSelf()]),
            Trigger("open-market-turn-begin", OccurrenceKind.TurnBegin, BehaviorTriggerScope.Controller, [TakeHosted(1)]),
        ],
        hostedCreditUses: [new BehaviorHostedCreditUse(BehaviorHostedCreditUseKind.InstallResource, ["connection", "job"])]);

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
    [
        Azimat,
        OpenMarket,
        DevadattaDrone,
        Gourmand,
        Hantu,
        Principia,
        SangKancil,
        Cacophony,
        DewiSubrotoputri,
        SideHustle,
        KnickknackOBrian,
    ];
}
