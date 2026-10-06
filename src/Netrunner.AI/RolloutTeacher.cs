using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>How the teacher labels a decision (bot plan S1.2).</summary>
/// <param name="Samples">States sampled from the agent's observation; every candidate is played out on every one.</param>
/// <param name="Seed">The teacher's own random stream for the samples.</param>
/// <param name="HorizonTurns">Turns to play past the decision before the leaf scores the position; 0 plays to the game's end.</param>
/// <param name="Evaluation">The fitted evaluation's feature set that scores a position the play-out stopped short of the end.</param>
/// <param name="Cap">Simulated decisions after which a play-out is scored where it stands.</param>
/// <param name="Lines">Bot plan S1.5: the beam width of the turn-line search; a candidate is valued at its best line of the
/// turn. 0 values it followed by the heuristic's play of the rest of the turn.</param>
/// <param name="ScreenTurns">How far the turn-line search's screen plays a line: turns past the decision, as
/// <paramref name="HorizonTurns"/> counts them; 0 scores it by the leaf where the root side's turn ends.</param>
public sealed record TeacherSettings(int Samples, ulong Seed, int HorizonTurns = 0, int Evaluation = 4, int Cap = PlannerProbe.GameRolloutCap,
    int Lines = 0, int ScreenTurns = 1);

/// <summary>
/// A candidate's teacher value: the mean win probability over its play-outs and the standard error of that mean. Every
/// candidate of one evaluation has the same play-outs; none (and a NaN mean) when no sample could play them all. With the
/// turn-line search, <paramref name="Line"/> is the best line's rank path: the heuristic's choice at each later turn action.
/// </summary>
public sealed record TeacherValue(ActionId Action, double Mean, double StandardError, int PlayOuts, IReadOnlyList<int>? Line = null);

/// <summary>
/// Bot plan S1.2: the teacher of the regret suite. It labels a decision by playing every candidate out on the same
/// samples of the hidden state (common random numbers), dealt as Hard deals them (the tracker's pins and, when the
/// agent knows it, the opponent's list), with both sides making the planner's rollout choices past the turn's end.
/// It reads only the agent's own observation, knowledge and configuration, and never its checkpoint's random stream.
/// </summary>
public static class RolloutTeacher
{
    public static IReadOnlyList<TeacherValue> Evaluate(OfflineAgent agent, Observation observation, IReadOnlyList<ActionId> candidates,
        TeacherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(settings.Samples);
        ObservationDeterminizer determinizer = observation.Determinizer
            ?? throw new ArgumentException("The observation carries no determinizer.", nameof(observation));
        if (determinizer.Viewer != agent.ControlledSide) throw new ArgumentException("The observation is not the agent's.", nameof(observation));
        var planner = new TurnPlanner(agent.Policy, agent.Configuration, agent.Catalog);
        AgentKnowledge knowledge = agent.Checkpoint.Knowledge;
        PlayerSide side = agent.ControlledSide;
        var planning = new PlanningContext(determinizer, new DeterministicRandom(settings.Seed));
        OpponentCardPool pool = planner.PoolFor(side, knowledge, fromList: true);
        // The turn-line search is for a turn-action root; elsewhere a candidate's line is its heuristic continuation.
        if (settings.Lines > 1 && observation.Decision?.Kind == DecisionKind.TurnAction) return Lines(planner, candidates, settings, side, knowledge, determinizer, planning, pool);
        var values = candidates.Select(_ => new List<double>()).ToArray();
        // Every candidate is played on the same samples. A sample where one candidate cannot be played (a choice naming a
        // hidden card the deal replaced) is dropped for all, and another is dealt, up to twice the samples asked for.
        double[] played = new double[candidates.Count];
        for (int dealt = 0; dealt < 2 * settings.Samples && values[0].Count < settings.Samples; dealt++)
        {
            GameState state = TurnPlanner.Deal(side, knowledge, pool, planning, pinned: true);
            bool whole = true;
            for (int index = 0; index < candidates.Count && whole; index++)
            {
                double? value = planner.PlayOut(state, candidates[index], side, knowledge, determinizer, settings.HorizonTurns,
                    settings.Evaluation, settings.Cap);
                if (value is { } known) played[index] = known;
                else whole = false;
            }
            if (whole)
                for (int index = 0; index < candidates.Count; index++) values[index].Add(played[index]);
        }
        return [.. candidates.Select((action, index) => Summarize(action, values[index]))];
    }

    /// <summary>
    /// Plays <paramref name="action"/> on the teacher's samples, followed by the heuristic as the one-step value plays it,
    /// and shows <paramref name="observe"/> each sample's states in order (with the sample's index), so a caller can see
    /// what the rollout's opponent does after a candidate (the S1.5 follow-up's agenda exposure). Returns the samples played.
    /// </summary>
    public static int Trace(OfflineAgent agent, Observation observation, ActionId action, TeacherSettings settings, Action<int, GameState> observe)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(observe);
        ObservationDeterminizer determinizer = observation.Determinizer
            ?? throw new ArgumentException("The observation carries no determinizer.", nameof(observation));
        var planner = new TurnPlanner(agent.Policy, agent.Configuration, agent.Catalog);
        AgentKnowledge knowledge = agent.Checkpoint.Knowledge;
        PlayerSide side = agent.ControlledSide;
        var planning = new PlanningContext(determinizer, new DeterministicRandom(settings.Seed));
        OpponentCardPool pool = planner.PoolFor(side, knowledge, fromList: true);
        int played = 0;
        for (int dealt = 0; dealt < 2 * settings.Samples && played < settings.Samples; dealt++)
        {
            GameState sample = TurnPlanner.Deal(side, knowledge, pool, planning, pinned: true);
            int index = played;
            if (planner.PlayLine(sample, action, [], side, knowledge, determinizer, settings.HorizonTurns, settings.Evaluation, settings.Cap,
                out _, observe: state => observe(index, state)) is not null)
                played++;
        }
        return played;
    }

    /// <summary>
    /// Bot plan S1.5: every candidate valued at its best line of the turn (<see cref="TurnPlanner.BestLine"/>) on the same
    /// samples, dealt first: the lines are screened by the leaf where the turn ends, and the heuristic's own line and the
    /// best other are played on. A sample where one candidate cannot be played is dropped for all, as above.
    /// </summary>
    private static TeacherValue[] Lines(TurnPlanner planner, IReadOnlyList<ActionId> candidates, TeacherSettings settings, PlayerSide side,
        AgentKnowledge knowledge, ObservationDeterminizer determinizer, PlanningContext planning, OpponentCardPool pool)
    {
        var samples = new List<GameState>();
        for (int dealt = 0; dealt < 2 * settings.Samples && samples.Count < settings.Samples; dealt++)
        {
            GameState state = TurnPlanner.Deal(side, knowledge, pool, planning, pinned: true);
            if (candidates.All(candidate => TurnPlanner.Playable(state, candidate, determinizer))) samples.Add(state);
        }
        if (samples.Count == 0) return [.. candidates.Select(action => Summarize(action, []))];
        // One memo for the root: lines of different candidates often leave the turn in the same state.
        var memo = new Dictionary<string, double>(StringComparer.Ordinal);
        var screens = new Dictionary<string, double>(StringComparer.Ordinal);
        int screenTurns = settings.ScreenTurns == 0 ? settings.HorizonTurns : settings.ScreenTurns;
        return [.. candidates.Select(action =>
        {
            (double[] values, int[] ranks) = TurnPlanner.BestLine(samples.Count,
                (index, ranks) => (planner.PlayLine(samples[index], action, ranks, side, knowledge, determinizer, screenTurns,
                    settings.Evaluation, settings.Cap, out int reached, screens, toTurnEnd: settings.ScreenTurns == 0) ?? 0, reached),
                settings.Lines,
                (index, ranks) => (planner.PlayLine(samples[index], action, ranks, side, knowledge, determinizer, settings.HorizonTurns,
                    settings.Evaluation, settings.Cap, out int reached, memo) ?? 0, reached));
            return Summarize(action, [.. values]) with { Line = ranks };
        })];
    }

    private static TeacherValue Summarize(ActionId action, List<double> values)
    {
        if (values.Count == 0) return new TeacherValue(action, double.NaN, double.NaN, 0);
        double mean = values.Average();
        double variance = values.Count > 1 ? values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1) : 0;
        return new TeacherValue(action, mean, Math.Sqrt(variance / values.Count), values.Count);
    }
}
