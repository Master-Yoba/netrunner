using System.Collections.Concurrent;
using Netrunner.AI;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class FullMatchTelemetryTests
{
    [Fact]
    public async Task ActionCountIncludesBothPlayersAndExcludesThinkingPublications()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer();
        var opponent = new CountingAgent(new FullGameContractAgent(
            new FullGameContractPolicy(PlayerSide.Corp), definition.Runtime));
        await using var coordinator = new MatchCoordinator(definition, opponent,
            new MatchCoordinatorOptions { HumanSide = PlayerSide.Runner });
        var publications = new ConcurrentQueue<MatchSnapshot>();
        coordinator.SnapshotPublished += publications.Enqueue;
        MatchSnapshot initial = await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, initial.ActionCount);
        Assert.Equal(PlayerSide.Corp, initial.SetupDecisionOwnerSide);
        Assert.Null(initial.Observation.Decision);
        Assert.Empty(initial.Observation.LegalActions);

        for (int humanActions = 0; humanActions < 2; humanActions++)
        {
            Observation observation = await coordinator.GetObservationAsync(PlayerSide.Runner)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DecisionKind.Setup, observation.Decision!.Kind);
            Assert.Equal(PlayerSide.Runner, coordinator.CurrentSnapshot!.SetupDecisionOwnerSide);
            Assert.Equal(humanActions + opponent.Choices, coordinator.CurrentSnapshot!.ActionCount);
            // Mulligan, then mandatory keep: both are ordinary accepted engine actions.
            LegalAction action = Assert.Single(observation.LegalActions,
                candidate => candidate.Parameters is NumberChoiceParameters number
                    && number.Value == (humanActions == 0 ? 1 : 0));
            MatchSubmissionResult result = await coordinator.SubmitHumanActionAsync(action.Id)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(result.Accepted, result.Feedback);
        }

        await coordinator.GetObservationAsync(PlayerSide.Runner).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(opponent.Choices > 0);
        Assert.Equal(2 + opponent.Choices, coordinator.CurrentSnapshot!.ActionCount);
        Assert.Null(coordinator.CurrentSnapshot.SetupDecisionOwnerSide);
        MatchSnapshot[] snapshots = publications.ToArray();
        Assert.Contains(snapshots, snapshot => snapshot.IsOpponentThinking);
        Assert.True(snapshots.Length > coordinator.CurrentSnapshot.ActionCount);
        for (int index = 1; index < snapshots.Length; index++)
        {
            Assert.InRange(snapshots[index].ActionCount - snapshots[index - 1].ActionCount, 0, 1);
            if (snapshots[index].IsOpponentThinking)
            {
                Assert.Equal(snapshots[index - 1].ActionCount, snapshots[index].ActionCount);
            }
        }
    }

    private sealed class CountingAgent(IGameAgent inner) : IGameAgent
    {
        private int _choices;

        public int Choices => Volatile.Read(ref _choices);

        public async ValueTask<ActionId> ChooseActionAsync(Observation observation,
            CancellationToken cancellationToken = default)
        {
            ActionId result = await inner.ChooseActionAsync(observation, cancellationToken);
            Interlocked.Increment(ref _choices);
            return result;
        }
    }
}
