using Netrunner.Core;

namespace Netrunner.AI;

/// <summary>
/// Creates the offline policy a configuration names. Every supported version
/// stays constructible, so frozen benchmarks and older saves keep their exact
/// behavior while new games use the latest version.
/// </summary>
public static class OfflinePolicies
{
    public static bool IsSupported(PlayerSide side, string policyVersion) => side == PlayerSide.Corp
        ? CorpHeuristicPolicy.Versions.Contains(policyVersion)
        : RunnerHeuristicPolicy.Versions.Contains(policyVersion);

    public static void Validate(OfflineAgentConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Side == PlayerSide.Corp) CorpPolicyProfiles.Validate(configuration);
        else RunnerPolicyProfiles.Validate(configuration);
    }

    public static IOfflinePolicy Create(OfflineAgentConfiguration configuration)
    {
        Validate(configuration);
        return configuration.Side == PlayerSide.Corp
            ? new CorpHeuristicPolicy(configuration.PolicyVersion)
            : new RunnerHeuristicPolicy(configuration.PolicyVersion);
    }

    /// <summary>The latest version for a side that never plans.</summary>
    public static string LatestHeuristic(PlayerSide side) =>
        side == PlayerSide.Corp ? CorpHeuristicPolicy.LatestHeuristic : RunnerHeuristicPolicy.LatestHeuristic;

    /// <summary>Creates the configuration for a profile of a given side and version.</summary>
    public static OfflineAgentConfiguration Configure(PlayerSide side, string profileId, ulong policySeed, string? policyVersion = null) =>
        side == PlayerSide.Corp
            ? CorpPolicyProfiles.Create(profileId, policySeed, policyVersion ?? CorpHeuristicPolicy.PolicyVersion)
            : RunnerPolicyProfiles.Create(profileId, policySeed, policyVersion ?? RunnerHeuristicPolicy.PolicyVersion);
}
