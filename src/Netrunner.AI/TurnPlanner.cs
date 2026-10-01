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
        // The opponent is modelled by the latest Standard heuristic, which never plans.
        _opponentConfiguration = OfflinePolicies.Configure(opponent, "standard", 0, OfflinePolicies.LatestHeuristic(opponent));
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
        bool run = !AppliesToTurn(features);
        int samples = run ? RunSamples : Samples;
        AgentCandidate[] planned = [.. heuristic
            .OrderByDescending(candidate => candidate.Utility.Priority)
            .ThenByDescending(candidate => candidate.Utility.Score)
            .ThenBy(candidate => candidate.ActionId.Value, StringComparer.Ordinal)
            .Take(run ? RunWidth : Width)];
        var totals = planned.ToDictionary(candidate => candidate.ActionId, _ => 0L);
        OpponentCardPool pool = OpponentPool(features.Side);
        for (int sample = 0; sample < samples; sample++)
        {
            GameState state = planning.Determinizer.Sample(SamplerKnowledge.None, pool, planning.Random);
            foreach (AgentCandidate candidate in planned)
                totals[candidate.ActionId] += Rollout(state, candidate.ActionId, features.Side, knowledge, planning.Determinizer, run);
        }
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
            PendingDecision pending = state.PendingDecision!;
            ApplyResult applied = GameEngine.Apply(state, new ActionSubmission(pending.Owner, pending.Id, pending.Revision, action),
                determinizer.Behavior, determinizer.Runtime, ApplyMode.Simulation);
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

    /// <summary>The heuristic choice of whichever side owns the simulated decision.</summary>
    private ActionId? Choose(GameState state, AgentKnowledge knowledge, ObservationDeterminizer determinizer, PlayerSide side)
    {
        PendingDecision pending = state.PendingDecision!;
        // A forced choice (usually a pass) needs no observation.
        if (pending.LegalActions.Count == 1) return pending.LegalActions[0].Id;
        PlayerSide owner = state.GetPlayer(pending.Owner).Side;
        bool own = owner == side;
        OfflineAgentConfiguration configuration = own ? _configuration : _opponentConfiguration;
        AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, owner, determinizer.Behavior),
            configuration.FeatureVersion);
        if (features.Actions.IsEmpty) return null;
        ImmutableArray<AgentCandidate> candidates = (own ? _policy : _opponentPolicy).EvaluateDecision(features,
            own ? knowledge : AgentKnowledge.Empty, null, configuration, _catalog, new AgentWorkBudget(configuration.WorkBudget));
        return candidates
            .OrderByDescending(candidate => candidate.Utility.Priority)
            .ThenByDescending(candidate => candidate.Utility.Score)
            .ThenBy(candidate => candidate.ActionId.Value, StringComparer.Ordinal)
            .First().ActionId;
    }

    private long Score(GameState state, PlayerSide side, ObservationDeterminizer determinizer)
    {
        AgentFeatures features = ObservationReader.Read(ObservationBuilder.Build(state, side, determinizer.Behavior),
            _configuration.FeatureVersion);
        // A5: planning versions score rollouts by the fitted win probability (as log-odds).
        // plan-evaluation 2: the refitted evaluation (feature set 2) Corp v11 plans with.
        // A6: a personality shifts named terms of the fitted evaluation.
        return _configuration.Parameters.GetValueOrDefault("plan-evaluation") is int version and (1 or 2 or 3)
            ? FittedEvaluation.Score(features, _catalog, version, AgentPersonalities.Of(_configuration))
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
