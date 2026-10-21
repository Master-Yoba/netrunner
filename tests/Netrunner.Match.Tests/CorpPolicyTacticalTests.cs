using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class CorpPolicyTacticalTests
{
    public static IEnumerable<object[]> TuningCases => GatewayTacticalCatalog.Specifications
        .Where(value => value.Side == PlayerSide.Corp && value.Split == "tuning").Select(value => new object[] { value.Id });

    [OfficialGatewayTheory]
    [InlineData("corp-choices-05")]
    public async Task PrivateSearchUsesMetadataIntentWithoutConstructingAnEngineEffect(string id)
    {
        TacticalFixture fixture = new GatewayTacticalCatalog(CardDataResource.LoadEmbedded()).Build(id);
        var evaluation = new GatewayEvaluationCatalog(fixture.Board.Runtime, fixture.Board.Behavior);
        OfflineAgent Create() => new(CorpPolicyProfiles.Create("standard", 5301, CorpHeuristicPolicy.V2), new LegacyCorpPolicy(CorpHeuristicPolicy.V2), evaluation, captureDiagnostics: true);
        TacticalArtifacts artifacts = await TacticalExecution.RunAsync(fixture, Create(), twinFactory: Create);
        Assert.True(artifacts.Result.Passed, artifacts.Result.Error);
        Assert.True(artifacts.Result.ReplayVerified);
    }

    [OfficialGatewayTheory]
    [MemberData(nameof(TuningCases))]
    public async Task StandardRemainsLegalAndPassesCriticalTuningPosition(string id)
    {
        TacticalFixture fixture = new GatewayTacticalCatalog(CardDataResource.LoadEmbedded()).Build(id);
        var evaluation = new GatewayEvaluationCatalog(fixture.Board.Runtime, fixture.Board.Behavior);
        OfflineAgent Create() => new(CorpPolicyProfiles.Create("standard", 5301, CorpHeuristicPolicy.V2), new LegacyCorpPolicy(CorpHeuristicPolicy.V2), evaluation, captureDiagnostics: true);
        TacticalArtifacts artifacts = await TacticalExecution.RunAsync(fixture, Create(), twinFactory: Create);
        if (fixture.Metadata.Critical) Assert.True(artifacts.Result.Passed, $"{id}: {artifacts.Result.Error}; actions={string.Join(',', artifacts.Result.SemanticActions)}; evaluations={EngineJson.Serialize(artifacts.Evaluations)}");
        Assert.True(artifacts.Result.ReplayVerified);
        Assert.All(artifacts.Evaluations, evaluation => Assert.False(evaluation.BudgetExhausted));
    }

    [OfficialGatewayTheory]
    [InlineData("standard")]
    [InlineData("easy")]
    public async Task FinalFrozenSuiteRetainsCriticalHeldOutAndTwinChecksAcrossWorkerCounts(string profile)
    {
        var runner = new TacticalBenchmarkRunner(CardDataResource.LoadEmbedded());
        TacticalBenchmarkManifest manifest = BenchmarkSuites.WithCorpProfile(runner.CreateContractManifest(), profile, CorpHeuristicPolicy.V2);
        string directory = Path.Combine(Path.GetTempPath(), "netrunner-corp-policy-" + Guid.NewGuid().ToString("N"));
        try
        {
            TacticalBenchmarkReport serial = await runner.RunAsync(manifest, Path.Combine(directory, "serial"));
            TacticalBenchmarkReport parallel = await runner.RunAsync(manifest, Path.Combine(directory, "parallel"), workers: 4);
            Assert.Equal(serial.DeterministicDigest, parallel.DeterministicDigest);
            Assert.Equal(40, serial.Positions.Length);
            Assert.Equal(16, serial.Positions.Count(position => position.Split == "held-out"));
            Assert.All(serial.Positions, position =>
            {
                Assert.True(position.Completed, position.Id + ": " + position.Error);
                Assert.True(position.ReplayVerified);
                Assert.Null(position.Error);
                if (position.Critical) Assert.True(position.Passed, position.Id);
                if (position.HiddenTwinEquivalent is { } equivalent) Assert.True(equivalent, position.Id);
            });
            if (profile == "standard") Assert.True(serial.Passed);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [OfficialGatewayTheory]
    [InlineData("standard")]
    [InlineData("easy")]
    public async Task TuningScoresRetainAllCasesAndCriticalFailures(string profile)
    {
        var catalog = new GatewayTacticalCatalog(CardDataResource.LoadEmbedded());
        var results = new List<TacticalCaseResult>();
        foreach (string id in TuningCases.Select(row => (string)row[0]))
        {
            TacticalFixture fixture = catalog.Build(id);
            var evaluation = new GatewayEvaluationCatalog(fixture.Board.Runtime, fixture.Board.Behavior);
            OfflineAgent Create() => new(CorpPolicyProfiles.Create(profile, 5301, CorpHeuristicPolicy.V2), new LegacyCorpPolicy(CorpHeuristicPolicy.V2), evaluation, captureDiagnostics: true);
            results.Add((await TacticalExecution.RunAsync(fixture, Create(), twinFactory: Create)).Result);
        }
        string failures = string.Join("; ", results.Where(result => !result.Passed).Select(result => result.Id + ": " + result.Error));
        Assert.Equal(24, results.Count);
        Assert.All(results.Where(result => result.Critical), result => Assert.True(result.Passed, failures));
        if (profile == "standard") Assert.True(results.Count(result => result.Passed) >= 21, failures);
    }
}
