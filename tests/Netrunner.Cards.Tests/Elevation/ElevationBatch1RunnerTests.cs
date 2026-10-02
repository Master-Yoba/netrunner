using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.Elevation;

public sealed class ElevationBatch1RunnerTests
{
    private static bool CanUse(GameState state, string code) =>
        state.PendingDecision?.LegalActions.Any(action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, code)) == true;

    [Fact]
    [Trait("ElevationCard", "35007")]
    public void GourmandTrashesItselfAndTheAccessedNonAgendaAndDraws()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35007", Place.Rig)
            .Add(ElevationHarness.CorpFillerAsset, Place.Root)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, value => CanUse(value, "35007"));
        Assert.True(CanUse(state, "35007"));
        state = Apply(state, action => action.Kind == ActionKind.UseAbility).State;
        state = RoundTrip(state);

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, ElevationHarness.CorpFillerAsset));
        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, "35007"));
        Assert.Single(state.GetZone(ZoneKind.RunnerGrip).CardIds);
    }

    [Fact]
    [Trait("ElevationCard", "35007")]
    public void GourmandCannotTrashAnAccessedAgenda()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35007", Place.Rig)
            .Add(ElevationHarness.CorpFillerAgenda, Place.Root)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, value => CanUse(value, "35007"));

        Assert.False(CanUse(state, "35007"));
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, "35007"));
    }

    [Fact]
    [Trait("ElevationCard", "35008")]
    public void HantuSpendsAVirusCounterForTwoStrengthAndBreaksASentry()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35008", Place.Rig, counters: new Dictionary<string, int> { ["virus"] = 2 })
            .Add("30047", Place.Ice, rezzed: true)
            .Begin();
        state = RunTo(state, ElevationBoard.Remote, InEncounter);
        Assert.False(CanBreak(state, "35008"));
        Assert.DoesNotContain(Actions(state), static action => action.Kind == ActionKind.PumpBreaker);

        state = Apply(state, action => action.Kind == ActionKind.UseAbility
            && action.Parameters is AbilityActionParameters use && use.SourceCardId == Card(state, "35008")).State;
        state = PassUntil(state, InEncounter);
        state = RoundTrip(state);

        Assert.Equal(1, Counter(state, "35008", "virus"));
        Assert.Equal(4, EncounterStrength.ProjectBreakerQuote(state, Behavior, Card(state, "35008"))!.EffectiveValue);
        Assert.True(CanBreak(state, "35008"));
    }

    [Fact]
    [Trait("ElevationCard", "35008")]
    public void HantuPlacesTwoVirusCountersWhenInstalled()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner).Add("35008", Place.Grip).Begin();
        state = Apply(state, action => action.Kind == ActionKind.InstallCard).State;
        state = ToAction(state);

        Assert.Equal(2, Counter(state, "35008", "virus"));
        Assert.Equal(2, Player(state, PlayerSide.Runner).Credits);
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35009")]
    public void RisingTideGainsStrengthForEachFracterInTheHeap()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35009", Place.Rig)
            .Add("30006", Place.Heap)
            .Add("30016", Place.Heap)
            .Add("30005", Place.Heap)
            .Add("30072", Place.Ice, server: ElevationBoard.Hq, rezzed: true)
            .Begin();
        state = RunTo(state, ElevationBoard.Hq, InEncounter);

        ValueQuote quote = EncounterStrength.ProjectBreakerQuote(state, Behavior, Card(state, "35009"))!;
        Assert.Equal(3, quote.EffectiveValue);
        Assert.Contains(quote.Terms, static term => term.ReasonKey == "rising-tide-heap-fracters" && term.Quantity == 2);
        Assert.True(CanBreak(state, "35009"));
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35032")]
    public void PrincipiaCostsOneLessForEachOtherInstalledIcebreaker()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35032", Place.Grip)
            .Add("30006", Place.Rig)
            .Add("30005", Place.Rig)
            .Begin();
        LegalAction install = Assert.Single(Actions(state), action => action.Kind == ActionKind.InstallCard
            && action.Parameters is InstallCardParameters parameters && parameters.CardId == Card(state, "35032"));
        state = Apply(state, install).State;
        state = ToAction(state);

        Assert.Equal(5 - 2, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, "35032"));
        RoundTrip(state);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 3)]
    [Trait("ElevationCard", "35020")]
    public void SangKancilPumpsForOneDuringARunEventRun(bool runEvent, int pumpCost)
    {
        ElevationBoard board = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 10 }
            .Add("35020", Place.Rig)
            .Add("30046", Place.Ice, server: ElevationBoard.Hq, rezzed: true);
        if (runEvent)
        {
            board.Add("35014", Place.Grip);
        }

        GameState state = board.Begin();
        ServerId hq = ElevationBoard.ServerId(state, ElevationBoard.Hq);
        state = runEvent
            ? Apply(state, action => action.Parameters is PlayCardParameters { TargetServerId: { } server } && server == hq).State
            : Apply(state, action => action.Kind == ActionKind.StartRun
                && action.Parameters is RunServerParameters run && run.ServerId == hq).State;
        state = PassUntil(state, InEncounter);
        int before = Player(state, PlayerSide.Runner).Credits;
        state = Apply(state, action => action.Kind == ActionKind.PumpBreaker).State;
        state = RoundTrip(state);

        Assert.Equal(before - pumpCost, Player(state, PlayerSide.Runner).Credits);
        Assert.Equal(4, EncounterStrength.ProjectBreakerQuote(state, Behavior, Card(state, "35020"))!.EffectiveValue);
    }
}
