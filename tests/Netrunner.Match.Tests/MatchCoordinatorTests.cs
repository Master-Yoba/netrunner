using Netrunner.AI;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class MatchCoordinatorTests
{
    [Fact]
    public async Task RepresentativeStartPublishesRunOpeningAndRunnerDecision()
    {
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
            },
            new FirstLegalAgent());

        MatchSnapshot snapshot = await coordinator.StartAsync();

        Assert.Contains(snapshot.Events, value => value.Kind == EngineEventKinds.RunEventPlayed);
        Assert.Contains(snapshot.Events, value => value.Kind == EngineEventKinds.RunInitiated);
        Assert.NotNull(snapshot.View.Decision);
        Assert.Equal(PlayerSide.Runner, snapshot.View.Decision!.OwnerSide);
        Assert.Contains(snapshot.View.LegalActions, value => value.Kind == ActionKind.PassPriority);
        Assert.NotNull(snapshot.View.Run);
        Assert.Equal("card:7", snapshot.View.Run!.InitiatingSource!.Id);
    }

    [Fact]
    public void RunnerProjectionDoesNotExposeCorpHiddenCardsOrAnonymousIdentity()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        GameState state = M2ScenarioSetup.Build(document);
        Observation runnerObservation = ObservationBuilder.Build(state, PlayerSide.Runner);
        MatchViewModel view = ObservationViewMapper.Build(runnerObservation);

        Assert.All(view.Servers.SelectMany(value => value.Ice), card => Assert.Null(card));
        Assert.All(view.Servers.SelectMany(value => value.Root), card => Assert.Null(card));
        Assert.Empty(view.CorpHand.VisibleCards);
        Assert.Empty(view.CorpDeck.VisibleCards);
        Assert.Equal(1, view.RunnerHand.Count);
        Assert.Contains(view.RunnerHand.VisibleCards, value => value.DefinitionId == "30029");

        MatchViewModel corpView = ObservationViewMapper.Build(
            ObservationBuilder.Build(state, PlayerSide.Corp));
        ServerViewModel remote = Assert.Single(corpView.Servers, value => value.Kind == ServerKind.Remote);
        Assert.Equal(["card:2", "card:1"], remote.OrderedIce.Select(card => card!.Id));
    }

    [Fact]
    public async Task TenSecondAgentDelayPublishesThinkingWithoutBlockingRunnerSubmission()
    {
        var releaseAgent = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                AgentDelay = TimeSpan.FromSeconds(10),
                Delay = (_, cancellationToken) => new ValueTask(releaseAgent.Task.WaitAsync(cancellationToken)),
            },
            new FirstLegalAgent());

        MatchSnapshot initial = await coordinator.StartAsync();
        LegalAction pass = Assert.Single(initial.Observation.LegalActions, value => value.Kind == ActionKind.PassPriority);

        MatchSubmissionResult submitted = await coordinator.SubmitRunnerActionAsync(pass.Id);
        Assert.True(submitted.Accepted);
        await WaitForAsync(
            () => coordinator.CurrentSnapshot?.IsOpponentThinking == true,
            TimeSpan.FromSeconds(2));
        Assert.Null(coordinator.CurrentSnapshot!.Observation.Decision);
        Assert.Null(coordinator.CurrentSnapshot.View.Decision);

        releaseAgent.SetResult(null);
        await WaitForAsync(
            () => coordinator.CurrentSnapshot?.IsOpponentThinking == false,
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StaleSubmissionPublishesFeedbackAndPreservesObservation()
    {
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
            },
            new FirstLegalAgent());

        MatchSnapshot initial = await coordinator.StartAsync();
        ObservationDecision decision = initial.Observation.Decision
            ?? throw new InvalidOperationException("The representative start should await Runner input.");
        LegalAction action = initial.Observation.LegalActions[0];
        string observationBefore = EngineJson.Serialize(initial.Observation);
        MatchSubmissionResult rejected = await coordinator.SubmitActionAsync(
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision + 1, action.Id));

        Assert.False(rejected.Accepted);
        Assert.Equal(ApplyRejectionCode.StaleRevision, rejected.Rejection?.Code);
        Assert.Equal(observationBefore, EngineJson.Serialize(rejected.Snapshot.Observation));
        Assert.Equal(MatchFeedbackKind.Rejected, rejected.Snapshot.Feedback?.Kind);
    }

    [Fact]
    public async Task HumanRunnerCanCompleteTheRepresentativeRunWithoutDroppedEventBatches()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        M2ScenarioRunSpec run = Assert.IsType<M2ScenarioRunSpec>(document.Run);
        var runner = new RunContractAgent(new RunPlayContract(
            run.Server,
            run.Source,
            Assert.IsType<string>(run.Breaker),
            run.MaxPumps));
        var publishedEvents = new System.Collections.Concurrent.ConcurrentQueue<EngineEvent>();
        await using MatchCoordinator coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
            });
        coordinator.SnapshotPublished += snapshot =>
        {
            foreach (EngineEvent @event in snapshot.Events)
            {
                publishedEvents.Enqueue(@event);
            }
        };

        MatchSnapshot snapshot = await coordinator.StartAsync();
        for (int step = 0; step < 256 && snapshot.View.Run is not null; step++)
        {
            if (snapshot.AwaitingHumanDecision)
            {
                ActionId selected = await runner.ChooseActionAsync(snapshot.Observation);
                MatchSubmissionResult result = await coordinator.SubmitRunnerActionAsync(selected);
                Assert.True(result.Accepted, result.Feedback);
                snapshot = result.Snapshot;
                continue;
            }

            long sequence = snapshot.Sequence;
            await WaitForAsync(
                () => coordinator.CurrentSnapshot is { Sequence: > 0 } current
                    && current.Sequence > sequence,
                TimeSpan.FromSeconds(2));
            snapshot = coordinator.CurrentSnapshot!;
        }

        await WaitForAsync(
            () => coordinator.CurrentSnapshot?.View.Run is null,
            TimeSpan.FromSeconds(2));
        snapshot = coordinator.CurrentSnapshot!;

        Assert.Null(snapshot.View.Run);
        Assert.Equal(2, snapshot.View.Runner.AgendaPoints);
        Assert.Equal(12, snapshot.View.Runner.Credits);
        Assert.Contains(publishedEvents, @event => @event.Kind == EngineEventKinds.RunEnded);
        Assert.NotNull(snapshot.RunOutcome);
        Assert.Equal(RunEndReason.BreachCompleted, snapshot.RunOutcome!.Reason);
        Assert.True(snapshot.RunOutcome.Successful);
        Assert.DoesNotContain("GameState", EngineJson.Serialize(snapshot.RunOutcome));
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The asynchronous coordinator did not reach the expected state.");
            }

            await Task.Delay(10);
        }
    }
}
