using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// VP.7 cards: zones and hidden information. CR-26.03 citations are in <c>data/vantage-point/batch-registry.json</c>.
/// </summary>
public static class VantagePointBatch3Specs
{
    private static readonly BehaviorCardFilter CorpCard = new(ownerSide: PlayerSide.Corp);

    /// <summary>
    /// VP-ARRANGE: arranging the top <paramref name="count"/> cards of R&amp;D in any order, as that many rounds of putting
    /// one of them on top. The card chosen last ends on top, so every order can be made.
    /// </summary>
    private static IReadOnlyList<BehaviorInstruction> ArrangeTop(string key, int count) =>
    [
        .. Enumerable.Range(1, count).SelectMany(round => new[]
        {
            ChooseCards($"{key}-{round}", BehaviorTarget.TopOfZone(BehaviorZone.Deck, count, CorpCard), BehaviorChoiceCardinality.AsManyAsAble(1),
                BehaviorChooser.Corp),
            new BehaviorInstruction(BehaviorInstructionKind.PlaceOnTopOfDeck, target: ChosenBy($"{key}-{round}")),
        }),
    ];

    // 36006 — VP-CR-1.13-hosting. VP-HOST-RESOURCES: unique companion and connection resources, 1 less; +2 maximum hand
    // size while it hosts one of each.
    private static readonly CardBehaviorDefinition Hackerspace = new(
        VantagePointCardCodes.Hackerspace,
        PlayerSide.Runner,
        hostsResources: new ResourceHostingSpec(["companion", "connection"], uniqueOnly: true, installDiscount: 1, handSizeWhileHostingEach: 2));

    // 36007 — VP-CR-7.3.2-archives-faceup. VP-ARCHIVES-TURNED-FACEUP.
    private static readonly CardBehaviorDefinition NurseHanh = new(
        VantagePointCardCodes.NurseHanh,
        PlayerSide.Runner,
        triggers: [Trigger("nurse-hanh-archives", OccurrenceKind.ArchivesCardsTurnedFaceUp, BehaviorTriggerScope.Any, [Draw(2)])]);

    // 36017 — VP-CR-9.6-conditional. VP-CARD-TRASHED and a look at R&D's top: the Runner sees the card it is offered.
    private static IReadOnlyList<BehaviorInstruction> HiramLook(string key) =>
    [
        Branch(BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Deck, 1),
            [ChooseCards(key, BehaviorTarget.TopOfZone(BehaviorZone.Deck, 1, CorpCard), BehaviorChoiceCardinality.Exactly(1))]),
    ];

    private static readonly BehaviorCardFilter RunnerHardware = new(BehaviorCardType.Hardware, ownerSide: PlayerSide.Runner);

    private static readonly CardBehaviorDefinition Hiram = new(
        VantagePointCardCodes.Hiram0missionSvensson,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("hiram-install", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Controller, HiramLook("hiram-install-look"),
                subject: RunnerHardware),
            Trigger("hiram-trash", OccurrenceKind.CardTrashed, BehaviorTriggerScope.Any, HiramLook("hiram-trash-look"),
                subject: RunnerHardware, occurrenceController: PlayerSide.Runner),
        ]);

    // 36022 — VP-CR-1.13.7-facedown-hosting. VP-HOST-FACEDOWN: up to 4 cards; hosting one draws one. Its trash shuffles
    // what it hosts into the stack (the shuffle is listed first, so the hosted cards are still there).
    private static IReadOnlyList<BehaviorInstruction> ReadWriteShareHost(string key) =>
    [
        Branch(BehaviorCondition.SourceHostsAtLeast(4), [new BehaviorInstruction(BehaviorInstructionKind.NoOp)],
        [
            ChooseCards(key, BehaviorTarget.CardsInZone(BehaviorZone.Grip, new BehaviorCardFilter(ownerSide: PlayerSide.Runner)),
                BehaviorChoiceCardinality.UpTo(1)),
            Branch(BehaviorCondition.Chose(key), [new BehaviorInstruction(BehaviorInstructionKind.HostFaceDown, target: ChosenBy(key)), Draw(1)]),
        ]),
    ];

    private static readonly CardBehaviorDefinition ReadWriteShare = new(
        VantagePointCardCodes.ReadWriteShare,
        PlayerSide.Runner,
        triggers:
        [
            Trigger("read-write-share-install", OccurrenceKind.CardInstalled, BehaviorTriggerScope.Self, ReadWriteShareHost("read-write-share-install-card"),
                TriggerMode.Optional),
            Trigger("read-write-share-turn", OccurrenceKind.TurnBegin, BehaviorTriggerScope.Controller, ReadWriteShareHost("read-write-share-turn-card"),
                TriggerMode.Optional),
        ],
        paidAbilities:
        [
            PaidAbility(
                "read-write-share-trash",
                BehaviorAbilityTiming.AnyPaidWindow,
                [],
                [
                    new BehaviorInstruction(BehaviorInstructionKind.ShuffleIntoDeck, target: BehaviorTarget.HostedOnSource(PlayerSide.Runner),
                        sourceZone: BehaviorZone.HostedFaceUp, destinationZone: BehaviorZone.Stack),
                    TrashSelf(),
                ]),
        ],
        hostsFaceDownLimit: 4);

    // 36036 — VP-CR-1.5.2-melies-u; VP-CR-9.6-conditional. VP-SECRET-IDENTITY: the front sets a copy secretly when the
    // Corp's discard phase ends, flips on a successful central run and gains a credit when the Runner's action phase
    // ends; the back named by the run's server looks at R&D's top, and flips back when the Runner's discard phase ends.
    private static BehaviorTrigger MeliesFlip(string key, ServerKind server) =>
        Trigger(key, OccurrenceKind.RunSuccessful, BehaviorTriggerScope.Any,
        [
            new BehaviorInstruction(BehaviorInstructionKind.FlipIdentity),
            Branch(new BehaviorCondition(BehaviorConditionKind.SecretIdentityMatchesAttackedServer),
            [
                Branch(BehaviorCondition.ZoneCountAtLeast(BehaviorZone.Deck, 1),
                [
                    ChooseCards($"{key}-trash", BehaviorTarget.TopOfZone(BehaviorZone.Deck, 1, CorpCard), BehaviorChoiceCardinality.UpTo(1),
                        BehaviorChooser.Corp),
                    Branch(BehaviorCondition.Chose($"{key}-trash"),
                    [
                        new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy($"{key}-trash")),
                        ChooseCards($"{key}-archives", BehaviorTarget.CardsInZone(BehaviorZone.Archives, CorpCard), BehaviorChoiceCardinality.AsManyAsAble(1),
                            BehaviorChooser.Corp),
                        new BehaviorInstruction(BehaviorInstructionKind.AddToHand, target: ChosenBy($"{key}-archives"), sourceZone: BehaviorZone.Archives,
                            destinationZone: BehaviorZone.Headquarters),
                    ]),
                ]),
            ]),
        ],
            server: server,
            occurrenceController: PlayerSide.Runner,
            requirement: new BehaviorCondition(BehaviorConditionKind.IdentityNotFlipped));

    private static readonly CardBehaviorDefinition MeliesU = new(
        VantagePointCardCodes.MeliesU,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("melies-u-set", OccurrenceKind.DiscardPhaseEnded, BehaviorTriggerScope.Controller,
            [
                ChooseOne(
                    "melies-u-copy",
                    BehaviorChooser.Corp,
                    Option("hq", [new BehaviorInstruction(BehaviorInstructionKind.SetSecretIdentity, argument: nameof(ServerKind.Headquarters))]),
                    Option("rd", [new BehaviorInstruction(BehaviorInstructionKind.SetSecretIdentity, argument: nameof(ServerKind.RAndD))]),
                    Option("archives", [new BehaviorInstruction(BehaviorInstructionKind.SetSecretIdentity, argument: nameof(ServerKind.Archives))])),
            ],
                occurrenceController: PlayerSide.Corp,
                requirement: new BehaviorCondition(BehaviorConditionKind.IdentityNotFlipped)),
            MeliesFlip("melies-u-flip-hq", ServerKind.Headquarters),
            MeliesFlip("melies-u-flip-rd", ServerKind.RAndD),
            MeliesFlip("melies-u-flip-archives", ServerKind.Archives),
            Trigger("melies-u-credit", OccurrenceKind.ActionPhaseEnded, BehaviorTriggerScope.Any, [Gain(1)],
                occurrenceController: PlayerSide.Runner,
                requirement: new BehaviorCondition(BehaviorConditionKind.IdentityNotFlipped)),
            Trigger("melies-u-flip-back", OccurrenceKind.DiscardPhaseEnded, BehaviorTriggerScope.Any,
                [new BehaviorInstruction(BehaviorInstructionKind.FlipIdentity)],
                occurrenceController: PlayerSide.Runner,
                requirement: new BehaviorCondition(BehaviorConditionKind.IdentityFlipped)),
        ]);

    // 36037 — VP-CR-1.9-counters; VP-CR-1.13-hosting. VP-MOVE-UPGRADE: a hosted agenda counter moves a rezzed upgrade to the
    // root of another server, named by one of its installed cards.
    private static readonly AgendaBehaviorSpec LotusHazeAgenda = new(VantagePointCardCodes.LotusHaze, agendaPoints: 2);

    private static BehaviorInstruction LotusHazeMove(BehaviorZone central) =>
        new(BehaviorInstructionKind.MoveSourceToServerOf, argument: "lotus-haze-upgrade", destinationZone: central);

    private static readonly CardBehaviorDefinition LotusHaze = new(
        VantagePointCardCodes.LotusHaze,
        PlayerSide.Corp,
        triggers: [Score("lotus-haze-score", [AddCounter(AgendaCounter, 3)])],
        paidAbilities:
        [
            PaidAbility(
                "lotus-haze-move",
                BehaviorAbilityTiming.AnyPaidWindow,
                [CounterCost(AgendaCounter)],
                [
                    ChooseCards("lotus-haze-upgrade", Installed(new BehaviorCardFilter(BehaviorCardType.Upgrade, ownerSide: PlayerSide.Corp, rezzed: true)),
                        BehaviorChoiceCardinality.Exactly(1)),
                    // The review of #157: a central server is a destination even with nothing installed in it; a remote
                    // exists only while it holds a card, so choosing one of its cards chooses it.
                    ChooseOne(
                        "lotus-haze-destination",
                        BehaviorChooser.Corp,
                        Option("hq", [LotusHazeMove(BehaviorZone.Headquarters)]),
                        Option("rd", [LotusHazeMove(BehaviorZone.Deck)]),
                        Option("archives", [LotusHazeMove(BehaviorZone.Archives)]),
                        Option("remote",
                        [
                            ChooseCards("lotus-haze-server", new BehaviorTarget(BehaviorTargetKind.InstalledCard, BehaviorTarget.RemoteServerSelector),
                                BehaviorChoiceCardinality.AsManyAsAble(1)),
                            new BehaviorInstruction(BehaviorInstructionKind.MoveSourceToServerOf, argument: "lotus-haze-upgrade",
                                target: ChosenBy("lotus-haze-server")),
                        ])),
                ]),
        ]);

    // 36040 — VP-CR-1.9-counters; VP-CR-8.3-arrange; VP-CR-6.5.7-pass-and-break. VP-ICE-VIRUS-COUNTERS, VP-ENCOUNTER-ENDED
    // and VP-ARRANGE.
    private static readonly IceBehaviorSpec KnowledgeSeekerIce = new(
        VantagePointCardCodes.KnowledgeSeeker,
        strength: 5,
        IceType.CodeGate,
        rezCost: 5,
        [new BehaviorSubroutineSpec("knowledge-seeker-virus"), new BehaviorSubroutineSpec("knowledge-seeker-arrange"), new EndRunSubroutineSpec()],
        []);

    private static readonly CardBehaviorDefinition KnowledgeSeeker = new(
        VantagePointCardCodes.KnowledgeSeeker,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("knowledge-seeker-virus", [new BehaviorInstruction(BehaviorInstructionKind.AddCounter, amount: 1, argument: "virus", target: Self)]),
            Subroutine("knowledge-seeker-arrange", ArrangeTop("knowledge-seeker-top", 4)),
            Trigger("knowledge-seeker-ended", OccurrenceKind.EncounterEnded, BehaviorTriggerScope.Self,
                [new BehaviorInstruction(BehaviorInstructionKind.PurgeVirusCounters), new BehaviorInstruction(BehaviorInstructionKind.Derez, target: Self)],
                requirement: CounterAtLeast("virus", 3)),
        ]);

    // 36043 — VP-CR-8.3-arrange. Of R&D's top 5: trash 1, add 1 to HQ, arrange the rest (the top 3 by then).
    private static readonly CardBehaviorDefinition Cultivate = new(
        VantagePointCardCodes.Cultivate,
        PlayerSide.Corp,
        instructions:
        [
            ChooseCards("cultivate-trash", BehaviorTarget.TopOfZone(BehaviorZone.Deck, 5, CorpCard), BehaviorChoiceCardinality.AsManyAsAble(1)),
            new BehaviorInstruction(BehaviorInstructionKind.TrashCard, target: ChosenBy("cultivate-trash")),
            ChooseCards("cultivate-hq", BehaviorTarget.TopOfZone(BehaviorZone.Deck, 4, CorpCard), BehaviorChoiceCardinality.AsManyAsAble(1)),
            new BehaviorInstruction(BehaviorInstructionKind.AddToHand, target: ChosenBy("cultivate-hq"), sourceZone: BehaviorZone.Deck,
                destinationZone: BehaviorZone.Headquarters),
            .. ArrangeTop("cultivate-arrange", 3),
        ]);

    // 36055 — VP-CR-2.3-cost-modifiers; VP-CR-9.6-conditional. VP-DYNAMIC-REZ-COST; VP-SOURCE-SERVER-ROOT. The counter is
    // placed before the trash, while the server is still the upgrade's.
    private static readonly CardBehaviorDefinition HypeMachine = new(
        VantagePointCardCodes.HypeMachine,
        PlayerSide.Corp,
        costModifiers:
        [
            new BehaviorCostModifier(BehaviorCostModifierKind.SelfRezCreditCost, -6,
                BehaviorCondition.TurnEventCountAtLeast(TurnEventKind.AgendaScoredOrStolen, 1), "hype-machine-agenda"),
        ],
        paidAbilities:
        [
            PaidAbility(
                "hype-machine-advance",
                BehaviorAbilityTiming.AnyPaidWindow,
                [],
                [
                    ChooseCards("hype-machine-target",
                        new BehaviorTarget(BehaviorTargetKind.InstalledCard, BehaviorTarget.SourceServerRootSelector,
                            filter: new BehaviorCardFilter(BehaviorCardType.Advanceable, ownerSide: PlayerSide.Corp)),
                        BehaviorChoiceCardinality.Exactly(1)),
                    new BehaviorInstruction(BehaviorInstructionKind.AdvanceCard, amount: 1, target: ChosenBy("hype-machine-target")),
                    TrashSelf(),
                ]),
        ]);

    // 36060 — VP-CR-9.5-paid-abilities; VP-CR-8.7-search; VP-CR-9.8-subroutines. VP-ABILITY-FROM-HQ: Expendable.
    private static readonly IceBehaviorSpec TocsinIce = new(
        VantagePointCardCodes.Tocsin,
        strength: 5,
        IceType.CodeGate,
        rezCost: 8,
        [new LoseCreditsSubroutineSpec(PlayerSide.Runner, 2), new EndRunSubroutineSpec(), new EndRunSubroutineSpec()],
        []);

    private static IReadOnlyList<BehaviorInstruction> TocsinSearch(string key, string subtype) =>
    [
        .. SearchAndReveal(key, BehaviorZone.Deck, new BehaviorCardFilter(BehaviorCardType.Ice, subtype: subtype, ownerSide: PlayerSide.Corp)),
        Branch(BehaviorCondition.FoundCard(),
            [new BehaviorInstruction(BehaviorInstructionKind.AddToHand, target: Chosen, sourceZone: BehaviorZone.Deck, destinationZone: BehaviorZone.Headquarters)]),
    ];

    private static readonly CardBehaviorDefinition Tocsin = new(
        VantagePointCardCodes.Tocsin,
        PlayerSide.Corp,
        paidAbilities:
        [
            new BehaviorPaidAbility(
                "tocsin-search",
                BehaviorAbilityTiming.ActionPhase,
                BehaviorAbilityActivation.ClickAction,
                [Click(), Credits(1), TrashSelfCost()],
                [],
                [],
                [
                    new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: Self),
                    .. TocsinSearch("tocsin-barrier", "barrier"),
                    .. TocsinSearch("tocsin-sentry", "sentry"),
                ],
                BehaviorAbilityUsageLimit.Unlimited,
                usableFromHeadquarters: true),
        ]);

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
    [
        Hackerspace, NurseHanh, Hiram, ReadWriteShare, MeliesU, LotusHaze, KnowledgeSeeker, Cultivate, HypeMachine, Tocsin,
    ];

    public static IReadOnlyList<AgendaBehaviorSpec> AgendaSpecs { get; } = [LotusHazeAgenda];

    public static IReadOnlyList<IceBehaviorSpec> IceSpecs { get; } = [KnowledgeSeekerIce, TocsinIce];
}
