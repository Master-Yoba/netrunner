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
}

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
        _checkpoint = _checkpoint with
        {
            ObservationCursor = actionSequence,
            ObservationDigest = digest,
            Knowledge = _checkpoint.Knowledge.Observe(features, actionSequence),
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
            ImmutableArray<AgentCandidate> candidates = _policy.EvaluateDecision(features, _checkpoint.Knowledge,
                _checkpoint.Objective, Configuration, _catalog, budget);
            if (candidates.IsDefault || candidates.Length != features.Actions.Length || candidates.Any(candidate => candidate?.Utility is null)
                || !candidates.Select(candidate => candidate.ActionId).OrderBy(id => id.Value, StringComparer.Ordinal)
                    .SequenceEqual(features.Actions.Select(action => action.Id).OrderBy(id => id.Value, StringComparer.Ordinal)))
                throw new InvalidOperationException("Policy evaluation must score every issued action exactly once.");
            candidates = candidates.OrderBy(candidate => candidate.ActionId.Value, StringComparer.Ordinal).ToImmutableArray();
            int priority = candidates.Max(candidate => candidate.Utility.Priority);
            int best = candidates.Where(candidate => candidate.Utility.Priority == priority).Max(candidate => candidate.Utility.Score);
            AgentCandidate[] nearTies = candidates.Where(candidate => candidate.Utility.Priority == priority
                && (long)best - candidate.Utility.Score <= Configuration.NearTieTolerance).ToArray();
            var random = DeterministicRandom.Restore(_checkpoint.RandomState);
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
