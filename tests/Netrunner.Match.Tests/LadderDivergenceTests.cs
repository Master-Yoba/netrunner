using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>Where a candidate's saved ladder game first departs from its baseline's (after chiriboga's replay diff).</summary>
public sealed class LadderDivergenceTests
{
    [Fact]
    public async Task PairedGamesDivergeAtTheCandidatesFirstDifferentChoice()
    {
        var saved = new List<LadderSavedGame>();
        foreach (LadderGame game in RatingLadder.PlanDeals(["random", "contract"], 2, 5, [RulesFuzzRunner.DeveloperContentId]))
        {
            LadderGameResult result = await RatingLadder.PlayAsync(game, RatingLadder.DefaultActionCap, entry => { lock (saved) saved.Add(entry); });
            Assert.Null(result.Error);
        }
        var games = saved.Select(game => (game, RulesFuzzRunner.CreateDefinition(game.Game.ContentId, game.Game.GameSeed))).ToArray();

        LadderDivergenceReport report = LadderDivergences.Compare(games, PlayerSide.Corp, "random", "contract");

        // Two deals, each against two Runner entrants.
        Assert.Equal(4, report.Pairs);
        Assert.Equal(report.Pairs, report.Identical + report.Divergences.Count);
        Assert.NotEmpty(report.Divergences);
        LadderDivergence first = report.Divergences[0];
        // The Runner entrant is the same on both sides of a pair, so the first departure is the Corp's.
        Assert.Equal(PlayerSide.Corp, first.Decider);
        Assert.False(string.IsNullOrEmpty(first.BaselineChoice) || string.IsNullOrEmpty(first.CandidateChoice));
        Assert.DoesNotContain("card:", first.Kind, StringComparison.Ordinal);
        // A game against itself never departs.
        (LadderSavedGame any, FullMatchDefinition definition) = games[0];
        Assert.Null(LadderDivergences.First(any, any, definition, PlayerSide.Corp, "random", "random"));
    }
}
