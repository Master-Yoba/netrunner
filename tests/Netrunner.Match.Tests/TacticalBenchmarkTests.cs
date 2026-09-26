using System.Collections.Immutable;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class TacticalBenchmarkTests
{
    [OfficialGatewayTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FrozenManifestIsIndependentAndWorkerCountCannotChangeResults(bool fixtureValidation)
    {
        using var files = new OutputDirectory();
        var runner = new TacticalBenchmarkRunner(CardDataResource.LoadEmbedded());
        TacticalBenchmarkManifest current = runner.CreateContractManifest();
        TacticalBenchmarkManifest manifest = EngineJson.Deserialize<TacticalBenchmarkManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Benchmarks/tactical-contract-v1.json")));
        Assert.Equal(EngineJson.Serialize(current), EngineJson.Serialize(manifest));
        Assert.Equal(80, manifest.Positions.Select(position => position.InitialStateHash).Distinct().Count());
        TacticalBenchmarkReport serial = await runner.RunAsync(manifest, Path.Combine(files.Path, "serial"), verifyFixtures: fixtureValidation);
        TacticalBenchmarkReport parallel = await runner.RunAsync(manifest, Path.Combine(files.Path, "parallel"), workers: 4, verifyFixtures: fixtureValidation);
        Assert.Equal(serial.DeterministicDigest, parallel.DeterministicDigest);
        Assert.Equal(serial.Positions.Select(position => position.ReplayHash), parallel.Positions.Select(position => position.ReplayHash));
        Assert.Equal(fixtureValidation ? "fixture-validation" : "policy-evaluation", serial.Mode);
        Assert.All(serial.Positions, position => Assert.True(position.ReplayVerified));
        Assert.All(serial.Scores.Where(score => score.Category == "all" && score.Split == "all"), score => Assert.Equal(40, score.Positions));
        Assert.Equal(fixtureValidation, serial.Passed);
        foreach (TacticalCaseResult result in serial.Positions.Where(position => position.HiddenTwinReplayHash is not null))
        {
            Assert.True(result.HiddenTwinEquivalent);
            Assert.True(File.Exists(Path.Combine(files.Path, "serial", result.Id, "hidden-twin-replay.json")));
        }
        Assert.Equal(serial.DeterministicDigest, EngineJson.Deserialize<TacticalBenchmarkReport>(EngineJson.Serialize(serial)).DeterministicDigest);
    }

    [OfficialGatewayTheory]
    [InlineData("pins")]
    [InlineData("root")]
    [InlineData("policy")]
    public async Task FrozenInputMismatchFailsBeforeCreatingEvidence(string change)
    {
        using var files = new OutputDirectory();
        var runner = new TacticalBenchmarkRunner(CardDataResource.LoadEmbedded());
        TacticalBenchmarkManifest manifest = runner.CreateContractManifest();
        TacticalPositionSpec first = manifest.Positions[0];
        manifest = change switch
        {
            "pins" => manifest with { BuilderVersion = "unknown" },
            "root" => manifest with { Positions = [first with { InitialStateHash = new string('0', 64) }] },
            _ => manifest with { Positions = [first with { Agent = first.Agent with { PolicyVersion = "uninstalled" } }] },
        };
        string output = Path.Combine(files.Path, "bad");
        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync(manifest, output));
        Assert.False(Directory.Exists(output));
    }

    [OfficialGatewayTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaultsAndCancellationKeepEveryDeclaredPositionAndReplay(bool cancel)
    {
        using var files = new OutputDirectory();
        var runner = new TacticalBenchmarkRunner(CardDataResource.LoadEmbedded(), new IllegalFactory());
        TacticalBenchmarkManifest all = runner.CreateContractManifest();
        TacticalBenchmarkManifest manifest = all with { Positions = all.Positions.Take(3).ToImmutableArray() };
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.Cancel();
        string output = Path.Combine(files.Path, "faults");
        TacticalBenchmarkReport report = await runner.RunAsync(manifest, output, workers: 3, cancellationToken: cancellation.Token);
        Assert.False(report.Passed);
        Assert.Equal(3, report.Positions.Length);
        Assert.All(report.Positions, result =>
        {
            Assert.False(result.Passed);
            Assert.True(result.ReplayVerified);
            Assert.NotNull(result.Error);
            Assert.True(File.Exists(Path.Combine(output, result.ReplayPath)));
            Assert.Contains("finalSnapshot", File.ReadAllText(Path.Combine(output, result.Id, "failure.json")), StringComparison.Ordinal);
        });
        await Assert.ThrowsAsync<IOException>(() => runner.RunAsync(manifest, output));
    }

    private sealed class IllegalFactory : IBenchmarkAgentFactory
    {
        public void Validate(BenchmarkAgentSpec specification) => specification.Validate();
        public IGameAgent Create(BenchmarkAgentSpec specification, IRuntimeCardCatalog runtime, ICardEvaluationCatalog evaluation) => new IllegalAgent();
    }

    [OfficialGatewayTheory]
    [InlineData("corp-information-01")]
    public async Task FailedHiddenTwinRetainsItsOwnSnapshotAndVerifiedReplay(string id)
    {
        using var files = new OutputDirectory();
        var runner = new TacticalBenchmarkRunner(CardDataResource.LoadEmbedded(), new TwinFaultFactory());
        TacticalBenchmarkManifest manifest = runner.CreateContractManifest();
        manifest = manifest with { Positions = [manifest.Positions.Single(position => position.Metadata.Id == id)] };
        TacticalBenchmarkReport report = await runner.RunAsync(manifest, files.Path);
        TacticalCaseResult result = Assert.Single(report.Positions);
        Assert.False(result.Passed);
        Assert.True(result.ReplayVerified);
        Assert.True(result.HiddenTwinReplayVerified);
        Assert.NotNull(result.HiddenTwinError);
        Assert.Contains("finalSnapshot", File.ReadAllText(Path.Combine(files.Path, id, "hidden-twin-failure.json")), StringComparison.Ordinal);
    }

    private sealed class TwinFaultFactory : IBenchmarkAgentFactory
    {
        private int _created;
        public void Validate(BenchmarkAgentSpec specification) => specification.Validate();
        public IGameAgent Create(BenchmarkAgentSpec specification, IRuntimeCardCatalog runtime, ICardEvaluationCatalog evaluation) =>
            Interlocked.Increment(ref _created) == 1 ? new Netrunner.AI.FullGameContractAgent(specification.Side, runtime) : new IllegalAgent();
    }
    private sealed class IllegalAgent : IGameAgent
    {
        public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActionId("action:unissued"));
    }

    private sealed class OutputDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netrunner-tactics-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
