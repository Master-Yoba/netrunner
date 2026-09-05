using System.Text.Json;
using Netrunner.Cards;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class FullMatchCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullMatchAndSimulationProduceVerifiedSetupReplays(bool simulation)
    {
        string replayPath = Path.GetTempFileName();
        string fixturePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(fixturePath, SimulationFixtureJson.Serialize(FullMatchDefinition.DeveloperFixture(DeveloperMatchCards.Create())));
            using var output = new StringWriter();
            using var error = new StringWriter();
            string[] args = simulation ? ["simulate", fixturePath, "--json", "--replay-out", replayPath]
                : ["full-match", "--seed", "42", "--json", "--replay-out", replayPath];
            Assert.Equal(0, CliCommands.Run(args, output, error));
            Assert.Empty(error.ToString());
            using JsonDocument result = JsonDocument.Parse(output.ToString());
            Assert.True(result.RootElement.GetProperty("replayVerified").GetBoolean());
            Assert.True(result.RootElement.GetProperty("developerOnly").GetBoolean());
            output.GetStringBuilder().Clear();
            Assert.Equal(0, CliCommands.Run(["replay", replayPath, "--json"], output, error));
            Assert.Empty(error.ToString());
            Assert.Contains("\"status\":\"ok\"", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(replayPath);
            File.Delete(fixturePath);
        }
    }

    [Theory]
    [InlineData("--seed", "invalid")]
    [InlineData("--unexpected", "42")]
    public void InvalidFullMatchArgumentsFailWithoutRunning(string option, string value)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, CliCommands.Run(["full-match", option, value], output, error));
        Assert.Empty(output.ToString());
        Assert.Contains("full-match failed", error.ToString(), StringComparison.Ordinal);
    }
}
