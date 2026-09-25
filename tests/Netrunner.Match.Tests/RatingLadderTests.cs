using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class RatingLadderTests
{
    [Fact]
    public void PlanPairsEveryEntrantInEachSeatOnSharedSeeds()
    {
        IReadOnlyList<LadderGame> games = RatingLadder.Plan(["random", "standard"], 3, 9, RulesFuzzRunner.OfficialContentIds);

        Assert.Equal(2 * 2 * 3 * 4, games.Count);
        Assert.Equal(games.Count, games.Select(game => game.Id).Distinct().Count());
        Assert.Equal(games, RatingLadder.Plan(["random", "standard"], 3, 9, RulesFuzzRunner.OfficialContentIds));
        // Common random numbers: every pairing sees the same seeds.
        var seedsByPairing = games.GroupBy(game => (game.Corp, game.Runner))
            .Select(group => group.Select(game => game.GameSeed).Order().ToArray()).ToArray();
        Assert.All(seedsByPairing, seeds => Assert.Equal(seedsByPairing[0], seeds));
        Assert.Throws<ArgumentException>(() => RatingLadder.Plan(["random", "grandmaster"], 1, 1, ["developer"]));
    }

    [Fact]
    public void RatingRecoversAClearOrderingAndIsDeterministic()
    {
        var results = new List<LadderGameResult>();
        foreach (string corp in new[] { "random", "standard" })
        {
            foreach (string runner in new[] { "random", "standard" })
            {
                for (int index = 0; index < 40; index++)
                {
                    double score = corp == runner ? index % 2 : corp == "standard" ? 1 : 0;
                    results.Add(new LadderGameResult(new LadderGame($"{corp}-{runner}-{index}", "developer", (ulong)index, corp, runner),
                        score, false, 100, null));
                }
            }
        }

        LadderReport report = RatingLadder.Rate(results, bootstrap: 100, bootstrapSeed: 3);

        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            LadderRating random = report.Ratings.Single(rating => rating.Side == side && rating.Entrant == "random");
            LadderRating standard = report.Ratings.Single(rating => rating.Side == side && rating.Entrant == "standard");
            Assert.Equal(0, random.Elo);
            Assert.True(standard.Elo > 200, $"{side} standard rated {standard.Elo}.");
            Assert.True(standard.Lower > random.Upper);
            LadderDifference gap = Assert.Single(report.Differences, difference => difference.Side == side);
            Assert.Equal(("random", "standard"), (gap.Weaker, gap.Stronger));
            Assert.True(gap.Separated);
            Assert.True(Assert.Single(report.Separations, separation => separation.Side == side).Separated);
        }

        Assert.Equal(report.Ratings, RatingLadder.Rate(results, bootstrap: 100, bootstrapSeed: 3).Ratings);
    }

    [Fact]
    public void EvenResultsDoNotSeparate()
    {
        var results = new List<LadderGameResult>();
        foreach (string corp in new[] { "easy", "standard" })
        {
            foreach (string runner in new[] { "easy", "standard" })
            {
                for (int index = 0; index < 20; index++)
                {
                    results.Add(new LadderGameResult(new LadderGame($"{corp}-{runner}-{index}", "developer", (ulong)index, corp, runner),
                        index % 2, false, 100, null));
                }
            }
        }

        LadderReport report = RatingLadder.Rate(results, bootstrap: 100);

        Assert.All(report.Separations, separation => Assert.False(separation.Separated));
    }

    [Fact]
    public async Task DeveloperLadderGameFinishes()
    {
        LadderGame game = RatingLadder.Plan(["random", "contract"], 1, 2, [RulesFuzzRunner.DeveloperContentId])
            .First(candidate => candidate.Corp == "contract" && candidate.Runner == "random");

        LadderGameResult result = await RatingLadder.PlayAsync(game);

        Assert.Null(result.Error);
        Assert.False(result.Capped);
        Assert.InRange(result.CorpScore, 0, 1);
    }

    [Fact]
    public async Task HumanReviewComparesEveryReviewedDecisionWithoutRawIds()
    {
        FullMatchDefinition definition = FullMatchDefinition.Developer(47042);
        FullMatchResult played = await FullMatchRunner.RunAsync(definition, 1000,
            new RandomLegalAgent(1), new RandomLegalAgent(2));
        DeveloperMatchCards cards = DeveloperMatchCards.Create();

        IReadOnlyList<ReviewedDecision> reviewed = await HumanGameReview.ReviewAsync(played.Replay, PlayerSide.Runner,
            definition.Behavior, definition.Runtime, new CardCatalogMetadataResolver(cards.Data));

        Assert.NotEmpty(reviewed);
        Assert.Equal(reviewed.Count, reviewed.Select(decision => decision.Sequence).Distinct().Count());
        Assert.All(reviewed, decision =>
        {
            Assert.NotNull(decision.HumanKind);
            Assert.NotNull(decision.BotKind);
            Assert.Equal(decision.HumanActionId == decision.BotActionId, decision.Agrees);
            foreach (string? text in decision.TopCandidates.Select(candidate => candidate.Description)
                .Append(decision.HumanDescription).Append(decision.BotDescription))
            {
                Assert.DoesNotContain("card:", text ?? string.Empty);
                Assert.DoesNotContain("server:", text ?? string.Empty);
            }
        });
    }
}
