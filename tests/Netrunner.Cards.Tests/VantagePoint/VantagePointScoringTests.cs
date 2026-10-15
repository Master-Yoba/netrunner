using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>
/// VP-SCORE-STEAL-COST, VP-CARD-AS-AGENDA, VP-AGENDA-POINTS-MODIFIER, VP-SCORING-FACTS and VP-ADVANCED, on the probe
/// catalog's test cards (see <see cref="ElevationHarness.VantagePointProbe"/>).
/// </summary>
public sealed class VantagePointScoringTests
{
    private static GameState RunRemoteToAccess(GameState state) =>
        PassUntil(Apply(state, action => action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, ElevationBoard.Remote)).State,
            static value => value.RunState?.Step == RunTimingStep.BreachAccessAbility);

    private static bool Steals(LegalAction action) => action.Kind == ActionKind.StealAgenda;

    private static GameState Score(GameState state) =>
        Apply(PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.ScoreAgenda) == true),
            static action => action.Kind == ActionKind.ScoreAgenda).State;

    private static GameState BeginWithoutPassing(ElevationBoard board)
    {
        GameState built = board.Build(catalog: VantagePointProbe);
        return GameEngine.BeginTurn(built, board.Active, CatalogFor(built), Runtime);
    }

    private static GameState Advance(GameState state) =>
        Apply(state, static action => action.Kind is ActionKind.AdvanceCard or ActionKind.AdvanceAgenda).State;

    [Fact]
    public void AnAgendaWithStealCostsIsStolenOnlyByPayingThem()
    {
        // The agenda's own click and the rezzed filler asset's 2 credits.
        ElevationBoard board = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add(CorpFillerAgenda, Place.Root)
            .Add(CorpFillerAsset, Place.Root, server: ElevationBoard.Remote + 1, rezzed: true);
        GameState access = RunRemoteToAccess(board.Begin(catalog: VantagePointProbe));
        Assert.Contains(Actions(access), Steals);

        // No action declines the costs and leaves the agenda where it is.
        GameState declined = ToAction(Apply(access, static action => action.Kind == ActionKind.NoAccessAction).State);
        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(declined, CorpFillerAgenda));
        Assert.Equal(0, Player(declined, PlayerSide.Runner).AgendaPoints);

        int clicks = Player(access, PlayerSide.Runner).Clicks;
        GameState stolen = RoundTrip(ToAction(Apply(access, Actions(access).First(Steals)).State));
        Assert.Equal(ZoneKind.RunnerScore, ZoneOf(stolen, CorpFillerAgenda));
        // Worth 1 in the Runner's score area, not its printed 2.
        Assert.Equal(1, Player(stolen, PlayerSide.Runner).AgendaPoints);
        Assert.Equal(5 - 2, Player(stolen, PlayerSide.Runner).Credits);
        Assert.Equal(clicks - 1, Player(stolen, PlayerSide.Runner).Clicks);
    }

    [Fact]
    public void WithoutTheCostsInHandTheAgendaCantBeStolen()
    {
        GameState access = RunRemoteToAccess(new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 1 }
            .Add(CorpFillerAgenda, Place.Root)
            .Add(CorpFillerAsset, Place.Root, server: ElevationBoard.Remote + 1, rezzed: true)
            .Begin(catalog: VantagePointProbe));

        Assert.DoesNotContain(Actions(access), Steals);
    }

    [Fact]
    public void ScoringAnAgendaInstalledThisTurnPaysTheScoreCost()
    {
        // The Corp installs the filler agenda (3 advancements) this turn and scores it: the Runner's filler resource goes
        // to the Corp's score area as a -1 agenda that can't be forfeited.
        // Nanomanagement's two clicks make room for the install and three advances.
        GameState state = new ElevationBoard { CorpCredits = 10 }
            .Add("35043", Place.Hq)
            .Add(CorpFillerAgenda, Place.Hq)
            .Add(RunnerFillerResource, Place.Rig)
            .Begin(catalog: VantagePointProbe);
        state = ToAction(PlayFirst(state, "35043"));
        state = ToAction(Apply(state, action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, CorpFillerAgenda) && install.CreateNewRemote).State);
        state = ToAction(Advance(state));
        state = ToAction(Advance(state));
        state = RoundTrip(Score(Advance(state)));

        Assert.Equal(ZoneKind.CorpScore, ZoneOf(state, RunnerFillerResource));
        Assert.Equal(2 - 1, Player(state, PlayerSide.Corp).AgendaPoints);
        ScoreAreaAgenda converted = Assert.Single(state.RulesState!.ScoredAsAgendas!);
        Assert.Equal((-1, true), (converted.Points, converted.CannotForfeit));
    }

    [Fact]
    public void ScoringAnEarlierAgendaPaysNoScoreCostAndOpensTheProbeOperation()
    {
        GameState state = BeginWithoutPassing(new ElevationBoard()
            .Add(CorpFillerAgenda, Place.Root, advancement: 3)
            .Add(RunnerFillerResource, Place.Rig)
            .Add(CorpProbeOperation, Place.Hq));
        static bool PlaysProbe(GameState value) => Actions(value).Any(action => action.Parameters is PlayCardParameters play
            && play.CardId == Card(value, CorpProbeOperation));
        Assert.False(PlaysProbe(ToAction(state)));

        state = ToAction(Score(state));
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, RunnerFillerResource));
        Assert.Equal(2, Player(state, PlayerSide.Corp).AgendaPoints);
        Assert.True(PlaysProbe(state));

        // Played, the operation joins the Corp's score area as a 2-point agenda that may be forfeited.
        state = RoundTrip(ToAction(Apply(state, action => action.Parameters is PlayCardParameters play
            && play.CardId == Card(state, CorpProbeOperation)).State));
        Assert.Equal(ZoneKind.CorpScore, ZoneOf(state, CorpProbeOperation));
        Assert.Equal(4, Player(state, PlayerSide.Corp).AgendaPoints);
        Assert.False(Assert.Single(state.RulesState!.ScoredAsAgendas!).CannotForfeit);
    }

    [Fact]
    public void AdvancingIsAnOccurrence()
    {
        GameState state = new ElevationBoard { CorpClicks = 3, CorpCredits = 5 }
            .Add(CorpFillerAgenda, Place.Root)
            .Add(CorpProbeUpgrade, Place.Root, server: ElevationBoard.Remote + 1, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = ToAction(Apply(state, static action => action.Kind is ActionKind.AdvanceCard or ActionKind.AdvanceAgenda).State);
        Assert.Equal(5 - 1 + 1, Player(state, PlayerSide.Corp).Credits);
        state = ToAction(Apply(state, static action => action.Kind is ActionKind.AdvanceCard or ActionKind.AdvanceAgenda).State);

        // Once per turn: the second advance gains nothing.
        Assert.Equal(5 - 2 + 1, Player(state, PlayerSide.Corp).Credits);
    }
}
