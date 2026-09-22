using System.Collections.Immutable;
using System.Text.Json;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class AiBenchmarkTests
{
    [Fact]
    public async Task WorkerCountDoesNotChangeOrderedResultsOrAcceptedReplay()
    {
        using var files = new OutputDirectory();
        AiBenchmarkRunner runner = CreateRunner();
        AiBenchmarkManifest manifest = Manifest(runner, count: 4);
        AiBenchmarkReport serial = await runner.RunAsync(manifest, Path.Combine(files.Path, "serial"));
        AiBenchmarkReport parallel = await runner.RunAsync(manifest, Path.Combine(files.Path, "parallel"), workers: 3);
        Assert.Equal(serial.DeterministicDigest, parallel.DeterministicDigest);
        Assert.Equal(serial.Games.Select(game => game.Result.ReplayHash), parallel.Games.Select(game => game.Result.ReplayHash));
        Assert.Equal(manifest.Games.Select(game => game.Id).Order(StringComparer.Ordinal), parallel.Games.Select(game => game.Game.Id));
        Assert.Equal(4, serial.Aggregate.Terminal);
        Assert.All(serial.Games, game => Assert.True(game.Result.ReplayVerified));
        foreach (BenchmarkGameResult game in serial.Games)
        {
            FullMatchResult legacy = await FullMatchRunner.RunAsync(FullMatchDefinition.Developer(game.Game.GameSeed));
            Assert.Equal(legacy.FinalStateHash, game.Result.FinalStateHash);
            string replay = File.ReadAllText(Path.Combine(files.Path, "serial", game.ReplayPath));
            Assert.Equal(EngineJson.Serialize(legacy.Replay), replay);
        }
        AiBenchmarkReport roundTrip = EngineJson.Deserialize<AiBenchmarkReport>(EngineJson.Serialize(serial));
        Assert.Equal(serial.DeterministicDigest, roundTrip.DeterministicDigest);
        Assert.Null(serial.Games[0].Result.Corp.Fallbacks); // baseline supplies no fallback diagnostics
    }

    [Theory]
    [InlineData("illegal", BenchmarkTermination.IllegalAction)]
    [InlineData("exception", BenchmarkTermination.AgentException)]
    [InlineData("hang", BenchmarkTermination.WatchdogTimeout)]
    public async Task FaultyAgentsRetainVerifiedPrefixesAndFailureEvidence(string fault, BenchmarkTermination expected)
    {
        using var files = new OutputDirectory();
        using var blocked = new ManualResetEventSlim(false);
        AiBenchmarkRunner runner = CreateRunner(new FaultFactory(fault, blocked));
        AiBenchmarkManifest manifest = Manifest(runner) with { Limits = new BenchmarkLimits(WatchdogSeconds: 1) };
        try
        {
            AiBenchmarkReport report = await runner.RunAsync(manifest, Path.Combine(files.Path, "failure"));
            BenchmarkGameResult game = Assert.Single(report.Games);
            Assert.Equal(expected, game.Result.Termination);
            Assert.Equal(0, game.Result.Actions);
            Assert.True(game.Result.ReplayVerified);
            Assert.Equal(1, report.Aggregate.Failures);
            Assert.True(File.Exists(Path.Combine(files.Path, "failure", game.ReplayPath)));
            using JsonDocument failure = JsonDocument.Parse(File.ReadAllText(Path.Combine(files.Path, "failure", game.FailurePath!)));
            Assert.Equal("private-benchmark-failure", failure.RootElement.GetProperty("artifactKind").GetString());
            Assert.Equal(0, failure.RootElement.GetProperty("snapshot").GetProperty("actionSequence").GetInt64());
            Assert.Equal(1, failure.RootElement.GetProperty("recentDecisions").GetArrayLength());
        }
        finally { blocked.Set(); }
    }

    [Theory]
    [InlineData(true, BenchmarkTermination.ActionCap)]
    [InlineData(false, BenchmarkTermination.TurnCap)]
    public async Task DeclaredCapsHaveDistinctOutcomesAndVerifiedReplayPrefixes(bool actionCap, BenchmarkTermination expected)
    {
        using var files = new OutputDirectory();
        AiBenchmarkRunner runner = CreateRunner();
        AiBenchmarkManifest manifest = Manifest(runner) with
        {
            Limits = actionCap ? new BenchmarkLimits(Actions: 1) : new BenchmarkLimits(TurnPairs: 1),
        };
        BenchmarkGameResult game = Assert.Single((await runner.RunAsync(manifest, Path.Combine(files.Path, "cap"))).Games);
        Assert.Equal(expected, game.Result.Termination);
        Assert.True(game.Result.ReplayVerified);
        if (actionCap) Assert.Equal(1, game.Result.Actions);
        else Assert.Equal(1, game.Result.CompletedTurnPairs);
        Assert.NotNull(game.FailurePath);
    }

    [Fact]
    public async Task CancelledRowsRemainInTheDenominatorAndExistingEvidenceCannotBeOverwritten()
    {
        using var files = new OutputDirectory();
        AiBenchmarkRunner runner = CreateRunner();
        AiBenchmarkManifest manifest = Manifest(runner, count: 3);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        string output = Path.Combine(files.Path, "cancelled");
        AiBenchmarkReport report = await runner.RunAsync(manifest, output, cancellationToken: cancel.Token);
        Assert.Equal(3, report.Aggregate.Games);
        Assert.All(report.Games, game =>
        {
            Assert.Equal(BenchmarkTermination.Cancelled, game.Result.Termination);
            Assert.True(game.Result.ReplayVerified);
        });
        await Assert.ThrowsAsync<IOException>(() => runner.RunAsync(manifest, output));
    }

    [Fact]
    public async Task PinDeckPairingAndUnknownPolicyFailuresAreExplicitBeforeExecution()
    {
        using var files = new OutputDirectory();
        AiBenchmarkRunner runner = CreateRunner();
        AiBenchmarkManifest manifest = Manifest(runner);
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(manifest with
        {
            Pins = manifest.Pins with { RulesVersion = "wrong" },
        }, Path.Combine(files.Path, "pins")));
        BenchmarkGameSpec row = manifest.Games[0];
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(manifest with
        {
            Games = [row with { CorpDeckHash = new string('0', 64) }],
        }, Path.Combine(files.Path, "deck")));
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(manifest with
        {
            Games = [row with { Corp = row.Corp with { Kind = "not-installed" } }],
        }, Path.Combine(files.Path, "policy")));
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(manifest with
        {
            Games = [row with { RunnerPreset = "unsupported" }],
        }, Path.Combine(files.Path, "pairing")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(files.Path));
    }

    [Fact]
    public void ProgressIgnoresAllocatorAndDecisionChurnButTracksResourcesAndPhase()
    {
        GameState first = GameState.CreateNew(42);
        JsonObjectMutation mutation = new(first);
        mutation.Root["eventSequence"] = 50;
        mutation.Root["actionSequence"] = 50;
        mutation.Root["decisionRevision"] = 50;
        mutation.Root["randomState"] = 999;
        // This is a fingerprint fixture, not a Core-executable tactical case.
        GameState changed = JsonSerializer.Deserialize<GameState>(mutation.Root.ToJsonString(), EngineJson.Options)!;
        Assert.Equal(BenchmarkProgressDetector.Fingerprint(first), BenchmarkProgressDetector.Fingerprint(changed));
        mutation.Root["players"]![0]!["credits"] = 99;
        GameState resources = JsonSerializer.Deserialize<GameState>(mutation.Root.ToJsonString(), EngineJson.Options)!;
        Assert.NotEqual(BenchmarkProgressDetector.Fingerprint(first), BenchmarkProgressDetector.Fingerprint(resources));
        var detector = new BenchmarkProgressDetector(new BenchmarkLimits(RepetitionCount: 3, ProgressWindow: 4));
        Assert.False(detector.Observe(first));
        Assert.False(detector.Observe(changed));
        Assert.True(detector.Observe(first));
    }

    private static AiBenchmarkRunner CreateRunner(IBenchmarkAgentFactory? factory = null)
    {
        DeveloperMatchCards developer = DeveloperMatchCards.Create();
        return new(developer.Data, developer.Behavior, factory);
    }

    private static AiBenchmarkManifest Manifest(AiBenchmarkRunner runner, int count = 1) => new(1, AiBenchmarkManifest.ArtifactKind,
        "test-benchmark", "developer", runner.Pins, new(), Enumerable.Range(1, count).Reverse()
            .Select(index => runner.CreateRow($"game-{index}", "developer", "developer-corp", "developer-runner", (ulong)index)).ToImmutableArray());

    private sealed class FaultFactory(string fault, ManualResetEventSlim blocked) : IBenchmarkAgentFactory
    {
        public void Validate(BenchmarkAgentSpec specification) => specification.Validate();
        public IGameAgent Create(BenchmarkAgentSpec specification, IRuntimeCardCatalog runtime, ICardEvaluationCatalog evaluation) =>
            specification.Side == PlayerSide.Corp ? new FaultAgent(fault, blocked) : new FullGameContractAgent(PlayerSide.Runner, runtime);
    }

    private sealed class FaultAgent(string fault, ManualResetEventSlim blocked) : IGameAgent
    {
        public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
        {
            if (fault == "exception") throw new InvalidOperationException("Intentional test policy failure.");
            if (fault == "hang") blocked.Wait(CancellationToken.None); // deliberately ignores cancellation to test the outer watchdog
            return ValueTask.FromResult(new ActionId("action:not-issued"));
        }
    }

    private sealed class JsonObjectMutation(GameState state)
    {
        public System.Text.Json.Nodes.JsonObject Root { get; } = System.Text.Json.Nodes.JsonNode.Parse(EngineJson.Serialize(state))!.AsObject();
    }

    private sealed class OutputDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netrunner-benchmark-tests-" + Guid.NewGuid().ToString("N"));
        public OutputDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
