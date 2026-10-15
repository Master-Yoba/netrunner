using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>
/// VP.2b, costs and values: VP-TRASH-AS-COST, VP-DYNAMIC-REZ-COST, VP-PLAY-COST-MODIFIER, VP-CLICK-COST-MODIFIER, VP-AMOUNT, VP-RUN-FACTS and
/// VP-UNINSTALL-INTERRUPT, on the probe catalog's test cards (see <see cref="ElevationHarness.VantagePointProbe"/>).
/// </summary>
public sealed class VantagePointCostTests
{
    private const string NicoCampaign = "30037";
    private const string Hantu = "35008";

    private static GameState BeginWithoutPassing(ElevationBoard board)
    {
        GameState built = board.Build(catalog: VantagePointProbe);
        return GameEngine.BeginTurn(built, board.Active, CatalogFor(built), Runtime);
    }

    private static GameState RunServer(GameState state, int server) =>
        Apply(state, action => action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, server)).State;

    private static RezCardParameters? RezOffer(GameState state, string code) =>
        state.PendingDecision?.LegalActions.Select(static action => action.Parameters).OfType<RezCardParameters>()
            .FirstOrDefault(rez => rez.CardId == Card(state, code));

    [Fact]
    public void AnIceRezCostFallsForEachOtherUnrezzedIce()
    {
        // Two other unrezzed ICE: the probe Reverb's printed 4 costs 2. The Corp rezzes it on approach.
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 2 }
            .Add(ProbeReverbIce, Place.Ice)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.Hq)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.Rd)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, value => RezOffer(value, ProbeReverbIce) is not null);
        RezCardParameters offer = RezOffer(state, ProbeReverbIce)!;

        Assert.Equal(2, offer.CreditCost);
        state = RoundTrip(Apply(state, action => action.Parameters == offer).State);
        Assert.True(CardState(state, ProbeReverbIce).Rezzed);
        Assert.Equal(0, Player(state, PlayerSide.Corp).Credits);
    }

    [Fact]
    public void AnUpgradeRezCostFallsOnTheTurnAnAgendaWasScored()
    {
        // The rez window opens at the turn's start: before the score the upgrade costs its printed 3.
        GameState state = BeginWithoutPassing(new ElevationBoard { CorpCredits = 1 }
            .Add(CorpFillerAgenda, Place.Root, advancement: 3)
            .Add(ProbeHypeUpgrade, Place.Root, server: ElevationBoard.Remote + 1));
        state = PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.ScoreAgenda) == true);
        Assert.Null(RezOffer(state, ProbeHypeUpgrade));

        state = Apply(state, static action => action.Kind == ActionKind.ScoreAgenda).State;
        state = PassUntil(state, value => RezOffer(value, ProbeHypeUpgrade) is not null);
        RezCardParameters offer = RezOffer(state, ProbeHypeUpgrade)!;
        Assert.Equal(1, offer.CreditCost);
        state = RoundTrip(Apply(state, action => action.Parameters == offer).State);

        Assert.True(CardState(state, ProbeHypeUpgrade).Rezzed);
    }

    [Fact]
    public void ARunEventCostsLessForEachIceOnHq()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 1 }
            .Add(ProbeTailgateEvent, Place.Grip)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.Hq)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.Hq)
            .Begin(catalog: VantagePointProbe);
        LegalAction play = Actions(state).Single(action => action.Parameters is PlayCardParameters card
            && card.CardId == Card(state, ProbeTailgateEvent));

        Assert.Equal(1, Assert.IsType<PlayCardParameters>(play.Parameters).CreditCost);
        state = Apply(state, play).State;
        Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);
        Assert.NotNull(state.RunState);
    }

    [Fact]
    public void OnlyTheFirstDoubleOperationOfTheTurnCostsAClickLess()
    {
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add(ProbeDoubleDiscountAsset, Place.Root, rezzed: true)
            .Add(ProbeDoubleOperation, Place.Hq)
            .Add(ProbeDoubleOperation, Place.Hq)
            .Begin(catalog: VantagePointProbe);
        int clicks = Player(state, PlayerSide.Corp).Clicks;
        state = ToAction(Apply(state, Actions(state).First(static action => action.Kind == ActionKind.PlayCard)).State);
        // The play action's click only: the additional click is discounted.
        Assert.Equal(clicks - 1, Player(state, PlayerSide.Corp).Clicks);

        state = ToAction(Apply(state, Actions(state).First(static action => action.Kind == ActionKind.PlayCard)).State);
        Assert.Equal(clicks - 1 - 2, Player(state, PlayerSide.Corp).Clicks);
        Assert.Equal(6, Player(state, PlayerSide.Corp).Credits);
    }

    [Fact]
    public void AnAmountCanBeAChosenCardsPrintedRezCost()
    {
        // Filler ICE has a printed rez cost of 1; the probe Reverb's is 4.
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add(ProbeReallocOperation, Place.Hq)
            .Add(ProbeReverbIce, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = Apply(state, action => action.Parameters is PlayCardParameters play
            && play.CardId == Card(state, ProbeReallocOperation)).State;
        state = RoundTrip(ToAction(ChooseCards(state, Card(state, ProbeReverbIce))));

        Assert.Equal(4, Player(state, PlayerSide.Corp).Credits);
    }

    [Fact]
    public void AnAmountCanCountTheProtectedRemotesWithACardInTheirRoot()
    {
        // Two protected remotes with a root card, one unprotected, one protected but empty.
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add(ProbeFloodOperation, Place.Hq)
            .Add(NicoCampaign, Place.Root, server: ElevationBoard.Remote)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.Remote)
            .Add(NicoCampaign, Place.Root, server: ElevationBoard.Remote + 1)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.Remote + 1)
            .Add(NicoCampaign, Place.Root, server: ElevationBoard.Remote + 2)
            .Begin(catalog: VantagePointProbe);
        state = RoundTrip(ToAction(Apply(state, action => action.Parameters is PlayCardParameters play
            && play.CardId == Card(state, ProbeFloodOperation)).State));

        Assert.Equal(2, Player(state, PlayerSide.Corp).Credits);
    }

    [Fact]
    public void SuccessfulRunsOnEveryCentralAreATurnFact()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0, HqFiller = 1, CorpDeckFiller = 5 }
            .Add(ProbeCentralsEvent, Place.Grip)
            .Begin(catalog: VantagePointProbe);
        static bool Playable(GameState value) => Actions(value).Any(action => action.Parameters is PlayCardParameters play
            && play.CardId == Card(value, ProbeCentralsEvent));
        foreach (int central in new[] { ElevationBoard.Hq, ElevationBoard.Rd })
        {
            Assert.False(Playable(state));
            state = ToAction(RunServer(state, central));
        }

        Assert.False(Playable(state));
        state = ToAction(RunServer(state, ElevationBoard.ArchivesServer));
        Assert.True(Playable(state));
        state = ToAction(Apply(state, action => action.Parameters is PlayCardParameters play
            && play.CardId == Card(state, ProbeCentralsEvent)).State);
        Assert.Equal(5, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    public void RezzingIceIsATurnFact()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 5 }
            .Add(ProbeIceRezzedEvent, Place.Grip)
            .Add(CorpFillerIce, Place.Ice)
            .Begin(catalog: VantagePointProbe);
        static bool Playable(GameState value) => Actions(value).Any(action => action.Parameters is PlayCardParameters play
            && play.CardId == Card(value, ProbeIceRezzedEvent));
        Assert.False(Playable(state));

        state = RunTo(state, ElevationBoard.Remote, value => RezOffer(value, CorpFillerIce) is not null);
        state = ToAction(Apply(state, action => action.Parameters == RezOffer(state, CorpFillerIce)).State);

        Assert.True(Playable(state));
    }

    [Fact]
    public void AnUninstalledCardReturnsItsHostedBadPublicity()
    {
        // The Runner trashes the probe Luana (trash cost 3), hosting 2 bad publicity: the Corp takes them back.
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5, BadPublicity = 1 }
            .Add(ProbeLuanaAsset, Place.Root, rezzed: true, counters: new Dictionary<string, int> { [CardBehaviorDefinition.HostedBadPublicityCounter] = 2 })
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote,
            static value => value.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.TrashAccessedCard) == true);
        state = RoundTrip(ToAction(Apply(state, Actions(state).First(static action => action.Kind == ActionKind.TrashAccessedCard)).State));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, ProbeLuanaAsset));
        Assert.Equal(1 + 2, state.RulesState!.BadPublicity);
    }

    [Fact]
    public void APlayCanTrashAnInstalledCardAsACost()
    {
        // Without an installed resource the probe Sell Out can't be played; with two, it is offered once for each.
        Assert.DoesNotContain(new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0 }
            .Add(ProbeSellOutEvent, Place.Grip)
            .Begin(catalog: VantagePointProbe)
            .PendingDecision!.LegalActions, static action => action.Kind == ActionKind.PlayCard);

        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0 }
            .Add(ProbeSellOutEvent, Place.Grip)
            .Add(RunnerFillerResource, Place.Rig)
            .Add(RunnerFillerResource, Place.Rig)
            .Add(Hantu, Place.Rig)
            .Begin(catalog: VantagePointProbe);
        PlayCardParameters[] plays = Actions(state).Select(static action => action.Parameters).OfType<PlayCardParameters>().ToArray();
        CardInstanceId[] resources = state.Cards.Where(static card => card.DefinitionId.Value == RunnerFillerResource)
            .Select(static card => card.Id).ToArray();
        Assert.Equal(resources.ToHashSet(), plays.Select(static play => play.TrashCostCardId!.Value).ToHashSet());
        Assert.Equal(2, plays.Length);
        CardInstanceId resource = resources[0];

        state = RoundTrip(ToAction(Apply(state, action => action.Parameters is PlayCardParameters play
            && play.TrashCostCardId == resource).State));
        Assert.Equal(ZoneKind.RunnerHeap, state.Zones.Single(zone => zone.CardIds.Contains(resource)).Kind);
        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, ProbeSellOutEvent));
        Assert.Equal(4, Player(state, PlayerSide.Runner).Credits);
    }
}
