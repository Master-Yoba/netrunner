using System.Collections.Concurrent;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class MatchHistoryTests
{
    [Fact]
    public void HistoryRetentionDoesNotDependOnVisiblePageOrRepeatedPublication()
    {
        var store = new MatchHistoryStore();
        var entries = Enumerable.Range(1, 2000).Select(i => new MatchHistoryEntry(
            $"action-{i}", i, 1, PlayerSide.Corp, $"Action {i}", [])).ToArray();
        store.Append(entries);
        store.Append(entries);
        Assert.Equal(2000, store.Entries.Count);
        Assert.Equal("Action 1", store.Entries[0].Summary);
        Assert.Equal("Action 2000", store.Entries[^1].Summary);
        store.Clear();
        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task OfficialNicoOpeningExplainsTwoReplacementsWithoutNamingHiddenInstall()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Netrunner.sln"))) root = root.Parent;
        Assert.NotNull(root);
        if (!File.Exists(Path.Combine(root.FullName, "data/vendor/system-gateway/cards.json"))) return;
        CardDataSet data = CardDataLoader.LoadSystemGateway(root.FullName);
        BehaviorCatalog behavior = GatewayBehaviorCatalog.Create();
        MatchRequest request = MatchRequestComposer.ComposeOfficial("system-gateway-basic-corp",
            "system-gateway-basic-runner", data, 47042, OfficialPresetCatalog.Embedded(), behaviorCatalog: behavior);
        FullMatchDefinition definition = FullMatchDefinition.FromRequest(request, data, behavior);
        await using var coordinator = new MatchCoordinator(definition,
            new FullGameContractAgent(new FullGameContractPolicy(PlayerSide.Corp), definition.Runtime),
            new MatchCoordinatorOptions { HumanSide = PlayerSide.Runner });
        var publications = new ConcurrentQueue<MatchSnapshot>();
        coordinator.SnapshotPublished += publications.Enqueue;
        await coordinator.StartAsync();
        for (int i = 0; i < 12; i++)
        {
            Observation observation = await coordinator.GetObservationAsync(PlayerSide.Runner).WaitAsync(TimeSpan.FromSeconds(5));
            if (coordinator.CurrentSnapshot!.View.Turn.ActiveSide == PlayerSide.Runner) break;
            LegalAction action = observation.LegalActions.First(a => a.Kind == ActionKind.PassPriority
                || a.Parameters is NumberChoiceParameters { Value: 0 });
            Assert.True((await coordinator.SubmitHumanActionAsync(action.Id)).Accepted);
        }
        MatchHistoryEntry[] entries = publications.SelectMany(s => s.History).ToArray();
        MatchHistoryEntry[] replacements = entries.Where(e => e.Summary.Contains("replaces a card", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, replacements.Length);
        Assert.All(replacements, e =>
        {
            Assert.Contains("Nico Campaign → Archives", e.Summary);
            Assert.Contains("installs a face-down card", e.Summary);
            Assert.All(e.Cards, card => Assert.Equal("Nico Campaign", card.Title));
        });
        Assert.Equal(2, entries.Count(e => e.Summary.Contains("rezzes Nico Campaign", StringComparison.Ordinal)));
        MatchHistoryEntry firstInstall = entries.First(e => e.Summary.Contains("installs", StringComparison.Ordinal));
        Assert.Contains("a face-down card", firstInstall.Summary);
        Assert.Empty(firstInstall.Cards); // A later rez must not rewrite an earlier entry.
        Assert.All(publications.Where(s => s.IsOpponentThinking), s => Assert.Empty(s.History));
    }

    [Theory]
    [InlineData(PlayerSide.Runner)]
    [InlineData(PlayerSide.Corp)]
    public async Task CoordinatorPublishesHistoryOnlyForAcceptedActionsAndNeverRawCardIds(PlayerSide human)
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
        PlayerSide other = human == PlayerSide.Runner ? PlayerSide.Corp : PlayerSide.Runner;
        await using var coordinator = new MatchCoordinator(definition,
            new FullGameContractAgent(new FullGameContractPolicy(other), definition.Runtime),
            new MatchCoordinatorOptions { HumanSide = human });
        var publications = new ConcurrentQueue<MatchSnapshot>();
        coordinator.SnapshotPublished += publications.Enqueue;
        await coordinator.StartAsync();
        var policy = new FullGameContractAgent(new FullGameContractPolicy(human), definition.Runtime);
        for (int i = 0; i < 25; i++)
        {
            Observation observation = await coordinator.GetObservationAsync(human).WaitAsync(TimeSpan.FromSeconds(5));
            if (coordinator.CurrentSnapshot!.IsTerminal) break;
            ActionId id = await policy.ChooseActionAsync(observation);
            Assert.True((await coordinator.SubmitHumanActionAsync(id)).Accepted);
        }
        MatchSnapshot[] snapshots = publications.ToArray();
        Assert.All(snapshots.Where(s => s.IsOpponentThinking), s => Assert.Empty(s.History));
        MatchHistoryEntry[] entries = snapshots.SelectMany(s => s.History).ToArray();
        Assert.NotEmpty(entries);
        Assert.Equal(entries.Length, entries.Select(e => e.Key).Distinct().Count());
        string serialized = EngineJson.Serialize(entries);
        Assert.DoesNotContain("card:", serialized);
        Assert.DoesNotContain("zone:", serialized);
        Assert.DoesNotContain("server:", serialized);
        Assert.Contains(entries, e => e.Summary.Contains("passes priority", StringComparison.Ordinal));
    }
}
