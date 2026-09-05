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
            ulong expectedSeed = simulation ? 48042ul : 42ul;
            File.WriteAllText(fixturePath, SimulationFixtureJson.Serialize(
                FullMatchDefinition.DeveloperFixture(DeveloperMatchCards.Create(), expectedSeed)));
            using var output = new StringWriter();
            using var error = new StringWriter();
            string[] args = simulation ? ["simulate", fixturePath, "--json", "--replay-out", replayPath]
                : ["full-match", "--seed", "42", "--json", "--replay-out", replayPath];
            Assert.Equal(0, CliCommands.Run(args, output, error));
            Assert.Empty(error.ToString());
            using JsonDocument result = JsonDocument.Parse(output.ToString());
            Assert.Equal(expectedSeed, result.RootElement.GetProperty("seed").GetUInt64());
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

    [Fact]
    public void FullMatchUsesHumanOutputUnlessJsonIsRequested()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(["full-match", "--seed", "42"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.Contains("full-match=ok", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"status\"", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--seed", "invalid", "--json")]
    [InlineData("--json", "--seed", "invalid")]
    public void InvalidFullMatchArgumentsReturnStructuredJsonFailureRegardlessOfFlagOrder(params string[] options)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(["full-match", .. options], output, error);

        Assert.Equal(1, exitCode);
        Assert.Empty(error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.Contains("--seed", document.RootElement.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FullMatchRuntimeFailureUsesStructuredJsonWhenRequested()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(
            ["simulate", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"), "--json"],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public void OfficialPresetFullMatchDoesNotFallBackToDeveloperContent()
    {
        if (string.Equals(CardDataResource.EmbeddedMode, CardDataResource.OfficialMode, StringComparison.Ordinal))
        {
            return;
        }

        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(["full-match", "--preset", "basic", "--seed", "47042", "--json"], output, error);

        Assert.Equal(1, exitCode);
        Assert.Empty(error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        string message = document.RootElement.GetProperty("message").GetString()!;
        Assert.Contains("authenticated System Gateway", message, StringComparison.Ordinal);
        Assert.Contains("NetrunnerContentEnabled=true", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("basic", "runner", "deckOut", 922)]
    [InlineData("expanded", "corp", "flatline", 111)]
    public void OfficialPresetFullMatchIsDeterministicAndReplayVerifiedWhenAuthenticated(
        string preset,
        string winner,
        string reason,
        int actionCount)
    {
        if (!string.Equals(CardDataResource.EmbeddedMode, CardDataResource.OfficialMode, StringComparison.Ordinal))
        {
            return;
        }

        string replayPath = Path.GetTempFileName();
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exitCode = CliCommands.Run(
                ["full-match", "--preset", preset, "--seed", "47042", "--json", "--replay-out", replayPath],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Empty(error.ToString());
            using JsonDocument first = JsonDocument.Parse(output.ToString());
            Assert.Equal("ok", first.RootElement.GetProperty("status").GetString());
            Assert.Equal($"system-gateway-{preset}", first.RootElement.GetProperty("fixtureId").GetString());
            Assert.Equal(preset, first.RootElement.GetProperty("preset").GetString());
            Assert.Equal(47042ul, first.RootElement.GetProperty("seed").GetUInt64());
            Assert.False(first.RootElement.GetProperty("developerOnly").GetBoolean());
            Assert.Equal(winner, first.RootElement.GetProperty("winner").GetString());
            Assert.Equal(reason, first.RootElement.GetProperty("reason").GetString());
            Assert.Equal(actionCount, first.RootElement.GetProperty("actionCount").GetInt32());
            Assert.True(first.RootElement.GetProperty("replayVerified").GetBoolean());
            string firstHash = first.RootElement.GetProperty("finalStateHash").GetString()!;
            Assert.NotEmpty(File.ReadAllText(replayPath));

            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            Assert.Equal(
                0,
                CliCommands.Run(["full-match", "--preset", preset, "--seed", "47042", "--json"], output, error));
            Assert.Empty(error.ToString());
            using JsonDocument second = JsonDocument.Parse(output.ToString());
            Assert.Equal(firstHash, second.RootElement.GetProperty("finalStateHash").GetString());
            Assert.Equal(actionCount, second.RootElement.GetProperty("actionCount").GetInt32());

            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            Assert.Equal(0, CliCommands.Run(["replay", replayPath, "--json"], output, error));
            Assert.Empty(error.ToString());
            using JsonDocument replay = JsonDocument.Parse(output.ToString());
            Assert.Equal("ok", replay.RootElement.GetProperty("status").GetString());
            Assert.Equal(firstHash, replay.RootElement.GetProperty("finalStateHash").GetString());
        }
        finally
        {
            File.Delete(replayPath);
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
