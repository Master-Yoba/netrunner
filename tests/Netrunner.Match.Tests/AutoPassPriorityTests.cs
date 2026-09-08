using System.Collections.Concurrent;
using System.Reflection;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class AutoPassPriorityTests
{
    [Fact]
    public async Task AutoPassPriorityAdvancesTheSoleHumanPassAfterStart()
    {
        (M2ScenarioDocument _, BehaviorCatalog catalog, GameState initial) = RepresentativeRunState();
        Observation opening = ObservationBuilder.Build(initial, PlayerSide.Runner, catalog);
        LegalAction pass = Assert.Single(opening.LegalActions);
        Assert.Equal(ActionKind.PassPriority, pass.Kind);

        var publications = new ConcurrentQueue<MatchSnapshot>();
        await using var coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                AutoPassPriority = true,
                Delay = static (_, _) => ValueTask.CompletedTask,
            },
            new FirstLegalAgent());
        coordinator.SnapshotPublished += publications.Enqueue;

        await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForAsync(
            () => coordinator.CurrentSnapshot is { ActionCount: > 0 },
            TimeSpan.FromSeconds(5));

        Assert.Contains(publications, snapshot => snapshot.ActionCount > 0);
        Assert.DoesNotContain(
            publications.SelectMany(snapshot => snapshot.History),
            entry => entry.Summary.Contains("passes priority", StringComparison.OrdinalIgnoreCase)
                || entry.Details.Any(detail => detail.Contains("passes priority", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task AutoPassPriorityFalseLeavesTheSoleHumanPassForManualChoice()
    {
        await using var coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                Delay = static (_, _) => ValueTask.CompletedTask,
            },
            new FirstLegalAgent());

        MatchSnapshot started = await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(started.AwaitingHumanDecision);
        LegalAction pass = Assert.Single(started.Observation.LegalActions);
        Assert.Equal(ActionKind.PassPriority, pass.Kind);
        MatchSubmissionResult submitted = await coordinator.SubmitHumanActionAsync(pass.Id)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(submitted.Accepted, submitted.Feedback);
    }

    [Fact]
    public async Task AutoPassPriorityLeavesAHumanChoiceWhenAnotherActionIsLegal()
    {
        (M2ScenarioDocument _, BehaviorCatalog catalog, GameState state) = RepresentativeRunState();
        state = ApplyKind(state, ActionKind.PassPriority, catalog);
        state = ApplyKind(state, ActionKind.PassPriority, catalog);
        state = ApplyKind(state, ActionKind.PassPriority, catalog);
        state = ApplyKind(state, ActionKind.RezIce, catalog);
        state = ApplyKind(state, ActionKind.PassPriority, catalog);
        state = ApplyKind(state, ActionKind.PassPriority, catalog);

        await using var coordinator = M2MatchFactory.CreateRepresentative(
            new MatchCoordinatorOptions
            {
                AutoPassPriority = true,
                Delay = static (_, _) => ValueTask.CompletedTask,
            },
            new FirstLegalAgent());
        SetPrivateState(coordinator, state);
        MatchSnapshot published = (MatchSnapshot)typeof(MatchCoordinator)
            .GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, [Array.Empty<EngineEvent>(), null, false])!;

        Assert.True(published.AwaitingHumanDecision);
        Assert.Contains(published.Observation.LegalActions, action => action.Kind == ActionKind.PassPriority);
        Assert.Contains(published.Observation.LegalActions, action => action.Kind != ActionKind.PassPriority);

        await (Task)typeof(MatchCoordinator)
            .GetMethod("DriveAgentsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(coordinator, null)!;

        Assert.Equal(state.ActionSequence, GetPrivateState(coordinator).ActionSequence);
        Assert.True(coordinator.CurrentSnapshot!.AwaitingHumanDecision);
    }

    [Fact]
    public async Task AutoPassPriorityDrivesTheHumanCorpSeatAfterSetup()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
        await using var coordinator = new MatchCoordinator(
            definition,
            new FirstLegalAgent(),
            new MatchCoordinatorOptions
            {
                HumanSide = PlayerSide.Corp,
                AutoPassPriority = true,
                Delay = static (_, _) => ValueTask.CompletedTask,
            });

        MatchSnapshot started = await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(started.AwaitingHumanDecision);
        LegalAction keep = Assert.Single(
            started.Observation.LegalActions,
            action => action.Parameters is NumberChoiceParameters { Value: 0 });
        MatchSubmissionResult submitted = await coordinator.SubmitHumanActionAsync(keep.Id)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(submitted.Accepted, submitted.Feedback);

        await WaitForAsync(
            () => coordinator.CurrentSnapshot is { AwaitingHumanDecision: true } snapshot
            && snapshot.Observation.LegalActions.Count > 0
            && !IsSolePass(snapshot.Observation.LegalActions),
            TimeSpan.FromSeconds(5));

        Assert.True(coordinator.CurrentSnapshot!.ActionCount >= 3);
    }

    [Fact]
    public async Task RecoveredSessionDoesNotAutoPassUntilExplicitlyContinued()
    {
        string sourceDirectory = NewDirectory();
        string destinationDirectory = NewDirectory();
        try
        {
            (M2ScenarioDocument document, BehaviorCatalog catalog, GameState initial) = RepresentativeRunState();
            using (SessionTrace.CreateNew(
                sourceDirectory,
                initial,
                PlayerSide.Runner,
                catalog,
                scenarioId: document.ScenarioId))
            {
            }

            await using var coordinator = M2MatchFactory.CreateRepresentative(
                new MatchCoordinatorOptions
                {
                    AutoPassPriority = true,
                    ResumeTraceDirectory = sourceDirectory,
                    SessionTraceDirectory = destinationDirectory,
                    Delay = static (_, _) => ValueTask.CompletedTask,
                },
                new FirstLegalAgent());

            MatchSnapshot paused = await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, paused.ActionCount);
            Assert.False(paused.IsOpponentThinking);
            Assert.True(paused.AwaitingHumanDecision);
            Assert.Equal(ActionKind.PassPriority, Assert.Single(paused.Observation.LegalActions).Kind);

            await coordinator.ContinueRecoveredSessionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForAsync(
                () => coordinator.CurrentSnapshot is { ActionCount: > 0 },
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            DeleteDirectory(sourceDirectory);
            DeleteDirectory(destinationDirectory);
        }
    }

    [Fact]
    public async Task AutomaticallySubmittedPassIsIncludedInDurableReplay()
    {
        string directory = NewDirectory();
        try
        {
            (M2ScenarioDocument _, BehaviorCatalog catalog, _) = RepresentativeRunState();
            await using (var coordinator = M2MatchFactory.CreateRepresentative(
                new MatchCoordinatorOptions
                {
                    AutoPassPriority = true,
                    SessionTraceDirectory = directory,
                    Delay = static (_, _) => ValueTask.CompletedTask,
                },
                new FirstLegalAgent()))
            {
                await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await WaitForAsync(
                    () => coordinator.CurrentSnapshot is { ActionCount: > 0 },
                    TimeSpan.FromSeconds(5));
            }

            SessionRecoveryResult recovered = SessionTrace.Recover(directory, catalog);
            Assert.True(recovered.State.ActionSequence > 0);
            Assert.Equal(recovered.State.ActionSequence, recovered.Replay.Actions.Count);
            Assert.Equal(
                recovered.State.Players.Single(player => player.Side == PlayerSide.Runner).Id,
                recovered.Replay.Actions[0].Submission.Actor);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static (M2ScenarioDocument Document, BehaviorCatalog Catalog, GameState State) RepresentativeRunState()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        BehaviorCatalog catalog = document.CreateBehaviorCatalog(GatewayBehaviorCatalog.Create());
        M2ScenarioRunSpec run = document.Run
            ?? throw new InvalidOperationException("The representative scenario has no run contract.");
        GameState setup = M2ScenarioSetup.Build(document, catalog);
        ServerId server = setup.Servers.Single(candidate => candidate.Kind == ServerKind.Remote).Id;
        GameState started = GameEngine.BeginRunWithEvents(
            setup,
            server,
            catalog,
            initiatingSource: new CardInstanceId(run.Source)).State;
        return (document, catalog, started);
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTimeOffset.UtcNow >= deadline)
                throw new TimeoutException("The coordinator did not reach the expected state.");

            await Task.Delay(10);
        }
    }

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), $"netrunner-auto-pass-{Guid.NewGuid():N}");

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private static bool IsSolePass(IReadOnlyList<LegalAction> actions) =>
        actions.Count == 1 && actions[0].Kind == ActionKind.PassPriority;

    private static GameState ApplyKind(GameState state, ActionKind kind, BehaviorCatalog catalog)
    {
        PendingDecision decision = state.PendingDecision
            ?? throw new InvalidOperationException("The test state has no pending decision.");
        LegalAction action = decision.LegalActions.Single(candidate => candidate.Kind == kind);
        ApplyResult result = GameEngine.Apply(
            state,
            new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id),
            catalog);
        Assert.True(result.Accepted, result.Rejection?.Message);
        return result.State;
    }

    private static void SetPrivateState(MatchCoordinator coordinator, GameState state) =>
        typeof(MatchCoordinator)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(coordinator, state);

    private static GameState GetPrivateState(MatchCoordinator coordinator) =>
        (GameState)typeof(MatchCoordinator)
            .GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(coordinator)!;
}
