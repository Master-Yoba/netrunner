using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>VP.3's Runner cards on the Startup catalog.</summary>
public sealed class VantagePointBatch1RunnerTests
{
    private const string TakeADive = "36002";
    private const string Corsair = "36004";
    private const string Vic = "36009";
    private const string SellOut = "36011";
    private const string Tailgate = "36012";
    private const string BorrowedGoods = "36013";
    private const string UnderdomeIrregulars = "36016";
    private const string Methuselah = "36020";
    private const string Touchstone = "36021";
    private const string Stowaway = "36024";
    private const string WordOnTheStreet = "36025";

    private const string Doomscroll = "35063";
    private const string Kessleroid = "35075";
    private const string Hantu = "35008";
    private const string Cookbook = "30009";
    private const string NicoCampaign = "30037";
    private const string OffworldOffice = "30067";

    private static Dictionary<string, int> RunCredits(int credits) => new() { ["run-credit"] = credits };

    private static bool Playable(GameState state, string code) =>
        Actions(state).Any(action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, code));

    private static GameState Play(GameState state, string code, int? server = null) =>
        Apply(state, Actions(state).First(action => action.Parameters is PlayCardParameters play
            && state.GetCard(play.CardId).DefinitionId.Value == code
            && (server is null || play.TargetServerId == ElevationBoard.ServerId(state, server.Value)))).State;

    private static bool CanUse(GameState state, string code) =>
        Actions(state).Any(action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, code));

    private static GameState Use(GameState state, string code) =>
        Apply(state, Actions(state).First(action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, code))).State;

    private static RezCardParameters? RezOffer(GameState state, string code) =>
        state.PendingDecision?.LegalActions.Select(static action => action.Parameters).OfType<RezCardParameters>()
            .FirstOrDefault(rez => rez.CardId == Card(state, code));

    private static GameState Rez(GameState state, string code) =>
        Apply(state, action => action.Parameters == RezOffer(state, code)).State;

    private static GameState RunServer(GameState state, int server) =>
        Apply(state, action => action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, server)).State;

    [Fact]
    [Trait("VantagePointCard", TakeADive)]
    public void TakeADiveGivesBadPublicityWhenASubroutineResolvedDuringASuccessfulRun()
    {
        // Doomscroll's tag and net damage resolve unbroken; the run goes on to HQ.
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 3, HqFiller = 1, GripFiller = 3 }
            .Add(TakeADive, Place.Grip)
            .Add(Doomscroll, Place.Ice, server: ElevationBoard.Hq)
            .Begin(catalog: Startup);
        state = PassUntil(Play(state, TakeADive, ElevationBoard.Hq), value => RezOffer(value, Doomscroll) is not null);
        state = RoundTrip(ToAction(Rez(state, Doomscroll)));

        Assert.Equal(1, state.RulesState!.BadPublicity);
        Assert.Equal(ZoneKind.RunnerRemovedFromGame, ZoneOf(state, TakeADive));
    }

    [Fact]
    [Trait("VantagePointCard", TakeADive)]
    public void TakeADiveWithoutAResolvedSubroutineGivesNothingAndIsStillRemoved()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add(TakeADive, Place.Grip)
            .Begin(catalog: Startup);
        Assert.Equal(2, Actions(state).Select(static action => action.Parameters).OfType<PlayCardParameters>().Count());
        state = RoundTrip(ToAction(Play(state, TakeADive, ElevationBoard.Rd)));

        Assert.Equal(0, state.RulesState?.BadPublicity ?? 0);
        Assert.Equal(ZoneKind.RunnerRemovedFromGame, ZoneOf(state, TakeADive));
    }

    [Fact]
    [Trait("VantagePointCard", Corsair)]
    public void CorsairLowersABarrierWithStealthCreditsAndBreaksIt()
    {
        // Kessleroid is a strength-1 barrier; Corsair's strength is 0. A stealth credit from Touchstone lowers it by 3,
        // then two pool credits break both subroutines.
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 2, CorpCredits = 2 }
            .Add(Corsair, Place.Rig)
            .Add(Touchstone, Place.Rig, counters: RunCredits(1))
            .Add(Kessleroid, Place.Ice)
            .Begin(catalog: Startup);
        state = PassUntil(RunServer(state, ElevationBoard.Remote), value => RezOffer(value, Kessleroid) is not null);
        state = PassUntil(Rez(state, Kessleroid), InEncounter);
        Assert.False(CanBreak(state, Corsair));
        Assert.True(CanUse(state, Corsair));

        state = Use(state, Corsair);
        state = PassUntil(state, value => InEncounter(value) && CanBreak(value, Corsair));
        Assert.Equal(0, Counter(state, Touchstone, "credit"));
        Assert.Equal(2, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("VantagePointCard", Corsair)]
    public void CorsairsStrengthAbilityCantBePaidFromThePool()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5, CorpCredits = 2 }
            .Add(Corsair, Place.Rig)
            .Add(Kessleroid, Place.Ice)
            .Begin(catalog: Startup);
        state = PassUntil(RunServer(state, ElevationBoard.Remote), value => RezOffer(value, Kessleroid) is not null);
        state = PassUntil(Rez(state, Kessleroid), InEncounter);

        Assert.False(CanUse(state, Corsair));
    }

    [Fact]
    [Trait("VantagePointCard", Vic)]
    public void VicDrawsAndRemovesATagOncePerTurn()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 2, Tags = 2 }
            .Add(Vic, Place.RunnerIdentity)
            .Begin(catalog: Startup);
        int clicks = Player(state, PlayerSide.Runner).Clicks;
        state = RoundTrip(ToAction(Use(state, Vic)));

        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
        Assert.Single(state.GetZone(ZoneKind.RunnerGrip).CardIds);
        Assert.Equal((clicks - 1, 1), (Player(state, PlayerSide.Runner).Clicks, Player(state, PlayerSide.Runner).Credits));
        Assert.False(CanUse(state, Vic));
    }

    [Fact]
    [Trait("VantagePointCard", SellOut)]
    public void SellOutTrashesAnInstalledResourceToGainAndDraw()
    {
        Assert.False(Playable(new ElevationBoard(PlayerSide.Runner).Add(SellOut, Place.Grip).Begin(catalog: Startup), SellOut));

        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 1 }
            .Add(SellOut, Place.Grip)
            .Add(Cookbook, Place.Rig)
            .Begin(catalog: Startup);
        state = RoundTrip(ToAction(Play(state, SellOut)));

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, Cookbook));
        Assert.Equal(1 - 1 + 4, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(2, state.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
    }

    [Fact]
    [Trait("VantagePointCard", Tailgate)]
    public void TailgateCostsLessPerIceOnHqAndAccessesTwoMore()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 1, CorpCredits = 0, HqFiller = 4 }
            .Add(Tailgate, Place.Grip)
            .Add(Kessleroid, Place.Ice, server: ElevationBoard.Hq)
            .Add(Kessleroid, Place.Ice, server: ElevationBoard.Hq)
            .Begin(catalog: Startup);
        PlayCardParameters play = Actions(state).Select(static action => action.Parameters).OfType<PlayCardParameters>().Single();
        Assert.Equal(1, play.CreditCost);

        state = RoundTrip(ToAction(Play(state, Tailgate)));
        Assert.Equal(3, state.Turn.History!.ThisTurn(PlayerSide.Runner)!.AccessedCardOccurrenceIds.Count);
    }

    [Theory]
    [Trait("VantagePointCard", BorrowedGoods)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    public void BorrowedGoodsTagsAnUntaggedRunnerOnInstall(int tags, int tagsAfter)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { Tags = tags }
            .Add(BorrowedGoods, Place.Grip)
            .Begin(catalog: Startup);
        state = RoundTrip(ToAction(Apply(state, action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, BorrowedGoods)).State));

        Assert.Equal(tagsAfter, Rules(state, PlayerSide.Runner).Tags);
    }

    [Fact]
    [Trait("VantagePointCard", UnderdomeIrregulars)]
    public void UnderdomeIrregularsRewardsATurnThatSawIceRezzed()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 2, Tags = 1 }
            .Add(UnderdomeIrregulars, Place.Rig)
            .Add(Kessleroid, Place.Ice)
            .Begin(catalog: Startup);
        state = PassUntil(RunServer(state, ElevationBoard.Remote), value => RezOffer(value, Kessleroid) is not null);
        state = SpendRemainingClicks(ToAction(Rez(state, Kessleroid)));
        state = RoundTrip(ToAction(ChooseOption(state, "remove-tag")));

        Assert.Equal(0, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, UnderdomeIrregulars));
    }

    [Fact]
    [Trait("VantagePointCard", UnderdomeIrregulars)]
    public void UnderdomeIrregularsIsTrashedOnATurnWithoutIceRezzed()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add(UnderdomeIrregulars, Place.Rig)
            .Begin(catalog: Startup);
        state = RoundTrip(SpendRemainingClicks(state));

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, UnderdomeIrregulars));
    }

    [Fact]
    [Trait("VantagePointCard", Methuselah)]
    public void MethuselahTrashesGripHardwareForCreditsSpentDuringTheRun()
    {
        // The 2 hosted credits pay Nico Campaign's trash cost of 2; the empty pool pays nothing.
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0 }
            .Add(Methuselah, Place.Rig)
            .Add(BorrowedGoods, Place.Grip)
            .Add(NicoCampaign, Place.Root)
            .Begin(catalog: Startup);
        state = PassUntil(RunServer(state, ElevationBoard.Remote), static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        state = ChooseOption(state, "trash-hardware");
        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, BorrowedGoods));
        Assert.Equal(2, Counter(state, Methuselah, "credit"));

        state = PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.TrashAccessedCard) == true);
        state = RoundTrip(ToAction(Apply(state, Actions(state).First(static action => action.Kind == ActionKind.TrashAccessedCard)).State));
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, NicoCampaign));
        Assert.Equal(0, Counter(state, Methuselah, "credit"));
    }

    [Fact]
    [Trait("VantagePointCard", Touchstone)]
    public void TouchstoneGainsACreditForTheFirstEventEachTurn()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 2 }
            .Add(Touchstone, Place.Rig)
            .Add(Cookbook, Place.Rig)
            .Add(RunnerFillerResource, Place.Rig)
            .Add(SellOut, Place.Grip)
            .Add(SellOut, Place.Grip)
            .Begin(catalog: Startup);
        state = ToAction(Play(state, SellOut));
        Assert.Equal(1, Counter(state, Touchstone, "credit"));
        state = RoundTrip(ToAction(Play(state, SellOut)));
        Assert.Equal(1, Counter(state, Touchstone, "credit"));
    }

    [Fact]
    [Trait("VantagePointCard", Stowaway)]
    public void StowawayGainsOnSuccessfulRunsOnItsHostsServer()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0, CorpCredits = 0, HqFiller = 1 }
            .Add(Stowaway, Place.Grip)
            .Add(Kessleroid, Place.Ice)
            .Begin(catalog: Startup);
        CardInstanceId ice = Card(state, Kessleroid);
        state = ToAction(Apply(state, action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, Stowaway) && install.HostCardId == ice).State);

        state = ToAction(RunServer(state, ElevationBoard.Hq));
        Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);
        state = RoundTrip(ToAction(RunServer(state, ElevationBoard.Remote)));
        Assert.Equal(2, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("VantagePointCard", WordOnTheStreet)]
    public void WordOnTheStreetPaysOutWhenAnEarlierAgendaIsScored()
    {
        // The agenda was installed on an earlier turn: no score cost; Word on the Street is trashed for 4 and a card.
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add(OffworldOffice, Place.Root, advancement: 4)
            .Add(WordOnTheStreet, Place.Rig)
            .Build(catalog: Startup);
        state = GameEngine.BeginTurn(state, PlayerSide.Corp, CatalogFor(state), Runtime);
        int credits = Player(state, PlayerSide.Runner).Credits;
        state = RoundTrip(ToAction(Apply(PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.ScoreAgenda) == true), static action => action.Kind == ActionKind.ScoreAgenda).State));

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, WordOnTheStreet));
        Assert.Equal(credits + 4, Player(state, PlayerSide.Runner).Credits);
    }
}
