using Netrunner.Core;

namespace Netrunner.AI;

public static class CorpPolicyProfiles
{
    private static readonly string[] ParameterKeys = ["credit-scale", "protection-scale", "draw-scale"];
    private static readonly string[] V3ParameterKeys = ["credit-scale", "protection-scale", "draw-scale", "coverage-scale"];
    private static readonly string[] V5ParameterKeys = [.. V3ParameterKeys, "plan-samples", "plan-width"];

    /// <summary>Creates a profile for the latest policy version.</summary>
    public static OfflineAgentConfiguration Create(string profileId, ulong policySeed) =>
        Create(profileId, policySeed, CorpHeuristicPolicy.PolicyVersion);

    public static OfflineAgentConfiguration Create(string profileId, ulong policySeed, string policyVersion) => (policyVersion, profileId) switch
    {
        (CorpHeuristicPolicy.V3 or CorpHeuristicPolicy.V4 or CorpHeuristicPolicy.V6 or CorpHeuristicPolicy.V7 or CorpHeuristicPolicy.V8, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: 0, parameters: [.. Parameters(100, 100, 100), new("coverage-scale", 100)]),
        // Easy (A6a): a wide near-tie band and no breaker-coverage reasoning.
        (CorpHeuristicPolicy.V3 or CorpHeuristicPolicy.V4 or CorpHeuristicPolicy.V6 or CorpHeuristicPolicy.V7 or CorpHeuristicPolicy.V8, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 12000, nearTieTolerance: EasyNearTieTolerance, parameters: [.. Parameters(90, 80, 100), new("coverage-scale", 0)]),
        // A3: Standard plans turn actions; Easy keeps V4 choices (compute tiers are A6).
        (CorpHeuristicPolicy.V5, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: 0,
            parameters: [.. Parameters(100, 100, 100), new("coverage-scale", 100), .. Planning(PlanSamples, PlanWidth)]),
        (CorpHeuristicPolicy.V5, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 12000, nearTieTolerance: EasyNearTieTolerance,
            parameters: [.. Parameters(90, 80, 100), new("coverage-scale", 0), .. Planning(0, 0)]),
        (CorpHeuristicPolicy.V2, _) => CreateV2(profileId, policySeed),
        _ => throw new ArgumentException("Unknown Corp profile or policy version.", nameof(profileId)),
    };

    public const int EasyNearTieTolerance = 150;
    public const int PlanSamples = 3;
    public const int PlanWidth = 3;

    internal static KeyValuePair<string, int>[] Planning(int samples, int width) =>
        [new("plan-samples", samples), new("plan-width", width)];

    private static OfflineAgentConfiguration CreateV2(string profileId, ulong policySeed) => profileId switch
    {
        "standard" => OfflineAgentConfiguration.Create(PlayerSide.Corp, CorpHeuristicPolicy.V2, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: 0, parameters: Parameters(100, 100, 100)),
        "easy" => OfflineAgentConfiguration.Create(PlayerSide.Corp, CorpHeuristicPolicy.V2, profileId, policySeed,
            workBudget: 12000, nearTieTolerance: 15, parameters: Parameters(90, 80, 100)),
        _ => throw new ArgumentException("Unknown Corp profile.", nameof(profileId)),
    };

    public static void Validate(OfflineAgentConfiguration configuration)
    {
        configuration.Validate();
        string[]? keys = configuration.PolicyVersion switch
        {
            CorpHeuristicPolicy.V2 => ParameterKeys,
            CorpHeuristicPolicy.V3 or CorpHeuristicPolicy.V4 or CorpHeuristicPolicy.V6 or CorpHeuristicPolicy.V7 or CorpHeuristicPolicy.V8 => V3ParameterKeys,
            CorpHeuristicPolicy.V5 => V5ParameterKeys,
            _ => null,
        };
        if (configuration.Side != PlayerSide.Corp || keys is null
            || configuration.ProfileId is not ("standard" or "easy") || configuration.Parameters.Count != keys.Length
            || !keys.All(configuration.Parameters.ContainsKey)
            || configuration.Parameters.Any(pair => pair.Key switch
            {
                "coverage-scale" => pair.Value is < 0 or > 200,
                "plan-samples" or "plan-width" => pair.Value is < 0 or > 16,
                _ => pair.Value is < 25 or > 200,
            })
            || configuration.FeatureVersion != AgentFeatures.Version)
            throw new ArgumentException("Unsupported Corp profile parameters.", nameof(configuration));
    }

    private static KeyValuePair<string, int>[] Parameters(int credit, int protection, int draw) =>
        [new("credit-scale", credit), new("protection-scale", protection), new("draw-scale", draw)];
}
