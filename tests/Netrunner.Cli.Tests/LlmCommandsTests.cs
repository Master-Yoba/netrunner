using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class LlmCommandsTests
{
    [Theory]
    [InlineData("ai-codex-review")]
    [InlineData("ai-codex-review", "no-such-directory", "--side", "corp", "--output", "x")]
    [InlineData("ai-codex-review", ".", "--output", "x")]
    [InlineData("ai-codex-review", ".", "--side", "corp", "--reasoning", "huge", "--output", "x")]
    [InlineData("ai-llm-play")]
    [InlineData("ai-llm-play", "--output", "x", "--side", "left")]
    [InlineData("ai-llm-play", "--output", "x", "--opponent", "nobody")]
    [InlineData("ai-llm-play", "--output", "x", "--tools", "cards")]
    [InlineData("ai-llm-play", "--output", "x", "--parallel", "0")]
    [InlineData("ai-llm-play", "--output", "x", "--shortlist", "1")]
    [InlineData("ai-llm-play", "--output", "x", "--content", "developer")]
    [InlineData("ai-codex-play", "--print-prompt", "dealer")]
    public void InvalidArgumentsExitWithTwo(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, CliCommands.Run(args, output, error));
        Assert.Contains("failed:", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CodexPlayPrintsItsDealsAndPromptForOtherPlayers()
    {
        if (Netrunner.Cards.CardDataResource.EmbeddedMode != Netrunner.Cards.CardDataResource.OfficialMode) return;
        using var deals = new StringWriter();
        Assert.Equal(0, CliCommands.Run(["ai-codex-play", "--print-deals", "--first-game", "50", "--games", "2"], deals, TextWriter.Null));
        // Batch 3's first two games (scripts/ai-claude-play.sh plays the same deals as ai-codex-play).
        Assert.Equal(["051 gateway-discretion-advised~gateway-planning-ahead corp", "052 gateway-advanced-yomi~gateway-party-hard runner"],
            deals.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        using var prompt = new StringWriter();
        Assert.Equal(0, CliCommands.Run(["ai-codex-play", "--print-prompt", "runner"], prompt, TextWriter.Null));
        Assert.Contains("as the runner", prompt.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CardVocabularyListsEachCardsEngineVocabulary()
    {
        if (Netrunner.Cards.CardDataResource.EmbeddedMode != Netrunner.Cards.CardDataResource.OfficialMode) return;
        using var output = new StringWriter();
        Assert.Equal(0, CliCommands.Run(["card-vocabulary", "--json"], output, TextWriter.Null));
        using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
        var cards = document.RootElement.GetProperty("cards").EnumerateArray().ToArray();
        // Every Gateway card, plus the Elevation cards registered so far, each once.
        string[] codes = cards.Select(static card => card.GetProperty("code").GetString()!).ToArray();
        Assert.Equal(77, codes.Count(static code => code.StartsWith("30", StringComparison.Ordinal)));
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        // E6 (docs/history/ELEVATION_PLAN.md): an icebreaker's spec type is part of its vocabulary.
        var breaker = cards.Single(card => card.GetProperty("code").GetString() == "30005");
        Assert.Contains("type:IcebreakerBehaviorSpec", breaker.GetProperty("vocabulary").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(2, CliCommands.Run(["card-vocabulary", "--all"], TextWriter.Null, new StringWriter()));
    }
}
