using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// Plans the rest of a turn before each turn action (AI plan A3). The
/// heuristic's best few actions are each tried on several states sampled from
/// the agent's observation (engine plan E5): the action is applied in
/// Simulation mode, the rest of the turn is played out by the heuristic
/// policies of both sides, and the position at the end of the turn is scored
/// from the agent's own observation. The action with the best mean score is
/// chosen; the agent re-plans after every accepted action.
///
/// Samples depend only on the observation and the agent's checkpointed random
/// stream, and every simulated choice is deterministic, so hidden-state twins
/// get the same plan and a resumed agent repeats it.
///
/// Run search (A4) uses the same rollouts for the Runner's key choices inside
/// a run: whether to continue or jack out, and whether to pay to trash an
/// accessed card. Each candidate is played out by both heuristics until the
/// run ends, and the position is scored there.
/// </summary>
internal sealed partial class TurnPlanner
{
    /// <summary>Simulated decisions per rollout before it is scored where it stands.</summary>
    private const int RolloutDecisionCap = 60;

    private readonly IOfflinePolicy _policy;
    private readonly OfflineAgentConfiguration _configuration;
    private readonly ICardEvaluationCatalog _catalog;
    private readonly IOfflinePolicy _opponentPolicy;
    private readonly OfflineAgentConfiguration _opponentConfiguration;

    public TurnPlanner(IOfflinePolicy policy, OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog)
    {
        _policy = policy;
        _configuration = configuration;
        _catalog = catalog;
        PlayerSide opponent = configuration.Side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        // The opponent is modelled by the latest Standard heuristic, which never plans; plan-model 1 (an A6 Hard
        // setting) models it by the current Standard version instead.
        _opponentConfiguration = OfflinePolicies.Configure(opponent, "standard", 0,
            configuration.Parameters.GetValueOrDefault("plan-model") == 1 ? null : OfflinePolicies.LatestHeuristic(opponent));
        _opponentPolicy = OfflinePolicies.Create(_opponentConfiguration);
    }

    public int Samples => _configuration.Parameters.GetValueOrDefault("plan-samples");
    public int Width => _configuration.Parameters.GetValueOrDefault("plan-width");

    /// <summary>plan-lines 1 (bot plan S1.5): a turn-action candidate is valued at its best line of the turn, not its heuristic continuation.</summary>
    public bool Lines => _configuration.Parameters.GetValueOrDefault("plan-lines") == 1;
    public int RunSamples => _configuration.Parameters.GetValueOrDefault("run-samples");
    public int RunWidth => _configuration.Parameters.GetValueOrDefault("run-width");

    /// <summary>
    /// plan-value 1 (bot plan S5.1, brought forward by S4.2): a planned candidate's value is the mean over the samples of its
    /// leaf as a win probability less the sample's root (the position before it), and the heuristic's score is not part of
    /// it; among the top band's candidates within <see cref="Tie"/> of the best value, the heuristic's order decides.
    /// </summary>
    public bool Values => _configuration.Parameters.GetValueOrDefault("plan-value") == 1;

    /// <summary>plan-tie: the margin of value, in thousandths of a win probability, within which candidates tie (default 5).</summary>
    public int Tie => _configuration.Parameters.GetValueOrDefault("plan-tie", 5);

    /// <summary>Whether a decision is one the planner handles: a turn action with a click left, or a searched run choice.</summary>
    public bool Applies(AgentFeatures features) => AppliesToTurn(features) || AppliesToRun(features);

    private bool AppliesToTurn(AgentFeatures features) => Samples > 0 && Width > 1
        && features.Decision?.Kind == DecisionKind.TurnAction && features.Self.Clicks > 0 && features.Actions.Length > 1;

    /// <summary>
    /// A4: during a run, the Runner's choice between continuing and jacking out
    /// or trashing an accessed card or not, and the Corp's choice to rez a card
    /// (the approached ICE, or an upgrade or asset) or not.
    /// </summary>
    private bool AppliesToRun(AgentFeatures features) => RunSamples > 0 && RunWidth > 1
        && features.Board.Run is not null && features.Actions.Length > 1
        && features.Decision?.Kind != DecisionKind.TurnAction
        && features.Actions.Any(action => features.Side == PlayerSide.Runner
            ? action.Kind is ActionKind.JackOut or ActionKind.TrashAccessedCard
            : action.Kind is ActionKind.RezCard or ActionKind.RezIce);

    /// <summary>
    /// Re-scores the heuristic's candidates: the planned ones get the plan's
    /// value and outrank the rest, which keep their heuristic utility.
    /// </summary>
    public ImmutableArray<AgentCandidate> Plan(AgentFeatures features, ImmutableArray<AgentCandidate> heuristic,
        AgentKnowledge knowledge, PlanningContext planning)
    {
        long started = ProfileTimings.Start();
        bool run = !AppliesToTurn(features);
        int samples = run ? RunSamples : Samples;
        AgentCandidate[] planned = [.. heuristic
            .OrderByDescending(candidate => candidate.Utility.Priority)
            .ThenByDescending(candidate => candidate.Utility.Score)
            .ThenBy(candidate => candidate.ActionId.Value, StringComparer.Ordinal)
            .Take(run ? RunWidth : Width)];
        var totals = planned.ToDictionary(candidate => candidate.ActionId, _ => 0L);
        // plan-value 1: each rollout's leaf as a win probability, and each sample's root.
        var probabilities = planned.ToDictionary(candidate => candidate.ActionId, _ => 0.0);
        double roots = 0;
        OpponentCardPool pool = PoolFor(features.Side, knowledge);
        if (!run && Lines)
        {
            // Bot plan S1.5: a candidate is valued at its best line of the turn, on the same samples.
            GameState[] states = [.. Enumerable.Range(0, samples).Select(_ => Deal(features.Side, knowledge, pool, planning))];
            if (Values) roots = states.Sum(state => Probability(Score(state, features.Side, planning.Determinizer, knowledge)));
            foreach (AgentCandidate candidate in planned)
            {
                long rolling = ProfileTimings.Start();
                double[] lines = BestLine(samples, (index, ranks) =>
                    (Rollout(states[index], candidate.ActionId, ranks, features.Side, knowledge, planning.Determinizer, false, out int reached), reached),
                    Width).Values;
                totals[candidate.ActionId] = (long)lines.Sum();
                if (Values) probabilities[candidate.ActionId] = lines.Sum(value => Probability((long)value));
                ProfileTimings.Stop("plan.lines", rolling);
            }
        }
        else for (int sample = 0; sample < samples; sample++)
        {
            GameState state = Deal(features.Side, knowledge, pool, planning);
            if (Values) roots += Probability(Score(state, features.Side, planning.Determinizer, knowledge));
            foreach (AgentCandidate candidate in planned)
            {
                long rolling = ProfileTimings.Start();
                long leaf = Rollout(state, candidate.ActionId, features.Side, knowledge, planning.Determinizer, run);
                totals[candidate.ActionId] += leaf;
                if (Values) probabilities[candidate.ActionId] += Probability(leaf);
                ProfileTimings.Stop("plan.rollout", rolling);
            }
        }
        ProfileTimings.Stop("plan", started);
        if (Values) return Valued(heuristic, planned, probabilities, roots / samples, samples, run);
        // The heuristic knows things the end-of-turn score doesn't (pressure,
        // information, tempo), so a planned action keeps its heuristic score and
        // gains or loses what its rollouts show against the heuristic's own choice.
        long baseline = totals[planned[0].ActionId];
        return [.. heuristic.Select(candidate => totals.TryGetValue(candidate.ActionId, out long total)
            ? candidate with
            {
                Utility = candidate.Utility with
                {
                    Priority = PlannedPriority + Math.Clamp(candidate.Utility.Priority, -PlannedPriority / 2, PlannedPriority / 2),
                    Score = (int)Math.Clamp(candidate.Utility.Score + (total - baseline) / samples * Weight / 100,
                        int.MinValue / 2, int.MaxValue / 2),
                    Reason = $"plan/{(run ? "run-search" : "turn-rollout")} ({candidate.Utility.Reason})",
                },
            }
            : candidate)];
    }

    /// <summary>
    /// plan-value 1: a planned candidate's score is its value in millionths of a win probability; the top band's
    /// heuristic-first candidate within <see cref="Tie"/> of the band's best value is placed above the rest.
    /// </summary>
    private ImmutableArray<AgentCandidate> Valued(ImmutableArray<AgentCandidate> heuristic, AgentCandidate[] planned,
        Dictionary<ActionId, double> probabilities, double root, int samples, bool run)
    {
        int Band(AgentCandidate candidate) => PlannedPriority + Math.Clamp(candidate.Utility.Priority, -PlannedPriority / 2, PlannedPriority / 2);
        var values = planned.ToDictionary(candidate => candidate.ActionId, candidate => probabilities[candidate.ActionId] / samples - root);
        int top = planned.Max(Band);
        AgentCandidate[] band = [.. planned.Where(candidate => Band(candidate) == top)];
        double best = band.Max(candidate => values[candidate.ActionId]);
        // planned is in the heuristic's order.
        ActionId chosen = band.First(candidate => values[candidate.ActionId] >= best - Tie / 1000.0).ActionId;
        int Millionths(double value) => (int)Math.Round(value * 1_000_000);
        return [.. heuristic.Select(candidate => values.TryGetValue(candidate.ActionId, out double value)
            ? candidate with
            {
                Utility = candidate.Utility with
                {
                    Priority = Band(candidate),
                    Score = candidate.ActionId == chosen ? Millionths(best) + 1 : Millionths(value),
                    Reason = string.Create(CultureInfo.InvariantCulture,
                        $"plan/{(run ? "run-search" : "turn-rollout")} value {value:+0.0000;-0.0000} ({candidate.Utility.Reason})"),
                },
            }
            : candidate)];
    }

    /// <summary>A planner score (log-odds in thousandths) as a win probability; a first action the engine refuses is −1.</summary>
    private static double Probability(long score) => score <= int.MinValue / 8 ? -1 : 1 / (1 + Math.Exp(-score / 1000.0));

    /// <summary>The cards the opponent's unseen cards are dealt from: Hard's known list when it deals from it, otherwise the pool.</summary>
    internal OpponentCardPool PoolFor(PlayerSide side, AgentKnowledge knowledge) =>
        PoolFor(side, knowledge, DealsFromTracker && DealsFromList);

    /// <summary>The opponent's list when <paramref name="fromList"/> and the agent knows it (with a tracker), otherwise the pool.</summary>
    internal OpponentCardPool PoolFor(PlayerSide side, AgentKnowledge knowledge, bool fromList) =>
        fromList && knowledge.Tracker is not null && knowledge.OpponentList is { } list
            ? new OpponentCardPool.Decklist(list.ToDictionary(entry => new CardDefinitionId(entry.DefinitionId), entry => entry.Quantity))
            : OpponentPool(side);

    /// <summary>One state sampled from the agent's observation, with the tracker's pins when the profile deals from them.</summary>
    internal GameState Deal(PlayerSide side, AgentKnowledge knowledge, OpponentCardPool pool, PlanningContext planning) =>
        Deal(side, knowledge, pool, planning, DealsFromTracker && PinsTracked);

    /// <summary>One state sampled from the agent's observation, pinning the tracker's beliefs when <paramref name="pinned"/>.</summary>
    internal static GameState Deal(PlayerSide side, AgentKnowledge knowledge, OpponentCardPool pool, PlanningContext planning, bool pinned)
    {
        long sampling = ProfileTimings.Start();
        SamplerKnowledge known = pinned && knowledge.Tracker is not null ? Pins(knowledge.Tracker, side, planning) : SamplerKnowledge.None;
        GameState state = planning.Determinizer.Sample(known, pool, planning.Random);
        ProfileTimings.Stop("plan.sample", sampling);
        return state;
    }

    /// <summary>A9d (plan-tracker 1, a Hard setting): samples are dealt from the tracker's beliefs, and from the list when Hard knows it.</summary>
    public bool DealsFromTracker => _configuration.Parameters.GetValueOrDefault("plan-tracker") is 1 or 2 or 3;

    /// <summary>plan-tracker 1 pins the tracker's beliefs and deals the rest from the list; 2 only the list; 3 only the pins, the rest from the pool.</summary>
    private bool PinsTracked => _configuration.Parameters.GetValueOrDefault("plan-tracker") is 1 or 3;

    private bool DealsFromList => _configuration.Parameters.GetValueOrDefault("plan-tracker") is 1 or 2;

    /// <summary>
    /// A9d: one sample's pins from the tracker, drawn with its probabilities: a face-down slot is the card it's believed to
    /// be at that chance (or left to the deal); a tracked hand card is in the hand at its weight, at a random position; a
    /// known top of the deck is on top.
    /// </summary>
    private static SamplerKnowledge Pins(CardTracker tracker, PlayerSide side, PlanningContext planning)
    {
        ObservationDeterminizer determinizer = planning.Determinizer;
        DeterministicRandom random = planning.Random;
        bool corp = side == PlayerSide.Runner;
        var pins = new List<KnownHiddenCard>();
        foreach (IGrouping<(string? Server, string? Zone, int Index), TrackedCard> slot in tracker.Cards.Where(card => card.Place == TrackedPlace.Slot)
            .GroupBy(card => (card.ServerId, card.Zone, card.Index)))
        {
            if (determinizer.ZoneOf(slot.Key.Zone == "ice" ? ZoneKind.ServerIce : ZoneKind.ServerRoot, slot.Key.Server) is not { } zone) continue;
            double draw = random.NextInt(1_000_000) / 1_000_000.0, sum = 0;
            foreach (TrackedCard card in slot.OrderBy(card => card.DefinitionId, StringComparer.Ordinal))
            {
                sum += card.Weight;
                if (draw < sum) { pins.Add(new(zone, slot.Key.Index, new CardDefinitionId(card.DefinitionId))); break; }
            }
        }
        if (determinizer.ZoneOf(corp ? ZoneKind.CorpHand : ZoneKind.RunnerGrip) is { } hand)
        {
            List<int> free = [.. Enumerable.Range(0, determinizer.CountOf(hand))];
            foreach (TrackedCard card in tracker.Cards.Where(card => card.Place == TrackedPlace.Hand).OrderBy(card => card.DefinitionId, StringComparer.Ordinal))
            {
                if (free.Count == 0) break;
                if (random.NextInt(1_000_000) / 1_000_000.0 >= card.Weight) continue;
                int at = random.NextInt(free.Count);
                pins.Add(new(hand, free[at], new CardDefinitionId(card.DefinitionId)));
                free.RemoveAt(at);
            }
        }
        if (determinizer.ZoneOf(corp ? ZoneKind.CorpDeck : ZoneKind.RunnerStack) is { } deck)
            foreach (TrackedCard card in tracker.Cards.Where(card => card.Place == TrackedPlace.DeckTop && card.Weight >= 0.5))
                if (card.Depth < determinizer.CountOf(deck)) pins.Add(new(deck, card.Depth, new CardDefinitionId(card.DefinitionId)));
        return new SamplerKnowledge(pins);
    }

    /// <summary>Added to every planned action's priority, so a planned action is always preferred to an unplanned one.</summary>
    public const int PlannedPriority = 1_000_000;

    /// <summary>How much the rollouts count against the heuristic's score, in percent.</summary>
    public int Weight => _configuration.Parameters.GetValueOrDefault("plan-weight", 100);

    private long Rollout(GameState sample, ActionId first, PlayerSide side, AgentKnowledge knowledge, ObservationDeterminizer determinizer,
        bool untilRunEnds) => Rollout(sample, first, [], side, knowledge, determinizer, untilRunEnds, out _);

    /// <summary>A turn rollout of a line (bot plan S1.5): at the root side's later turn actions, the heuristic's <paramref name="ranks"/>[i]-th choice.</summary>
    private long Rollout(GameState sample, ActionId first, int[] ranks, PlayerSide side, AgentKnowledge knowledge,
        ObservationDeterminizer determinizer, bool untilRunEnds, out int reached)
    {
        reached = 0;
        int turn = sample.Turn.TurnNumber;
        GameState state = sample;
        ActionId? next = first;
        for (int step = 0; step < RolloutDecisionCap && next is { } action; step++)
        {
            ApplyResult applied = Simulate(state, action, determinizer);
            if (!applied.Accepted) return step == 0 ? int.MinValue / 4 : Score(state, side, determinizer, knowledge);
            state = applied.State;
            if (state.Outcome.Kind != GameOutcomeKind.Ongoing || state.PendingDecision is null
                || state.Turn.TurnNumber != turn || state.Turn.ActiveSide != side
                || untilRunEnds && state.RunState is null)
                break;
            bool turnAction = state.PendingDecision.Kind == DecisionKind.TurnAction && state.RunState is null
                && state.GetPlayer(state.PendingDecision.Owner).Side == side;
            int rank = turnAction && reached < ranks.Length ? ranks[reached] : 0;
            if (turnAction) reached++;
            next = Choose(state, knowledge, determinizer, side, rank);
        }
        return Score(state, side, determinizer, knowledge);
    }

    /// <summary>
    /// Bot plan S0.1: plays a sampled state on to the game's end (or <paramref name="cap"/> simulated decisions), both
    /// sides choosing as they do in a turn rollout. It measures what a rollout past the turn's end costs, for the teacher
    /// of the regret suite (S1.2).
    /// </summary>
    internal (GameState State, int Steps) PlayToEnd(GameState sample, PlayerSide side, AgentKnowledge knowledge,
        ObservationDeterminizer determinizer, int cap)
    {
        GameState state = sample;
        int steps = 0;
        while (steps < cap && state.Outcome.Kind == GameOutcomeKind.Ongoing && state.PendingDecision is not null
            && Choose(state, knowledge, determinizer, side) is { } action)
        {
            ApplyResult applied = Simulate(state, action, determinizer);
            if (!applied.Accepted) break;
            state = applied.State;
            steps++;
        }
        return (state, steps);
    }

    /// <summary>
    /// Bot plan S1.2, the teacher: <paramref name="first"/> applied to a sample, then both sides' rollout choices until
    /// the game ends, <paramref name="horizonTurns"/> turns have passed (0: no limit), or <paramref name="cap"/> simulated
    /// decisions. The value is 1 or 0 for a finished game, otherwise the fitted evaluation's win probability for
    /// <paramref name="side"/>; null when the action is not legal in the sample.
    /// </summary>
    internal double? PlayOut(GameState sample, ActionId first, PlayerSide side, AgentKnowledge knowledge,
        ObservationDeterminizer determinizer, int horizonTurns, int evaluation, int cap) =>
        PlayLine(sample, first, [], side, knowledge, determinizer, horizonTurns, evaluation, cap, out _);

    /// <summary>
    /// Bot plan S1.5, a line of the root side's turn: <paramref name="first"/>, then at each later turn-action decision of
    /// <paramref name="side"/> in the same turn the heuristic's <paramref name="ranks"/>[i]-th distinct choice (its last past the
    /// list), and everything else (nested choices, the opponent, later turns) as <see cref="PlayOut"/> plays it. A line is a
    /// rank path, not a list of actions, so it plays on every sample the same way, without reading a sample's hidden cards.
    /// <paramref name="reached"/> counts the later turn-action decisions the line met. With a <paramref name="memo"/>, a
    /// line that leaves the turn in a state another line left it in (the same actions in another order, often) takes that
    /// line's value: the rest of the play-out is a function of the state. With <paramref name="toTurnEnd"/>, the line is
    /// scored by the leaf where the root side's turn ends (the teacher's screen). <paramref name="endOfLine"/> (bot plan
    /// S4.2b) is shown the state where the root side's turn ended and the play-out's value from it, once per line played
    /// past it: not for a line the memo already valued, nor one whose game ended inside the turn.
    /// </summary>
    internal double? PlayLine(GameState sample, ActionId first, IReadOnlyList<int> ranks, PlayerSide side, AgentKnowledge knowledge,
        ObservationDeterminizer determinizer, int horizonTurns, int evaluation, int cap, out int reached,
        Dictionary<string, double>? memo = null, bool toTurnEnd = false, Action<GameState>? observe = null,
        Action<GameState, double>? endOfLine = null)
    {
        reached = 0;
        ApplyResult applied = Simulate(sample, first, determinizer);
        if (!applied.Accepted) return null;
        GameState state = applied.State;
        observe?.Invoke(state);
        int turn = sample.Turn.TurnNumber;
        int lastTurn = horizonTurns > 0 ? turn + horizonTurns : int.MaxValue;
        string? key = null;
        GameState? turnEnd = null;
        bool Ended(GameState at) => at.Turn.TurnNumber != turn || at.Turn.ActiveSide != side;
        for (int steps = 0; steps < cap && state.Outcome.Kind == GameOutcomeKind.Ongoing && state.PendingDecision is { } pending
            && state.Turn.TurnNumber < lastTurn; steps++)
        {
            if (turnEnd is null && Ended(state)) turnEnd = state;
            if (key is null && Ended(state))
            {
                if (toTurnEnd) break;
                if (memo is not null)
                {
                    key = string.Create(CultureInfo.InvariantCulture, $"{steps}:{EngineJson.SearchFingerprint(state)}");
                    if (memo.TryGetValue(key, out double known)) return known;
                }
            }
            bool turnAction = pending.Kind == DecisionKind.TurnAction && state.RunState is null && state.Turn.TurnNumber == turn
                && state.Turn.ActiveSide == side && state.GetPlayer(pending.Owner).Side == side;
            int rank = turnAction && reached < ranks.Count ? ranks[reached] : 0;
            if (turnAction) reached++;
            if (Choose(state, knowledge, determinizer, side, rank) is not { } action) break;
            applied = Simulate(state, action, determinizer);
            if (!applied.Accepted) break;
            state = applied.State;
            observe?.Invoke(state);
        }
        double value = Leaf(state, side, determinizer, evaluation, knowledge);
        if (key is not null) memo![key] = value;
        if (endOfLine is not null && !toTurnEnd)
        {
            // A turn that ends on the horizon's step leaves the loop before it is seen.
            turnEnd ??= state.Outcome.Kind == GameOutcomeKind.Ongoing && Ended(state) ? state : null;
            if (turnEnd is not null) endOfLine(turnEnd, value);
        }
        return value;
    }

    /// <summary>Bot plan S4.2b: feature set 5 (set 4 its first terms) of a state, from <paramref name="side"/>'s view with its knowledge.</summary>
    internal double[] LeafFeatures(GameState state, PlayerSide side, ObservationDeterminizer determinizer, AgentKnowledge knowledge) =>
        EvaluationFeatures.Extract(ObservationReader.Read(ObservationBuilder.Build(state, side, determinizer.Behavior), _configuration.FeatureVersion),
            _catalog, 5, knowledge);

    private double Leaf(GameState state, PlayerSide side, ObservationDeterminizer determinizer, int evaluation, AgentKnowledge knowledge)
    {
        if (state.Outcome.Kind != GameOutcomeKind.Ongoing) return state.Outcome.Winner == side ? 1 : 0;
        long scoring = ProfileTimings.Start();
        AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, side, determinizer.Behavior),
            _configuration.FeatureVersion);
        double value = FittedEvaluation.WinProbability(features, _catalog, evaluation, determinizer.Behavior.Version, knowledge);
        ProfileTimings.Stop("plan.leaf", scoring);
        return value;
    }

    /// <summary>The most later turn-action decisions a line of the turn ranks (bot plan S1.5); past them it takes the heuristic's choice.</summary>
    internal const int LineDepth = 8;

    /// <summary>
    /// Bot plan S1.5: the best line of the root side's turn by a beam over rank paths (see <see cref="PlayLine"/>).
    /// <paramref name="screen"/> values a rank path on a sample and counts the later turn-action decisions it met; every
    /// line is screened on every sample, or with <paramref name="play"/> on the first quarter of them. A line's children differ from it at the next decision, taking the heuristic's
    /// 2nd to <paramref name="width"/>-th choice there, and the best <paramref name="width"/> lines go on, to
    /// <see cref="LineDepth"/> decisions or the turn's end. Without <paramref name="play"/> the best screened line is the
    /// line. With it (the teacher, whose screen is a short play-out and whose play goes to the horizon), the heuristic's
    /// own line is played first, and when it meets a later decision the best <paramref name="finalists"/> − 1 other
    /// screened lines are played too; the best of them is the line, so a candidate is never valued below its heuristic
    /// continuation on the same samples.
    /// </summary>
    /// <returns>The best line's value on every sample, and its rank path.</returns>
    internal static (double[] Values, int[] Ranks) BestLine(int samples, Func<int, int[], (double Value, int Reached)> screen, int width,
        Func<int, int[], (double Value, int Reached)>? play = null, int finalists = 2)
    {
        // Sequential halving: with a play-out to come, the screen sees a quarter of the samples.
        int screening = play is null ? samples : Math.Max(1, samples / 4);
        static (double[] Values, double Mean, int Reached) Run(int samples, Func<int, int[], (double Value, int Reached)> value, int[] ranks)
        {
            double[] values = new double[samples];
            int reached = 0;
            for (int index = 0; index < samples; index++)
            {
                (values[index], int met) = value(index, ranks);
                reached = Math.Max(reached, met);
            }
            return (values, values.Average(), reached);
        }
        (double[] Values, double Mean, int Reached)? played = play is null ? null : Run(samples, play, []);
        if (played is { Reached: 0 } alone) return (alone.Values, []);
        var root = (Ranks: Array.Empty<int>(), Screen: Run(screening, screen, []));
        var screened = new List<(int[] Ranks, (double[] Values, double Mean, int Reached) Screen)> { root };
        var frontier = new List<(int[] Ranks, (double[] Values, double Mean, int Reached) Screen)> { root };
        for (int level = 0; level < LineDepth; level++)
        {
            var children = new List<(int[] Ranks, (double[] Values, double Mean, int Reached) Screen)>();
            foreach ((int[] ranks, _) in frontier.Where(line => line.Screen.Reached > level))
                for (int rank = 1; rank < width; rank++)
                {
                    int[] child = [.. ranks, .. Enumerable.Repeat(0, level - ranks.Length), rank];
                    children.Add((child, Run(screening, screen, child)));
                }
            if (children.Count == 0) break;
            screened.AddRange(children);
            frontier = [.. frontier.Concat(children).OrderByDescending(line => line.Screen.Mean).ThenBy(line => line.Ranks.Length).Take(width)];
        }
        if (played is not { } heuristic)
        {
            var top = screened.OrderByDescending(line => line.Screen.Mean).ThenBy(line => line.Ranks.Length).First();
            return (top.Screen.Values, top.Ranks);
        }
        (double[] Values, int[] Ranks) best = (heuristic.Values, []);
        double bestMean = heuristic.Mean;
        foreach ((int[] ranks, _) in screened.Skip(1).OrderByDescending(line => line.Screen.Mean).ThenBy(line => line.Ranks.Length)
            .Take(finalists - 1))
        {
            (double[] values, double mean, _) = Run(samples, play!, ranks);
            if (mean > bestMean) (best, bestMean) = ((values, ranks), mean);
        }
        return best;
    }

    /// <summary>Whether <paramref name="first"/> can be played on a sample: the common-sample rule's test.</summary>
    internal static bool Playable(GameState sample, ActionId first, ObservationDeterminizer determinizer) =>
        Simulate(sample, first, determinizer).Accepted;

    private static ApplyResult Simulate(GameState state, ActionId action, ObservationDeterminizer determinizer)
    {
        long applying = ProfileTimings.Start();
        PendingDecision pending = state.PendingDecision!;
        ApplyResult applied = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action),
            determinizer.Behavior, determinizer.Runtime, ApplyMode.Simulation);
        ProfileTimings.Stop("plan.apply", applying);
        return applied;
    }

    /// <summary>The heuristic choice of whichever side owns the simulated decision.</summary>
    private ActionId? Choose(GameState state, AgentKnowledge knowledge, ObservationDeterminizer determinizer, PlayerSide side, int rank = 0)
    {
        PendingDecision pending = state.PendingDecision!;
        // A forced choice (usually a pass) needs no observation.
        if (pending.LegalActions.Count == 1) return pending.LegalActions[0].Id;
        PlayerSide owner = state.GetPlayer(pending.Owner).Side;
        bool own = owner == side;
        OfflineAgentConfiguration configuration = own ? _configuration : _opponentConfiguration;
        long observing = ProfileTimings.Start();
        Observation observation = ObservationBuilder.Build(state, owner, determinizer.Behavior);
        ProfileTimings.Stop("plan.choose.observation.build", observing);
        observing = ProfileTimings.Start();
        AgentFeatures features = ObservationReader.Read(observation, configuration.FeatureVersion);
        ProfileTimings.Stop("plan.choose.observation.read", observing);
        if (features.Actions.IsEmpty) return null;
        long evaluating = ProfileTimings.Start();
        ImmutableArray<AgentCandidate> candidates = (own ? _policy : _opponentPolicy).EvaluateDecision(features,
            own ? knowledge : AgentKnowledge.Empty, null, configuration, _catalog, new AgentWorkBudget(configuration.WorkBudget));
        AgentCandidate[] ordered = [.. candidates
            .OrderByDescending(candidate => candidate.Utility.Priority)
            .ThenByDescending(candidate => candidate.Utility.Score)
            .ThenBy(candidate => candidate.ActionId.Value, StringComparer.Ordinal)];
        ActionId chosen = rank == 0 ? ordered[0].ActionId : Distinct(state, pending, ordered, rank);
        ProfileTimings.Stop("plan.choose.heuristic", evaluating);
        return chosen;
    }

    /// <summary>
    /// The heuristic's <paramref name="rank"/>-th distinct choice (its last past the list), for a line of the turn (bot
    /// plan S1.5): actions that differ only in which copy of a card they use are one choice.
    /// </summary>
    private static ActionId Distinct(GameState state, PendingDecision pending, AgentCandidate[] ordered, int rank)
    {
        var definitions = state.Cards.ToDictionary(card => card.Id.Value, card => card.DefinitionId.Value, StringComparer.Ordinal);
        var actions = pending.LegalActions.ToDictionary(action => action.Id);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        ActionId chosen = ordered[0].ActionId;
        foreach (AgentCandidate candidate in ordered)
        {
            if (!actions.TryGetValue(candidate.ActionId, out LegalAction? action)) continue;
            string key = action.Kind + CardInstance().Replace(EngineJson.Serialize(action.Parameters),
                match => definitions.TryGetValue(match.Value, out string? definition) ? "def:" + definition : match.Value);
            if (!seen.Add(key)) continue;
            chosen = candidate.ActionId;
            if (seen.Count > rank) break;
        }
        return chosen;
    }

    [GeneratedRegex(@"card:\d+", RegexOptions.CultureInvariant)]
    private static partial Regex CardInstance();

    private long Score(GameState state, PlayerSide side, ObservationDeterminizer determinizer, AgentKnowledge knowledge)
    {
        long scoring = ProfileTimings.Start();
        try { return ScoreLeaf(state, side, determinizer, knowledge); }
        finally { ProfileTimings.Stop("plan.leaf", scoring); }
    }

    /// <param name="knowledge">The planner's own, which set 5 reads (S4.2): the leaf sees what the planner knows.</param>
    private long ScoreLeaf(GameState state, PlayerSide side, ObservationDeterminizer determinizer, AgentKnowledge knowledge)
    {
        AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, side, determinizer.Behavior),
            _configuration.FeatureVersion);
        // A5: planning versions score rollouts by the fitted win probability (as log-odds).
        // plan-evaluation 2: the refitted evaluation (feature set 2) Corp v11 plans with.
        // A6: a personality shifts named terms of the fitted evaluation.
        // S4.2: plan-evaluation 5 is set 5 fitted to the teacher, 6 set 4's features refitted to it; S4.2b's 7 and 8 the
        // same fitted where the turn ends.
        return _configuration.Parameters.GetValueOrDefault("plan-evaluation") is int version and >= 1 and <= 8
            ? FittedEvaluation.Score(features, _catalog, version, AgentPersonalities.Of(_configuration), determinizer.Behavior.Version, knowledge)
            : PositionEvaluation.Score(features, _catalog, _configuration);
    }

    private readonly Dictionary<PlayerSide, OpponentCardPool> _pools = [];

    /// <summary>Unseen opponent cards may be any Gateway card of their side: the agent doesn't know the opponent's deck.</summary>
    private OpponentCardPool OpponentPool(PlayerSide side)
    {
        if (_pools.TryGetValue(side, out OpponentCardPool? pool)) return pool;
        PlayerSide opponent = side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        CardDefinitionId[] cards = [.. _catalog.DefinitionIds
            .Where(id => _catalog.TryGet(id, out CardEvaluationDescriptor descriptor) && descriptor.Printed.Side == opponent
                && descriptor.Printed.CardClass != RuntimeCardClass.Identity)
            .OrderBy(id => id.Value, StringComparer.Ordinal)];
        return _pools[side] = new OpponentCardPool.AnyOf(cards);
    }
}

/// <summary>
/// The heuristic's priority bands, read through the planner's offset (bot plan S1.4): a planned candidate's priority is
/// <see cref="Planned"/> plus its band, so a planned and an unplanned candidate of one band compare as equals.
/// </summary>
public static class PlannerPriorities
{
    public const int Planned = TurnPlanner.PlannedPriority;

    /// <summary>The band of a candidate's priority: 0 for an ordinary action, below it for a discouraged or vetoed one.</summary>
    public static int Band(int priority) => priority >= Planned / 2 ? priority - Planned : priority;
}
