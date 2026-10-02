using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.Elevation;

public sealed class ElevationBatch1EconomyTests
{
    [Fact]
    [Trait("ElevationCard", "35040")]
    public void OttoCampaignLoadsSixPaysTwoEachTurnAndGivesTwoClicksWhenEmpty()
    {
        GameState state = new ElevationBoard(PlayerSide.Corp)
            .Add("35040", Place.Root, rezzed: true, counters: new Dictionary<string, int> { ["credit"] = 2 })
            .Begin();

        // Turn begin took the last 2: the asset is trashed and the Corp gains two clicks.
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35040"));
        Assert.Equal(7, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(5, Player(state, PlayerSide.Corp).Clicks);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35072")]
    public void AnthillTakesFourAndDrawsAtTurnBegin()
    {
        GameState state = new ElevationBoard(PlayerSide.Corp)
            .Add("35072", Place.Root, rezzed: true, counters: new Dictionary<string, int> { ["credit"] = 8 })
            .Begin();

        Assert.Equal(9, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(4, Counter(state, "35072", "credit"));
        Assert.Equal(2, state.GetZone(ZoneKind.CorpHand).CardIds.Count);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35043")]
    public void NanomanagementGainsTwoClicks()
    {
        GameState state = new ElevationBoard(PlayerSide.Corp).Add("35043", Place.Hq).Begin();
        state = Apply(state, action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, "35043")).State;
        state = ToAction(state);

        Assert.Equal(1, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(4, Player(state, PlayerSide.Corp).Clicks);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35014")]
    public void CleanGetawayGainsSixOnASuccessfulRun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 3 }.Add("35014", Place.Grip).Begin();
        ServerId archives = ElevationBoard.ServerId(state, ElevationBoard.ArchivesServer);
        state = Apply(state, action => action.Parameters is PlayCardParameters { TargetServerId: { } server } play
            && play.CardId == Card(state, "35014") && server == archives).State;
        state = PassUntil(state, static value => value.RunState is null);

        Assert.Equal(6, Player(state, PlayerSide.Runner).Credits);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35011")]
    public void RentRiotersSpendsThreeClicksAndTrashesForNine()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner).Add("35011", Place.Rig).Begin();
        state = Apply(state, action => action.Kind == ActionKind.UseAbility).State;
        state = ToAction(state);

        Assert.Equal(14, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(1, Player(state, PlayerSide.Runner).Clicks);
        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, "35011"));
        RoundTrip(state);
    }

    private static GameState Play(GameState state, string code) =>
        Apply(state, action => action.Kind == ActionKind.PlayCard
            && action.Parameters is PlayCardParameters play && play.CardId == Card(state, code)).State;

    private static GameState Choose(GameState state, string optionKey) =>
        Apply(state, action => action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.Count == 1 && choice.OptionKeys[0] == optionKey).State;

    private static bool Offers(GameState state, string optionKey) =>
        state.PendingDecision?.LegalActions.Any(action => action.Parameters is BehaviorChoiceParameters choice
            && choice.OptionKeys.Contains(optionKey)) == true;

    [Fact]
    [Trait("ElevationCard", "35026")]
    public void RitualDrawsOneCardPerRemainingClick()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner).Add("35026", Place.Grip).Begin();
        state = ToAction(Play(state, "35026"));

        Assert.Equal(3, state.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        Assert.Equal(3, Player(state, PlayerSide.Runner).Clicks);
        RoundTrip(state);
    }

    [Theory]
    [InlineData("draw", 3, 3, 4)]
    [InlineData("remove-tags", 3, 1, 0)]
    [Trait("ElevationCard", "35015")]
    public void LieLowSpendsAnExtraClickThenDrawsFourOrRemovesUpToTwoTags(string mode, int tags, int tagsAfter, int drawn)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { Tags = tags }.Add("35015", Place.Grip).Begin();
        state = Play(state, "35015");
        state = ToAction(Choose(state, mode));

        Assert.Equal(2, Player(state, PlayerSide.Runner).Clicks);
        Assert.Equal(4, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(tagsAfter, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(drawn, state.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35015")]
    public void LieLowCannotBePlayedWithOnlyOneClick()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner).Add("35015", Place.Grip).Add("35026", Place.Grip).Begin();
        state = ToAction(Play(state, "35026"));
        state = ToAction(Apply(state, static action => action.Kind == ActionKind.GainCredit).State);
        state = ToAction(Apply(state, static action => action.Kind == ActionKind.GainCredit).State);

        Assert.Equal(1, Player(state, PlayerSide.Runner).Clicks);
        Assert.DoesNotContain(Actions(state), action => action.Parameters is PlayCardParameters play
            && play.CardId == Card(state, "35015"));
    }

    [Theory]
    [InlineData(5, 3, 6)]
    [InlineData(2, 2, 4)]
    [Trait("ElevationCard", "35017")]
    public void TransferOfWealthTagsTheRunnerAndPaysTwicePerCreditTheCorpLost(int corpCredits, int lost, int gained)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = corpCredits, RunnerCredits = 0 }
            .Add("35017", Place.Grip).Begin();
        Assert.Single(Actions(state), action => action.Parameters is PlayCardParameters { TargetServerId: not null } play
            && play.CardId == Card(state, "35017"));
        state = Apply(state, action => action.Parameters is PlayCardParameters { TargetServerId: not null } play
            && play.CardId == Card(state, "35017")).State;
        state = PassUntil(state, static value => value.RunState is null);

        Assert.Equal(corpCredits - lost, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(gained, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35065")]
    public void BiggerPictureRemovesChosenTagsForFiveEachAndTakesWhatTheRunnerLost()
    {
        GameState state = new ElevationBoard(PlayerSide.Corp) { Tags = 2, RunnerCredits = 7 }
            .Add("35065", Place.Hq).Begin();
        state = Play(state, "35065");
        Assert.True(Offers(state, "remove-2-tags"));
        Assert.False(Offers(state, "remove-3-tags"));
        state = ToAction(Choose(state, "remove-2-tags"));

        Assert.Equal(0, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(5 + 7, Player(state, PlayerSide.Corp).Credits);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35065")]
    public void BiggerPictureNeedsATaggedRunner()
    {
        GameState state = new ElevationBoard(PlayerSide.Corp).Add("35065", Place.Hq).Begin();
        Assert.DoesNotContain(Actions(state), action => action.Parameters is PlayCardParameters play
            && play.CardId == Card(state, "35065"));
    }

    [Theory]
    [InlineData(10, "pay-eight", 2, 0)]
    [InlineData(10, "take-damage", 10, 4)]
    [Trait("ElevationCard", "35078")]
    public void MeasuredResponseNeedsThreatFourAndARunLastTurnThenDamagesUnlessTheRunnerPays(
        int runnerCredits, string choice, int creditsAfter, int damage)
    {
        ElevationBoard board = new(PlayerSide.Corp) { CorpCredits = 6, RunnerCredits = runnerCredits, RunnerPoints = 4, GripFiller = 5 };
        board.Add("35078", Place.Hq);
        GameState built = board.Build();
        built = WithRunnerLastTurnSuccess(built);
        GameState state = ToAction(GameEngine.BeginTurn(built, PlayerSide.Corp, Behavior, Runtime));
        state = Play(state, "35078");
        state = ToAction(Choose(state, choice));

        Assert.Equal(creditsAfter, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(1, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(5 - damage, state.GetZone(ZoneKind.RunnerGrip).CardIds.Count);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35078")]
    public void MeasuredResponseIsUnplayableBelowThreatFour()
    {
        ElevationBoard board = new(PlayerSide.Corp) { CorpCredits = 6, RunnerPoints = 3 };
        board.Add("35078", Place.Hq);
        GameState state = ToAction(GameEngine.BeginTurn(WithRunnerLastTurnSuccess(board.Build()), PlayerSide.Corp, Behavior, Runtime));

        Assert.DoesNotContain(Actions(state), action => action.Parameters is PlayCardParameters play
            && play.CardId == Card(state, "35078"));
    }

    private static GameState WithRunnerLastTurnSuccess(GameState state)
    {
        TurnFactsState runnerLast = new TurnFactsState(1, PlayerSide.Runner, state.Turn.TurnNumber - 1, 4, 0, true, false, true, true,
            successfulRun: true);
        TurnHistoryState history = new(1, state.Turn.History!.CorpThisTurn, null, runnerLast, null);
        return new GameState(state.SchemaVersion, state.GameId, state.RulesVersion, state.CardDataVersion,
            state.Players, state.Cards, state.Zones, state.Servers,
            new TurnState(state.Turn.ActiveSide, state.Turn.Phase, state.Turn.TurnNumber, state.Turn.Step, history),
            state.Outcome, state.RandomState, state.Allocator, initialSeed: state.InitialSeed, rulesState: state.RulesState,
            behaviorRegistryVersion: state.BehaviorRegistryVersion, behaviorCatalogFingerprint: state.BehaviorCatalogFingerprint,
            runtimeCardCatalogVersion: state.RuntimeCardCatalogVersion, runtimeCardCatalogFingerprint: state.RuntimeCardCatalogFingerprint);
    }

    private static BehaviorInstallChoice[] InstallChoices(GameState state) =>
        Actions(state).Select(static action => action.Parameters).OfType<BehaviorInstallChoiceParameters>()
            .Where(static parameters => parameters.Choice is not null).Select(static parameters => parameters.Choice!).ToArray();

    private static GameState InstallChoice(GameState state, Func<BehaviorInstallChoice, bool> select) =>
        Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice } && select(choice)).State;

    private static GameState DeclineInstall(GameState state) =>
        Apply(state, static action => action.Parameters is BehaviorInstallChoiceParameters { Choice: null }).State;

    [Fact]
    [Trait("ElevationCard", "35044")]
    public void TopDownSolutionsDrawsTwoThenInstallsUpToTwoFromHq()
    {
        GameState state = new ElevationBoard(PlayerSide.Corp) { CorpDeckFiller = 0 }
            .Add("35044", Place.Hq)
            .Add(ElevationHarness.CorpFillerAsset, Place.Hq)
            .Add(ElevationHarness.CorpFillerIce, Place.Hq)
            .Add(ElevationHarness.CorpFiller, Place.CorpDeck)
            .Add(ElevationHarness.CorpFiller, Place.CorpDeck)
            .Add(ElevationHarness.CorpFiller, Place.CorpDeck)
            .Begin();
        state = Play(state, "35044");
        Assert.Contains(InstallChoices(state), choice => choice.CardId == Card(state, ElevationHarness.CorpFillerAsset)
            && choice.CreateNewRemote);
        state = InstallChoice(state, choice => choice.CardId == Card(state, ElevationHarness.CorpFillerAsset) && choice.CreateNewRemote);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice);
        ServerId hq = ElevationBoard.ServerId(state, ElevationBoard.Hq);
        state = InstallChoice(state, choice => choice.CardId == Card(state, ElevationHarness.CorpFillerIce) && choice.ServerId == hq);
        state = ToAction(state);

        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, ElevationHarness.CorpFillerAsset));
        Assert.Equal(ZoneKind.ServerIce, ZoneOf(state, ElevationHarness.CorpFillerIce));
        Assert.Equal(5 - 2, Player(state, PlayerSide.Corp).Credits);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35044")]
    public void TopDownSolutionsInstallsAreOptional()
    {
        GameState state = new ElevationBoard(PlayerSide.Corp)
            .Add("35044", Place.Hq)
            .Add(ElevationHarness.CorpFillerAsset, Place.Hq)
            .Begin();
        state = Play(state, "35044");
        state = DeclineInstall(state);
        state = DeclineInstall(state);
        state = ToAction(state);

        Assert.Equal(ZoneKind.CorpHand, ZoneOf(state, ElevationHarness.CorpFillerAsset));
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35025")]
    public void IlluminationInstallsFromTheGripOneCheaperEachAfterASuccessfulRdRun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 2 }
            .Add("35025", Place.Grip)
            .Add(ElevationHarness.RunnerFillerProgram, Place.Grip)
            .Add(ElevationHarness.RunnerFillerResource, Place.Grip)
            .Add("35011", Place.Grip)
            .Begin();
        state = Apply(state, action => action.Parameters is PlayCardParameters { TargetServerId: not null } play
            && play.CardId == Card(state, "35025")).State;
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice);
        Assert.All(InstallChoices(state), choice => Assert.True(choice.CreditCost <= 1));
        state = InstallChoice(state, choice => choice.CardId == Card(state, ElevationHarness.RunnerFillerProgram));
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice);
        state = InstallChoice(state, choice => choice.CardId == Card(state, "35011"));
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice
            || value.RunState is null);
        if (state.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice)
        {
            state = InstallChoice(state, choice => choice.CardId == Card(state, ElevationHarness.RunnerFillerResource));
        }

        state = PassUntil(state, static value => value.RunState is null);
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, ElevationHarness.RunnerFillerProgram));
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, "35011"));
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, ElevationHarness.RunnerFillerResource));
        Assert.Equal(2 - 0 - 1 - 0, Player(state, PlayerSide.Runner).Credits);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35077")]
    public void KeyPerformanceIndicatorsResolvesTwoChosenModesInOrder()
    {
        GameState state = new ElevationBoard(PlayerSide.Corp)
            .Add("35077", Place.Hq)
            .Add(ElevationHarness.CorpFillerIce, Place.Hq)
            .Add(ElevationHarness.CorpFillerAgenda, Place.Root)
            .Add("30074", Place.Ice, server: ElevationBoard.Hq)
            .Begin();
        state = Play(state, "35077");
        state = Choose(state, "install-ice-then-advance");
        ServerId hq = ElevationBoard.ServerId(state, ElevationBoard.Hq);
        BehaviorInstallChoice free = Assert.Single(InstallChoices(state), choice => choice.ServerId == hq);
        Assert.Equal(0, free.CreditCost);
        state = InstallChoice(state, choice => choice == free);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        state = Apply(state, action => action.Parameters is BehaviorChoiceParameters choice
            && choice.CardIds.Contains(Card(state, ElevationHarness.CorpFillerAgenda))).State;
        state = ToAction(state);

        Assert.Equal(1, CardState(state, ElevationHarness.CorpFillerAgenda).AdvancementCounters);
        Assert.Equal(ZoneKind.ServerIce, ZoneOf(state, ElevationHarness.CorpFillerIce));
        Assert.Equal(4, Player(state, PlayerSide.Corp).Credits);
        RoundTrip(state);
    }
}
