using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match.Regret;

/// <summary>
/// One labelled root, read for fitting the leaf to the teacher (bot plan S4.2): the acting side's features in sets 4 and
/// 5 from its own view (its knowledge the teacher's, which has watched the game), the teacher's root value (the best
/// recorded group's) and whether that side won the game.
/// </summary>
public sealed record TeacherFitRow(string RootId, PlayerSide Side, string Split, double[] Set4, double[] Set5, double Teacher, double Won,
    string? BehaviorVersion);

/// <summary>Collects the rows the leaf is fitted to (S4.2).</summary>
public static class TeacherFit
{
    /// <summary>A game's labelled roots as rows; a game with no winner gives none.</summary>
    public static IReadOnlyList<TeacherFitRow> Collect(RegretGame game, IReadOnlyList<(RegretRoot Root, RegretLabel Label)> roots)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(roots);
        if (roots.Count == 0) return [];
        FullMatchDefinition definition = RegretGames.Definition(game);
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        var teachers = new[] { PlayerSide.Corp, PlayerSide.Runner }.ToDictionary(side => side,
            side => (OfflineAgent)RatingLadder.CreateAgent(RegretLabels.DefaultTeacher, side, definition));
        var bySequence = roots.ToDictionary(entry => entry.Root.Sequence);
        var rows = new List<(RegretRoot Root, double[] Set4, double[] Set5, double Teacher)>();
        GameState state = definition.Start().State;
        foreach (int choice in game.Choices)
        {
            foreach (OfflineAgent teacher in teachers.Values) AgentLifecycleHost.Observe(teacher, state, definition.Behavior);
            if (bySequence.TryGetValue(state.ActionSequence + 1, out var entry) && entry.Label.Best is { } best)
            {
                OfflineAgent teacher = teachers[entry.Root.Side];
                AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, entry.Root.Side, definition.Behavior),
                    teacher.Configuration.FeatureVersion);
                rows.Add((entry.Root, EvaluationFeatures.Extract(features, catalog, 4),
                    EvaluationFeatures.Extract(features, catalog, 5, teacher.Checkpoint.Knowledge), best.Mean));
            }
            PendingDecision pending = state.PendingDecision!;
            state = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, pending.LegalActions[choice].Id),
                definition.Behavior, definition.Runtime).State;
        }
        if (state.Outcome.Winner is not { } winner) return [];
        return [.. rows.Select(row => new TeacherFitRow(row.Root.Id, row.Root.Side, row.Root.Split, row.Set4, row.Set5, row.Teacher,
            row.Root.Side == winner ? 1 : 0, definition.Behavior.Version))];
    }
}
