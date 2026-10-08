using System.Collections.Immutable;
using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>A pure policy; no state, random stream, clock or authoritative engine is supplied.</summary>
public interface IOfflinePolicy
{
    string Version { get; }
    void ValidateAction(AgentFeatures features, AgentAction action) { }
    AgentUtility Evaluate(AgentFeatures features, AgentAction action, AgentKnowledge knowledge, AgentObjective? objective,
        OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget);
    AgentUtility Fallback(AgentFeatures features, AgentAction action);

    ImmutableArray<AgentCandidate> EvaluateDecision(AgentFeatures features, AgentKnowledge knowledge, AgentObjective? objective,
        OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget) =>
        features.Actions.Select(action => new AgentCandidate(action.Id,
            budget.TrySpend() ? Evaluate(features, action, knowledge, objective, configuration, catalog, budget)
                : Fallback(features, action) with { UsedFallback = true })).ToImmutableArray();

    /// <summary>
    /// As the overload above, when the host lets the agent plan. A policy that
    /// doesn't plan ignores <paramref name="planning"/>, so its decisions and
    /// its use of the random stream are unchanged.
    /// </summary>
    ImmutableArray<AgentCandidate> EvaluateDecision(AgentFeatures features, AgentKnowledge knowledge, AgentObjective? objective,
        OfflineAgentConfiguration configuration, ICardEvaluationCatalog catalog, AgentWorkBudget budget, PlanningContext? planning) =>
        EvaluateDecision(features, knowledge, objective, configuration, catalog, budget);
}

/// <summary>
/// What a planning policy may use beyond its observation (AI plan A3): samples
/// of the hidden state consistent with that observation, and the agent's own
/// checkpointed random stream, so planning stays deterministic and resumable.
/// </summary>
public sealed record PlanningContext(ObservationDeterminizer Determinizer, DeterministicRandom Random);

public sealed record AgentUtility(int Priority, int Score, string Reason, bool Approximate = false, AgentObjective? Objective = null, bool UsedFallback = false);
public sealed record AgentCandidate(ActionId ActionId, AgentUtility Utility);
public sealed record AgentEvaluation(string ObservationDigest, string CheckpointDigest,
    ImmutableArray<AgentCandidate> Candidates, ActionId SelectedAction, int WorkUsed, bool BudgetExhausted);

public sealed class AgentWorkBudget
{
    private readonly CancellationToken _cancellationToken;
    public AgentWorkBudget(int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        Limit = limit;
        _cancellationToken = cancellationToken;
    }
    public int Limit { get; }
    public int Used { get; private set; }
    public bool Exhausted { get; private set; }
    public bool TrySpend(int operations = 1)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operations);
        if (operations > Limit - Used) { Exhausted = true; return false; }
        Used += operations;
        return true;
    }
}

/// <summary>
/// Transactional policy driver. A host serializes calls; tentative evaluation is
/// discarded unless its exact issued decision/action is committed after Apply.
/// </summary>
public sealed class OfflineAgent : IAgentLifecycle
{
    private readonly IOfflinePolicy _policy;
    private readonly ICardEvaluationCatalog _catalog;
    private readonly bool _captureDiagnostics;
    private OfflineAgentCheckpoint _checkpoint;
    private PendingEvaluation? _pending;

    /// <summary>A9: whether this agent's version tracks the opponent's hidden cards.</summary>
    private bool TracksOpponent => Configuration.Side == PlayerSide.Runner
        ? RunnerHeuristicPolicy.UsesTracker(Configuration.PolicyVersion)
        : CorpHeuristicPolicy.UsesTracker(Configuration.PolicyVersion);

    /// <summary>A9: Easy forgets on purpose; Standard and Hard keep what they've seen.</summary>
    private TrackerMemory Memory => Configuration.ProfileId == "easy" ? TrackerMemory.Forgetful : TrackerMemory.Full;

    private CardTracker Track(AgentKnowledge knowledge, AgentFeatures features)
    {
        PlayerSide opponent = Configuration.Side == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp;
        Dictionary<string, double> prior = TrackerPrior.Remaining(knowledge.OpponentList is { } list
            ? TrackerPrior.List(list.Select(entry => (entry.DefinitionId, entry.Quantity)))
            : TrackerPrior.Pool(_catalog, opponent), features);
        return (knowledge.Tracker ?? CardTracker.Empty).Observe(features, _catalog,
            zone => TrackerPrior.Share(prior, definition => TrackerPrior.Fits(_catalog, definition, zone)), Memory);
    }

    /// <summary>
    /// A9: a Hard bot knows the opponent's decklist (decided 2 October); Easy and Standard know only the pool. Set once, when
    /// the bot is created; it's part of the checkpoint, so a resumed game keeps it.
    /// </summary>
    public void KnowOpponentList(IEnumerable<(string DefinitionId, int Quantity)> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (!TracksOpponent) return;
        _checkpoint = _checkpoint with
        {
            Knowledge = _checkpoint.Knowledge with
            {
                OpponentList = [.. list.GroupBy(entry => entry.DefinitionId, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new AgentListEntry(group.Key, group.Sum(entry => entry.Quantity)))],
            },
        };
    }

    public OfflineAgent(OfflineAgentConfiguration configuration, IOfflinePolicy policy, ICardEvaluationCatalog catalog,
        OfflineAgentCheckpoint? checkpoint = null, bool captureDiagnostics = false)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(catalog);
        configuration.Validate();
        if (configuration.PolicyVersion != policy.Version) throw new ArgumentException("Policy version mismatch.", nameof(policy));
        Configuration = configuration with { Parameters = configuration.Parameters.WithComparers(StringComparer.Ordinal) };
        _policy = policy;
        _catalog = catalog;
        _captureDiagnostics = captureDiagnostics;
        _checkpoint = checkpoint ?? OfflineAgentCheckpoint.Initial(configuration);
        ValidateCheckpoint(_checkpoint);
    }

    public PlayerSide ControlledSide => Configuration.Side;
    public OfflineAgentConfiguration Configuration { get; }
    internal IOfflinePolicy Policy => _policy;
    internal ICardEvaluationCatalog Catalog => _catalog;
    public OfflineAgentCheckpoint Checkpoint => _checkpoint;
    // Opt-in callers may inspect this private decision report. Hosts must not
    // put its contents into player history or opponent tooltips.
    public AgentEvaluation? LastEvaluation { get; private set; }

    public void Observe(Observation observation, long actionSequence)
    {
        AgentFeatures features = ReadOwn(observation);
        string digest = ObservationReader.Digest(features);
        if (actionSequence == _checkpoint.ObservationCursor && digest == _checkpoint.ObservationDigest) return;
        if (actionSequence != _checkpoint.ObservationCursor + 1)
            throw new InvalidOperationException("Observation cursor must advance exactly once per accepted action.");
        AgentObjective? objective = _checkpoint.Objective;
        if (objective is not null && ((objective.RunId is not null && objective.RunId != features.Board.Run?.Id)
            || (objective.EncounterId is not null && objective.EncounterId != features.Board.Run?.Encounter?.Id)
            || (objective.TargetCardId is not null && !features.VisibleCards().Any(pair => pair.Card.Id == objective.TargetCardId)))) objective = null;
        AgentKnowledge knowledge = _checkpoint.Knowledge.Observe(features, actionSequence,
            Configuration.Side == PlayerSide.Runner && RunnerHeuristicPolicy.KeepsRootMemoryThroughInstalls(Configuration.PolicyVersion));
        if (TracksOpponent) knowledge = knowledge with { Tracker = Track(knowledge, features) };
        _checkpoint = _checkpoint with
        {
            ObservationCursor = actionSequence,
            ObservationDigest = digest,
            Knowledge = knowledge,
            Objective = objective,
        };
        DiscardDecision();
    }

    public ValueTask<ActionId> ChooseActionAsync(Observation observation, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_checkpoint.LastCommit is { } commit && commit.ActionSequence > _checkpoint.ObservationCursor)
                throw new InvalidOperationException("Host must deliver the post-commit observation before another choice.");
            AgentFeatures features = ReadOwn(observation);
            AgentDecision decision = features.Decision ?? throw new InvalidOperationException("Agent does not own a decision.");
            if (features.Actions.IsEmpty) throw new InvalidOperationException("No issued actions.");
            string digest = ObservationReader.Digest(features);
            if (digest != _checkpoint.ObservationDigest) throw new InvalidOperationException("Host must observe the committed boundary before choosing.");
            if (_pending is { } retry && retry.Digest == digest) return ValueTask.FromResult(retry.Action);
            var budget = new AgentWorkBudget(Configuration.WorkBudget, cancellationToken);
            foreach (AgentAction action in features.Actions) _policy.ValidateAction(features, action);
            // Planning draws from the same checkpointed stream as tie-breaking, before it.
            var random = DeterministicRandom.Restore(_checkpoint.RandomState);
            PlanningContext? planning = observation.Determinizer is { } determinizer && determinizer.Viewer == ControlledSide
                ? new PlanningContext(determinizer, random) : null;
            ImmutableArray<AgentCandidate> candidates = _policy.EvaluateDecision(features, _checkpoint.Knowledge,
                _checkpoint.Objective, Configuration, _catalog, budget, planning);
            if (candidates.IsDefault || candidates.Length != features.Actions.Length || candidates.Any(candidate => candidate?.Utility is null)
                || !candidates.Select(candidate => candidate.ActionId).OrderBy(id => id.Value, StringComparer.Ordinal)
                    .SequenceEqual(features.Actions.Select(action => action.Id).OrderBy(id => id.Value, StringComparer.Ordinal)))
                throw new InvalidOperationException("Policy evaluation must score every issued action exactly once.");
            // Bot plan S4.5: the apprentice re-orders the heuristic's top band (apprentice 1).
            if (Apprentice.Applies(Configuration, features))
            {
                long reordering = ProfileTimings.Start();
                candidates = Apprentice.Reorder(features, candidates, _checkpoint.Knowledge, _catalog,
                    Apprentice.DocumentFor(Configuration.Parameters[Apprentice.ParameterKey]));
                ProfileTimings.Stop("apprentice", reordering);
            }
            candidates = candidates.OrderBy(candidate => candidate.ActionId.Value, StringComparer.Ordinal).ToImmutableArray();
            int priority = candidates.Max(candidate => candidate.Utility.Priority);
            int best = candidates.Where(candidate => candidate.Utility.Priority == priority).Max(candidate => candidate.Utility.Score);
            AgentCandidate[] nearTies = candidates.Where(candidate => candidate.Utility.Priority == priority
                && (long)best - candidate.Utility.Score <= Configuration.NearTieTolerance).ToArray();
            AgentCandidate chosen = nearTies.Length == 1 ? nearTies[0] : nearTies[random.NextInt(nearTies.Length)];
            cancellationToken.ThrowIfCancellationRequested();
            LastEvaluation = _captureDiagnostics
                ? new(digest, ObservationReader.Hash(_checkpoint), candidates, chosen.ActionId, budget.Used, budget.Exhausted) : null;
            _pending = new(decision, digest, chosen.ActionId, _checkpoint with
            {
                RandomState = random.State,
                CommittedDecisionCount = checked(_checkpoint.CommittedDecisionCount + 1),
                Objective = chosen.Utility.Objective,
            });
            return ValueTask.FromResult(chosen.ActionId);
        }
        catch { DiscardDecision(); throw; }
    }

    public void CommitDecision(DecisionId decisionId, long revision, ActionId actionId, long actionSequence)
    {
        var commit = new AgentCommit(decisionId, revision, actionId, actionSequence);
        if (_checkpoint.LastCommit == commit) return;
        if (_pending is not { } pending || pending.Decision.Id != decisionId || pending.Decision.Revision != revision
            || pending.Action != actionId || actionSequence != _checkpoint.ObservationCursor + 1)
            throw new InvalidOperationException("Cannot commit a stale or unevaluated agent choice.");
        _checkpoint = pending.Next with { LastCommit = commit };
        _pending = null;
    }

    public void DiscardDecision() { _pending = null; LastEvaluation = null; }

    private AgentFeatures ReadOwn(Observation observation) => observation.Viewer == ControlledSide
        ? ObservationReader.Read(observation, Configuration.FeatureVersion) : throw new ArgumentException("Observation viewer differs from controlled side.", nameof(observation));

    private void ValidateCheckpoint(OfflineAgentCheckpoint checkpoint)
    {
        if (checkpoint.SchemaVersion != OfflineAgentCheckpoint.CurrentSchema || checkpoint.ConfigurationDigest != Configuration.Digest
            || checkpoint.CommittedDecisionCount < 0 || checkpoint.ObservationCursor < -1
            || checkpoint.CommittedDecisionCount > checkpoint.ObservationCursor + 1
            || (checkpoint.CommittedDecisionCount == 0) != (checkpoint.LastCommit is null)
            || (checkpoint.ObservationCursor == -1) != (checkpoint.ObservationDigest is null)
            || checkpoint.Knowledge is null || checkpoint.Knowledge.VisibleCards.IsDefault
            || checkpoint.Knowledge.Reveals.IsDefault || checkpoint.Knowledge.History.IsDefault
            || checkpoint.Knowledge.Reveals.Length > 256 || checkpoint.Knowledge.History.Length > 128
            || (checkpoint.LastCommit is { } commit && (commit.ActionSequence > checkpoint.ObservationCursor + 1 || commit.ActionSequence < 1)))
            throw new ArgumentException("Unsupported or invalid policy checkpoint.", nameof(checkpoint));
    }

    private sealed record PendingEvaluation(AgentDecision Decision, string Digest, ActionId Action, OfflineAgentCheckpoint Next);
}
