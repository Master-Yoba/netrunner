using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Netrunner.Core;

namespace Netrunner.AI;

public sealed record OfflineAgentConfiguration(
    int SchemaVersion, string AgentVersion, string PolicyVersion, string FeatureVersion,
    PlayerSide Side, string ProfileId, ImmutableSortedDictionary<string, int> Parameters,
    string ParameterHash, ulong PolicySeed, int WorkBudget, int NearTieTolerance)
{
    public const int CurrentSchema = 1;
    public const string CurrentAgentVersion = "offline-agent-v1";

    public static OfflineAgentConfiguration Create(PlayerSide side, string policyVersion, string profileId,
        ulong policySeed, int workBudget = 10000, int nearTieTolerance = 0,
        IEnumerable<KeyValuePair<string, int>>? parameters = null)
    {
        var frozen = (parameters ?? []).ToImmutableSortedDictionary(StringComparer.Ordinal);
        var result = new OfflineAgentConfiguration(CurrentSchema, CurrentAgentVersion, policyVersion, AgentFeatures.Version,
            side, profileId, frozen, ObservationReader.Hash(frozen), policySeed, workBudget, nearTieTolerance);
        result.Validate();
        return result;
    }

    [JsonIgnore]
    public string Digest => ObservationReader.Hash(new
    {
        SchemaVersion,
        AgentVersion,
        PolicyVersion,
        FeatureVersion,
        Side,
        ProfileId,
        Parameters,
        ParameterHash,
        PolicySeed,
        WorkBudget,
        NearTieTolerance,
    });

    public void Validate()
    {
        if (SchemaVersion != CurrentSchema || AgentVersion != CurrentAgentVersion || FeatureVersion != AgentFeatures.Version
            || !Enum.IsDefined(Side) || string.IsNullOrWhiteSpace(PolicyVersion) || string.IsNullOrWhiteSpace(ProfileId)
            || Parameters is null || Parameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key))
            || ParameterHash != ObservationReader.Hash(Parameters) || WorkBudget <= 0 || NearTieTolerance < 0)
            throw new ArgumentException("Unsupported or invalid offline agent configuration.");
    }
}
