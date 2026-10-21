namespace Netrunner.Core;

/// <summary>
/// Optional observation/transaction lifecycle. Hosts call Observe initially and
/// after accepted actions, CommitDecision only for this agent's accepted choice,
/// and DiscardDecision on cancellation/rejection. No authoritative state crosses
/// this boundary. Existing stateless IGameAgent implementations need not opt in.
/// </summary>
public interface IAgentLifecycle : IGameAgent
{
    PlayerSide ControlledSide { get; }

    void Observe(Observation observation, long actionSequence);

    void CommitDecision(DecisionId decisionId, long revision, ActionId actionId, long actionSequence);

    void DiscardDecision();
}
