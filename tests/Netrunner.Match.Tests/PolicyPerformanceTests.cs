using System.Collections.Immutable;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class PolicyPerformanceTests
{
    [Fact]
    public async Task DirectTimingPreservesPolicyActionsAndRecordsEveryDecisionWithoutPrivateState()
    {
        DeveloperMatchCards cards = DeveloperMatchCards.Create();
        var runner = new AiBenchmarkRunner(cards.Data, cards.Behavior);
        AiBenchmarkManifest seed = BenchmarkSuites.CreateContract(runner, "developer");
        AiBenchmarkManifest manifest = seed with
        {
            Games = seed.Games.Take(2).Select(game => game with
            {
                Corp = BenchmarkAgentSpec.Offline(CorpPolicyProfiles.Create("standard", game.Corp.PolicySeed)),
                Runner = BenchmarkAgentSpec.Offline(RunnerPolicyProfiles.Create("standard", game.Runner.PolicySeed)),
            }).ToImmutableArray(),
        };
        string directory = Path.Combine(Path.GetTempPath(), "netrunner-performance-" + Guid.NewGuid().ToString("N"));
        try
        {
            AiBenchmarkReport control = await runner.RunAsync(manifest, directory + "-control");
            PolicyPerformanceReport performance = await PolicyPerformanceRunner.RunAsync(cards.Data, cards.Behavior,
                manifest, directory, "test fixture", "test");
            AiBenchmarkReport measured = EngineJson.Deserialize<AiBenchmarkReport>(await File.ReadAllTextAsync(Path.Combine(directory, "report.json")));
            Assert.Equal(2, measured.Aggregate.Terminal);
            Assert.Equal(control.Games.Select(game => game.Result.ReplayHash), measured.Games.Select(game => game.Result.ReplayHash));
            Assert.Equal(measured.Games.Sum(game => game.Result.Actions), performance.Samples.Length);
            Assert.Equal(performance.Samples.Length, performance.Groups.Sum(group => group.Samples));
            Assert.False(performance.DiagnosticsEnabled);
            Assert.All(performance.Samples, sample =>
            {
                Assert.True(sample.LegalActions > 0);
                Assert.True(sample.Milliseconds >= 0 && double.IsFinite(sample.Milliseconds));
            });
            string serialized = EngineJson.Serialize(performance);
            Assert.DoesNotContain("knowledge", serialized, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("definitionId", serialized, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            if (Directory.Exists(directory + "-control")) Directory.Delete(directory + "-control", recursive: true);
        }
    }

    [Fact]
    public void PercentilesUseEverySampleAndKeepSeatsAndTacticalDecisionsSeparate()
    {
        PolicyTimingSample[] samples = [
            .. Enumerable.Range(1, 20).Select(index => new PolicyTimingSample(PlayerSide.Corp, DecisionKind.TurnAction, index * 2, index)),
            new(PlayerSide.Corp, DecisionKind.MemoryCleanup, 3, 80),
            new(PlayerSide.Runner, DecisionKind.TurnAction, 25, 30),
        ];
        ImmutableArray<PolicyTimingGroup> groups = PolicyPerformanceRunner.Summarize(samples);
        Assert.Equal(3, groups.Length);
        PolicyTimingGroup ordinary = groups.Single(group => group.Side == PlayerSide.Corp && group.Category == "ordinary");
        Assert.Equal(20, ordinary.Samples);
        Assert.Equal(10, ordinary.MedianMilliseconds);
        Assert.Equal(19, ordinary.P95Milliseconds);
        Assert.Equal(38, ordinary.P95LegalActions);
        Assert.Equal(40, ordinary.MaximumLegalActions);
        Assert.Equal(50, ordinary.BudgetMilliseconds);
        Assert.Equal(200, groups.Single(group => group.Category == "tactical").BudgetMilliseconds);
    }
}
