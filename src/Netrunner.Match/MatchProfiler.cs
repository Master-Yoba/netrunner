using System.Diagnostics;
using Netrunner.AI;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Where the profiled games' time went (bot plan S0.1), with the whole-game rollouts measured at their roots.</summary>
public sealed record GameProfile(string Profile, int Games, int Actions, TimeSpan Elapsed, IReadOnlyList<ProfileSegment> Segments,
    GameRolloutCost GameRollouts, IReadOnlyList<ProfileSegment> RolloutSegments);

/// <summary>Where one planner decision's time went (bot plan S0.1), over <see cref="Repeats"/> identical choices.</summary>
public sealed record PlanProfile(string GameId, long Sequence, PlayerSide Side, string Profile, string Chosen, int Repeats,
    TimeSpan Elapsed, IReadOnlyList<ProfileSegment> Segments, GameRolloutCost GameRollouts, IReadOnlyList<ProfileSegment> RolloutSegments);

/// <summary>
/// Bot plan S0.1: profiles ladder games and planner decisions with <see cref="ProfileTimings"/>. The games are played
/// by the ladder's own runner and agents, so the split is the ladder's; one untimed game warms the JIT first.
/// </summary>
public static class MatchProfiler
{
    /// <summary>
    /// Plays <paramref name="games"/> self-play games of <paramref name="profile"/> on the pool's pairings in turn, then
    /// measures <paramref name="rolloutSamples"/> whole-game rollouts at up to <paramref name="rolloutRoots"/> turn starts
    /// of each game.
    /// </summary>
    public static async Task<GameProfile> ProfileGamesAsync(IReadOnlyList<string> contentIds, ulong seed, string profile,
        int games = 1, int rolloutRoots = 8, int rolloutSamples = 4, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentIds);
        ArgumentOutOfRangeException.ThrowIfZero(contentIds.Count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(games);
        await PlayAsync(Deal(contentIds, seed, games), profile, cancellationToken).ConfigureAwait(false);
        var timings = new ProfileTimings();
        var rolloutTimings = new ProfileTimings();
        var rollouts = new List<GameRolloutCost>();
        int actions = 0;
        TimeSpan elapsed = TimeSpan.Zero;
        for (int game = 0; game < games; game++)
        {
            FullMatchDefinition definition = Deal(contentIds, seed, game);
            long started = Stopwatch.GetTimestamp();
            FullMatchResult result = await Timed(timings, () => PlayAsync(definition, profile, cancellationToken)).ConfigureAwait(false);
            elapsed += Stopwatch.GetElapsedTime(started);
            actions += result.ActionCount;
            rollouts.Add(Rollouts(definition, result.Replay, profile, rolloutRoots, rolloutSamples, seed, rolloutTimings, cancellationToken));
        }
        return new GameProfile(profile, games, actions, elapsed, timings.Segments, Sum(rollouts), rolloutTimings.Segments);
    }

    /// <summary>
    /// Replays a saved game to <paramref name="sequence"/>, where a fresh <paramref name="profile"/> agent of the acting side
    /// chooses <paramref name="repeats"/> times (the choice is discarded between, so each is the same decision), then
    /// measures <paramref name="rolloutSamples"/> whole-game rollouts from it.
    /// </summary>
    public static async Task<PlanProfile> ProfilePlanAsync(LadderSavedGame saved, FullMatchDefinition definition, long sequence,
        string profile, int repeats = 10, int rolloutSamples = 8, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repeats);
        if (!saved.Replay.Actions.Any(recorded => recorded.Sequence == sequence))
            throw new ArgumentException($"The game has no action {sequence}.", nameof(sequence));
        GameState state = saved.Replay.InitialSnapshot.ToState();
        foreach (ReplayAction recorded in saved.Replay.Actions.TakeWhile(recorded => recorded.Sequence != sequence))
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
        PendingDecision pending = state.PendingDecision!;
        PlayerSide side = state.GetPlayer(pending.Owner).Side;
        OfflineAgent agent = Watch(saved.Replay, definition, profile, side, sequence, cancellationToken);
        Observation observation = ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime);
        // Untimed choices first, so the timed ones run on tiered-up code as a ladder's do.
        for (int warm = 0; warm < Math.Max(5, repeats); warm++)
        {
            await agent.ChooseActionAsync(observation, cancellationToken).ConfigureAwait(false);
            agent.DiscardDecision();
        }
        PlannerProbe.PlayToEnd(agent, observation, Math.Min(rolloutSamples, 4), saved.Game.GameSeed + 1);
        var timings = new ProfileTimings();
        ActionId chosen = default;
        long started = Stopwatch.GetTimestamp();
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            chosen = await Timed(timings, () => agent.ChooseActionAsync(observation, cancellationToken).AsTask()).ConfigureAwait(false);
            agent.DiscardDecision();
        }
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started) / repeats;
        var rolloutTimings = new ProfileTimings();
        GameRolloutCost rollouts = Timed(rolloutTimings, () => PlannerProbe.PlayToEnd(agent, observation, rolloutSamples, saved.Game.GameSeed));
        LegalAction action = pending.LegalActions.Single(legal => legal.Id == chosen);
        return new PlanProfile(saved.Game.Id, sequence, side, profile, action.Kind.ToString(), repeats, elapsed, timings.Segments, rollouts,
            rolloutTimings.Segments);
    }

    private static FullMatchDefinition Deal(IReadOnlyList<string> contentIds, ulong seed, int game) =>
        RulesFuzzRunner.CreateDefinition(contentIds[game % contentIds.Count], RulesFuzzRunner.DeriveGameSeed(seed, game));

    private static Task<FullMatchResult> PlayAsync(FullMatchDefinition definition, string profile, CancellationToken cancellationToken) =>
        FullMatchRunner.RunAsync(definition, RatingLadder.DefaultActionCap, RatingLadder.CreateAgent(profile, PlayerSide.Corp, definition),
            RatingLadder.CreateAgent(profile, PlayerSide.Runner, definition), cancellationToken);

    /// <summary>Whole-game rollouts at evenly spaced turn starts of a played game, from each acting side's own agent.</summary>
    private static GameRolloutCost Rollouts(FullMatchDefinition definition, ReplayArtifact replay, string profile,
        int roots, int samples, ulong seed, ProfileTimings timings, CancellationToken cancellationToken)
    {
        if (roots <= 0 || samples <= 0) return new GameRolloutCost(0, 0, 0, TimeSpan.Zero);
        var agents = new Dictionary<PlayerSide, OfflineAgent>
        {
            [PlayerSide.Corp] = Agent(profile, PlayerSide.Corp, definition),
            [PlayerSide.Runner] = Agent(profile, PlayerSide.Runner, definition),
        };
        GameState state = replay.InitialSnapshot.ToState();
        foreach (OfflineAgent agent in agents.Values) AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        var starts = new List<long>();
        int turn = -1;
        foreach (ReplayAction recorded in replay.Actions)
        {
            if (state.PendingDecision?.Kind == DecisionKind.TurnAction && state.Turn.TurnNumber != turn)
            {
                turn = state.Turn.TurnNumber;
                starts.Add(recorded.Sequence);
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
        }
        int count = Math.Min(roots, starts.Count);
        HashSet<long> chosen = [.. Enumerable.Range(0, count).Select(index => starts[index * starts.Count / count])];
        var costs = new List<GameRolloutCost>();
        state = replay.InitialSnapshot.ToState();
        foreach (ReplayAction recorded in replay.Actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chosen.Contains(recorded.Sequence))
            {
                PlayerSide side = state.GetPlayer(state.PendingDecision!.Owner).Side;
                Observation observation = ObservationBuilder.Build(state, side, definition.Behavior, definition.Runtime);
                costs.Add(Timed(timings, () => PlannerProbe.PlayToEnd(agents[side], observation, samples, seed ^ (ulong)recorded.Sequence)));
            }
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            foreach (OfflineAgent agent in agents.Values) AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        return Sum(costs);
    }

    /// <summary>A fresh agent that has watched the game up to (not including) <paramref name="sequence"/>.</summary>
    private static OfflineAgent Watch(ReplayArtifact replay, FullMatchDefinition definition, string profile, PlayerSide side,
        long sequence, CancellationToken cancellationToken)
    {
        OfflineAgent agent = Agent(profile, side, definition);
        GameState state = replay.InitialSnapshot.ToState();
        AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        foreach (ReplayAction recorded in replay.Actions.TakeWhile(recorded => recorded.Sequence < sequence))
        {
            cancellationToken.ThrowIfCancellationRequested();
            state = GameEngine.Apply(state, recorded.Submission, definition.Behavior, definition.Runtime).State;
            AgentLifecycleHost.Observe(agent, state, definition.Behavior);
        }
        return agent;
    }

    private static OfflineAgent Agent(string profile, PlayerSide side, FullMatchDefinition definition) =>
        RatingLadder.CreateAgent(profile, side, definition) as OfflineAgent
            ?? throw new ArgumentException($"The '{profile}' entrant is not an offline agent.", nameof(profile));

    private static GameRolloutCost Sum(IEnumerable<GameRolloutCost> costs) => costs.Aggregate(new GameRolloutCost(0, 0, 0, TimeSpan.Zero),
        (total, cost) => new GameRolloutCost(total.Rollouts + cost.Rollouts, total.Finished + cost.Finished, total.Steps + cost.Steps,
            total.Elapsed + cost.Elapsed));

    private static async Task<T> Timed<T>(ProfileTimings timings, Func<Task<T>> run)
    {
        ProfileTimings? previous = ProfileTimings.Current;
        ProfileTimings.Current = timings;
        try { return await run().ConfigureAwait(false); }
        finally { ProfileTimings.Current = previous; }
    }

    private static T Timed<T>(ProfileTimings timings, Func<T> run)
    {
        ProfileTimings? previous = ProfileTimings.Current;
        ProfileTimings.Current = timings;
        try { return run(); }
        finally { ProfileTimings.Current = previous; }
    }
}
