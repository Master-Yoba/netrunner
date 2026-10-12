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
        // Bot plan S3: one live policy a side; every earlier version is frozen legacy code.
        return (configuration.Side, configuration.PolicyVersion) switch
        {
            (PlayerSide.Corp, CorpHeuristicPolicy.V31 or CorpHeuristicPolicy.V32 or CorpHeuristicPolicy.V33 or CorpHeuristicPolicy.V34 or CorpHeuristicPolicy.V35 or CorpHeuristicPolicy.V36 or CorpHeuristicPolicy.V37) => new CorpHeuristicPolicy(configuration.PolicyVersion),
            (PlayerSide.Corp, _) => new LegacyCorpPolicy(configuration.PolicyVersion),
            (_, RunnerHeuristicPolicy.V27 or RunnerHeuristicPolicy.V28 or RunnerHeuristicPolicy.V29 or RunnerHeuristicPolicy.V30 or RunnerHeuristicPolicy.V31 or RunnerHeuristicPolicy.V32 or RunnerHeuristicPolicy.V33 or RunnerHeuristicPolicy.V34 or RunnerHeuristicPolicy.V35 or RunnerHeuristicPolicy.V36 or RunnerHeuristicPolicy.V37) => new RunnerHeuristicPolicy(configuration.PolicyVersion),
            _ => new LegacyRunnerPolicy(configuration.PolicyVersion),
        };
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
