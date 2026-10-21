using Netrunner.Core;

using static Netrunner.Cards.ElevationSpec;

namespace Netrunner.Cards;

/// <summary>
/// VP.3 Corp cards: bad publicity, scoring and economy. Printed agenda points, trash costs, strengths and rez costs are
/// hand-copied; CR-26.03 citations are in <c>data/vantage-point/batch-registry.json</c>.
/// </summary>
public static class VantagePointBatch1CorpSpecs
{
    /// <summary>"Remove all tags": more than any game reaches; the removal is capped at the Runner's tags.</summary>
    private const int AllTags = 100;

    private static BehaviorInstruction TakeBadPublicity(int amount) => new(BehaviorInstructionKind.TakeBadPublicity, amount: amount);

    private static BehaviorInstruction RemoveBadPublicity(int amount) => new(BehaviorInstructionKind.RemoveBadPublicity, amount: amount);

    private static BehaviorInstruction RunnerAllottedClicksNextTurn(int amount) =>
        new(BehaviorInstructionKind.GainAllottedClicksNextTurn, amount: amount, recipient: BehaviorPlayerSelector.Runner);

    /// <summary>Search R&amp;D for a card matching the filter, reveal it and add it to HQ.</summary>
    private static IReadOnlyList<BehaviorInstruction> SearchToHq(string key, BehaviorCardFilter filter) =>
    [
        .. SearchAndReveal(key, BehaviorZone.Deck, filter),
        Branch(BehaviorCondition.FoundCard(),
            [new BehaviorInstruction(BehaviorInstructionKind.AddToHand, target: Chosen, sourceZone: BehaviorZone.Deck,
                destinationZone: BehaviorZone.Headquarters)]),
    ];

    // 36026 — VP-CR-1.16.10-steal-score-costs; VP-CR-1.11-clicks. The steal click is the agenda's own steal cost.
    private static readonly AgendaBehaviorSpec MeliesCityLuxuryLineAgenda = new(
        VantagePointCardCodes.MeliesCityLuxuryLine, agendaPoints: 3, stealClicks: 1);

    private static readonly CardBehaviorDefinition MeliesCityLuxuryLine = new(
        VantagePointCardCodes.MeliesCityLuxuryLine,
        PlayerSide.Corp,
        triggers: [Score("melies-score", [GainClicks(1)])]);

    // 36027 — VP-CR-1.16-costs. VP-CLICK-COST-MODIFIER.
    private static readonly AssetBehaviorSpec SynchrocyclotronAsset = new(VantagePointCardCodes.Synchrocyclotron, trashCost: 3);

    private static readonly CardBehaviorDefinition Synchrocyclotron = new(
        VantagePointCardCodes.Synchrocyclotron,
        PlayerSide.Corp,
        firstDoubleOperationClickDiscount: 1);

    // 36032 — VP-CR-1.11-clicks. VP-ALLOTTED-CLICKS: the Runner's next turn has a click fewer or more.
    private static readonly CardBehaviorDefinition CaveatEmptor = new(
        VantagePointCardCodes.CaveatEmptor,
        PlayerSide.Corp,
        costs: [Credits(5)],
        instructions:
        [
            ChooseOne(
                "caveat-emptor-mode",
                BehaviorChooser.Controller,
                Option("gain-6", [Gain(6), RunnerAllottedClicksNextTurn(-1)]),
                Option("gain-10", [Gain(10), RunnerAllottedClicksNextTurn(1)])),
        ]);

    // 36033 — VP-CR-8.1.3-derez; VP-CR-1.16-costs. Double: the extra click is a play cost. VP-AMOUNT reads the cards
    // bound to "chosen". Two must be chosen when two are rezzed; with one, that one (VP-CHOOSE-AS-MANY-AS-ABLE).
    private static readonly CardBehaviorDefinition Realloc = new(
        VantagePointCardCodes.Realloc,
        PlayerSide.Corp,
        costs: [Credits(0), Click()],
        instructions:
        [
            Choose(new BehaviorChoiceSpec(
                "chosen",
                BehaviorChooser.Controller,
                BehaviorChoiceMode.Mandatory,
                BehaviorChoiceCardinality.AsManyAsAble(2),
                target: Installed(new BehaviorCardFilter(BehaviorCardType.Ice, ownerSide: PlayerSide.Corp, rezzed: true)))),
            new BehaviorInstruction(BehaviorInstructionKind.GainCredits,
                amountOperand: BehaviorAmountOperand.Of(BehaviorAmountKind.ChosenCardsPrintedRezCost, 1)),
            new BehaviorInstruction(BehaviorInstructionKind.Derez, target: ChosenBy("chosen")),
        ]);

    // 36034 — VP-CR-8.5-install; VP-CR-1.16-costs. Double. One mode per installable kind.
    private static readonly CardBehaviorDefinition RetirementPlan = new(
        VantagePointCardCodes.RetirementPlan,
        PlayerSide.Corp,
        costs: [Credits(1), Click()],
        instructions:
        [
            ChooseOne(
                "retirement-plan-kind",
                BehaviorChooser.Controller,
                Option("agenda", [InstallFrom(BehaviorZone.Archives, PlayerSide.Corp, BehaviorCardType.Agenda, optional: false)]),
                Option("asset", [InstallFrom(BehaviorZone.Archives, PlayerSide.Corp, BehaviorCardType.Asset, optional: false)]),
                Option("ice", [InstallFrom(BehaviorZone.Archives, PlayerSide.Corp, BehaviorCardType.Ice, optional: false)])),
        ]);

    // 36038 — VP-CR-7.1-access-abilities; VP-CR-10.4-damage. Revealed when accessed in R&D (Elevation's Byte).
    private static readonly AssetBehaviorSpec EscaAsset = new(VantagePointCardCodes.Esca, trashCost: 3);

    private static readonly CardBehaviorDefinition Esca = new(
        VantagePointCardCodes.Esca,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "esca-reveal-in-rd",
                OccurrenceKind.CardAccessed,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                null,
                [new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: Self)],
                requirement: BehaviorCondition.SourceIn(BehaviorZone.Deck),
                activeWhileAccessed: true),
            new BehaviorTrigger(
                "esca-access",
                OccurrenceKind.CardAccessed,
                TriggerMode.Mandatory,
                BehaviorTriggerScope.Self,
                null,
                [RunnerLoses(1), Branch(BehaviorCondition.Tagged(PlayerSide.Runner), [NetDamage(1)])],
                activeWhileAccessed: true),
        ]);

    // 36046 — VP-CR-10.6-bad-publicity; VP-CR-8.7-search. One mode per searchable subtype.
    private static readonly CardBehaviorDefinition EditorialDivision = new(
        VantagePointCardCodes.EditorialDivision,
        PlayerSide.Corp,
        triggers:
        [
            Trigger(
                "editorial-division-first-bad-publicity",
                OccurrenceKind.FirstBadPublicityThisTurn,
                BehaviorTriggerScope.Controller,
                [
                    ChooseOne(
                        "editorial-division-subtype",
                        BehaviorChooser.Controller,
                        Option("black-ops", SearchToHq("editorial-black-ops", new BehaviorCardFilter(BehaviorCardType.NonAgenda, subtype: "black-ops", ownerSide: PlayerSide.Corp))),
                        Option("gray-ops", SearchToHq("editorial-gray-ops", new BehaviorCardFilter(BehaviorCardType.NonAgenda, subtype: "gray-ops", ownerSide: PlayerSide.Corp))),
                        Option("liability", SearchToHq("editorial-liability", new BehaviorCardFilter(BehaviorCardType.NonAgenda, subtype: "liability", ownerSide: PlayerSide.Corp)))),
                ],
                TriggerMode.Optional),
        ]);

    // 36047 — VP-CR-10.6-bad-publicity; VP-CR-10.5-tags; VP-CR-9.6-conditional. VP-SCORING-FACTS.
    private static readonly AgendaBehaviorSpec WitchHuntAgenda = new(VantagePointCardCodes.WitchHunt, agendaPoints: 2);

    private static readonly CardBehaviorDefinition WitchHunt = new(
        VantagePointCardCodes.WitchHunt,
        PlayerSide.Corp,
        triggers:
        [
            Score("witch-hunt-bad-publicity", [TakeBadPublicity(1)], onSteal: true),
            Trigger("witch-hunt-action-phase-end", OccurrenceKind.ActionPhaseEnded, BehaviorTriggerScope.Controller,
                [RemoveTags(AllTags), GiveTags(3)],
                occurrenceController: PlayerSide.Corp,
                requirement: BehaviorCondition.SourceScoredThisTurn()),
        ]);

    // 36048 — VP-CR-1.16.10-steal-score-costs.
    private static readonly AssetBehaviorSpec MagistrateRevontuletAsset = new(VantagePointCardCodes.MagistrateRevontulet, trashCost: 3);

    private static readonly CardBehaviorDefinition MagistrateRevontulet = new(
        VantagePointCardCodes.MagistrateRevontulet,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("magistrate-score", OccurrenceKind.AgendaScoredOrStolen, BehaviorTriggerScope.Any, [RunnerLoses(3)],
                occurrenceController: PlayerSide.Corp),
        ],
        stealCredits: 3);

    // 36049 — VP-CR-10.6-bad-publicity; VP-CR-1.9-counters; VP-CR-10.5-tags. Loaded with power counters; the only
    // removal is its own, after which an empty Nihilo Agent is trashed.
    private static readonly AssetBehaviorSpec NihiloAgentAsset = new(VantagePointCardCodes.NihiloAgent, trashCost: 3);

    private static readonly CardBehaviorDefinition NihiloAgent = new(
        VantagePointCardCodes.NihiloAgent,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("nihilo-rez", OccurrenceKind.CardRezzed, BehaviorTriggerScope.Self, [AddCounter(PowerCounter, 3)]),
            Trigger("nihilo-turn-begin", OccurrenceKind.TurnBegin, BehaviorTriggerScope.Controller,
                [RemoveTags(1), RemoveBadPublicity(1)]),
            Trigger("nihilo-discard-end", OccurrenceKind.DiscardPhaseEnded, BehaviorTriggerScope.Controller,
            [
                GiveTags(1),
                TakeBadPublicity(1),
                RemoveCounter(PowerCounter, 1),
                Branch(CounterAtLeast(PowerCounter, 1), [new BehaviorInstruction(BehaviorInstructionKind.NoOp)], [TrashSelf()]),
            ]),
        ]);

    // 36050 — VP-CR-10.6-bad-publicity. VP-REZ-SERVER-KIND: one rez trigger per central server.
    private static readonly IceBehaviorSpec GrubberIce = new(
        VantagePointCardCodes.Grubber,
        strength: 5,
        IceType.Barrier,
        rezCost: 5,
        [new BehaviorSubroutineSpec("grubber-pay-or-end-1"), new BehaviorSubroutineSpec("grubber-pay-or-end-2")],
        []);

    private static IReadOnlyList<BehaviorInstruction> PayThreeOrEndTheRun(string key) =>
    [
        ChooseOne(
            key,
            BehaviorChooser.Runner,
            Option("pay", [new BehaviorInstruction(BehaviorInstructionKind.NoOp)], costs: [Credits(3)]),
            Option("end-the-run", [new BehaviorInstruction(BehaviorInstructionKind.EndRun)])),
    ];

    private static readonly CardBehaviorDefinition Grubber = new(
        VantagePointCardCodes.Grubber,
        PlayerSide.Corp,
        triggers:
        [
            Subroutine("grubber-pay-or-end-1", PayThreeOrEndTheRun("grubber-pay-1")),
            Subroutine("grubber-pay-or-end-2", PayThreeOrEndTheRun("grubber-pay-2")),
            Trigger("grubber-rez-hq", OccurrenceKind.CardRezzed, BehaviorTriggerScope.Self, [TakeBadPublicity(1)],
                server: ServerKind.Headquarters),
            Trigger("grubber-rez-rd", OccurrenceKind.CardRezzed, BehaviorTriggerScope.Self, [TakeBadPublicity(1)],
                server: ServerKind.RAndD),
            Trigger("grubber-rez-archives", OccurrenceKind.CardRezzed, BehaviorTriggerScope.Self, [TakeBadPublicity(1)],
                server: ServerKind.Archives),
        ]);

    // 36053 — VP-CR-1.16-costs. Double. VP-AMOUNT: protected remotes with a root card.
    private static readonly CardBehaviorDefinition FloodTheMarket = new(
        VantagePointCardCodes.FloodTheMarket,
        PlayerSide.Corp,
        costs: [Credits(3), Click()],
        instructions:
        [
            ChooseCards(
                "flood-the-market-target",
                Installed(new BehaviorCardFilter(BehaviorCardType.Advanceable, ownerSide: PlayerSide.Corp)),
                BehaviorChoiceCardinality.Exactly(1)),
            new BehaviorInstruction(BehaviorInstructionKind.AdvanceCard, target: ChosenBy("flood-the-market-target"),
                amountOperand: BehaviorAmountOperand.Of(BehaviorAmountKind.ProtectedRemotesWithRootCards, 1)),
        ]);

    // 36054 — VP-CR-10.6-bad-publicity. The Runner chooses the mode; removing bad publicity needs some to remove.
    private static readonly CardBehaviorDefinition Scapegoat = new(
        VantagePointCardCodes.Scapegoat,
        PlayerSide.Corp,
        costs: [Credits(0)],
        instructions:
        [
            ChooseOne(
                "scapegoat-mode",
                BehaviorChooser.Runner,
                Option("remove-bad-publicity", [RemoveBadPublicity(2)], condition: BehaviorCondition.BadPublicityAtLeast(1)),
                Option("shuffle-runner-card",
                [
                    // Installed Runner cards are in the rig, ICE-hosted trojans included.
                    ChooseCards(
                        "scapegoat-card",
                        BehaviorTarget.CardsInZone(BehaviorZone.Rig, new BehaviorCardFilter(ownerSide: PlayerSide.Runner)),
                        BehaviorChoiceCardinality.AsManyAsAble(1),
                        BehaviorChooser.Corp),
                    new BehaviorInstruction(BehaviorInstructionKind.ShuffleIntoDeck, target: ChosenBy("scapegoat-card"),
                        sourceZone: BehaviorZone.Rig, destinationZone: BehaviorZone.Stack),
                ])),
        ]);

    // 36057 — VP-CR-10.6-bad-publicity; VP-CR-1.13-hosting; VP-CR-9.9-interrupts. Hosted bad publicity is a counter
    // (VP-BAD-PUBLICITY); VP-UNINSTALL-INTERRUPT returns it.
    private static readonly AssetBehaviorSpec LuanaCamposAsset = new(VantagePointCardCodes.LuanaCampos, trashCost: 3);

    private static readonly CardBehaviorDefinition LuanaCampos = new(
        VantagePointCardCodes.LuanaCampos,
        PlayerSide.Corp,
        triggers:
        [
            Trigger("luana-turn-begin", OccurrenceKind.TurnBegin, BehaviorTriggerScope.Controller,
                [
                    RemoveBadPublicity(1),
                    AddCounter(CardBehaviorDefinition.HostedBadPublicityCounter, 1),
                    Gain(3),
                    Draw(1),
                ],
                TriggerMode.Optional,
                requirement: BehaviorCondition.BadPublicityAtLeast(1)),
        ],
        returnsHostedBadPublicity: true);

    // 36061 — VP-CR-10.1.3-as-an-agenda; VP-CR-9.6-conditional. VP-CARD-AS-AGENDA.
    private static readonly CardBehaviorDefinition Myoshu = new(
        VantagePointCardCodes.Myoshu,
        PlayerSide.Corp,
        costs: [Credits(10)],
        conditions: [BehaviorCondition.TurnEventCountAtLeast(TurnEventKind.AgendaScoredNotInstalledThisTurn, 1)],
        instructions: [new BehaviorInstruction(BehaviorInstructionKind.AddSelfToScoreAreaAsAgenda, amount: 2)]);

    // 36063 — VP-CR-10.6-bad-publicity.
    private static readonly CardBehaviorDefinition VultureFund = new(
        VantagePointCardCodes.VultureFund,
        PlayerSide.Corp,
        costs: [Credits(7)],
        instructions: [Gain(14), TakeBadPublicity(1)]);

    // 36066 — VP-CR-8.7-search; VP-CR-1.17.1-agenda-points. VP-AGENDA-POINTS-MODIFIER. One mode per zone searched; the
    // found agenda goes to HQ or the bottom of R&D.
    private static readonly AgendaBehaviorSpec LetThemDreamAgenda = new(
        VantagePointCardCodes.LetThemDream, agendaPoints: 2, runnerScoreAreaPoints: 1);

    private static readonly BehaviorCardFilter CorpAgenda = new(BehaviorCardType.Agenda, ownerSide: PlayerSide.Corp);

    private static BehaviorInstruction LetThemDreamDestination(string key, BehaviorZone from) =>
        ChooseOne(
            $"{key}-destination",
            BehaviorChooser.Controller,
            Option("hq", [new BehaviorInstruction(BehaviorInstructionKind.AddToHand, target: ChosenBy(key), sourceZone: from,
                destinationZone: BehaviorZone.Headquarters)]),
            Option("rd-bottom", [new BehaviorInstruction(BehaviorInstructionKind.MoveCard, target: ChosenBy(key), sourceZone: from,
                destinationZone: BehaviorZone.Deck)]));

    private static IReadOnlyList<BehaviorInstruction> LetThemDreamFrom(string key, BehaviorZone zone) =>
    [
        ChooseCards(key, BehaviorTarget.CardsInZone(zone, CorpAgenda), BehaviorChoiceCardinality.UpTo(1)),
        Branch(BehaviorCondition.Chose(key),
            [new BehaviorInstruction(BehaviorInstructionKind.Reveal, target: ChosenBy(key)), LetThemDreamDestination(key, zone)]),
    ];

    private static readonly CardBehaviorDefinition LetThemDream = new(
        VantagePointCardCodes.LetThemDream,
        PlayerSide.Corp,
        triggers:
        [
            new BehaviorTrigger(
                "let-them-dream-score",
                OccurrenceKind.AgendaScoredOrStolen,
                TriggerMode.Optional,
                BehaviorTriggerScope.Self,
                null,
                [
                    ChooseOne(
                        "let-them-dream-zone",
                        BehaviorChooser.Controller,
                        Option("rd",
                        [
                            .. SearchAndReveal("let-them-dream-rd", BehaviorZone.Deck, CorpAgenda),
                            Branch(BehaviorCondition.FoundCard(), [LetThemDreamDestination("let-them-dream-rd", BehaviorZone.Deck)]),
                        ]),
                        Option("hq", LetThemDreamFrom("let-them-dream-hq", BehaviorZone.Headquarters)),
                        Option("archives", LetThemDreamFrom("let-them-dream-archives", BehaviorZone.Archives))),
                ],
                occurrenceControllerSide: PlayerSide.Corp),
        ]);

    public static IReadOnlyList<CardBehaviorDefinition> GenericSpecs { get; } =
    [
        MeliesCityLuxuryLine, Synchrocyclotron, CaveatEmptor, Realloc, RetirementPlan, Esca, EditorialDivision, WitchHunt,
        MagistrateRevontulet, NihiloAgent, Grubber, FloodTheMarket, Scapegoat, LuanaCampos, Myoshu, VultureFund, LetThemDream,
    ];

    public static IReadOnlyList<AgendaBehaviorSpec> AgendaSpecs { get; } =
        [MeliesCityLuxuryLineAgenda, WitchHuntAgenda, LetThemDreamAgenda];

    public static IReadOnlyList<AssetBehaviorSpec> AssetSpecs { get; } =
        [SynchrocyclotronAsset, EscaAsset, MagistrateRevontuletAsset, NihiloAgentAsset, LuanaCamposAsset];

    public static IReadOnlyList<IceBehaviorSpec> IceSpecs { get; } = [GrubberIce];
}
