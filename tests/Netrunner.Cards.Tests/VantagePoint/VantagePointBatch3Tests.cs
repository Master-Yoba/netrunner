using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>VP.7's cards on the Startup catalog, with VP.6's primitives tested through them.</summary>
public sealed class VantagePointBatch3Tests
{
    private const string Hackerspace = "36006";
    private const string NurseHanh = "36007";
    private const string StickAndPoke = "36008";
    private const string Hiram = "36017";
    private const string ReadWriteShare = "36022";
    private const string MeliesU = "36036";
    private const string LotusHaze = "36037";
    private const string KnowledgeSeeker = "36040";
    private const string Cultivate = "36043";
    private const string HypeMachine = "36055";
    private const string Tocsin = "36060";

    private const string BorrowedGoods = "36013";
    private const string PerfectRecall = "36035";
    private const string Paywall = "36052";
    private const string Lionsmane = "36041";
    private const string Vertigo = "36031";
    private const string HedgeFund = "30075";

    private static bool IsChoice(GameState state) => state.PendingDecision?.Kind == DecisionKind.BehaviorChoice;

    private static int ZoneCount(GameState state, ZoneKind kind) =>
        state.Zones.Where(zone => zone.Kind == kind).Sum(static zone => zone.CardIds.Count);

    private static string View(GameState state, PlayerSide viewer) =>
        EngineJson.Serialize(ObservationBuilder.Build(state, viewer, CatalogFor(state), Runtime));

    /// <summary>The Corp's turn at its first paid window, before its action phase.</summary>
    private static GameState BeginWithoutPassing(ElevationBoard board)
    {
        GameState built = board.Build(catalog: Startup);
        return GameEngine.BeginTurn(built, board.Active, CatalogFor(built), Runtime);
    }

    private static GameState ToAbility(GameState state, string key) =>
        PassUntil(state, value => value.PendingDecision?.LegalActions.Any(action => action.Parameters is AbilityActionParameters ability
            && ability.AbilityKey == key) == true);

    private static CardInstanceId[] Deck(GameState state) => state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDeck).CardIds.ToArray();

    [Fact]
    [Trait("VantagePointCard", Hackerspace)]
    public void HackerspaceHostsACompanionAndAConnectionForLessAndMoreHandSize()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 1 }
            .Add(Hackerspace, Place.Rig)
            .Add(NurseHanh, Place.Grip)
            .Add(StickAndPoke, Place.Grip)
            .Begin(catalog: Startup);
        CardInstanceId host = Card(state, Hackerspace);
        InstallCardParameters hosted = Actions(state).Select(static action => action.Parameters).OfType<InstallCardParameters>()
            .Single(install => install.CardId == Card(state, NurseHanh) && install.HostCardId == host);
        Assert.Equal(0, hosted.CostQuote?.EffectiveCost ?? hosted.CreditCost);
        state = RoundTrip(ToAction(Apply(state, action => action.Parameters == hosted).State));
        state = RoundTrip(ToAction(Apply(state, action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, StickAndPoke) && install.HostCardId == host).State));

        Assert.Equal(host, CardState(state, NurseHanh).HostCardId);
        Assert.Equal(1, Player(state, PlayerSide.Runner).Credits);
        Assert.Contains("\"maximumHandSize\":7", View(state, PlayerSide.Runner), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("VantagePointCard", NurseHanh)]
    [InlineData(2, 2)]
    [InlineData(1, 0)]
    public void NurseHanhDrawsWhenTwoArchivesCardsTurnFaceup(int facedown, int drawn)
    {
        ElevationBoard board = new ElevationBoard(PlayerSide.Runner) { StackFiller = 3 }.Add(NurseHanh, Place.Rig);
        for (int index = 0; index < facedown; index++)
        {
            board = board.Add(index == 0 ? HedgeFund : Paywall, Place.Archives);
        }

        GameState state = board.Begin(catalog: Startup);
        int grip = ZoneCount(state, ZoneKind.RunnerGrip);
        state = Apply(state, action => action.Kind == ActionKind.StartRun && action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, ElevationBoard.ArchivesServer)).State;
        state = RoundTrip(ToAction(state));

        Assert.Equal(grip + drawn, ZoneCount(state, ZoneKind.RunnerGrip));
    }

    [Fact]
    [Trait("VantagePointCard", Hiram)]
    public void HiramLooksAtRAndDsTopWhenHardwareIsInstalled()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpDeckFiller = 2 }
            .Add(Hiram, Place.RunnerIdentity)
            .Add(BorrowedGoods, Place.Grip)
            .Begin(catalog: Startup);
        CardInstanceId top = Deck(state)[0];
        state = Apply(state, action => action.Parameters is InstallCardParameters install && install.CardId == Card(state, BorrowedGoods)).State;
        state = PassUntil(state, static value => IsChoice(value) || value.PendingDecision?.Kind == DecisionKind.TurnAction);

        Assert.True(IsChoice(state));
        // The Runner sees the card it looks at; the Corp's view of R&D is unchanged.
        string runner = View(state, PlayerSide.Runner);
        Assert.Contains($"\"id\":\"{top.Value}\",\"definitionId\"", runner, StringComparison.Ordinal);
        state = RoundTrip(ToAction(ChooseCards(state, top)));
        Assert.Equal(top, Deck(state)[0]);
    }

    [Fact]
    [Trait("VantagePointCard", ReadWriteShare)]
    public void ReadWriteShareHostsGripCardsFacedownToDrawAndShufflesThemBack()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { StackFiller = 3 }
            .Add(ReadWriteShare, Place.Grip)
            .Add(BorrowedGoods, Place.Grip)
            .Begin(catalog: Startup);
        state = Apply(state, action => action.Parameters is InstallCardParameters install && install.CardId == Card(state, ReadWriteShare)).State;
        state = PassUntil(state, HasTrigger);
        state = UseTrigger(state);
        state = RoundTrip(ToAction(ChooseCards(PassUntil(state, IsChoice), Card(state, BorrowedGoods))));

        CardInstanceState hosted = CardState(state, BorrowedGoods);
        Assert.False(hosted.FaceUp);
        Assert.Equal(Card(state, ReadWriteShare), hosted.HostCardId);
        Assert.Equal(1, ZoneCount(state, ZoneKind.RunnerGrip));
        Assert.DoesNotContain($"\"id\":\"{hosted.Id.Value}\",\"definitionId\"", View(state, PlayerSide.Corp), StringComparison.Ordinal);
        Assert.Contains("\"hostedFaceDown\"", View(state, PlayerSide.Runner), StringComparison.Ordinal);

        int stack = ZoneCount(state, ZoneKind.RunnerStack);
        state = RoundTrip(ToAction(Apply(ToAbility(Apply(state, static action => action.Kind == ActionKind.GainCredit).State,
            "read-write-share-trash"), static action => action.Parameters is AbilityActionParameters { AbilityKey: "read-write-share-trash" }).State));
        Assert.Equal(ZoneKind.RunnerStack, ZoneOf(state, BorrowedGoods));
        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, ReadWriteShare));
        Assert.Equal(stack + 1, ZoneCount(state, ZoneKind.RunnerStack));
    }

    [Fact]
    [Trait("VantagePointCard", MeliesU)]
    public void MeliesUSetsACopySecretlyAndFlipsOnTheMatchingCentralRun()
    {
        GameState state = new ElevationBoard { CorpDeckFiller = 3 }
            .Add(MeliesU, Place.CorpIdentity)
            .Add(HedgeFund, Place.Archives)
            .Begin(catalog: Startup);
        state = SpendRemainingClicks(ToAction(state));
        while (state.Turn.ActiveSide == PlayerSide.Corp && !IsChoice(state))
        {
            state = SpendRemainingClicks(Pass(state));
        }

        Assert.Equal(["archives", "hq", "rd"], Actions(state).Select(static action => action.Parameters).OfType<BehaviorChoiceParameters>()
            .SelectMany(static choice => choice.OptionKeys).Order(StringComparer.Ordinal));
        state = RoundTrip(ChooseOption(state, "rd"));
        Assert.Equal(new SecretIdentityState(ServerKind.RAndD, Flipped: false), state.RulesState!.SecretIdentity);
        Assert.DoesNotContain("identitySide", View(state, PlayerSide.Runner), StringComparison.Ordinal);
        Assert.DoesNotContain("RAndD", View(state, PlayerSide.Runner), StringComparison.Ordinal);
        Assert.Contains("\"secretIdentitySide\":\"RAndD\"", View(state, PlayerSide.Corp), StringComparison.Ordinal);

        // The Runner's successful R&D run flips it, and the R&D side trashes the top card to bring Hedge Fund back.
        state = PassUntil(state, static value => value.Turn.ActiveSide == PlayerSide.Runner && value.PendingDecision?.Kind == DecisionKind.TurnAction);
        int corpCredits = Player(state, PlayerSide.Corp).Credits;
        state = Apply(state, action => action.Kind == ActionKind.StartRun && action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, ElevationBoard.Rd)).State;
        state = PassUntil(state, IsChoice);
        Assert.True(state.RulesState!.SecretIdentity!.Flipped);
        Assert.Contains("\"identitySide\":\"RAndD\"", View(state, PlayerSide.Runner), StringComparison.Ordinal);
        CardInstanceId top = Deck(state)[0];
        state = ChooseCards(state, top);
        state = RoundTrip(ChooseCards(PassUntil(state, IsChoice), Card(state, HedgeFund)));
        Assert.Equal(ZoneKind.CorpHand, ZoneOf(state, HedgeFund));

        // Flipped, the front's credit at the end of the Runner's action phase doesn't come; the back flips it at the discard's end.
        state = SpendRemainingClicks(ToAction(state));
        state = PassUntil(state, static value => value.Turn.ActiveSide == PlayerSide.Corp);
        Assert.False(state.RulesState!.SecretIdentity!.Flipped);
        Assert.Equal(corpCredits, Player(state, PlayerSide.Corp).Credits);
    }

    // The review of #157: the Runner's samples don't carry the copy the Corp set. Two states differing only in it sample
    // alike, and the side is drawn among the three; the Corp's own sample keeps it.
    [Fact]
    [Trait("VantagePointCard", MeliesU)]
    public void TheRunnersSamplesDrawMeliesUsSecretCopyAgain()
    {
        GameState state = new ElevationBoard { CorpDeckFiller = 3 }
            .Add(MeliesU, Place.CorpIdentity)
            .Begin(catalog: Startup);
        state = SpendRemainingClicks(ToAction(state));
        while (state.Turn.ActiveSide == PlayerSide.Corp && !IsChoice(state))
        {
            state = SpendRemainingClicks(Pass(state));
        }

        GameState rd = ChooseOption(state, "rd");
        GameState hq = ChooseOption(state, "hq");
        var pool = new OpponentCardPool.Placeholders(new CardDefinitionId(HedgeFund), new CardDefinitionId(HedgeFund));
        var sides = new HashSet<ServerKind>();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            GameState left = ObservationSampler.Sample(rd, PlayerSide.Runner, SamplerKnowledge.None, pool, ElevationHarness.Runtime,
                new DeterministicRandom(seed));
            GameState right = ObservationSampler.Sample(hq, PlayerSide.Runner, SamplerKnowledge.None, pool, ElevationHarness.Runtime,
                new DeterministicRandom(seed));
            Assert.Equal(left.RulesState!.SecretIdentity, right.RulesState!.SecretIdentity);
            Assert.Equal(left.RandomState, right.RandomState);
            sides.Add(left.RulesState!.SecretIdentity!.Side);
            Assert.Equal(rd.RulesState!.SecretIdentity, ObservationSampler.Sample(rd, PlayerSide.Corp, SamplerKnowledge.None, pool,
                ElevationHarness.Runtime, new DeterministicRandom(seed)).RulesState!.SecretIdentity);
        }

        Assert.Equal(3, sides.Count);
    }

    // The review of #157: a central server with nothing installed is a destination; a remote is chosen by one of its cards.
    [Fact]
    [Trait("VantagePointCard", LotusHaze)]
    public void LotusHazeMovesARezzedUpgradeToAnEmptyCentralServer()
    {
        GameState state = ToAbility(BeginWithoutPassing(new ElevationBoard()
            .Add(LotusHaze, Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 3 })
            .Add(PerfectRecall, Place.Root, rezzed: true)), "lotus-haze-move");
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "lotus-haze-move" }).State;
        state = ChooseCards(PassUntil(state, IsChoice), Card(state, PerfectRecall));
        state = ChooseOption(PassUntil(state, IsChoice), "rd");

        state = RoundTrip(ToAction(state));
        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.Rd),
            state.Zones.Single(zone => zone.CardIds.Contains(Card(state, PerfectRecall))).ServerId);
    }

    // The second review of #157: a region can't join another region in a root (CR 3.6.5e), so it stays where it is.
    [Fact]
    [Trait("VantagePointCard", LotusHaze)]
    public void LotusHazeDoesNotPutTwoRegionsInOneRoot()
    {
        GameState state = ToAbility(BeginWithoutPassing(new ElevationBoard()
            .Add(LotusHaze, Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 3 })
            .Add("36065", Place.Root, rezzed: true)
            .Add("35082", Place.Root, server: ElevationBoard.Hq)), "lotus-haze-move");
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "lotus-haze-move" }).State;
        state = ChooseCards(PassUntil(state, IsChoice), Card(state, "36065"));
        state = ChooseOption(PassUntil(state, IsChoice), "hq");

        state = RoundTrip(ToAction(state));
        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.Remote),
            state.Zones.Single(zone => zone.CardIds.Contains(Card(state, "36065"))).ServerId);
    }

    [Fact]
    [Trait("VantagePointCard", LotusHaze)]
    public void LotusHazeMovesARezzedUpgradeToARemoteServerByOneOfItsCards()
    {
        GameState state = ToAbility(BeginWithoutPassing(new ElevationBoard()
            .Add(LotusHaze, Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 3 })
            .Add(PerfectRecall, Place.Root, server: ElevationBoard.Hq, rezzed: true)
            .Add(Paywall, Place.Ice)), "lotus-haze-move");
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "lotus-haze-move" }).State;
        state = ChooseCards(PassUntil(state, IsChoice), Card(state, PerfectRecall));
        state = ChooseOption(PassUntil(state, IsChoice), "remote");
        state = ChooseCards(PassUntil(state, IsChoice), Card(state, Paywall));

        state = RoundTrip(ToAction(state));
        Assert.Equal(ElevationBoard.ServerId(state, ElevationBoard.Remote),
            state.Zones.Single(zone => zone.CardIds.Contains(Card(state, PerfectRecall))).ServerId);
    }

    [Fact]
    [Trait("VantagePointCard", KnowledgeSeeker)]
    public void KnowledgeSeekerArrangesRAndDAndDerezzesWithThreeVirusCounters()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpDeckFiller = 5 }
            .Add(KnowledgeSeeker, Place.Ice, rezzed: true, counters: new Dictionary<string, int> { ["virus"] = 2 })
            .Begin(catalog: Startup);
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        CardInstanceId[] top = Deck(state)[..4];
        state = Apply(state, static action => action.Kind == ActionKind.PassPriority).State;
        // Four rounds: each choice goes on top, so the reverse order ends as chosen.
        foreach (CardInstanceId card in new[] { top[3], top[2], top[1], top[0] }.Reverse())
        {
            state = ChooseCards(PassUntil(state, IsChoice), card);
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal([top[3], top[2], top[1], top[0]], Deck(state)[..4]);
        Assert.False(CardState(state, KnowledgeSeeker).Rezzed);
    }

    [Fact]
    [Trait("VantagePointCard", Cultivate)]
    public void CultivateTrashesOneTakesOneAndArrangesTheRest()
    {
        GameState state = new ElevationBoard { CorpDeckFiller = 6 }
            .Add(Cultivate, Place.Hq)
            .Begin(catalog: Startup);
        CardInstanceId[] top = Deck(state)[..5];
        int hq = ZoneCount(state, ZoneKind.CorpHand);
        state = PlayFirst(state, Cultivate);
        state = ChooseCards(PassUntil(state, IsChoice), top[0]);
        state = ChooseCards(PassUntil(state, IsChoice), top[1]);
        foreach (CardInstanceId card in new[] { top[4], top[3], top[2] })
        {
            state = ChooseCards(PassUntil(state, IsChoice), card);
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal(ZoneKind.CorpDiscard, state.Zones.Single(zone => zone.CardIds.Contains(top[0])).Kind);
        Assert.Equal(ZoneKind.CorpHand, state.Zones.Single(zone => zone.CardIds.Contains(top[1])).Kind);
        Assert.Equal([top[2], top[3], top[4]], Deck(state)[..3]);
        Assert.Equal(hq - 1 + 1, ZoneCount(state, ZoneKind.CorpHand));
    }

    [Fact]
    [Trait("VantagePointCard", HypeMachine)]
    public void HypeMachineTrashesToAdvanceACardInItsRoot()
    {
        GameState state = ToAbility(BeginWithoutPassing(new ElevationBoard()
            .Add(HypeMachine, Place.Root, rezzed: true)
            .Add("30068", Place.Root)), "hype-machine-advance");
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "hype-machine-advance" }).State;
        if (IsChoice(state))
        {
            state = ChooseCards(state, Card(state, "30068"));
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal(1, CardState(state, "30068").AdvancementCounters);
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, HypeMachine));
    }

    [Fact]
    [Trait("VantagePointCard", Tocsin)]
    public void TocsinIsTrashedFromHqToFindABarrierAndASentry()
    {
        GameState state = new ElevationBoard { CorpCredits = 1 }
            .Add(Tocsin, Place.Hq)
            .Add(Paywall, Place.CorpDeck)
            .Add(Lionsmane, Place.CorpDeck)
            .Add(Vertigo, Place.CorpDeck)
            .Begin(catalog: Startup);
        state = Apply(state, static action => action.Parameters is AbilityActionParameters { AbilityKey: "tocsin-search" }).State;
        while (IsChoice(state) || state.PendingDecision?.Kind == DecisionKind.ChooseCard)
        {
            LegalAction pick = Actions(state).First(action => action.Parameters is BehaviorChoiceParameters choice
                && (choice.CardIds.Contains(Card(state, Paywall)) || choice.CardIds.Contains(Card(state, Lionsmane))));
            state = Apply(state, pick).State;
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, Tocsin));
        Assert.Equal(ZoneKind.CorpHand, ZoneOf(state, Paywall));
        Assert.Equal(ZoneKind.CorpHand, ZoneOf(state, Lionsmane));
        Assert.Equal(0, Player(state, PlayerSide.Corp).Credits);
    }

    // The review of #157: Expendable is used from HQ only, so an installed Tocsin doesn't offer it.
    [Fact]
    [Trait("VantagePointCard", Tocsin)]
    public void AnInstalledTocsinDoesNotOfferItsExpendableSearch()
    {
        GameState state = new ElevationBoard()
            .Add(Tocsin, Place.Ice, rezzed: true)
            .Add(Paywall, Place.CorpDeck)
            .Add(Lionsmane, Place.CorpDeck)
            .Begin(catalog: Startup);
        Assert.DoesNotContain(Actions(state), static action => action.Parameters is AbilityActionParameters { AbilityKey: "tocsin-search" });
    }

    // The VP.7 ladder's find: Scapegoat shuffling Methuselah (+1 memory) away from a full rig leaves the Runner over its
    // memory limit, and the Runner trashes a program to fit.
    [Fact]
    [Trait("VantagePointCard", "36054")]
    public void ShufflingAwayAMemoryCardMakesTheRunnerTrashAProgram()
    {
        GameState state = new ElevationBoard { Tags = 0 }
            .Add("36054", Place.Hq)
            .Add("36020", Place.Rig)
            .Add("36023", Place.Rig)
            .Add("36004", Place.Rig)
            .Add("36005", Place.Rig)
            .Add("36015", Place.Rig)
            .Begin(catalog: Startup);
        state = PlayFirst(state, "36054");
        state = ChooseOption(PassUntil(state, IsChoice), "shuffle-runner-card");
        state = ChooseCards(PassUntil(state, IsChoice), Card(state, "36020"));
        Assert.Equal(DecisionKind.MemoryCleanup, state.PendingDecision?.Kind);
    }
}
