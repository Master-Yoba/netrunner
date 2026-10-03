using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// EL.3 Corp identities, agendas and assets. CR-26.03 citations are in
/// <c>data/elevation/batch-registry.json</c>.
/// </summary>
public static class ElevationBatch1CorpSpecs
{
    // 35070 — EL-CR-8.2-scoring; EL-CR-1.15-forfeit.
    private static readonly CardBehaviorDefinition Greenmail = new(
        ElevationCardCodes.Greenmail,
        PlayerSide.Corp,
        triggers:
        [
            Score("greenmail-score", [Gain(2)]),
            Trigger("greenmail-forfeit", OccurrenceKind.AgendaForfeited, BehaviorTriggerScope.Self, [Gain(4)]),
        ]);

    // 35037 — EL-CR-9.1-triggered; EL-CR-1.11-clicks. The first time the
    // Runner trashes an installed Corp card during their turn they may spend
    // a click; if they do not, the Corp gets +1 allotted click next turn.
    private static readonly CardBehaviorDefinition AggressiveTrendsetting = new(
        ElevationCardCodes.AggressiveTrendsetting,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "aggressive-trendsetting-trash",
                OccurrenceKind.AccessedCardTrashed,
                BehaviorTriggerScope.Any,
                [
                    ChooseOne(
                        "aggressive-trendsetting-click",
                        BehaviorChooser.Runner,
                        Option("spend-click", [], costs: [Click()]),
                        Option("decline", [new BehaviorInstruction(BehaviorInstructionKind.GainAllottedClicksNextTurn, amount: 1)])),
                ],
                requirement: BehaviorCondition.TurnEventCountIs(TurnEventKind.InstalledCorpCardTrashed, 1, PlayerSide.Runner)),
        ]);

    // 35057 — EL-CR-9.1-triggered; double-sided identity (EL-FLIP).
    private static readonly CardBehaviorDefinition NebulaTalentManagement = new(
        ElevationCardCodes.NebulaTalentManagement,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "nebula-action-phase-end",
                OccurrenceKind.ActionPhaseEnded,
                BehaviorTriggerScope.Controller,
                [Gain(1), FlipSelf()],
                requirement: new BehaviorCondition(BehaviorConditionKind.PlayedThisTurn),
                face: BehaviorFace.Front),
            Trigger(
                "nebula-first-operation",
                OccurrenceKind.FirstOperationPlayedThisTurn,
                BehaviorTriggerScope.Controller,
                [GainClicks(1)],
                face: BehaviorFace.Back),
            Trigger(
                "nebula-hq-run",
                OccurrenceKind.RunSuccessful,
                BehaviorTriggerScope.Any,
                [FlipSelf()],
                server: ServerKind.Headquarters,
                face: BehaviorFace.Back),
            Trigger(
                "nebula-rd-run",
                OccurrenceKind.RunSuccessful,
                BehaviorTriggerScope.Any,
                [FlipSelf()],
                server: ServerKind.RAndD,
                face: BehaviorFace.Back),
        ]);

    // 35051 — EL-CR-9.1-triggered; EL-CR-1.9-counters; EL-AMOUNT.
    private static readonly CardBehaviorDefinition PhatGioanBaotixita = new(
        ElevationCardCodes.PhatGioanBaotixita,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "phat-discard-phase",
                OccurrenceKind.DiscardPhaseEnded,
                BehaviorTriggerScope.Controller,
                [AddCounter(PowerCounter, 1)]),
            Trigger(
                "phat-agenda",
                OccurrenceKind.AgendaScoredOrStolen,
                BehaviorTriggerScope.Any,
                [
                    ChooseOne(
                        "phat-power",
                        BehaviorChooser.Controller,
                        Option("remove-none", [NetDamage(1)]),
                        Option("remove-one", [RemoveCounter(PowerCounter, 1), NetDamage(2)], CounterAtLeast(PowerCounter, 1)),
                        Option("remove-two", [RemoveCounter(PowerCounter, 2), NetDamage(3)], CounterAtLeast(PowerCounter, 2))),
                ],
                requirement: BehaviorCondition.TurnEventCountIs(TurnEventKind.AgendaScoredOrStolen, 1)),
        ]);

    // 35061 — EL-CR-8.2-advancement; EL-AMOUNT.
    private static readonly CardBehaviorDefinition Idiosyncresis = new(
        ElevationCardCodes.Idiosyncresis,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "idiosyncresis-turn-begin",
                OccurrenceKind.TurnBegin,
                BehaviorTriggerScope.Controller,
                [
                    new BehaviorInstruction(
                        BehaviorInstructionKind.GainCredits,
                        amountOperand: BehaviorAmountOperand.Of(BehaviorAmountKind.SourceAdvancementCounters, 3)),
                    RunnerLoses(0, BehaviorAmountOperand.Of(BehaviorAmountKind.SourceAdvancementCounters, 2)),
                    TrashSelf(),
                ],
                TriggerMode.Optional),
        ],
        canBeAdvanced: true);

    // 35047 — EL-CR-9.1-triggered; EL-CR-8.2-advancement.
    private static readonly CardBehaviorDefinition PtUntaian = new(
        ElevationCardCodes.PTUntaian,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "pt-untaian-discard-phase",
                OccurrenceKind.DiscardPhaseEnded,
                BehaviorTriggerScope.Controller,
                [
                    ChooseOne(
                        "pt-untaian-pay",
                        BehaviorChooser.Controller,
                        Option(
                            "pay",
                            AdvanceInstalled("pt-untaian-target", 1, rezzed: false, cannotScoreThisTurn: true),
                            costs: [Credits(1)]),
                        Decline()),
                ],
                TriggerMode.Optional,
                requirement: new BehaviorCondition(
                    BehaviorConditionKind.ZoneCountAtMost,
                    fact: nameof(BehaviorZone.Headquarters),
                    value: 3)),
        ]);

    // 35049 — EL-DIVIDENDS; EL-CR-8.2-advancement.
    private static readonly CardBehaviorDefinition SericultureExpansion = new(
        ElevationCardCodes.SericultureExpansion,
        PlayerSide.Corp,
        triggers:
        [
            Dividends("sericulture-dividends", 1),
            Trigger(
                "sericulture-discard-phase",
                OccurrenceKind.DiscardPhaseEnded,
                BehaviorTriggerScope.Controller,
                [
                    RemoveCounter(AgendaCounter, 1),
                    .. AdvanceInstalled("sericulture-target", 2, cardType: BehaviorCardType.Any, cannotScoreThisTurn: true),
                ],
                TriggerMode.Optional,
                requirement: CounterAtLeast(AgendaCounter, 1)),
        ]);

    // 35058 — EL-TAG-REMOVED; EL-REMOVE-TAGS; EL-BEHAVIOR-INSTALL.
    private static readonly CardBehaviorDefinition SynapseGlobal = new(
        ElevationCardCodes.SynapseGlobal,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "synapse-first-tag-removed",
                OccurrenceKind.FirstTagRemovalThisTurn,
                BehaviorTriggerScope.Any,
                // "Reveal and install" (EL.5 review): the installed card is revealed to the Runner.
                RevealedInstall("synapse-install", BehaviorZone.Headquarters),
                TriggerMode.Optional),
        ],
        paidAbilities:
        [
            ClickAbility(
                "synapse-remove-tag",
                [Click()],
                [RemoveTags(1), Gain(2)],
                conditions: [BehaviorCondition.RunnerTagsAtLeast(1)]),
        ]);

    // 35069 — EL-CR-9.1-triggered (first credit gain through an agenda or operation each turn).
    private static readonly CardBehaviorDefinition TheZwickyGroup = new(
        ElevationCardCodes.TheZwickyGroup,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "zwicky-first-card-credits",
                OccurrenceKind.FirstCardCreditGainThisTurn,
                BehaviorTriggerScope.Controller,
                [Draw(1)],
                TriggerMode.Optional),
        ]);

    // 35062 — EL-THREAT; a trashed card's own ability uses last-known information.
    private static readonly CardBehaviorDefinition PublicAccessPlaza = new(
        ElevationCardCodes.PublicAccessPlaza,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("plaza-turn-begin", OccurrenceKind.TurnBegin, BehaviorTriggerScope.Controller, [Gain(1)]),
            Trigger(
                "plaza-trashed",
                OccurrenceKind.AccessedCardTrashed,
                BehaviorTriggerScope.Self,
                [GiveTags(1)],
                requirement: BehaviorCondition.ThreatLevelAtLeast(2)),
        ]);

    private static readonly BehaviorCardFilter CorpCard = new(ownerSide: PlayerSide.Corp);

    // 35060 — EL-SCORE-AREA-ABILITY; EL-CR-9.5-paid-abilities.
    private static readonly CardBehaviorDefinition NextBigThing = new(
        ElevationCardCodes.NextBigThing,
        PlayerSide.Corp,
        triggers:
        [
            Score("next-big-thing-score", [AddCounter(AgendaCounter, 1)], onSteal: true),
        ],
        paidAbilities:
        [
            ClickAbility(
                "next-big-thing-draw",
                [Click(), CounterCost(AgendaCounter)],
                [
                    Draw(4),
                    ChooseCards(
                        "next-big-thing-shuffle",
                        BehaviorTarget.CardInZone(BehaviorZone.Headquarters, CorpCard),
                        BehaviorChoiceCardinality.AnyNumber),
                    new BehaviorInstruction(
                        BehaviorInstructionKind.ShuffleIntoDeck,
                        target: ChosenBy("next-big-thing-shuffle"),
                        sourceZone: BehaviorZone.Headquarters,
                        destinationZone: BehaviorZone.Deck),
                ],
                usableFromRunnerScoreArea: true),
        ]);

    // 35059 — EL-DIVIDENDS; EL-CR-1.13-search.
    private static readonly CardBehaviorDefinition EmbeddedReporting = new(
        ElevationCardCodes.EmbeddedReporting,
        PlayerSide.Corp,
        triggers:
        [
            Dividends("embedded-reporting-dividends", 2),
            Trigger(
                "embedded-reporting-discard-phase",
                OccurrenceKind.DiscardPhaseEnded,
                BehaviorTriggerScope.Controller,
                [
                    RemoveCounter(AgendaCounter, 1),
                    .. SearchAndReveal(
                        "embedded-reporting-search",
                        BehaviorZone.Deck,
                        new BehaviorCardFilter(BehaviorCardType.Operation, ownerSide: PlayerSide.Corp)),
                    Branch(BehaviorCondition.FoundCard(), [new BehaviorInstruction(BehaviorInstructionKind.PlaceOnTopOfDeck, target: Chosen)]),
                ],
                TriggerMode.Optional,
                requirement: CounterAtLeast(AgendaCounter, 1)),
        ]);

    // 35071 — EL-DIVIDENDS; EL-CR-1.13-search; EL-BEHAVIOR-INSTALL. The Corp
    // picks "install" (a card of R&D, installed ignoring all costs) or "HQ"
    // (a searched card added to HQ); R&D is shuffled either way.
    private static readonly CardBehaviorDefinition OffTheBooks = new(
        ElevationCardCodes.OffTheBooks,
        PlayerSide.Corp,
        triggers:
        [
            Dividends("off-the-books-dividends", 1),
            Trigger(
                "off-the-books-discard-phase",
                OccurrenceKind.DiscardPhaseEnded,
                BehaviorTriggerScope.Controller,
                [
                    RemoveCounter(AgendaCounter, 1),
                    ChooseOne(
                        "off-the-books-mode",
                        BehaviorChooser.Controller,
                        Option(
                            "install",
                            [
                                // The searched card is revealed (EL.5 review), then installed.
                                .. RevealedInstall("off-the-books-install", BehaviorZone.Deck, optional: false),
                                new BehaviorInstruction(BehaviorInstructionKind.ShuffleDeck, sourceZone: BehaviorZone.Deck),
                            ]),
                        Option(
                            "hq",
                            [
                                .. SearchAndReveal("off-the-books-search", BehaviorZone.Deck, CorpCard),
                                Branch(BehaviorCondition.FoundCard(), [new BehaviorInstruction(
                                    BehaviorInstructionKind.AddToHand,
                                    target: Chosen,
                                    sourceZone: BehaviorZone.Deck,
                                    destinationZone: BehaviorZone.Headquarters)]),
                            ])),
                ],
                TriggerMode.Optional,
                requirement: CounterAtLeast(AgendaCounter, 1)),
        ]);

    /// <summary>
    /// An install of a Corp card from a zone, ignoring all costs, whose card is then revealed: the keyed install
    /// binds the card it installed (EL.4).
    /// </summary>
    private static IReadOnlyList<BehaviorInstruction> RevealedInstall(string key, BehaviorZone zone, bool optional = true) =>
    [
        new BehaviorInstruction(
            BehaviorInstructionKind.InstallCard,
            argument: key,
            target: BehaviorTarget.CardInZone(zone, new BehaviorCardFilter(ownerSide: PlayerSide.Corp)),
            sourceZone: zone,
            optional: optional,
            costRule: BehaviorInstallCostRule.IgnoreAllCosts),
        Branch(BehaviorCondition.Chose(key), [new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: ChosenBy(key))]),
    ];

    // 35082 — EL-RECURRING (rez in this server); EL-PERSISTENT; EL-REGION.
    private static readonly CardBehaviorDefinition MahkotaLangitGrid = new(
        ElevationCardCodes.MahkotaLangitGrid,
        PlayerSide.Corp,
        triggers: [Trigger("mahkota-rez", OccurrenceKind.CardRezzed, BehaviorTriggerScope.Self, [RefillRecurring()])],
        hostedCreditUses: [new BehaviorHostedCreditUse(BehaviorHostedCreditUseKind.RezInServer)],
        recurringCredits: 2,
        rootAssetTrashCostIncrease: 2);

    public static IReadOnlyList<AssetBehaviorSpec> AssetSpecs { get; } =
    [
        new(ElevationCardCodes.PhatGioanBaotixita, trashCost: 3),
        new(ElevationCardCodes.Idiosyncresis, trashCost: 2),
        new(ElevationCardCodes.PublicAccessPlaza, trashCost: 2),
    ];

    public static IReadOnlyList<AgendaBehaviorSpec> AgendaSpecs { get; } =
    [
        new(ElevationCardCodes.Greenmail, 1),
        new(ElevationCardCodes.AggressiveTrendsetting, 1),
        new(ElevationCardCodes.SericultureExpansion, 2),
        new(ElevationCardCodes.NextBigThing, 3),
        new(ElevationCardCodes.EmbeddedReporting, 2),
        new(ElevationCardCodes.OffTheBooks, 2),
    ];

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
    [
        Greenmail,
        AggressiveTrendsetting,
        NebulaTalentManagement,
        PhatGioanBaotixita,
        Idiosyncresis,
        PtUntaian,
        SericultureExpansion,
        SynapseGlobal,
        TheZwickyGroup,
        PublicAccessPlaza,
        NextBigThing,
        EmbeddedReporting,
        OffTheBooks,
        MahkotaLangitGrid,
    ];
}
