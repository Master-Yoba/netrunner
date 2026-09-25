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

    [Theory]
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
