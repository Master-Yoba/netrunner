using Netrunner.AI;
using Netrunner.Cards;
using Netrunner.Core;

namespace Netrunner.Match;

/// <summary>Player-facing offline opponent selection; contract proofs retain their explicit factory.</summary>
public static class OfflineMatchFactory
{
    public static IGameAgent CreateTraceOpponent(FullMatchDefinition definition, PlayerSide humanSide, string traceDirectory,
        long? throughActionSequence = null)
    {
        SessionRecoveryResult recovered = SessionTrace.Recover(traceDirectory, definition.Behavior, definition.Runtime, throughActionSequence);
        if (recovered.Policy is { } policy) return policy.Restore(definition.Behavior, definition.Runtime);
        return new FullGameContractAgent(humanSide == PlayerSide.Corp ? PlayerSide.Runner : PlayerSide.Corp, definition.Runtime);
    }

    /// <summary>Creates the opponent; <paramref name="policyVersion"/> defaults to the latest version for that seat.</summary>
    public static OfflineAgent CreateOpponent(FullMatchDefinition definition, PlayerSide humanSide, string difficulty = "standard",
        string? policyVersion = null)
    {
        PlayerSide side = humanSide switch
        {
            PlayerSide.Corp => PlayerSide.Runner,
            PlayerSide.Runner => PlayerSide.Corp,
            _ => throw new ArgumentOutOfRangeException(nameof(humanSide)),
        };
        ulong seed = AgentLifecycleHost.DerivePolicySeed(definition.Request.Seed, side);
        OfflineAgentConfiguration configuration = OfflinePolicies.Configure(side, difficulty, seed, policyVersion);
        return new(configuration, OfflinePolicies.Create(configuration),
            new GatewayEvaluationCatalog(definition.Runtime, definition.Behavior));
    }
}
