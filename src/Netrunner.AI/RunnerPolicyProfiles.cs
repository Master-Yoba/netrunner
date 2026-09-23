using Netrunner.Core;

namespace Netrunner.AI;

public static class RunnerPolicyProfiles
{
    private static readonly string[] ParameterKeys = ["credit-scale", "risk-scale", "draw-scale"];
    public static OfflineAgentConfiguration Create(string profileId, ulong policySeed) => profileId switch
    {
        "standard" => OfflineAgentConfiguration.Create(PlayerSide.Runner, RunnerHeuristicPolicy.PolicyVersion, profileId, policySeed,
            workBudget: 40000, nearTieTolerance: 0, parameters: Parameters(100, 100, 100)),
        "easy" => OfflineAgentConfiguration.Create(PlayerSide.Runner, RunnerHeuristicPolicy.PolicyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: 15, parameters: Parameters(90, 120, 100)),
        _ => throw new ArgumentException("Unknown Runner profile.", nameof(profileId)),
    };

    public static void Validate(OfflineAgentConfiguration configuration)
    {
        configuration.Validate();
        if (configuration.Side != PlayerSide.Runner || configuration.PolicyVersion != RunnerHeuristicPolicy.PolicyVersion
            || configuration.ProfileId is not ("standard" or "easy") || configuration.Parameters.Count != 3
            || !ParameterKeys.All(configuration.Parameters.ContainsKey)
            || configuration.Parameters.Values.Any(value => value is < 25 or > 200))
            throw new ArgumentException("Unsupported Runner profile parameters.", nameof(configuration));
    }

    private static KeyValuePair<string, int>[] Parameters(int credit, int protection, int draw) =>
        [new("credit-scale", credit), new("risk-scale", protection), new("draw-scale", draw)];
}
