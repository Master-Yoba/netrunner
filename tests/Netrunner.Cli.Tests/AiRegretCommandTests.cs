using Netrunner.Cards;
using Netrunner.Match.Regret;
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

    [Fact]
    public void AChoiceOutsideTheLabelledGroupsIsLabelledOnDemand()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        string suite = Path.Combine(Path.GetTempPath(), $"netrunner-regret-{Guid.NewGuid():N}");
        string games = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Netrunner.Match.Tests", "Fixtures", "saved-games",
            "strategy1-petty-cash");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            // One labelled group a root: the versions' other choices are unlabelled until labelled on demand (S1.4).
            Assert.Equal(0, CliCommands.Run(["ai-regret", "roots", games, "--output", suite, "--turn-roots", "3", "--run-roots", "0",
                "--labelled", "1"], output, error));
            Assert.Equal(0, CliCommands.Run(["ai-regret", "label", suite, "--samples", "1", "--horizon", "1"], output, error));
            using var scored = new StringWriter();
            Assert.Equal(0, CliCommands.Run(["ai-regret", "score", suite, "--version", "corp-heuristic-v29,corp-heuristic-v31", "--bootstrap", "0",
                "--label-missing"], scored, error));

            string text = scored.ToString();
            Assert.Contains("ai-regret label-missing", text, StringComparison.Ordinal);
            Assert.Contains("corp-heuristic-v29 (Corp, standard)", text, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"unlabelled=[1-9]", text);
            Assert.Contains(RegretSuite.Load(suite).Roots, root => root.Candidates.Length > 1);
        }
        finally
        {
            if (Directory.Exists(suite)) Directory.Delete(suite, recursive: true);
        }
    }

    [Fact]
    public void RerunningFindingsReplacesThemAndTheirLabels()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        string suite = Path.Combine(Path.GetTempPath(), $"netrunner-regret-{Guid.NewGuid():N}");
        string fixtures = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Netrunner.Match.Tests", "Fixtures", "saved-games");
        string one = Path.Combine(Path.GetTempPath(), $"netrunner-findings-{Guid.NewGuid():N}.json");
        string none = Path.Combine(Path.GetTempPath(), $"netrunner-findings-{Guid.NewGuid():N}.json");
        File.WriteAllText(one, """{"findings":[{"fixture":"strategy1-petty-cash","sequence":101,"side":"corp","test":"T.Petty"}]}""");
        File.WriteAllText(none, """{"findings":[]}""");
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            string[] findings = ["--fixtures", fixtures, "--content", "gateway-elevation"];
            Assert.Equal(0, CliCommands.Run(["ai-regret", "roots", Path.Combine(fixtures, "strategy1-petty-cash"), "--output", suite,
                "--turn-roots", "1", "--run-roots", "0"], output, error));
            Assert.Equal(0, CliCommands.Run(["ai-regret", "findings", suite, "--manifest", one, .. findings], output, error));
            Assert.Equal(0, CliCommands.Run(["ai-regret", "label", suite, "--samples", "1", "--horizon", "1"], output, error));
            Assert.Contains(RegretSuite.Load(suite).Labels, label => label.RootId.StartsWith("finding-", StringComparison.Ordinal));

            Assert.Equal(0, CliCommands.Run(["ai-regret", "findings", suite, "--manifest", none, .. findings], output, error));

            RegretSuite rebuilt = RegretSuite.Load(suite);
            Assert.DoesNotContain(rebuilt.Games, game => game.Source == "finding");
            Assert.DoesNotContain(rebuilt.Labels, label => label.RootId.StartsWith("finding-", StringComparison.Ordinal));
            Assert.NotEmpty(rebuilt.Labels);
        }
        finally
        {
            if (Directory.Exists(suite)) Directory.Delete(suite, recursive: true);
            File.Delete(one);
            File.Delete(none);
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
