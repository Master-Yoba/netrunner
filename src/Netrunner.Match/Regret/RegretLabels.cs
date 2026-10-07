using System.Collections.Immutable;
using System.Globalization;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match.Regret;

/// <summary>
/// Labels a game's roots with the teacher (bot plan S1.2). The teacher is the latest Hard bot of the acting side, which
/// has watched the game to the root: it knows the opponent's list and its tracker's beliefs, and deals from both. Its
/// rollouts model the opponent by the live Standard version (<see cref="DefaultTeacher"/>).
/// </summary>
public static class RegretLabels
{
    /// <summary>
    /// The teacher every labelling uses unless told otherwise (S1.5 follow-up, 6 October): Hard modelling the opponent by
    /// the live Standard version (<c>plan-model 1</c>). Plain Hard models it by the planner's pinned versions (Runner v7,
    /// Corp v8), which no label meant to train an evaluation should learn from; labels name a teacher other than Hard.
    /// </summary>
    public const string DefaultTeacher = "hard-model";

    /// <summary>
    /// Bot plan S1.7: the teacher's version, frozen between milestones and named on every label and score. v1 is S1.2's
    /// one-step teacher; v1-lines S1.5's line search with the pinned opponent (Runner v7, Corp v8); v2 the line search
    /// (beam 5, a one-turn screen on a quarter of the samples, two finalists) with the live Standard opponent, a 4-turn
    /// horizon and set 4 at the leaf. Any other settings are named by them. The teacher is the live Hard bot of each side and,
    /// for hard-model, the live Standard of the other, so v2 is v2 only while those are Corp v31 and Runner v28: after a
    /// policy bump the name carries the versions, and a label never claims a teacher it was not made by. Labels are tagged
    /// with their version when written (<c>ai-regret tag</c> for files made before), so a bump never renames old labels.
    /// </summary>
    public static string VersionOf(int lines, int screenTurns, int horizonTurns, int evaluation, string teacher) =>
        (lines, screenTurns, horizonTurns, evaluation, teacher) switch
        {
            ( <= 1, _, 4, 4, "hard") => "v1",
            (5, 1, 4, 4, "hard") => "v1-lines",
            (5, 1, 4, 4, "hard-model") when CorpHeuristicPolicy.PolicyVersion == CorpHeuristicPolicy.V31
                && RunnerHeuristicPolicy.PolicyVersion == RunnerHeuristicPolicy.V28 => "v2",
            _ => string.Create(CultureInfo.InvariantCulture,
                $"{teacher}-lines{lines}-screen{screenTurns}-horizon{horizonTurns}-set{evaluation}-{CorpHeuristicPolicy.PolicyVersion}-{RunnerHeuristicPolicy.PolicyVersion}"),
        };

    /// <summary>The version the teacher's settings are, as <see cref="VersionOf"/> names it.</summary>
    public static string VersionOf(TeacherSettings settings, string teacher)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return VersionOf(settings.Lines, settings.Lines > 1 ? settings.ScreenTurns : 0, settings.HorizonTurns, settings.Evaluation, teacher);
    }

    /// <param name="teacher">The ladder entrant that teaches: Hard, or <c>hard-model</c>, Hard modelling the opponent by the
    /// live Standard version instead of the planner's pinned model (Runner v7, Corp v8).</param>
    /// <param name="endLines">Bot plan S4.2b: receives a row for every line played to the horizon (the end-of-turn state's
    /// feature set 5 and the play-out's value from it, with its sample and line, S4.2c), in play order.</param>
    /// <param name="repeatRoots">S4.2c, the ceiling: roots whose rows also get a second, independent play-out.</param>
    public static ImmutableArray<RegretLabel> Label(RegretGame game, IReadOnlyList<RegretRoot> roots, TeacherSettings settings,
        string teacher = DefaultTeacher, ICollection<EndLineRow>? endLines = null, IReadOnlySet<string>? repeatRoots = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(settings);
        if (roots.Count == 0) return [];
        FullMatchDefinition definition = RegretGames.Definition(game);
        var bySequence = roots.ToDictionary(root => root.Sequence);
        var teachers = new Dictionary<PlayerSide, OfflineAgent>
        {
            [PlayerSide.Corp] = Teacher(definition, PlayerSide.Corp, teacher),
            [PlayerSide.Runner] = Teacher(definition, PlayerSide.Runner, teacher),
        };
        var labels = ImmutableArray.CreateBuilder<RegretLabel>(roots.Count);
        RegretGames.Replay(game, definition, position =>
        {
            foreach (OfflineAgent teacher in teachers.Values) AgentLifecycleHost.Observe(teacher, position.State, definition.Behavior);
            if (!bySequence.TryGetValue(position.Sequence, out RegretRoot? root)) return;
            Observation observation = ObservationBuilder.Build(position.State, root.Side, definition.Behavior, definition.Runtime);
            TeacherSettings rooted = settings with { Seed = settings.Seed ^ RegretRoots.StableHash(root.Id) };
            // A targeted root's chosen groups are always recorded (S1.6).
            IReadOnlySet<string> chosen = RegretRoots.DivergeChoices(root);
            IReadOnlyList<TeacherValue> values = RolloutTeacher.Evaluate(teachers[root.Side], observation,
                [.. root.Candidates.Select(candidate => new ActionId(candidate.ActionIds[0]))], rooted,
                root.Candidates.Select((candidate, index) => (candidate, index)).Where(entry => chosen.Contains(entry.candidate.Group))
                    .Select(entry => entry.index).ToHashSet(),
                endLines is null ? null : line => endLines.Add(new EndLineRow(root.Id, root.GameId, root.Side, root.Split,
                    [.. line.Features.Select(feature => Math.Round(feature, 4))], Math.Round(line.Value, 5), line.Sample,
                    $"{root.Candidates[line.Candidate].Group}|{string.Join('.', line.Ranks)}",
                    line.Redeal is { } redeal ? Math.Round(redeal, 5) : null, line.Reseed is { } reseed ? Math.Round(reseed, 5) : null)),
                repeatRoots?.Contains(root.Id) == true);
            // A root no sample could play every candidate on has no label: it is scored as unlabelled.
            if (values.Any(value => value.PlayOuts == 0)) return;
            // With adaptive samples the label records the recording samples the root used, and the deciding ones.
            labels.Add(new RegretLabel(root.Id, settings.Decide > 0 ? values.Where(value => !value.Pruned).Max(value => value.PlayOuts) : settings.Samples,
                settings.HorizonTurns, settings.Evaluation, rooted.Seed,
                [.. root.Candidates.Zip(values, Value)], settings.Lines, settings.Lines > 1 ? settings.ScreenTurns : 0,
                teacher == "hard" ? null : teacher, VersionOf(settings, teacher), settings.Decide, settings.Decide > 0 ? settings.Prune : 0));
        });
        return labels.ToImmutable();
    }

    /// <summary>
    /// Labels further groups at roots that have a label (S1.4, <c>ai-regret score --label-missing</c>): each with the root's
    /// own settings and seed, so the new groups are played on the samples the root's other groups were.
    /// </summary>
    public static IReadOnlyDictionary<string, ImmutableArray<RegretValue>> LabelGroups(RegretGame game,
        IReadOnlyList<(RegretRoot Root, RegretLabel Label, IReadOnlyList<RegretCandidate> Groups)> requests)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(requests);
        var values = new Dictionary<string, ImmutableArray<RegretValue>>(StringComparer.Ordinal);
        if (requests.Count == 0) return values;
        FullMatchDefinition definition = RegretGames.Definition(game);
        var bySequence = requests.ToDictionary(request => request.Root.Sequence);
        // A game's labels share their teacher, so its first request names it.
        string teacher = requests[0].Label.Teacher ?? "hard";
        var teachers = new Dictionary<PlayerSide, OfflineAgent>
        {
            [PlayerSide.Corp] = Teacher(definition, PlayerSide.Corp, teacher),
            [PlayerSide.Runner] = Teacher(definition, PlayerSide.Runner, teacher),
        };
        RegretGames.Replay(game, definition, position =>
        {
            foreach (OfflineAgent teacher in teachers.Values) AgentLifecycleHost.Observe(teacher, position.State, definition.Behavior);
            if (!bySequence.TryGetValue(position.Sequence, out var request)) return;
            // The root's recording samples: its deciding ones are drawn and passed over first.
            var settings = new TeacherSettings(request.Label.Samples, request.Label.Seed, request.Label.HorizonTurns, request.Label.Evaluation,
                Lines: request.Label.Lines, ScreenTurns: request.Label.ScreenTurns, Decide: request.Label.Decide);
            IReadOnlyList<TeacherValue> found = RolloutTeacher.Evaluate(teachers[request.Root.Side],
                ObservationBuilder.Build(position.State, request.Root.Side, definition.Behavior, definition.Runtime),
                [.. request.Groups.Select(group => new ActionId(group.ActionIds[0]))], settings);
            // A value added to a label names the teacher it came from, so score can refuse a root that mixes two.
            values[request.Root.Id] = [.. request.Groups.Zip(found, Value).Where(value => value.PlayOuts > 0)
                .Select(value => value with { TeacherVersion = VersionOf(settings, teacher) })];
        });
        return values;
    }

    private static RegretValue Value(RegretCandidate candidate, TeacherValue value) =>
        new(candidate.Group, value.Mean, value.StandardError, value.PlayOuts, value.Line is { } line ? [.. line] : null, Pruned: value.Pruned);

    private static OfflineAgent Teacher(FullMatchDefinition definition, PlayerSide side, string teacher) =>
        RatingLadder.CreateAgent(teacher, side, definition) as OfflineAgent
            ?? throw new InvalidOperationException("The Hard bot is not an offline agent.");
}

/// <summary>A version's choice at one root, and its regret against the teacher's labels.</summary>
/// <param name="Chosen">The chosen group, or null when the choice is not one of the labelled groups.</param>
/// <param name="Regret">The best labelled value minus the chosen group's, in win probability; null when unlabelled.</param>
/// <param name="Agrees">Whether the chosen group is the teacher's best.</param>
/// <param name="AgreesWithinNoise">Whether the chosen group is within the two standard errors' sum of the best.</param>
/// <param name="Missing">The chosen group when it is not one of the labelled groups (S1.4: labelled on demand).</param>
public sealed record RegretChoice(string RootId, PlayerSide Side, string Kind, string Split, string? Chosen, string ChosenDescription,
    string Reason, double? Regret, bool? Agrees, bool? AgreesWithinNoise, RegretCandidate? Missing = null, bool Pruned = false);

/// <summary>Asks a policy version to choose at every root of a game, through the production path (bot plan S1.3).</summary>
public static class RegretScores
{
    /// <param name="agent">Makes the agent being scored for a side, or null when the version does not play that side.</param>
    public static ImmutableArray<RegretChoice> Choose(RegretGame game, IReadOnlyList<RegretRoot> roots,
        IReadOnlyDictionary<string, RegretLabel> labels, Func<FullMatchDefinition, PlayerSide, OfflineAgent?> agent)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(agent);
        FullMatchDefinition definition = RegretGames.Definition(game);
        var agents = new Dictionary<PlayerSide, OfflineAgent>();
        foreach (PlayerSide side in roots.Select(root => root.Side).Distinct())
            if (agent(definition, side) is { } created) agents[side] = created;
        var bySequence = roots.Where(root => agents.ContainsKey(root.Side) && labels.ContainsKey(root.Id)).ToDictionary(root => root.Sequence);
        if (bySequence.Count == 0) return [];
        var choices = ImmutableArray.CreateBuilder<RegretChoice>(bySequence.Count);
        RegretGames.Replay(game, definition, position =>
        {
            foreach (OfflineAgent watcher in agents.Values) AgentLifecycleHost.Observe(watcher, position.State, definition.Behavior);
            if (!bySequence.TryGetValue(position.Sequence, out RegretRoot? root)) return;
            OfflineAgent chooser = agents[root.Side];
            ActionId chosen = chooser.ChooseActionAsync(ObservationBuilder.Build(position.State, root.Side, definition.Behavior, definition.Runtime))
                .AsTask().GetAwaiter().GetResult();
            string reason = chooser.LastEvaluation?.Candidates.FirstOrDefault(candidate => candidate.ActionId == chosen)?.Utility.Reason ?? "";
            chooser.DiscardDecision();
            string description = position.State.PendingDecision!.LegalActions.First(action => action.Id == chosen).Description;
            RegretChoice choice = Score(root, labels[root.Id], chosen, description, reason);
            choices.Add(choice.Regret is null
                ? choice with { Missing = RegretRoots.Group(position.State, position.State.PendingDecision!).First(group => group.ActionIds.Contains(chosen.Value)) }
                : choice);
        });
        return choices.ToImmutable();
    }

    private static RegretChoice Score(RegretRoot root, RegretLabel label, ActionId chosen, string description, string reason)
    {
        RegretCandidate? group = root.Candidates.FirstOrDefault(candidate => candidate.ActionIds.Contains(chosen.Value));
        RegretValue? best = label.Best;
        RegretValue? value = group is null ? null : label.Values.FirstOrDefault(entry => entry.Group == group.Group && !double.IsNaN(entry.Mean));
        if (best is null || value is null)
            return new RegretChoice(root.Id, root.Side, root.Kind, root.Split, group?.Group, description, reason, null, null, null);
        // A pruned group's value is its deciding samples' (S1.6); its regret is floored at zero and the choice marked.
        double regret = value.Pruned ? Math.Max(0, best.Mean - value.Mean) : best.Mean - value.Mean;
        return new RegretChoice(root.Id, root.Side, root.Kind, root.Split, group!.Group, description, reason, regret,
            value.Group == best.Group, regret <= best.StandardError + value.StandardError, Pruned: value.Pruned);
    }
}

/// <summary>
/// Bot plan S4.2b: a state the planner's leaf is read at (where the root side's turn ends, on a line the teacher played to
/// its horizon), from the root side's view: its feature set 5 (set 4 the first terms) and the play-out's value from there.
/// S4.2c: the sample it was played on (its place in the root's deal) and the line (the candidate's group and the
/// heuristic's rank at each later turn action), so rows of one sample pair on common random numbers; and at the ceiling's
/// roots, the values of two more play-outs from the same state: the hidden state dealt again, one world for the sample's
/// end states (<paramref name="Redeal"/>), and only the engine's random stream reseeded (<paramref name="Reseed"/>).
/// </summary>
public sealed record EndLineRow(string RootId, string GameId, PlayerSide Side, string Split, double[] Features, double Value,
    int Sample = -1, string? Line = null, double? Redeal = null, double? Reseed = null);
