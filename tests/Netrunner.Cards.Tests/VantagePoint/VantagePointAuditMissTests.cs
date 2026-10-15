using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>
/// VP.2c, the primitives binding VP.3 found missing from the audit, on the probe catalog's test cards (see
/// <see cref="ElevationHarness.VantagePointProbe"/>): VP-BAD-PUBLICITY's condition, VP-ENCOUNTER-SUBTYPE,
/// VP-ALLOTTED-CLICKS, VP-RUN-EVENT-REMOVED, VP-RUN-PROTECTED-SERVER, VP-FIRST-EVENT, VP-HOSTED-SERVER,
/// VP-INSTALL-FACEUP and VP-REZ-SERVER-KIND.
/// </summary>
public sealed class VantagePointAuditMissTests
{
    private const string Diviner = "30046";

    private static bool Playable(GameState state, string code) =>
        Actions(state).Any(action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, code));

    private static bool CanUse(GameState state, string code) =>
        Actions(state).Any(action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, code));

    private static RezCardParameters? RezOffer(GameState state, string code) =>
        state.PendingDecision?.LegalActions.Select(static action => action.Parameters).OfType<RezCardParameters>()
            .FirstOrDefault(rez => rez.CardId == Card(state, code));

    private static GameState RunServer(GameState state, int server) =>
        Apply(state, action => action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, server)).State;

    [Fact]
    public void ACardCanRequireBadPublicity()
    {
        Assert.False(Playable(new ElevationBoard { CorpCredits = 0 }
            .Add(ProbeBadPublicityOperation, Place.Hq)
            .Begin(catalog: VantagePointProbe), ProbeBadPublicityOperation));
        Assert.True(Playable(new ElevationBoard { CorpCredits = 0, BadPublicity = 1 }
            .Add(ProbeBadPublicityOperation, Place.Hq)
            .Begin(catalog: VantagePointProbe), ProbeBadPublicityOperation));
    }

    [Theory]
    [InlineData(CorpFillerIce, true)]
    [InlineData(Diviner, false)]
    public void AnAbilityCanRequireEncounteringABarrier(string ice, bool usable)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 2 }
            .Add(ProbeBarrierResource, Place.Rig)
            .Add(ice, Place.Ice, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, ElevationBoard.Remote, InEncounter);

        Assert.True(InEncounter(state));
        Assert.Equal(usable, CanUse(state, ProbeBarrierResource));
    }

    // On the Corp's first turn the Runner has had no turn: the active side's facts hold the grant.
    [Fact]
    public void ARunnerCanGetAClickFewerNextTurn()
    {
        GameState state = new ElevationBoard { CorpCredits = 0 }
            .Add(ProbeCaveatOperation, Place.Hq)
            .Begin(catalog: VantagePointProbe);
        state = SpendRemainingClicks(ToAction(PlayFirst(state, ProbeCaveatOperation)));
        state = RoundTrip(PassUntil(state, static value => value.Turn.ActiveSide == PlayerSide.Runner
            && value.PendingDecision?.Kind == DecisionKind.TurnAction));

        Assert.Equal(3, Player(state, PlayerSide.Runner).Clicks);
    }

    [Fact]
    public void ARunEventCanNeedAProtectedServerAndLeaveTheGame()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 0, HqFiller = 1 }
            .Add(ProbeDiveEvent, Place.Grip)
            .Add(CorpFillerIce, Place.Ice, server: ElevationBoard.Hq)
            .Begin(catalog: VantagePointProbe);
        ServerId[] targets = Actions(state).Select(static action => action.Parameters).OfType<PlayCardParameters>()
            .Select(static play => play.TargetServerId!.Value).ToArray();
        Assert.Equal([ElevationBoard.ServerId(state, ElevationBoard.Hq)], targets);

        state = RoundTrip(ToAction(Apply(state, static action => action.Parameters is PlayCardParameters).State));
        Assert.Equal(ZoneKind.RunnerRemovedFromGame, ZoneOf(state, ProbeDiveEvent));
    }

    [Fact]
    public void TheFirstEventEachTurnIsAnOccurrence()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 1 }
            .Add(ProbeTouchstoneResource, Place.Rig)
            .Add(RunnerFillerResource, Place.Rig)
            .Add(RunnerFillerResource, Place.Rig)
            .Add(ProbeSellOutEvent, Place.Grip)
            .Add(ProbeSellOutEvent, Place.Grip)
            .Begin(catalog: VantagePointProbe);
        GameState PlayTrashingAFiller(GameState value) => ToAction(Apply(value, Actions(value).First(action => action.Parameters
            is PlayCardParameters { TrashCostCardId: { } trashed } && value.GetCard(trashed).DefinitionId.Value == RunnerFillerResource)).State);

        // Each play costs 0 and gains 4; only the first also gains the probe's credit.
        state = PlayTrashingAFiller(state);
        Assert.Equal(1 + 4 + 1, Player(state, PlayerSide.Runner).Credits);
        state = RoundTrip(PlayTrashingAFiller(state));
        Assert.Equal(1 + 4 + 1 + 4, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    public void ACardHostedOnIceIsInItsHostsServer()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0, CorpCredits = 0, HqFiller = 1 }
            .Add(ProbeStowawayProgram, Place.Grip)
            .Add(CorpFillerIce, Place.Ice)
            .Begin(catalog: VantagePointProbe);
        CardInstanceId ice = Card(state, CorpFillerIce);
        state = ToAction(Apply(state, action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, ProbeStowawayProgram) && install.HostCardId == ice).State);
        Assert.Equal(ice, CardState(state, ProbeStowawayProgram).HostCardId);

        state = ToAction(RunServer(state, ElevationBoard.Hq));
        Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);
        state = RoundTrip(ToAction(RunServer(state, ElevationBoard.Remote)));
        Assert.Equal(2, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    public void AnAgendaCanBeInstalledFaceUpWithItsAbilitiesActive()
    {
        GameState state = new ElevationBoard { CorpCredits = 1 }
            .Add(ProbePublicAgenda, Place.Hq)
            .Begin(catalog: VantagePointProbe);
        state = ToAction(Apply(state, action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, ProbePublicAgenda) && install.CreateNewRemote).State);
        Assert.True(CardState(state, ProbePublicAgenda).FaceUp);
        Assert.False(CardState(state, ProbePublicAgenda).Rezzed);

        state = RoundTrip(ToAction(Apply(state, static action => action.Kind == ActionKind.AdvanceCard).State));
        Assert.Equal(1, CardState(state, ProbePublicAgenda).AdvancementCounters);
        Assert.Equal(1 - 1 + 1, Player(state, PlayerSide.Corp).Credits);
    }

    [Theory]
    [InlineData(ElevationBoard.Hq, 1)]
    [InlineData(ElevationBoard.Remote, 0)]
    public void ARezTriggerCanNameItsServersKind(int server, int badPublicity)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpCredits = 0, HqFiller = 1 }
            .Add(ProbeGrubberIce, Place.Ice, server: server)
            .Begin(catalog: VantagePointProbe);
        state = RunTo(state, server, value => RezOffer(value, ProbeGrubberIce) is not null);
        state = RoundTrip(ToAction(Apply(state, action => action.Parameters == RezOffer(state, ProbeGrubberIce)).State));

        Assert.True(CardState(state, ProbeGrubberIce).Rezzed);
        Assert.Equal(badPublicity, state.RulesState?.BadPublicity ?? 0);
    }
}
