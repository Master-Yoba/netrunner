using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.Elevation;

public sealed class ElevationBatch1IceTests
{
    private const string OffworldOffice = "30067";

    private static GameState Subroutine(GameState state, IceSubroutineChoiceKind choice) =>
        Apply(state, action => action.Parameters is IceSubroutineChoiceParameters parameters
            && parameters.Choice == choice).State;

    [Theory]
    [Trait("ElevationCard", "35063")]
    [InlineData(1, 5 - 3)]
    [InlineData(0, 5 - 1)]
    public void DoomscrollTagsThenDealsExtraDamageOnlyAtTwoTags(int tags, int grip)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { Tags = tags, GripFiller = 5 }
            .Add("35063", Place.Ice, rezzed: true)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = RoundTrip(PassUntil(state, static value => value.RunState is null
            || value.PendingDecision?.Kind == DecisionKind.TurnAction));
        state = ToAction(state);

        Assert.Equal(tags + 1, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(grip, state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerGrip).CardIds.Count);
    }

    [Fact]
    [Trait("ElevationCard", "35075")]
    public void KessleroidCannotBeTrashedByTheRunnerWhileRezzed()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 0, StackFiller = 0 }
            .Add(RunnerFiller, Place.Grip)
            .Add("35075", Place.Ice, rezzed: true)
            .Begin(catalog: Probe);
        state = Apply(state, static action => action.Kind == ActionKind.PlayCard).State;
        state = ChooseCards(state, Card(state, "35075"));
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.ServerIce, ZoneOf(state, "35075"));
    }

    [Fact]
    [Trait("ElevationCard", "35075")]
    public void KessleroidCanBeTrashedByTheRunnerWhileUnrezzed()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 0, StackFiller = 0 }
            .Add(RunnerFiller, Place.Grip)
            .Add("35075", Place.Ice)
            .Begin(catalog: Probe);
        state = Apply(state, static action => action.Kind == ActionKind.PlayCard).State;
        state = ChooseCards(state, Card(state, "35075"));
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35075"));
    }

    [Fact]
    [Trait("ElevationCard", "35075")]
    public void KessleroidEndsTheRun()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35075", Place.Ice, rezzed: true)
            .Add(CorpFillerAgenda, Place.Root)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);

        // The run ends before breach, so the agenda in the root is never stolen.
        state = RoundTrip(PassUntil(state, static value => value.RunState is null));
        Assert.Equal(ZoneKind.ServerRoot, ZoneOf(state, CorpFillerAgenda));
    }

    [Fact]
    [Trait("ElevationCard", "35080")]
    public void LamplighterEndsTheRunWhenTheRunnerTakesTheTag()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35080", Place.Ice, rezzed: true)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.IceSubroutineChoice);
        state = Subroutine(state, IceSubroutineChoiceKind.TakeTag);
        state = PassUntil(state, static value => value.RunState is null);
        state = RoundTrip(state);

        Assert.Equal(1, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(5, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35080")]
    public void LamplighterLetsAnUntaggedRunnerThroughAfterPaying()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35080", Place.Ice, rezzed: true)
            .Add(CorpFillerAsset, Place.Root)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.IceSubroutineChoice);
        state = Subroutine(state, IceSubroutineChoiceKind.PayCredits);
        state = PassUntil(state, static value => value.RunState is null || value.RunState.Step == RunTimingStep.BreachCandidateSelection
            || value.RunState.AccessedCards.Count > 0);

        Assert.NotNull(state.RunState);
        Assert.Equal(0, Rules(state, PlayerSide.Runner).Tags);
        Assert.Equal(2, Player(state, PlayerSide.Runner).Credits);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35080")]
    public void LamplighterIsTrashedWhenAnAgendaIsStolenFromItsServer()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35080", Place.Ice, rezzed: true)
            .Add(OffworldOffice, Place.Root)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.IceSubroutineChoice);
        state = Subroutine(state, IceSubroutineChoiceKind.PayCredits);
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.RunnerScore, ZoneOf(state, OffworldOffice));
        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35080"));
    }

    [Fact]
    [Trait("ElevationCard", "35080")]
    public void LamplighterIsTrashedWhenAnAgendaIsScoredFromItsServer()
    {
        GameState built = new ElevationBoard(PlayerSide.Corp)
            .Add("35080", Place.Ice, rezzed: true)
            .Add(CorpFillerAgenda, Place.Root, advancement: 3)
            .Add("35080", Place.Ice, server: ElevationBoard.Hq, rezzed: true)
            .Build();
        GameState state = PassUntil(GameEngine.BeginTurn(built, PlayerSide.Corp, Behavior, Runtime),
            static value => value.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.ScoreAgenda) == true);
        state = Apply(state, static action => action.Kind == ActionKind.ScoreAgenda).State;
        state = RoundTrip(ToAction(state));

        Assert.Equal(ZoneKind.CorpScore, ZoneOf(state, CorpFillerAgenda));
        CardInstanceState[] lamplighters = state.Cards.Where(static card => card.DefinitionId.Value == "35080").ToArray();
        Assert.Single(lamplighters, card => state.Zones.Single(zone => zone.Id == card.ZoneId).Kind == ZoneKind.CorpDiscard);
        Assert.Single(lamplighters, card => state.Zones.Single(zone => zone.Id == card.ZoneId).Kind == ZoneKind.ServerIce);
    }

    [Fact]
    [Trait("ElevationCard", "35076")]
    public void SyailendraSubroutinesAdvanceACardDrainAndDamage()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { GripFiller = 3 }
            .Add("35076", Place.Ice, rezzed: true)
            .Add(CorpFillerAsset, Place.Root, server: ElevationBoard.Remote + 1)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        state = PassUntil(state, static value => value.PendingDecision?.Kind == DecisionKind.BehaviorChoice);
        Assert.Equal(PlayerSide.Corp, state.Players.Single(player => player.Id == state.PendingDecision!.Owner).Side);
        state = ChooseCards(state, Card(state, "35076"));
        state = PassUntil(state, static value => value.RunState is null);
        state = RoundTrip(ToAction(state));

        Assert.Equal(1, CardState(state, "35076").AdvancementCounters);
        Assert.Equal(3, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(2, state.Zones.Single(static zone => zone.Kind == ZoneKind.RunnerGrip).CardIds.Count);
    }

    [Fact]
    [Trait("ElevationCard", "35076")]
    public void SyailendraWithThreeCountersOffersAnEncounterAdvance()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35076", Place.Ice, rezzed: true, advancement: 3)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, static value => value.PendingDecision?.Kind == DecisionKind.TriggerChoice);
        state = RoundTrip(state);
        state = Apply(state, static action => action.Kind == ActionKind.UseTrigger).State;
        state = ChooseCards(state, Card(state, "35076"));

        Assert.Equal(4, CardState(state, "35076").AdvancementCounters);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35076")]
    public void SyailendraWithFewerCountersDoesNotTriggerOnEncounter()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35076", Place.Ice, rezzed: true, advancement: 2)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, static value => InEncounter(value)
            || value.PendingDecision?.Kind == DecisionKind.TriggerChoice);

        Assert.True(InEncounter(state));
    }
}
