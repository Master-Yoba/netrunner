using Netrunner.Core;

namespace Netrunner.AI;

public static class RunnerPolicyProfiles
{
    private static readonly string[] ParameterKeys = ["credit-scale", "risk-scale", "draw-scale"];
    private static readonly string[] V2ParameterKeys = ["credit-scale", "risk-scale", "draw-scale", "inference-scale"];
    private static readonly string[] V3ParameterKeys = [.. V2ParameterKeys, "plan-samples", "plan-width"];
    private static readonly string[] V8ParameterKeys = [.. V3ParameterKeys, "plan-evaluation"];
    private static readonly string[] V11ParameterKeys = [.. V2ParameterKeys, "archetype-primary", "archetype-secondary"];
    private static readonly string[] V9ParameterKeys = [.. V8ParameterKeys, "run-samples", "run-width"];

    /// <summary>Creates a profile for the latest policy version.</summary>
    public static OfflineAgentConfiguration Create(string profileId, ulong policySeed) =>
        Create(profileId, policySeed, RunnerHeuristicPolicy.PolicyVersion);

    public static OfflineAgentConfiguration Create(string profileId, ulong policySeed, string policyVersion) => (policyVersion, profileId) switch
    {
        (RunnerHeuristicPolicy.V1, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 40000, nearTieTolerance: 0, parameters: Parameters(100, 100, 100)),
        (RunnerHeuristicPolicy.V1, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: 15, parameters: Parameters(90, 120, 100)),
        (RunnerHeuristicPolicy.V2 or RunnerHeuristicPolicy.V4 or RunnerHeuristicPolicy.V5 or RunnerHeuristicPolicy.V6 or RunnerHeuristicPolicy.V7 or RunnerHeuristicPolicy.V10 or RunnerHeuristicPolicy.V12 or RunnerHeuristicPolicy.V13 or RunnerHeuristicPolicy.V14 or RunnerHeuristicPolicy.V15 or RunnerHeuristicPolicy.V16 or RunnerHeuristicPolicy.V17 or RunnerHeuristicPolicy.V18 or RunnerHeuristicPolicy.V19 or RunnerHeuristicPolicy.V20 or RunnerHeuristicPolicy.V21 or RunnerHeuristicPolicy.V22 or RunnerHeuristicPolicy.V23 or RunnerHeuristicPolicy.V24 or RunnerHeuristicPolicy.V25 or RunnerHeuristicPolicy.V26 or RunnerHeuristicPolicy.V27 or RunnerHeuristicPolicy.V28 or RunnerHeuristicPolicy.V30 or RunnerHeuristicPolicy.V32 or RunnerHeuristicPolicy.V35 or RunnerHeuristicPolicy.V37 or RunnerHeuristicPolicy.V38, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 40000, nearTieTolerance: 0, parameters: [.. Parameters(100, 100, 100), new("inference-scale", 100)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        // Easy (A6a): a wide near-tie band picks among clearly different
        // options, and it ignores the public advancement and density cues.
        (RunnerHeuristicPolicy.V2 or RunnerHeuristicPolicy.V4 or RunnerHeuristicPolicy.V5 or RunnerHeuristicPolicy.V6 or RunnerHeuristicPolicy.V7 or RunnerHeuristicPolicy.V10 or RunnerHeuristicPolicy.V12 or RunnerHeuristicPolicy.V13 or RunnerHeuristicPolicy.V14 or RunnerHeuristicPolicy.V15 or RunnerHeuristicPolicy.V16 or RunnerHeuristicPolicy.V17 or RunnerHeuristicPolicy.V18 or RunnerHeuristicPolicy.V19 or RunnerHeuristicPolicy.V20 or RunnerHeuristicPolicy.V21 or RunnerHeuristicPolicy.V22 or RunnerHeuristicPolicy.V23 or RunnerHeuristicPolicy.V24 or RunnerHeuristicPolicy.V25 or RunnerHeuristicPolicy.V26 or RunnerHeuristicPolicy.V27 or RunnerHeuristicPolicy.V28 or RunnerHeuristicPolicy.V30 or RunnerHeuristicPolicy.V32 or RunnerHeuristicPolicy.V35 or RunnerHeuristicPolicy.V37 or RunnerHeuristicPolicy.V38, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: EasyNearTieTolerance, parameters: [.. Parameters(90, 120, 100), new("inference-scale", 0)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        // A6 Hard: the same choices with the turn planner, scored by the corrected fitted evaluation (set 3).
        (RunnerHeuristicPolicy.V18 or RunnerHeuristicPolicy.V19 or RunnerHeuristicPolicy.V20 or RunnerHeuristicPolicy.V21 or RunnerHeuristicPolicy.V22 or RunnerHeuristicPolicy.V23 or RunnerHeuristicPolicy.V24 or RunnerHeuristicPolicy.V25 or RunnerHeuristicPolicy.V26 or RunnerHeuristicPolicy.V27 or RunnerHeuristicPolicy.V28 or RunnerHeuristicPolicy.V30 or RunnerHeuristicPolicy.V32 or RunnerHeuristicPolicy.V35 or RunnerHeuristicPolicy.V37 or RunnerHeuristicPolicy.V38, "hard") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 40000, nearTieTolerance: 0,
            parameters: [.. Parameters(100, 100, 100), new("inference-scale", 100), .. CorpPolicyProfiles.Planning(HardPlanSamples, HardPlanWidth),
                new("plan-evaluation", 3)],
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
        // A3 on A4a and A5: V7 choices, planned, with rollouts scored by the fitted evaluation.
        (RunnerHeuristicPolicy.V8, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 40000, nearTieTolerance: 0,
            parameters: [.. Parameters(100, 100, 100), new("inference-scale", 100), .. CorpPolicyProfiles.Planning(PlanSamples, PlanWidth), new("plan-evaluation", 1)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        (RunnerHeuristicPolicy.V8, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: EasyNearTieTolerance,
            parameters: [.. Parameters(90, 120, 100), new("inference-scale", 0), .. CorpPolicyProfiles.Planning(0, 0), new("plan-evaluation", 1)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        // A4: V7 choices with run search, scored by the fitted evaluation; turn actions aren't planned.
        (RunnerHeuristicPolicy.V9, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 40000, nearTieTolerance: 0,
            parameters: [.. Parameters(100, 100, 100), new("inference-scale", 100), .. CorpPolicyProfiles.Planning(0, 0), new("plan-evaluation", 1),
                new("run-samples", RunSamples), new("run-width", RunWidth)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        (RunnerHeuristicPolicy.V9, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: EasyNearTieTolerance,
            parameters: [.. Parameters(90, 120, 100), new("inference-scale", 0), .. CorpPolicyProfiles.Planning(0, 0), new("plan-evaluation", 1),
                new("run-samples", 0), new("run-width", 0)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        // A8: V10 playing to its deck's archetype, set per match by WithArchetypes (generic until then).
        (RunnerHeuristicPolicy.V11, "standard") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 40000, nearTieTolerance: 0,
            parameters: [.. Parameters(100, 100, 100), new("inference-scale", 100), new("archetype-primary", 0), new("archetype-secondary", 0)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        (RunnerHeuristicPolicy.V11, "easy") => OfflineAgentConfiguration.Create(PlayerSide.Runner, policyVersion, profileId, policySeed,
            workBudget: 20000, nearTieTolerance: EasyNearTieTolerance,
            parameters: [.. Parameters(90, 120, 100), new("inference-scale", 0), new("archetype-primary", 0), new("archetype-secondary", 0)],
            featureVersion: AgentFeatures.VersionWithHiddenAdvancements),
        _ => throw new ArgumentException("Unknown Runner profile or policy version.", nameof(profileId)),
    };

    public const int EasyNearTieTolerance = 150;

    /// <summary>Whether a version plays to a deck archetype (A8).</summary>
    public static bool SupportsArchetypes(string policyVersion) => policyVersion == RunnerHeuristicPolicy.V11;

    /// <summary>Sets the deck's archetypes on a configuration of a version that supports them.</summary>
    public static OfflineAgentConfiguration WithArchetypes(OfflineAgentConfiguration configuration, RunnerArchetype primary, RunnerArchetype secondary)
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
    public const int RunSamples = 4;
    public const int RunWidth = 3;
    public const int HardPlanSamples = 4;
    public const int HardPlanWidth = 3;

    /// <summary>Whether a version has the A6 tiers: the Hard profile (which plans) and personalities.</summary>
    public static bool SupportsTiers(string policyVersion) => policyVersion is RunnerHeuristicPolicy.V18 or RunnerHeuristicPolicy.V19 or RunnerHeuristicPolicy.V20 or RunnerHeuristicPolicy.V21 or RunnerHeuristicPolicy.V22 or RunnerHeuristicPolicy.V23 or RunnerHeuristicPolicy.V24 or RunnerHeuristicPolicy.V25 or RunnerHeuristicPolicy.V26 or RunnerHeuristicPolicy.V27 or RunnerHeuristicPolicy.V28 or RunnerHeuristicPolicy.V30 or RunnerHeuristicPolicy.V32 or RunnerHeuristicPolicy.V35 or RunnerHeuristicPolicy.V37 or RunnerHeuristicPolicy.V38;

    /// <summary>Sets a Runner personality (A6) on a configuration of a version that has them; Balanced removes it.</summary>
    public static OfflineAgentConfiguration WithPersonality(OfflineAgentConfiguration configuration, AgentPersonality personality)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!SupportsTiers(configuration.PolicyVersion) || !AgentPersonalities.IsValid(PlayerSide.Runner, (int)personality))
            throw new ArgumentException("The Runner version or personality doesn't support personalities.", nameof(personality));
        OfflineAgentConfiguration updated = OfflineAgentConfiguration.Create(configuration.Side, configuration.PolicyVersion, configuration.ProfileId,
            configuration.PolicySeed, configuration.WorkBudget, configuration.NearTieTolerance,
            [.. configuration.Parameters.Where(pair => pair.Key != AgentPersonalities.ParameterKey),
                .. personality == AgentPersonality.Balanced ? [] : new KeyValuePair<string, int>[] { new(AgentPersonalities.ParameterKey, (int)personality) }],
            configuration.FeatureVersion);
        Validate(updated);
        return updated;
    }

    public static void Validate(OfflineAgentConfiguration configuration)
    {
        configuration.Validate();
        string[]? keys = configuration.PolicyVersion switch
        {
            RunnerHeuristicPolicy.V1 => ParameterKeys,
            RunnerHeuristicPolicy.V18 or RunnerHeuristicPolicy.V19 or RunnerHeuristicPolicy.V20 or RunnerHeuristicPolicy.V21 or RunnerHeuristicPolicy.V22 or RunnerHeuristicPolicy.V23 or RunnerHeuristicPolicy.V24 or RunnerHeuristicPolicy.V25 or RunnerHeuristicPolicy.V26 or RunnerHeuristicPolicy.V27 or RunnerHeuristicPolicy.V28 or RunnerHeuristicPolicy.V30 or RunnerHeuristicPolicy.V32 or RunnerHeuristicPolicy.V35 or RunnerHeuristicPolicy.V37 or RunnerHeuristicPolicy.V38 when configuration.ProfileId == "hard" => V8ParameterKeys,
            RunnerHeuristicPolicy.V2 or RunnerHeuristicPolicy.V4 or RunnerHeuristicPolicy.V5 or RunnerHeuristicPolicy.V6 or RunnerHeuristicPolicy.V7 or RunnerHeuristicPolicy.V10 or RunnerHeuristicPolicy.V12 or RunnerHeuristicPolicy.V13 or RunnerHeuristicPolicy.V14 or RunnerHeuristicPolicy.V15 or RunnerHeuristicPolicy.V16 or RunnerHeuristicPolicy.V17 or RunnerHeuristicPolicy.V18 or RunnerHeuristicPolicy.V19 or RunnerHeuristicPolicy.V20 or RunnerHeuristicPolicy.V21 or RunnerHeuristicPolicy.V22 or RunnerHeuristicPolicy.V23 or RunnerHeuristicPolicy.V24 or RunnerHeuristicPolicy.V25 or RunnerHeuristicPolicy.V26 or RunnerHeuristicPolicy.V27 or RunnerHeuristicPolicy.V28 or RunnerHeuristicPolicy.V30 or RunnerHeuristicPolicy.V32 or RunnerHeuristicPolicy.V35 or RunnerHeuristicPolicy.V37 or RunnerHeuristicPolicy.V38 => V2ParameterKeys,
            RunnerHeuristicPolicy.V3 => V3ParameterKeys,
            RunnerHeuristicPolicy.V8 => V8ParameterKeys,
            RunnerHeuristicPolicy.V9 => V9ParameterKeys,
            RunnerHeuristicPolicy.V11 => V11ParameterKeys,
            _ => null,
        };
        bool tiers = SupportsTiers(configuration.PolicyVersion);
        bool hard = tiers && configuration.ProfileId == "hard";
        bool personality = configuration.Parameters.ContainsKey(AgentPersonalities.ParameterKey);
        int tuning = hard ? configuration.Parameters.Keys.Count(key => CorpPolicyProfiles.HardTuningKeys.Contains(key) && !keys!.Contains(key)) : 0;
        // Bot plan S1.5: the turn-line search is an optional switch of a tiered profile.
        bool lines = tiers && configuration.Parameters.ContainsKey("plan-lines");
        // Bot plan S4.5: the apprentice is an optional switch of a tiered profile.
        bool apprentice = tiers && configuration.Parameters.ContainsKey(Apprentice.ParameterKey);
        if (configuration.Side != PlayerSide.Runner || keys is null
            || !(configuration.ProfileId is "standard" or "easy" || hard)
            || personality && !tiers
            || configuration.Parameters.Count != keys.Length + (personality ? 1 : 0) + (lines ? 1 : 0) + (apprentice ? 1 : 0) + tuning
            || !keys.All(configuration.Parameters.ContainsKey)
            || configuration.Parameters.Any(pair => pair.Key switch
            {
                "inference-scale" => pair.Value is < 0 or > 200,
                // Bot plan S0.3: the budget-scaling entrants plan up to 16 times Hard's samples.
                "plan-samples" => pair.Value is < 0 or > 64,
                "plan-width" or "run-samples" or "run-width" => pair.Value is < 0 or > 16,
                "plan-weight" => pair.Value is < 25 or > 400,
                "plan-model" => pair.Value is < 0 or > 1,
                "plan-tracker" => pair.Value is < 0 or > 3,
                "plan-lines" => pair.Value is < 0 or > 1,
                Apprentice.ParameterKey => pair.Value is < 0 or > Apprentice.Latest,
                "plan-value" => pair.Value is < 0 or > 1,
                "plan-tie" => pair.Value is < 0 or > 100,
                "archetype-primary" or "archetype-secondary" => !Enum.IsDefined((RunnerArchetype)pair.Value),
                "plan-evaluation" => pair.Value is < 0 || pair.Value > (hard ? 8 : 1),
                AgentPersonalities.ParameterKey => pair.Value == 0 || !AgentPersonalities.IsValid(PlayerSide.Runner, pair.Value),
                _ => pair.Value is < 25 or > 200,
            })
            || configuration.FeatureVersion != (configuration.PolicyVersion == RunnerHeuristicPolicy.V1
                ? AgentFeatures.Version : AgentFeatures.VersionWithHiddenAdvancements))
            throw new ArgumentException("Unsupported Runner profile parameters.", nameof(configuration));
    }

    private static KeyValuePair<string, int>[] Parameters(int credit, int protection, int draw) =>
        [new("credit-scale", credit), new("risk-scale", protection), new("draw-scale", draw)];
}
