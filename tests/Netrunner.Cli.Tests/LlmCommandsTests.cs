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
    public void InvalidArgumentsExitWithTwo(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, CliCommands.Run(args, output, error));
        Assert.Contains("failed:", error.ToString(), StringComparison.Ordinal);
    }
}
