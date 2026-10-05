using System.Text.Json;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class AiLadderCommandTests
{
    [Fact]
    public void DeveloperLadderReportsRatingsForBothSeats()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(["ai-ladder", "--content", "developer", "--entrants", "random,contract",
            "--seeds-per-pairing", "2", "--bootstrap", "20", "--workers", "2", "--json"], output, error);

        Assert.True(exitCode is 0 or 1, error.ToString());
        using JsonDocument report = JsonDocument.Parse(output.ToString());
        Assert.Equal(8, report.RootElement.GetProperty("games").GetInt32());
        Assert.Equal(0, report.RootElement.GetProperty("errors").GetInt32());
        Assert.Equal(4, report.RootElement.GetProperty("ratings").GetArrayLength());
        Assert.Equal(2, report.RootElement.GetProperty("separations").GetArrayLength());
    }

    [Fact]
    public void UnverifiedLadderGamesMatchVerifiedOnesGameByGame()
    {
        JsonElement Results(string every)
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            int exitCode = CliCommands.Run(["ai-ladder", "--content", "developer", "--entrants", "random,standard",
                "--seeds-per-pairing", "2", "--bootstrap", "0", "--workers", "2", "--verify-replays-every", every, "--json"], output, error);
            Assert.True(exitCode is 0 or 1, error.ToString());
            using JsonDocument report = JsonDocument.Parse(output.ToString());
            return report.RootElement.Clone();
        }

        JsonElement checkedEvery = Results("1"), checkedSome = Results("3");
        Assert.Equal(8, checkedEvery.GetProperty("verifiedGames").GetInt32());
        Assert.Equal(3, checkedSome.GetProperty("verifiedGames").GetInt32());
        JsonElement[] every = [.. checkedEvery.GetProperty("results").EnumerateArray()];
        JsonElement[] some = [.. checkedSome.GetProperty("results").EnumerateArray()];
        Assert.Equal(every.Length, some.Length);
        for (int index = 0; index < every.Length; index++)
        {
            Assert.Equal(every[index].GetProperty("id").GetString(), some[index].GetProperty("id").GetString());
            Assert.Equal(every[index].GetProperty("actions").GetInt32(), some[index].GetProperty("actions").GetInt32());
            Assert.Equal(every[index].GetProperty("corpScore").GetDouble(), some[index].GetProperty("corpScore").GetDouble());
            Assert.False(string.IsNullOrEmpty(every[index].GetProperty("finalStateHash").GetString()));
            Assert.Equal(every[index].GetProperty("finalStateHash").GetString(), some[index].GetProperty("finalStateHash").GetString());
            Assert.Equal(index % 3 == 0, some[index].GetProperty("verified").GetBoolean());
        }
    }

    [Fact]
    public void ACappedGameIsAlwaysVerified()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = CliCommands.Run(["ai-ladder", "--content", "developer", "--entrants", "random,contract", "--seeds-per-pairing", "1",
            "--bootstrap", "0", "--action-cap", "20", "--verify-replays-every", "1000", "--json"], output, error);

        Assert.True(exitCode is 0 or 1, error.ToString());
        using JsonDocument report = JsonDocument.Parse(output.ToString());
        Assert.Equal(0, report.RootElement.GetProperty("errors").GetInt32());
        JsonElement[] results = [.. report.RootElement.GetProperty("results").EnumerateArray()];
        Assert.All(results, result => Assert.True(result.GetProperty("capped").GetBoolean()));
        Assert.All(results, result => Assert.True(result.GetProperty("verified").GetBoolean()));
        Assert.Equal(results.Length, report.RootElement.GetProperty("verifiedGames").GetInt32());
    }

    [Theory]
    [InlineData("--verify-replays-every", "0")]
    [InlineData("--entrants", "random")]
    [InlineData("--entrants", "random,grandmaster")]
    [InlineData("--seeds-per-pairing", "0")]
    [InlineData("--content", "elsewhere")]
    public void InvalidLadderArgumentsExitWithTwo(string option, string value)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(2, CliCommands.Run(["ai-ladder", "--content", "developer", option, value], output, error));
    }

    [Fact]
    public void ReviewRequiresAnExistingSessionDirectory()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(2, CliCommands.Run(["ai-review"], output, error));
        Assert.Equal(2, CliCommands.Run(["ai-review", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}")], output, error));
    }
}
