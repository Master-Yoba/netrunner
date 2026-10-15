using Netrunner.Cards.Tests.Elevation;
using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.VantagePoint;

/// <summary>
/// VP-STEALTH-CREDITS, VP-POOL-LOCK and VP-CREDITS-OUTSIDE-POOL (Vantage Point progress, VP.2's design): credits from
/// outside the Runner's pool during a run. The probe catalog's test cards stand in for the set's stealth cards and
/// Shackleton Grid.
/// </summary>
public sealed class VantagePointCreditSourceTests
{
    private const string NicoCampaign = "30037";

    private static Dictionary<string, int> RunCredits(int credits) => new() { ["run-credit"] = credits };

    private static bool CanTrashAccessed(GameState state) =>
        state.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.TrashAccessedCard) == true;

    private static GameState RunRemote(GameState state) =>
        Apply(state, action => action.Parameters is RunServerParameters run
            && run.ServerId == ElevationBoard.ServerId(state, ElevationBoard.Remote)).State;

    private static GameState ToTrash(GameState state) => PassUntil(state, CanTrashAccessed);

    private static int[] HostedSplits(GameState state) => Actions(state)
        .Where(static action => action.Kind == ActionKind.TrashAccessedCard)
        .Select(static action => IssuedRunPaymentPreference.RunCredits(action.Parameters))
        .Order()
        .ToArray();

    [Fact]
    public void RunSpendableHostedCreditsPayFromOutsideThePool()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner, turnNumber: 2) { RunnerCredits = 1 }
            .Add(StealthHardware, Place.Rig, counters: RunCredits(2))
            .Add(NicoCampaign, Place.Root)
            .Begin(catalog: VantagePointProbe);
        state = ToTrash(RunRemote(state));

        // A trash cost of 2: from the pool alone is impossible with 1 credit, so the outside share is 1 or 2.
        Assert.Equal([1, 2], HostedSplits(state));
        state = RoundTrip(ToAction(Apply(state, Actions(state).Single(static action =>
            action.Kind == ActionKind.TrashAccessedCard && IssuedRunPaymentPreference.RunCredits(action.Parameters) == 2)).State));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, NicoCampaign));
        Assert.Equal(0, Counter(state, StealthHardware, "credit"));
        Assert.Equal(1, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    public void SpendingCreditsFromOutsideThePoolIsAnOccurrence()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add(StealthHardware, Place.Rig, counters: RunCredits(2))
            .Add(NicoCampaign, Place.Root)
            .Add(CorpProbeUpgrade, Place.Root, rezzed: true)
            .Begin(catalog: VantagePointProbe);
        int corpCredits = Player(state, PlayerSide.Corp).Credits;
        state = ToTrash(RunRemote(state));
        GameState fromPool = ToAction(Apply(state, Actions(state).First(action =>
            action.Kind == ActionKind.TrashAccessedCard && IssuedRunPaymentPreference.RunCredits(action.Parameters) == 0
            && action.Parameters is RunCardPaymentParameters trash && trash.CardId == Card(state, NicoCampaign))).State);
        GameState fromOutside = ToAction(Apply(state, Actions(state).First(action =>
            action.Kind == ActionKind.TrashAccessedCard && IssuedRunPaymentPreference.RunCredits(action.Parameters) > 0
            && action.Parameters is RunCardPaymentParameters trash && trash.CardId == Card(state, NicoCampaign))).State);

        Assert.Equal(corpCredits, Player(fromPool, PlayerSide.Corp).Credits);
        Assert.Equal(corpCredits + 1, Player(fromOutside, PlayerSide.Corp).Credits);
    }

    [Fact]
    public void AStealthOnlyCostSpendsStealthCardsCreditsAndNothingElse()
    {
        static bool Offered(GameState state) => Actions(state).Any(static action => action.Parameters is AbilityActionParameters ability
            && ability.AbilityKey == "probe-stealth-access");

        // Pool credits and the bad publicity fund don't pay it.
        GameState poor = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5, BadPublicity = 2 }
            .Add(StealthHardware, Place.Rig)
            .Add(NicoCampaign, Place.Root)
            .Begin(catalog: VantagePointProbe);
        Assert.False(Offered(ToTrash(RunRemote(poor))));

        // The stealth hardware's own run-spendable credits do.
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5, StackFiller = 5 }
            .Add(StealthHardware, Place.Rig, counters: RunCredits(1))
            .Add(NicoCampaign, Place.Root)
            .Begin(catalog: VantagePointProbe);
        state = ToTrash(RunRemote(state));
        Assert.True(Offered(state));
        state = RoundTrip(Apply(state, static action => action.Parameters is AbilityActionParameters ability
            && ability.AbilityKey == "probe-stealth-access").State);

        Assert.Equal(0, Counter(state, StealthHardware, "credit"));
        Assert.Equal(5, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    public void ALockedPoolNeitherPaysNorLoses()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add(StealthRunEvent, Place.Grip)
            .Add(NicoCampaign, Place.Root)
            .Begin(catalog: VantagePointProbe);
        state = Apply(state, action => action.Parameters is PlayCardParameters play && play.CardId == Card(state, StealthRunEvent)
            && play.TargetServerId == ElevationBoard.ServerId(state, ElevationBoard.Remote)).State;
        Assert.Equal(2, state.RunState!.RunCredits);
        state = ToTrash(state);

        // Only the event's 2 credits may pay: no split with the pool is offered.
        Assert.Equal([2], HostedSplits(state));
        state = ToAction(Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State);

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, NicoCampaign));
        Assert.Equal(5, Player(state, PlayerSide.Runner).Credits);
    }
}
