using Netrunner.Cards;
using Xunit;

namespace Netrunner.Cli.Tests;

/// <summary>Bot plan S1: ai-regret roots, label and score, end to end on one saved game.</summary>
public sealed class AiRegretCommandTests
{
    [Fact]
    public void ASuiteIsDrawnLabelledAndScored()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        string suite = Path.Combine(Path.GetTempPath(), $"netrunner-regret-{Guid.NewGuid():N}");
        string games = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Netrunner.Match.Tests", "Fixtures", "saved-games",
            "strategy1-petty-cash");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            Assert.True(CliCommands.Run(["ai-regret", "roots", games, "--output", suite, "--turn-roots", "2", "--run-roots", "1"], output, error) == 0,
                error.ToString());
            Assert.True(CliCommands.Run(["ai-regret", "label", suite, "--samples", "2", "--horizon", "1"], output, error) == 0, error.ToString());
            Assert.True(CliCommands.Run(["ai-regret", "score", suite, "--version", "corp-heuristic-v30,corp-heuristic-v29", "--bootstrap", "20"],
                output, error) == 0, error.ToString());

            string text = output.ToString();
            Assert.Contains("ai-regret roots games=1", text, StringComparison.Ordinal);
            Assert.Contains("corp-heuristic-v30 (Corp, standard)", text, StringComparison.Ordinal);
            Assert.Contains("compare corp-heuristic-v30 − corp-heuristic-v29", text, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(suite)) Directory.Delete(suite, recursive: true);
        }
    }

    [Theory]
    [InlineData("roots")]
    [InlineData("unknown")]
    public void AnIncompleteRequestFails(string command)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(2, CliCommands.Run(["ai-regret", command, Path.GetTempPath()], output, error));
        Assert.StartsWith("ai-regret failed:", error.ToString(), StringComparison.Ordinal);
    }
}
