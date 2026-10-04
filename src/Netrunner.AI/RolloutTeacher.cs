using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>How the teacher labels a decision (bot plan S1.2).</summary>
/// <param name="Samples">States sampled from the agent's observation; every candidate is played out on every one.</param>
/// <param name="Seed">The teacher's own random stream for the samples.</param>
/// <param name="HorizonTurns">Turns to play past the decision before the leaf scores the position; 0 plays to the game's end.</param>
/// <param name="Evaluation">The fitted evaluation's feature set that scores a position the play-out stopped short of the end.</param>
/// <param name="Cap">Simulated decisions after which a play-out is scored where it stands.</param>
public sealed record TeacherSettings(int Samples, ulong Seed, int HorizonTurns = 0, int Evaluation = 4, int Cap = PlannerProbe.GameRolloutCap);

/// <summary>A candidate's teacher value: the mean win probability over its play-outs and the standard error of that mean.</summary>
public sealed record TeacherValue(ActionId Action, double Mean, double StandardError, int PlayOuts);

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
        var values = candidates.Select(_ => new List<double>()).ToArray();
        for (int sample = 0; sample < settings.Samples; sample++)
        {
            GameState state = TurnPlanner.Deal(side, knowledge, pool, planning, pinned: true);
            for (int index = 0; index < candidates.Count; index++)
                if (planner.PlayOut(state, candidates[index], side, knowledge, determinizer, settings.HorizonTurns, settings.Evaluation,
                    settings.Cap) is { } value) values[index].Add(value);
        }
        return [.. candidates.Select((action, index) => Summarize(action, values[index]))];
    }

    private static TeacherValue Summarize(ActionId action, List<double> values)
    {
        if (values.Count == 0) return new TeacherValue(action, double.NaN, double.NaN, 0);
        double mean = values.Average();
        double variance = values.Count > 1 ? values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1) : 0;
        return new TeacherValue(action, mean, Math.Sqrt(variance / values.Count), values.Count);
    }
}
