using System.Diagnostics;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

public sealed record EngineBenchmarkResult(int Games, int Actions, double AuthoritativeMicroseconds,
    double SimulationMicroseconds)
{
    public double Speedup => SimulationMicroseconds <= 0 ? 0 : AuthoritativeMicroseconds / SimulationMicroseconds;
}

/// <summary>
/// Measures Apply throughput in both modes over the same recorded positions.
/// Positions come from random-legal games, so they cover setup, turns, runs
/// and accesses. Each mode is timed over every position three times and the
/// fastest pass is reported, to limit noise from other processes.
/// </summary>
public static class EngineBenchmark
{
    private const int Passes = 3;

    public static EngineBenchmarkResult Run(string contentId, int games, ulong seed, int actionCap = 1500)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(games);
        var positions = new List<(GameState State, ActionSubmission Submission, FullMatchDefinition Definition)>();
        for (int game = 0; game < games; game++)
        {
            FullMatchDefinition definition = RulesFuzzRunner.CreateDefinition(contentId, RulesFuzzRunner.DeriveGameSeed(seed, game));
            var corp = new RandomLegalAgent(AgentLifecycleHost.DerivePolicySeed(definition.Request.Seed, PlayerSide.Corp));
            var runner = new RandomLegalAgent(AgentLifecycleHost.DerivePolicySeed(definition.Request.Seed, PlayerSide.Runner));
            GameState state = definition.Start().State;
            for (int action = 0; action < actionCap && state.Outcome.Kind == GameOutcomeKind.Ongoing
                && state.PendingDecision is { } pending; action++)
            {
                PlayerSide side = state.GetPlayer(pending.Owner).Side;
                ActionId chosen = (side == PlayerSide.Corp ? corp : runner)
                    .ChooseActionAsync(ObservationBuilder.Build(state, side, definition.Behavior)).AsTask().GetAwaiter().GetResult();
                var submission = new ActionSubmission(pending.Owner, pending.Id, pending.Revision, chosen);
                positions.Add((state, submission, definition));
                ApplyResult applied = GameEngine.Apply(state, submission, definition.Behavior, definition.Runtime);
                if (!applied.Accepted) break;
                state = applied.State;
            }
        }

        double Measure(ApplyMode mode)
        {
            double best = double.MaxValue;
            for (int pass = 0; pass < Passes; pass++)
            {
                long started = Stopwatch.GetTimestamp();
                foreach ((GameState state, ActionSubmission submission, FullMatchDefinition definition) in positions)
                {
                    GameEngine.Apply(state, submission, definition.Behavior, definition.Runtime, mode);
                }
                best = Math.Min(best, Stopwatch.GetElapsedTime(started).TotalMicroseconds / Math.Max(1, positions.Count));
            }
            return best;
        }

        // Warm up both paths before timing.
        foreach ((GameState state, ActionSubmission submission, FullMatchDefinition definition) in positions.Take(50))
        {
            GameEngine.Apply(state, submission, definition.Behavior, definition.Runtime, ApplyMode.Authoritative);
            GameEngine.Apply(state, submission, definition.Behavior, definition.Runtime, ApplyMode.Simulation);
        }

        return new EngineBenchmarkResult(games, positions.Count, Measure(ApplyMode.Authoritative), Measure(ApplyMode.Simulation));
    }
}
