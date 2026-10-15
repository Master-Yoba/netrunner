using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>VP.3's Corp cards on the Startup catalog.</summary>
public sealed class VantagePointBatch1CorpTests
{
    private const string MeliesCityLuxuryLine = "36026";
    private const string Synchrocyclotron = "36027";
    private const string CaveatEmptor = "36032";
    private const string Realloc = "36033";
    private const string RetirementPlan = "36034";
    private const string Esca = "36038";
    private const string EditorialDivision = "36046";
    private const string WitchHunt = "36047";
    private const string MagistrateRevontulet = "36048";
    private const string NihiloAgent = "36049";
    private const string Grubber = "36050";
    private const string FloodTheMarket = "36053";
    private const string Scapegoat = "36054";
    private const string LuanaCampos = "36057";
    private const string Myoshu = "36061";
    private const string VultureFund = "36063";
    private const string LetThemDream = "36066";

    private const string Kessleroid = "35075";
    private const string Doomscroll = "35063";
    private const string NicoCampaign = "30037";
    private const string OffworldOffice = "30067";
    private const string Cookbook = "30009";
    private const string HedgeFund = "30075";

    private static GameState BeginWithoutPassing(ElevationBoard board)
    {
        GameState built = board.Build(catalog: Startup);
        return GameEngine.BeginTurn(built, board.Active, CatalogFor(built), Runtime);
    }

    private static GameState Play(GameState state, string code) =>
        Apply(state, Actions(state).First(action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, code))).State;

    private static bool Playable(GameState state, string code) =>
        Actions(state).Any(action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, code));

    private static GameState Score(GameState state) =>
        Apply(PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.ScoreAgenda) == true),
            static action => action.Kind == ActionKind.ScoreAgenda).State;

    private static GameState RunServer(GameState state, int server) =>
        Apply(state, action => action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, server)).State;

    private static GameState RunToAccess(GameState state, int server = ElevationBoard.Remote) =>
        PassUntil(RunServer(state, server), static value => value.RunState?.Step == RunTimingStep.BreachAccessAbility);

    private static RezCardParameters? RezOffer(GameState state, string code) =>
        state.PendingDecision?.LegalActions.Select(static action => action.Parameters).OfType<RezCardParameters>()
            .FirstOrDefault(rez => rez.CardId == Card(state, code));

    private static bool Steals(LegalAction action) => action.Kind == ActionKind.StealAgenda;

    private static bool IsChoice(GameState state) => state.PendingDecision?.Kind == DecisionKind.BehaviorChoice;

    private static int BadPublicity(GameState state) => state.RulesState?.BadPublicity ?? 0;

    [Fact]
    [Trait("VantagePointCard", MeliesCityLuxuryLine)]
    public void MeliesCityLuxuryLineCostsAClickToSteal()
    {
        GameState access = RunToAccess(new ElevationBoard(PlayerSide.Runner)
            .Add(MeliesCityLuxuryLine, Place.Root)
            .Begin(catalog: Startup));
        int clicks = Player(access, PlayerSide.Runner).Clicks;
        GameState stolen = RoundTrip(ToAction(Apply(access, Actions(access).First(Steals)).State));

        Assert.Equal(ZoneKind.RunnerScore, ZoneOf(stolen, MeliesCityLuxuryLine));
        Assert.Equal(clicks - 1, Player(stolen, PlayerSide.Runner).Clicks);
    }

    [Fact]
    [Trait("VantagePointCard", MeliesCityLuxuryLine)]
    public void ScoringMeliesCityLuxuryLineGainsAClick()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard().Add(MeliesCityLuxuryLine, Place.Root, advancement: 5));
        int clicks = Player(ToAction(state), PlayerSide.Corp).Clicks;
        state = RoundTrip(ToAction(Score(state)));

        Assert.Equal(3, Player(state, PlayerSide.Corp).AgendaPoints);
        Assert.Equal(clicks + 1, Player(state, PlayerSide.Corp).Clicks);
    }

    [Fact]
    [Trait("VantagePointCard", Synchrocyclotron)]
    public void SynchrocyclotronMakesTheFirstDoubleOperationCostAClickLess()
    {
        GameState state = new ElevationBoard { CorpCredits = 1 }
            .Add(Synchrocyclotron, Place.Root, rezzed: true)
            .Add(RetirementPlan, Place.Hq)
            .Add(NicoCampaign, Place.Archives)
            .Begin(catalog: Startup);
        int clicks = Player(state, PlayerSide.Corp).Clicks;
        state = Play(state, RetirementPlan);
        Assert.Equal(clicks - 1, Player(state, PlayerSide.Corp).Clicks);
    }

    [Theory]
    [Trait("VantagePointCard", CaveatEmptor)]
    [InlineData("gain-6", 6, 3)]
    [InlineData("gain-10", 10, 5)]
    public void CaveatEmptorTradesCreditsForTheRunnersNextTurn(string mode, int gained, int runnerClicks)
    {
        GameState state = new ElevationBoard { CorpCredits = 5 }
            .Add(CaveatEmptor, Place.Hq)
            .Begin(catalog: Startup);
        state = ToAction(ChooseOption(Play(state, CaveatEmptor), mode));
        Assert.Equal(gained, Player(state, PlayerSide.Corp).Credits);

        state = RoundTrip(PassUntil(SpendRemainingClicks(state), static value => value.Turn.ActiveSide == PlayerSide.Runner
            && value.PendingDecision?.Kind == DecisionKind.TurnAction));
        Assert.Equal(runnerClicks, Player(state, PlayerSide.Runner).Clicks);
    }

    [Fact]
    [Trait("VantagePointCard", Realloc)]
    public void ReallocGainsTwoIcesPrintedRezCostsAndDerezzesThem()
    {
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add(Realloc, Place.Hq)
            .Add(Kessleroid, Place.Ice, rezzed: true)
            .Add(Doomscroll, Place.Ice, server: ElevationBoard.Hq, rezzed: true)
            .Begin(catalog: Startup);
        int clicks = Player(state, PlayerSide.Corp).Clicks;
        state = RoundTrip(ToAction(ChooseCards(Play(state, Realloc), Card(state, Kessleroid), Card(state, Doomscroll))));

        Assert.Equal(2 + 3, Player(state, PlayerSide.Corp).Credits);
        Assert.False(CardState(state, Kessleroid).Rezzed);
        Assert.False(CardState(state, Doomscroll).Rezzed);
        Assert.Equal(clicks - 2, Player(state, PlayerSide.Corp).Clicks);
    }

    [Fact]
    [Trait("VantagePointCard", Realloc)]
    public void ReallocWithOneRezzedIceTakesThatOne()
    {
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add(Realloc, Place.Hq)
            .Add(Kessleroid, Place.Ice, rezzed: true)
            .Add(Doomscroll, Place.Ice, server: ElevationBoard.Hq)
            .Begin(catalog: Startup);
        state = RoundTrip(ToAction(ChooseCards(Play(state, Realloc), Card(state, Kessleroid))));

        Assert.Equal(2, Player(state, PlayerSide.Corp).Credits);
        Assert.False(CardState(state, Kessleroid).Rezzed);
    }

    [Fact]
    [Trait("VantagePointCard", RetirementPlan)]
    public void RetirementPlanInstallsIceFromArchives()
    {
        GameState state = new ElevationBoard { CorpCredits = 1 }
            .Add(RetirementPlan, Place.Hq)
            .Add(Kessleroid, Place.Archives)
            .Begin(catalog: Startup);
        state = ChooseOption(Play(state, RetirementPlan), "ice");
        LegalAction install = Actions(state).First(action =>
            action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice } && choice.CardId == Card(state, Kessleroid));
        state = RoundTrip(ToAction(Apply(state, install).State));

        Assert.Equal(ZoneKind.ServerIce, ZoneOf(state, Kessleroid));
    }

    [Fact]
    [Trait("VantagePointCard", RetirementPlan)]
    public void RetirementPlanOffersOnlyTheKindsArchivesHolds()
    {
        // PR #153's review: with only ICE in Archives, the agenda and asset modes would skip the mandatory install.
        GameState state = new ElevationBoard { CorpCredits = 1 }
            .Add(RetirementPlan, Place.Hq)
            .Add(Kessleroid, Place.Archives)
            .Begin(catalog: Startup);
        state = Play(state, RetirementPlan);

        Assert.Equal(["ice"], Actions(state).Select(static action => action.Parameters).OfType<BehaviorChoiceParameters>()
            .SelectMany(static choice => choice.OptionKeys));
    }

    [Fact]
    [Trait("VantagePointCard", RetirementPlan)]
    public void RetirementPlanWithNothingToInstallDoesNothing()
    {
        GameState state = new ElevationBoard { CorpCredits = 1 }
            .Add(RetirementPlan, Place.Hq)
            .Add(HedgeFund, Place.Archives)
            .Begin(catalog: Startup);
        state = RoundTrip(ToAction(Play(state, RetirementPlan)));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, RetirementPlan));
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, HedgeFund));
    }

    [Theory]
    [Trait("VantagePointCard", Esca)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    public void EscaTakesACreditAndHurtsATaggedRunner(int tags, int damage)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 3, Tags = tags, GripFiller = 2 }
            .Add(Esca, Place.Root)
            .Begin(catalog: Startup);
        state = RoundTrip(ToAction(RunServer(state, ElevationBoard.Remote)));

        Assert.Equal(2, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(2 - damage, state.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
    }

    [Fact]
    [Trait("VantagePointCard", Esca)]
    public void EscaIsRevealedWhenAccessedInRd()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 3, CorpDeckFiller = 0 }
            .Add(Esca, Place.CorpDeck)
            .Begin(catalog: Startup);
        state = RunServer(state, ElevationBoard.Rd);
        var events = new List<EngineEvent>();
        for (int step = 0; step < 40 && state.RunState is not null; step++)
        {
            ApplyResult passed = PassResult(state);
            events.AddRange(passed.Events);
            state = passed.State;
        }

        Assert.Contains(events, static value => value.Kind == EngineEventKinds.BehaviorCardRevealed);
    }

    [Fact]
    [Trait("VantagePointCard", EditorialDivision)]
    public void EditorialDivisionSearchesForAGrayOpsTheFirstTimeTheCorpTakesBadPublicity()
    {
        // The turn's draw takes the top card, Hedge Fund; Scapegoat stays in R&D for the search.
        GameState state = new ElevationBoard { CorpCredits = 7, CorpDeckFiller = 2 }
            .Add(EditorialDivision, Place.CorpIdentity)
            .Add(VultureFund, Place.Hq)
            .Add(HedgeFund, Place.CorpDeck)
            .Add(Scapegoat, Place.CorpDeck)
            .Begin(catalog: Startup);
        Assert.Equal(ZoneKind.CorpDeck, ZoneOf(state, Scapegoat));
        state = PassUntil(Play(state, VultureFund), HasTrigger);
        state = ChooseOption(PassUntil(UseTrigger(state), IsChoice), "gray-ops");
        state = RoundTrip(ToAction(ChooseCards(PassUntil(state, IsChoice), Card(state, Scapegoat))));

        Assert.Equal(ZoneKind.CorpHand, ZoneOf(state, Scapegoat));
        Assert.Equal(1, BadPublicity(state));
    }

    [Fact]
    [Trait("VantagePointCard", WitchHunt)]
    public void WitchHuntGivesBadPublicityAndResetsTheRunnersTagsToThree()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard { Tags = 1 }.Add(WitchHunt, Place.Root, advancement: 4));
        state = ToAction(Score(state));
        Assert.Equal(1, BadPublicity(state));
        state = RoundTrip(PassUntil(SpendRemainingClicks(state), static value => value.Turn.ActiveSide == PlayerSide.Runner));

        Assert.Equal(3, Rules(state, PlayerSide.Runner).Tags);
    }

    [Fact]
    [Trait("VantagePointCard", WitchHunt)]
    public void StealingWitchHuntGivesTheCorpBadPublicity()
    {
        GameState stolen = RoundTrip(ToAction(RunServer(new ElevationBoard(PlayerSide.Runner).Add(WitchHunt, Place.Root)
            .Begin(catalog: Startup), ElevationBoard.Remote)));

        Assert.Equal(ZoneKind.RunnerScore, ZoneOf(stolen, WitchHunt));
        Assert.Equal(1, BadPublicity(stolen));
    }

    [Fact]
    [Trait("VantagePointCard", MagistrateRevontulet)]
    public void MagistrateRevontuletChargesThreeToStealAndOnEveryScore()
    {
        GameState access = RunToAccess(new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 3 }
            .Add(OffworldOffice, Place.Root)
            .Add(MagistrateRevontulet, Place.Root, server: ElevationBoard.Remote + 1, rezzed: true)
            .Begin(catalog: Startup));
        GameState stolen = ToAction(Apply(access, Actions(access).First(Steals)).State);
        Assert.Equal(0, Player(stolen, PlayerSide.Runner).Credits);

        GameState score = BeginWithoutPassing(new ElevationBoard { RunnerCredits = 5 }
            .Add(OffworldOffice, Place.Root, advancement: 4)
            .Add(MagistrateRevontulet, Place.Root, server: ElevationBoard.Remote + 1, rezzed: true));
        score = RoundTrip(ToAction(Score(score)));
        Assert.Equal(2, Player(score, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("VantagePointCard", NihiloAgent)]
    public void NihiloAgentCyclesTagsAndBadPublicityUntilEmpty()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard { CorpCredits = 1 }.Add(NihiloAgent, Place.Root));
        state = PassUntil(state, value => RezOffer(value, NihiloAgent) is not null);
        state = ToAction(Apply(state, action => action.Parameters == RezOffer(state, NihiloAgent)).State);
        Assert.Equal(3, Counter(state, NihiloAgent, "power"));

        state = PassUntil(SpendRemainingClicks(state), static value => value.Turn.ActiveSide == PlayerSide.Runner);
        Assert.Equal((1, 1), (Rules(state, PlayerSide.Runner).Tags, BadPublicity(state)));
        Assert.Equal(2, Counter(state, NihiloAgent, "power"));

        state = RoundTrip(PassUntil(SpendRemainingClicks(state), static value => value.Turn.ActiveSide == PlayerSide.Corp
            && value.PendingDecision?.Kind == DecisionKind.TurnAction));
        Assert.Equal((0, 0), (Rules(state, PlayerSide.Runner).Tags, BadPublicity(state)));
    }

    [Fact]
    [Trait("VantagePointCard", NihiloAgent)]
    public void AnEmptyNihiloAgentIsTrashed()
    {
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add(NihiloAgent, Place.Root, rezzed: true, counters: new Dictionary<string, int> { ["power"] = 1 })
            .Begin(catalog: Startup);
        state = RoundTrip(PassUntil(SpendRemainingClicks(state), static value => value.Turn.ActiveSide == PlayerSide.Runner));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, NihiloAgent));
    }

    [Theory]
    [Trait("VantagePointCard", Grubber)]
    [InlineData(ElevationBoard.Hq, 1)]
    [InlineData(ElevationBoard.Remote, 0)]
    public void GrubberGivesBadPublicityWhenRezzedOnACentral(int server, int badPublicity)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 5, RunnerCredits = 3, HqFiller = 1 }
            .Add(Grubber, Place.Ice, server: server)
            .Begin(catalog: Startup);
        state = PassUntil(RunServer(state, server), value => RezOffer(value, Grubber) is not null);
        state = Apply(state, action => action.Parameters == RezOffer(state, Grubber)).State;
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        Assert.Equal(badPublicity, BadPublicity(state));

        // The Runner pays 3 for the first subroutine and can't pay for the second: the run ends.
        state = ChooseOption(state, "pay");
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        Assert.DoesNotContain(Actions(state), static action => action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.Contains("pay"));
        state = RoundTrip(ToAction(ChooseOption(state, "end-the-run")));
        Assert.Null(state.RunState);
        Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("VantagePointCard", FloodTheMarket)]
    public void FloodTheMarketAdvancesOncePerProtectedRemoteWithARootCard()
    {
        GameState state = new ElevationBoard { CorpCredits = 3, RemoteServers = 3 }
            .Add(FloodTheMarket, Place.Hq)
            .Add(OffworldOffice, Place.Root, server: ElevationBoard.Remote)
            .Add(Kessleroid, Place.Ice, server: ElevationBoard.Remote)
            .Add(NicoCampaign, Place.Root, server: ElevationBoard.Remote + 1)
            .Add(Kessleroid, Place.Ice, server: ElevationBoard.Remote + 1)
            .Add(NicoCampaign, Place.Root, server: ElevationBoard.Remote + 2)
            .Begin(catalog: Startup);
        state = RoundTrip(ToAction(ChooseCards(Play(state, FloodTheMarket), Card(state, OffworldOffice))));

        Assert.Equal(2, CardState(state, OffworldOffice).AdvancementCounters);
    }

    [Fact]
    [Trait("VantagePointCard", Scapegoat)]
    public void TheRunnerCanRemoveTheCorpsBadPublicityWithScapegoat()
    {
        GameState state = new ElevationBoard { BadPublicity = 3 }
            .Add(Scapegoat, Place.Hq)
            .Add(Cookbook, Place.Rig)
            .Begin(catalog: Startup);
        state = RoundTrip(ToAction(ChooseOption(Play(state, Scapegoat), "remove-bad-publicity")));

        Assert.Equal(1, BadPublicity(state));
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, Cookbook));
    }

    [Fact]
    [Trait("VantagePointCard", Scapegoat)]
    public void WithoutBadPublicityScapegoatShufflesAnInstalledRunnerCardIntoTheStack()
    {
        GameState state = new ElevationBoard()
            .Add(Scapegoat, Place.Hq)
            .Add(Cookbook, Place.Rig)
            .Begin(catalog: Startup);
        state = Play(state, Scapegoat);
        Assert.DoesNotContain(Actions(state), static action => action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.Contains("remove-bad-publicity"));

        state = ChooseOption(state, "shuffle-runner-card");
        state = RoundTrip(ToAction(ChooseCards(state, Card(state, Cookbook))));
        Assert.Equal(ZoneKind.RunnerStack, ZoneOf(state, Cookbook));
    }

    [Fact]
    [Trait("VantagePointCard", Scapegoat)]
    public void ScapegoatWithNothingInstalledShufflesNothing()
    {
        GameState state = new ElevationBoard().Add(Scapegoat, Place.Hq).Begin(catalog: Startup);
        state = RoundTrip(ToAction(ChooseOption(Play(state, Scapegoat), "shuffle-runner-card")));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, Scapegoat));
    }

    [Fact]
    [Trait("VantagePointCard", LuanaCampos)]
    public void LuanaCamposHostsBadPublicityForCreditsAndACardAndReturnsItWhenTrashed()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard { CorpCredits = 0, BadPublicity = 1 }
            .Add(LuanaCampos, Place.Root, rezzed: true));
        state = ToAction(UseTrigger(PassUntil(state, HasTrigger)));
        Assert.Equal(0, BadPublicity(state));
        Assert.Equal(1, Counter(state, LuanaCampos, CardBehaviorDefinition.HostedBadPublicityCounter));
        Assert.Equal(3, Player(state, PlayerSide.Corp).Credits);

        GameState runner = PassUntil(SpendRemainingClicks(state), static value => value.Turn.ActiveSide == PlayerSide.Runner
            && value.PendingDecision?.Kind == DecisionKind.TurnAction);
        runner = PassUntil(RunServer(runner, ElevationBoard.Remote), static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.TrashAccessedCard) == true);
        runner = RoundTrip(ToAction(Apply(runner, Actions(runner).First(static action => action.Kind == ActionKind.TrashAccessedCard)).State));
        Assert.Equal(1, BadPublicity(runner));
    }

    [Fact]
    [Trait("VantagePointCard", Myoshu)]
    public void MyoshuJoinsTheScoreAreaAfterAnEarlierAgendaIsScored()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard { CorpCredits = 10 }
            .Add(OffworldOffice, Place.Root, advancement: 4)
            .Add(Myoshu, Place.Hq));
        Assert.False(Playable(ToAction(state), Myoshu));
        state = ToAction(Score(state));
        Assert.True(Playable(state, Myoshu));

        state = RoundTrip(ToAction(Play(state, Myoshu)));
        Assert.Equal(ZoneKind.CorpScore, ZoneOf(state, Myoshu));
        Assert.Equal(2 + 2, Player(state, PlayerSide.Corp).AgendaPoints);
    }

    [Fact]
    [Trait("VantagePointCard", VultureFund)]
    public void VultureFundGainsFourteenAndTakesBadPublicity()
    {
        GameState state = new ElevationBoard { CorpCredits = 7 }.Add(VultureFund, Place.Hq).Begin(catalog: Startup);
        state = RoundTrip(ToAction(Play(state, VultureFund)));

        Assert.Equal(14, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(1, BadPublicity(state));
    }

    [Fact]
    [Trait("VantagePointCard", LetThemDream)]
    public void LetThemDreamFindsAnAgendaInRdForHq()
    {
        // The turn's draw takes the top card, Hedge Fund; the agenda stays in R&D for the search.
        GameState state = BeginWithoutPassing(new ElevationBoard()
            .Add(LetThemDream, Place.Root, advancement: 4)
            .Add(HedgeFund, Place.CorpDeck)
            .Add(OffworldOffice, Place.CorpDeck));
        state = PassUntil(Score(state), HasTrigger);
        Assert.Equal(ZoneKind.CorpDeck, ZoneOf(state, OffworldOffice));
        state = ChooseOption(PassUntil(UseTrigger(state), IsChoice), "rd");
        state = ChooseCards(PassUntil(state, IsChoice), Card(state, OffworldOffice));
        state = ChooseOption(PassUntil(state, IsChoice), "hq");
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.CorpHand, ZoneOf(state, OffworldOffice));
    }

    [Fact]
    [Trait("VantagePointCard", LetThemDream)]
    public void LetThemDreamIsWorthOneLessToTheRunner()
    {
        GameState stolen = RoundTrip(ToAction(RunServer(new ElevationBoard(PlayerSide.Runner).Add(LetThemDream, Place.Root)
            .Begin(catalog: Startup), ElevationBoard.Remote)));

        Assert.Equal(ZoneKind.RunnerScore, ZoneOf(stolen, LetThemDream));
        Assert.Equal(1, Player(stolen, PlayerSide.Runner).AgendaPoints);
    }
}
