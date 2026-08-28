using System.Text.Json;
using Netrunner.AI;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class ToyMatchRunnerTests
{
    [Theory]
    [InlineData(ToyGoal.CorpScore, PlayerSide.Corp, GameWinReason.AgendaPoints)]
    [InlineData(ToyGoal.RunnerSteal, PlayerSide.Runner, GameWinReason.AgendaPoints)]
    [InlineData(ToyGoal.DeckOut, PlayerSide.Runner, GameWinReason.DeckOut)]
    public void EveryContractGoalCompletesWithTypedTerminalOutcome(
        ToyGoal goal,
        PlayerSide expectedWinner,
        GameWinReason expectedReason)
    {
        ToyMatchResult result = ToyMatchRunner.Run(42, goal);

        Assert.True(result.ActionCount > 0);
        Assert.InRange(result.ActionCount, 1, ToyMatchRunner.MaxAcceptedActions);
        Assert.Equal(GameOutcomeKind.Won, result.FinalState.Outcome.Kind);
        Assert.Equal(expectedWinner, result.Winner);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Null(result.FinalState.PendingDecision);
        Assert.Empty(result.FinalState.ResolutionStack);
        Assert.Equal(result.ActionCount, result.AcceptedActions.Count);
        Assert.Equal(result.ActionCount, result.ReplayActions.Count);
        Assert.NotEmpty(result.Events);
        Assert.Equal(result.FinalStateHash, DeterministicHash.ForState(result.FinalState));
        Assert.Equal(result.EventStreamHash, DeterministicHash.ForEvents(result.Events));

        ReplayVerificationResult verification = ReplayVerifier.Verify(result.Replay);
        Assert.True(verification.Success, verification.Message);
        Assert.Equal(result.FinalStateHash, verification.FinalStateHash);
    }

    [Theory]
    [InlineData(ToyGoal.CorpScore)]
    [InlineData(ToyGoal.RunnerSteal)]
    [InlineData(ToyGoal.DeckOut)]
    public void SameSeedAndGoalProduceByteIdenticalStateReplayAndEventEvidence(ToyGoal goal)
    {
        ToyMatchResult first = ToyMatchRunner.Run(42, goal);
        ToyMatchResult second = ToyMatchRunner.Run(42, goal);

        Assert.Equal(
            CanonicalJson.Serialize(first.FinalState),
            CanonicalJson.Serialize(second.FinalState));
        Assert.Equal(
            CanonicalJson.Serialize(first.Replay),
            CanonicalJson.Serialize(second.Replay));
        Assert.Equal(first.EventStreamHash, second.EventStreamHash);
        Assert.Equal(first.FinalStateHash, second.FinalStateHash);
        Assert.Equal(
            first.AcceptedActions.Select(action => action.ResultingStateHash),
            second.AcceptedActions.Select(action => action.ResultingStateHash));
        Assert.Equal(
            first.AcceptedActions.Select(action => action.ResultingEventBatchHash),
            second.AcceptedActions.Select(action => action.ResultingEventBatchHash));
        Assert.Equal(
            first.Events.Select(@event => CanonicalJson.Serialize(@event)),
            second.Events.Select(@event => CanonicalJson.Serialize(@event)));
    }

    [Fact]
    public void TamperedPerStepStateOrEventEvidenceReportsTheFirstAction()
    {
        ToyMatchResult result = ToyMatchRunner.Run(42, ToyGoal.CorpScore);

        ReplayAction[] stateTamperedActions = result.ReplayActions
            .Select((action, index) => new ReplayAction(
                action.Sequence,
                CopySubmission(action.Submission),
                index == 0 ? new string('0', 64) : action.ExpectedStateHash,
                action.ExpectedEventBatchHash))
            .ToArray();
        ReplayVerificationResult stateDivergence = ReplayVerifier.Verify(new ReplayArtifact(
            result.InitialSnapshot,
            result.Seed,
            stateTamperedActions,
            expectedFinalStateHash: result.FinalStateHash));
        Assert.False(stateDivergence.Success);
        Assert.Equal(1, stateDivergence.ActionSequence);
        Assert.Equal(new string('0', 64), stateDivergence.ExpectedHash);

        ReplayAction[] eventTamperedActions = result.ReplayActions
            .Select((action, index) => new ReplayAction(
                action.Sequence,
                CopySubmission(action.Submission),
                action.ExpectedStateHash,
                index == 0 ? new string('f', 64) : action.ExpectedEventBatchHash))
            .ToArray();
        ReplayVerificationResult eventDivergence = ReplayVerifier.Verify(new ReplayArtifact(
            result.InitialSnapshot,
            result.Seed,
            eventTamperedActions,
            expectedFinalStateHash: result.FinalStateHash));
        Assert.False(eventDivergence.Success);
        Assert.Equal(1, eventDivergence.ActionSequence);
        Assert.Equal(new string('f', 64), eventDivergence.ExpectedHash);
    }

    [Fact]
    public void ResultEvidenceAndReplayActionsAreReadOnlyAtThePublicBoundary()
    {
        ToyMatchResult result = ToyMatchRunner.Run(42, ToyGoal.RunnerSteal);

        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<AcceptedActionRecord>>(result.AcceptedActions).Clear());
        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<ReplayAction>>(result.ReplayActions).Clear());
        Assert.Throws<NotSupportedException>(() =>
            Assert.IsAssignableFrom<IList<EngineEvent>>(result.Events).Clear());
    }

    [Theory]
    [InlineData("--seed", "42", "--goal", "corp-score", "--json")]
    [InlineData("--goal", "runner-steal", "--seed", "42", "--json")]
    [InlineData("--seed", "42", "--goal", "deck-out", "--json")]
    public void PlayJsonSuccessContainsStableMatchFields(params string[] options)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(["play", .. options], output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement root = document.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal(42UL, root.GetProperty("seed").GetUInt64());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("goal").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("winner").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("reason").GetString()));
        Assert.True(root.GetProperty("actionCount").GetInt32() > 0);
        Assert.Equal(64, root.GetProperty("finalStateHash").GetString()!.Length);
        Assert.Equal(64, root.GetProperty("eventStreamHash").GetString()!.Length);
    }

    [Fact]
    public void PlayCanExportReplayThatTheReplayCommandVerifies()
    {
        string directory = Directory.CreateTempSubdirectory("netrunner-play-").FullName;
        string path = Path.Combine(directory, "match.json");
        try
        {
            ToyMatchResult expected = ToyMatchRunner.Run(42, ToyGoal.CorpScore);
            using var playOutput = new StringWriter();
            using var playError = new StringWriter();
            int playCode = CliCommands.Run(
                ["play", "--seed", "42", "--goal", "corp-score", "--json", "--replay-out", path],
                playOutput,
                playError);

            Assert.Equal(0, playCode);
            Assert.Empty(playError.ToString());
            Assert.True(File.Exists(path));
            using JsonDocument playDocument = JsonDocument.Parse(playOutput.ToString());
            string? reportedPath = playDocument.RootElement.GetProperty("replayPath").GetString();
            Assert.Equal(Path.GetFullPath(path), reportedPath);
            Assert.Equal(
                CanonicalJson.Serialize(expected.Replay),
                File.ReadAllText(path));

            using var replayOutput = new StringWriter();
            using var replayError = new StringWriter();
            int replayCode = CliCommands.Run(["replay", path, "--json"], replayOutput, replayError);

            Assert.Equal(0, replayCode);
            Assert.Empty(replayError.ToString());
            using JsonDocument replayDocument = JsonDocument.Parse(replayOutput.ToString());
            Assert.Equal("ok", replayDocument.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("play", "--goal", "corp-score", "--json")]
    [InlineData("play", "--seed", "42", "--json")]
    [InlineData("play", "--seed", "nope", "--goal", "corp-score", "--json")]
    [InlineData("play", "--seed", "42", "--goal", "unknown", "--json")]
    [InlineData("play", "--seed", "42", "--goal", "corp-score", "--seed", "1", "--json")]
    [InlineData("play", "--seed", "42", "--goal", "corp-score", "--json", "--json")]
    [InlineData("play", "--seed", "42", "--goal", "corp-score", "--unknown", "--json")]
    public void InvalidPlayOptionsReturnCodeTwoAndStructuredJsonFailure(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(args, output, error);

        Assert.Equal(2, exitCode);
        Assert.Empty(error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public void InvalidReplayOutputPathReturnsRuntimeFailureInJsonMode()
    {
        string directory = Directory.CreateTempSubdirectory("netrunner-play-").FullName;
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            int exitCode = CliCommands.Run(
                ["play", "--seed", "42", "--goal", "corp-score", "--json", "--replay-out", directory],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(error.ToString());
            using JsonDocument document = JsonDocument.Parse(output.ToString());
            Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ActionSubmission CopySubmission(ActionSubmission submission) =>
        new(submission.Actor, submission.DecisionId, submission.Revision, submission.ActionId);
}
