using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Netrunner.Core;

namespace Netrunner.AI;

public static class ObservationReader
{
    public static AgentFeatures Read(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (!Enum.IsDefined(observation.Viewer)) throw new ArgumentException("Invalid viewer.", nameof(observation));
        ObservationDecision? decision = observation.Decision;
        if ((decision is null && observation.LegalActions.Count > 0)
            || (decision is not null && (decision.OwnerSide != observation.Viewer
                || decision.Id.Value != observation.DecisionId || !Enum.IsDefined(decision.Kind))))
            throw new ArgumentException("Actions require an explicit viewer-owned decision envelope.", nameof(observation));

        // The same serializer handles native enums/dictionaries and JsonElement
        // values. The allowlisted records discard prose and unrecognized fields.
        AgentBoard board = Decode<AgentBoard>(observation.PublicState);
        AgentPrivateCards cards = Decode<AgentPrivateCards>(observation.PrivateState);
        if (board.Players is null || board.Players.Corp is null || board.Players.Runner is null || board.Turn is null
            || board.Turn.TurnNumber < 0 || !Enum.IsDefined(board.Turn.ActiveSide)
            || !Enum.IsDefined(board.Turn.Phase)) throw new ArgumentException("Malformed board observation.", nameof(observation));
        if (decision is not null && (decision.Revision < 0 || decision.Owner.Value !=
                (observation.Viewer == PlayerSide.Corp ? board.Players.Corp.Id : board.Players.Runner.Id)))
            throw new ArgumentException("Decision owner does not match the viewer.", nameof(observation));
        var actions = observation.LegalActions.Select(action => new AgentAction(action.Id, action.Kind,
            EngineJson.Deserialize<ActionParameters>(EngineJson.Serialize(action.Parameters))))
            .OrderBy(action => action.Id.Value, StringComparer.Ordinal).ToImmutableArray();
        if (actions.Select(action => action.Id).Distinct().Count() != actions.Length
            || actions.Any(action => !Enum.IsDefined(action.Kind)))
            throw new ArgumentException("Malformed legal actions.", nameof(observation));
        return new(observation.Viewer, decision is null ? null : new(decision.Id, decision.Revision, decision.Owner, decision.Kind),
            actions, board, cards);
    }

    public static string Digest(AgentFeatures features) => Hash(features);

    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(CanonicalJson.Serialize(value)))).ToLowerInvariant();

    private static T Decode<T>(IReadOnlyDictionary<string, object?> value)
    {
        JsonSerializerOptions options = EngineJson.Options;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
        return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, options), options)
            ?? throw new ArgumentException("Null observation section.");
    }
}
