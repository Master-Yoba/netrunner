using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// VP.5 Corp cards: ICE, runs and access. Printed strengths, rez costs, agenda points and trash costs are hand-copied;
/// CR-26.03 citations are in <c>data/vantage-point/batch-registry.json</c>.
/// </summary>
public static class VantagePointBatch2CorpSpecs
{
    private static readonly BehaviorCardFilter CorpCard = new(ownerSide: PlayerSide.Corp);

    private static readonly BehaviorCardFilter CorpIce = new(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp);

    private static readonly BehaviorInstruction EndTheRun = new(BehaviorInstructionKind.EndRun);

    private static BehaviorInstruction RunnerDamage(DamageKind kind, int amount) =>
        new(BehaviorInstructionKind.DealDamage, amount: amount, damageKind: kind, recipient: BehaviorPlayerSelector.Runner);

    /// <summary>"... unless the Runner pays N[credit]": the Runner chooses to pay, or the effect.</summary>
    private static BehaviorInstruction UnlessRunnerPays(string key, int credits, IReadOnlyList<BehaviorInstruction> otherwise) =>
        ChooseOne(
            key,
            BehaviorChooser.Runner,
            Option("pay", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)], costs: [Credits(credits)]),
            Option("decline", otherwise));

    /// <summary>"You may draw 1 card."</summary>
    private static BehaviorInstruction MayDraw(string key) =>
        ChooseOne(key, BehaviorChooser.Corp, Option("draw", [Draw(1)]), Option("decline", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)]));

    // 36028 — VP-CR-9.8-subroutines; VP-CR-8.5-install. VP-BIOROID-MULTI-BREAK: "Lose [click][click]: Break up to 2
    // subroutines", only the Runner. The install is Ansel 1.0's ("You may install 1 card from HQ or Archives").
    private static readonly IceBehaviorSpec Ansel20Ice = new(
        VantagePointCardCodes.Ansel20,
        strength: 5,
        IceType.Sentry,
        rezCost: 8,
        [
            new TrashInstalledRunnerCardSubroutineSpec(),
            new BehaviorSubroutineSpec("ansel-2-heap"),
            new InstallCardSubroutineSpec(),
            new EndRunSubroutineSpec(),
        ],
        [],
        encounteredBioroidAbilities:
        [
            new EncounteredBioroidAbilitySpec("ansel-2-break", new BehaviorCost(BehaviorCostKind.LoseClick, 2), breakCount: 2),
        ]);

    private static readonly CardBehaviorDefinition Ansel20 = new(
        VantagePointCardCodes.Ansel20,
        PlayerSide.Corp,
        triggers:
        [
            // "Remove 1 card in the heap from the game": the Corp chooses, and must when the heap has one.
            Subroutine("ansel-2-heap",
            [
                Branch(BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Heap, 1),
                [
                    ChooseCards("ansel-2-heap-card", BehaviorTarget.CardsInZone(BehaviorZone.Heap, new BehaviorCardFilter(ownerSide: PlayerSide.Runner)),
                        BehaviorChoiceCardinality.Exactly(1), BehaviorChooser.Corp),
                    new BehaviorInstruction(BehaviorInstructionKind.MoveCard, target: ChosenBy("ansel-2-heap-card"), sourceZone: BehaviorZone.Heap,
                        destinationZone: BehaviorZone.RemovedFromGame),
                ]),
            ]),
        ]);

    // 36029 — VP-CR-2.3-cost-modifiers. VP-DYNAMIC-REZ-COST: 1 less for each other unrezzed piece of ICE.
    private static readonly IceBehaviorSpec ReverbIce = new(
        VantagePointCardCodes.Reverb,
        strength: 2,
        IceType.Barrier,
        rezCost: 4,
        [new EndRunSubroutineSpec(), new EndRunSubroutineSpec()],
        []);

    private static readonly CardBehaviorDefinition Reverb = new(
        VantagePointCardCodes.Reverb,
        PlayerSide.Corp,
        costModifiers:
        [
            BehaviorCostModifier.Always(BehaviorCostModifierKind.SelfRezCreditCost, -1, "reverb-unrezzed-ice",
                new BehaviorCardFilter(BehaviorCardType.Ice, rezzed: false)),
        ]);

    // 36030 — VP-CR-9.8-subroutines; VP-CR-8.7-search (a shuffle into R&D).
    private static readonly IceBehaviorSpec SleipnirIce = new(
        VantagePointCardCodes.Sleipnir,
        strength: 4,
        IceType.CodeGate,
        rezCost: 4,
        [new BehaviorSubroutineSpec("sleipnir-draw"), new BehaviorSubroutineSpec("sleipnir-shuffle"), new EndRunSubroutineSpec()],
        []);

    private static IReadOnlyList<BehaviorInstruction> ShuffleIntoRAndD(string key, BehaviorZone zone) =>
    [
        ChooseCards(key, BehaviorTarget.CardsInZone(zone, CorpCard), BehaviorChoiceCardinality.UpTo(1), BehaviorChooser.Corp),
        new BehaviorInstruction(BehaviorInstructionKind.ShuffleIntoDeck, target: ChosenBy(key), sourceZone: zone,
            destinationZone: BehaviorZone.Deck),
    ];

    private static readonly CardBehaviorDefinition Sleipnir = new(
        VantagePointCardCodes.Sleipnir,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("sleipnir-draw", [MayDraw("sleipnir-may-draw")]),
            Subroutine("sleipnir-shuffle",
            [
                ChooseOne(
                    "sleipnir-from",
                    BehaviorChooser.Corp,
                    Option("headquarters", ShuffleIntoRAndD("sleipnir-hq", BehaviorZone.Headquarters)),
                    Option("archives", ShuffleIntoRAndD("sleipnir-archives", BehaviorZone.Archives)),
                    Option("decline", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)])),
            ]),
        ]);

    // 36031 — VP-CR-6.5.7-pass-and-break; VP-CR-1.11-clicks. VP-ICE-PASSED and VP-CANNOT-STEAL-OR-TRASH.
    private static readonly IceBehaviorSpec VertigoIce = new(
        VantagePointCardCodes.Vertigo,
        strength: 1,
        IceType.CodeGate,
        rezCost: 1,
        [new BehaviorSubroutineSpec("vertigo-lose-click")],
        []);

    private static readonly CardBehaviorDefinition Vertigo = new(
        VantagePointCardCodes.Vertigo,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("vertigo-lose-click",
                [new BehaviorInstruction(BehaviorInstructionKind.LoseClicks, amount: 1, recipient: BehaviorPlayerSelector.Runner)]),
            Trigger("vertigo-passed", OccurrenceKind.IcePassed, BehaviorTriggerScope.Self,
                [new BehaviorInstruction(BehaviorInstructionKind.RestrictRun, argument: nameof(RunRestrictionKind.CannotStealOrTrashCorpCards))],
                requirement: BehaviorCondition.ClicksAtMost(0, PlayerSide.Runner)),
        ]);

    // 36035 — VP-CR-1.9-counters. VP-CANNOT-STEAL-OR-TRASH: copies of the revealed card, for the remainder of the run.
    private static readonly CardBehaviorDefinition PerfectRecall = new(
        VantagePointCardCodes.PerfectRecall,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("perfect-recall-rez", OccurrenceKind.CardRezzed, BehaviorTriggerScope.Self, [AddCounter(PowerCounter, 1)]),
            Trigger("perfect-recall-agenda", OccurrenceKind.AgendaScoredOrStolen, BehaviorTriggerScope.Any, [AddCounter(PowerCounter, 1)],
                onAttackedServer: true),
        ],
        paidAbilities:
        [
            PaidAbility(
                "perfect-recall-name",
                BehaviorAbilityTiming.AnyPaidWindow,
                [CounterCost(PowerCounter)],
                [
                    ChooseCards("perfect-recall-card", BehaviorTarget.CardsInZone(BehaviorZone.Headquarters, reveal: true),
                        BehaviorChoiceCardinality.Exactly(1)),
                    new BehaviorInstruction(BehaviorInstructionKind.RestrictRun, argument: nameof(RunRestrictionKind.CannotStealOrTrashCorpCards),
                        target: ChosenBy("perfect-recall-card")),
                ],
                conditions: [new BehaviorCondition(BehaviorConditionKind.RunInProgress), BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Headquarters, 1)]),
        ]);

    // 36039 — VP-CR-8.8-swap; VP-CR-8.3-arrange. VP-SWAP-WITH, VP-DECK-BOTTOM and VP-ALL-ICE-STRENGTH.
    private static readonly IceBehaviorSpec EzaMIce = new(
        VantagePointCardCodes.EzaM,
        strength: 3,
        IceType.CodeGate,
        rezCost: 1,
        [new BehaviorSubroutineSpec("ezam-look"), new BehaviorSubroutineSpec("ezam-strength")],
        []);

    private static readonly CardBehaviorDefinition EzaM = new(
        VantagePointCardCodes.EzaM,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("ezam-look",
            [
                ChooseCards("ezam-top", BehaviorTarget.TopOfZone(BehaviorZone.Deck, 1, CorpCard), BehaviorChoiceCardinality.UpTo(1),
                    BehaviorChooser.Corp),
                new BehaviorInstruction(BehaviorInstructionKind.MoveCard, target: ChosenBy("ezam-top"), sourceZone: BehaviorZone.Deck,
                    destinationZone: BehaviorZone.Deck),
            ]),
            Subroutine("ezam-strength", [new BehaviorInstruction(BehaviorInstructionKind.ModifyAllIceStrengthForRun, amount: 1)]),
        ],
        paidAbilities:
        [
            ClickAbility(
                "ezam-swap",
                [Click()],
                [
                    ChooseCards("ezam-swap-other", Installed(CorpIce, other: true), BehaviorChoiceCardinality.Exactly(1)),
                    new BehaviorInstruction(BehaviorInstructionKind.SwapInstalledIce, argument: "self", target: ChosenBy("ezam-swap-other")),
                ]),
        ]);

    // 36041 — VP-CR-6.1.5-jack-out; VP-CR-10.4-damage. VP-UNLESS-JACK-OUT.
    private static readonly IceBehaviorSpec LionsmaneIce = new(
        VantagePointCardCodes.Lionsmane,
        strength: 4,
        IceType.Sentry,
        rezCost: 6,
        [new DamageSubroutineSpec(DamageKind.Net, 2), new BehaviorSubroutineSpec("lionsmane-pay"), new BehaviorSubroutineSpec("lionsmane-jack")],
        []);

    private static readonly CardBehaviorDefinition Lionsmane = new(
        VantagePointCardCodes.Lionsmane,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("lionsmane-pay", [UnlessRunnerPays("lionsmane-pay-choice", 3, [RunnerDamage(DamageKind.Net, 2)])]),
            Subroutine("lionsmane-jack",
            [
                ChooseOne(
                    "lionsmane-jack-choice",
                    BehaviorChooser.Runner,
                    Option("suffer-damage", [RunnerDamage(DamageKind.Net, 2)]),
                    Option("jack-out", [new BehaviorInstruction(BehaviorInstructionKind.JackOut)])),
            ]),
        ]);

    // 36042 — VP-CR-10.5-tags; VP-CR-10.4-damage. VP-TRAP-ICE. X is the Runner's tags as the subroutine resolves.
    private static readonly IceBehaviorSpec VicsekIce = new(
        VantagePointCardCodes.Vicsek,
        strength: 3,
        IceType.Trap,
        rezCost: 2,
        [new BehaviorSubroutineSpec("vicsek-x"), new BehaviorSubroutineSpec("vicsek-tag-trash")],
        []);

    private static readonly CardBehaviorDefinition Vicsek = new(
        VantagePointCardCodes.Vicsek,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("vicsek-x",
            [
                new BehaviorInstruction(BehaviorInstructionKind.DealDamage, damageKind: DamageKind.Net, recipient: BehaviorPlayerSelector.Runner,
                    damageAmountOperand: new BehaviorDamageAmountOperand(BehaviorDamageAmountKind.RunnerTags)),
                new BehaviorInstruction(BehaviorInstructionKind.AddTags, recipient: BehaviorPlayerSelector.Runner,
                    amountOperand: BehaviorAmountOperand.Of(BehaviorAmountKind.RunnerTags, 1)),
            ]),
            Subroutine("vicsek-tag-trash", [GiveTags(1), TrashSelf()]),
        ]);

    // 36044 — VP-CR-10.5-tags; VP-CR-9.8-subroutines. VP-TAG-COST and VP-RESOLVE-SUBROUTINE: one mode per subroutine
    // position, offered while that subroutine of the rezzed ICE can resolve outside an encounter.
    private const int UnleashMostSubroutines = 6;

    private static readonly CardBehaviorDefinition Unleash = new(
        VantagePointCardCodes.Unleash,
        PlayerSide.Corp,
        costs: [new BehaviorCost(BehaviorCostKind.Tags, 1)],
        instructions:
        [
            ChooseCards("unleash-ice", Installed(CorpIce), BehaviorChoiceCardinality.Exactly(1)),
            new BehaviorInstruction(BehaviorInstructionKind.RezIgnoringCosts, target: ChosenBy("unleash-ice")),
            ChooseOne(
                "unleash-subroutine",
                BehaviorChooser.Controller,
                [
                    .. Enumerable.Range(0, UnleashMostSubroutines).Select(ordinal => Option($"subroutine-{ordinal + 1}",
                        [new BehaviorInstruction(BehaviorInstructionKind.ResolveIceSubroutine, amount: ordinal, target: ChosenBy("unleash-ice"))])),
                    Decline(),
                ]),
        ]);

    // 36045 — VP-CR-1.9-counters; VP-CR-9.6-conditional. Central servers only.
    private static readonly CardBehaviorDefinition TheRedRoom = new(
        VantagePointCardCodes.TheRedRoom,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("red-room-agenda", OccurrenceKind.AgendaScoredOrStolen, BehaviorTriggerScope.Any, [AddCounter(PowerCounter, 1)],
                oncePerTurn: true),
        ],
        paidAbilities:
        [
            PaidAbility("red-room-end-run", BehaviorAbilityTiming.AnyPaidWindow, [CounterCost(PowerCounter)], [EndTheRun],
                conditions: [new BehaviorCondition(BehaviorConditionKind.RunAgainstAnotherServer)]),
        ],
        installServerKinds: [ServerKind.Headquarters, ServerKind.RAndD, ServerKind.Archives]);

    // 36051 — VP-CR-6.5.7-pass-and-break; VP-CR-6.5.8-bypass. VP-ICE-PASSED (bypassed or fully broken).
    private static readonly IceBehaviorSpec LetheIce = new(
        VantagePointCardCodes.Lethe,
        strength: 6,
        IceType.Sentry,
        rezCost: 9,
        [new BehaviorSubroutineSpec("lethe-archives"), new BehaviorSubroutineSpec("lethe-bounce")],
        []);

    private static readonly CardBehaviorDefinition Lethe = new(
        VantagePointCardCodes.Lethe,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("lethe-bypassed", OccurrenceKind.IceBypassed, BehaviorTriggerScope.Self, [GiveTags(1)]),
            Trigger("lethe-broken", OccurrenceKind.IceFullyBroken, BehaviorTriggerScope.Self, [GiveTags(1)]),
            Subroutine("lethe-archives",
            [
                ChooseCards("lethe-archives-card", BehaviorTarget.CardsInZone(BehaviorZone.Archives, CorpCard), BehaviorChoiceCardinality.UpTo(1),
                    BehaviorChooser.Corp),
                Branch(BehaviorCondition.Chose("lethe-archives-card"),
                [
                    ChooseOne(
                        "lethe-archives-where",
                        BehaviorChooser.Corp,
                        Option("top", [new BehaviorInstruction(BehaviorInstructionKind.PlaceOnTopOfDeck, target: ChosenBy("lethe-archives-card"))]),
                        Option("bottom", [new BehaviorInstruction(BehaviorInstructionKind.MoveCard, target: ChosenBy("lethe-archives-card"),
                            sourceZone: BehaviorZone.Archives, destinationZone: BehaviorZone.Deck)])),
                ]),
            ]),
            Subroutine("lethe-bounce",
            [
                ChooseCards("lethe-bounce-card", Installed(new BehaviorCardFilter(ownerSide: PlayerSide.Runner)),
                    BehaviorChoiceCardinality.AsManyAsAble(1), BehaviorChooser.Corp),
                new BehaviorInstruction(BehaviorInstructionKind.AddToHand, target: ChosenBy("lethe-bounce-card"), sourceZone: BehaviorZone.Rig,
                    destinationZone: BehaviorZone.Grip),
            ]),
        ]);

    // 36052 — VP-CR-9.6-conditional.
    private static readonly IceBehaviorSpec PaywallIce = new(
        VantagePointCardCodes.Paywall,
        strength: 1,
        IceType.Barrier,
        rezCost: 1,
        [new BehaviorSubroutineSpec("paywall-pay-or-end")],
        []);

    private static readonly CardBehaviorDefinition Paywall = new(
        VantagePointCardCodes.Paywall,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("paywall-encounter", OccurrenceKind.IceEncountered, BehaviorTriggerScope.Self, [RunnerLoses(1)]),
            Subroutine("paywall-pay-or-end", [UnlessRunnerPays("paywall-pay", 1, [EndTheRun])]),
        ]);

    // 36056 — VP-CR-8.5-install; VP-CR-10.4-damage. Installed only faceup; VP-ADVANCED and VP-ADVANCEMENT-COST.
    private static readonly AgendaBehaviorSpec SacrificeZoneExpansionAgenda = new(
        VantagePointCardCodes.SacrificeZoneExpansion, agendaPoints: 2, installsFaceUp: true);

    private static readonly CardBehaviorDefinition SacrificeZoneExpansion = new(
        VantagePointCardCodes.SacrificeZoneExpansion,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("sacrifice-zone-advanced", OccurrenceKind.CardAdvanced, BehaviorTriggerScope.Self, [Gain(3)], oncePerTurn: true),
            new BehaviorTrigger(
                "sacrifice-zone-meat",
                OccurrenceKind.RunSuccessful,
                TriggerMode.Optional,
                BehaviorTriggerScope.Any,
                null,
                [
                    new BehaviorInstruction(BehaviorInstructionKind.RemoveAdvancementCounters, amount: 1, target: Self),
                    RunnerDamage(DamageKind.Meat, 1),
                ],
                oncePerTurn: true,
                condition: BehaviorTriggerCondition.RunOnAnotherServer,
                requirement: BehaviorCondition.SourceAdvancementAtLeast(1)),
        ]);

    // 36058 — VP-CR-9.8-subroutines; VP-CR-9.5-paid-abilities. The trash ability ends a run on this server.
    private static readonly IceBehaviorSpec EventHorizonIce = new(
        VantagePointCardCodes.EventHorizon,
        strength: 0,
        IceType.Sentry,
        rezCost: 4,
        [new BehaviorSubroutineSpec("event-horizon-program"), new BehaviorSubroutineSpec("event-horizon-end")],
        []);

    private static readonly CardBehaviorDefinition EventHorizon = new(
        VantagePointCardCodes.EventHorizon,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("event-horizon-program",
            [
                UnlessRunnerPays("event-horizon-program-pay", 3,
                [
                    ChooseCards("event-horizon-program-card", Installed(new BehaviorCardFilter(BehaviorCardType.Program, ownerSide: PlayerSide.Runner)),
                        BehaviorChoiceCardinality.AsManyAsAble(1), BehaviorChooser.Corp),
                    new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("event-horizon-program-card")),
                ]),
            ]),
            Subroutine("event-horizon-end", [UnlessRunnerPays("event-horizon-end-pay", 3, [EndTheRun])]),
        ],
        paidAbilities:
        [
            PaidAbility("event-horizon-trash", BehaviorAbilityTiming.AnyPaidWindow, [TrashSelfCost()], [EndTheRun],
                conditions: [new BehaviorCondition(BehaviorConditionKind.RunAgainstSourceServer)]),
        ]);

    // 36059 — VP-CR-9.8-subroutines.
    private static readonly IceBehaviorSpec FlywheelIce = new(
        VantagePointCardCodes.Flywheel,
        strength: 3,
        IceType.Sentry,
        rezCost: 2,
        [new BehaviorSubroutineSpec("flywheel-1"), new BehaviorSubroutineSpec("flywheel-2")],
        []);

    private static readonly CardBehaviorDefinition Flywheel = new(
        VantagePointCardCodes.Flywheel,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("flywheel-1", [Gain(1), MayDraw("flywheel-1-draw")]),
            Subroutine("flywheel-2", [Gain(1), MayDraw("flywheel-2-draw")]),
        ]);

    // 36062 — VP-CR-10.6-bad-publicity; VP-CR-8.5-install. VP-INSTALL-AND-REZ-LESS.
    private static readonly CardBehaviorDefinition ReanimationProtocol = new(
        VantagePointCardCodes.ReanimationProtocol,
        PlayerSide.Corp,
        costs: [Credits(2)],
        instructions:
        [
            new BehaviorInstruction(BehaviorInstructionKind.InstallCard, amount: 10, argument: "reanimation-ice",
                target: BehaviorTarget.CardInZone(BehaviorZone.Archives, CorpIce), sourceZone: BehaviorZone.Archives,
                costRule: BehaviorInstallCostRule.Normal),
            // With no ICE in Archives nothing is installed, and so nothing is rezzed.
            Branch(BehaviorCondition.Chose("reanimation-ice"),
            [
                new BehaviorInstruction(BehaviorInstructionKind.RezPayingLess, target: ChosenBy("reanimation-ice"),
                    filter: new BehaviorCardFilter(BehaviorCardType.Ice, excludedSubtype: "liability"),
                    amountOperand: new BehaviorAmountOperand(null, 1, BehaviorAmountKind.DiscountLeftAfterIceInstallTax, offset: 10),
                    recordAs: "reanimation-rezzed"),
                new BehaviorInstruction(BehaviorInstructionKind.TakeBadPublicity,
                    amountOperand: new BehaviorAmountOperand(null, 1, BehaviorAmountKind.Recorded, recordedKey: "reanimation-rezzed")),
            ]),
        ]);

    // 36064 — VP-CR-6.7-success; VP-CR-7.4-access-limits; VP-CR-9.12.5-persistent. HQ or R&D only. VP-NO-SUCCESSFUL-RUN;
    // VP-ACCESS-LIMIT is declared, and can't apply while runs here can't succeed.
    private static readonly CardBehaviorDefinition Flagship = new(
        VantagePointCardCodes.Flagship,
        PlayerSide.Corp,
        installServerKinds: [ServerKind.Headquarters, ServerKind.RAndD],
        runsCannotSucceed: true,
        runAccessLimitBesidesSelf: 1);

    // 36065 — VP-CR-1.10.4-credit-sources; VP-CR-10.4-damage. VP-CREDITS-OUTSIDE-POOL, with the bad publicity fund.
    private static readonly CardBehaviorDefinition ShackletonGrid = new(
        VantagePointCardCodes.ShackletonGrid,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("shackleton-grid-meat", OccurrenceKind.CreditsSpentFromOutsidePool, BehaviorTriggerScope.Any,
                [RunnerDamage(DamageKind.Meat, 4)],
                TriggerMode.Optional,
                oncePerTurn: true,
                onAttackedServer: true),
        ]);

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
    [
        Ansel20, Reverb, Sleipnir, Vertigo, PerfectRecall, EzaM, Lionsmane, Vicsek, Unleash, TheRedRoom, Lethe, Paywall,
        SacrificeZoneExpansion, EventHorizon, Flywheel, ReanimationProtocol, Flagship, ShackletonGrid,
    ];

    public static IReadOnlyList<AgendaBehaviorSpec> AgendaSpecs { get; } = [SacrificeZoneExpansionAgenda];

    public static IReadOnlyList<IceBehaviorSpec> IceSpecs { get; } =
    [
        Ansel20Ice, ReverbIce, SleipnirIce, VertigoIce, EzaMIce, LionsmaneIce, VicsekIce, LetheIce, PaywallIce, EventHorizonIce,
        FlywheelIce,
    ];
}
