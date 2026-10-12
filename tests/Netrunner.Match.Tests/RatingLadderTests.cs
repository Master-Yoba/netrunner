using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

public sealed class RatingLadderTests
{
    /// <summary>
    /// Bot plan S3: an agent's random stream comes from the match seed and its side, never its version, so a version that
    /// plays as another (v31 as v30) plays the same games: the seat-swap identity gate depends on it.
    /// </summary>
    [Fact]
    public void TheLiveAndThePreviousVersionShareTheirSeeds()
    {
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(
            CardDataResource.EmbeddedMode == CardDataResource.OfficialMode ? RulesFuzzRunner.GatewayElevationContentIds[0] : RulesFuzzRunner.DeveloperContentId, 9);
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            var live = (OfflineAgent)RatingLadder.CreateAgent("standard", side, definition);
            var previous = (OfflineAgent)RatingLadder.CreateAgent("standard-prev", side, definition);
            Assert.Equal(live.Configuration.PolicySeed, previous.Configuration.PolicySeed);
            Assert.Equal(side == PlayerSide.Corp ? CorpHeuristicPolicy.V30 : RunnerHeuristicPolicy.V26, previous.Configuration.PolicyVersion);
            Assert.Equal(side == PlayerSide.Corp ? CorpHeuristicPolicy.PolicyVersion : RunnerHeuristicPolicy.PolicyVersion, live.Configuration.PolicyVersion);
        }
    }

    [Fact]
    public void BudgetScalingEntrantsPlanAMultipleOfHardsSamples()
    {
        if (CardDataResource.EmbeddedMode != CardDataResource.OfficialMode) return;
        FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(RulesFuzzRunner.GatewayElevationContentIds[0], 5);
        foreach (PlayerSide side in new[] { PlayerSide.Corp, PlayerSide.Runner })
        {
            var hard = (OfflineAgent)RatingLadder.CreateAgent("hard", side, definition);
            foreach (int multiple in new[] { 1, 2, 4, 8, 16 })
            {
                var scaled = (OfflineAgent)RatingLadder.CreateAgent($"standard-x{multiple}", side, definition);
                Assert.Equal(hard.Configuration.Parameters["plan-samples"] * multiple, scaled.Configuration.Parameters["plan-samples"]);
                Assert.Equal(hard.Configuration.Parameters.Remove("plan-samples"), scaled.Configuration.Parameters.Remove("plan-samples"));
                Assert.Equal(hard.Checkpoint.Knowledge.OpponentList, scaled.Checkpoint.Knowledge.OpponentList);
            }
        }
    }

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

    /// <summary>
    /// A candidate that changes 20 of 400 deals, winning each it changes, and otherwise plays its baseline's games: deal
    /// luck is shared, so the paired comparison is clear and counts the changed deals, and the ladder's deal bootstrap
    /// separates them.
    /// </summary>
    [Fact]
    public void APairedCandidateIsJudgedByTheDealsItChanges()
    {
        var results = new List<LadderGameResult>();
        for (int deal = 0; deal < 400; deal++)
            foreach (string corp in new[] { "easy", "standard" })
                foreach (string runner in new[] { "easy", "standard" })
                {
                    double luck = deal % 2;
                    double score = corp == "standard" && deal % 20 == 0 ? 1 : luck;
                    results.Add(new LadderGameResult(new LadderGame($"{corp}-{runner}-{deal}", "box", (ulong)deal, corp, runner, deal),
                        score, false, 100, null));
                }

        LadderPairedComparison paired = Assert.Single(RatingLadder.ComparePaired(results, "easy", "standard", bootstrap: 200),
            comparison => comparison.Side == PlayerSide.Corp);
        Assert.Equal(400, paired.Deals);
        Assert.Equal(20, paired.ChangedDeals);
        Assert.Equal(20, paired.ResultChangedDeals);
        Assert.Equal(0.05, paired.ScoreDifference, 6);
        Assert.True(paired.Clear && paired.Lower > 0, $"[{paired.Lower}, {paired.Upper}]");
        LadderDifference gap = Assert.Single(RatingLadder.Rate(results, bootstrap: 200).Differences, difference => difference.Side == PlayerSide.Corp);
        Assert.True(gap.Separated, $"{gap.Elo} [{gap.Lower}, {gap.Upper}]");
    }

    /// <summary>
    /// The review of #143: a deal's changes are counted game by game before its games are averaged. On deal 0 the candidate
    /// wins against one opponent and loses against the other (the mean difference is zero); on deal 1 it plays a different
    /// game to the same result. Both deals changed; only deal 0's results did.
    /// </summary>
    [Fact]
    public void ChangedDealsAreCountedGameByGame()
    {
        LadderGameResult Game(int deal, string corp, string runner, double score, string hash) =>
            new(new LadderGame($"{corp}-{runner}-{deal}", "box", (ulong)deal, corp, runner, deal), score, false, 100, null, hash);
        LadderGameResult[] results =
        [
            Game(0, "easy", "easy", 0, "a"), Game(0, "standard", "easy", 1, "b"),
            Game(0, "easy", "standard", 1, "c"), Game(0, "standard", "standard", 0, "d"),
            Game(1, "easy", "easy", 1, "e"), Game(1, "standard", "easy", 1, "f"),
            Game(1, "easy", "standard", 1, "g"), Game(1, "standard", "standard", 1, "g"),
            Game(2, "easy", "easy", 0, "h"), Game(2, "standard", "easy", 0, "h"),
            Game(2, "easy", "standard", 0, "i"), Game(2, "standard", "standard", 0, "i"),
        ];

        LadderPairedComparison corp = Assert.Single(RatingLadder.ComparePaired(results, "easy", "standard", bootstrap: 50),
            comparison => comparison.Side == PlayerSide.Corp);

        Assert.Equal(0, corp.ScoreDifference, 6);
        Assert.Equal(2, corp.ChangedDeals);
        Assert.Equal(1, corp.ResultChangedDeals);
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
