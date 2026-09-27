using Netrunner.AI;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class RulesFuzzRunnerTests
{
    [Fact]
    public void PlanIsDeterministicAndCyclesContentAndPairings()
    {
        IReadOnlyList<FuzzGameCase> first = RulesFuzzRunner.Plan(12, 5, RulesFuzzRunner.AllOfficialContentIds);
        IReadOnlyList<FuzzGameCase> second = RulesFuzzRunner.Plan(12, 5, RulesFuzzRunner.AllOfficialContentIds);

        Assert.Equal(first, second);
        Assert.Equal(12, first.Select(fuzzCase => fuzzCase.GameSeed).Distinct().Count());
        Assert.Equal(RulesFuzzRunner.AllOfficialContentIds, first.Take(4).Select(fuzzCase => fuzzCase.ContentId));
        Assert.Equal(RulesFuzzRunner.Pairings,
            first.Where((_, index) => index % 4 == 0).Select(fuzzCase => (fuzzCase.Corp, fuzzCase.Runner)));
        Assert.NotEqual(first[0].GameSeed, RulesFuzzRunner.Plan(1, 6, RulesFuzzRunner.AllOfficialContentIds)[0].GameSeed);
    }

    [Fact]
    public async Task DeveloperGamesFinishWithoutDefectsAndRepeatExactly()
    {
        foreach (FuzzGameCase fuzzCase in RulesFuzzRunner.Plan(3, 1, [RulesFuzzRunner.DeveloperContentId]))
        {
            FuzzGameResult first = await RulesFuzzRunner.RunAsync(fuzzCase);
            FuzzGameResult second = await RulesFuzzRunner.RunAsync(fuzzCase);

            Assert.Equal(FuzzGameStatus.Terminal, first.Status);
            Assert.False(first.IsDefect);
            Assert.Null(first.Failure);
            Assert.Equal(first.ActionCount, second.ActionCount);
            Assert.Equal(first.Outcome?.Winner, second.Outcome?.Winner);
        }
    }

    [Fact]
    public async Task CapIsReportedButIsNotADefect()
    {
        FuzzGameCase fuzzCase = RulesFuzzRunner.Plan(1, 1, [RulesFuzzRunner.DeveloperContentId])[0];

        FuzzGameResult result = await RulesFuzzRunner.RunAsync(fuzzCase, actionCap: 5);

        Assert.Equal(FuzzGameStatus.Capped, result.Status);
        Assert.Equal(5, result.ActionCount);
        Assert.False(result.IsDefect);
    }

    [Fact]
    public async Task RejectedActionKeepsAReplayablePriorPosition()
    {
        FuzzGameCase fuzzCase = RulesFuzzRunner.Plan(1, 1, [RulesFuzzRunner.DeveloperContentId])[0];
        var unissued = new ActionId("action:not-issued");

        FuzzGameResult result = await RulesFuzzRunner.RunAsync(fuzzCase, RulesFuzzRunner.DefaultActionCap,
            (kind, side, definition) => side == PlayerSide.Runner
                ? new SabotageAfter(new RandomLegalAgent(9), decisions: 10, unissued)
                : RulesFuzzRunner.CreateAgent(kind, side, definition));

        Assert.Equal(FuzzGameStatus.Rejected, result.Status);
        Assert.True(result.IsDefect);
        FuzzFailure failure = Assert.IsType<FuzzFailure>(result.Failure);
        Assert.Equal(unissued, failure.FailingSubmission?.ActionId);
        Assert.Null(failure.FailingActionKind);
        Assert.Equal(result.ActionCount, failure.ReplayPrefix.Actions.Count);

        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(fuzzCase.ContentId, fuzzCase.GameSeed);
        ReplayArtifact roundTripped = EngineJson.Deserialize<ReplayArtifact>(
            EngineJson.Serialize(failure.ReplayPrefix), definition.Behavior, definition.Runtime);
        ReplayVerificationResult verified = ReplayVerifier.Verify(roundTripped, definition.Behavior, definition.Runtime);
        Assert.True(verified.Success, verified.Message);
    }

    /// <summary>Delegates to an inner agent, then returns an unissued action on the given decision.</summary>
    private sealed class SabotageAfter(IGameAgent inner, int decisions, ActionId unissued) : IGameAgent
    {
        private int _seen;

        public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default) =>
            ++_seen > decisions ? ValueTask.FromResult(unissued) : inner.ChooseActionAsync(observation, cancellationToken);
    }
}
