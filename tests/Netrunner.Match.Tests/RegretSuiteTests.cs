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

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void TheTeacherGivesHiddenStateTwinsTheSameLabelsAndRepeatsItself(int lines)
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
        var settings = new TeacherSettings(Samples: lines > 0 ? 4 : 2, Seed: 7, HorizonTurns: 1, Lines: lines);

        IReadOnlyList<TeacherValue> real = RolloutTeacher.Evaluate(teacher,
            ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime), candidates, settings);
        IReadOnlyList<TeacherValue> twinned = RolloutTeacher.Evaluate(teacher,
            ObservationBuilder.Build(twin, PlayerSide.Corp, definition.Behavior, definition.Runtime), candidates, settings);
        IReadOnlyList<TeacherValue> again = RolloutTeacher.Evaluate(teacher,
            ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime), candidates, settings);

        // A line is a list, so the values are compared by their contents.
        static string Key(TeacherValue value) =>
            $"{value.Action.Value} {value.Mean:R} {value.StandardError:R} {value.PlayOuts} {string.Join(',', value.Line ?? [])}";
        Assert.Equal(real.Select(Key), twinned.Select(Key));
        Assert.Equal(real.Select(Key), again.Select(Key));
        Assert.All(real, value => Assert.Equal(settings.Samples, value.PlayOuts));
        Assert.All(real, value => Assert.InRange(value.Mean, 0, 1));
        Assert.All(real, value => Assert.Equal(lines > 0, value.Line is not null));
    }

    /// <summary>
    /// Bot plan S1.5: lines that leave the turn in states with equal fingerprints share one play-out in the teacher's memo,
    /// which is exact only if those play-outs are equal. This plays lines without sharing and checks it, so a state field
    /// left out of the fingerprint that changes play fails here.
    /// </summary>
    [Fact]
    public void LinesThatLeaveTheTurnInOneStateHaveOnePlayOut()
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
        ObservationDeterminizer determinizer = ObservationBuilder.Build(state, PlayerSide.Corp, definition.Behavior, definition.Runtime).Determinizer!;
        var planner = new TurnPlanner(teacher.Policy, teacher.Configuration, teacher.Catalog);
        AgentKnowledge knowledge = teacher.Checkpoint.Knowledge;
        GameState sample = TurnPlanner.Deal(PlayerSide.Corp, knowledge, planner.PoolFor(PlayerSide.Corp, knowledge, fromList: true),
            new PlanningContext(determinizer, new DeterministicRandom(11)), pinned: true);
        int[][] lines = [[], [1], [2], [0, 1], [1, 0], [1, 1], [0, 2], [2, 0], [1, 2], [2, 1]];

        var ends = new Dictionary<string, double>(StringComparer.Ordinal);
        int shared = 0;
        foreach (LegalAction first in state.PendingDecision!.LegalActions.Take(8))
            foreach (int[] ranks in lines)
            {
                // A memo of its own, so the line is played out whole and the memo only records where it left the turn.
                var memo = new Dictionary<string, double>(StringComparer.Ordinal);
                double? value = planner.PlayLine(sample, first.Id, ranks, PlayerSide.Corp, knowledge, determinizer, 1, 4,
                    PlannerProbe.GameRolloutCap, out _, memo);
                if (value is not { } played || memo.Count == 0) continue;
                (string end, double recorded) = Assert.Single(memo);
                Assert.Equal(played, recorded);
                if (ends.TryGetValue(end, out double earlier))
                {
                    shared++;
                    Assert.Equal(earlier, played);
                }
                else ends[end] = played;
            }
        Assert.True(shared > 0, "No two lines left the turn in one state, so nothing was checked.");
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
    public void TheDiscordantViewComparesTwoVersionsWhereTheyChooseDifferently()
    {
        RegretChoice Choice(string root, string group, double regret) =>
            new(root, PlayerSide.Corp, RegretRoots.TurnKind, "train", group, group, "", regret, regret == 0, regret < 0.05);
        string[] ids = [.. Enumerable.Range(0, 10).Select(index => $"r{index}")];
        // The two versions agree at seven roots and differ at three, where the first is 0.1 worse.
        RegretChoice[] first = [.. ids.Select((id, index) => index < 3 ? Choice(id, "InstallCard|a", 0.1) : Choice(id, "DrawCard|c", 0))];
        RegretChoice[] second = [.. ids.Select((id, index) => index < 3 ? Choice(id, "GainCredit|b", 0) : Choice(id, "DrawCard|c", 0))];
        var labels = ids.ToDictionary(id => id, id => new RegretLabel(id, 32, 4, 4, 1,
            [new RegretValue("GainCredit|b", 0.6, 0.02, 32), new RegretValue("InstallCard|a", 0.5, 0.02, 32), new RegretValue("DrawCard|c", 0.6, 0.02, 32)]));

        RegretDiscordance discordance = RegretReport.Discordance("one", first, "two", second, new Dictionary<string, RegretRoot>(), labels);

        Assert.Equal(["r0", "r1", "r2"], discordance.Discords.Select(discord => discord.RootId));
        RegretDiscord discord = discordance.Discords[0];
        Assert.Equal(("InstallCard|a", 0.5, "GainCredit|b", 0.6), (discord.FirstChosen, discord.FirstValue, discord.SecondChosen, discord.SecondValue));
        Assert.Equal((3, 0.1), (discordance.Discordant.Roots, Math.Round(discordance.Discordant.MeanDifference, 9)));
        Assert.Equal((10, 0.03), (discordance.All.Roots, Math.Round(discordance.All.MeanDifference, 9)));
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
