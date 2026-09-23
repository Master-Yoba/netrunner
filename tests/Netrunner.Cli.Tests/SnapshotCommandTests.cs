using System.Text.Json;
using Netrunner.Core;
using Netrunner.Match;
using Xunit;

namespace Netrunner.Cli.Tests;

public sealed class SnapshotCommandTests
{
    [Fact]
    public async Task RawSnapshotImportsStepsExportsAndReopensWithReplayWithoutSetup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "netrunner-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(71);
            await using var coordinator = new MatchCoordinator(definition, OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp),
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
            await coordinator.StartAsync();
            SoloSaveDocument save = await coordinator.CaptureSaveAsync("Source");
            string source = Path.Combine(directory, "raw.json");
            string destination = Path.Combine(directory, "developer.json");
            await File.WriteAllTextAsync(source, EngineJson.Serialize(save.Payload.Snapshot));
            using var output = new StringWriter();
            using var error = new StringWriter();
            Assert.Equal(0, CliCommands.Run(["snapshot", source, "--synthetic", "--side", "corp", "--step", "--output", destination, "--json"], output, error));
            Assert.Empty(error.ToString());
            using JsonDocument report = JsonDocument.Parse(output.ToString());
            Assert.Equal(1, report.RootElement.GetProperty("acceptedActions").GetInt32());
            Assert.DoesNotContain("cards", output.ToString(), StringComparison.OrdinalIgnoreCase);
            DeveloperSnapshotDocument document = DeveloperSnapshotStore.Read(destination, PlayerSide.Runner, definition.Behavior, definition.Runtime);
            Assert.Equal(PlayerSide.Corp, document.HumanSide);
            Assert.Equal(DeveloperSnapshotDocument.ImportedRoot, document.Origin);
            Assert.Single(document.Replay.Actions);
            Assert.Equal(EngineJson.Serialize(save.Payload.Snapshot), await File.ReadAllTextAsync(source));
            output.GetStringBuilder().Clear();
            Assert.Equal(0, CliCommands.Run(["snapshot", destination, "--synthetic", "--until", "turn:1", "--max-actions", "150", "--output", destination, "--json"], output, error));
            document = DeveloperSnapshotStore.Read(destination, PlayerSide.Runner, definition.Behavior, definition.Runtime);
            Assert.Equal(1, document.Snapshot.Turn.TurnNumber);
            string valid = await File.ReadAllTextAsync(destination);
            await File.WriteAllTextAsync(source, EngineJson.Serialize(document with { IntegrityHash = new string('0', 64) }));
            Assert.Equal(2, CliCommands.Run(["snapshot", source, "--synthetic", "--step", "--output", destination], output, error));
            Assert.Equal(valid, await File.ReadAllTextAsync(destination));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData()]
    [InlineData("missing", "--step")]
    [InlineData("missing", "--max-actions", "0")]
    [InlineData("missing", "--max-actions", "garbage")]
    [InlineData("missing", "--json", "--json")]
    [InlineData("missing", "--side", "spectator")]
    [InlineData("missing", "--unknown")]
    public void InvalidArgumentsFail(params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, CliCommands.Run(["snapshot", .. arguments], output, error));
    }
}
