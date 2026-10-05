using System.Collections.Immutable;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match.Regret;
using Xunit;

namespace Netrunner.Match.Tests;

/// <summary>Bot plan S1: the regret suite's games, roots, teacher and report.</summary>
public sealed class RegretSuiteTests
{
    private const string Fixture = "strategy1-petty-cash";
    private const long CorpTurn = 101;

    [Fact]
    public void ASavedGameRoundTripsThroughItsCompactForm()
    {
        if (!Official) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Saved();
        RegretGame game = RegretGames.FromSaved(saved, definition, "train", "test");

        Assert.Equal(saved.Replay.ExpectedFinalStateHash, game.FinalStateHash);
        Assert.Equal(saved.Replay.Actions.Count, game.Choices.Length);
        RegretPosition[] positions = [.. RegretGames.Positions(game, RegretGames.Definition(game))];
        Assert.Equal(saved.Replay.Actions.Select(action => action.Sequence), positions.Select(position => position.Sequence));
        Assert.Equal(saved.Replay.Actions.Select(action => action.Submission.ActionId),
            positions.Select(position => position.State.PendingDecision!.LegalActions[position.Choice].Id));
    }

    [Fact]
    public void GroupsPartitionTheLegalActionsAndJoinOnlyTheSameChoice()
    {
        if (!Official) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Saved();
        foreach (RegretPosition position in SavedDecision.Positions(saved.Replay, definition).Where(position => position.Sequence % 7 == 0))
        {
            PendingDecision pending = position.State.PendingDecision!;
            IReadOnlyList<RegretCandidate> groups = RegretRoots.Group(position.State, pending);
            Assert.Equal(pending.LegalActions.Select(action => action.Id.Value).Order(StringComparer.Ordinal),
                groups.SelectMany(group => group.ActionIds).Order(StringComparer.Ordinal));
            foreach (RegretCandidate group in groups)
                Assert.Single(group.ActionIds.Select(id => pending.LegalActions.Single(action => action.Id.Value == id).Kind).Distinct());
        }
    }

    [Fact]
    public void TheTeacherGivesHiddenStateTwinsTheSameLabelsAndRepeatsItself()
    {
        if (!Official) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Saved();
        OfflineAgent teacher = (OfflineAgent)RatingLadder.CreateAgent("hard", PlayerSide.Corp, definition);
        GameState? root = null;
        foreach (RegretPosition position in SavedDecision.Positions(saved.Replay, definition))
        {
            AgentLifecycleHost.Observe(teacher, position.State, definition.Behavior);
            if (position.Sequence == CorpTurn) { root = position.State; break; }
        }
        GameState state = root ?? throw new InvalidOperationException("The fixture has no root.");
        GameState twin = TurnPlannerTests.SwapTwoHiddenCards(state, PlayerSide.Runner)
            ?? throw new InvalidOperationException("The fixture's Runner has no two different hidden cards.");
        ActionId[] candidates = [.. state.PendingDecision!.LegalActions.Take(3).Select(action => action.Id)];
        var settings = new TeacherSettings(Samples: 2, Seed: 7, HorizonTurns: 1);

        IReadOnlyList<TeacherValue> real = RolloutTeacher.Evaluate(teacher,
            ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime), candidates, settings);
        IReadOnlyList<TeacherValue> twinned = RolloutTeacher.Evaluate(teacher,
            ObservationBuilder.Build(twin, PlayerSide.Corp, definition.Behavior, definition.Runtime), candidates, settings);
        IReadOnlyList<TeacherValue> again = RolloutTeacher.Evaluate(teacher,
            ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime), candidates, settings);

        Assert.Equal(real, twinned);
        Assert.Equal(real, again);
        Assert.All(real, value => Assert.Equal(2, value.PlayOuts));
        Assert.All(real, value => Assert.InRange(value.Mean, 0, 1));
    }

    [Fact]
    public void EveryCandidateIsPlayedOnTheSameSamples()
    {
        if (!Official) return;
        // At this choice a candidate names a hidden card some deals replace (the review of #101).
        (LadderSavedGame saved, FullMatchDefinition definition) = Assert.Single(LadderGameArchive.Read(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games", "loop1-002")));
        RegretGame game = RegretGames.FromSaved(saved, definition, "held-out", "finding");
        RegretRoot root = Assert.Single(RegretRoots.At(game, new HashSet<long> { 131 }, new RegretSampling(), []));

        OfflineAgent teacher = (OfflineAgent)RatingLadder.CreateAgent("hard", root.Side, definition);
        GameState? state = null;
        foreach (RegretPosition position in RegretGames.Positions(game, definition))
        {
            AgentLifecycleHost.Observe(teacher, position.State, definition.Behavior);
            if (position.Sequence == root.Sequence) { state = position.State; break; }
        }
        IReadOnlyList<TeacherValue> values = RolloutTeacher.Evaluate(teacher,
            ObservationBuilder.Build(state!, root.Side, definition.Behavior, definition.Runtime),
            [.. root.Candidates.Select(candidate => new ActionId(candidate.ActionIds[0]))], new TeacherSettings(Samples: 16, Seed: 3, HorizonTurns: 1));

        // The same samples for every candidate, never one candidate's subset; a root with none is left unlabelled.
        int playOuts = Assert.Single(values.Select(value => value.PlayOuts).Distinct());
        Assert.InRange(playOuts, 0, 16);
        ImmutableArray<RegretLabel> labels = RegretLabels.Label(game, [root], new TeacherSettings(Samples: 16, Seed: 3, HorizonTurns: 1));
        Assert.Equal(playOuts == 0, labels.IsEmpty);
    }

    [Fact]
    public void TheReportRanksVersionsOnTheSameRootsAndMeasuresItsNoise()
    {
        RegretChoice Choice(string root, double regret) =>
            new(root, PlayerSide.Runner, RegretRoots.TurnKind, "held-out", "g", "", "", regret, regret == 0, regret < 0.05);
        RegretChoice[] better = [.. Enumerable.Range(0, 200).Select(index => Choice($"r{index}", index % 4 == 0 ? 0.1 : 0))];
        RegretChoice[] worse = [.. Enumerable.Range(0, 200).Select(index => Choice($"r{index}", index % 4 == 0 ? 0.2 : 0.05))];

        RegretSummary summary = RegretReport.Summarize("all", better);
        RegretComparison comparison = RegretReport.Compare("worse", worse, "better", better);

        Assert.Equal(0.025, summary.MeanRegret, 6);
        Assert.Equal(0.75, summary.Agreement, 6);
        Assert.InRange(summary.MeanRegret, summary.Lower, summary.Upper);
        Assert.Equal(summary, RegretReport.Summarize("all", better));
        Assert.True(comparison.Separated);
        Assert.Equal(0.0625, comparison.MeanDifference, 6);

        RegretLabel Label(string root, string best) => new(root, 8, 4, 4, 1,
            [new RegretValue("a", best == "a" ? 0.6 : 0.4, 0.1, 8), new RegretValue("b", best == "b" ? 0.6 : 0.4, 0.1, 8)]);
        RegretNoise noise = RegretReport.Noise([Label("x", "a"), Label("y", "a")], [Label("x", "a"), Label("y", "b")]);
        Assert.Equal(2, noise.Roots);
        Assert.Equal(0.5, noise.BestDisagreement, 6);
        Assert.Equal(0.1, noise.MedianStandardError, 6);
    }

    [Fact]
    public void TheCommittedBoxSuiteIsWhole()
    {
        RegretSuite suite = RegretSuite.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "ai", "regret",
            "gateway-elevation"));
        HashSet<string> games = [.. suite.Games.Select(game => game.Id)];
        HashSet<string> roots = [.. suite.Roots.Select(root => root.Id)];

        Assert.Equal(suite.Games.Length, games.Count);
        Assert.Equal(suite.Roots.Length, roots.Count);
        Assert.All(suite.Roots, root => Assert.Contains(root.GameId, games));
        Assert.All(suite.Roots, root => Assert.True(root.Candidates.Length >= 1));
        Assert.All(suite.Roots, root => Assert.Equal(root.Candidates.Length, root.Candidates.Select(candidate => candidate.Group).Distinct().Count()));
        // A root no sample could play every candidate on is left unlabelled; there are few.
        Assert.Subset(roots, suite.Labels.Select(label => label.RootId).ToHashSet());
        Assert.True(suite.Labels.Length >= suite.Roots.Length * 0.99);
        var byId = suite.Roots.ToDictionary(root => root.Id);
        // S1.4 extended the roots to every meaningful group; a label holds the groups labelled so far, each once.
        Assert.All(suite.Labels, label => Assert.Subset(byId[label.RootId].Candidates.Select(candidate => candidate.Group).ToHashSet(),
            label.Values.Select(value => value.Group).ToHashSet()));
        Assert.All(suite.Labels, label => Assert.Equal(label.Values.Length, label.Values.Select(value => value.Group).Distinct().Count()));
        Assert.Contains(suite.Roots, root => root.Categories.Contains("finding"));
    }

    private static bool Official => CardDataResource.EmbeddedMode == CardDataResource.OfficialMode;

    private static (LadderSavedGame Saved, FullMatchDefinition Definition) Saved() => Assert.Single(LadderGameArchive.Read(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "saved-games", Fixture)));
}
