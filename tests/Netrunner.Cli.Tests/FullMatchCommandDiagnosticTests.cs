using System.Text.Json;
using System.Text.Json.Nodes;
using Netrunner.Cards;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class FullMatchCommandDiagnosticTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CappedSimulationReportsItsSeedAndPreservesDiagnosticsWhenReplayOutputFails(bool invalidOutput)
    {
        string fixturePath = Path.GetTempFileName();
        string replayPath = Path.GetTempFileName();
        try
        {
            string requestedPath = invalidOutput
                ? Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", "replay.json")
                : replayPath;
            JsonNode fixture = JsonNode.Parse(SimulationFixtureJson.Serialize(
                FullMatchDefinition.DeveloperFixture(DeveloperMatchCards.Create(), seed: 48042)))!;
            fixture["actionCap"] = 1;
            File.WriteAllText(fixturePath, fixture.ToJsonString());
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(1, CliCommands.Run(
                ["simulate", fixturePath, "--json", "--replay-out", requestedPath], output, error));
            Assert.Empty(error.ToString());
            using JsonDocument failure = JsonDocument.Parse(output.ToString());
            Assert.Equal("failed", failure.RootElement.GetProperty("status").GetString());
            Assert.Equal(48042ul, failure.RootElement.GetProperty("seed").GetUInt64());
            Assert.Equal(1, failure.RootElement.GetProperty("actionCount").GetInt32());
            Assert.Equal(1, failure.RootElement.GetProperty("actionCap").GetInt32());
            string hash = failure.RootElement.GetProperty("stateHash").GetString()!;

            if (invalidOutput)
            {
                Assert.False(string.IsNullOrWhiteSpace(failure.RootElement.GetProperty("diagnosticWriteError").GetString()));
                return;
            }

            output.GetStringBuilder().Clear();
            Assert.Equal(0, CliCommands.Run(["replay", replayPath, "--json"], output, error));
            Assert.Empty(error.ToString());
            using JsonDocument replay = JsonDocument.Parse(output.ToString());
            Assert.Equal("ok", replay.RootElement.GetProperty("status").GetString());
            Assert.Equal(hash, replay.RootElement.GetProperty("finalStateHash").GetString());
        }
        finally
        {
            File.Delete(fixturePath);
            File.Delete(replayPath);
        }
    }
}
