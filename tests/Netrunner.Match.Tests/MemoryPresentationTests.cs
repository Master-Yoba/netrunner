using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class MemoryPresentationTests
{
    [Fact]
    public void MapsAuthoritativeRunnerMemoryAndPreservesLegacyNulls()
    {
        Observation legacy = ObservationBuilder.Build(
            GameState.CreateNew(seed: 8103),
            PlayerSide.Runner);
        MatchViewModel legacyView = ObservationViewMapper.Build(legacy);

        Assert.Null(legacyView.Runner.UsedMemory);
        Assert.Null(legacyView.Runner.MemoryLimit);

        Dictionary<string, object?> publicState = new(legacy.PublicState);
        IReadOnlyDictionary<string, object?> originalPlayers = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            publicState["players"]);
        var players = originalPlayers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        IReadOnlyDictionary<string, object?> originalRunner = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            players["runner"]);
        var runner = new Dictionary<string, object?>(originalRunner, StringComparer.Ordinal)
        {
            ["usedMemory"] = 0,
            ["memoryLimit"] = 4,
        };
        players["runner"] = runner;
        publicState["players"] = players;

        MatchViewModel view = ObservationViewMapper.Build(legacy with { PublicState = publicState });

        Assert.Equal(0, view.Runner.UsedMemory);
        Assert.Equal(4, view.Runner.MemoryLimit);
        Assert.Null(view.Corp.UsedMemory);
        Assert.Null(view.Corp.MemoryLimit);
    }
}
