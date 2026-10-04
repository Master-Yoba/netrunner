using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>Bot plan S0.1: the profiler times the ladder's own games and the planner, and never changes either.</summary>
public sealed class MatchProfilerTests
{
    [Fact]
    public async Task ProfilingNeverChangesAGame()
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(RulesFuzzRunner.DeveloperContentId, 7);
        FullMatchResult plain = await Play(definition);
        ProfileTimings? previous = ProfileTimings.Current;
        ProfileTimings.Current = new ProfileTimings();
        try
        {
            FullMatchResult profiled = await Play(definition);
            Assert.Equal(plain.FinalStateHash, profiled.FinalStateHash);
            Assert.Equal(plain.ActionCount, profiled.ActionCount);
            Assert.Equal(plain.ActionCount, ProfileTimings.Current["match.apply"]!.Count);
        }
        finally { ProfileTimings.Current = previous; }
        Assert.Null(ProfileTimings.Current);
        Assert.Equal(0, ProfileTimings.Start());
    }

    [Fact]
    public async Task AGameProfileTimesEveryActionOnce()
    {
        GameProfile profile = await MatchProfiler.ProfileGamesAsync([RulesFuzzRunner.DeveloperContentId], 3, "standard", games: 1,
            rolloutRoots: 2, rolloutSamples: 1);
        Assert.True(profile.Actions > 0);
        foreach (string segment in new[] { "match.observation", "match.decide", "match.round-trip", "match.apply" })
            Assert.Equal(profile.Actions, Assert.Single(profile.Segments, entry => entry.Name == segment).Count);
        Assert.Equal(1, Assert.Single(profile.Segments, entry => entry.Name == "match.replay-verify").Count);
        Assert.Equal(profile.GameRollouts.Rollouts, profile.GameRollouts.Finished);
        Assert.True(profile.GameRollouts.Rollouts is > 0 and <= 2);
    }

    [Fact]
    public async Task APlanProfileTimesTheSavedDecisionAndLeavesItsChoiceAlone()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games", "strategy1-petty-cash")));
        PlanProfile first = await MatchProfiler.ProfilePlanAsync(saved, definition, 101, "hard", repeats: 2, rolloutSamples: 2);
        PlanProfile second = await MatchProfiler.ProfilePlanAsync(saved, definition, 101, "hard", repeats: 1, rolloutSamples: 0);
        Assert.Equal(PlayerSide.Corp, first.Side);
        Assert.Equal(first.Chosen, second.Chosen);
        Assert.Equal(2, Assert.Single(first.Segments, entry => entry.Name == "plan").Count);
        Assert.Equal(2, first.GameRollouts.Finished);
        Assert.Equal(0, second.GameRollouts.Rollouts);
    }

    private static Task<FullMatchResult> Play(FullMatchDefinition definition) => FullMatchRunner.RunAsync(definition,
        RatingLadder.DefaultActionCap, RatingLadder.CreateAgent("standard", PlayerSide.Corp, definition),
        RatingLadder.CreateAgent("standard", PlayerSide.Runner, definition));
}
