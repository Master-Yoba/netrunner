using Netrunner.Core;

using Xunit;

using static Netrunner.Cards.Tests.Elevation.ElevationHarness;

namespace Netrunner.Cards.Tests.Elevation;

public sealed class ElevationBatch1HostedCreditTests
{
    private static Dictionary<string, int> Declared(int credits) => new() { ["declared-credit"] = credits };

    private static bool CanTrashAccessed(GameState state) =>
        state.PendingDecision?.LegalActions.Any(static action => action.Kind == ActionKind.TrashAccessedCard) == true;

    private static GameState RunToAccess(GameState state, int server, string code) =>
        RunTo(state, server, value => value.RunState?.ChosenAccessCards.Count > 0
            && value.RunState.ChosenAccessCards[^1] == Card(value, code)
            && value.RunState.Step == RunTimingStep.BreachAccessAbility);

    [Fact]
    [Trait("ElevationCard", "35029")]
    public void AzimatLoadsTwoRecurringCreditsThatPayATrashCost()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 1 }
            .Add("35029", Place.Grip)
            .Add("35062", Place.Root)
            .Begin();
        state = ToAction(Apply(state, static action => action.Kind == ActionKind.InstallCard).State);
        Assert.Equal(2, Counter(state, "35029", "credit"));
        Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);

        state = RunToAccess(state, ElevationBoard.Remote, "35062");
        Assert.True(CanTrashAccessed(state));
        state = RoundTrip(ToAction(Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State));

        Assert.Equal(ZoneKind.CorpDiscard, ZoneOf(state, "35062"));
        Assert.Equal(0, Counter(state, "35029", "credit"));
    }

    [Fact]
    [Trait("ElevationCard", "35029")]
    public void AzimatRefillsBeforeTheRunnerTurnBegins()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35029", Place.Rig)
            .Begin();

        Assert.Equal(2, Counter(state, "35029", "credit"));
    }

    [Fact]
    [Trait("ElevationCard", "35022")]
    public void OpenMarketPaysForJobInstallsAndDripsACreditEachTurn()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 2 }
            .Add("35022", Place.Grip)
            .Add("35034", Place.Grip)
            .Begin();
        state = ToAction(Apply(state, action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, "35022")).State);
        Assert.Equal(6, Counter(state, "35022", "credit"));
        Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);

        state = ToAction(Apply(state, action => action.Parameters is InstallCardParameters install
            && install.CardId == Card(state, "35034")).State);
        state = RoundTrip(state);

        Assert.Equal(ZoneKind.RunnerRig, ZoneOf(state, "35034"));
        Assert.Equal(4, Counter(state, "35022", "credit"));
        Assert.Equal(0, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35022")]
    public void OpenMarketPaysForAUniqueConnectionThatReplacesItsCopy()
    {
        // EL.2 review regression: the final install action needed the wallet alone, so a replacing install
        // that hosted credits pay for had no legal continuation.
        const string Knickknack = "35033";
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 0 }
            .Add("35022", Place.Rig, counters: Declared(6))
            .Add(Knickknack, Place.Rig)
            .Add(Knickknack, Place.Grip)
            .Begin();
        Assert.Equal(1, Player(state, PlayerSide.Runner).Credits);
        CardInstanceId incoming = state.GetZone(ZoneKind.RunnerGrip).CardIds.Single();
        for (int step = 0; step < 4 && state.GetCard(incoming).ZoneId == state.GetZone(ZoneKind.RunnerGrip).Id; step++)
        {
            LegalAction install = Actions(state).Where(action => action.Parameters is InstallCardParameters parameters
                    && parameters.CardId == incoming)
                .OrderByDescending(action => ((InstallCardParameters)action.Parameters).FinalizeSelection)
                .First();
            state = Apply(state, install).State;
        }
        state = RoundTrip(state);

        Assert.Equal(ZoneKind.RunnerRig, state.GetZone(state.GetCard(incoming).ZoneId).Kind);
        Assert.Equal(1, state.GetZone(ZoneKind.RunnerRig).CardIds.Count(id => state.GetCard(id).DefinitionId.Value == Knickknack));
    }

    [Fact]
    [Trait("ElevationCard", "35022")]
    public void OpenMarketIsTrashedWhenEmpty()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner)
            .Add("35022", Place.Rig, counters: Declared(1))
            .Begin();

        Assert.Equal(ZoneKind.RunnerHeap, ZoneOf(state, "35022"));
        Assert.Equal(5 + 1, Player(state, PlayerSide.Runner).Credits);
    }

    [Fact]
    [Trait("ElevationCard", "35082")]
    public void MahkotaPaysToRezAnAssetInItsRoot()
    {
        GameState built = new ElevationBoard { CorpCredits = 0 }
            .Add("35082", Place.Root, rezzed: true)
            .Add("35062", Place.Root)
            .Build();
        GameState state = GameEngine.BeginTurn(built, PlayerSide.Corp, Behavior, Runtime);
        Assert.Equal(2, Counter(state, "35082", "credit"));
        state = PassUntil(state, static value => value.PendingDecision?.LegalActions.Any(
            static action => action.Kind == ActionKind.RezCard) == true);
        state = RoundTrip(Apply(state, static action => action.Kind == ActionKind.RezCard).State);

        Assert.True(CardState(state, "35062").Rezzed);
        Assert.Equal(1, Counter(state, "35082", "credit"));
    }

    [Theory]
    [Trait("ElevationCard", "35082")]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void MahkotaRaisesTheTrashCostOfAssetsInItsRoot(int credits, bool canTrash)
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = credits }
            .Add("35082", Place.Root, rezzed: true)
            .Add("35062", Place.Root)
            .Begin();
        state = RunToAccess(state, ElevationBoard.Remote, "35062");

        Assert.Equal(canTrash, CanTrashAccessed(state));
        RoundTrip(state);
    }

    [Fact]
    [Trait("ElevationCard", "35082")]
    public void MahkotaTrashCostIncreasePersistsAfterItIsTrashed()
    {
        GameState state = new ElevationBoard(PlayerSide.Runner) { RunnerCredits = 5 }
            .Add("35082", Place.Root, rezzed: true)
            .Add("35062", Place.Root)
            .Begin();
        state = RunToAccess(state, ElevationBoard.Remote, "35082");
        state = Apply(state, static action => action.Kind == ActionKind.TrashAccessedCard).State;
        Assert.Equal(3, Player(state, PlayerSide.Runner).Credits);
        state = PassUntil(state, value => value.RunState?.ChosenAccessCards.Count > 0
            && value.RunState.ChosenAccessCards[^1] == Card(value, "35062")
            && value.RunState.Step == RunTimingStep.BreachAccessAbility);

        Assert.False(CanTrashAccessed(state));
        RoundTrip(state);
    }
}
