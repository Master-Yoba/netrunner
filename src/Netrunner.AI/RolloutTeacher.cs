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
/// <param name="Decide">Bot plan S1.7, adaptive samples: samples drawn first only to decide how many to record, then
/// discarded, so a stop that a large-looking gap triggered does not keep that gap in the label. 0 records
/// <paramref name="Samples"/> flat.</param>
/// <param name="ExtendTo">With <paramref name="Decide"/>: a root whose deciding samples left its best two candidates within
/// two standard errors records <paramref name="ExtendStep"/> more samples at a time, until they separate on the recording
/// samples or this many are recorded.</param>
/// <param name="Prune">Bot plan S1.6, pruning: with <paramref name="Decide"/>, only the best this many candidates on the
/// deciding samples (and any the caller requires) get the recording samples; the rest keep their deciding value, marked
/// pruned. 0 records every candidate.</param>
public sealed record TeacherSettings(int Samples, ulong Seed, int HorizonTurns = 0, int Evaluation = 4, int Cap = PlannerProbe.GameRolloutCap,
    int Lines = 0, int ScreenTurns = 1, int Decide = 0, int ExtendTo = 0, int ExtendStep = 8, int Prune = 0);

/// <summary>
/// A candidate's teacher value: the mean win probability over its play-outs and the standard error of that mean. Every
/// candidate of one evaluation has the same play-outs; none (and a NaN mean) when no sample could play them all. With the
/// turn-line search, <paramref name="Line"/> is the best line's rank path: the heuristic's choice at each later turn action.
/// </summary>
/// A pruned candidate (S1.6) has only its deciding samples' value.
public sealed record TeacherValue(ActionId Action, double Mean, double StandardError, int PlayOuts, IReadOnlyList<int>? Line = null,
    bool Pruned = false);

/// <summary>
/// Bot plan S1.2: the teacher of the regret suite. It labels a decision by playing every candidate out on the same
/// samples of the hidden state (common random numbers), dealt as Hard deals them (the tracker's pins and, when the
/// agent knows it, the opponent's list), with both sides making the planner's rollout choices past the turn's end.
/// It reads only the agent's own observation, knowledge and configuration, and never its checkpoint's random stream.
/// </summary>
public static class RolloutTeacher
{
    /// <param name="required">Candidates (by index) that get the recording samples whatever the deciding ones say (S1.6:
    /// the groups the compared versions chose at a targeted root).</param>
    /// <param name="endOfLine">Bot plan S4.2b: shown, for every line played to the horizon, feature set 5 of the state where
    /// the root side's turn ended (from its view, with its knowledge) and the play-out's value from there, with the sample
    /// and the line (S4.2c).</param>
    /// <param name="repeat">Bot plan S4.2c, the ceiling: each end-of-line row also gets two more play-outs from its state:
    /// one with the hidden state dealt again, one world for all of a sample's end states (<see cref="Repeat"/>), and one
    /// with only the engine's random stream reseeded. The rows then come after the root is valued.</param>
    public static IReadOnlyList<TeacherValue> Evaluate(OfflineAgent agent, Observation observation, IReadOnlyList<ActionId> candidates,
        TeacherSettings settings, IReadOnlySet<int>? required = null, Action<EndLine>? endOfLine = null, bool repeat = false)
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
        // S4.2c: every sample has an id (its place in the deal), and a line is its candidate and rank path.
        var sampleIds = new Dictionary<GameState, int>(ReferenceEqualityComparer.Instance);
        var held = new List<(EndOfTurn End, EndLine Line)>();
        Action<EndOfTurn>? Ended(GameState sample, ActionId action, IReadOnlyList<int> ranks) => endOfLine is null ? null : end =>
        {
            var line = new EndLine(planner.LeafFeatures(end.State, side, determinizer, knowledge), end.Value, sampleIds[sample],
                IndexOf(candidates, action), [.. ranks], null, null);
            if (repeat) held.Add((end, line));
            else endOfLine(line);
        };
        IReadOnlyList<TeacherValue> Done(IReadOnlyList<TeacherValue> values)
        {
            if (endOfLine is not null && held.Count > 0)
                foreach (EndLine line in Repeat(planner, held, side, knowledge, determinizer, pool, settings)) endOfLine(line);
            return values;
        }
        // The turn-line search is for a turn-action root; elsewhere a candidate's line is its heuristic continuation.
        bool lines = settings.Lines > 1 && observation.Decision?.Kind == DecisionKind.TurnAction;
        // One memo for the root: lines of different candidates often leave the turn in the same state.
        var memo = new Dictionary<string, double>(StringComparer.Ordinal);
        var screens = new Dictionary<string, double>(StringComparer.Ordinal);
        int dealt = 0;
        // Every candidate is played on the same samples. A deal where one candidate cannot be played (a choice naming a
        // hidden card the deal replaced) is dropped for all, and another is dealt, up to twice the samples asked for.
        List<GameState> Draw(int count)
        {
            var drawn = new List<GameState>();
            for (int limit = dealt + 2 * count; dealt < limit && drawn.Count < count; dealt++)
            {
                GameState state = TurnPlanner.Deal(side, knowledge, pool, planning, pinned: true);
                if (candidates.All(candidate => TurnPlanner.Playable(state, candidate, determinizer)))
                {
                    drawn.Add(state);
                    sampleIds[state] = dealt;
                }
            }
            return drawn;
        }
        TeacherValue[] ValueOf(IReadOnlyList<ActionId> actions, List<GameState> samples) => samples.Count == 0 ? [.. actions.Select(action => Summarize(action, []))]
            : lines ? Lines(planner, actions, settings, side, knowledge, determinizer, samples, memo, screens, Ended)
            : [.. actions.Select(action => Summarize(action, [.. samples.Select(sample => planner.PlayLine(sample, action, [], side, knowledge,
                determinizer, settings.HorizonTurns, settings.Evaluation, settings.Cap, out _, endOfLine: Ended(sample, action, [])) ?? 0)]))];
        TeacherValue[] Value(List<GameState> samples) => ValueOf(candidates, samples);
        if (settings.Decide <= 0) return Done(Value(Draw(settings.Samples)));
        if (settings.Prune > 0)
        {
            // S1.6, pruning: the deciding samples value every candidate and pick which get the recording samples.
            TeacherValue[] deciding = Value(Draw(settings.Decide));
            HashSet<int> kept = [.. deciding.Select((value, index) => (value, index)).Where(entry => !double.IsNaN(entry.value.Mean))
                .OrderByDescending(entry => entry.value.Mean).Take(settings.Prune).Select(entry => entry.index), .. required ?? new HashSet<int>()];
            int[] order = [.. kept.Order()];
            TeacherValue[] recorded = ValueOf([.. order.Select(index => candidates[index])], Draw(settings.Samples));
            var byIndex = order.Select((index, position) => (index, position)).ToDictionary(entry => entry.index, entry => recorded[entry.position]);
            return Done([.. candidates.Select((_, index) => byIndex.TryGetValue(index, out TeacherValue? value) ? value : deciding[index] with { Pruned = true })]);
        }
        // S1.7: the deciding samples choose how many to record and are discarded. Without an extension to decide (a group
        // labelled on demand) they are only drawn, so the recording samples are the root's.
        bool clear = settings.ExtendTo <= settings.Samples || !Close(Value(Draw(settings.Decide)));
        if (settings.ExtendTo <= settings.Samples) Draw(settings.Decide);
        List<GameState> recording = Draw(settings.Samples);
        TeacherValue[] values = Value(recording);
        while (!clear && recording.Count < settings.ExtendTo && Close(values))
        {
            List<GameState> more = Draw(Math.Min(settings.ExtendStep, settings.ExtendTo - recording.Count));
            if (more.Count == 0) break;
            recording.AddRange(more);
            values = Value(recording);
        }
        return values;
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
    /// samples: the lines are screened by a short play-out (or the leaf where the turn ends), and the heuristic's own line
    /// and the best other are played on. The memos are the root's, so a sample valued before costs nothing again.
    /// </summary>
    private static TeacherValue[] Lines(TurnPlanner planner, IReadOnlyList<ActionId> candidates, TeacherSettings settings, PlayerSide side,
        AgentKnowledge knowledge, ObservationDeterminizer determinizer, List<GameState> samples, Dictionary<string, double> memo,
        Dictionary<string, double> screens, Func<GameState, ActionId, IReadOnlyList<int>, Action<EndOfTurn>?> ended)
    {
        int screenTurns = settings.ScreenTurns == 0 ? settings.HorizonTurns : settings.ScreenTurns;
        return [.. candidates.Select(action =>
        {
            (double[] values, int[] ranks) = TurnPlanner.BestLine(samples.Count,
                (index, ranks) => (planner.PlayLine(samples[index], action, ranks, side, knowledge, determinizer, screenTurns,
                    settings.Evaluation, settings.Cap, out int reached, screens, toTurnEnd: settings.ScreenTurns == 0) ?? 0, reached),
                settings.Lines,
                (index, ranks) => (planner.PlayLine(samples[index], action, ranks, side, knowledge, determinizer, settings.HorizonTurns,
                    settings.Evaluation, settings.Cap, out int reached, memo, endOfLine: ended(samples[index], action, ranks)) ?? 0, reached));
            return Summarize(action, [.. values]) with { Line = ranks };
        })];
    }

    private static int IndexOf(IReadOnlyList<ActionId> candidates, ActionId action)
    {
        for (int index = 0; index < candidates.Count; index++)
            if (candidates[index] == action) return index;
        throw new ArgumentException("The action is not a candidate.", nameof(action));
    }

    /// <summary>
    /// S4.2c, the ceiling: two more play-outs from where each line left the turn, on streams of their own seeded by the
    /// sample, so the label's samples are not touched. The re-deal deals one hidden world for all of a sample's end states
    /// (each card instance one identity in all of them, a card seen in any keeping it), so a pair is played again on
    /// common random numbers as its sample paired it; the reseed keeps every card and replaces only the engine's random
    /// stream, the play-out's own noise. Both go on to the horizon with the decisions the play-out had left.
    /// </summary>
    private static IEnumerable<EndLine> Repeat(TurnPlanner planner, List<(EndOfTurn End, EndLine Line)> held, PlayerSide side,
        AgentKnowledge knowledge, ObservationDeterminizer determinizer, OpponentCardPool pool, TeacherSettings settings)
    {
        var redealt = new Dictionary<int, double>();
        foreach (IGrouping<int, int> sample in Enumerable.Range(0, held.Count).GroupBy(index => held[index].Line.Sample))
        {
            int[] rows = [.. sample];
            ulong seed = settings.Seed ^ (0x9E3779B97F4A7C15UL * (ulong)(sample.Key + 1));
            IReadOnlyList<GameState> worlds = determinizer.SampleJointly([.. rows.Select(row => held[row].End.State)], pool, new DeterministicRandom(seed));
            for (int at = 0; at < rows.Length; at++)
            {
                EndOfTurn end = held[rows[at]].End;
                redealt[rows[at]] = planner.ContinueFrom(worlds[at], side, knowledge, determinizer, end.LastTurn, settings.Evaluation, end.CapLeft);
            }
        }
        for (int row = 0; row < held.Count; row++)
        {
            (EndOfTurn end, EndLine line) = held[row];
            ulong seed = settings.Seed ^ (0xD1B54A32D192ED03UL * (ulong)(line.Sample + 1));
            double reseeded = planner.ContinueFrom(ObservationSampler.Reseeded(end.State, seed), side, knowledge, determinizer,
                end.LastTurn, settings.Evaluation, end.CapLeft);
            yield return line with { Redeal = redealt[row], Reseed = reseeded };
        }
    }

    /// <summary>
    /// S1.7: whether the best two candidates are within two standard errors of each other (the standard error of their
    /// difference, taken as unpaired), so the root needs more samples to tell them apart.
    /// </summary>
    private static bool Close(TeacherValue[] values)
    {
        TeacherValue[] ranked = [.. values.Where(value => !double.IsNaN(value.Mean)).OrderByDescending(value => value.Mean)];
        if (ranked.Length < 2) return false;
        return ranked[0].Mean - ranked[1].Mean <= 2 * Math.Sqrt(ranked[0].StandardError * ranked[0].StandardError
            + ranked[1].StandardError * ranked[1].StandardError);
    }

    private static TeacherValue Summarize(ActionId action, List<double> values)
    {
        if (values.Count == 0) return new TeacherValue(action, double.NaN, double.NaN, 0);
        double mean = values.Average();
        double variance = values.Count > 1 ? values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1) : 0;
        return new TeacherValue(action, mean, Math.Sqrt(variance / values.Count), values.Count);
    }
}

/// <summary>
/// Bot plan S4.2b and S4.2c: a line played to the horizon, read where the root side's turn ended: feature set 5 there, the
/// play-out's value from there, the sample (its place in the root's deal) and the line (the candidate by index and the
/// heuristic's rank at each later turn action), and with the ceiling's repeat, the values of a play-out with the hidden
/// state dealt again (<paramref name="Redeal"/>) and of one with only the engine's random stream reseeded
/// (<paramref name="Reseed"/>).
/// </summary>
public sealed record EndLine(double[] Features, double Value, int Sample, int Candidate, IReadOnlyList<int> Ranks, double? Redeal,
    double? Reseed);
