using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// EL.5 Batch 2: zones and hidden information. CR-26.03 citations are in
/// <c>data/elevation/batch-registry.json</c>.
/// </summary>
public static class ElevationBatch2Specs
{
    private static readonly BehaviorCardFilter CorpCard = new(ownerSide: PlayerSide.Corp);

    // 35081 — EL-PLAY-FROM-ARCHIVES; EL-ACTION-NOT-FINISHED.
    private static readonly CardBehaviorDefinition PettyCash = new(
        ElevationCardCodes.PettyCash,
        PlayerSide.Corp,
        costs: [Credits(3)],
        conditions: [new BehaviorCondition(BehaviorConditionKind.NoActionFinishedThisTurn)],
        instructions:
        [
            Gain(5),
            Branch(BehaviorCondition.PlayedFrom(BehaviorZone.Headquarters),
                [new BehaviorInstruction(BehaviorInstructionKind.NoOp)], [GainClicks(1)]),
        ],
        paidAbilities:
        [
            ClickAbility("petty-cash-from-archives", [Click()], [Play(Self, removeFromGameAfter: true)],
                conditions: [new BehaviorCondition(BehaviorConditionKind.NoActionFinishedThisTurn)],
                usableFromArchives: true),
        ]);

    // 35039 — EL-NESTED-PLAY; EL-BEHAVIOR-INSTALL.
    private static readonly CardBehaviorDefinition HumanoidResources = new(
        ElevationCardCodes.HumanoidResources,
        PlayerSide.Corp,
        paidAbilities:
        [
            ClickAbility(
                "humanoid-resources-reorganize",
                [Click(3), TrashSelfCost()],
                [
                    Gain(4),
                    Draw(3),
                    InstallFrom(BehaviorZone.Headquarters, PlayerSide.Corp),
                    InstallFrom(BehaviorZone.Headquarters, PlayerSide.Corp),
                    ChooseCards(
                        "humanoid-resources-operation",
                        BehaviorTarget.CardInZone(BehaviorZone.Headquarters,
                            new BehaviorCardFilter(BehaviorCardType.Operation, ownerSide: PlayerSide.Corp)),
                        BehaviorChoiceCardinality.UpTo(1)),
                    Play(ChosenBy("humanoid-resources-operation")),
                ]),
        ]);

    // 35038 — EL-DIVIDENDS; EL-INSTALL-FROM-ARCHIVES.
    private static readonly CardBehaviorDefinition ProjectIngatan = new(
        ElevationCardCodes.ProjectIngatan,
        PlayerSide.Corp,
        triggers:
        [
            Dividends("project-ingatan-dividends", 1),
            Trigger(
                "project-ingatan-discard-phase",
                OccurrenceKind.DiscardPhaseEnded,
                BehaviorTriggerScope.Controller,
                [
                    RemoveCounter(AgendaCounter, 1),
                    InstallFrom(BehaviorZone.Archives, PlayerSide.Corp, costRule: BehaviorInstallCostRule.IgnoreAllCosts),
                ],
                TriggerMode.Optional,
                requirement: CounterAtLeast(AgendaCounter, 1)),
        ]);

    // 35004 — EL-INSTALL-FROM-HEAP. Double: the extra click is a play cost.
    private static readonly CardBehaviorDefinition Scrounge = new(
        ElevationCardCodes.Scrounge,
        PlayerSide.Runner,
        costs: [Credits(1), Click()],
        instructions:
        [
            InstallFrom(BehaviorZone.Heap, PlayerSide.Runner, BehaviorCardType.Program, optional: false),
            ChooseCards(
                "scrounge-bottom",
                BehaviorTarget.CardInZone(BehaviorZone.Heap,
                    new BehaviorCardFilter(BehaviorCardType.Program, ownerSide: PlayerSide.Runner)),
                BehaviorChoiceCardinality.UpTo(1)),
            new BehaviorInstruction(
                BehaviorInstructionKind.MoveCard,
                target: ChosenBy("scrounge-bottom"),
                sourceZone: BehaviorZone.Heap,
                destinationZone: BehaviorZone.Stack),
        ]);

    // 35036 — EL-LOOK-TOP; EL-BEHAVIOR-INSTALL.
    private static readonly CardBehaviorDefinition PoetriLuxuryBrands = new(
        ElevationCardCodes.PoetriLuxuryBrands,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "poetri-score",
                OccurrenceKind.AgendaScoredOrStolen,
                BehaviorTriggerScope.Any,
                [InstallFrom(BehaviorZone.Deck, PlayerSide.Corp, BehaviorCardType.NonAgenda, top: 3)],
                occurrenceController: PlayerSide.Corp),
            Trigger(
                "poetri-stolen",
                OccurrenceKind.AgendaScoredOrStolen,
                BehaviorTriggerScope.Any,
                [InstallFrom(BehaviorZone.Headquarters, PlayerSide.Corp, BehaviorCardType.NonAgenda)],
                occurrenceController: PlayerSide.Runner),
        ]);

    // 35013 — EL-LOOK-TOP. The Runner looks at the top card and may reveal an
    // icebreaker or run event and add it to the grip.
    private static readonly CardBehaviorDefinition MuslihaT = new(
        ElevationCardCodes.MuslihaT,
        PlayerSide.Runner,
        triggers:
        [
            Trigger(
                "muslihat-turn-begin",
                OccurrenceKind.TurnBegin,
                BehaviorTriggerScope.Controller,
                [
                    ChooseOne(
                        "muslihat-look",
                        BehaviorChooser.Controller,
                        Option("icebreaker", TakeTopCard("muslihat-icebreaker",
                            new BehaviorCardFilter(BehaviorCardType.Icebreaker, ownerSide: PlayerSide.Runner))),
                        Option("run-event", TakeTopCard("muslihat-run-event",
                            new BehaviorCardFilter(BehaviorCardType.Event, subtype: "run", ownerSide: PlayerSide.Runner))),
                        Decline()),
                ]),
        ]);

    // 35050 — EL-REVEAL-ON-ACCESS; an access-time ability (CR 7.2.1).
    private static readonly CardBehaviorDefinition Byte = new(
        ElevationCardCodes.Byte,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "byte-reveal-in-rd",
                OccurrenceKind.CardAccessed,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                null,
                [new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: Self)],
                requirement: BehaviorCondition.SourceIn(BehaviorZone.Deck),
                activeWhileAccessed: true),
            new BehaviorTrigger(
                "byte-ambush",
                OccurrenceKind.CardAccessed,
                TriggerMode.Optional,
                BehaviorTriggerScope.Self,
                null,
                [
                    ChooseOne(
                        "byte-pay",
                        BehaviorChooser.Controller,
                        Option("pay", [GiveTags(1), NetDamage(3)], costs: [Credits(4)]),
                        Decline()),
                ],
                activeWhileAccessed: true),
        ]);

    // 35024 — EL-HAND-SIZE-DISCARD; EL-BEHAVIOR-INSTALL.
    private static readonly CardBehaviorDefinition MagdaleneKeinoChemutai = new(
        ElevationCardCodes.MagdaleneKeinoChemutai,
        PlayerSide.Runner,
        triggers:
        [
            Trigger(
                "magdalene-discard",
                OccurrenceKind.HandSizeDiscarded,
                BehaviorTriggerScope.Controller,
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.InstallCard,
                        target: new BehaviorTarget(
                            BehaviorTargetKind.ZoneCards,
                            BehaviorTarget.HandSizeDiscardsSelector,
                            BehaviorZone.Heap,
                            new BehaviorCardFilter(BehaviorCardType.ProgramOrHardware, ownerSide: PlayerSide.Runner)),
                        sourceZone: BehaviorZone.Heap,
                        optional: true,
                        costRule: BehaviorInstallCostRule.Normal),
                ]),
        ]);

    // 35068 — EL-FACEUP-AGENDA. Installing an agenda faceup is modelled as the
    // Corp choosing, as it is installed, to turn it faceup (it stays inactive).
    private static readonly CardBehaviorDefinition Bangun = new(
        ElevationCardCodes.BANGUN,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "bangun-faceup",
                OccurrenceKind.CardInstalled,
                BehaviorTriggerScope.Controller,
                [new BehaviorInstruction(BehaviorInstructionKind.TurnFaceUp, target: ChosenBy("trigger-subject"))],
                TriggerMode.Optional,
                subject: new BehaviorCardFilter(BehaviorCardType.Agenda, ownerSide: PlayerSide.Corp)),
            new BehaviorTrigger(
                "bangun-access",
                OccurrenceKind.CardAccessed,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Any,
                new BehaviorCardFilter(BehaviorCardType.Agenda, ownerSide: PlayerSide.Corp),
                [
                    new BehaviorInstruction(BehaviorInstructionKind.DealDamage, amount: 2, damageKind: DamageKind.Meat),
                    GiveTags(1),
                ],
                condition: BehaviorTriggerCondition.FaceUpInstalledSubject),
        ]);

    // 35046 — EL-LOOK-TOP; damage done and HQ trashes as occurrences.
    private static readonly CardBehaviorDefinition AUCo = new(
        ElevationCardCodes.AUCo,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("au-co-damage", OccurrenceKind.DamageDone, BehaviorTriggerScope.Controller, [AddCounter(PowerCounter, 1)]),
            Trigger("au-co-hq-trash", OccurrenceKind.CardsTrashedFromHq, BehaviorTriggerScope.Controller, [AddCounter(PowerCounter, 1)]),
            Trigger(
                "au-co-turn-begin",
                OccurrenceKind.TurnBegin,
                BehaviorTriggerScope.Controller,
                [
                    RemoveCounter(PowerCounter, 2),
                    ChooseCards("au-co-trash", BehaviorTarget.TopOfZone(BehaviorZone.Deck, 3, CorpCard), BehaviorChoiceCardinality.Exactly(1)),
                    new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("au-co-trash")),
                    new BehaviorInstruction(BehaviorInstructionKind.AddToHand, target: BehaviorTarget.TopOfZone(BehaviorZone.Deck, 2, CorpCard),
                        sourceZone: BehaviorZone.Deck, destinationZone: BehaviorZone.Headquarters),
                ],
                TriggerMode.Optional,
                requirement: CounterAtLeast(PowerCounter, 2)),
        ]);

    // 35055 — EL-REVEAL-SUBSET; EL-BEHAVIOR-INSTALL.
    private static readonly CardBehaviorDefinition PeerReview = new(
        ElevationCardCodes.PeerReview,
        PlayerSide.Corp,
        costs: [Credits(4)],
        instructions:
        [
            ChooseCards("peer-review-hidden", BehaviorTarget.CardInZone(BehaviorZone.Headquarters, CorpCard), BehaviorChoiceCardinality.Exactly(1)),
            new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: new BehaviorTarget(BehaviorTargetKind.ZoneCards, BehaviorZone.Headquarters, CorpCard)),
            new BehaviorInstruction(BehaviorInstructionKind.ReturnSetAside),
            Gain(7),
            InstallFrom(BehaviorZone.Headquarters, PlayerSide.Corp, destination: BehaviorZone.ServerRoot),
        ]);

    // 35067 — EL-REVEAL-GRIP. Double: the extra click is a play cost.
    private static readonly CardBehaviorDefinition TouchUps = new(
        ElevationCardCodes.TouchUps,
        PlayerSide.Corp,
        costs: [Credits(2), Click()],
        instructions:
        [
            .. AdvanceInstalled("touch-ups-advance", 2),
            Branch(BehaviorCondition.Chose("touch-ups-advance"),
            [
                ChooseOne(
                    "touch-ups-type",
                    BehaviorChooser.Controller,
                    TouchUpsType("program", BehaviorCardType.Program),
                    TouchUpsType("hardware", BehaviorCardType.Hardware),
                    TouchUpsType("resource", BehaviorCardType.Resource),
                    TouchUpsType("event", BehaviorCardType.Event)),
            ]),
        ]);

    private static BehaviorChoiceOption TouchUpsType(string key, BehaviorCardType type) => Option(key,
    [
        new BehaviorInstruction(BehaviorInstructionKind.Reveal,
            target: new BehaviorTarget(BehaviorTargetKind.ZoneCards, BehaviorZone.Grip, new BehaviorCardFilter(ownerSide: PlayerSide.Runner))),
        ChooseCards($"touch-ups-{key}",
            BehaviorTarget.CardInZone(BehaviorZone.Grip, new BehaviorCardFilter(type, ownerSide: PlayerSide.Runner)),
            BehaviorChoiceCardinality.UpTo(2)),
        new BehaviorInstruction(BehaviorInstructionKind.ShuffleIntoDeck, target: ChosenBy($"touch-ups-{key}"),
            sourceZone: BehaviorZone.Grip, destinationZone: BehaviorZone.Stack),
    ]);

    // 35066 — EL-INSTALL-FROM-SCORE-AREA; EL-REMOVE-TAGS (X as an additional cost);
    // EL-OPERATION-TRASH-COST. X is chosen as an option per printed agenda value.
    private static readonly CardBehaviorDefinition IPEnforcement = new(
        ElevationCardCodes.IPEnforcement,
        PlayerSide.Corp,
        conditions: [BehaviorCondition.Tagged(PlayerSide.Runner)],
        instructions:
        [
            ChooseOne(
                "ip-enforcement-x",
                BehaviorChooser.Controller,
                IPEnforcementX(1),
                IPEnforcementX(2),
                IPEnforcementX(3)),
        ]);

    private static BehaviorChoiceOption IPEnforcementX(int x) => Option($"x-{x}",
    [
        RemoveTags(x),
        new BehaviorInstruction(
            BehaviorInstructionKind.InstallCard,
            argument: $"ip-enforcement-agenda-{x}",
            target: BehaviorTarget.CardInZone(BehaviorZone.ScoreArea,
                new BehaviorCardFilter(BehaviorCardType.Agenda, ownerSide: PlayerSide.Corp, agendaPoints: x)),
            sourceZone: BehaviorZone.ScoreArea,
            optional: false,
            costRule: BehaviorInstallCostRule.Normal),
        Branch(BehaviorCondition.Tagged(PlayerSide.Runner),
            [new BehaviorInstruction(BehaviorInstructionKind.AdvanceCard, amount: 1, target: ChosenBy($"ip-enforcement-agenda-{x}"))]),
    ], BehaviorCondition.RunnerTagsAtLeast(x));

    // 35073 — EL-PLAY-FROM-ARCHIVES; EL-FORFEIT; EL-REVEAL-TRASH-COST. The
    // additional rez cost is paid by a "when rezzed" ability, and the rez is
    // offered only when the Corp can pay one of its alternatives.
    private static readonly CardBehaviorDefinition Plutus = new(
        ElevationCardCodes.Plutus,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "plutus-rez-cost",
                OccurrenceKind.CardRezzed,
                BehaviorTriggerScope.Self,
                [
                    ChooseOne(
                        "plutus-cost",
                        BehaviorChooser.Controller,
                        Option("forfeit",
                        [
                            ChooseCards("plutus-forfeit",
                                BehaviorTarget.CardInZone(BehaviorZone.ScoreArea, new BehaviorCardFilter(BehaviorCardType.Agenda, ownerSide: PlayerSide.Corp)),
                                BehaviorChoiceCardinality.Exactly(1)),
                            new BehaviorInstruction(BehaviorInstructionKind.ForfeitAgenda, target: ChosenBy("plutus-forfeit")),
                        ], BehaviorCondition.ZoneCountAtLeast(BehaviorZone.ScoreArea, 1)),
                        Option("reveal-and-trash",
                        [
                            ChooseCards("plutus-hq", BehaviorTarget.CardInZone(BehaviorZone.Headquarters, CorpCard), BehaviorChoiceCardinality.Exactly(3)),
                            new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: ChosenBy("plutus-hq")),
                            new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("plutus-hq")),
                        ], BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Headquarters, 3))),
                ]),
            Trigger(
                "plutus-turn-begin",
                OccurrenceKind.TurnBegin,
                BehaviorTriggerScope.Controller,
                [
                    ChooseCards("plutus-transaction",
                        BehaviorTarget.CardInZone(BehaviorZone.Archives,
                            new BehaviorCardFilter(BehaviorCardType.Operation, subtype: "transaction", ownerSide: PlayerSide.Corp)),
                        BehaviorChoiceCardinality.UpTo(1)),
                    Play(ChosenBy("plutus-transaction"), removeFromGameAfter: true),
                ],
                TriggerMode.Optional),
        ],
        rezRequirements:
        [
            BehaviorCondition.ZoneCountAtLeast(BehaviorZone.ScoreArea, 1),
            BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Headquarters, 3),
        ]);

    /// <summary>Choose the top card of the stack if it matches, reveal it and add it to the grip.</summary>
    private static IReadOnlyList<BehaviorInstruction> TakeTopCard(string key, BehaviorCardFilter filter) =>
    [
        ChooseCards(key, BehaviorTarget.TopOfZone(BehaviorZone.Stack, 1, filter), BehaviorChoiceCardinality.UpTo(1)),
        new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: ChosenBy(key)),
        new BehaviorInstruction(BehaviorInstructionKind.AddToHand, target: ChosenBy(key),
            sourceZone: BehaviorZone.Stack, destinationZone: BehaviorZone.Grip),
    ];

    private static readonly BehaviorStaticModifier PlusOneMemory = new(BehaviorStaticModifierKind.MemoryUnits, delta: 1);

    // 35006 — EL-HOSTED-FACEUP (hosted cards play as if in the grip); EL-FREE-INSTALL.
    private static readonly CardBehaviorDefinition Bling = new(
        ElevationCardCodes.Bling,
        PlayerSide.Runner,
        costs: [Credits(2)],
        staticModifiers: [PlusOneMemory],
        triggers:
        [
            new BehaviorTrigger(
                "bling-host",
                OccurrenceKind.CardInstalled,
                TriggerMode.Optional,
                BehaviorTriggerScope.Controller,
                null,
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.HostFaceUp,
                        target: BehaviorTarget.TopOfZone(BehaviorZone.Stack, 1, new BehaviorCardFilter(ownerSide: PlayerSide.Runner))),
                ],
                occurrenceControllerSide: PlayerSide.Runner,
                condition: BehaviorTriggerCondition.SubjectInstalledWithoutCredits),
            Trigger(
                "bling-discard",
                OccurrenceKind.DiscardPhaseEnded,
                BehaviorTriggerScope.Controller,
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.TrashCard,
                        target: BehaviorTarget.HostedOnSource(PlayerSide.Runner)),
                ],
                occurrenceController: PlayerSide.Runner,
                requirement: BehaviorCondition.ZoneCountAtLeast(BehaviorZone.HostedFaceUp, 1)),
        ],
        hostedCardsPlayAsGrip: true);

    // 35028 — EL-HOSTED-FACEUP; EL-BEHAVIOR-INSTALL from the hosted cards.
    private static readonly CardBehaviorDefinition Madani = new(
        ElevationCardCodes.Madani,
        PlayerSide.Runner,
        costs: [Credits(2)],
        staticModifiers: [PlusOneMemory],
        paidAbilities:
        [
            ClickAbility(
                "madani-host",
                [Click()],
                [
                    ChooseCards(
                        "madani-programs",
                        BehaviorTarget.CardsInZone(BehaviorZone.Grip, new BehaviorCardFilter(BehaviorCardType.Program, ownerSide: PlayerSide.Runner)),
                        BehaviorChoiceCardinality.AnyNumber),
                    new BehaviorInstruction(BehaviorInstructionKind.HostFaceUp, target: ChosenBy("madani-programs")),
                ]),
            PaidAbility(
                "madani-install",
                BehaviorAbilityTiming.AnyPaidWindow,
                [],
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.InstallCard,
                        target: BehaviorTarget.HostedOnSource(PlayerSide.Runner, BehaviorCardType.Program),
                        sourceZone: BehaviorZone.HostedFaceUp,
                        optional: false),
                ],
                BehaviorAbilityUsageLimit.OncePerTurn,
                conditions:
                [
                    // One console at a time (Limit 1 console), so the zone holds only this host's cards.
                    BehaviorCondition.ZoneCountAtLeast(BehaviorZone.HostedFaceUp, 1),
                ]),
        ]);

    public static IReadOnlyList<AssetBehaviorSpec> AssetSpecs { get; } =
    [
        new(ElevationCardCodes.HumanoidResources, trashCost: 1),
        new(ElevationCardCodes.Byte, trashCost: 0),
        new(ElevationCardCodes.Plutus, trashCost: 3),
    ];

    public static IReadOnlyList<AgendaBehaviorSpec> AgendaSpecs { get; } =
    [
        new(ElevationCardCodes.ProjectIngatan, 2),
    ];

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
    [
        PettyCash,
        HumanoidResources,
        ProjectIngatan,
        Scrounge,
        PoetriLuxuryBrands,
        MuslihaT,
        Byte,
        MagdaleneKeinoChemutai,
        Bangun,
        AUCo,
        PeerReview,
        TouchUps,
        IPEnforcement,
        Plutus,
        Bling,
        Madani,
    ];
}
