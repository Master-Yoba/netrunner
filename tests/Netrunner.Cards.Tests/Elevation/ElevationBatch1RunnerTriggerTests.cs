using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.Elevation;

public sealed class ElevationBatch1RunnerTriggerTests
{
    private const string OffworldOffice = "30067";

    private static int ZoneCount(GameState state, ZoneKind kind) =>
        state.Zones.Single(zone => zone.Kind == kind).CardIds.Count;

    private static GameState StartRun(GameState state, int server)
    {
        ServerId target = ElevationBoard.ServerId(state, server);
        return Apply(state, action => action.Kind == ActionKind.StartRun
            && action.Parameters is RunServerParameters run && run.ServerId == target).State;
    }

    private static GameState TrashAccessed(GameState state)
    {
        state = PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.TrashAccessedCard) == true);
        return Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State;
    }

    [Fact]
    [Trait("ElevationCard", "35010")]
    public void CacophonyGetsOnePowerCounterForTheFirstTrashEachTurn()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35010", Place.Rig)
            .Add("35062", Place.Root)
            .Add("35061", Place.Root, server: ElevationBoard.Remote + 1)
            .Begin();
        ServerId second = ElevationBoard.ServerId(state, ElevationBoard.Remote + 1);
        state = ToAction(TrashAccessed(StartRun(state, ElevationBoard.Remote)));
        state = ToAction(TrashAccessed(Apply(state, action => action.Kind == ActionKind.StartRun
            && action.Parameters is RunServerParameters run && run.ServerId == second).State));
        state = RoundTrip(state);

        Assert.Equal(1, Counter(state, "35010", "power"));
    }

    [Fact]
    [Trait("ElevationCard", "35010")]
    public void CacophonyGetsAPowerCounterForAStolenAgenda()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35010", Place.Rig)
            .Add(OffworldOffice, Place.Root)
            .Begin();
        state = RoundTrip(ToAction(StartRun(state, ElevationBoard.Remote)));

        Assert.Equal(ZoneKind.RunnerScore, ZoneOf(state, OffworldOffice));
        Assert.Equal(1, Counter(state, "35010", "power"));
    }

    [Fact]
    [Trait("ElevationCard", "35010")]
    public void CacophonySabotagesThreeWhenTheActionPhaseEnds()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 2, CorpDeckFiller = 5 }
            .Add("35010", Place.Rig, counters: new Dictionary<string, int> { ["power"] = 2 })
            .Begin();
        state = UseTrigger(SpendRemainingClicks(state));
        Assert.Equal(PlayerSide.Corp, state.Players.Single(player => player.Id == state.PendingDecision!.Owner).Side);
        CardInstanceId hqCard = state.Zones.Single(static zone => zone.Kind == ZoneKind.CorpHand).CardIds[0];
        state = RoundTrip(ChooseCards(state, hqCard));

        Assert.Equal(0, Counter(state, "35010", "power"));
        Assert.Equal(1, ZoneCount(state, ZoneKind.CorpHand));
        Assert.Equal(3, ZoneCount(state, ZoneKind.CorpDiscard));
        Assert.Equal(5 - 2, ZoneCount(state, ZoneKind.CorpDeck));
    }

    [Fact]
    [Trait("ElevationCard", "35023")]
    public void DewiFlipsAndGainsACreditAfterASuccessfulRunWithFullMemory()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1 }
            .Add("35023", Place.RunnerIdentity)
            .Add(RunnerFillerProgram, Place.Rig)
            .Add(RunnerFillerProgram, Place.Rig)
            .Add(RunnerFillerProgram, Place.Rig)
            .Add(RunnerFillerProgram, Place.Rig)
            .Begin();
        state = UseTrigger(PassToTrigger(StartRun(state, ElevationBoard.Hq)));
        state = RoundTrip(ToAction(state));

        Assert.Equal(1, Counter(state, "35023", "flipped"));
        Assert.Equal(5 + 1, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35023")]
    public void DewiBackFlipsAndDrawsAfterASuccessfulRunWithFreeMemory()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { HqFiller = 1, GripFiller = 0 }
            .Add("35023", Place.RunnerIdentity, counters: new Dictionary<string, int> { ["flipped"] = 1 })
            .Begin();
        state = UseTrigger(PassToTrigger(StartRun(state, ElevationBoard.Hq)));
        state = RoundTrip(ToAction(state));

        Assert.Equal(0, Counter(state, "35023", "flipped"));
        Assert.Equal(1, ZoneCount(state, ZoneKind.RunnerGrip));
    }

    [Fact]
    [Trait("ElevationCard", "35034")]
    public void SideHustleGainsACreditOnInstallAndWheneverARunBegins()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35034", Place.Grip)
            .Begin();
        state = ToAction(Apply(state, static action => action.Kind == ActionKind.InstallCard).State);
        Assert.Equal(1, Counter(state, "35034", "credit"));

        state = RoundTrip(ToAction(StartRun(state, ElevationBoard.Hq)));
        Assert.Equal(2, Counter(state, "35034", "credit"));
    }

    [Fact]
    [Trait("ElevationCard", "35034")]
    public void SideHustlePaysOutAtSixCredits()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 0 }
            .Add("35034", Place.Rig, counters: new Dictionary<string, int> { ["credit"] = 5 })
            .Begin();
        state = RoundTrip(ToAction(StartRun(state, ElevationBoard.Hq)));

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, "35034"));
        Assert.Equal(5 + 6, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(1, ZoneCount(state, ZoneKind.RunnerGrip));
    }

    [Fact]
    [Trait("ElevationCard", "35033")]
    public void KnickknackTrashesAnotherCardForItsInstallCostAndADrawOnTheFirstRun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 0 }
            .Add("35033", Place.Rig)
            .Add(RunnerFillerResource, Place.Rig)
            .Begin();
        state = UseTrigger(PassToTrigger(StartRun(state, ElevationBoard.Hq)));
        Assert.DoesNotContain(Actions(state), action => action.Parameters is BehaviorChoiceParameters choice
            && choice.CardIds.Contains(Card(state, "35033")));
        state = ChooseCards(state, Card(state, RunnerFillerResource));
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, RunnerFillerResource));
        Assert.Equal(5 + 1, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(1, ZoneCount(state, ZoneKind.RunnerGrip));

        state = PassUntil(StartRun(state, ElevationBoard.Hq), static value => HasTrigger(value) || value.RunState is null);
        Assert.False(HasTrigger(state));
    }

    [Fact]
    [Trait("ElevationCard", "35031")]
    public void DevadattaDroneGetsTwoCountersAndSpendsOneForAnExtraRdAccess()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { CorpDeckFiller = 5 }
            .Add("35031", Place.Grip)
            .Begin();
        state = ToAction(Apply(state, static action => action.Kind == ActionKind.InstallCard).State);
        Assert.Equal(2, Counter(state, "35031", "power"));

        state = UseTrigger(PassToTrigger(StartRun(state, ElevationBoard.Rd)));
        state = PassUntil(state, static value => value.RunState?.Step == RunTimingStep.BreachCandidateSelection);
        state = RoundTrip(state);

        Assert.Equal(1, Counter(state, "35031", "power"));
        Assert.Equal(2, state.RunState!.RandomAccessLimit);
    }
}
