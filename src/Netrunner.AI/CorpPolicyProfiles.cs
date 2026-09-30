using Netrunner.Core;

namespace Netrunner.AI;

public static class CorpPolicyProfiles
{
    private static readonly string[] ParameterKeys = ["credit-scale", "protection-scale", "draw-scale"];
    private static readonly string[] V3ParameterKeys = ["credit-scale", "protection-scale", "draw-scale", "coverage-scale"];
    private static readonly string[] V5ParameterKeys = [.. V3ParameterKeys, "plan-samples", "plan-width"];
    private static readonly string[] V9ParameterKeys = [.. V5ParameterKeys, "plan-evaluation"];
    private static readonly string[] V12ParameterKeys = [.. V9ParameterKeys, "archetype-primary", "archetype-secondary"];
    private static readonly string[] V10ParameterKeys = [.. V9ParameterKeys, "run-samples", "run-width"];

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
        // A3 on A4a and A5: V8 choices, planned, with rollouts scored by the fitted evaluation.
        (CorpHeuristicPolicy.V9, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: 0,
            parameters: [.. Parameters(100, 100, 100), new("coverage-scale", 100), .. Planning(PlanSamples, PlanWidth), new("plan-evaluation", 1)]),
        (CorpHeuristicPolicy.V9, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 12000, nearTieTolerance: EasyNearTieTolerance,
            parameters: [.. Parameters(90, 80, 100), new("coverage-scale", 0), .. Planning(0, 0), new("plan-evaluation", 1)]),
        // V9 with the playtest repairs (V11), planning with the refitted evaluation (feature set 2).
        (CorpHeuristicPolicy.V11, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: 0,
            parameters: [.. Parameters(100, 100, 100), new("coverage-scale", 100), .. Planning(PlanSamples, PlanWidth), new("plan-evaluation", 2)]),
        (CorpHeuristicPolicy.V11, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 12000, nearTieTolerance: EasyNearTieTolerance,
            parameters: [.. Parameters(90, 80, 100), new("coverage-scale", 0), .. Planning(0, 0), new("plan-evaluation", 2)]),
        // A8: V11 playing to its deck's archetype, set per match by WithArchetypes (generic until then).
        (CorpHeuristicPolicy.V12 or CorpHeuristicPolicy.V13 or CorpHeuristicPolicy.V14 or CorpHeuristicPolicy.V15 or CorpHeuristicPolicy.V16 or CorpHeuristicPolicy.V17, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: 0,
            parameters: [.. Parameters(100, 100, 100), new("coverage-scale", 100), .. Planning(PlanSamples, PlanWidth), new("plan-evaluation", 2),
                new("archetype-primary", 0), new("archetype-secondary", 0)]),
        (CorpHeuristicPolicy.V12 or CorpHeuristicPolicy.V13 or CorpHeuristicPolicy.V14 or CorpHeuristicPolicy.V15 or CorpHeuristicPolicy.V16 or CorpHeuristicPolicy.V17, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 12000, nearTieTolerance: EasyNearTieTolerance,
            parameters: [.. Parameters(90, 80, 100), new("coverage-scale", 0), .. Planning(0, 0), new("plan-evaluation", 2),
                new("archetype-primary", 0), new("archetype-secondary", 0)]),
        // A4: V8 choices with rez search, scored by the fitted evaluation; turn actions aren't planned.
        (CorpHeuristicPolicy.V10, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: 0,
            parameters: [.. Parameters(100, 100, 100), new("coverage-scale", 100), .. Planning(0, 0), new("plan-evaluation", 1),
                new("run-samples", RunnerPolicyProfiles.RunSamples), new("run-width", 2)]),
        (CorpHeuristicPolicy.V10, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Corp, policyVersion, profileId, policySeed,
            workBudget: 12000, nearTieTolerance: EasyNearTieTolerance,
            parameters: [.. Parameters(90, 80, 100), new("coverage-scale", 0), .. Planning(0, 0), new("plan-evaluation", 1),
                new("run-samples", 0), new("run-width", 0)]),
        (CorpHeuristicPolicy.V2, _) => CreateV2(profileId, policySeed),
        _ => throw new ArgumentException("Unknown Corp profile or policy version.", nameof(profileId)),
    };

    public const int EasyNearTieTolerance = 150;

    /// <summary>Whether a version plays to a deck archetype (A8).</summary>
    public static bool SupportsArchetypes(string policyVersion) => policyVersion is CorpHeuristicPolicy.V12 or CorpHeuristicPolicy.V13 or CorpHeuristicPolicy.V14 or CorpHeuristicPolicy.V15 or CorpHeuristicPolicy.V16 or CorpHeuristicPolicy.V17;

    /// <summary>Sets the deck's archetypes on a configuration of a version that supports them.</summary>
    public static OfflineAgentConfiguration WithArchetypes(OfflineAgentConfiguration configuration, CorpArchetype primary, CorpArchetype secondary)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!SupportsArchetypes(configuration.PolicyVersion)) return configuration;
        OfflineAgentConfiguration updated = OfflineAgentConfiguration.Create(configuration.Side, configuration.PolicyVersion, configuration.ProfileId,
            configuration.PolicySeed, configuration.WorkBudget, configuration.NearTieTolerance,
            [.. configuration.Parameters.Where(pair => pair.Key is not ("archetype-primary" or "archetype-secondary")),
                new("archetype-primary", (int)primary), new("archetype-secondary", (int)secondary)],
            configuration.FeatureVersion);
        Validate(updated);
        return updated;
    }
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
            CorpHeuristicPolicy.V9 or CorpHeuristicPolicy.V11 => V9ParameterKeys,
            CorpHeuristicPolicy.V10 => V10ParameterKeys,
            CorpHeuristicPolicy.V12 or CorpHeuristicPolicy.V13 or CorpHeuristicPolicy.V14 or CorpHeuristicPolicy.V15 or CorpHeuristicPolicy.V16 or CorpHeuristicPolicy.V17 => V12ParameterKeys,
            _ => null,
        };
        if (configuration.Side != PlayerSide.Corp || keys is null
            || configuration.ProfileId is not ("standard" or "easy") || configuration.Parameters.Count != keys.Length
            || !keys.All(configuration.Parameters.ContainsKey)
            || configuration.Parameters.Any(pair => pair.Key switch
            {
                "coverage-scale" => pair.Value is < 0 or > 200,
                "plan-samples" or "plan-width" or "run-samples" or "run-width" => pair.Value is < 0 or > 16,
                "plan-evaluation" => pair.Value is < 0 or > 2,
                "archetype-primary" or "archetype-secondary" => !Enum.IsDefined((CorpArchetype)pair.Value),
                _ => pair.Value is < 25 or > 200,
            })
            || configuration.FeatureVersion != AgentFeatures.Version)
            throw new ArgumentException("Unsupported Corp profile parameters.", nameof(configuration));
    }

    private static KeyValuePair<string, int>[] Parameters(int credit, int protection, int draw) =>
        [new("credit-scale", credit), new("protection-scale", protection), new("draw-scale", draw)];
}
