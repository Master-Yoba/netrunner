using System.Text.Json.Serialization;

namespace Netrunner.Core;

public enum PlayerSide
{
    Corp,
    Runner,
}

public sealed record GameEvent(
    long Sequence,
    string Kind,
    IReadOnlyDictionary<string, string>? Data = null);

public sealed record Observation(
    PlayerSide Viewer,
    string DecisionId,
    IReadOnlyList<LegalAction> LegalActions,
    IReadOnlyDictionary<string, object?> PublicState,
    IReadOnlyDictionary<string, object?> PrivateState)
{
    /// <summary>
    /// Metadata for a decision owned by this observation's viewer. It is
    /// attached by <see cref="ObservationBuilder"/> but intentionally omitted
    /// from the serialized observation contract so existing agent/replay
    /// payloads keep their stable shape. Opponent-owned prompts and revisions
    /// are never exposed through this property.
    /// </summary>
    [JsonIgnore]
    public ObservationDecision? Decision { get; init; }
}

/// <summary>Viewer-owned metadata for the currently issued decision.</summary>
public sealed record ObservationDecision(
    DecisionId Id,
    long Revision,
    PlayerId Owner,
    PlayerSide OwnerSide,
    DecisionKind Kind,
    string Prompt);

public interface IGameAgent
{
    ValueTask<ActionId> ChooseActionAsync(
        Observation observation,
        CancellationToken cancellationToken = default);
}
