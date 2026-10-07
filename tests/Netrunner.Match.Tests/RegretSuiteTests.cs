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

    /// <summary>
    /// The S1.5 follow-up's exposure count: every agenda the Corp installs in a remote is followed to where it went, and
    /// one the Runner stole ends in its score area while one the Corp scored ends in the Corp's.
    /// </summary>
    [Fact]
    public void AnAgendaInstalledInARemoteIsFollowedToWhereItWent()
    {
        if (!Official) return;
        RegretSuite suite = RegretSuite.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "ai", "regret",
            "gateway-elevation"));
        var fates = suite.Games.Where(game => game.Source == "ladder").Take(6).SelectMany(game => RemoteExposure.FromGame(game)).ToList();

        Assert.NotEmpty(fates);
        Assert.All(fates, fate => Assert.Contains(fate.Outcome, new[] { RemoteExposure.Scored, RemoteExposure.Stolen, RemoteExposure.Trashed,
            RemoteExposure.Installed, "installed-at-end" }));
        Assert.All(fates, fate => Assert.True(fate.Runs >= 0 && fate.Turns >= 0));
        Assert.Contains(fates, fate => fate.Outcome == RemoteExposure.Scored);
        // The run model is asked once, at the Runner's next turn, unless the agenda was gone by then.
        Assert.All(fates.Where(fate => fate.Breakable is null), fate => Assert.True(fate.Outcome != RemoteExposure.Stolen || fate.Runs > 0));
        Assert.Equal(fates.Select(fate => (fate.Source, fate.Outcome, fate.Runs)),
            suite.Games.Where(game => game.Source == "ladder").Take(6).SelectMany(game => RemoteExposure.FromGame(game)).Select(fate => (fate.Source, fate.Outcome, fate.Runs)));
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
        // The default teacher is hard-model (its rollouts model the live opponent, not the pinned one) and labels name it;
        // plain Hard, as labels-v1 was made, is recorded as no name.
        Assert.All(labels, label => Assert.Equal(RegretLabels.DefaultTeacher, label.Teacher));
        Assert.All(RegretLabels.Label(game, [root], new TeacherSettings(Samples: 4, Seed: 3, HorizonTurns: 1), "hard"),
            label => Assert.Null(label.Teacher));
    }

    /// <summary>
    /// Bot plan S1.7, adaptive samples: the deciding samples are discarded and the label records the recording samples it
    /// used, between the first step and the cap, with every group played on all of them.
    /// </summary>
    [Fact]
    public void AdaptiveSamplesRecordOnlyTheSamplesDrawnAfterTheDecision()
    {
        if (!Official) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Saved();
        RegretGame game = RegretGames.FromSaved(saved, definition, "held-out", "finding");
        RegretRoot root = Assert.Single(RegretRoots.At(game, new HashSet<long> { CorpTurn }, new RegretSampling(), []));
        var adaptive = new TeacherSettings(Samples: 4, Seed: 3, HorizonTurns: 1, Decide: 4, ExtendTo: 12, ExtendStep: 4);

        RegretLabel label = Assert.Single(RegretLabels.Label(game, [root], adaptive));
        RegretLabel again = Assert.Single(RegretLabels.Label(game, [root], adaptive));

        Assert.Equal(4, label.Decide);
        Assert.InRange(label.Samples, 1, 12);
        Assert.All(label.Values, value => Assert.Equal(label.Samples, value.PlayOuts));
        Assert.Equal(label.Values.Select(value => value.Mean), again.Values.Select(value => value.Mean));
        // The deciding samples are not the recording ones: a flat labelling's first samples differ from the recorded.
        RegretLabel flat = Assert.Single(RegretLabels.Label(game, [root], adaptive with { Decide = 0, ExtendTo = 0, Samples = label.Samples }));
        Assert.NotEqual(flat.Values.Select(value => value.Mean), label.Values.Select(value => value.Mean));
    }

    /// <summary>
    /// Bot plan S4.2b: labelling with end-of-line rows records, for lines played past the root side's turn, set 5 of the
    /// state where the turn ended and the play-out's value from it, and labels exactly as without them.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void EndOfLineRowsAreTheStatesWhereTheTurnEndedAndLeaveTheLabelsAlone(int lines)
    {
        if (!Official) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Saved();
        RegretGame game = RegretGames.FromSaved(saved, definition, "held-out", "finding");
        RegretRoot root = Assert.Single(RegretRoots.At(game, new HashSet<long> { CorpTurn }, new RegretSampling(), []));
        var settings = new TeacherSettings(Samples: 3, Seed: 3, HorizonTurns: 2, Lines: lines);
        var rows = new List<EndLineRow>();
        var repeated = new List<EndLineRow>();

        RegretLabel label = Assert.Single(RegretLabels.Label(game, [root], settings, endLines: rows));
        RegretLabel withCeiling = Assert.Single(RegretLabels.Label(game, [root], settings, endLines: repeated, repeatRoots: new HashSet<string> { root.Id }));

        Assert.Equal(Assert.Single(RegretLabels.Label(game, [root], settings)).Values.Select(value => value.Mean), label.Values.Select(value => value.Mean));
        Assert.Equal(label.Values.Select(value => value.Mean), withCeiling.Values.Select(value => value.Mean));
        Assert.NotEmpty(rows);
        // S4.2c: every row names its sample and line; the ceiling's repeat changes nothing else and is repeatable.
        Assert.All(rows, row => Assert.True(row.Sample >= 0 && row.Line is not null && row.Redeal is null && row.Reseed is null, $"{row.Sample} {row.Line}"));
        Assert.Equal(rows.Select(row => (row.Sample, row.Line, row.Value)), repeated.Select(row => (row.Sample, row.Line, row.Value)));
        Assert.All(repeated, row => Assert.InRange(row.Redeal ?? -1, 0, 1));
        Assert.All(repeated, row => Assert.InRange(row.Reseed ?? -1, 0, 1));
        var again = new List<EndLineRow>();
        RegretLabels.Label(game, [root], settings, endLines: again, repeatRoots: new HashSet<string> { root.Id });
        Assert.Equal(repeated.Select(row => (row.Redeal, row.Reseed)), again.Select(row => (row.Redeal, row.Reseed)));
        Assert.Equal(rows.Count, rows.Select(row => (row.Sample, row.Line)).Distinct().Count());
        // Every line played has a row on every sample (memo hits, and games that end inside the turn, included).
        Assert.All(rows.GroupBy(row => row.Line), line => Assert.Equal(label.Samples, line.Count()));
        // At most one row a line played to the horizon: each group's lines on each sample, memo hits included (S4.2c).
        Assert.True(rows.Count <= label.Values.Length * settings.Samples * Math.Max(2, lines), $"{rows.Count} rows");
        Assert.All(rows, row =>
        {
            Assert.Equal((root.Id, root.Side, "held-out"), (row.RootId, row.Side, row.Split));
            Assert.Equal(EvaluationFeatures.NamesFor(5).Count, row.Features.Length);
            Assert.InRange(row.Value, 0, 1);
        });
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

    /// <summary>
    /// Bot plan S1.6, pruning: every group is valued on the deciding samples, only the best few and the required ones get
    /// the recording samples, and a pruned group keeps its deciding value, marked, and is never the root's best.
    /// </summary>
    [Fact]
    public void PruningRecordsTheBestGroupsAndTheRequiredOnes()
    {
        if (!Official) return;
        (LadderSavedGame saved, FullMatchDefinition definition) = Saved();
        RegretGame game = RegretGames.FromSaved(saved, definition, "held-out", "finding");
        RegretRoot root = Assert.Single(RegretRoots.At(game, new HashSet<long> { CorpTurn }, new RegretSampling(), []));
        Assert.True(root.Candidates.Length > 3);
        // The last group is a version's choice at a targeted root: recorded whatever the deciding samples say.
        string required = root.Candidates[^1].Group;
        RegretRoot targeted = root with { Categories = [.. root.Categories, RegretRoots.DivergeCategory, $"{RegretRoots.DivergeCategory}:x={required}"] };

        RegretLabel label = Assert.Single(RegretLabels.Label(game, [targeted],
            new TeacherSettings(Samples: 4, Seed: 3, HorizonTurns: 1, Decide: 4, Prune: 2)));

        RegretValue[] recorded = [.. label.Values.Where(value => !value.Pruned)];
        Assert.InRange(recorded.Length, 2, 3);
        Assert.Contains(recorded, value => value.Group == required);
        Assert.All(recorded, value => Assert.Equal(label.Samples, value.PlayOuts));
        Assert.Contains(label.Values, value => value.Pruned);
        Assert.False(label.Best!.Pruned);
        Assert.Equal((4, 2), (label.Decide, label.Prune));
    }

    /// <summary>Bot plan S1.6: a targeted root is a decision where the two versions chose different groups, and both are labelled.</summary>
    [Fact]
    public void ADivergentRootRecordsTwoDifferentChoices()
    {
        if (!Official) return;
        RegretSuite suite = RegretSuite.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "ai", "regret",
            "gateway-elevation"));
        RegretGame game = suite.Games.First(entry => entry.Source == "ladder");
        ImmutableArray<RegretRoot> roots = RegretRoots.Diverge(game, CorpHeuristicPolicy.V29, CorpHeuristicPolicy.V30, PlayerSide.Corp,
            (definition, side, version) => SavedDecision.Agent(definition, side, version), new RegretSampling());

        Assert.All(roots, root =>
        {
            IReadOnlySet<string> chosen = RegretRoots.DivergeChoices(root);
            Assert.Equal(2, chosen.Count);
            Assert.All(chosen, group => Assert.Contains(root.Candidates, candidate => candidate.Group == group));
            Assert.Equal(PlayerSide.Corp, root.Side);
        });
        Assert.Equal(roots.Select(root => root.Id), RegretRoots.Diverge(game, CorpHeuristicPolicy.V29, CorpHeuristicPolicy.V30, PlayerSide.Corp,
            (definition, side, version) => SavedDecision.Agent(definition, side, version), new RegretSampling()).Select(root => root.Id));
    }

    /// <summary>
    /// Bot plan S4.1: evaluation set 5 on real positions of the box suite, from both sides with the teacher's knowledge:
    /// every value is finite, and set 5's own terms vary.
    /// </summary>
    /// <summary>S4.2: a game's labelled roots become fitting rows, each the acting side's sets 4 and 5, the teacher's root value and the outcome.</summary>
    [Fact]
    public void TeacherFitRowsAreTheLabelledRootsOfAFinishedGame()
    {
        if (!Official) return;
        RegretSuite suite = RegretSuite.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "ai", "regret",
            "gateway-elevation"));
        RegretGame game = suite.Games.First(entry => entry.Source == "ladder");
        RegretRoot[] roots = [.. suite.Roots.Where(root => root.GameId == game.Id).Take(3)];
        var labelled = roots.Select((root, index) => (root, new RegretLabel(root.Id, 32, 4, 4, 1,
            [new(root.Candidates[0].Group, 0.4 + index / 10.0, 0.02, 32), new(root.Candidates[1].Group, 0.1, 0.02, 32)], 5, 1, "hard-model"))).ToArray();

        IReadOnlyList<TeacherFitRow> rows = TeacherFit.Collect(game, labelled);

        Assert.Equal(roots.Select(root => root.Id), rows.Select(row => row.RootId));
        Assert.Equal([0.4, 0.5, 0.6], rows.Select(row => Math.Round(row.Teacher, 6)));
        Assert.All(rows, row =>
        {
            Assert.Equal(EvaluationFeatures.NamesFor(4).Count, row.Set4.Length);
            Assert.Equal(row.Set4, row.Set5[..row.Set4.Length]);
            Assert.True(row.Won is 0 or 1, $"won {row.Won}");
        });
        Assert.Equal(rows.Select(row => row.Set5), TeacherFit.Collect(game, labelled).Select(row => row.Set5));
    }

    [Fact]
    public void SetFiveReadsRealPositions()
    {
        if (!Official) return;
        RegretSuite suite = RegretSuite.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "ai", "regret",
            "gateway-elevation"));
        IReadOnlyList<string> names = EvaluationFeatures.NamesFor(5);
        int own = names.Count - EvaluationFeatures.NamesFor(4).Count;
        var seen = Enumerable.Range(0, names.Count).Select(_ => new HashSet<double>()).ToArray();
        foreach (RegretGame game in suite.Games.Where(entry => entry.Source == "ladder").Take(4))
        {
            FullMatchDefinition definition = RegretGames.Definition(game);
            var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
            var agents = new[] { PlayerSide.Corp, PlayerSide.Runner }.ToDictionary(side => side,
                side => (OfflineAgent)RatingLadder.CreateAgent(RegretLabels.DefaultTeacher, side, definition));
            foreach (RegretPosition position in RegretGames.Positions(game, definition))
            {
                foreach (OfflineAgent agent in agents.Values) AgentLifecycleHost.Observe(agent, position.State, definition.Behavior);
                if (position.Sequence % 7 != 0) continue;
                foreach ((PlayerSide side, OfflineAgent agent) in agents)
                {
                    AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(position.State, side, definition.Behavior, definition.Runtime),
                        agent.Configuration.FeatureVersion);
                    double[] values = EvaluationFeatures.Extract(features, catalog, 5, agent.Checkpoint.Knowledge);
                    Assert.Equal(names.Count, values.Length);
                    Assert.All(values, value => Assert.True(double.IsFinite(value)));
                    for (int index = 0; index < values.Length; index++) seen[index].Add(Math.Round(values[index], 6));
                }
            }
        }
        // A punisher the Runner cannot survive (a castable one, the threat met, a grip under its damage) is rare in self-play,
        // where v26 keeps the grip up against it; its unit test covers it.
        Assert.All(Enumerable.Range(names.Count - own, own).Where(index => names[index] != "punisher-exposure"),
            index => Assert.True(seen[index].Count > 1, $"{names[index]} never varies."));
    }

    /// <summary>Bot plan S1.7: a label names its teacher's version, and a root whose values come from two is refused.</summary>
    [Fact]
    public void ALabelNamesItsTeacherAndARootMixingTwoIsRefused()
    {
        Assert.Equal("v1", RegretLabels.VersionOf(0, 0, 4, 4, "hard"));
        Assert.Equal("v1-lines", RegretLabels.VersionOf(5, 1, 4, 4, "hard"));
        Assert.Equal("v2", RegretLabels.VersionOf(5, 1, 4, 4, "hard-model"));
        // Other settings are named by them and by the live policies the teacher plays.
        Assert.Equal($"hard-model-lines5-screen1-horizon8-set4-{CorpHeuristicPolicy.PolicyVersion}-{RunnerHeuristicPolicy.PolicyVersion}",
            RegretLabels.VersionOf(5, 1, 8, 4, "hard-model"));
        var label = new RegretLabel("r#1", 32, 4, 4, 1, [new("a|1", 0.5, 0.02, 32), new("b|1", 0.4, 0.02, 32)], 5, 1, "hard-model");
        Assert.Equal("v2", label.TeacherName);
        label.EnsureOneTeacher();
        (label with { Values = [.. label.Values, new("c|1", 0.3, 0.02, 32, TeacherVersion: "v2")] }).EnsureOneTeacher();
        Assert.Throws<InvalidDataException>(() => (label with { Values = [.. label.Values, new("c|1", 0.3, 0.02, 32, TeacherVersion: "v1")] })
            .EnsureOneTeacher());
    }

    /// <summary>Bot plan S1.7: the core is drawn by deal, in the suite's held-out share, and a new marking replaces the old.</summary>
    [Fact]
    public void TheCoreIsDrawnByDealInTheHeldOutShare()
    {
        RegretSuite suite = RegretSuite.Load(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "ai", "regret",
            "gateway-elevation"));
        RegretSuite marked = suite.WithCore(2000, 7);
        RegretRoot[] core = [.. marked.Roots.Where(root => root.Categories.Contains(RegretSuite.CoreCategory))];

        // A deal's roots are wholly in or out; the largest deal bounds how far the count can overshoot.
        var games = suite.Games.ToDictionary(game => game.Id);
        var byDeal = marked.Roots.GroupBy(root => games[root.GameId] is var game && game.Source == "finding" ? game.Id : $"{game.ContentId}|{game.GameSeed}");
        Assert.All(byDeal, deal => Assert.Single(deal.Select(root => root.Categories.Contains(RegretSuite.CoreCategory)).Distinct()));
        int largest = byDeal.Max(deal => deal.Count());
        Assert.InRange(core.Length, 2000, 2000 + 2 * largest);
        double share = suite.Roots.Count(root => root.Split == "held-out") / (double)suite.Roots.Length;
        Assert.InRange(core.Count(root => root.Split == "held-out") / (double)core.Length, share - 0.03, share + 0.03);
        Assert.Equal(core.Select(root => root.Id), marked.WithCore(2000, 7).Roots.Where(root => root.Categories.Contains(RegretSuite.CoreCategory))
            .Select(root => root.Id));
        Assert.NotEqual(core.Select(root => root.Id), marked.WithCore(2000, 8).Roots.Where(root => root.Categories.Contains(RegretSuite.CoreCategory))
            .Select(root => root.Id));
        Assert.All(marked.WithCore(500, 7).Roots, root => Assert.True(root.Categories.Count(category => category == RegretSuite.CoreCategory) <= 1));
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
