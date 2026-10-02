using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>D-EL-8: a game's format chooses its content, and its save records and reloads that format.</summary>
public sealed class PlayFormatsTests
{
    private static bool ElevationAvailable => PlayFormats.IsAvailable(CardFormatIds.GatewayElevation);

    [Fact]
    public void PairingsAndCatalogsMapToTheirFormat()
    {
        Assert.Equal(CardFormatIds.Gateway, PlayFormats.FormatOfPresets("gateway-advanced-yomi", "gateway-stolen-goods"));
        Assert.Equal(CardFormatIds.GatewayElevation, PlayFormats.FormatOfPresets("elevation-leo-bioroid-wall", "gateway-stolen-goods"));
        Assert.Equal(CardFormatIds.Gateway, PlayFormats.FormatOfPreset("elevation-sim-leo-corp"));
        Assert.Equal(CardFormatIds.GatewayElevation, PlayFormats.FormatOfBehavior(ElevationBehaviorCatalog.Create()));
        Assert.Equal(CardFormatIds.Gateway, PlayFormats.FormatOfBehavior(GatewayBehaviorCatalog.Create()));
        Assert.True(PlayFormats.TryGetPreset("elevation-topan-street-gang", out OfficialPreset topan));
        Assert.Equal("35002", topan.IdentityCode);
        Assert.False(PlayFormats.TryGetPreset("elevation-sim-leo-corp", out _));
    }

    [Fact]
    public void EachFormatOffersOnlyItsOwnDecks()
    {
        if (!ElevationAvailable) return;
        PlayFormatContent gateway = PlayFormats.Get(CardFormatIds.Gateway);
        PlayFormatContent elevation = PlayFormats.Get(CardFormatIds.GatewayElevation);

        Assert.Equal(7, gateway.Decks.Count);
        Assert.Equal(14, elevation.Decks.Count);
        Assert.All(elevation.Decks, deck => Assert.True(OfficialPresetCatalog.IsElevationDeck(deck.PresetId)));
        Assert.Same(OfficialPresetCatalog.Embedded(), gateway.Presets);
        Assert.Equal(GatewayBehaviorCatalog.Create().Fingerprint, gateway.Behavior.Fingerprint);
        Assert.Equal(ElevationBehaviorCatalog.Version, elevation.Behavior.Version);
    }

    [Fact]
    public async Task TheFormatsDeckPoolIsFuzzAndLadderContent()
    {
        Assert.Equal(48, RulesFuzzRunner.ContentIds("gateway-elevation").Count);
        Assert.Equal("gateway-elevation-v1", RulesFuzzRunner.PoolVersion("gateway-elevation"));
        if (!ElevationAvailable) return;
        string content = RulesFuzzRunner.ContentIds("gateway-elevation")[0];

        FuzzGameResult result = await RulesFuzzRunner.RunAsync(
            new FuzzGameCase("pool", content, 11, FuzzAgentKind.Heuristic, FuzzAgentKind.Heuristic));

        Assert.False(result.IsDefect, $"{content}: {result.Status} {result.Message}");
        Assert.Equal(ElevationBehaviorCatalog.Version, RulesFuzzRunner.CreateDefinition(content, 11).Behavior.Version);
    }

    [Theory]
    [InlineData(PlayerSide.Runner)]
    [InlineData(PlayerSide.Corp)]
    public async Task AnElevationSaveRecordsItsFormatAndLoadsWithItsContent(PlayerSide human)
    {
        if (!ElevationAvailable) return;
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            PlayFormatContent content = PlayFormats.Get(CardFormatIds.GatewayElevation);
            FullMatchDefinition definition = content.Compose(
                content.ComposeOfficial("elevation-synapse-fast-news", "elevation-magdalene-cryptarchitect", 31));
            await using var service = new SoloSessionService(PlayFormats.SaveContent, directory);
            await using var original = new MatchCoordinator(definition, OfflineMatchFactory.CreateOpponent(definition, human),
                new MatchCoordinatorOptions { HumanSide = human, AutoPassPriority = true });
            service.SetActiveSession(original);
            await original.StartAsync();
            await original.WaitForHumanDecisionAsync();

            SoloSaveReceipt receipt = await service.SaveAsync(original, "An Elevation game");

            Assert.Equal(CardFormatIds.GatewayElevation, receipt.Preview.FormatId);
            IReadOnlyList<SoloSaveBrowserEntry> entries = await service.BrowseAsync();
            Assert.Equal(2, entries.Count);
            Assert.All(entries, entry => Assert.True(entry.IsCompatible, entry.Error));
            Assert.All(entries, entry => Assert.Equal(CardFormatIds.GatewayElevation, entry.Preview!.FormatId));
            LoadedSoloSession loaded = await service.LoadAsync(receipt.Preview.SaveId);
            await using MatchCoordinator restored = loaded.Coordinator;
            Assert.Equal(receipt.ActionCount, restored.CurrentSnapshot!.ActionCount);

            // A Gateway-only reader can't validate an Elevation save: it is listed as incompatible.
            PlayFormatContent gateway = PlayFormats.Get(CardFormatIds.Gateway);
            using var gatewayStore = new SoloSaveStore(gateway.Behavior, gateway.Runtime, directory);
            Assert.All(await gatewayStore.BrowseAsync(), entry => Assert.False(entry.IsCompatible));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task AGatewaySaveRecordsTheGatewayFormat()
    {
        if (!PlayFormats.IsAvailable(CardFormatIds.Gateway)) return;
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            PlayFormatContent content = PlayFormats.Get(CardFormatIds.Gateway);
            FullMatchDefinition definition = content.Compose(content.ComposeOfficial("gateway-advanced-yomi", "gateway-stolen-goods", 5));
            await using var service = new SoloSessionService(PlayFormats.SaveContent, directory);
            await using var original = new MatchCoordinator(definition, OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Runner),
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Runner, AutoPassPriority = true });
            service.SetActiveSession(original);
            await original.StartAsync();
            await original.WaitForHumanDecisionAsync();

            SoloSaveReceipt receipt = await service.SaveAsync(original, "A Gateway game");

            Assert.Equal(CardFormatIds.Gateway, receipt.Preview.FormatId);
            LoadedSoloSession loaded = await service.LoadAsync(receipt.Preview.SaveId);
            await using MatchCoordinator restored = loaded.Coordinator;
            Assert.Equal(receipt.ActionCount, restored.CurrentSnapshot!.ActionCount);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
