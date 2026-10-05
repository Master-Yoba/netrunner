using System.Text.Json.Nodes;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match.Puzzles;
using Netrunner.Match.Regret;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>Bot plan S2: the puzzle sets, their matchers and line predicates, and the converted saved-decision tests.</summary>
public sealed class PuzzleSuiteTests
{
    [Fact]
    public void AnActionMatchIsASubsetOfTheActionsParameters()
    {
        if (!Official) return;
        (PuzzleSet set, RegretGame game) = Box("strategy1-petty-cash");
        FullMatchDefinition definition = RegretGames.Definition(game);
        GameState state = RegretGames.Positions(game, definition).Single(position => position.Sequence == 101).State;
        LegalAction petty = state.PendingDecision!.LegalActions.First(action =>
            new ActionMatch(ActionKind.PlayCard, new JsonObject { ["cardId"] = "def:35081" }).Matches(state, action));

        Assert.True(new ActionMatch().Matches(state, petty));
        Assert.True(new ActionMatch(ActionKind.PlayCard).Matches(state, petty));
        Assert.False(new ActionMatch(ActionKind.InstallCard).Matches(state, petty));
        Assert.False(new ActionMatch(ActionKind.PlayCard, new JsonObject { ["cardId"] = "def:30000" }).Matches(state, petty));
        Assert.False(new ActionMatch(ActionKind.PlayCard, new JsonObject { ["absent"] = 1 }).Matches(state, petty));
        LegalAction? install = state.PendingDecision.LegalActions.FirstOrDefault(action => action.Kind == ActionKind.InstallCard);
        if (install is not null)
            Assert.True(new ActionMatch(ActionKind.InstallCard, new JsonObject { ["candidateReplacementCardIds"] = new JsonArray() })
                .Matches(state, install));
        Assert.NotEmpty(set.Puzzles);
    }

    [Theory]
    [InlineData("gateway-elevation")]
    [InlineData("nsg-core-sets")]
    public void TheCommittedSetsLoadAndValidate(string pool)
    {
        PuzzleSet set = PuzzleSet.Load(SetDirectory(pool));
        Assert.NotEmpty(set.Puzzles);
        Assert.All(set.Puzzles, puzzle => Assert.Contains(set.Games, game => game.Id == puzzle.GameId));
    }

    /// <summary>The saved-decision tests of bot loops 1 and 2 and bot strategy 1, as puzzles: the defaults pass every one.</summary>
    [Theory]
    [InlineData("gateway-elevation")]
    [InlineData("nsg-core-sets")]
    public void TheDefaultsPassEveryConvertedTest(string pool)
    {
        if (!Official) return;
        PuzzleSet set = PuzzleSet.Load(SetDirectory(pool));
        var games = set.Games.ToDictionary(game => game.Id);
        Puzzle[] converted = [.. set.Puzzles.Where(puzzle => puzzle.Source?.Contains("Tests.", StringComparison.Ordinal) == true)];
        Assert.NotEmpty(converted);
        foreach (Puzzle puzzle in converted)
        {
            PuzzleResult result = PuzzleRunner.Run(puzzle, games[puzzle.GameId], (definition, side) => SavedDecision.Agent(definition, side, null));
            Assert.True(result.Passed, $"{puzzle.Id}: {result.Detail}");
        }
    }

    [Theory]
    [InlineData(CorpHeuristicPolicy.V30, true)]
    [InlineData(CorpHeuristicPolicy.V29, false)]
    public void ALinePuzzleJudgesTheTurnPlayed(string version, bool passes)
    {
        if (!Official) return;
        (_, RegretGame game) = Box("strategy1-petty-cash");
        var puzzle = new Puzzle("petty-cash-line", "Petty Cash leads the turn", "sequencing", Puzzle.LineKind, "tuning", game.Id, 101,
            PlayerSide.Corp, new PuzzleAcceptance(Line: [new LinePredicate("first", Match: new ActionMatch(ActionKind.PlayCard,
                new JsonObject { ["cardId"] = "def:35081" }))]));

        PuzzleResult result = PuzzleRunner.Run(puzzle, game, (definition, side) => SavedDecision.Agent(definition, side,
            side == PlayerSide.Corp ? version : null));

        Assert.Equal(passes, result.Passed);
        Assert.False(string.IsNullOrEmpty(result.Detail));
    }

    [Fact]
    public void ANestedPuzzleWhoseDecisionIsNotReachedFails()
    {
        if (!Official) return;
        (_, RegretGame game) = Box("strategy1-petty-cash");
        var puzzle = new Puzzle("never", "A decision the turn never reaches", "economy", Puzzle.NestedKind, "tuning", game.Id, 101, PlayerSide.Corp,
            new PuzzleAcceptance(Accept: [new ActionMatch(ActionKind.JackOut)], At: new ActionMatch(ActionKind.JackOut)));

        PuzzleResult result = PuzzleRunner.Run(puzzle, game, (definition, side) => SavedDecision.Agent(definition, side, null));

        Assert.False(result.Passed);
        Assert.Contains("not reached", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInvalidPuzzleIsRefused()
    {
        var puzzle = new Puzzle("bad", "", "nonsense", Puzzle.DecisionKind, "tuning", "g", 1, PlayerSide.Corp,
            new PuzzleAcceptance(Accept: [new ActionMatch()]));
        var game = new RegretGame("g", "c", 1, "a", "b", "train", "test", [], "h");
        Assert.Throws<InvalidDataException>(() => new PuzzleSet([game], [puzzle]).Validate());
        Assert.Throws<InvalidDataException>(() => new PuzzleSet([game], [puzzle with { Category = "economy", Kind = Puzzle.LineKind }]).Validate());
        new PuzzleSet([game], [puzzle with { Category = "economy" }]).Validate();
    }

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static string SetDirectory(string pool) =>
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "ai", "puzzles", pool);

    private static (PuzzleSet Set, RegretGame Game) Box(string game)
    {
        PuzzleSet set = PuzzleSet.Load(SetDirectory("gateway-elevation"));
        return (set, set.Games.Single(entry => entry.Id == game));
    }
}
