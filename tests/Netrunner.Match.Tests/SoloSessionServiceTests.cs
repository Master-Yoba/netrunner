using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class SoloSessionServiceTests
{
    [Theory]
    [InlineData(PlayerSide.Corp, "standard")]
    [InlineData(PlayerSide.Runner, "standard")]
    [InlineData(PlayerSide.Corp, "easy")]
    [InlineData(PlayerSide.Runner, "easy")]
    public async Task ClientFacadeRestartsPausedWithExactProfileAndIndependentHistory(PlayerSide human, string difficulty)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer(67);
            await using var service = new SoloSessionService(definition.Behavior, definition.Runtime, _ => definition, directory);
            await using var original = new MatchCoordinator(definition, OfflineMatchFactory.CreateOpponent(definition, human, difficulty),
                new MatchCoordinatorOptions { HumanSide = human, AutoPassPriority = true });
            service.SetActiveSession(original);
            await original.StartAsync();
            await original.WaitForHumanDecisionAsync();
            SoloSaveReceipt receipt = await service.SaveAsync(original, "A named game");
            Assert.True(original.CurrentSnapshot!.IsPaused);
            Assert.Equal(difficulty, receipt.Preview.Difficulty);
            Assert.Equal(human, receipt.Preview.HumanSide);
            IReadOnlyList<SoloSaveBrowserEntry> entries = await service.BrowseAsync();
            Assert.Equal(2, entries.Count);
            Assert.All(entries, entry => Assert.True(entry.IsCompatible));
            LoadedSoloSession loaded = await service.LoadAsync(receipt.Preview.SaveId);
            await using MatchCoordinator restored = loaded.Coordinator;
            Assert.True(restored.CurrentSnapshot!.IsPaused);
            Assert.Empty(restored.CurrentSnapshot.Events);
            Assert.Equal(receipt.ActionCount, restored.CurrentSnapshot.ActionCount);
            service.SetActiveSession(restored);
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.SaveAsync(original, "Stale game"));
            await original.ContinueAsync();
            await restored.ContinueAsync();
            MatchSnapshot left = await original.WaitForHumanDecisionAsync();
            MatchSnapshot right = await restored.WaitForHumanDecisionAsync();
            Assert.Equal(EngineJson.Serialize(left.Observation), EngineJson.Serialize(right.Observation));
            Assert.Equal((await original.CaptureSaveAsync("left")).PolicyStateHash,
                (await restored.CaptureSaveAsync("right")).PolicyStateHash);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task RapidAutoSaveRequestsAndSessionSwitchLeaveNewestSessionAndVerifiedBackup()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            FullMatchDefinition firstDefinition = FullMatchDefinition.Developer(11);
            FullMatchDefinition nextDefinition = FullMatchDefinition.Developer(12);
            await using var service = new SoloSessionService(firstDefinition.Behavior, firstDefinition.Runtime,
                request => FullMatchDefinition.Developer(request.Seed), directory);
            await using var first = new MatchCoordinator(firstDefinition, OfflineMatchFactory.CreateOpponent(firstDefinition, PlayerSide.Corp),
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
            await using var next = new MatchCoordinator(nextDefinition, OfflineMatchFactory.CreateOpponent(nextDefinition, PlayerSide.Corp),
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
            await first.StartAsync();
            await next.StartAsync();
            service.SetActiveSession(first);
            await service.RequestAutoSaveAsync(first);
            Task[] requests = Enumerable.Range(0, 100).Select(_ => service.RequestAutoSaveAsync(first)).ToArray();
            service.SetActiveSession(next);
            await service.RequestAutoSaveAsync(next);
            await Task.WhenAll(requests);
            SoloSaveBrowserEntry entry = Assert.Single(await service.BrowseAsync());
            Assert.Equal(12UL, entry.Preview!.Seed);
            Assert.False(next.CurrentSnapshot!.IsPaused);
            SoloSaveDocument backup = SoloSaveStore.Read(Path.Combine(directory, SoloSessionService.AutoSaveId + ".solo.json.bak"));
            backup.Validate(firstDefinition.Behavior, firstDefinition.Runtime);
            Assert.Null(service.AutoSaveError);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task FailedSaveKeepsActiveMatchUsableAndCorruptLoadDoesNotReplaceIt()
    {
        string file = Path.GetTempFileName();
        try
        {
            FullMatchDefinition definition = FullMatchDefinition.Developer();
            await using var service = new SoloSessionService(definition.Behavior, definition.Runtime, _ => definition, file);
            await using var coordinator = new MatchCoordinator(definition, OfflineMatchFactory.CreateOpponent(definition, PlayerSide.Corp),
                new MatchCoordinatorOptions { HumanSide = PlayerSide.Corp });
            service.SetActiveSession(coordinator);
            await coordinator.StartAsync();
            await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(coordinator, "No directory"));
            await Assert.ThrowsAnyAsync<IOException>(() => service.LoadAsync(Guid.NewGuid().ToString("N")));
            await coordinator.ContinueAsync();
            MatchSnapshot ready = await coordinator.WaitForHumanDecisionAsync();
            Assert.True((await coordinator.SubmitHumanActionAsync(ready.Observation.LegalActions[0].Id)).Accepted);
        }
        finally { File.Delete(file); }
    }
}
