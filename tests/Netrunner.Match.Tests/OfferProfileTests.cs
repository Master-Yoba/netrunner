using Netrunner.Core;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>The misplay sweep's profile: each seat's offered and taken plays over saved ladder games.</summary>
public sealed class OfferProfileTests
{
    [Fact]
    public async Task EachSeatsChoicesAreCountedAmongItsOffers()
    {
        var saved = new List<LadderSavedGame>();
        foreach (LadderGame game in RatingLadder.PlanDeals(["random", "contract"], 2, 5, [RulesFuzzRunner.DeveloperContentId]))
        {
            LadderGameResult result = await RatingLadder.PlayAsync(game, RatingLadder.DefaultActionCap, entry => { lock (saved) saved.Add(entry); });
            Assert.Null(result.Error);
        }
        var games = saved.Select(game => (game, RulesFuzzRunner.CreateDefinition(game.Game.ContentId, game.Game.GameSeed))).ToArray();

        IReadOnlyList<OfferRow> rows = OfferProfile.Measure(games, workers: 2);

        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.InRange(row.Taken, 0, row.Offered);
            Assert.InRange(row.GamesTaken, 0, row.GamesOffered);
            Assert.InRange(row.GamesOffered, 1, row.Games);
            Assert.DoesNotContain("card:", row.Play, StringComparison.Ordinal);
        });
        // Every choice a seat made among two or more actions is one play taken: the plays' takes sum to the seat's choices.
        foreach (var seat in rows.GroupBy(row => (row.Side, row.Entrant, row.Deck, row.Decision)))
            Assert.Equal(seat.First().SeatActions, seat.Sum(row => row.Taken));
        // Each entrant played each side in half the games.
        Assert.Equal(games.Length, rows.Where(row => row.Side == PlayerSide.Corp).GroupBy(row => (row.Entrant, row.Deck)).Sum(seat => seat.First().Games));
        Assert.Contains(rows, row => row.Side == PlayerSide.Runner && row.Decision == DecisionKind.TurnAction && row.Taken > 0);
        // A seat is its deck from the game's content id (the review of #147), and the order doesn't depend on the workers.
        Assert.All(rows, row => Assert.Contains(row.Deck, games.Select(game => game.game.Game.ContentId)
            .SelectMany(content => content.Split('~')).ToHashSet()));
        Assert.Equal(rows, OfferProfile.Measure(games, workers: 1));
    }
}
