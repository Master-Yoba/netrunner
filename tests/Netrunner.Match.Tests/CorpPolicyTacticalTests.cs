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
    [MemberData(nameof(TuningCases))]
    public async Task StandardRemainsLegalAndPassesCriticalTuningPosition(string id)
    {
        TacticalFixture fixture = new GatewayTacticalCatalog(CardDataResource.LoadEmbedded()).Build(id);
        var evaluation = new GatewayEvaluationCatalog(fixture.Board.Runtime, fixture.Board.Behavior);
        OfflineAgent Create() => new(CorpPolicyProfiles.Create("standard", 5301), new CorpHeuristicPolicy(), evaluation, captureDiagnostics: true);
        TacticalArtifacts artifacts = await TacticalExecution.RunAsync(fixture, Create(), twinFactory: Create);
        if (fixture.Metadata.Critical) Assert.True(artifacts.Result.Passed, $"{id}: {artifacts.Result.Error}; actions={string.Join(',', artifacts.Result.SemanticActions)}; evaluations={EngineJson.Serialize(artifacts.Evaluations)}");
        Assert.True(artifacts.Result.ReplayVerified);
        Assert.All(artifacts.Evaluations, evaluation => Assert.False(evaluation.BudgetExhausted));
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
            OfflineAgent Create() => new(CorpPolicyProfiles.Create(profile, 5301), new CorpHeuristicPolicy(), evaluation, captureDiagnostics: true);
            results.Add((await TacticalExecution.RunAsync(fixture, Create(), twinFactory: Create)).Result);
        }
        string failures = string.Join("; ", results.Where(result => !result.Passed).Select(result => result.Id + ": " + result.Error));
        Assert.Equal(24, results.Count);
        Assert.All(results.Where(result => result.Critical), result => Assert.True(result.Passed, failures));
        if (profile == "standard") Assert.True(results.Count(result => result.Passed) >= 21, failures);
    }
}
