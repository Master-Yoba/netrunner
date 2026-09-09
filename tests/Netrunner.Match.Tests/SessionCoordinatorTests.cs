using Netrunner.AI;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class SessionCoordinatorTests
{
    [Fact]
    public async Task AcceptedPublicationCanRecoverTraceBeforeSubscriberReturns()
    {
        string directory = NewDirectory();
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
            var opponent = new FullGameContractAgent(
                new FullGameContractPolicy(PlayerSide.Corp),
                definition.Runtime);
            await using var coordinator = new MatchCoordinator(
                definition,
                opponent,
                new MatchCoordinatorOptions
                {
                    HumanSide = PlayerSide.Runner,
                    SessionTraceDirectory = directory,
                });

            var callbackCompleted = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Exception? callbackError = null;
            int checkedPublication = 0;
            coordinator.SnapshotPublished += snapshot =>
            {
                if (snapshot.ActionCount == 0 || Interlocked.Exchange(ref checkedPublication, 1) != 0)
                {
                    return;
                }

                try
                {
                    SessionRecoveryResult recovered = SessionTrace.Recover(
                        directory,
                        definition.Behavior,
                        definition.Runtime);
                    if (recovered.State.ActionSequence != snapshot.ActionCount)
                    {
                        throw new Xunit.Sdk.XunitException(
                            $"The publication sequence {snapshot.ActionCount} was ahead of the durable state sequence {recovered.State.ActionSequence}.");
                    }

                    callbackCompleted.TrySetResult(null);
                }
                catch (Exception exception)
                {
                    callbackError = exception;
                    callbackCompleted.TrySetResult(null);
                }
            };

            await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await callbackCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Null(callbackError);
            Assert.Equal(1, Volatile.Read(ref checkedPublication));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public async Task RecoveredOpponentDecisionStaysPausedAndResumesIntoDistinctTrace()
    {
        string sourceDirectory = NewDirectory();
        string destinationDirectory = NewDirectory();
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
            CreateSourceTrace(definition, sourceDirectory, appendCorpKeep: true);
            Dictionary<string, byte[]> sourceBytes = ReadTraceFiles(sourceDirectory);
            var opponent = new CountingAgent(
                new FullGameContractAgent(
                    new FullGameContractPolicy(PlayerSide.Runner),
                    definition.Runtime));
            await using var coordinator = new MatchCoordinator(
                definition,
                opponent,
                new MatchCoordinatorOptions
                {
                    HumanSide = PlayerSide.Corp,
                    ResumeTraceDirectory = sourceDirectory,
                    SessionTraceDirectory = destinationDirectory,
                });

            MatchSnapshot paused = await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(PlayerSide.Runner, paused.SetupDecisionOwnerSide);
            Assert.False(paused.IsOpponentThinking);
            Assert.Equal(0, opponent.Calls);
            Assert.True(Directory.Exists(destinationDirectory));

            await coordinator.ContinueRecoveredSessionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForAsync(() => opponent.Calls > 0, TimeSpan.FromSeconds(5));

            Assert.True(opponent.Calls > 0);
            Assert.NotEqual(
                Path.GetFullPath(sourceDirectory),
                Path.GetFullPath(destinationDirectory));
            foreach ((string name, byte[] expected) in sourceBytes)
            {
                Assert.Equal(expected, File.ReadAllBytes(Path.Combine(sourceDirectory, name)));
            }
        }
        finally
        {
            DeleteDirectory(sourceDirectory);
            DeleteDirectory(destinationDirectory);
        }
    }

    [Fact]
    public async Task RecoveredHumanSubmissionIsRejectedUntilContinue()
    {
        string sourceDirectory = NewDirectory();
        string destinationDirectory = NewDirectory();
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
            CreateSourceTrace(definition, sourceDirectory, appendCorpKeep: false);
            var opponent = new CountingAgent(
                new FullGameContractAgent(
                    new FullGameContractPolicy(PlayerSide.Runner),
                    definition.Runtime));
            await using var coordinator = new MatchCoordinator(
                definition,
                opponent,
                new MatchCoordinatorOptions
                {
                    HumanSide = PlayerSide.Corp,
                    ResumeTraceDirectory = sourceDirectory,
                    SessionTraceDirectory = destinationDirectory,
                });

            MatchSnapshot paused = await coordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            ObservationDecision decision = paused.Observation.Decision
                ?? throw new Xunit.Sdk.XunitException("The recovered human setup decision was not published.");
            LegalAction action = Assert.Single(paused.Observation.LegalActions,
                candidate => candidate.Parameters is NumberChoiceParameters { Value: 0 });

            MatchSubmissionResult rejected = await coordinator.SubmitHumanActionAsync(action.Id)
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(rejected.Accepted);
            Assert.Equal(ApplyRejectionCode.InvalidInputState, rejected.Rejection?.Code);
            Assert.Contains("paused", rejected.Rejection?.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(decision.Revision, rejected.Snapshot.Observation.Decision?.Revision);
            Assert.Equal(0, opponent.Calls);

            await coordinator.ContinueRecoveredSessionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            MatchSubmissionResult accepted = await coordinator.SubmitHumanActionAsync(action.Id)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(accepted.Accepted, accepted.Feedback);
        }
        finally
        {
            DeleteDirectory(sourceDirectory);
            DeleteDirectory(destinationDirectory);
        }
    }

    [Fact]
    public async Task RecoveryRejectsOpponentPolicyVersionMismatch()
    {
        string sourceDirectory = NewDirectory();
        string destinationDirectory = NewDirectory();
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
            var opponent = new FullGameContractAgent(
                new FullGameContractPolicy(PlayerSide.Runner),
                definition.Runtime);
            await using (var sourceCoordinator = new MatchCoordinator(
                definition,
                opponent,
                new MatchCoordinatorOptions
                {
                    HumanSide = PlayerSide.Corp,
                    SessionTraceDirectory = sourceDirectory,
                }))
            {
                await sourceCoordinator.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }

            SessionTraceManifest manifest = SessionTrace.ReadManifest(sourceDirectory);
            SessionTraceAgentInfo recordedAgent = Assert.IsType<SessionTraceAgentInfo>(manifest.Agent);
            Assert.Equal(typeof(FullGameContractAgent).FullName, recordedAgent.Type);
            Assert.Equal(FullGameContractAgentVersion.AgentVersion, recordedAgent.Version);
            Assert.Equal(FullGameContractAgentVersion.PolicyVersion, recordedAgent.PolicyVersion);

            string manifestPath = Path.Combine(sourceDirectory, SessionTrace.ManifestFileName);
            string manifestJson = File.ReadAllText(manifestPath);
            string policyJson = $"\"policyVersion\":\"{FullGameContractAgentVersion.PolicyVersion}\"";
            Assert.Contains(policyJson, manifestJson, StringComparison.Ordinal);
            File.WriteAllText(
                manifestPath,
                manifestJson.Replace(
                    policyJson,
                    "\"policyVersion\":\"full-game-contract-policy-tampered\"",
                    StringComparison.Ordinal));

            await using var resumed = new MatchCoordinator(
                definition,
                opponent,
                new MatchCoordinatorOptions
                {
                    HumanSide = PlayerSide.Corp,
                    ResumeTraceDirectory = sourceDirectory,
                    SessionTraceDirectory = destinationDirectory,
                });
            SessionTraceException exception = await Assert.ThrowsAsync<SessionTraceException>(
                () => resumed.StartAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("opponent agent", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(sourceDirectory);
            DeleteDirectory(destinationDirectory);
        }
    }

    [Fact]
    public async Task RecordingFailureStopsPublicationAndFurtherSubmissions()
    {
        string directory = NewDirectory();
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
            var opponent = new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Runner), definition.Runtime);
            await using var coordinator = new MatchCoordinator(definition, opponent,
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp, SessionTraceDirectory = directory });
            MatchSnapshot initial = await coordinator.StartAsync();
            string replayPath = Path.Combine(directory, SessionTrace.ReplayFileName);
            string backupPath = replayPath + ".saved";
            File.Move(replayPath, backupPath);
            Directory.CreateDirectory(replayPath);
            DecisionViewModel decision = initial.View.Decision!;
            LegalActionViewModel action = initial.View.LegalActions[0];
            ActionSubmission submission = new(decision.Owner, new DecisionId(decision.Id), decision.Revision, action.Id);
            MatchSubmissionResult failed = await coordinator.SubmitActionAsync(submission);
            Assert.False(failed.Accepted);
            Assert.Contains("match stopped", failed.Snapshot.Feedback!.Message, StringComparison.Ordinal);
            Assert.Equal(initial.ActionCount, coordinator.CurrentSnapshot!.ActionCount);

            Directory.Delete(replayPath);
            File.Move(backupPath, replayPath);
            Exception? fault = await Record.ExceptionAsync(() => coordinator.SubmitActionAsync(submission));
            Assert.True(fault is InvalidOperationException or SessionTraceException);
            SessionRecoveryResult recovered = SessionTrace.Recover(directory, definition.Behavior, definition.Runtime);
            Assert.Equal(initial.ActionCount, recovered.State.ActionSequence);
            Assert.True(recovered.HasInterruptedAction);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static void CreateSourceTrace(
        FullMatchDefinition definition,
        string directory,
        bool appendCorpKeep)
    {
        GameState initial = GameEngine.CreateMatch(
            definition.Request.Seed,
            definition.Request.CorpDeck,
            definition.Request.RunnerDeck,
            definition.Request.CardDataVersion,
            definition.Behavior,
            definition.Runtime).State;
        using SessionTrace trace = SessionTrace.CreateNew(
            directory,
            initial,
            PlayerSide.Corp,
            definition.Behavior,
            definition.Runtime,
            definition.Request);
        if (!appendCorpKeep)
        {
            return;
        }

        PendingDecision pending = initial.PendingDecision
            ?? throw new Xunit.Sdk.XunitException("The developer match did not start with a setup decision.");
        Observation observation = ObservationBuilder.Build(initial, PlayerSide.Corp, definition.Behavior);
        LegalAction keep = Assert.Single(observation.LegalActions,
            action => action.Parameters is NumberChoiceParameters { Value: 0 });
        ActionSubmission submission = new(pending.Owner, pending.Id, pending.Revision, keep.Id);
        string operationId = trace.AppendIntent(initial, submission);
        ApplyResult applied = GameEngine.Apply(initial, submission, definition.Behavior, definition.Runtime);
        Assert.True(applied.Accepted, applied.Rejection?.Message);
        trace.AppendAccepted(operationId, initial, applied.AcceptedAction!);

        PendingDecision next = applied.State.PendingDecision
            ?? throw new Xunit.Sdk.XunitException("The setup action did not issue the opponent decision.");
        Assert.Equal(PlayerSide.Runner, applied.State.GetPlayer(next.Owner).Side);
    }

    private static Dictionary<string, byte[]> ReadTraceFiles(string directory) =>
        new(StringComparer.Ordinal)
        {
            [SessionTrace.ManifestFileName] = File.ReadAllBytes(Path.Combine(directory, SessionTrace.ManifestFileName)),
            [SessionTrace.ReplayFileName] = File.ReadAllBytes(Path.Combine(directory, SessionTrace.ReplayFileName)),
            [SessionTrace.JournalFileName] = File.ReadAllBytes(Path.Combine(directory, SessionTrace.JournalFileName)),
        };

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!predicate())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new Xunit.Sdk.XunitException("The expected coordinator condition was not reached before the timeout.");
            }

            await Task.Delay(10);
        }
    }

    private static string NewDirectory() =>
        Path.Combine(Path.GetTempPath(), $"netrunner-session-coordinator-{Guid.NewGuid():N}");

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CountingAgent(IGameAgent inner) : IGameAgent
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask<ActionId> ChooseActionAsync(
            Observation observation,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return await inner.ChooseActionAsync(observation, cancellationToken).ConfigureAwait(false);
        }
    }
}
