using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match.Regret;

/// <summary>
/// One candidate group of a labelled turn-action root, read for the apprentice (bot plan S4.5): the group's own terms
/// (<see cref="ApprenticeFeatures"/>, its first action read with the heuristic's utilities), the root's set 5 from the
/// acting side's view, and the teacher's value of the group (a pruned group's at its deciding samples, marked).
/// </summary>
public sealed record ApprenticeRow(string RootId, PlayerSide Side, string Split, bool Core, string Group, double Value, double StandardError,
    bool Pruned, double[] Candidate, double[] Root);

/// <summary>Collects the apprentice's rows (S4.5).</summary>
public static class ApprenticeRows
{
    /// <summary>
    /// A game's labelled turn-action roots as rows, read by the entrant the apprentice re-orders (the Standard heuristic
    /// with no planner): its candidates' utilities and its knowledge at the root.
    /// </summary>
    public static IReadOnlyList<ApprenticeRow> Collect(RegretGame game, IReadOnlyList<(RegretRoot Root, RegretLabel Label)> roots)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(roots);
        var turn = roots.Where(entry => entry.Root.Kind == RegretRoots.TurnKind).ToDictionary(entry => entry.Root.Sequence);
        if (turn.Count == 0) return [];
        FullMatchDefinition definition = RegretGames.Definition(game);
        var catalog = new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior);
        var agents = new[] { PlayerSide.Corp, PlayerSide.Runner }.ToDictionary(side => side, side =>
        {
            var heuristic = (OfflineAgent)RatingLadder.CreateAgent(RatingLadder.ApprenticeBase, side, definition);
            return new OfflineAgent(heuristic.Configuration, OfflinePolicies.Create(heuristic.Configuration), catalog, null, captureDiagnostics: true);
        });
        var rows = new List<ApprenticeRow>();
        RegretGames.Replay(game, definition, position =>
        {
            foreach (OfflineAgent agent in agents.Values) AgentLifecycleHost.Observe(agent, position.State, definition.Behavior);
            if (!turn.TryGetValue(position.Sequence, out var entry)) return;
            OfflineAgent own = agents[entry.Root.Side];
            Observation observation = ObservationBuilder.Build(position.State, entry.Root.Side, definition.Behavior, definition.Runtime);
            own.ChooseActionAsync(observation).AsTask().GetAwaiter().GetResult();
            AgentCandidate[] candidates = [.. own.LastEvaluation!.Candidates];
            own.DiscardDecision();
            AgentFeatures features = ObservationReader.Read(observation, own.Configuration.FeatureVersion);
            double[] root = EvaluationFeatures.Extract(features, catalog, 5, own.Checkpoint.Knowledge);
            var values = entry.Label.Values.ToDictionary(value => value.Group, StringComparer.Ordinal);
            foreach (RegretCandidate group in entry.Root.Candidates)
            {
                if (!values.TryGetValue(group.Group, out RegretValue? value)) continue;
                AgentAction? action = features.Actions.FirstOrDefault(action => action.Id.Value == group.ActionIds[0]);
                if (action is null) continue;
                rows.Add(new ApprenticeRow(entry.Root.Id, entry.Root.Side, entry.Root.Split, entry.Root.Categories.Contains(RegretSuite.CoreCategory),
                    group.Group, value.Mean, value.StandardError, value.Pruned,
                    ApprenticeFeatures.Extract(features, action, candidates, catalog, own.Checkpoint.Knowledge), root));
            }
        });
        return rows;
    }
}
