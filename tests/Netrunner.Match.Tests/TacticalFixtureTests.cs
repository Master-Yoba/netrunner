using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class TacticalFixtureTests
{
    public static IEnumerable<object[]> ImplementedCases => GatewayTacticalCatalog.ImplementedIds.Select(id => new object[] { id });

    [Fact]
    public void SpecificationRetainsBothSidesAndIndependentTuningHeldOutCategories()
    {
        Assert.Equal(80, GatewayTacticalCatalog.Specifications.Length);
        Assert.Equal(80, GatewayTacticalCatalog.Specifications.Select(value => value.Id).Distinct().Count());
        foreach (PlayerSide side in Enum.GetValues<PlayerSide>())
        {
            TacticalCaseMetadata[] cases = GatewayTacticalCatalog.Specifications.Where(value => value.Side == side).ToArray();
            Assert.Equal(40, cases.Length);
            Assert.Equal(24, cases.Count(value => value.Split == "tuning"));
            Assert.Equal(16, cases.Count(value => value.Split == "held-out"));
            Assert.Equal(8, cases.Select(value => value.Category).Distinct().Count());
        }
    }

    [OfficialGatewayTheory]
    [MemberData(nameof(ImplementedCases))]
    public async Task IntendedCoreIssuedLinePassesAndRetainsVerifiedReplay(string id)
    {
        var catalog = new GatewayTacticalCatalog(CardDataResource.LoadEmbedded());
        TacticalFixture fixture = catalog.Build(id);
        (TacticalCaseResult result, ReplayArtifact replay) = await TacticalExecution.RunAsync(fixture, null, oracle: true);
        Assert.True(result.Passed, $"{id}: {result.Error}; completed={result.Completed}; actions={string.Join(',', result.SemanticActions)}");
        Assert.True(result.ReplayVerified);
        Assert.NotEmpty(replay.Actions);
        Assert.Equal(DeterministicHash.ForState(fixture.Initial), result.InitialStateHash);
        if (fixture.HiddenTwin is { } twin)
        {
            Assert.NotEqual(DeterministicHash.ForState(fixture.Initial), DeterministicHash.ForState(twin));
            Assert.True(result.HiddenTwinEquivalent);
            Assert.NotEqual(result.ReplayHash, result.HiddenTwinReplayHash);
        }
    }

    [OfficialGatewayTheory]
    [MemberData(nameof(ImplementedCases))]
    public async Task AContrastingLegalChoiceDoesNotFalselyPass(string id)
    {
        var catalog = new GatewayTacticalCatalog(CardDataResource.LoadEmbedded());
        TacticalFixture fixture = catalog.Build(id);
        // A compulsory one-action prompt has no contrasting legal choice.
        // Its negative control is failure to complete the required action.
        if (fixture.ForcedContinuation) fixture = fixture with { ActionCap = 0 };
        TacticalArtifacts artifacts = await TacticalExecution.RunAsync(fixture, null, oracle: true, rejectFirstOracleChoice: true);
        TacticalCaseResult result = artifacts.Result;
        Assert.True(fixture.ForcedContinuation || artifacts.CounterexampleApplied, $"{id}: negative control never submitted a contrasting choice: {result.Error}");
        Assert.False(result.Passed);
        Assert.True(result.ReplayVerified);
    }

    [OfficialGatewayTheory]
    [InlineData("corp-information-01")]
    [InlineData("corp-information-02")]
    [InlineData("runner-payments-05")]
    public async Task TwinExecutionComparesRealPolicyScoresChoicesAndCommittedCheckpoints(string id)
    {
        var catalog = new GatewayTacticalCatalog(CardDataResource.LoadEmbedded());
        TacticalFixture fixture = catalog.Build(id);
        var evaluation = new GatewayEvaluationCatalog(fixture.Board.Runtime, fixture.Board.Behavior);
        var configuration = OfflineAgentConfiguration.Create(fixture.Metadata.Side, "fixture-tie-v1", "fixture", 125);
        OfflineAgent Create() => new(configuration, new TiePolicy(), evaluation, captureDiagnostics: true);
        TacticalArtifacts artifacts = await TacticalExecution.RunAsync(fixture, Create(), twinFactory: Create);
        Assert.True(artifacts.Result.Passed, artifacts.Result.Error);
        Assert.True(artifacts.Result.HiddenTwinEquivalent);
        Assert.NotNull(artifacts.Result.CheckpointDigest);
        Assert.NotEmpty(artifacts.Result.EvaluationDigests);
        Assert.NotNull(artifacts.HiddenTwinReplay);
    }

    private sealed class TiePolicy : IOfflinePolicy
    {
        public string Version => "fixture-tie-v1";
        public AgentUtility Evaluate(AgentFeatures features, AgentAction action, AgentKnowledge knowledge, AgentObjective? objective,
            OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget) => new(0, 0, "fixture-equal");
        public AgentUtility Fallback(AgentFeatures features, AgentAction action) => new(0, 0, "fixture-fallback");
    }
}

public sealed class OfficialGatewayTheoryAttribute : TheoryAttribute
{
    public OfficialGatewayTheoryAttribute()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode)
            Skip = "Requires the provisioned official Gateway content lane; synthetic content does not count as tactical evidence.";
    }
}
