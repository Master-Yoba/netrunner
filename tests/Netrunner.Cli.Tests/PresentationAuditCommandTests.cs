using System.Text.Json;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class PresentationAuditCommandTests
{
    [Fact]
    public void DeveloperAuditPlaysBothSeatsWithoutSnapMismatches()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = CliCommands.Run(["presentation-audit", "--games", "3", "--seed", "5", "--workers", "2", "--json"], output, error);

        Assert.True(code == 0, error.ToString());
        using JsonDocument document = JsonDocument.Parse(output.ToString());
        JsonElement root = document.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal(6, root.GetProperty("seats").GetInt32());
        Assert.Equal(0, root.GetProperty("snapMismatches").GetInt32());
        Assert.Equal(0, root.GetProperty("captionLeaks").GetInt32());
        Assert.True(root.GetProperty("beats").GetInt32() > 0);
    }

    [Theory]
    [InlineData("--games", "0")]
    [InlineData("--content", "other")]
    [InlineData("--workers", "65")]
    [InlineData("--captions", "0")]
    public void InvalidArgumentsExitWithTwo(string option, string value)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(2, CliCommands.Run(["presentation-audit", option, value], output, error));
    }
}
