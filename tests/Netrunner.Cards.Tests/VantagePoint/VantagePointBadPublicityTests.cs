using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>
/// VP-BAD-PUBLICITY (CR 26.03 10.6): the Corp's bad publicity counters, and the run's bad publicity fund. The probe catalog's
/// filler operation takes 2 bad publicity; no registered card uses the primitive yet.
/// </summary>
public sealed class VantagePointBadPublicityTests
{
    private static int BadPublicity(GameState state) => state.RulesState!.BadPublicity;

    [Fact]
    public void TakingBadPublicityPutsPublicCountersOnTheCorp()
    {
        GameState state = new ElevationBoard { HqFiller = 1 }.Begin(catalog: VantagePointProbe);
        ApplyResult played = Apply(state, Actions(state).First(static action => action.Kind == ActionKind.PlayCard));
        state = RoundTrip(played.State);

        Assert.Equal(2, BadPublicity(state));
        BadPublicityAdjustedEventPayload taken = Assert.Single(played.Events.Select(static value => value.Payload).OfType<BadPublicityAdjustedEventPayload>());
        Assert.Equal((2, 2), (taken.Delta, taken.BadPublicityAfter));
        // Public to both sides (CR 1.9.5d).
        string runnerView = EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Runner, VantagePointProbe));
        Assert.Contains("\"badPublicity\":2", runnerView, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheFirstBadPublicityEachTurnIsAnOccurrence()
    {
        GameState state = new ElevationBoard { HqFiller = 2, CorpClicks = 3 }
            .Add(CorpFillerAsset, Place.Root, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        int credits = Player(state, PlayerSide.Corp).Credits;

        state = ToAction(Apply(state, Actions(state).First(static action => action.Kind == ActionKind.PlayCard)).State);
        Assert.Equal(credits + 1, Player(state, PlayerSide.Corp).Credits);
        state = ToAction(Apply(state, Actions(state).First(static action => action.Kind == ActionKind.PlayCard)).State);

        Assert.Equal(4, BadPublicity(state));
        Assert.Equal(credits + 1, Player(state, PlayerSide.Corp).Credits);
    }

    [Fact]
    public void AStateWithoutBadPublicityKeepsItsBytes()
    {
        GameState none = new ElevationBoard().Build();
        GameState some = new ElevationBoard { BadPublicity = 1 }.Build();

        Assert.DoesNotContain("badPublicity", EngineJson.Serialize(none), StringComparison.Ordinal);
        Assert.Contains("\"badPublicity\":1", EngineJson.Serialize(some), StringComparison.Ordinal);
        Assert.Equal(1, BadPublicity(RoundTrip(some)));
    }

    [Fact]
    public void TheFundHoldsACreditPerBadPublicityAndPaysBeforeThePool()
    {
        // A remote with Nico Campaign (trash cost 2) and no ICE: the fund pays the trash cost, the pool is untouched.
        GameState state = new ElevationBoard(PlayerSide.Runner) { BadPublicity = 2, RunnerCredits = 0 }
            .Add("30037", Place.Root)
            .Begin();
        state = Apply(state, action => action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, ElevationBoard.Remote)).State;

        Assert.Equal(2, state.RunState!.RunCredits);
        Assert.Equal(2, state.RunState.BadPublicityCredits);
        Assert.Contains("\"badPublicityCredits\":2",
            EngineJson.Serialize(ObservationBuilder.Build(state, PlayerSide.Corp, Behavior)), StringComparison.Ordinal);

        state = PassUntil(state, value => value.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.TrashAccessedCard) == true);
        state = RoundTrip(ToAction(Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "30037"));
        Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);
        Assert.Null(state.RunState);
        Assert.Equal(2, BadPublicity(state));
    }

    [Fact]
    public void UnspentFundCreditsReturnToTheBankWhenTheRunEnds()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { BadPublicity = 3 }.Begin();
        int credits = Player(state, PlayerSide.Runner).Credits;
        state = Apply(state, action => action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, ElevationBoard.ArchivesServer)).State;
        Assert.Equal(3, state.RunState!.BadPublicityCredits);

        state = ToAction(state);

        Assert.Null(state.RunState);
        Assert.Equal(credits, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    public void ANewRunWithoutBadPublicityHasNoFund()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner).Begin();
        state = Apply(state, action => action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, ElevationBoard.ArchivesServer)).State;

        Assert.Equal(0, state.RunState!.RunCredits);
        Assert.DoesNotContain("badPublicityCredits", EngineJson.Serialize(state), StringComparison.Ordinal);
    }
}
