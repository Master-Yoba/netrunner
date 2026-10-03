using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.Elevation;

public sealed class ElevationBatch1CorpTests
{
    private const string HedgeFund = "30075";

    private static GameState Score(GameState state) =>
        Apply(PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.ScoreAgenda) == true),
            static action => action.Kind == ActionKind.ScoreAgenda).State;

    private static GameState BeginWithoutPassing(ElevationBoard board, BehaviorCatalog? catalog = null)
    {
        GameState built = board.Build(catalog: catalog);
        return GameEngine.BeginTurn(built, board.Active, CatalogFor(built), Runtime);
    }

    private static int GripCount(GameState state) =>
        state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerGrip).CardIds.Count;

    private static int HqCount(GameState state) =>
        state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpHand).CardIds.Count;

    [Fact]
    [Trait("ElevationCard", "35070")]
    public void GreenmailGainsTwoWhenScored()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard()
            .Add("35070", Place.Root, advancement: 2));
        state = ToAction(Score(state));

        Assert.Equal(ZoneKind.CorpScore, ZoneOf(state, "35070"));
        Assert.Equal(5 + 2, Player(state, PlayerSide.Corp).Credits);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35070")]
    public void GreenmailGainsFourWhenForfeited()
    {
        GameState state = new ElevationBoard { CorpPoints = 1 }
            .Add("35070", Place.CorpScore)
            .Add(CorpFiller, Place.Hq)
            .Begin(catalog: Probe);
        state = PlayFirst(state, CorpFiller);
        state = ChooseCards(state, Card(state, "35070"));
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.CorpRemovedFromGame, ZoneOf(state, "35070"));
        Assert.Equal(0, Player(state, PlayerSide.Corp).AgendaPoints);
        Assert.Equal(5 + 4, Player(state, PlayerSide.Corp).Credits);
    }

    [Theory]
    [Trait("ElevationCard", "35037")]
    [InlineData(true)]
    [InlineData(false)]
    public void AggressiveTrendsettingMakesTheRunnerSpendAClickOrGivesTheCorpOne(bool spendClick)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35037", Place.CorpScore)
            .Add("35062", Place.Root)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.TrashAccessedCard) == true);
        state = Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State;
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        Assert.Equal(PlayerSide.Runner, state.Players.Single(player => player.Id == state.PendingDecision!.Owner).Side);
        state = ChooseOption(state, spendClick ? "spend-click" : "decline");
        state = RoundTrip(ToAction(state));

        Assert.Equal(spendClick ? 2 : 3, Player(state, PlayerSide.Runner).Clicks);
        Assert.Equal(spendClick ? 0 : 1, state.Turn.History!.CorpThisTurn!.NextTurnExtraClicks);
    }

    [Fact]
    [Trait("ElevationCard", "35037")]
    public void AggressiveTrendsettingIgnoresALaterTrashOfACardThatWasNotInstalled()
    {
        // EL.7 ladder regression: a later trash from HQ fired it again mid-access
        // and broke the access frame.
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35037", Place.CorpScore)
            .Add("35062", Place.Root)
            .Add("35056", Place.Hq)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.TrashAccessedCard) == true);
        state = Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State;
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        state = ChooseOption(state, "decline");
        state = ToAction(state);
        state = RunTo(state, ElevationBoard.Hq, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.TrashAccessedCard) == true);
        state = Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State;
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35056"));
        Assert.Equal(1, state.Turn.History!.CorpThisTurn!.NextTurnExtraClicks);
        Assert.Equal(2, Player(state, PlayerSide.Runner).Clicks);
    }

    [Fact]
    [Trait("ElevationCard", "35037")]
    public void AggressiveTrendsettingFiresOnceWhenARunTrashesTwoInstalledCards()
    {
        // EL.7 ladder regression: a second trash in the same breach queued an
        // occurrence no trigger took, and the access frame lost its prompt.
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 9 }
            .Add("35037", Place.CorpScore)
            .Add("35062", Place.Root)
            .Add("35082", Place.Root)
            .Begin();
        for (int trashed = 0; trashed < 2; trashed++)
        {
            state = trashed == 0
                ? RunTo(state, ElevationBoard.Remote, static value => value.PendingDecision?.LegalActions.Any(
                    static action => action.Kind is ActionKind.TrashAccessedCard or ActionKind.ChooseBreachCandidate) == true)
                : PassUntil(state, static value => value.RunState is null || value.PendingDecision?.LegalActions.Any(
                    static action => action.Kind is ActionKind.TrashAccessedCard or ActionKind.ChooseBreachCandidate) == true);
            if (state.PendingDecision!.LegalActions.FirstOrDefault(static action => action.Kind == ActionKind.ChooseBreachCandidate) is { } candidate)
                state = PassUntil(Apply(state, action => action.Id == candidate.Id).State,
                    static value => value.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.TrashAccessedCard) == true);
            state = Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State;
            if (trashed == 0)
            {
                state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
                state = ChooseOption(state, "decline");
            }
        }
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35062"));
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35082"));
        Assert.Equal(1, state.Turn.History!.CorpThisTurn!.NextTurnExtraClicks);
    }

    [Fact]
    [Trait("ElevationCard", "35037")]
    public void AggressiveTrendsettingsExtraClickIsAllottedOnTheCorpsNextTurn()
    {
        // Ladder regression: the turn-facts validator demanded exactly 3 allotted clicks.
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpDeckFiller = 3 }
            .Add("35037", Place.CorpScore)
            .Add("35062", Place.Root)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.TrashAccessedCard) == true);
        state = Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State;
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        state = ChooseOption(state, "decline");
        state = PassUntil(SpendRemainingClicks(ToAction(state)), static value => value.Turn.ActiveSide == PlayerSide.Corp
            && value.PendingDecision?.Kind == DecisionKind.TurnAction);
        state = RoundTrip(state);

        Assert.Equal(4, state.Turn.History!.CorpThisTurn!.ClicksAllotted);
    }

    [Fact]
    [Trait("ElevationCard", "35057")]
    public void NebulaFlipsAfterAnOperationTurnAndItsBackGainsAClickOnTheFirstOperation()
    {
        GameState state = new ElevationBoard()
            .Add("35057", Place.CorpIdentity)
            .Add(HedgeFund, Place.Hq)
            .Begin();
        state = PlayFirst(state, HedgeFund);
        state = RoundTrip(SpendRemainingClicks(ToAction(state)));

        Assert.Equal(5 - 5 + 9 + 2 + 1, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(1, Counter(state, "35057", "flipped"));

        GameState flipped = new ElevationBoard()
            .Add("35057", Place.CorpIdentity, counters: new Dictionary<string, int> { ["flipped"] = 1 })
            .Add(HedgeFund, Place.Hq)
            .Begin();
        flipped = PlayFirst(flipped, HedgeFund);
        flipped = RoundTrip(ToAction(flipped));
        Assert.Equal(3, Player(flipped, PlayerSide.Corp).Clicks);
    }

    [Fact]
    [Trait("ElevationCard", "35057")]
    public void NebulaBackFlipsWhenTheRunnerRunsOnHq()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add("35057", Place.CorpIdentity, counters: new Dictionary<string, int> { ["flipped"] = 1 })
            .Begin();
        state = RunTo(state, ElevationBoard.Hq, static value => false);
        state = RoundTrip(ToAction(state));

        Assert.Equal(0, Counter(state, "35057", "flipped"));
    }

    [Fact]
    [Trait("ElevationCard", "35051")]
    public void PhatGioanRemovesPowerCountersForExtraNetDamageOnTheFirstScore()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard { GripFiller = 5 }
            .Add("35051", Place.Root, server: ElevationBoard.Remote, rezzed: true,
                counters: new Dictionary<string, int> { ["power"] = 2 })
            .Add(CorpFillerAgenda, Place.Root, server: ElevationBoard.Remote + 1, advancement: 3));
        state = Score(state);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        state = ChooseOption(state, "remove-two");
        state = RoundTrip(ToAction(state));

        Assert.Equal(0, Counter(state, "35051", "power"));
        Assert.Equal(5 - 3, GripCount(state));
    }

    [Fact]
    [Trait("ElevationCard", "35051")]
    public void PhatGioanGainsAPowerCounterWhenTheDiscardPhaseEnds()
    {
        GameState state = new ElevationBoard()
            .Add("35051", Place.Root, rezzed: true)
            .Begin();
        state = RoundTrip(SpendRemainingClicks(state));

        Assert.Equal(1, Counter(state, "35051", "power"));
    }

    [Fact]
    [Trait("ElevationCard", "35061")]
    public void IdiosyncresisTrashesItselfForThreeCreditsAndTwoRunnerCreditsPerCounter()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard()
            .Add("35061", Place.Root, rezzed: true, advancement: 2));
        state = UseTrigger(PassToTrigger(state));
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35061"));
        Assert.Equal(5 + 6, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(5 - 4, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35047")]
    public void PtUntaianPaysToAdvanceAnUnrezzedCardThatCannotBeScoredThisTurn()
    {
        GameState state = new ElevationBoard { HqFiller = 2 }
            .Add("35047", Place.CorpIdentity)
            .Add(CorpFillerAgenda, Place.Root, advancement: 2)
            .Begin();
        state = UseTrigger(SpendRemainingClicks(state));
        state = ChooseOption(state, "pay");
        state = ChooseCards(state, Card(state, CorpFillerAgenda));
        state = RoundTrip(state);

        Assert.Equal(3, CardState(state, CorpFillerAgenda).AdvancementCounters);
        Assert.Equal(5 + 3 - 1, Player(state, PlayerSide.Corp).Credits);
        Assert.Contains(state.Turn.History!.CorpThisTurn!.UsedAbilityKeys, key => key.SourceCardId == Card(state, CorpFillerAgenda) && key.AbilityKey == "card-score-prohibited");
    }

    [Fact]
    [Trait("ElevationCard", "35047")]
    public void PtUntaianDoesNotTriggerWithMoreThanThreeCardsInHq()
    {
        GameState state = new ElevationBoard { HqFiller = 4 }
            .Add("35047", Place.CorpIdentity)
            .Add(CorpFillerAgenda, Place.Root, advancement: 2)
            .Begin();
        state = SpendRemainingClicks(state);

        Assert.False(HasTrigger(state));
    }

    [Fact]
    [Trait("ElevationCard", "35049")]
    public void SericultureGetsDividendsAndSpendsThemOnTwoAdvancementCounters()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard()
            .Add("35049", Place.Root, advancement: 4)
            .Add(CorpFillerAsset, Place.Root, server: ElevationBoard.Remote + 1));
        state = Score(state);
        state = ToAction(state);
        Assert.Equal(1, Counter(state, "35049", "agenda"));

        state = UseTrigger(SpendRemainingClicks(state));
        state = ChooseCards(state, Card(state, CorpFillerAsset));
        state = RoundTrip(state);

        Assert.Equal(0, Counter(state, "35049", "agenda"));
        Assert.Equal(2, CardState(state, CorpFillerAsset).AdvancementCounters);
    }

    [Fact]
    [Trait("ElevationCard", "35058")]
    public void SynapseGlobalRemovesATagForTwoCreditsThenInstallsFromHqForFree()
    {
        GameState state = new ElevationBoard { Tags = 1 }
            .Add("35058", Place.CorpIdentity)
            .Add(CorpFillerAsset, Place.Hq)
            .Begin();
        state = Apply(state, action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, "35058")).State;
        state = UseTrigger(PassToTrigger(state));
        ApplyResult installed = Apply(state, static action => action.Parameters is BehaviorInstallChoiceParameters { Choice: not null });
        // EL.5 review: "reveal and install": the installed card is revealed.
        Assert.Contains(installed.Events, engineEvent => engineEvent.Payload is BehaviorCardRevealedEventPayload revealed
            && revealed.CardId == Card(state, CorpFillerAsset));
        state = RoundTrip(ToAction(installed.State));

        Assert.Equal(0, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(5 + 2, Player(state, PlayerSide.Corp).Credits);
        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, CorpFillerAsset));
    }

    [Fact]
    [Trait("ElevationCard", "35069")]
    public void TheZwickyGroupDrawsAfterTheFirstOperationCreditGain()
    {
        GameState state = new ElevationBoard()
            .Add("35069", Place.CorpIdentity)
            .Add(HedgeFund, Place.Hq)
            .Begin();
        int hq = HqCount(state);
        state = PlayFirst(state, HedgeFund);
        state = UseTrigger(PassToTrigger(state));
        state = RoundTrip(ToAction(state));

        Assert.Equal(hq - 1 + 1, HqCount(state));
    }

    [Theory]
    [Trait("ElevationCard", "35062")]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    public void PublicAccessPlazaTagsTheRunnerWhoTrashesItAtThreatTwo(int corpPoints, int tags)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpPoints = corpPoints }
            .Add("35062", Place.Root, rezzed: true)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.TrashAccessedCard) == true);
        state = Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State;
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35062"));
        Assert.Equal(tags, Rules(state, PlayerSide.Runner).Tags);
    }

    [Fact]
    [Trait("ElevationCard", "35062")]
    public void PublicAccessPlazaGainsACreditWhenTheTurnBegins()
    {
        GameState state = new ElevationBoard()
            .Add("35062", Place.Root, rezzed: true)
            .Begin();

        Assert.Equal(5 + 1, Player(state, PlayerSide.Corp).Credits);
    }

    private static CardInstanceId[] DeckOf(GameState state) =>
        state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpDeck).CardIds.ToArray();

    [Fact]
    [Trait("ElevationCard", "35060")]
    public void NextBigThingGetsACounterWhenScored()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard()
            .Add("35060", Place.Root, advancement: 5));
        state = RoundTrip(ToAction(Score(state)));

        Assert.Equal(1, Counter(state, "35060", "agenda"));
    }

    [Theory]
    [Trait("ElevationCard", "35060")]
    [InlineData(false)]
    [InlineData(true)]
    public void NextBigThingDrawsFourFromEitherScoreArea(bool stolen)
    {
        GameState state = new ElevationBoard { CorpDeckFiller = 6 }
            .Add("35060", stolen ? Place.RunnerScore : Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 1 })
            .Begin();
        int hq = HqCount(state);
        state = Apply(state, action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, "35060")).State;
        CardInstanceId shuffled = state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpHand).CardIds[0];
        state = RoundTrip(ToAction(ChooseCards(state, shuffled)));

        Assert.Equal(0, Counter(state, "35060", "agenda"));
        Assert.Equal(hq + 4 - 1, HqCount(state));
        Assert.Contains(shuffled, DeckOf(state));
    }

    [Fact]
    [Trait("ElevationCard", "35059")]
    public void EmbeddedReportingPutsAnOperationOnTopOfRd()
    {
        GameState state = new ElevationBoard { CorpDeckFiller = 4 }
            .Add("35059", Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 1 })
            .Add(CorpFiller, Place.CorpDeck)
            .Add(HedgeFund, Place.CorpDeck)
            .Begin();
        state = UseTrigger(SpendRemainingClicks(state));
        state = ChooseCards(state, Card(state, HedgeFund));
        state = RoundTrip(state);

        Assert.Equal(0, Counter(state, "35059", "agenda"));
        Assert.Equal(Card(state, HedgeFund), DeckOf(state)[0]);
    }

    [Fact]
    [Trait("ElevationCard", "35059")]
    public void EmbeddedReportingGetsTwoCountersPerExcessAdvancement()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard()
            .Add("35059", Place.Root, advancement: 4));
        state = RoundTrip(ToAction(Score(state)));

        Assert.Equal(2, Counter(state, "35059", "agenda"));
    }

    [Fact]
    [Trait("ElevationCard", "35071")]
    public void OffTheBooksInstallsACardFromRdIgnoringAllCosts()
    {
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add("35071", Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 1 })
            .Add(CorpFiller, Place.CorpDeck)
            .Add("35062", Place.CorpDeck)
            .Begin();
        state = UseTrigger(SpendRemainingClicks(state));
        state = ChooseOption(state, "install");
        state = Apply(state, action => action.Parameters is BehaviorInstallChoiceParameters
        {
            Choice: { CreateNewRemote: true } choice,
        } && choice.CardId == Card(state, "35062")).State;
        state = RoundTrip(state);

        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, "35062"));
        Assert.Equal(3, Player(state, PlayerSide.Corp).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35071")]
    public void OffTheBooksCanAddTheSearchedCardToHq()
    {
        GameState state = new ElevationBoard()
            .Add("35071", Place.CorpScore, counters: new Dictionary<string, int> { ["agenda"] = 1 })
            .Add(CorpFiller, Place.CorpDeck)
            .Add("35062", Place.CorpDeck)
            .Begin();
        state = UseTrigger(SpendRemainingClicks(state));
        state = ChooseOption(state, "hq");
        state = ChooseCards(state, Card(state, "35062"));
        state = RoundTrip(state);

        Assert.Equal(ZoneKind.CorpHand, ZoneOf(state, "35062"));
    }
}
