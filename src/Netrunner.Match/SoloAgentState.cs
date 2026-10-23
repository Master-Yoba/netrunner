using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;
using Netrunner.Match.Llm;

namespace Netrunner.Match;

/// <summary>Exact installed policy and committed state. Never contains tentative thinking.</summary>
public sealed record SoloAgentState(string Kind, string Version, PlayerSide Side,
    OfflineAgentConfiguration? Configuration, OfflineAgentCheckpoint? Checkpoint, SoloContractPolicy? ContractPolicy)
{
    /// <summary>
    /// The bot was the LLM opponent's (its model isn't saved): a load wraps it again when a model server is set. Omitted
    /// when false, so other saves and their hashes are unchanged.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LlmOpponent { get; init; }

    public static SoloAgentState Capture(IGameAgent agent) => agent switch
    {
        LlmOpponentAgent llm => Capture(llm.Heuristic) with { LlmOpponent = true },
        OfflineAgent offline => new("offline", OfflineAgentConfiguration.CurrentAgentVersion, offline.ControlledSide,
            offline.Configuration, offline.Checkpoint, null),
        FullGameContractAgent contract => new("contract", FullGameContractAgent.Version,
            contract.Policy.ControlledSide, null, null, SoloContractPolicy.From(contract.Policy)),
        _ => throw new InvalidOperationException("This agent has no supported persistent policy contract."),
    };

    public IGameAgent Restore(IBehaviorCatalog behavior, IRuntimeCardCatalog runtime)
    {
        if (!Enum.IsDefined(Side)) throw new InvalidOperationException("Invalid saved agent seat.");
        if (Kind == "contract" && Version == FullGameContractAgent.Version && Configuration is null
            && Checkpoint is null && ContractPolicy is { } policy && policy.ControlledSide == Side)
            return new FullGameContractAgent(policy.Restore(), runtime);
        if (Kind != "offline" || Version != OfflineAgentConfiguration.CurrentAgentVersion || Configuration is null
            || Checkpoint is null || ContractPolicy is not null || Configuration.Side != Side)
            throw new InvalidOperationException("The saved agent version or configuration is unavailable.");
        return new OfflineAgent(Configuration, OfflinePolicies.Create(Configuration),
            new GatewayEvaluationCatalog(runtime, behavior), Checkpoint);
    }

    public void ValidateBoundary(GameState state, IBehaviorCatalog behavior, IRuntimeCardCatalog runtime)
    {
        IGameAgent restored = Restore(behavior, runtime);
        if (restored is not OfflineAgent offline) return;
        string digest = ObservationReader.Digest(ObservationReader.Read(ObservationBuilder.Build(state, Side, behavior),
            offline.Configuration.FeatureVersion));
        if (offline.Checkpoint.ObservationCursor != state.ActionSequence || offline.Checkpoint.ObservationDigest != digest
            || offline.Checkpoint.LastCommit?.ActionSequence > state.ActionSequence)
            throw new InvalidOperationException("The saved policy checkpoint does not match the engine decision boundary.");
    }
}

public sealed record SoloContractPolicy(int SchemaVersion, string PolicyVersion, PlayerSide ControlledSide,
    ImmutableArray<ActionKind> TurnActionOrder, ImmutableArray<ActionKind> RunActionOrder,
    BioroidSubroutinePreference BioroidSubroutinePreference)
{
    public static SoloContractPolicy From(FullGameContractPolicy policy) => new(policy.SchemaVersion, policy.PolicyVersion,
        policy.ControlledSide, policy.TurnActionOrder.ToImmutableArray(), policy.RunActionOrder.ToImmutableArray(),
        policy.BioroidSubroutinePreference);

    public FullGameContractPolicy Restore()
    {
        if (PolicyVersion != FullGameContractPolicy.Version || TurnActionOrder.IsDefault || RunActionOrder.IsDefault)
            throw new InvalidOperationException("Unsupported saved contract policy.");
        return new(ControlledSide, SchemaVersion, TurnActionOrder, RunActionOrder, BioroidSubroutinePreference);
    }
}
