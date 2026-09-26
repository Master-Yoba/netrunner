using System.Collections.Immutable;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class PolicyDecisionLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "netrunner-policy-log-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OptInRecordsEveryChoiceWithoutChangingReplaysOrLeakingScoresIntoReports()
    {
        (AiBenchmarkRunner runner, AiBenchmarkManifest manifest) = Create(2);
        AiBenchmarkReport normal = await runner.RunAsync(manifest, Path.Combine(_directory, "normal"));
        AiBenchmarkReport logged = await runner.RunAsync(manifest, Path.Combine(_directory, "logged"), workers: 2,
            buildRevision: "diagnostic-test", writePolicyDiagnostics: true);
        Assert.Equal(normal.DeterministicDigest, logged.DeterministicDigest);
        Assert.Equal(2, logged.Aggregate.Terminal);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_directory, "normal"), "*.jsonl", SearchOption.AllDirectories));
        Assert.DoesNotContain("\"candidates\":", EngineJson.Serialize(logged), StringComparison.Ordinal);
        foreach (BenchmarkGameResult game in logged.Games)
        {
            string path = Path.Combine(_directory, "logged", game.Game.Id);
            PolicyDecisionDiagnostic[] records = (await File.ReadAllLinesAsync(Path.Combine(path, "policy-decisions.jsonl")))
                .Select(EngineJson.Deserialize<PolicyDecisionDiagnostic>).ToArray();
            FullMatchDefinition definition = runner.Compose(manifest.ContentMode, game.Game.CorpPreset, game.Game.RunnerPreset, game.Game.GameSeed);
            ReplayArtifact replay = EngineJson.Deserialize<ReplayArtifact>(await File.ReadAllTextAsync(Path.Combine(path, "replay.json")),
                definition.Behavior, definition.Runtime);
            Assert.Equal(game.Result.Actions, records.Length);
            for (int index = 0; index < records.Length; index++)
            {
                PolicyDecisionDiagnostic record = records[index];
                ReplayAction action = replay.Actions[index];
                Assert.Equal(1, record.SchemaVersion);
                Assert.Equal("private-policy-decision", record.ArtifactKind);
                Assert.Equal("selected-before-apply", record.Stage);
                Assert.Equal(manifest.SuiteId, record.SuiteId);
                Assert.Equal(game.Game.Id, record.GameId);
                Assert.Equal(logged.ManifestHash, record.ManifestHash);
                Assert.Equal(logged.Pins, record.Pins);
                Assert.Equal("diagnostic-test", record.BuildRevision);
                Assert.Equal(action.Sequence - 1, record.CommittedActionSequence);
                Assert.Equal(action.Submission.DecisionId, record.DecisionId);
                Assert.Equal(action.Submission.Revision, record.DecisionRevision);
                Assert.Equal(action.Submission.ActionId, record.Evaluation.SelectedAction);
                Assert.Contains(record.Evaluation.Candidates, candidate => candidate.ActionId == record.Evaluation.SelectedAction);
                Assert.All(record.Evaluation.Candidates, candidate => Assert.False(string.IsNullOrWhiteSpace(candidate.Utility.Reason)));
                Assert.True(DeterministicHash.IsSha256Hex(record.Evaluation.ObservationDigest));
                Assert.True(DeterministicHash.IsSha256Hex(record.Evaluation.CheckpointDigest));
                Assert.True(double.IsFinite(record.AgentCallMilliseconds) && record.AgentCallMilliseconds >= 0);
                Assert.True(record.Evaluation.WorkUsed > 0);
                record.Configuration.Validate();
                Assert.Equal(record.Side, record.Configuration.Side);
                Assert.Equal(EngineJson.Serialize(record.Side == PlayerSide.Corp ? game.Game.Corp.Configuration : game.Game.Runner.Configuration),
                    EngineJson.Serialize(record.Configuration));
            }
        }
    }

    [Fact]
    public async Task FailedCappedAttemptKeepsItsChosenDecisionLogAndVerifiedReplay()
    {
        (AiBenchmarkRunner runner, AiBenchmarkManifest manifest) = Create(1);
        manifest = manifest with { Limits = new BenchmarkLimits(Actions: 1) };
        AiBenchmarkReport report = await runner.RunAsync(manifest, _directory, writePolicyDiagnostics: true);
        BenchmarkGameResult game = Assert.Single(report.Games);
        Assert.Equal(BenchmarkTermination.ActionCap, game.Result.Termination);
        Assert.True(game.Result.ReplayVerified);
        Assert.Single(await File.ReadAllLinesAsync(Path.Combine(_directory, game.Game.Id, "policy-decisions.jsonl")));
        Assert.True(File.Exists(Path.Combine(_directory, game.FailurePath!)));
    }

    [Fact]
    public async Task CancelledRunCannotInventChoiceRecordsOrAcceptActions()
    {
        (AiBenchmarkRunner runner, AiBenchmarkManifest manifest) = Create(2);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        AiBenchmarkReport report = await runner.RunAsync(manifest, _directory, writePolicyDiagnostics: true,
            cancellationToken: cancellation.Token);
        Assert.Equal(2, report.Games.Length);
        foreach (BenchmarkGameResult game in report.Games)
        {
            Assert.Equal(BenchmarkTermination.Cancelled, game.Result.Termination);
            Assert.Equal(0, game.Result.Actions);
            Assert.True(game.Result.ReplayVerified);
            Assert.Empty(await File.ReadAllLinesAsync(Path.Combine(_directory, game.Game.Id, "policy-decisions.jsonl")));
        }
    }

    [Fact]
    public async Task ContractOnlyManifestCannotPretendToProvidePolicyScores()
    {
        DeveloperMatchCards cards = DeveloperMatchCards.Create();
        var runner = new AiBenchmarkRunner(cards.Data, cards.Behavior);
        AiBenchmarkManifest manifest = BenchmarkSuites.CreateContract(runner, "developer");
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(manifest, _directory, writePolicyDiagnostics: true));
        Assert.False(Directory.Exists(_directory));
    }

    private static (AiBenchmarkRunner Runner, AiBenchmarkManifest Manifest) Create(int count)
    {
        DeveloperMatchCards cards = DeveloperMatchCards.Create();
        var runner = new AiBenchmarkRunner(cards.Data, cards.Behavior);
        AiBenchmarkManifest manifest = BenchmarkSuites.CreateContract(runner, "developer");
        return (runner, manifest with
        {
            Games = manifest.Games.Take(count).Select(game => game with
            {
                Corp = BenchmarkAgentSpec.Offline(CorpPolicyProfiles.Create("standard", game.Corp.PolicySeed)),
                Runner = BenchmarkAgentSpec.Offline(RunnerPolicyProfiles.Create("standard", game.Runner.PolicySeed)),
            }).ToImmutableArray(),
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
