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
    IReadOnlyDictionary<string, object?> PrivateState);

public interface IGameAgent
{
    ValueTask<ActionId> ChooseActionAsync(
        Observation observation,
        CancellationToken cancellationToken = default);
}
