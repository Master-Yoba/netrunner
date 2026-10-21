using System.Text.Json;
using Xunit;

namespace Netrunner.Cli.Tests;

/// <summary>Bot plan S0.1: engine-bench --profile-game and --profile-plan.</summary>
public sealed class EngineProfileCommandTests
{
    [Fact]
    public void AGameProfileReportsItsSegmentsAndRollouts()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(["engine-bench", "--profile-game", "--content", "developer", "--games", "1",
            "--rollout-roots", "1", "--rollout-samples", "1", "--json"], output, error);

        Assert.True(exitCode == 0, error.ToString());
        using JsonDocument report = JsonDocument.Parse(output.ToString());
        JsonElement root = report.RootElement;
        Assert.Equal("engine-profile-game", root.GetProperty("kind").GetString());
        int actions = root.GetProperty("actions").GetInt32();
        Assert.Contains(root.GetProperty("segments").EnumerateArray(),
            segment => segment.GetProperty("name").GetString() == "match.apply" && segment.GetProperty("count").GetInt32() == actions);
        Assert.Equal(1, root.GetProperty("gameRollouts").GetProperty("rollouts").GetInt32());
    }

    [Theory]
    [InlineData("--profile-plan", "games")]
    [InlineData("--profile-game", "--profile-plan")]
    public void AnIncompleteProfileRequestFails(string first, string second)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(["engine-bench", first, second], output, error);

        Assert.Equal(2, exitCode);
        Assert.StartsWith("engine-bench failed:", error.ToString(), StringComparison.Ordinal);
    }
}
