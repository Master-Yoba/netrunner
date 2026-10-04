using System.Collections.Immutable;
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
internal sealed class TurnPlanner
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
    public int RunSamples => _configuration.Parameters.GetValueOrDefault("run-samples");
    public int RunWidth => _configuration.Parameters.GetValueOrDefault("run-width");

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
        OpponentCardPool pool = PoolFor(features.Side, knowledge);
        for (int sample = 0; sample < samples; sample++)
        {
            GameState state = Deal(features.Side, knowledge, pool, planning);
            foreach (AgentCandidate candidate in planned)
            {
                long rolling = ProfileTimings.Start();
                totals[candidate.ActionId] += Rollout(state, candidate.ActionId, features.Side, knowledge, planning.Determinizer, run);
                ProfileTimings.Stop("plan.rollout", rolling);
            }
        }
        ProfileTimings.Stop("plan", started);
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
        bool untilRunEnds)
    {
        int turn = sample.Turn.TurnNumber;
        GameState state = sample;
        ActionId? next = first;
        for (int step = 0; step < RolloutDecisionCap && next is { } action; step++)
        {
            ApplyResult applied = Simulate(state, action, determinizer);
            if (!applied.Accepted) return step == 0 ? int.MinValue / 4 : Score(state, side, determinizer);
            state = applied.State;
            if (state.Outcome.Kind != GameOutcomeKind.Ongoing || state.PendingDecision is null
                || state.Turn.TurnNumber != turn || state.Turn.ActiveSide != side
                || untilRunEnds && state.RunState is null)
                break;
            next = Choose(state, knowledge, determinizer, side);
        }
        return Score(state, side, determinizer);
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
        ObservationDeterminizer determinizer, int horizonTurns, int evaluation, int cap)
    {
        ApplyResult applied = Simulate(sample, first, determinizer);
        if (!applied.Accepted) return null;
        GameState state = applied.State;
        int lastTurn = horizonTurns > 0 ? sample.Turn.TurnNumber + horizonTurns : int.MaxValue;
        for (int steps = 0; steps < cap && state.Outcome.Kind == GameOutcomeKind.Ongoing && state.PendingDecision is not null
            && state.Turn.TurnNumber < lastTurn && Choose(state, knowledge, determinizer, side) is { } action; steps++)
        {
            applied = Simulate(state, action, determinizer);
            if (!applied.Accepted) break;
            state = applied.State;
        }
        if (state.Outcome.Kind != GameOutcomeKind.Ongoing) return state.Outcome.Winner == side ? 1 : 0;
        long scoring = ProfileTimings.Start();
        AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, side, determinizer.Behavior),
            _configuration.FeatureVersion);
        double value = FittedEvaluation.WinProbability(features, _catalog, evaluation, determinizer.Behavior.Version);
        ProfileTimings.Stop("plan.leaf", scoring);
        return value;
    }

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
    private ActionId? Choose(GameState state, AgentKnowledge knowledge, ObservationDeterminizer determinizer, PlayerSide side)
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
        ActionId chosen = candidates
            .OrderByDescending(candidate => candidate.Utility.Priority)
            .ThenByDescending(candidate => candidate.Utility.Score)
            .ThenBy(candidate => candidate.ActionId.Value, StringComparer.Ordinal)
            .First().ActionId;
        ProfileTimings.Stop("plan.choose.heuristic", evaluating);
        return chosen;
    }

    private long Score(GameState state, PlayerSide side, ObservationDeterminizer determinizer)
    {
        long scoring = ProfileTimings.Start();
        try { return ScoreLeaf(state, side, determinizer); }
        finally { ProfileTimings.Stop("plan.leaf", scoring); }
    }

    private long ScoreLeaf(GameState state, PlayerSide side, ObservationDeterminizer determinizer)
    {
        AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, side, determinizer.Behavior),
            _configuration.FeatureVersion);
        // A5: planning versions score rollouts by the fitted win probability (as log-odds).
        // plan-evaluation 2: the refitted evaluation (feature set 2) Corp v11 plans with.
        // A6: a personality shifts named terms of the fitted evaluation.
        return _configuration.Parameters.GetValueOrDefault("plan-evaluation") is int version and (1 or 2 or 3 or 4)
            ? FittedEvaluation.Score(features, _catalog, version, AgentPersonalities.Of(_configuration), determinizer.Behavior.Version)
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
