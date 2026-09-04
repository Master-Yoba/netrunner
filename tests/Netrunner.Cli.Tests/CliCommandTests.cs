using System.Text.Json;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class CliCommandTests
{
    [Fact]
    public void ScenarioCommandReportsCanonicalStateAndSupportsJsonOutput()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Scenarios", "base.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(["scenario", path, "--json"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "0fc46fa8811517c94c1daf0bbd1b185b438ae898cecb40bd8d56ce0c10197efc",
            document.RootElement.GetProperty("stateHash").GetString());
        Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("state").ValueKind);
    }

    [Fact]
    public void ScenarioCommandReturnsNonzeroForStrictInvalidInput()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scenarios", "base.json"));
        string path = WriteTemp(source[..^1] + ",\"unexpected\":true}");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            int exitCode = CliCommands.Run(["scenario", path], output, error);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("scenario failed", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void JsonArtifactCommandsReturnStructuredFailuresForParseAndIoErrors()
    {
        string malformedPath = WriteTemp("{\"artifactKind\":");
        try
        {
            using var scenarioOutput = new StringWriter();
            using var scenarioError = new StringWriter();
            int scenarioCode = CliCommands.Run(
                ["scenario", malformedPath, "--json"],
                scenarioOutput,
                scenarioError);

            Assert.NotEqual(0, scenarioCode);
            Assert.Empty(scenarioError.ToString());
            using (JsonDocument scenarioDocument = JsonDocument.Parse(scenarioOutput.ToString()))
            {
                Assert.Equal("failed", scenarioDocument.RootElement.GetProperty("status").GetString());
                Assert.False(string.IsNullOrEmpty(scenarioDocument.RootElement.GetProperty("message").GetString()));
            }

            using var replayOutput = new StringWriter();
            using var replayError = new StringWriter();
            int replayCode = CliCommands.Run(
                ["replay", Path.Combine(Path.GetDirectoryName(malformedPath)!, "does-not-exist.json"), "--json"],
                replayOutput,
                replayError);

            Assert.NotEqual(0, replayCode);
            Assert.Empty(replayError.ToString());
            using (JsonDocument replayDocument = JsonDocument.Parse(replayOutput.ToString()))
            {
                Assert.Equal("failed", replayDocument.RootElement.GetProperty("status").GetString());
                Assert.False(string.IsNullOrEmpty(replayDocument.RootElement.GetProperty("message").GetString()));
            }
        }
        finally
        {
            File.Delete(malformedPath);
        }
    }

    [Fact]
    public void CardDataCommandRejectsInvalidOptionsAsStructuredJson()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(
            [
                "card-data",
                "normalize",
                "--upstream",
                "upstream.json",
                "--copyright",
                "COPYRIGHT.md",
                "--commit",
                "NOT-A-COMMIT",
                "--retrieval-date",
                "2026-08-28",
                "--output-dir",
                "stage",
                "--json",
            ],
            output,
            error);

        Assert.Equal(2, exitCode);
        Assert.Empty(error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.Contains("commit", document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CardDataValidationReportsMissingLocalSnapshotAsJsonFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), $"netrunner-card-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exitCode = CliCommands.Run(["card-data", "validate", "--root", root, "--json"], output, error);

            Assert.Equal(1, exitCode);
            Assert.Empty(error.ToString());
            using JsonDocument document = JsonDocument.Parse(output.ToString());
            Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
            Assert.Contains("normalized card data", document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReplayCommandReportsSuccessAndFirstDivergence()
    {
        (ReplayArtifact artifact, string expectedFinalHash) = CreateReplay();
        string successPath = WriteTemp(EngineJson.Serialize(artifact));
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            int successCode = CliCommands.Run(["replay", successPath], output, error);

            Assert.Equal(0, successCode);
            Assert.Contains("replay=ok", output.ToString(), StringComparison.Ordinal);
            Assert.Contains(expectedFinalHash, output.ToString(), StringComparison.Ordinal);
            Assert.Empty(error.ToString());

            var divergent = new ReplayArtifact(
                artifact.InitialSnapshot,
                artifact.Seed,
                artifact.Actions,
                expectedFinalStateHash: new string('0', 64));
            string divergentPath = WriteTemp(EngineJson.Serialize(divergent));
            try
            {
                output.GetStringBuilder().Clear();
                error.GetStringBuilder().Clear();
                int divergentCode = CliCommands.Run(["replay", divergentPath, "--json"], output, error);

                Assert.NotEqual(0, divergentCode);
                Assert.Contains("failed", output.ToString(), StringComparison.Ordinal);
                Assert.Contains("actionSequence", output.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(divergentPath);
            }
        }
        finally
        {
            File.Delete(successPath);
        }
    }

    private static (ReplayArtifact Artifact, string ExpectedFinalHash) CreateReplay()
    {
        GameState initial = GameState.CreateNew(42);
        PlayerId corp = initial.Players.Single(player => player.Side == PlayerSide.Corp).Id;
        GameState outerPrompt = GameEngine.BeginResolution(initial, new ChooseNumberThenAdjustCreditsFrame(corp, corp, 1, 3, 2));
        PendingDecision outerDecision = outerPrompt.PendingDecision!;
        LegalAction outerAction = outerDecision.LegalActions.Single(action =>
            action.Parameters is NumberChoiceParameters { Value: 2 });
        ApplyResult innerResult = GameEngine.Apply(
            outerPrompt,
            new ActionSubmission(outerDecision.Owner, outerDecision.Id, outerDecision.Revision, outerAction.Id));
        PendingDecision innerDecision = innerResult.State.PendingDecision!;
        LegalAction innerAction = innerDecision.LegalActions.Single(action =>
            action.Parameters is NumberChoiceParameters { Value: 3 });
        ApplyResult completedResult = GameEngine.Apply(
            innerResult.State,
            new ActionSubmission(innerDecision.Owner, innerDecision.Id, innerDecision.Revision, innerAction.Id));
        var artifact = new ReplayArtifact(
            outerPrompt.CreateSnapshot(),
            42,
            [
                ReplayAction.From(innerResult.AcceptedAction!),
                ReplayAction.From(completedResult.AcceptedAction!),
            ],
            expectedFinalStateHash: DeterministicHash.ForState(completedResult.State));
        return (artifact, DeterministicHash.ForState(completedResult.State));
    }

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"netrunner-cli-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }
}
