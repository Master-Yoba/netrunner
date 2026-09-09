using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class SessionTraceTests
{
    [Theory]
    [InlineData(PlayerSide.Corp)]
    [InlineData(PlayerSide.Runner)]
    public void AcceptedActionRoundTripsForBothHumanSides(PlayerSide humanSide)
    {
        string directory = NewDirectory();
        try
        {
            (GameState state, BehaviorCatalog behavior) = StartedState();
            ActionSubmission submission = FirstAction(state, behavior);
            using (SessionTrace trace = SessionTrace.CreateNew(directory, state, humanSide, behavior))
            {
                string operation = trace.AppendIntent(state, submission);
                ApplyResult applied = GameEngine.Apply(state, submission, behavior);
                Assert.True(applied.Accepted, applied.Rejection?.Message);
                trace.AppendAccepted(operation, state, applied.AcceptedAction!);
            }

            SessionTraceManifest manifest = SessionTrace.ReadManifest(directory);
            SessionRecoveryResult recovered = SessionTrace.Recover(directory, behavior);
            Assert.Equal(humanSide, manifest.HumanSide);
            Assert.Equal(DeterministicHash.ForState(recovered.State), recovered.Replay.ExpectedFinalStateHash);
            Assert.Single(recovered.Replay.Actions);
            Assert.False(recovered.HasInterruptedAction);
            Assert.False(recovered.IgnoredIncompleteTail);
            Assert.Equal(
                DeterministicHash.ForState(GameEngine.Apply(state, submission, behavior).State),
                DeterministicHash.ForState(recovered.State));
            Assert.NotNull(manifest.Build.EngineAssemblySha256);
            Assert.Equal(64, manifest.Build.EngineAssemblySha256.Length);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void RejectedActionIsDurableButDoesNotMutateResumableState()
    {
        string directory = NewDirectory();
        try
        {
            (GameState state, BehaviorCatalog behavior) = StartedState();
            ActionSubmission valid = FirstAction(state, behavior);
            ActionSubmission rejected = new(valid.Actor, valid.DecisionId, valid.Revision, new ActionId("action:not-issued"));
            string initialHash = DeterministicHash.ForState(state);
            using (SessionTrace trace = SessionTrace.CreateNew(directory, state, PlayerSide.Runner, behavior))
            {
                string operation = trace.AppendIntent(state, rejected);
                ApplyResult applied = GameEngine.Apply(state, rejected, behavior);
                Assert.False(applied.Accepted);
                trace.AppendRejected(operation, state, rejected, applied.Rejection!);
            }

            SessionRecoveryResult recovered = SessionTrace.Recover(directory, behavior);
            Assert.Equal(initialHash, DeterministicHash.ForState(recovered.State));
            Assert.Empty(recovered.Replay.Actions);
            Assert.False(recovered.HasInterruptedAction);
            Assert.Contains("rejection", File.ReadAllText(Path.Combine(directory, SessionTrace.JournalFileName)), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void IncompleteJournalTailIsReportedAndLastAcceptedStateRecovers()
    {
        string directory = NewDirectory();
        try
        {
            (GameState state, BehaviorCatalog behavior) = StartedState();
            ActionSubmission submission = FirstAction(state, behavior);
            using (SessionTrace trace = SessionTrace.CreateNew(directory, state, PlayerSide.Runner, behavior))
            {
                trace.AppendIntent(state, submission);
            }

            File.AppendAllText(Path.Combine(directory, SessionTrace.JournalFileName), "{\"kind\":\"intent\"");
            SessionRecoveryResult recovered = SessionTrace.Recover(directory, behavior);
            Assert.True(recovered.HasInterruptedAction);
            Assert.True(recovered.IgnoredIncompleteTail);
            Assert.Empty(recovered.Replay.Actions);
            Assert.Equal(DeterministicHash.ForState(state), DeterministicHash.ForState(recovered.State));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void CompleteJournalCorruptionFailsClosed()
    {
        string directory = NewDirectory();
        try
        {
            (GameState state, BehaviorCatalog behavior) = StartedState();
            using (SessionTrace trace = SessionTrace.CreateNew(directory, state, PlayerSide.Runner, behavior))
            {
            }

            File.AppendAllText(Path.Combine(directory, SessionTrace.JournalFileName), "{not-json}\n");
            Assert.Throws<SessionTraceException>(() => SessionTrace.Recover(directory, behavior));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void JournalIntentHashMismatchFailsClosed()
    {
        string directory = NewDirectory();
        try
        {
            (GameState state, BehaviorCatalog behavior) = StartedState();
            ActionSubmission submission = FirstAction(state, behavior);
            using (SessionTrace trace = SessionTrace.CreateNew(directory, state, PlayerSide.Runner, behavior))
            {
                trace.AppendIntent(state, submission);
            }

            string journalPath = Path.Combine(directory, SessionTrace.JournalFileName);
            string tampered = File.ReadAllText(journalPath).Replace(
                DeterministicHash.ForState(state),
                new string('0', 64),
                StringComparison.Ordinal);
            File.WriteAllText(journalPath, tampered);
            Assert.Throws<SessionTraceException>(() => SessionTrace.Recover(directory, behavior));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void CompletionContainingAcceptedActionAndRejectionFailsClosed()
    {
        string directory = NewDirectory();
        try
        {
            (GameState state, BehaviorCatalog behavior) = StartedState();
            ActionSubmission submission = FirstAction(state, behavior);
            using (SessionTrace trace = SessionTrace.CreateNew(directory, state, PlayerSide.Runner, behavior))
            {
                string operation = trace.AppendIntent(state, submission);
                ApplyResult applied = GameEngine.Apply(state, submission, behavior);
                trace.AppendAccepted(operation, state, applied.AcceptedAction!);
            }

            string journalPath = Path.Combine(directory, SessionTrace.JournalFileName);
            string journal = File.ReadAllText(journalPath);
            Assert.Contains("\"rejection\":null", journal, StringComparison.Ordinal);
            File.WriteAllText(
                journalPath,
                journal.Replace(
                    "\"rejection\":null",
                    "\"rejection\":{\"code\":\"unknownAction\",\"message\":\"tampered\"}",
                    StringComparison.Ordinal));
            Assert.Throws<SessionTraceException>(() => SessionTrace.Recover(directory, behavior));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void CompleteFinalRecordWithoutNewlineIsReportedAsInterrupted()
    {
        string directory = NewDirectory();
        try
        {
            (GameState state, BehaviorCatalog behavior) = StartedState();
            ActionSubmission submission = FirstAction(state, behavior);
            ApplyResult applied;
            using (SessionTrace trace = SessionTrace.CreateNew(directory, state, PlayerSide.Runner, behavior))
            {
                string operation = trace.AppendIntent(state, submission);
                applied = GameEngine.Apply(state, submission, behavior);
                trace.AppendAccepted(operation, state, applied.AcceptedAction!);
            }

            string journalPath = Path.Combine(directory, SessionTrace.JournalFileName);
            using (FileStream journal = new(journalPath, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                journal.SetLength(journal.Length - 1);
            }

            SessionRecoveryResult recovered = SessionTrace.Recover(directory, behavior);
            Assert.True(recovered.HasInterruptedAction);
            Assert.True(recovered.IgnoredIncompleteTail);
            Assert.Equal(
                DeterministicHash.ForState(applied.State),
                DeterministicHash.ForState(recovered.State));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [Fact]
    public void ReplayPinMismatchAndEarlierStateRecoveryAreVerified()
    {
        string directory = NewDirectory();
        try
        {
            (GameState state, BehaviorCatalog behavior) = StartedState();
            ActionSubmission submission = FirstAction(state, behavior);
            using (SessionTrace trace = SessionTrace.CreateNew(directory, state, PlayerSide.Runner, behavior))
            {
                string operation = trace.AppendIntent(state, submission);
                ApplyResult applied = GameEngine.Apply(state, submission, behavior);
                trace.AppendAccepted(operation, state, applied.AcceptedAction!);
            }

            SessionRecoveryResult recovered = SessionTrace.Recover(directory, behavior, throughActionSequence: state.ActionSequence);
            Assert.Equal(state.ActionSequence, recovered.State.ActionSequence);
            Assert.Empty(recovered.Replay.Actions);

            string replayPath = Path.Combine(directory, SessionTrace.ReplayFileName);
            string replay = File.ReadAllText(replayPath);
            File.WriteAllText(
                replayPath,
                replay.Replace(
                    "\"cardDataVersion\":\"",
                    "\"cardDataVersion\":\"tampered-",
                    StringComparison.Ordinal));
            Assert.Throws<SessionTraceException>(() => SessionTrace.Recover(directory, behavior));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static (GameState State, BehaviorCatalog Behavior) StartedState()
    {
        M2ScenarioDocument document = M2ScenarioProvider.LoadRepresentative();
        BehaviorCatalog behavior = document.Validate(GatewayBehaviorCatalog.Create());
        GameState initial = M2ScenarioSetup.Build(document, behavior);
        ServerState server = initial.Servers.Single(value => value.Kind == ServerKind.Remote);
        GameEngine.RunStartResult started = GameEngine.BeginRunWithEvents(
            initial,
            server.Id,
            behavior,
            initiatingSource: new CardInstanceId(document.Run!.Source));
        return (started.State, behavior);
    }

    private static ActionSubmission FirstAction(GameState state, IBehaviorCatalog behavior)
    {
        PendingDecision decision = state.PendingDecision
            ?? throw new InvalidOperationException("The fixture must yield a pending decision.");
        Observation observation = ObservationBuilder.Build(state, state.GetPlayer(decision.Owner).Side, behavior);
        LegalAction action = observation.LegalActions[0];
        return new ActionSubmission(decision.Owner, decision.Id, decision.Revision, action.Id);
    }

    private static string NewDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"netrunner-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
