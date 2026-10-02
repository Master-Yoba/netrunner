using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.Elevation;

public sealed class ElevationBatch2Tests
{
    private const string HedgeFund = "30075";
    private const string Cleaver = "30006";

    private static int ZoneCount(GameState state, ZoneKind kind) =>
        state.Zones.Single(zone => zone.Kind == kind).CardIds.Count;

    private static GameState PlayFirst(GameState state, string code) =>
        Apply(state, Actions(state).First(action => action.Kind == ActionKind.PlayCard
            && action.Parameters is PlayCardParameters play && state.Cards.Single(card => card.Id == play.CardId).DefinitionId.Value == code)).State;

    private static GameState UseAbilityOf(GameState state, string code) =>
        Apply(state, action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, code)).State;

    [Fact]
    [Trait("ElevationCard", "35081")]
    public void PettyCashFromHqGainsFiveCredits()
    {
        GameState state = new ElevationBoard()
            .Add("35081", Place.Hq)
            .Begin();
        state = RoundTrip(ToAction(PlayFirst(state, "35081")));

        Assert.Equal(5 - 3 + 5, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(3 - 1, Player(state, PlayerSide.Corp).Clicks);
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35081"));
    }

    [Fact]
    [Trait("ElevationCard", "35081")]
    public void PettyCashPlayedFromArchivesGainsAClickAndIsRemovedFromTheGame()
    {
        GameState state = new ElevationBoard()
            .Add("35081", Place.ArchivesFaceUp)
            .Begin();
        state = RoundTrip(ToAction(UseAbilityOf(state, "35081")));

        Assert.Equal(5 - 3 + 5, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(3 - 1 + 1, Player(state, PlayerSide.Corp).Clicks);
        Assert.Equal(ZoneKind.CorpRemovedFromGame, ZoneOf(state, "35081"));
    }

    [Fact]
    [Trait("ElevationCard", "35081")]
    public void PettyCashCannotBePlayedAfterAnActionFinished()
    {
        GameState state = new ElevationBoard()
            .Add("35081", Place.ArchivesFaceUp)
            .Begin();
        state = ToAction(Apply(state, static action => action.Kind == ActionKind.GainCredit).State);

        Assert.DoesNotContain(Actions(state), action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, "35081"));
    }

    [Fact]
    [Trait("ElevationCard", "35039")]
    public void HumanoidResourcesGainsDrawsInstallsAndPlaysAnOperation()
    {
        GameState state = new ElevationBoard { CorpDeckFiller = 6 }
            .Add("35039", Place.Root, rezzed: true)
            .Add(HedgeFund, Place.Hq)
            .Add("35062", Place.Hq)
            .Begin();
        state = UseAbilityOf(state, "35039");
        state = Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters
        {
            Choice: { CreateNewRemote: true } choice,
        } && choice.CardId == Card(state, "35062")).State;
        // Only operations are left in HQ, so the second install has nothing to offer.
        Assert.NotEqual(DecisionKind.BehaviorInstallChoice, state.PendingDecision!.Kind);
        state = ChooseCards(state, Card(state, HedgeFund));
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35039"));
        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, "35062"));
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, HedgeFund));
        Assert.Equal(5 + 4 - 5 + 9, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(0, Player(state, PlayerSide.Corp).Clicks);
    }

    [Fact]
    [Trait("ElevationCard", "35038")]
    public void ProjectIngatanInstallsFromArchivesIgnoringAllCosts()
    {
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add("35038", Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 1 })
            .Add("35062", Place.Archives)
            .Begin();
        state = UseTrigger(SpendRemainingClicks(state));
        state = Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters
        {
            Choice: { CreateNewRemote: true } choice,
        } && choice.CardId == Card(state, "35062")).State;
        state = RoundTrip(state);

        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, "35062"));
        Assert.Equal(0, Counter(state, "35038", "agenda"));
        Assert.Equal(3, Player(state, PlayerSide.Corp).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35004")]
    public void ScroungeInstallsAProgramFromTheHeapAndCanBottomAnother()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10, StackFiller = 2 }
            .Add("35004", Place.Grip)
            .Add(Cleaver, Place.Heap)
            .Add("35008", Place.Heap)
            .Begin();
        state = PlayFirst(state, "35004");
        state = Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, Cleaver)).State;
        state = ChooseCards(state, Card(state, "35008"));
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, Cleaver));
        Assert.Equal(Card(state, "35008"), state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerStack).CardIds[^1]);
        Assert.Equal(10 - 1 - 3, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(4 - 2, Player(state, PlayerSide.Runner).Clicks);
    }

    [Fact]
    [Trait("ElevationCard", "35036")]
    public void PoetriInstallsANonAgendaFromTheTopThreeWhenScoring()
    {
        GameState built = new ElevationBoard { CorpDeckFiller = 0 }
            .Add("35036", Place.CorpIdentity)
            .Add(CorpFillerAgenda, Place.Root, advancement: 3)
            .Add(CorpFiller, Place.CorpDeck)
            .Add(CorpFiller, Place.CorpDeck)
            .Add(CorpFiller, Place.CorpDeck)
            .Add("35062", Place.CorpDeck)
            .Add("35061", Place.CorpDeck)
            .Build();
        GameState state = GameEngine.BeginTurn(built, PlayerSide.Corp, Behavior, Runtime);
        state = Apply(PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.ScoreAgenda) == true), static action => action.Kind == ActionKind.ScoreAgenda).State;
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice);

        // After the mandatory draw the top three are two filler operations and
        // Public Access Plaza; Idiosyncresis, fourth, is out of reach.
        Assert.Contains(Actions(state), action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, "35062"));
        Assert.DoesNotContain(Actions(state), action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, "35061"));
        state = Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters
        {
            Choice: { CreateNewRemote: true } choice,
        } && choice.CardId == Card(state, "35062")).State;
        state = RoundTrip(state);

        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, "35062"));
    }

    [Fact]
    [Trait("ElevationCard", "35013")]
    public void MuslihaTAddsATopIcebreakerToTheGrip()
    {
        GameState built = new ElevationBoard(PlayerSide.Runner) { StackFiller = 0, GripFiller = 0 }
            .Add("35013", Place.RunnerIdentity)
            .Add(Cleaver, Place.Stack)
            .Add(RunnerFiller, Place.Stack)
            .Build();
        GameState state = GameEngine.BeginTurn(built, PlayerSide.Runner, Behavior, Runtime);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        // EL.5 review: the Runner sees the top card before choosing what to reveal.
        Assert.Contains(Cleaver, EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner, CatalogFor(state)).PrivateState));
        Assert.DoesNotContain(Actions(state), static action => action.Parameters is BehaviorChoiceParameters { OptionKeys.Count: > 0 });
        Assert.DoesNotContain(Cleaver, EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp, CatalogFor(state))));
        state = ChooseCards(state, Card(state, Cleaver));
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.RunnerGrip, ZoneOf(state, Cleaver));
    }

    [Theory]
    [Trait("ElevationCard", "35013")]
    [InlineData("30028", true)]
    [InlineData("30030", false)]
    public void MuslihaTOffersATopRunEventButNothingOfAnotherKind(string top, bool offered)
    {
        GameState built = new ElevationBoard(PlayerSide.Runner) { StackFiller = 0, GripFiller = 0 }
            .Add("35013", Place.RunnerIdentity)
            .Add(top, Place.Stack)
            .Add(RunnerFiller, Place.Stack)
            .Build();
        GameState state = GameEngine.BeginTurn(built, PlayerSide.Runner, Behavior, Runtime);
        state = PassUntil(state, static value => value.PendingDecision?.Kind is DecisionKind.BehaviorChoice or DecisionKind.TurnAction);
        Assert.Equal(offered, state.PendingDecision!.Kind == DecisionKind.BehaviorChoice);
        if (offered) state = ChooseCards(state, Card(state, top));
        state = RoundTrip(ToAction(state));

        Assert.Equal(offered ? ZoneKind.RunnerGrip : ZoneKind.RunnerStack, ZoneOf(state, top));
    }

    private const string Karuna = "30047";
    private const string OffworldOffice = "30067";

    private static GameState StartRun(GameState state, int server)
    {
        ServerId target = ElevationBoard.ServerId(state, server);
        return Apply(state, action => action.Kind == ActionKind.StartRun
            && action.Parameters is RunServerParameters run && run.ServerId == target).State;
    }

    [Fact]
    [Trait("ElevationCard", "35050")]
    public void ByteTagsAndDoesThreeNetWhenAccessedInRd()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpDeckFiller = 3, GripFiller = 5 }
            .Add("35050", Place.CorpDeck)
            .Begin();
        state = UseTrigger(PassToTrigger(StartRun(state, ElevationBoard.Rd)));
        state = ChooseOption(state, "pay");
        state = RoundTrip(ToAction(state));

        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(5 - 3, ZoneCount(state, ZoneKind.RunnerGrip));
        Assert.Equal(5 - 4, Player(state, PlayerSide.Corp).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35050")]
    public void ByteDoesNothingWhenAccessedInArchives()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35050", Place.ArchivesFaceUp)
            .Begin();
        state = PassUntil(StartRun(state, ElevationBoard.ArchivesServer), static value => HasTrigger(value) || value.RunState is null);

        Assert.False(HasTrigger(state));
    }

    [Fact]
    [Trait("ElevationCard", "35024")]
    public void MagdaleneInstallsAProgramDiscardedToHandSize()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 5, RunnerCredits = 10 }
            .Add("35024", Place.RunnerIdentity)
            .Add(Cleaver, Place.Grip)
            .Begin();
        for (int click = 0; click < 4; click++)
        {
            state = PassUntil(Apply(state, static action => action.Kind == ActionKind.GainCredit).State,
                static value => value.PendingDecision?.Kind is DecisionKind.TurnAction or DecisionKind.TurnDiscard);
        }

        state = Apply(state, action => action.Parameters is CardChoiceParameters choice && choice.CardId == Card(state, Cleaver)).State;
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorInstallChoice);
        state = Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, Cleaver)).State;
        state = RoundTrip(state);

        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, Cleaver));
    }

    [Fact]
    [Trait("ElevationCard", "35068")]
    public void BangunTurnsAnInstalledAgendaFaceup()
    {
        GameState state = new ElevationBoard()
            .Add("35068", Place.CorpIdentity)
            .Add(CorpFillerAgenda, Place.Hq)
            .Begin();
        state = Apply(state, action => action.Parameters is InstallCardParameters { CreateNewRemote: true } install
            && install.CardId == Card(state, CorpFillerAgenda)).State;
        state = RoundTrip(ToAction(UseTrigger(PassToTrigger(state))));

        Assert.True(CardState(state, CorpFillerAgenda).FaceUp);
        Assert.False(CardState(state, CorpFillerAgenda).Rezzed);
        Assert.Contains(Actions(state), action => action.Kind == ActionKind.AdvanceCard
            && action.Parameters is AdvanceCardParameters advance && advance.CardId == Card(state, CorpFillerAgenda));
        // EL.8: the Runner sees it, marked faceup and inactive.
        string runnerView = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner, CatalogFor(state)));
        Assert.Contains(CorpFillerAgenda, runnerView);
        Assert.Contains("\"faceUpInactive\":true", runnerView);
    }

    [Fact]
    [Trait("ElevationCard", "35068")]
    public void BangunPunishesAccessingAFaceupAgenda()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 5 }
            .Add("35068", Place.CorpIdentity)
            .Add(OffworldOffice, Place.Root, faceUp: true)
            .Begin();
        state = RoundTrip(ToAction(StartRun(state, ElevationBoard.Remote)));

        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(5 - 2, ZoneCount(state, ZoneKind.RunnerGrip));
        Assert.Equal(ZoneKind.RunnerScore, ZoneOf(state, OffworldOffice));
    }

    [Fact]
    [Trait("ElevationCard", "35046")]
    public void AuCoGetsAPowerCounterWhenItDoesDamage()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 5 }
            .Add("35046", Place.CorpIdentity)
            .Add(Karuna, Place.Ice, rezzed: true)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        for (int step = 0; step < 40 && state.PendingDecision?.Kind != DecisionKind.TurnAction; step++)
        {
            state = state.PendingDecision?.Kind == DecisionKind.IceSubroutineChoice
                ? Apply(state, Actions(state).First(static action => action.Parameters is IceSubroutineChoiceParameters
                { Choice: IceSubroutineChoiceKind.ContinueRun or IceSubroutineChoiceKind.Decline })).State
                : Pass(state);
        }

        state = RoundTrip(state);
        Assert.True(Counter(state, "35046", "power") >= 1);
    }

    [Fact]
    [Trait("ElevationCard", "35046")]
    public void AuCoTrashesOneOfTheTopThreeAndTakesTheRest()
    {
        GameState built = new ElevationBoard { CorpDeckFiller = 5 }
            .Add("35046", Place.CorpIdentity, counters: new Dictionary<string, int> { ["power"] = 2 })
            .Build();
        GameState state = UseTrigger(PassToTrigger(GameEngine.BeginTurn(built, PlayerSide.Corp, Behavior, Runtime)));
        CardInstanceId top = state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDeck).CardIds[0];
        state = RoundTrip(ChooseCards(state, top));

        Assert.Equal(0, Counter(state, "35046", "power"));
        Assert.Contains(top, state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDiscard).CardIds);
        // Two to HQ, then the mandatory draw.
        Assert.Equal(2 + 1, ZoneCount(state, ZoneKind.CorpHand));
        Assert.Equal(5 - 3 - 1, ZoneCount(state, ZoneKind.CorpDeck));
    }

    [Fact]
    [Trait("ElevationCard", "35055")]
    public void PeerReviewGainsSevenAndInstallsFromHq()
    {
        GameState state = new ElevationBoard { HqFiller = 2, CorpCredits = 4 }
            .Add("35055", Place.Hq)
            .Add("35062", Place.Hq)
            .Begin();
        state = PlayFirst(state, "35055");
        state = ChooseCards(state, Card(state, "35062"));
        state = Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters
        {
            Choice: { CreateNewRemote: true } choice,
        } && choice.CardId == Card(state, "35062")).State;
        state = RoundTrip(ToAction(state));

        Assert.Equal(4 - 4 + 7, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, "35062"));
    }

    [Fact]
    [Trait("ElevationCard", "35067")]
    public void TouchUpsAdvancesThenShufflesRevealedProgramsIntoTheStack()
    {
        GameState state = new ElevationBoard { GripFiller = 1, StackFiller = 2 }
            .Add("35067", Place.Hq)
            .Add(CorpFillerAgenda, Place.Root)
            .Add(Cleaver, Place.Grip)
            .Add("35008", Place.Grip)
            .Begin();
        state = PlayFirst(state, "35067");
        state = ChooseCards(state, Card(state, CorpFillerAgenda));
        state = ChooseOption(state, "program");
        // EL.5 review: the Corp chooses among the revealed grip cards it can see; the unrevealed filler isn't offered.
        string corpView = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp, CatalogFor(state)).PrivateState);
        Assert.Contains(Cleaver, corpView);
        Assert.Contains("35008", corpView);
        state = ChooseCards(state, Card(state, Cleaver), Card(state, "35008"));
        state = RoundTrip(ToAction(state));

        Assert.Equal(2, CardState(state, CorpFillerAgenda).AdvancementCounters);
        Assert.Equal(ZoneKind.RunnerStack, ZoneOf(state, Cleaver));
        Assert.Equal(ZoneKind.RunnerStack, ZoneOf(state, "35008"));
        Assert.Equal(1, ZoneCount(state, ZoneKind.RunnerGrip));
        Assert.Equal(3 - 2, Player(state, PlayerSide.Corp).Clicks);
    }

    [Fact]
    [Trait("ElevationCard", "35066")]
    public void IpEnforcementTakesBackAStolenAgendaAndAdvancesIt()
    {
        GameState state = new ElevationBoard { Tags = 3, RunnerPoints = 2 }
            .Add("35066", Place.Hq)
            .Add(OffworldOffice, Place.RunnerScore)
            .Begin();
        state = PlayFirst(state, "35066");
        state = ChooseOption(state, "x-2");
        state = Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters
        {
            Choice: { CreateNewRemote: true } choice,
        } && choice.CardId == Card(state, OffworldOffice)).State;
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, OffworldOffice));
        Assert.Equal(1, CardState(state, OffworldOffice).AdvancementCounters);
        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(0, Player(state, PlayerSide.Runner).AgendaPoints);
    }

    [Fact]
    [Trait("ElevationCard", "35073")]
    public void PlutusRevealsAndTrashesThreeHqCardsToRez()
    {
        GameState built = new ElevationBoard { HqFiller = 3 }
            .Add("35073", Place.Root)
            .Build();
        GameState state = GameEngine.BeginTurn(built, PlayerSide.Corp, Behavior, Runtime);
        state = Apply(PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.RezCard) == true), static action => action.Kind == ActionKind.RezCard).State;
        state = ChooseOption(state, "reveal-and-trash");
        CardInstanceId[] hq = state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpHand).CardIds.Take(3).ToArray();
        state = RoundTrip(ChooseCards(state, hq));

        Assert.True(CardState(state, "35073").Rezzed);
        Assert.All(hq, cardId => Assert.Equal(ZoneKind.CorpDiscard,
            state.Zones.Single(zone => zone.CardIds.Contains(cardId)).Kind));
    }

    [Fact]
    [Trait("ElevationCard", "35073")]
    public void PlutusCannotBeRezzedWithoutAnAlternativeCost()
    {
        GameState built = new ElevationBoard { HqFiller = 1 }
            .Add("35073", Place.Root)
            .Build();
        GameState state = PassUntil(GameEngine.BeginTurn(built, PlayerSide.Corp, Behavior, Runtime),
            static value => value.PendingDecision?.Kind == DecisionKind.TurnAction
                || value.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.RezCard) == true);

        Assert.DoesNotContain(Actions(state), static action => action.Kind == ActionKind.RezCard);
    }

    [Fact]
    [Trait("ElevationCard", "35073")]
    public void PlutusPlaysATransactionFromArchivesAndRemovesIt()
    {
        GameState built = new ElevationBoard()
            .Add("35073", Place.Root, rezzed: true)
            .Add(HedgeFund, Place.ArchivesFaceUp)
            .Build();
        GameState state = UseTrigger(PassToTrigger(GameEngine.BeginTurn(built, PlayerSide.Corp, Behavior, Runtime)));
        state = RoundTrip(ToAction(ChooseCards(state, Card(state, HedgeFund))));

        Assert.Equal(ZoneKind.CorpRemovedFromGame, ZoneOf(state, HedgeFund));
        Assert.Equal(5 - 5 + 9, Player(state, PlayerSide.Corp).Credits);
    }

    private const string SmartwareDistributor = "30033";
    private const string CreativeCommission = "30020";

    private static GameState InstallFirst(GameState state, string code) =>
        Apply(state, action => action.Kind == ActionKind.InstallCard
            && action.Parameters is InstallCardParameters install && install.CardId == Card(state, code)).State;

    private static GameState BlingWithFreeInstall(string hostedCode)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10, StackFiller = 2 }
            .Add("35006", Place.Rig)
            .Add(SmartwareDistributor, Place.Grip)
            .Add(hostedCode, Place.Stack)
            .Begin();
        state = InstallFirst(state, SmartwareDistributor);
        Assert.True(HasTrigger(state));
        return RoundTrip(ToAction(UseTrigger(state)));
    }

    [Fact]
    [Trait("ElevationCard", "35006")]
    public void BlingHostsTheTopStackCardFaceupAfterAFreeInstallAndItInstallsAsIfFromTheGrip()
    {
        GameState state = BlingWithFreeInstall(Cleaver);

        CardInstanceState hosted = CardState(state, Cleaver);
        Assert.Equal(ZoneKind.RunnerHostedFaceUp, ZoneOf(state, Cleaver));
        Assert.True(hosted.FaceUp);
        Assert.Equal(Card(state, "35006"), hosted.HostCardId);
        // A hosted card is public: the Corp sees it.
        Assert.Contains(Cleaver, EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp, CatalogFor(state))));

        state = RoundTrip(ToAction(InstallFirst(state, Cleaver)));
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, Cleaver));
        Assert.Null(CardState(state, Cleaver).HostCardId);
        Assert.Equal(10 - 3, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(4 - 2, Player(state, PlayerSide.Runner).Clicks);
    }

    [Fact]
    [Trait("ElevationCard", "35006")]
    public void BlingHostedEventsArePlayedAsIfFromTheGrip()
    {
        GameState state = BlingWithFreeInstall(CreativeCommission);
        state = RoundTrip(ToAction(PlayFirst(state, CreativeCommission)));

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, CreativeCommission));
        Assert.Equal(10 - 1 + 5, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35006")]
    public void BlingHostedRunEventsArePlayedAsIfFromTheGrip()
    {
        // EL.4 review: a hosted run event's source had to be in the grip.
        const string Jailbreak = "30028";
        GameState state = BlingWithFreeInstall(Jailbreak);
        state = RoundTrip(PlayFirst(state, Jailbreak));

        Assert.NotNull(state.RunState);
        Assert.Equal(ZoneKind.RunnerPlayArea, ZoneOf(state, Jailbreak));
        Assert.Null(CardState(state, Jailbreak).HostCardId);
    }

    [Fact]
    [Trait("ElevationCard", "35006")]
    public void BlingDoesNotTriggerOnAPaidInstall()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10, StackFiller = 2 }
            .Add("35006", Place.Rig)
            .Add(Cleaver, Place.Grip)
            .Begin();
        state = InstallFirst(state, Cleaver);

        Assert.False(HasTrigger(state));
    }

    [Fact]
    [Trait("ElevationCard", "35006")]
    public void BlingTrashesItsHostedCardsWhenTheDiscardPhaseEnds()
    {
        GameState state = BlingWithFreeInstall(Cleaver);
        state = PassUntil(SpendRemainingClicks(state), static value => value.Turn.ActiveSide == PlayerSide.Corp);

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, Cleaver));
        Assert.Null(CardState(state, Cleaver).HostCardId);
    }

    [Fact]
    [Trait("ElevationCard", "35028")]
    public void MadaniHostsProgramsAndInstallsOneOncePerTurn()
    {
        const string Marjanah = "30016";
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add("35028", Place.Rig)
            .Add(Cleaver, Place.Grip)
            .Add(Marjanah, Place.Grip)
            .Add(SmartwareDistributor, Place.Grip)
            .Begin();
        state = UseAbilityOf(state, "35028");
        state = RoundTrip(ChooseCards(state, Card(state, Cleaver), Card(state, Marjanah)));
        Assert.Equal(ZoneKind.RunnerHostedFaceUp, ZoneOf(state, Cleaver));
        Assert.Equal(ZoneKind.RunnerHostedFaceUp, ZoneOf(state, Marjanah));
        Assert.Equal(ZoneKind.RunnerGrip, ZoneOf(state, SmartwareDistributor));
        // Madani's hosted cards are not playable as if in the grip.
        Assert.DoesNotContain(Actions(ToAction(state)), action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, Cleaver));

        // The install is a paid ability: it waits for a paid-ability window.
        state = PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(IsMadaniInstall) == true);
        state = Apply(state, IsMadaniInstall).State;
        state = RoundTrip(Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, Cleaver)).State);

        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, Cleaver));
        Assert.Equal(10 - 3, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(4 - 1, Player(state, PlayerSide.Runner).Clicks);
        Assert.DoesNotContain(Actions(state), IsMadaniInstall);
        state = ToAction(state);
        Assert.Equal(ZoneKind.RunnerHostedFaceUp, ZoneOf(state, Marjanah));
    }

    private static bool IsMadaniInstall(LegalAction action) =>
        action.Parameters is AbilityActionParameters { AbilityKey: "madani-install" };

    [Fact]
    [Trait("ElevationCard", "35006")]
    public void BlingWithNothingHostedEndsTheTurnCleanly()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add("35006", Place.Rig)
            .Begin();
        state = RoundTrip(PassUntil(SpendRemainingClicks(state), static value => value.Turn.ActiveSide == PlayerSide.Corp));

        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, "35006"));
    }

    [Fact]
    [Trait("ElevationCard", "35028")]
    public void MadaniInstallsAHostedProgramDuringARun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add("35028", Place.Rig)
            .Add(Cleaver, Place.Grip)
            .Begin();
        state = UseAbilityOf(state, "35028");
        state = ToAction(ChooseCards(state, Card(state, Cleaver)));
        state = RunTo(state, 0, static value => value.PendingDecision?.LegalActions.Any(IsMadaniInstall) == true);
        Assert.NotNull(state.RunState);
        state = Apply(state, IsMadaniInstall).State;
        state = RoundTrip(Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters { Choice: { } choice }
            && choice.CardId == Card(state, Cleaver)).State);

        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, Cleaver));
        Assert.NotNull(state.RunState);
        Assert.Equal(10 - 3, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35066")]
    public void IpEnforcementCanInstallOverACardInAnExistingRemote()
    {
        const string Regolith = "30071";
        GameState state = new ElevationBoard { Tags = 3, RunnerPoints = 2 }
            .Add("35066", Place.Hq)
            .Add(OffworldOffice, Place.RunnerScore)
            .Add(Regolith, Place.Root)
            .Begin();
        state = PlayFirst(state, "35066");
        state = ChooseOption(state, "x-2");
        state = Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters
        {
            Choice: { CreateNewRemote: false, ServerId: not null } choice,
        } && choice.CardId == Card(state, OffworldOffice)).State;
        for (int step = 0; step < 4 && state.PendingDecision?.Kind == DecisionKind.InstallChoice; step++)
        {
            state = RoundTrip(Apply(state, static action => action.Parameters is InstallCardParameters install
                && (install.FinalizeSelection || install.SelectedReplacementCardIds.Count > 0)).State);
        }

        state = RoundTrip(ToAction(state));
        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, OffworldOffice));
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, Regolith));
        Assert.Equal(1, CardState(state, OffworldOffice).AdvancementCounters);
    }

    [Fact]
    [Trait("ElevationCard", "35066")]
    public void IpEnforcementWithNoAgendaOfValueXInstallsNothing()
    {
        // Ladder regression: the advance looked for a binding the empty install never made.
        GameState state = new ElevationBoard { Tags = 3, RunnerPoints = 2 }
            .Add("35066", Place.Hq)
            .Add(OffworldOffice, Place.RunnerScore)
            .Begin();
        state = PlayFirst(state, "35066");
        state = ChooseOption(state, "x-1");
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.RunnerScore, ZoneOf(state, OffworldOffice));
        Assert.Equal(2, Rules(state, PlayerSide.Runner).Tags);
    }

    [Fact]
    [Trait("ElevationCard", "35046")]
    public void AuCoTakesWhatIsLeftOfAShortRAndD()
    {
        // Ladder regression: adding the top 2 cards to HQ found none.
        GameState built = new ElevationBoard { CorpDeckFiller = 1 }
            .Add("35046", Place.CorpIdentity, counters: new Dictionary<string, int> { ["power"] = 2 })
            .Build();
        GameState state = UseTrigger(PassToTrigger(GameEngine.BeginTurn(built, PlayerSide.Corp, Behavior, Runtime)));
        CardInstanceId top = state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDeck).CardIds[0];
        state = RoundTrip(ChooseCards(state, top));

        Assert.Equal(ZoneKind.CorpDiscard, state.Zones.Single(zone => zone.CardIds.Contains(top)).Kind);
    }

    [Fact]
    [Trait("ElevationCard", "35039")]
    public void HumanoidResourcesNestedDrawFromAnEmptyRAndDEndsTheGame()
    {
        // Ladder regression: a nested operation's deck-out expected an ordinary play.
        const string Sprint = "30041";
        GameState state = new ElevationBoard { CorpDeckFiller = 3 }
            .Add("35039", Place.Root, rezzed: true)
            .Add(Sprint, Place.Hq)
            .Begin();
        state = UseAbilityOf(state, "35039");
        for (int step = 0; step < 20 && state.Outcome.Kind != GameOutcomeKind.Won; step++)
        {
            LegalAction? play = state.PendingDecision?.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.PlayCard
                || action.Parameters is BehaviorChoiceParameters { CardIds.Count: > 0 } choice && choice.CardIds.Contains(Card(state, Sprint)));
            state = play is not null
                ? Apply(state, play).State
                : state.PendingDecision?.LegalActions.FirstOrDefault(static action => action.Parameters is BehaviorInstallChoiceParameters { Choice: null }) is { } skip
                    ? Apply(state, skip).State
                    : Pass(state);
        }

        Assert.Equal(GameOutcomeKind.Won, state.Outcome.Kind);
        Assert.Equal(PlayerSide.Runner, state.Outcome.Winner);
        RoundTrip(state);
    }
}
