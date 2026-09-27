using Netrunner.Core;

namespace Netrunner.AI;

public static class RunnerPolicyProfiles
{
    private static readonly string[] ParameterKeys = ["credit-scale", "risk-scale", "draw-scale"];
    private static readonly string[] V2ParameterKeys = ["credit-scale", "risk-scale", "draw-scale", "inference-scale"];
    private static readonly string[] V3ParameterKeys = [.. V2ParameterKeys, "plan-samples", "plan-width"];

    /// <summary>Creates a profile for the latest policy version.</summary>
    public static OfflineAgentConfiguration Create(string profileId, ulong policySeed) =>
        Create(profileId, policySeed, RunnerHeuristicPolicy.PolicyVersion);

    public static OfflineAgentConfiguration Create(string profileId, ulong policySeed, string policyVersion) => (policyVersion, profileId) switch
    {
        (RunnerHeuristicPolicy.V1, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 40000, nearTieTolerance: 0, parameters: Parameters(100, 100, 100)),
        (RunnerHeuristicPolicy.V1, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: 15, parameters: Parameters(90, 120, 100)),
        (RunnerHeuristicPolicy.V2, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 40000, nearTieTolerance: 0, parameters: [.. Parameters(100, 100, 100), new("inference-scale", 100)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        // Easy (A6a): a wide near-tie band picks among clearly different
        // options, and it ignores the public advancement and density cues.
        (RunnerHeuristicPolicy.V2, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: EasyNearTieTolerance, parameters: [.. Parameters(90, 120, 100), new("inference-scale", 0)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        // A3: Standard plans turn actions; Easy keeps V2 choices (compute tiers are A6).
        (RunnerHeuristicPolicy.V3, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 40000, nearTieTolerance: 0,
            parameters: [.. Parameters(100, 100, 100), new("inference-scale", 100), .. CorpPolicyProfiles.Planning(PlanSamples, PlanWidth)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        (RunnerHeuristicPolicy.V3, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: EasyNearTieTolerance,
            parameters: [.. Parameters(90, 120, 100), new("inference-scale", 0), .. CorpPolicyProfiles.Planning(0, 0)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        _ => throw new ArgumentException("Unknown Runner profile or policy version.", nameof(profileId)),
    };

    public const int EasyNearTieTolerance = 150;
    public const int PlanSamples = 3;
    public const int PlanWidth = 3;

    public static void Validate(OfflineAgentConfiguration configuration)
    {
        configuration.Validate();
        string[]? keys = configuration.PolicyVersion switch
        {
            RunnerHeuristicPolicy.V1 => ParameterKeys,
            RunnerHeuristicPolicy.V2 => V2ParameterKeys,
            RunnerHeuristicPolicy.V3 => V3ParameterKeys,
            _ => null,
        };
        if (configuration.Side != PlayerSide.Runner || keys is null
            || configuration.ProfileId is not ("standard" or "easy") || configuration.Parameters.Count != keys.Length
            || !keys.All(configuration.Parameters.ContainsKey)
            || configuration.Parameters.Any(pair => pair.Key switch
            {
                "inference-scale" => pair.Value is < 0 or > 200,
                "plan-samples" or "plan-width" => pair.Value is < 0 or > 16,
                _ => pair.Value is < 25 or > 200,
            })
            || configuration.FeatureVersion != (configuration.PolicyVersion == RunnerHeuristicPolicy.V1
                ? AgentFeatures.Version : AgentFeatures.VersionWithHiddenAdvancements))
            throw new ArgumentException("Unsupported Runner profile parameters.", nameof(configuration));
    }

    private static KeyValuePair<string, int>[] Parameters(int credit, int protection, int draw) =>
        [new("credit-scale", credit), new("risk-scale", protection), new("draw-scale", draw)];
}
