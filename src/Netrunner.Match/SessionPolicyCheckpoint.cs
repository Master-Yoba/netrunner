using Netrunner.Core;

namespace Netrunner.Match;

public sealed record SessionPolicyBoundary(long ActionSequence, string EngineStateHash, SoloAgentState Agent);

/// <summary>The v2 trace commit atomically binds accepted engine actions to their policy states.</summary>
public sealed record SessionPolicyCheckpoint(int SchemaVersion, ReplayArtifact Replay, ReplayArtifact SessionReplay,
    IReadOnlyList<SessionPolicyBoundary> Boundaries, string IntegrityHash)
{
    public static SessionPolicyCheckpoint Create(ReplayArtifact replay, ReplayArtifact sessionReplay, IReadOnlyList<SessionPolicyBoundary> boundaries) =>
        new(1, replay, sessionReplay, boundaries.ToArray(), BenchmarkHash.Value(new { replay, sessionReplay, boundaries }));

    public void Validate(SessionTraceManifest manifest, IBehaviorCatalog behavior, IRuntimeCardCatalog runtime)
    {
        if (SchemaVersion != 1 || Replay is null || SessionReplay is null || Boundaries is null || Boundaries.Count != Replay.Actions.Count + 1
            || IntegrityHash != BenchmarkHash.Value(new { replay = Replay, sessionReplay = SessionReplay, boundaries = Boundaries }))
            throw new SessionTraceException("Invalid policy trace checkpoint or checksum.");
        Replay.Validate(behavior, runtime);
        ReplayVerificationResult session = ReplayVerifier.Verify(SessionReplay, behavior, runtime);
        if (!session.Success || session.FinalStateHash != Replay.ExpectedFinalStateHash)
            throw new SessionTraceException("The self-contained session history disagrees with the trace checkpoint.");
        GameState state = Replay.InitialSnapshot.ToState();
        string? configuration = null;
        for (int index = 0; index < Boundaries.Count; index++)
        {
            SessionPolicyBoundary boundary = Boundaries[index];
            if (boundary.ActionSequence != state.ActionSequence || boundary.EngineStateHash != DeterministicHash.ForState(state)
                || boundary.Agent.Side == manifest.HumanSide)
                throw new SessionTraceException("Policy trace checkpoint does not match its accepted engine action.");
            boundary.Agent.ValidateBoundary(state, behavior, runtime);
            string identity = BenchmarkHash.Value(new
            {
                boundary.Agent.Kind,
                boundary.Agent.Version,
                boundary.Agent.Side,
                boundary.Agent.Configuration,
                boundary.Agent.ContractPolicy
            });
            configuration ??= identity;
            if (identity != configuration) throw new SessionTraceException("Policy configuration changes within the trace.");
            if (index > 0 && boundary.Agent.Checkpoint is { } policy)
            {
                SessionPolicyBoundary previous = Boundaries[index - 1];
                ActionSubmission submission = Replay.Actions[index - 1].Submission;
                bool ownAction = state.GetPlayer(submission.Actor).Side == boundary.Agent.Side;
                if (previous.Agent.Checkpoint is not { } oldPolicy
                    || policy.CommittedDecisionCount != oldPolicy.CommittedDecisionCount + (ownAction ? 1 : 0)
                    || ownAction && policy.LastCommit != new Netrunner.AI.AgentCommit(submission.DecisionId,
                        submission.Revision, submission.ActionId, boundary.ActionSequence)
                    || !ownAction && (policy.LastCommit != oldPolicy.LastCommit || policy.RandomState != oldPolicy.RandomState))
                    throw new SessionTraceException("Policy commit cursor disagrees with the accepted action journal.");
            }
            if (index < Replay.Actions.Count)
            {
                ApplyResult applied = GameEngine.Apply(state, Replay.Actions[index].Submission, behavior, runtime);
                if (!applied.Accepted) throw new SessionTraceException("Policy trace replay action was rejected.");
                state = applied.State;
            }
        }
    }
}
