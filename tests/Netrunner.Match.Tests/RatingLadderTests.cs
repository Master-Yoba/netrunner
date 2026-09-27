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

        Assert.Equal(2 * 2 * 3 * RulesFuzzRunner.OfficialContentIds.Count, games.Count);
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
    public void DealPlansRotateThePoolAndPairEveryEntrantOnEachDeal()
    {
        string[] pool = ["a~x", "b~x", "c~y"];
        IReadOnlyList<LadderGame> games = RatingLadder.PlanDeals(["easy", "standard"], 7, 5, pool);

        // A1b: the budget is deals, not pairings × seeds; each deal is played by all four seat pairings.
        Assert.Equal(7 * 4, games.Count);
        Assert.Equal(games.Count, games.Select(game => game.Id).Distinct().Count());
        foreach (IGrouping<int, LadderGame> deal in games.GroupBy(game => game.Deal))
        {
            Assert.Equal(4, deal.Count());
            Assert.Single(deal.Select(game => (game.ContentId, game.GameSeed)).Distinct());
            Assert.Equal(pool[deal.Key % pool.Length], deal.First().ContentId);
        }
        // A later batch continues the same deal sequence.
        Assert.Equal(games.Where(game => game.Deal >= 4), RatingLadder.PlanDeals(["easy", "standard"], 3, 5, pool, firstDeal: 4));
    }

    [Fact]
    public void DeckComparisonFindsTheDeckWhereTheCandidateIsWorse()
    {
        // On content "good" the candidate Corp wins every game and the baseline loses
        // them all; on content "bad" it is the other way round.
        var results = new List<LadderGameResult>();
        for (int deal = 0; deal < 40; deal++)
        {
            string content = deal % 2 == 0 ? "good" : "bad";
            foreach (string corp in new[] { "easy", "standard" })
                foreach (string runner in new[] { "easy", "standard" })
                {
                    double score = corp == "standard" ? content == "good" ? 1 : 0 : content == "good" ? 0 : 1;
                    results.Add(new LadderGameResult(new LadderGame($"{corp}-{runner}-{deal}", content, (ulong)deal, corp, runner, deal),
                        score, false, 100, null));
                }
        }

        IReadOnlyList<LadderDeckComparison> decks = RatingLadder.CompareByDeck(results, "easy", "standard", bootstrap: 100);

        LadderDeckComparison bad = decks.Single(deck => deck.ContentId == "bad" && deck.Side == PlayerSide.Corp);
        LadderDeckComparison good = decks.Single(deck => deck.ContentId == "good" && deck.Side == PlayerSide.Corp);
        Assert.Equal(-1, bad.ScoreDifference, 6);
        Assert.True(bad.Warning);
        Assert.Equal(1, good.ScoreDifference, 6);
        Assert.False(good.Warning);
        Assert.Equal(20, bad.Deals);
    }

    [Fact]
    public void StoppingNeedsEveryDifferenceClearOfZero()
    {
        var clear = new LadderReport([], [new(PlayerSide.Corp, "easy", "standard", 90, 20, 160), new(PlayerSide.Runner, "easy", "standard", -80, -150, -10)],
            [], 10, 0, 0);
        var open = clear with { Differences = [.. clear.Differences.Take(1), new(PlayerSide.Runner, "easy", "standard", 5, -40, 50)] };
        Assert.True(RatingLadder.IsClear(clear));
        Assert.False(RatingLadder.IsClear(open));
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
