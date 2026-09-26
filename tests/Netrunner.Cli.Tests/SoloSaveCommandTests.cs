using System.Text.Json;
using Netrunner.AI;
using Netrunner.Core;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class SoloSaveCommandTests
{
    [Fact]
    public async Task ValidatesPortableSaveAndReturnsOnlyAllowlistedPreview()
    {
        string path = Path.Combine(Path.GetTempPath(), "netrunner-save-cli-" + Guid.NewGuid().ToString("N"));
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(75);
            await using var coordinator = new MatchCoordinator(definition, new FullGameContractAgent(PlayerSide.Runner, definition.Runtime),
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
            await coordinator.StartAsync();
            SoloSaveDocument save = await coordinator.CaptureSaveAsync("CLI validation");
            await File.WriteAllTextAsync(path, EngineJson.Serialize(save));
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, CliCommands.Run(["save-validate", path, "--json"], output, error));
            Assert.Empty(error.ToString());
            using JsonDocument report = JsonDocument.Parse(output.ToString());
            Assert.Equal("ok", report.RootElement.GetProperty("status").GetString());
            Assert.Equal(save.EngineStateHash, report.RootElement.GetProperty("engineStateHash").GetString());
            Assert.DoesNotContain("snapshot", output.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("knowledge", output.ToString(), StringComparison.OrdinalIgnoreCase);
            await File.WriteAllTextAsync(path, EngineJson.Serialize(save with { IntegrityHash = new string('0', 64) }));
            output.GetStringBuilder().Clear();
            Assert.Equal(1, CliCommands.Run(["save-validate", path, "--json"], output, error));
            Assert.Contains("checksum", output.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData()]
    [InlineData("missing", "--unknown")]
    public void InvalidArgumentsFail(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(1, CliCommands.Run(["save-validate", .. arguments], output, error));
        Assert.Contains("save-validate", error.ToString());
    }
}
