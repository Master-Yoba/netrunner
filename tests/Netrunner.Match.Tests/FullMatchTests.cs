using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class FullMatchTests
{
    [Theory]
    [InlineData(1ul)]
    [InlineData(42ul)]
    [InlineData(123ul)]
    public async Task DeveloperSimulationStartsFromDecksAndReplaysToItsDeclaredOutcome(ulong seed)
    {
        DeveloperMatchCards cards = DeveloperMatchCards.Create();
        SimulationFixtureDocument fixture = FullMatchDefinition.DeveloperFixture(cards, seed);
        fixture = SimulationFixtureJson.Parse(SimulationFixtureJson.Serialize(fixture));
        FullMatchResult first = await FullMatchRunner.RunSimulationAsync(fixture, cards.Data, cards.Behavior);
        FullMatchResult second = await FullMatchRunner.RunSimulationAsync(fixture, cards.Data, cards.Behavior);
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(EngineJson.Serialize(first.Replay), EngineJson.Serialize(second.Replay));
        Assert.Equal(GameWinReason.DeckOut, first.Outcome.Reason);
        Assert.Equal(PlayerSide.Runner, first.Outcome.Winner);
        Assert.True(first.ActionCount > 10);
        Assert.IsType<SetupFrame>(first.Replay.InitialSnapshot.ToState().ResolutionStack.Single());
    }

    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public async Task BothHumanRolesCompleteSetupAndFullTurnsThroughCoordinator(PlayerSide human)
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        var opponent = new FullGameContractAgent(new FullGameContractPolicy(human == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp));
        var humanPolicy = new FullGameContractAgent(new FullGameContractPolicy(human));
        await using var coordinator = new MatchCoordinator(definition, opponent, new MatchCoordinatorOptions { HumanSide = human });
        await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        bool setupSeen = false;
        bool mulliganTaken = false;
        for (int i = 0; i < 500; i++)
        {
            Observation observation = await coordinator.GetReadyObservationAsync(human).WaitAsync(TimeSpan.FromSeconds(5));
            MatchSnapshot snapshot = coordinator.CurrentSnapshot!;
            Assert.Equal(human, snapshot.View.Viewer);
            Assert.False(snapshot.Feedback?.IsError == true, snapshot.Feedback?.Message);
            if (snapshot.IsTerminal)
            {
                Assert.True(setupSeen);
                Assert.True(mulliganTaken);
                return;
            }
            Assert.True(snapshot.AwaitingHumanDecision);
            Assert.Equal(human, observation.Decision!.OwnerSide);
            setupSeen |= observation.Decision.Kind == DecisionKind.Setup;
            LegalAction? mulligan = !mulliganTaken && observation.Decision.Kind == DecisionKind.Setup
                ? observation.LegalActions.FirstOrDefault(action => action.Parameters is NumberChoiceParameters { Value: 1 }) : null;
            ActionId id = mulligan?.Id ?? await humanPolicy.ChooseActionAsync(observation);
            mulliganTaken |= mulligan is not null;
            MatchSubmissionResult submitted = await coordinator.SubmitHumanActionAsync(id).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(submitted.Accepted, submitted.Feedback);
        }
        Assert.Fail("Client match exceeded its action cap.");
    }

    [Fact]
    public async Task SimulationCapIsEnforcedAndDeveloperDecksCannotEnterStandardSetup()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        FullMatchStallException failure = await Assert.ThrowsAsync<FullMatchStallException>(
            () => FullMatchRunner.RunAsync(definition, actionCap: 1));
        Assert.Equal(1, failure.ActionCap);
        Assert.Single(failure.Replay.Actions);
        ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(EngineJson.Serialize(failure.Replay),
            definition.Behavior, definition.Runtime);
        Assert.True(ReplayVerifier.Verify(replay, definition.Behavior, definition.Runtime).Success);
        GameState stalled = failure.Snapshot.ToState();
        Assert.Equal(replay.ExpectedFinalStateHash, DeterministicHash.ForState(stalled));
        Assert.Equal(GameOutcomeKind.Ongoing, stalled.Outcome.Kind);
        Assert.NotNull(stalled.PendingDecision);
        Assert.Contains(replay.ExpectedFinalStateHash!, failure.Message, StringComparison.Ordinal);
        DeveloperMatchCards cards = DeveloperMatchCards.Create();
        Assert.Throws<MatchRequestValidationException>(() => FullMatchDefinition.FromRequest(definition.Request, cards.Data, cards.Behavior));
    }
}
