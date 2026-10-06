using System.Collections.Immutable;
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

    /// <param name="teacher">The ladder entrant that teaches: Hard, or <c>hard-model</c>, Hard modelling the opponent by the
    /// live Standard version instead of the planner's pinned model (Runner v7, Corp v8).</param>
    public static ImmutableArray<RegretLabel> Label(RegretGame game, IReadOnlyList<RegretRoot> roots, TeacherSettings settings,
        string teacher = DefaultTeacher)
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
            IReadOnlyList<TeacherValue> values = RolloutTeacher.Evaluate(teachers[root.Side], observation,
                [.. root.Candidates.Select(candidate => new ActionId(candidate.ActionIds[0]))], rooted);
            // A root no sample could play every candidate on has no label: it is scored as unlabelled.
            if (values.Any(value => value.PlayOuts == 0)) return;
            labels.Add(new RegretLabel(root.Id, settings.Samples, settings.HorizonTurns, settings.Evaluation, rooted.Seed,
                [.. root.Candidates.Zip(values, Value)], settings.Lines, settings.Lines > 1 ? settings.ScreenTurns : 0,
                teacher == "hard" ? null : teacher));
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
            var settings = new TeacherSettings(request.Label.Samples, request.Label.Seed, request.Label.HorizonTurns, request.Label.Evaluation,
                Lines: request.Label.Lines, ScreenTurns: request.Label.ScreenTurns);
            IReadOnlyList<TeacherValue> found = RolloutTeacher.Evaluate(teachers[request.Root.Side],
                ObservationBuilder.Build(position.State, request.Root.Side, definition.Behavior, definition.Runtime),
                [.. request.Groups.Select(group => new ActionId(group.ActionIds[0]))], settings);
            values[request.Root.Id] = [.. request.Groups.Zip(found, Value).Where(value => value.PlayOuts > 0)];
        });
        return values;
    }

    private static RegretValue Value(RegretCandidate candidate, TeacherValue value) =>
        new(candidate.Group, value.Mean, value.StandardError, value.PlayOuts, value.Line is { } line ? [.. line] : null);

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
    string Reason, double? Regret, bool? Agrees, bool? AgreesWithinNoise, RegretCandidate? Missing = null);

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
        double regret = best.Mean - value.Mean;
        return new RegretChoice(root.Id, root.Side, root.Kind, root.Split, group!.Group, description, reason, regret,
            value.Group == best.Group, regret <= best.StandardError + value.StandardError);
    }
}
