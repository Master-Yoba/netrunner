using System.Diagnostics;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>Whole-game rollouts from one decision: how many ran, their simulated decisions, and their time.</summary>
public sealed record GameRolloutCost(int Rollouts, int Finished, long Steps, TimeSpan Elapsed)
{
    public double MeanMilliseconds => Rollouts == 0 ? 0 : Elapsed.TotalMilliseconds / Rollouts;
    public double MeanSteps => Rollouts == 0 ? 0 : (double)Steps / Rollouts;
    public double MicrosecondsPerStep => Steps == 0 ? 0 : Elapsed.TotalMicroseconds / Steps;
}

/// <summary>
/// Bot plan S0.1: measures the planner's rollouts beyond what a decision runs. It reads only the agent's own
/// observation, knowledge and configuration, deals samples as the planner does, and never touches the agent's
/// checkpoint or random stream, so probing a decision leaves the agent's choice unchanged.
/// </summary>
public static class PlannerProbe
{
    /// <summary>Simulated decisions after which a whole-game rollout is stopped where it stands.</summary>
    public const int GameRolloutCap = 4000;

    /// <summary>
    /// Plays <paramref name="samples"/> states sampled from <paramref name="observation"/> to the game's end with the
    /// planner's rollout choices for both sides, on a random stream of its own seeded by <paramref name="seed"/>.
    /// </summary>
    public static GameRolloutCost PlayToEnd(OfflineAgent agent, Observation observation, int samples, ulong seed,
        int cap = GameRolloutCap)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentOutOfRangeException.ThrowIfNegative(samples);
        ObservationDeterminizer determinizer = observation.Determinizer
            ?? throw new ArgumentException("The observation carries no determinizer.", nameof(observation));
        if (determinizer.Viewer != agent.ControlledSide) throw new ArgumentException("The observation is not the agent's.", nameof(observation));
        var planner = new TurnPlanner(agent.Policy, agent.Configuration, agent.Catalog);
        AgentKnowledge knowledge = agent.Checkpoint.Knowledge;
        var planning = new PlanningContext(determinizer, new DeterministicRandom(seed));
        OpponentCardPool pool = planner.PoolFor(agent.ControlledSide, knowledge);
        int finished = 0;
        long steps = 0;
        long started = Stopwatch.GetTimestamp();
        for (int sample = 0; sample < samples; sample++)
        {
            (GameState end, int played) = planner.PlayToEnd(planner.Deal(agent.ControlledSide, knowledge, pool, planning),
                agent.ControlledSide, knowledge, determinizer, cap);
            steps += played;
            if (end.Outcome.Kind != GameOutcomeKind.Ongoing) finished++;
        }
        return new GameRolloutCost(samples, finished, steps, Stopwatch.GetElapsedTime(started));
    }
}
